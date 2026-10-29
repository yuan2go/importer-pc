using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;

static partial class SportmasterRcs
{
    public static async Task MvsOfflineActivation(string workspace, string root)
    {
        PrepareZxingFixture();
        await using var peer = new Peer();
        var rcs = Binding(peer.Endpoint) with { Implementation = "sportmaster-rcs", Parameters = new(Binding(peer.Endpoint).Parameters)
        { ["left-station-code"] = "SYNTH-STATION", ["right-station-code"] = "SYNTH-STATION", ["heartbeat-interval-seconds"] = "1" } };
        var seed = HeartbeatConfiguration(workspace, rcs);
        var readers = seed.Topology!.Readers.Select((r, i) => r with { Driver = "hikrobot-mvs-reader", Parameters = new()
        { ["device.ip"] = "192.0.2." + (11 + i), ["device.serial"] = "SYNTH-MVS-" + i, ["entity.mode"] = "read-result", ["acquisition.trigger"] = "off" } }).ToArray();
        var candidate = seed with { Reader = readers[0], Topology = seed.Topology with { Readers = readers }, Login = null,
            DeferredConnections = [readers[1].DeviceId] };
        var prior = candidate with { DeferredConnections = [readers[0].DeviceId, readers[1].DeviceId, rcs.InstanceId] };
        var folder = Path.Combine(root, "offline-config"); Directory.CreateDirectory(folder);
        ExtensionStore.AtomicWrite(Path.Combine(folder, "business.json"), prior);
        var launch = new BusinessLaunch(folder, Path.Combine(root, "offline-data"), null, null);
        var directory = Path.Combine(launch.DataRoot, candidate.Mode.ToString(), candidate.StationId, candidate.ApplicationId);
        // Establish the actual prior empty/deferred runtime and its database first;
        // never invent completed startup evidence over a missing business store.
        await using (var initial = new ParcelRuntime(launch, new SportmasterFlow(), new ParcelCapabilityFactory(launch)) { RuntimeIdentityPreflight = () => { } })
        { await initial.StartAsync(default); await initial.StopAsync(default); }
        var revisions = new ConfigurationRevisions<ParcelConfiguration>(Path.Combine(directory, "configuration.json"), prior, new SportmasterFlow().Validate);
        revisions.Stage(revisions.Current.Revision, candidate);
        var original = Wire.Encode(candidate);
        async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> ready)
        {
            for (var i = 0; i < 250; i++) { var snapshot = await app.SnapshotAsync(); if (ready(snapshot)) return snapshot; await Task.Delay(20); }
            throw new TimeoutException("Offline activation did not converge");
        }
        var absent = new SoftwareFrameApi(readers[0]) { Discovered = false };
        await using (var app = new ParcelRuntime(launch, new SportmasterFlow(), new SoftwareFlowFactory(launch, candidate, absent, false)) { RuntimeIdentityPreflight = () => { } })
        {
            await app.StartAsync(default);
            await ManagementModeFixture.Authorize(app);
            var s = await Wait(app, s => s.Health.Any(h => h.InstanceId == rcs.InstanceId && h.Ready));
            Check(s.Configuration.Status == "Effective" && s.Configuration.Pending is null && Wire.Encode(s.Configuration.Effective) == original,
                "saved MVS/RCS candidate becomes Effective unchanged while the physical camera is absent; no rollback to blank configuration");
            Check(s.Devices!.Single(d => d.DeviceId == readers[0].DeviceId).Health == DeviceHealth.Disconnected &&
                !s.Availability!.Admit && !s.Availability.Resume && s.Operations.Length == 0,
                "configured offline camera is visible as Disconnected and cannot fabricate admission, jobs or Ready");
            await app.ExitManagementModeAsync();
            s = await app.SnapshotAsync();
            Check(!ParcelConnectionSetup.IsDeferred(s.Configuration.Effective, readers[0].DeviceId) &&
                !ParcelConnectionSetup.IsDeferred(s.Configuration.Effective, rcs.InstanceId),
                "ordinary operator projection retains configured camera/RCS status and does not turn offline into unconfigured");
            await app.CommandAsync(new(Wire.Id(), s.Revision, "Pause"));
            await app.StopAsync(default);
        }
        var returned = new SoftwareFrameApi(readers[0]) { Discovered = false };
        await using (var app = new ParcelRuntime(launch, new SportmasterFlow(), new SoftwareFlowFactory(launch, candidate, returned, false)) { RuntimeIdentityPreflight = () => { } })
        {
            await app.StartAsync(default);
            await ManagementModeFixture.Authorize(app);
            var s = await Wait(app, s => s.Health.Any(h => h.InstanceId == rcs.InstanceId && h.Ready));
            Check(s.Configuration.Pending is null && Wire.Encode(s.Configuration.Effective) == original && s.Paused,
                "restart retains the configured offline camera and RCS with software pause; parameters need no re-entry");
            returned.Discovered = true;
            var reconnect = await app.ReconnectDeviceAsync(readers[0].DeviceId);
            Check(reconnect.Status == "Reconnected", "existing authorized single-device reconnect starts the returning camera");
            s = await Wait(app, s => s.Availability!.Resume);
            await app.CommandAsync(new(Wire.Id(), s.Revision, "Resume"));
            returned.Enqueue(MakeSoftwareBarcode("SYNTH-OFFLINE-RETURN"), 1);
            s = await Wait(app, s => s.Operations.Any(o => o.State == ParcelState.SlotResolved));
            Check(s.Operations.Single().Wcs!.Attempts == 1 && Wire.Encode(s.Configuration.Effective) == original,
                "returning camera decodes and completes one actual loopback RCS request using the retained configuration");
            await app.StopAsync(default);
        }
    }
}
