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
        string SavedEffective()
        {
            using var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "configuration.json")));
            return Wire.Encode(Wire.Decode<ParcelConfiguration>(saved.RootElement.GetProperty("Effective").GetRawText()));
        }
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
        var returned = new SoftwareFrameApi(readers[0]) { Discovered = false, OpenFailures = 1 };
        await using (var app = new ParcelRuntime(launch, new SportmasterFlow(), new SoftwareFlowFactory(launch, candidate, returned, false)) { RuntimeIdentityPreflight = () => { } })
        {
            await app.StartAsync(default);
            await ManagementModeFixture.Authorize(app);
            var s = await Wait(app, s => s.Health.Any(h => h.InstanceId == rcs.InstanceId && h.Ready));
            Check(s.Configuration.Pending is null && Wire.Encode(s.Configuration.Effective) == original && s.Paused,
                "restart retains the configured offline camera and RCS with software pause; parameters need no re-entry");
            returned.Discovered = true;
            await app.ExitManagementModeAsync();
            s = await Wait(app, s => !s.Paused && s.Devices!.Single(d => d.DeviceId == readers[0].DeviceId).Health == DeviceHealth.Ready);
            Check(SavedEffective() == original && returned.OpenAttempts == 2,
                "returning camera reconnects without management authorization; retired manual pause clears only after startup readiness");
            s = await Wait(app, s => s.Availability!.Admit);
            returned.Enqueue(MakeSoftwareBarcode("SYNTH-OFFLINE-RETURN"), 1);
            s = await Wait(app, s => s.Operations.Any(o => o.State == ParcelState.SlotResolved));
            Check(s.Operations.Single().Wcs!.Attempts == 1 && SavedEffective() == original,
                "returning camera decodes and completes one actual loopback RCS request using the retained configuration");
            var priorEpoch = s.Devices!.Single(d => d.DeviceId == readers[0].DeviceId).Epoch;
            returned.Discovered = false;
            await Wait(app, s => s.Devices!.Single(d => d.DeviceId == readers[0].DeviceId).Health == DeviceHealth.Disconnected);
            returned.Discovered = true;
            s = await Wait(app, s => s.Availability!.Admit && s.Devices!.Single(d => d.DeviceId == readers[0].DeviceId) is { Health: DeviceHealth.Ready } d && d.Epoch != priorEpoch);
            Check(!s.Paused && s.Operations.Single().Wcs!.Attempts == 1,
                "runtime unplug/replug automatically gets a new epoch without pausing software or replaying the original RCS request");
            returned.Enqueue(MakeSoftwareBarcode("SYNTH-OFFLINE-RETURN"), 1);
            s = await Wait(app, s => s.Operations.Count(o => o.State == ParcelState.SlotResolved) == 2);
            Check(s.Operations.All(o => o.Wcs!.Attempts == 1), "frame counter restart in new epoch creates one new read, not a replay of the old read");
            await ManagementModeFixture.Authorize(app);
            s = await app.SnapshotAsync();
            Check(!s.Paused && s.Availability!.Configure, "running SM exposes parameter save without a manual pause");
            var saved = await app.StageConfigurationAsync(s.Configuration.Revision, candidate with { TimeoutMs = candidate.TimeoutMs + 1 }, "Synthetic automatic parameter save");
            s = await app.SnapshotAsync();
            Check(saved.Status == "PendingRestart" && !s.Paused && SavedEffective() == original && s.Configuration.Pending is not null,
                "unpaused parameter save automatically quiesces, keeps effective settings and restores normal admission after staging");
            var stops = returned.StopAttempts;
            var opens = returned.OpenAttempts;
            returned.FailStop = true; returned.Discovered = false;
            try
            {
                await Wait(app, _ => returned.StopAttempts > stops);
                returned.Discovered = true;
                await Task.Delay(2200);
                s = await app.SnapshotAsync();
                Check(returned.OpenAttempts == opens && !s.Availability!.Admit && s.Operations.All(o => o.Wcs!.Attempts == 1),
                    "failed native cleanup blocks automatic reopen and admission while retaining original requests and ownership");
            }
            finally { returned.FailStop = false; }
            await app.StopAsync(default);
            await Task.Delay(1200);
            Check(returned.OpenAttempts == opens, "completed application stop cancels recovery and never opens the camera again");
        }
    }
}
