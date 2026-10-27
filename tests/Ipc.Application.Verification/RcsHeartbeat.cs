using System.Diagnostics;
using System.Text.Json;
using System.Runtime.Loader;
using Ipc.Application.Contracts;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Assembly;
using Ipc.Platform;

static partial class SportmasterRcs
{
    static ParcelConfiguration HeartbeatConfiguration(string workspace, CapabilityBinding binding) =>
        Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json"))) with
        { StationCode = "SYNTH-STATION", Wcs = binding };

    public static async Task HeartbeatChecks(string workspace)
    {
        await using var peer = new Peer();
        var binding = Binding(peer.Endpoint) with { Parameters = new(Binding(peer.Endpoint).Parameters.Where(p => p.Key != "heartbeat-interval-seconds")) };
        var config = HeartbeatConfiguration(workspace, binding);
        await using (var service = new SportmasterRcsTaskService(binding))
        {
            try { await service.StartAsync(default); throw new Exception("Started without runtime context"); }
            catch (InvalidDataException) { Check(peer.Requests.IsEmpty, "missing station/driver configuration refuses start before network I/O"); }
            service.ConfigureRuntime(config);
            var clock = Stopwatch.StartNew();
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.StartAsync(default)));
            await Task.Delay(300);
            Check(peer.Requests.IsEmpty, "repeated start owns one timer and default heartbeat waits for its interval");
            await Until(() => peer.Requests.Count == 1);
            Check(clock.Elapsed >= TimeSpan.FromSeconds(4.7), "omitted component interval defaults to five seconds");
            var exchange = peer.Requests.Single();
            using var json = JsonDocument.Parse(exchange.Body); var envelope = json.RootElement;
            var data = envelope.GetProperty("data").EnumerateArray().ToArray();
            Check(exchange.Method == "POST" && exchange.ContentType.StartsWith("application/json", StringComparison.Ordinal) &&
                envelope.GetProperty("source").GetString() == "BAR" && envelope.GetProperty("version").GetInt32() == 1 && envelope.GetProperty("locale").GetString() == "zh_CN",
                "heartbeat uses the configured RCS endpoint and envelope with numeric version/requestId");
            Check(data.Length == 2 && data.Select((item, i) => item.GetProperty("index").GetInt32() == i + 1 &&
                item.GetProperty("command").GetString() == "sorter.task.station_heartbeat" &&
                item.GetProperty("params").GetProperty("stationCode").GetString() == config.StationCode &&
                item.GetProperty("params").GetProperty("bcrCode").GetString() == config.Wcs!.Parameters[config.Topology!.Positions.Single(p => p.ReaderDeviceId == config.Topology.Readers[i].DeviceId).Id + "-bcr-code"]).All(x => x),
                "one batch uses system StationCode and the same per-camera mapping as destination requests, including synthetic readers");
            var timestamp = data[0].GetProperty("params").GetProperty("timestamp").GetInt64();
            Check(Math.Abs(timestamp - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) < 2000 &&
                data.All(i => i.GetProperty("params").GetProperty("timestamp").GetInt64() == timestamp), "batch timestamp is numeric UTC Unix milliseconds");
            await Healthy(service);
            Check((await service.HealthAsync(default)).Detail.Contains("unconfirmed", StringComparison.Ordinal), "HTTP delivery never fabricates an application acknowledgement");
            try { service.ConfigureRuntime(config); throw new Exception("Reconfigured running timer"); }
            catch (InvalidOperationException) { Check(true, "running component rejects context replacement"); }
            await service.StopAsync(default);
            var count = peer.Requests.Count; await Task.Delay(200);
            Check(peer.Requests.Count == count && !(await service.HealthAsync(default)).Ready, "stop drains the timer and reports stopped");
        }
        binding = binding with { Parameters = new(binding.Parameters) { ["heartbeat-interval-seconds"] = "1" } };
        config = HeartbeatConfiguration(workspace, binding);
        var schema = Wire.Decode<ParameterField[]>(File.ReadAllText(Path.Combine(workspace, "src/Ipc.Integrations.SportmasterRcs/parameters.json")));
        Check(ExtensionSchema.ValidateDefinition(schema).Length == 0 && schema.Single(f => f.Key == "heartbeat-interval-seconds") is { Default: "5", Minimum: 1, Maximum: 3600 } &&
            ExtensionSchema.Validate(schema, binding.Parameters).Length == 0, "component parameter schema exposes a valid five-second default and bounded editable interval");
        await using (var service = new SportmasterRcsTaskService(binding))
        {
            service.ConfigureRuntime(config); peer.Mode = "http-error";
            var count = peer.Requests.Count; await service.StartAsync(default);
            await Until(async () => peer.Requests.Count > count && !(await service.HealthAsync(default)).Ready);
            Check((await service.HealthAsync(default)).Detail.Contains("503", StringComparison.Ordinal), "configured one-second schedule exposes HTTP failures");
            peer.Mode = "assigned"; await Healthy(service);
            var first = peer.Requests.Last().Body;
            peer.Mode = "redirect"; await Until(async () => !(await service.HealthAsync(default)).Ready);
            Check((await service.HealthAsync(default)).Detail.Contains("302", StringComparison.Ordinal), "heartbeat does not follow redirects");
            peer.Mode = "timeout"; await Until(async () => (await service.HealthAsync(default)).Detail.Contains("timeout", StringComparison.Ordinal));
            Check(true, "stalled heartbeat times out and leaves destination outcomes untouched");
            peer.Mode = "assigned"; await service.ReconnectAsync(default); await Healthy(service);
            var last = peer.Requests.Last().Body;
            using var before = JsonDocument.Parse(first); using var after = JsonDocument.Parse(last);
            Check(after.RootElement.GetProperty("requestId").GetInt64() > before.RootElement.GetProperty("requestId").GetInt64(), "reconnect restarts a single loop with fresh increasing heartbeat IDs");
            var original = Request(binding); var prepared = service.PrepareRequest(original, 7);
            var reply = await service.RequestDestinationAsync(original with { IntegrationRequest = prepared }, default);
            Check(prepared.RequestId == 810000007 && reply.BusinessAccepted == true && reply.PhysicalCompleted != true,
                "heartbeat neither consumes durable destination IDs nor changes destination acceptance semantics");
            using (var destination = JsonDocument.Parse(prepared.Body))
            using (var heartbeat = JsonDocument.Parse(peer.Requests.First(r => r.Body.Contains("station_heartbeat", StringComparison.Ordinal)).Body))
                Check(destination.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("bcrCode").GetString() ==
                    heartbeat.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("bcrCode").GetString(),
                    "actual destination and heartbeat HTTP bodies use the same camera code");
            await service.StopAsync(default); count = peer.Requests.Count; await Task.Delay(1300);
            Check(peer.Requests.Count == count, "stopped component performs no later scheduled requests");
        }
        foreach (var invalid in new[] { "0", "-1", "1.5", "3601", "bad" })
        {
            try { await using var service = new SportmasterRcsTaskService(binding with { Parameters = new(binding.Parameters) { ["heartbeat-interval-seconds"] = invalid } }); throw new Exception("Invalid interval accepted"); }
            catch (InvalidDataException) { Check(true, "invalid interval refused: " + invalid); }
        }
        var sharedBinding = binding with { Parameters = new(binding.Parameters) { ["left-bcr-code"] = "SYNTH-NEW-BCR" } };
        config = HeartbeatConfiguration(workspace, sharedBinding);
        var reversed = RcsConfigurationValidation.HeartbeatMapping(config with { Topology = config.Topology! with { Readers = config.Topology.Readers.Reverse().ToArray() } });
        Check(reversed[0].Bcr == sharedBinding.Parameters["right-bcr-code"] && reversed[1].Bcr == sharedBinding.Parameters["left-bcr-code"],
            "camera codes bind by explicit topology position, never reader enumeration order");
        await using (var service = new SportmasterRcsTaskService(sharedBinding))
        {
            var readers = config.Topology!.Readers;
            try { service.ConfigureRuntime(config with { StationCode = null }); throw new Exception("Missing station code accepted"); }
            catch (InvalidDataException) { Check(true, "missing system StationCode refuses configuration"); }
            try { service.ConfigureRuntime(config with { Topology = config.Topology with { Readers = [readers[0], readers[0]] } }); throw new Exception("Duplicate reader code accepted"); }
            catch (InvalidDataException) { Check(true, "duplicate driver codes refuse configuration"); }
            var hardware = readers[0] with { Driver = "extension:synthetic-reader" };
            var instance = new ExtensionInstance("synthetic-reader", "synthetic.reader", "reader", hardware.DeviceId, true, new() { });
            var selected = config with { Wcs = sharedBinding, StationCode = "SYNTH-NEW-STATION", Topology = config.Topology with { Readers = [hardware] }, Extensions = new([], [instance]) };
            var missing = sharedBinding with { Parameters = new(sharedBinding.Parameters.Where(p => p.Key != "left-bcr-code")) };
            try { _ = RcsConfigurationValidation.HeartbeatMapping(selected with { Wcs = missing }); throw new Exception("Missing shared camera code accepted"); }
            catch (InvalidDataException ex) when (ex.Message.Contains("left-bcr-code")) { Check(true, "active hardware reader without shared camera code refuses configuration"); }
            service.ConfigureRuntime(selected);
            var count = peer.Requests.Count; await service.StartAsync(default); await Until(() => peer.Requests.Count > count);
            using var json = JsonDocument.Parse(peer.Requests.Last().Body); var data = json.RootElement.GetProperty("data");
            Check(data.GetArrayLength() == 1 && data[0].GetProperty("params").GetProperty("stationCode").GetString() == "SYNTH-NEW-STATION" &&
                data[0].GetProperty("params").GetProperty("bcrCode").GetString() == "SYNTH-NEW-BCR", "effective station and shared camera mapping determine the next runtime heartbeat without driver code copies");
        }
        var collision = binding with { Parameters = new(binding.Parameters) { ["request-id-base"] = "0", ["request-id-count"] = long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture) } };
        await using (var service = new SportmasterRcsTaskService(collision))
        {
            service.ConfigureRuntime(HeartbeatConfiguration(workspace, collision));
            var count = peer.Requests.Count; await service.StartAsync(default);
            await Until(async () => !(await service.HealthAsync(default)).Ready);
            Check(peer.Requests.Count == count && (await service.HealthAsync(default)).Detail.Contains("overlaps", StringComparison.Ordinal),
                "heartbeat refuses destination-ID range collisions before HTTP without consuming business IDs");
        }
        Console.WriteLine("PASS RCS HEARTBEAT COMPLETE");
    }

    static async Task Until(Func<bool> predicate) => await Until(() => Task.FromResult(predicate()));
    static async Task Until(Func<Task<bool>> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!await predicate()) await Task.Delay(20, deadline.Token);
    }
    static Task Healthy(SportmasterRcsTaskService service) => Until(async () =>
    {
        var health = await service.HealthAsync(default);
        return health.Ready && health.Detail.Contains("heartbeat HTTP", StringComparison.Ordinal);
    });

    public static async Task HeartbeatSignedChecks(string workspace, string root, string packageFile, string trustFile)
    {
        await using var peer = new Peer();
        ParcelCapabilityFactory.RegisterContracts();
        var trust = Wire.Decode<ExtensionTrust>(File.ReadAllText(trustFile));
        var verified = ExtensionPackages.Inspect(packageFile, trust, RunMode.Simulation);
        var payload = Directory.CreateDirectory(Path.Combine(root, "heartbeat-signed-payload")).FullName;
        ExtensionPackages.ExtractVerified(packageFile, payload, trust, RunMode.Simulation, verified.PackageSha256);
        var module = verified.Manifest.Modules.Single(x => x.Id == "sportmaster-rcs-task");
        Check(verified.Reference.Version == "1.2.1" && module.Requirements?.Query == false && module.Parameters.Single(p => p.Key == "heartbeat-interval-seconds").Default == "5",
            "signed immutable RCS package declares heartbeat schema and still has no original-query capability");
        var original = Binding(peer.Endpoint);
        var binding = original with { Version = verified.Reference.Version, Package = verified.Reference, Module = module.Id,
            Implementation = "extension:" + verified.Reference.Id + "/" + module.Id,
            Parameters = new(original.Parameters) { ["heartbeat-interval-seconds"] = "1" } };
        // Read-only signed-payload fixture. This does not install/register/activate a
        // package or bypass the Windows elevated installer and machine trust policy.
        var loading = new AssemblyLoadContext("synthetic-signed-rcs-heartbeat", isCollectible: true);
        loading.Resolving += (_, name) => name.Name == typeof(IWcsPort).Assembly.GetName().Name ? typeof(IWcsPort).Assembly :
            name.Name == typeof(ExtensionPackages).Assembly.GetName().Name ? typeof(ExtensionPackages).Assembly : null;
        try
        {
            var type = loading.LoadFromAssemblyPath(Path.Combine(payload, module.Assembly)).GetType(module.EntryType, throwOnError: true)!;
            var factory = (IWcsExtension)Activator.CreateInstance(type)!;
            await using var port = factory.Create(binding);
            Check(port is IWcsRuntimeConfiguration && ExtensionSchema.Validate(module.Parameters, binding.Parameters).Length == 0,
                "verified signed payload shares exact public contracts and valid component parameters in a separate load context");
            ((IWcsRuntimeConfiguration)port).ConfigureRuntime(HeartbeatConfiguration(workspace, binding));
            await port.StartAsync(default); await Until(() => peer.Requests.Count == 1);
            using var json = JsonDocument.Parse(peer.Requests.Single().Body);
            Check(json.RootElement.GetProperty("data").GetArrayLength() == 2 && json.RootElement.GetProperty("data")[0].GetProperty("command").GetString() == "sorter.task.station_heartbeat",
                "verified signed payload starts the actual two-reader heartbeat loop");
            var request = Request(binding); request = request with { IntegrationRequest = ((IWcsRequestPreparer)port).PrepareRequest(request, 7) };
            Check((await port.ResolveSlotAsync(request, default)).BusinessAccepted == true, "signed provider retains exact destination delivery alongside heartbeat");
            await port.StopAsync(default); var count = peer.Requests.Count; await Task.Delay(1200);
            Check(peer.Requests.Count == count, "signed component stops network work before package/load-context release");
            Console.WriteLine("PASS SIGNED RCS HEARTBEAT COMPLETE");
        }
        finally { loading.Unload(); }
    }
}
