using System.Net;
using System.Net.Sockets;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Platform;

static partial class SportmasterRcs
{
    public static async Task ProviderIdentity(string root)
    {
        var assembly = typeof(SportmasterFlow).Assembly;
        var declaration = BusinessCompatibility.Read(assembly);
        var launch = new BusinessLaunch(Path.Combine(root, "config"), Path.Combine(root, "data"), null, null);
        await using var factory = new ParcelCapabilityFactory(launch, declaration, assembly);
        var candidate = new SportmasterFlow().InitialConfiguration() with { Mode = RunMode.Simulation };
        var selected = factory.SelectedProviders(candidate).Where(x => x.Implementation == "hikrobot-mvs").ToArray();
        Console.WriteLine("ProviderIdentity=" + Wire.Encode(new { Selected = selected, DescriptorVersion = Ipc.Drivers.Hikrobot.Mvs.BundledMvs.Descriptor.Version }));
        Check(ParcelBindings.Readers(candidate).Count() == 2 && selected.Length == 1 && selected.All(x => x.Version == Ipc.Drivers.Hikrobot.Mvs.BundledMvs.Descriptor.Version), "normal selected SDK identity equals shipped descriptor");
        factory.ValidateStructure(candidate);
        Check(true, "normal business compatibility accepts deferred SDK startup candidate without bypassing declaration");
    }
    public static async Task DeferredMatrix(string workspace, string root)
    {
        var failures = new List<string>();
        for (var mask = 0; mask < 8; mask++)
        {
            var result = new Dictionary<string, string>();
            var left = new TcpListener(IPAddress.Loopback, 0); left.Start();
            var right = new TcpListener(IPAddress.Loopback, 0); right.Start();
            await using var peer = new Peer();
            try
            {
                var flow = new SportmasterFlow(); var seed = flow.InitialConfiguration();
                var readers = seed.Topology!.Readers.Select((r, i) => r with { Driver = "tcp-line", Parameters = (mask & (1 << i)) != 0
                    ? new() { ["entity.mode"] = "line" }
                    : new() { ["entity.mode"] = "line", ["device.ip"] = "127.0.0.1", ["device.port"] =
                        ((IPEndPoint)(i == 0 ? left : right).LocalEndpoint).Port.ToString() } }).ToArray();
                var binding = Binding(peer.Endpoint) with { Implementation = "sportmaster-rcs", Version = "1.0.0",
                    Parameters = new(Binding(peer.Endpoint).Parameters) { ["left-station-code"] = "SYNTH-STATION", ["right-station-code"] = "SYNTH-STATION", ["heartbeat-interval-seconds"] = "1" } };
                var c = seed with { StationCode = "SYNTH-STATION",
                    Reader = readers[0], Topology = seed.Topology with { Readers = readers }, Wcs = binding,
                    DeferredConnections = Enumerable.Range(0, 3).Where(i => (mask & (1 << i)) != 0).Select(i => i < 2 ? readers[i].DeviceId : binding.InstanceId).ToArray() };
                var launch = new BusinessLaunch(Path.Combine(root, "matrix-" + mask, "config"), Path.Combine(root, "matrix-" + mask, "data"), null, null);
                await using var factory = new ParcelCapabilityFactory(launch);
                async Task Stage(string name, Func<Task> action)
                {
                    try { await action(); result[name] = "PASS"; }
                    catch (Exception ex) { result[name] = ex.GetType().Name + ": " + ex.Message; failures.Add(mask + "/" + name + ": " + ex.Message); }
                }
                await Stage("Fields", () => { var editor = new StartupConfigurationDraft(c, factory.Catalog()).CreateParameterEditor((p, codes) => Task.FromResult(BarcodeFiltering.Select(p, codes))); Check(editor.ValidateFields(), "matrix " + mask + " field validation"); return Task.CompletedTask; });
                await Stage("Joint", () => { factory.ValidateStructure(c); return Task.CompletedTask; });
                var offline = new ParcelStartupMaintenance(launch, flow, () => new ParcelCapabilityFactory(launch), () => { });
                await Stage("Check", async () => { var check = await offline.CheckAsync(c); Check(check.Checks.All(x => x.Status != "FAIL"), "matrix joint check " + Wire.Encode(check)); });
                await Stage("Save", async () => { var original = await offline.ReadAsync(); var saved = await offline.RepairInitialAsync(original.ConfigurationHash, c, "synthetic deferred matrix"); Check(Wire.Encode(saved.Candidate) == Wire.Encode(c), "matrix saved candidate exact"); });
                await Stage("Start", async () =>
                {
                    // Start even when the old save path rejects the valid combination,
                    // so the baseline receipt captures both independent failures.
                    var simulation = c with { Mode = RunMode.Simulation };
                    Directory.CreateDirectory(launch.ConfigurationDirectory); ExtensionStore.AtomicWrite(Path.Combine(launch.ConfigurationDirectory, "business.json"), simulation);
                    // Isolated test composition exercises the real application, TCP and
                    // RCS ports, without installing a Windows machine lease policy.
                    await using var app = new ParcelRuntime(launch, flow, new P0SimulationFactory(launch)) { RuntimeIdentityPreflight = () => { } };
                    await app.StartAsync(default);
                    var snapshot = await app.SnapshotAsync();
                    Check(snapshot.Operations.Length == 0 && (!ParcelConnectionSetup.BlocksWork(c) || !snapshot.Availability!.Admit && !snapshot.Availability.Resume), "shared missing dependencies block admission; an optional unconfigured camera does not fabricate operations");
                    foreach (var reader in readers.Where(r => ParcelConnectionSetup.IsDeferred(c, r.DeviceId)))
                        Check(snapshot.Devices!.Single(d => d.DeviceId == reader.DeviceId).Health != DeviceHealth.Ready, "deferred reader remains unavailable");
                    await app.StopAsync(default);
                });
                if ((mask & 4) == 0)
                    await Stage("Heartbeat", async () =>
                    {
                        var mapping = RcsConfigurationValidation.HeartbeatMapping(c);
                        Check(mapping.Length == readers.Count(r => !ParcelConnectionSetup.IsDeferred(c, r.DeviceId)), "mapping excludes deferred readers");
                        await using var service = new SportmasterRcsTaskService(binding); service.ConfigureRuntime(c);
                        var before = peer.Requests.Count; await service.StartAsync(default); await Task.Delay(1250); await service.StopAsync(default);
                        var sent = peer.Requests.Skip(before).ToArray();
                        Check(mapping.Length == 0 ? sent.Length == 0 : sent.Length == 1, "zero active readers send no heartbeat; active readers send one batch");
                        if (sent.Length != 0)
                        {
                            using var body = System.Text.Json.JsonDocument.Parse(sent.Single().Body);
                            Check(body.RootElement.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("params").GetProperty("bcrCode").GetString()).SequenceEqual(mapping.Select(x => x.Bcr)), "runtime and preflight use the identical mapping");
                        }
                    });
                File.WriteAllText(Path.Combine(root, "matrix-" + mask + ".json"), Wire.Encode(new { Mask = mask, Deferred = c.DeferredConnections, Stages = result }));
                Console.WriteLine("MATRIX " + mask + " " + Wire.Encode(result));
            }
            finally { left.Stop(); right.Stop(); }
        }
        Check(failures.Count == 0, "all eight deferred combinations: " + string.Join(" | ", failures));
        var baseline = new SportmasterFlow().InitialConfiguration();
        var disabled = new ExtensionInstance("disabled", "synthetic.reader", "reader", "synthetic.disabled", false, new());
        var extensionReader = new DeviceBinding("parcel-reader", "extension:disabled", disabled.DeviceId, "1");
        var selection = baseline with { StationCode = "SYNTH-STATION", Reader = extensionReader, Topology = baseline.Topology! with { Readers = [extensionReader], Positions = [new("left", extensionReader.DeviceId)] }, DeferredConnections = null, Extensions = new([], [disabled]) };
        Check(RcsConfigurationValidation.HeartbeatMapping(selection).Length == 0, "explicitly disabled extension reader is not a heartbeat participant");
        try { RcsConfigurationValidation.HeartbeatMapping(selection with { Extensions = new([], [disabled with { Enabled = true }]) }); throw new Exception("active reader without bcr-code accepted"); }
        catch (InvalidDataException) { Check(true, "enabling the same reader restores mandatory bcr-code validation"); }
        await DeferredActivation(workspace, Path.Combine(root, "activation"));
    }
    static async Task DeferredActivation(string workspace, string root)
    {
        await using var peer = new Peer();
        var binding = Binding(peer.Endpoint) with { Implementation = "sportmaster-rcs", Version = "1.0.0" };
        var seed = new SportmasterFlow().InitialConfiguration();
        var oldBinding = binding with { Parameters = new(binding.Parameters) { ["request-id-base"] = "", ["request-id-count"] = "" } };
        var old = seed with { Mode = RunMode.Simulation, StationCode = "SYNTH-STATION", Wcs = oldBinding,
            DeferredConnections = seed.Topology!.Readers.Select(r => r.DeviceId).Append(binding.InstanceId).ToArray() };
        Directory.CreateDirectory(root); ExtensionStore.AtomicWrite(Path.Combine(root, "business.json"), old);
        var launch = new BusinessLaunch(root, Path.Combine(root, "data"), null, null);
        var completed = old with { Wcs = binding, DeferredConnections = seed.Topology.Readers.Select(r => r.DeviceId).ToArray() };
        var failures = new List<string>(); var result = new Dictionary<string, string>();
        await using (var app = new ParcelRuntime(launch, new SportmasterFlow(), new P0SimulationFactory(launch)) { RuntimeIdentityPreflight = () => { } })
        {
            await app.StartAsync(default);
            var snapshot = await app.SnapshotAsync(); await app.CommandAsync(new(Wire.Id(), snapshot.Revision, "Pause"));
            await ManagementModeFixture.Authorize(app);
            foreach (var id in old.DeferredConnections)
            {
                try { var receipt = await app.ReconnectDeviceAsync(id); result["Reconnect/" + id] = receipt.Status;
                    if (receipt.Status != "Rejected") failures.Add("deferred reconnect reported " + receipt.Status); }
                catch (InvalidOperationException error) { result["Reconnect/" + id] = error.GetType().Name; failures.Add("deferred reconnect threw"); }
            }
            try { var all = await app.ReconnectAsync(); result["ReconnectAll"] = all.Status;
                if (all.Status != "Rejected") failures.Add("all-deferred reconnect reported " + all.Status); }
            catch (InvalidOperationException error) { result["ReconnectAll"] = error.GetType().Name; failures.Add("all-deferred reconnect threw"); }
            BusinessReceipt? saved = null;
            await Until(async () => { var s = await app.SnapshotAsync(); if (!s.Availability!.Configure) return false;
                saved = await app.StageConfigurationAsync(s.Configuration.Revision, completed, "synthetic completion of postponed RCS"); return saved.Status != "Rejected"; });
            Check(saved!.Status == "PendingRestart", "postponed RCS completion saved as Pending"); result["Save"] = saved.Status;
            Check(Wire.Encode((await app.SnapshotAsync()).Configuration.Effective) == Wire.Encode(old), "save does not apply pending RCS through reconnect");
        }
        try
        {
            await using var app = new ParcelRuntime(launch, new SportmasterFlow(), new P0SimulationFactory(launch)) { RuntimeIdentityPreflight = () => { } };
            await app.StartAsync(default); var snapshot = await app.SnapshotAsync();
            Check(snapshot.Configuration.Pending is null && Wire.Encode(snapshot.Configuration.Effective) == Wire.Encode(completed) && !snapshot.Availability!.Admit,
                "controlled restart activates completed RCS without parsing empty postponed range or admitting deferred readers");
            result["Restart"] = "PASS";
        }
        catch (Exception error) { result["Restart"] = error.GetType().Name + ": " + error.Message; failures.Add(result["Restart"]); }
        File.WriteAllText(Path.Combine(root, "activation-result.json"), Wire.Encode(result));
        Console.WriteLine("DEFERRED ACTIVATION " + Wire.Encode(result));
        Check(failures.Count == 0, "deferred lifecycle and completion: " + string.Join(" | ", failures));
    }
}
