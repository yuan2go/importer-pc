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
        var rcs = Binding(peer.Endpoint) with
        {
            Implementation = "sportmaster-rcs",
            Parameters = new(Binding(peer.Endpoint).Parameters)
            { ["left-station-code"] = "SYNTH-STATION", ["right-station-code"] = "SYNTH-STATION", ["heartbeat-interval-ms"] = "1000" }
        };
        var seed = HeartbeatConfiguration(workspace, rcs);
        var readers = seed.Topology!.Readers.Select((r, i) => r with
        {
            Driver = "hikrobot-mvs-reader",
            Parameters = new()
            { ["motion.still-ms"] = "0", ["device.ip"] = "192.0.2." + (11 + i), ["device.serial"] = "SYNTH-MVS-" + i, ["entity.mode"] = "read-result", ["acquisition.trigger"] = "off" }
        }).ToArray();
        var candidate = seed with
        {
            Reader = readers[0],
            Topology = seed.Topology with { Readers = readers },
            Login = null,
            DeferredConnections = [readers[1].DeviceId]
        };
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
            await app.StartAsync(default); SmRoundFixture.Register(app, launch, candidate);
            await ManagementModeFixture.Authorize(app);
            var s = await Wait(app, s => s.Health.Any(h => h.InstanceId == rcs.InstanceId && h.Ready));
            Check(s.Configuration.Status == "Effective" && s.Configuration.Pending is null && Wire.Encode(s.Configuration.Effective) == original,
                "saved MVS/RCS candidate becomes Effective unchanged while the physical camera is absent; no rollback to blank configuration");
            Check(s.Devices!.Single(d => d.DeviceId == readers[0].DeviceId).Health == DeviceHealth.Disconnected &&
                s.Positions!.Single(p => p.Id == "left").BarcodeInput is { Camera: false, Manual: true } && s.Operations.Length == 0,
                "configured offline camera cannot admit camera input, jobs or Ready; independent manual supply remains available");
            await app.ExitManagementModeAsync();
            s = await app.SnapshotAsync();
            Check(!ParcelConnectionSetup.IsDeferred(s.Configuration.Effective, readers[0].DeviceId) &&
                !ParcelConnectionSetup.IsDeferred(s.Configuration.Effective, rcs.InstanceId),
                "ordinary operator projection retains configured camera/RCS status and does not turn offline into unconfigured");
            await app.CommandAsync(new(Wire.Id(), s.Revision, "Pause"));
            await app.StopAsync(default);
        }
        var returned = new SoftwareFrameApi(readers[0]) { Discovered = false, OpenFailures = 1 };
        var returnedFactory = new SoftwareFlowFactory(launch, candidate, returned, false);
        await using (var app = new ParcelRuntime(launch, new SportmasterFlow(), returnedFactory) { RuntimeIdentityPreflight = () => { } })
        {
            await app.StartAsync(default); SmRoundFixture.Register(app, launch, candidate);
            await ManagementModeFixture.Authorize(app);
            var s = await Wait(app, s => s.Health.Any(h => h.InstanceId == rcs.InstanceId && h.Ready) && !s.Paused);
            Check(s.Configuration.Pending is null && Wire.Encode(s.Configuration.Effective) == original &&
                s.Positions!.Single(p => p.Id == "left").BarcodeInput is { Camera: false, Manual: true },
                "restart retains offline camera/RCS configuration and clears retired pause for independent manual supply");
            returned.Discovered = true;
            await app.ExitManagementModeAsync();
            // Reconnect retires the old health entry before the new epoch reports.
            // Absence means keep waiting; duplicate device identities still fail.
            s = await Wait(app, s => !s.Paused && s.Devices!.SingleOrDefault(d => d.DeviceId == readers[0].DeviceId)?.Health == DeviceHealth.Ready);
            Check(SavedEffective() == original && returned.OpenAttempts == 2,
                "returning camera reconnects without management authorization or changes to the retained configuration");
            s = await Wait(app, s => s.Positions!.Single(p => p.Id == "left").BarcodeInput?.Camera == true);
            returned.Enqueue(MakeSoftwareBarcode("SYNTH-OFFLINE-RETURN"), 1);
            s = await Wait(app, s => s.Operations.Any(o => o.State == ParcelState.SlotResolved));
            Check(s.Operations.Single().Wcs!.Attempts == 1 && SavedEffective() == original,
                "returning camera decodes and completes one actual loopback RCS request using the retained configuration");
            var priorEpoch = s.Devices!.Single(d => d.DeviceId == readers[0].DeviceId).Epoch;
            returned.Discovered = false;
            await Wait(app, s => s.Devices!.SingleOrDefault(d => d.DeviceId == readers[0].DeviceId)?.Health == DeviceHealth.Disconnected);
            returned.Discovered = true;
            s = await Wait(app, s => s.Positions!.Single(p => p.Id == "left").Arrival?.CameraBoundaryReady == true && s.Devices!.SingleOrDefault(d => d.DeviceId == readers[0].DeviceId) is { Health: DeviceHealth.Ready } d && d.Epoch != priorEpoch);
            Check(!s.Paused && s.Operations.Single().Wcs!.Attempts == 1,
                "runtime unplug/replug automatically gets a new epoch without pausing software or replaying the original RCS request");
            var deliveredBefore = returnedFactory.Source!.ReadDiagnostics().Delivered;
            returned.Enqueue(MakeSoftwareBarcode("SYNTH-OFFLINE-RETURN"), 1);
            s = await Wait(app, _ => returnedFactory.Source!.ReadDiagnostics().Delivered > deliveredBefore);
            Check(s.Operations.Length == 1 && s.Operations.Single().Wcs!.Attempts == 1,
                "a new camera epoch alone does not invent a new physical placement or replay the resting parcel");
            for (uint frame = 2; frame <= 4; frame++)
            {
                var decoded = returnedFactory.Source!.ReadDiagnostics().DecodeCompleted;
                returned.Enqueue(new(Enumerable.Repeat((byte)220, 200 * 100).ToArray(), 200, 100), frame);
                await Wait(app, _ => returnedFactory.Source.ReadDiagnostics().DecodeCompleted > decoded);
                await Task.Delay(CameraRoundHarness.SyntheticBlankSpanMs / 2 + 50);
            }
            s = await app.SnapshotAsync();
            Check(s.Operations.Count(o => o.State == ParcelState.SlotResolved) == 1, "blank frames after reconnect do not prove physical removal or release automatic suppression");
            await SmRoundFixture.NextVehicleAsync(app, "left");
            s = await app.SnapshotAsync();
            var manual = await app.SubmitParcelBarcodeAsync(new(Wire.Id(), s.RuntimeId, s.Configuration.Revision, "left", "SYNTH-OFFLINE-RETURN", ParcelBarcodeInputKind.Keyboard, null, null) { ExpectedArrivalGeneration = s.Positions!.Single(p => p.Id == "left").Arrival!.Generation }, default);
            s = await Wait(app, s => s.Operations.Count(o => o.State == ParcelState.SlotResolved) == 2);
            Check(manual.Status == "Admitted" && s.Operations.All(o => o.Wcs!.Attempts == 1), "a deliberate new manual same-code submission creates one request without replaying the original");
            await ManagementModeFixture.Authorize(app);
            s = await app.SnapshotAsync();
            Check(!s.Paused && s.Availability!.Configure, "running SM exposes parameter save without a manual pause");
            var saved = await app.StageConfigurationAsync(s.Configuration.Revision, candidate with { TimeoutMs = candidate.TimeoutMs + 1 }, "Synthetic automatic parameter save");
            s = await app.SnapshotAsync();
            Check(saved.Status == "PendingRestart" && !s.Paused && SavedEffective() == original && s.Configuration.Pending is not null,
                "unpaused parameter save automatically quiesces, keeps effective settings and restores normal admission after staging");
            // Field case: GigE packet loss, then StopGrabbing fails with 0x80000214 on the broken
            // stream. Teardown records it, really destroys/finalizes, and automatic reconnect reopens.
            var stops = returned.StopAttempts;
            var opens = returned.OpenAttempts;
            returned.FailStop = true; returned.Discovered = false;
            try
            {
                await Wait(app, _ => returned.StopAttempts > stops);
                returned.Discovered = true;
                s = await Wait(app, s => returned.OpenAttempts > opens && s.Positions!.Single(p => p.Id == "left").Arrival?.CameraBoundaryReady == true);
                Check(returned.Calls.Contains("destroy") && s.Operations.All(o => o.Wcs!.Attempts == 1),
                    "a StopGrabbing error after stream loss no longer blocks reconnect: native handle destroyed, camera reopened, original requests not resent");
            }
            finally { returned.FailStop = false; }
            // A real release failure (DestroyHandle) still keeps the owner and blocks automatic reopen.
            var destroys = returned.DestroyAttempts;
            opens = returned.OpenAttempts;
            returned.FailDestroy = true; returned.Discovered = false;
            try
            {
                await Wait(app, _ => returned.DestroyAttempts > destroys);
                returned.Discovered = true;
                await Task.Delay(2200);
                s = await app.SnapshotAsync();
                Check(returned.OpenAttempts == opens && s.Positions!.Single(p => p.Id == "left").BarcodeInput?.Camera == false && s.Operations.All(o => o.Wcs!.Attempts == 1),
                    "failed native release blocks automatic reopen and camera admission while retaining original requests and ownership");
            }
            finally { returned.FailDestroy = false; }
            await app.StopAsync(default);
            await Task.Delay(1200);
            Check(returned.OpenAttempts == opens, "completed application stop cancels recovery and never opens the camera again");
        }
    }
}
