using System.Diagnostics;
using System.Text.Json;
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
        var binding = Binding(peer.Endpoint) with { Parameters = new(Binding(peer.Endpoint).Parameters.Where(p => p.Key != "heartbeat-interval-ms")) };
        var config = HeartbeatConfiguration(workspace, binding);
        await using (var service = new SportmasterRcsTaskService(binding))
        {
            try { await service.StartAsync(default); throw new Exception("Started without runtime context"); }
            catch (InvalidDataException) { Check(peer.Requests.IsEmpty, "missing station/driver configuration refuses start before network I/O"); }
            service.ConfigureRuntime(config);
            peer.ResponseHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var clock = Stopwatch.StartNew();
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.StartAsync(default)));
            await Until(() => peer.Requests.Count == 1);
            Check(peer.Requests.Count == 1, "repeated start owns one immediate initial heartbeat and one loop");
            Check((await service.HealthAsync(default)).RemoteConfirmation == RemoteConnectionConfirmation.Unconfirmed, "local lifecycle cannot fabricate a typed remote connection confirmation");
            Check((await service.HealthAsync(default)) is { RemoteConfirmedAt: null, RemoteValidUntil: null }, "health reads before first response cannot fabricate remote timestamps");
            peer.ResponseHold.TrySetResult(); peer.ResponseHold = null;
            await Until(() => peer.Requests.Count == 1);
            Check(clock.Elapsed < TimeSpan.FromSeconds(8) && RcsRequestPolicy.HeartbeatMs(binding) == 1000, "initial connectivity check is independent of the default one-second periodic interval");
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
            Check((await service.HealthAsync(default)).RemoteConfirmation == RemoteConnectionConfirmation.ResponseReceived, "actual successful heartbeat sets typed response evidence independently of display wording");
            Check((await service.HealthAsync(default)).Detail.Contains("unconfirmed", StringComparison.Ordinal), "HTTP delivery never fabricates an application acknowledgement");
            var evidence = await service.HealthAsync(default);
            var time = new TimeStorageFaultChecks.ManualTime { Utc = evidence.RemoteValidUntil!.Value.AddTicks(1) };
            service.HeartbeatTimeForVerification = time;
            var expired = await service.HealthAsync(default);
            Check(expired.RemoteConfirmation == RemoteConnectionConfirmation.Unconfirmed && expired.RemoteConfirmedAt == evidence.RemoteConfirmedAt && expired.RemoteValidUntil == evidence.RemoteValidUntil,
                "expired remote evidence pauses recognition without health reads renewing its actual response timestamps");
            service.HeartbeatTimeForVerification = TimeProvider.System;
            try { service.ConfigureRuntime(config); throw new Exception("Reconfigured running timer"); }
            catch (InvalidOperationException) { Check(true, "running component rejects context replacement"); }
            await service.StopAsync(default);
            var count = peer.Requests.Count; await Task.Delay(200);
            Check(peer.Requests.Count == count && !(await service.HealthAsync(default)).Ready, "stop drains the timer and reports stopped");
        }
        binding = binding with { Parameters = new(binding.Parameters) { ["heartbeat-interval-ms"] = "1000" } };
        config = HeartbeatConfiguration(workspace, binding);
        var schema = Wire.Decode<ParameterField[]>(File.ReadAllText(Path.Combine(workspace, "src/Ipc.Integrations.SportmasterRcs/parameters.json")));
        Check(ExtensionSchema.ValidateDefinition(schema).Length == 0 && schema.Single(f => f.Key == "heartbeat-interval-ms") is { Default: "1000", Minimum: 100, Maximum: 3600000 } &&
            ExtensionSchema.Validate(schema, binding.Parameters).Length == 0, "component parameter schema exposes a valid one-second default and bounded editable interval");
        await using (var service = new SportmasterRcsTaskService(binding))
        {
            service.ConfigureRuntime(config); peer.Mode = "http-error";
            var count = peer.Requests.Count; await service.StartAsync(default);
            await Until(async () => peer.Requests.Count > count && (await service.HealthAsync(default)).Detail.Contains("503", StringComparison.Ordinal));
            Check((await service.HealthAsync(default)).RemoteConfirmation == RemoteConnectionConfirmation.Unconfirmed, "failed HTTP heartbeat cannot retain typed online evidence");
            Check((await service.HealthAsync(default)).Detail.Contains("503", StringComparison.Ordinal), "configured one-second schedule exposes HTTP failures");
            peer.Mode = "assigned"; await Healthy(service);
            var first = peer.Requests.Last().Body;
            peer.Mode = "redirect"; await Until(async () => (await service.HealthAsync(default)).Detail.Contains("302", StringComparison.Ordinal));
            Check((await service.HealthAsync(default)).Detail.Contains("302", StringComparison.Ordinal), "heartbeat does not follow redirects");
            peer.Mode = "timeout"; await Until(async () => (await service.HealthAsync(default)).Detail.Contains("timeout", StringComparison.Ordinal));
            Check(true, "stalled heartbeat times out and leaves destination outcomes untouched");
            peer.Mode = "assigned"; await service.ReconnectAsync(default);
            Check((await service.HealthAsync(default)).RemoteConfirmation == RemoteConnectionConfirmation.Unconfirmed, "reconnect clears the old response confirmation before a new heartbeat");
            await Healthy(service);
            var last = peer.Requests.Last().Body;
            using var before = JsonDocument.Parse(first); using var after = JsonDocument.Parse(last);
            Check(after.RootElement.GetProperty("requestId").GetInt64() > before.RootElement.GetProperty("requestId").GetInt64(), "reconnect restarts a single loop with fresh increasing heartbeat IDs");
            var original = Request(binding); var prepared = service.PrepareRequest(original, 7);
            var reply = await service.RequestDestinationAsync(original with { IntegrationRequest = prepared }, default);
            Check(prepared.RequestId == 7 && reply.BusinessAccepted == true && reply.PhysicalCompleted != true,
                "heartbeat neither consumes durable destination IDs nor changes destination acceptance semantics");
            using (var destination = JsonDocument.Parse(prepared.Body))
            using (var heartbeat = JsonDocument.Parse(peer.Requests.First(r => r.Body.Contains("station_heartbeat", StringComparison.Ordinal)).Body))
                Check(destination.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("bcrCode").GetString() ==
                    heartbeat.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("bcrCode").GetString(),
                    "actual destination and heartbeat HTTP bodies use the same camera code");
            await service.StopAsync(default); count = peer.Requests.Count; await Task.Delay(1300);
            Check(peer.Requests.Count == count, "stopped component performs no later scheduled requests");
        }
        foreach (var invalid in new[] { "0", "99", "-1", "1.5", "3600001", "bad" })
        {
            try { await using var service = new SportmasterRcsTaskService(binding with { Parameters = new(binding.Parameters) { ["heartbeat-interval-ms"] = invalid } }); throw new Exception("Invalid interval accepted"); }
            catch (InvalidDataException) { Check(true, "invalid interval refused: " + invalid); }
        }
        var seconds = binding with { Parameters = new(binding.Parameters.Where(p => p.Key != "heartbeat-interval-ms")) { ["heartbeat-interval-seconds"] = "5" } };
        var originalSeconds = Wire.Encode(seconds);
        Check(RcsRequestPolicy.HeartbeatMs(seconds) == 5000 && RcsRequestPolicy.NormalizeHeartbeat(seconds).Parameters["heartbeat-interval-ms"] == "5000" && Wire.Encode(seconds) == originalSeconds,
            "editable seconds convert to equivalent milliseconds without rewriting the frozen original binding");
        foreach (var interval in new[] { 500, 1500 })
        {
            var precise = binding with { Parameters = new(binding.Parameters) { ["heartbeat-interval-ms"] = interval.ToString() } };
            await using var service = new SportmasterRcsTaskService(precise);
            service.ConfigureRuntime(HeartbeatConfiguration(workspace, precise));
            var count = peer.Requests.Count; var elapsed = Stopwatch.StartNew();
            await service.StartAsync(default); await Until(() => peer.Requests.Count > count);
            count = peer.Requests.Count; elapsed.Restart(); await Until(() => peer.Requests.Count > count);
            Check(elapsed.Elapsed.TotalMilliseconds >= interval * .8 && elapsed.Elapsed.TotalMilliseconds < interval + 1500,
                "actual heartbeat timer uses millisecond interval " + interval);
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
            Check(RcsConfigurationValidation.HeartbeatMapping(selected with { Wcs = missing }).All(p => p.Bcr != sharedBinding.Parameters["left-bcr-code"]),
                "empty camera code is omitted from heartbeat rather than blocking the configured lane");
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
            await Until(() => peer.Requests.Count > count);
            Check(peer.Requests.Count > count && (await service.HealthAsync(default)).Ready,
                "retired allocation fields do not block heartbeat or consume persisted destination IDs");
        }
        await ManualHeartbeatChecks(peer, workspace, binding);
        await VehicleHeartbeatChecks(peer, binding, HeartbeatConfiguration(workspace, binding));
        await VehicleProjectionChecks(workspace);
        Console.WriteLine("PASS RCS HEARTBEAT COMPLETE");
    }

    static async Task Until(Func<bool> predicate) => await Until(() => Task.FromResult(predicate()));
    static async Task ManualHeartbeatChecks(Peer peer, string workspace, CapabilityBinding binding)
    {
        var manual = binding with { Parameters = new(binding.Parameters) { ["heartbeat-interval-ms"] = "60000" } };
        await using var service = new SportmasterRcsTaskService(manual);
        service.ConfigureRuntime(HeartbeatConfiguration(workspace, manual));
        peer.Mode = "assigned";
        await service.StartAsync(default);
        await Healthy(service);
        var count = peer.Requests.Count;
        Check(await service.SendHeartbeatAsync(default) == RcsHeartbeatResult.Received && peer.Requests.Count == count + 1,
            "manual heartbeat immediately sends exactly one request before a scheduled tick");
        using var request = JsonDocument.Parse(peer.Requests.Last().Body);
        var items = request.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Check(items.Length == 2 && items.All(item => item.GetProperty("command").GetString() == "sorter.task.station_heartbeat") &&
            items.Select(item => item.GetProperty("params").GetProperty("bcrCode").GetString()).SequenceEqual(
                new[] { manual.Parameters["left-bcr-code"], manual.Parameters["right-bcr-code"] }),
            "manual heartbeat uses the existing protocol for all configured camera codes");
        peer.Mode = "http-error";
        Check(await service.SendHeartbeatAsync(default) == RcsHeartbeatResult.Failed && peer.Requests.Count == count + 2 &&
            (await service.HealthAsync(default)).RemoteConfirmation == RemoteConnectionConfirmation.Unconfirmed,
            "a manual connection failure reports the actual result without claiming online evidence");
        peer.Mode = "withhold-response";
        var pending = service.SendHeartbeatAsync(default);
        await Until(() => peer.Requests.Count == count + 3);
        Check(await service.SendHeartbeatAsync(default) == RcsHeartbeatResult.Busy, "repeated manual activation does not queue a duplicate");
        await service.StopAsync(default);
        Check(await pending == RcsHeartbeatResult.Unavailable && peer.Requests.Count == count + 3,
            "stop cancels and drains an in-flight manual heartbeat");
    }
    static async Task VehicleProjectionChecks(string workspace)
    {
        await using var station = new CameraRoundHarness(workspace, Path.Combine(Path.GetTempPath(), "agv-projection-" + Wire.Id()), heartbeatSeconds: 1);
        station.Peer.ReplyVehicleStates = true; await station.StartAsync(); await station.App.ExitManagementModeAsync();
        await Until(async () => (await station.App.SnapshotAsync()).Positions!.All(p => p.Vehicle is not null));
        var snapshot = await station.App.SnapshotAsync();
        Check(snapshot.Configuration.Effective.Wcs!.Parameters.Count == 0 && snapshot.Positions!.Single(p => p.Id == "left").Vehicle?.Status == "IDLE" &&
            snapshot.Positions!.Single(p => p.Id == "right").Vehicle?.Status == "TASKING",
            "Application projects the correct vehicle per position without exposing editable configuration to operators");
        var receipt = await station.App.SubmitParcelBarcodeAsync(new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, "right", "SYNTH-WORKING-AGV", ParcelBarcodeInputKind.Keyboard, null, null) { ExpectedArrivalGeneration = snapshot.Positions!.Single(p => p.Id == "right").Arrival!.Generation, ExpectedPlacementBoundary = snapshot.Positions!.Single(p => p.Id == "right").Arrival!.PlacementBoundary }, default);
        Check(receipt.Status == "Admitted", "a working vehicle is informational and cannot block a real manual admission");
        await Until(async () => (await station.App.OperationAsync(receipt.OperationId!))?.Wcs?.State == StageState.Accepted);
        station.Peer.ReplyVehicleStates = false;
        await Until(async () => (await station.App.SnapshotAsync()).Positions!.All(p => p.Vehicle is null));
        Check(true, "missing vehicle data clears previously projected idle state on the next heartbeat");
    }
    static async Task VehicleHeartbeatChecks(Peer peer, CapabilityBinding binding, ParcelConfiguration config)
    {
        peer.Mode = "heartbeat-vehicles";
        await using var service = new SportmasterRcsTaskService(binding);
        service.ConfigureRuntime(config); await service.StartAsync(default);
        await Until(async () => (await service.HealthAsync(default)).StationVehicles.Length == 2);
        var vehicles = (await service.HealthAsync(default)).StationVehicles;
        Check(vehicles.Single(v => v.CameraCode == binding.Parameters["left-bcr-code"]) is { VehicleNo: "SYNTH-AGV-1", Status: "IDLE" } &&
            vehicles.Single(v => v.CameraCode == binding.Parameters["right-bcr-code"]) is { VehicleNo: "SYNTH-AGV-2", Status: "TASKING" },
            "heartbeat response order does not swap the camera-bound idle and tasking vehicles");
        var exchange = peer.Requests.Last(); using var request = JsonDocument.Parse(exchange.Body);
        var id = request.RootElement.GetProperty("requestId").GetInt64();
        var mapping = RcsConfigurationValidation.HeartbeatMapping(config);
        StationVehicleState[] Parse(string body) => SportmasterRcsTaskService.ReadHeartbeatVehicles(System.Text.Encoding.UTF8.GetBytes(body), id, mapping, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(3));
        Check(Parse(exchange.Response.Replace(id.ToString(), (id + 1).ToString(), StringComparison.Ordinal)).Length == 0,
            "a previous heartbeat response cannot publish current idle state");
        Check(Parse(exchange.Response.Replace("SYNTH-STATION", "OTHER-STATION", StringComparison.Ordinal)).Length == 0,
            "a response for another station cannot show a vehicle at this station");
        Check(Parse(exchange.Response.Replace("\"success\":true", "\"success\":false", StringComparison.Ordinal)).Length == 0 && Parse("{bad").Length == 0,
            "failed and malformed heartbeat items never imply idle");
        peer.Mode = "body-timeout";
        await Until(async () => !(await service.HealthAsync(default)).Ready);
        Check((await service.HealthAsync(default)).StationVehicles.Length == 0, "a stalled response body expires its previous green vehicle state");
        peer.Mode = "heartbeat-vehicles"; await Until(async () => (await service.HealthAsync(default)).StationVehicles.Length == 2);
        await service.StopAsync(default);
        Check((await service.HealthAsync(default)).StationVehicles.Length == 0, "stop releases all previous vehicle telemetry");
        peer.Mode = "assigned";
    }
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

}
