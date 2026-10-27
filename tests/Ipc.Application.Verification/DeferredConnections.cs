using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;

static partial class SportmasterRcs
{
    // Real factory/lifecycle and loopback transports; no production qualification
    // or device is substituted. Each mask independently postpones left/right/RCS.
    public static async Task DeferredConnectionChecks(string workspace, string root)
    {
        Console.WriteLine(OperatingSystem.IsWindows()
            ? "WINDOWS: isolated loopback port composition; protected machine lease/installed runtime acceptance is not asserted. Production preflight uses the real factory."
            : "PORTABLE: real ParcelCapabilityFactory including session selection and resource leases.");
        for (var mask = 1; mask <= 7; mask++)
            await DeferredCombination(Path.Combine(root, "deferred-" + mask), mask);
        Console.WriteLine("PASS DEFERRED CONNECTIONS COMPLETE");
    }

    static ParcelConfiguration DeferredCandidate(TcpListener[] cameras, Peer peer, int mask)
    {
        var c = new SportmasterFlow().InitialConfiguration();
        var readers = c.Topology!.Readers.Select((r, i) => r with { Driver = "tcp-line", Parameters = new()
        {
            ["device.ip"] = "127.0.0.1", ["device.port"] = ((IPEndPoint)cameras[i].LocalEndpoint).Port.ToString(), ["camera.serial"] = "SYNTHETIC-DEFERRED-" + i,
            ["entity.mode"] = "line", ["entity.evidence"] = "Synthetic fixture: one line per new entity; no replay after disconnect"
        } }).ToArray();
        var b = Binding(peer.Endpoint);
        return c with
        {
            Reader = readers[0], Topology = c.Topology with { Readers = readers },
            Wcs = b with { Implementation = c.Wcs!.Implementation, Version = c.Wcs.Version, Parameters = new(b.Parameters)
            {
                ["left-station-code"] = c.StationCode!, ["right-station-code"] = c.StationCode!,
                ["heartbeat-interval-seconds"] = "1", ["zone-id"] = (mask & 4) != 0 ? "" : "71",
                ["left-bcr-code"] = (mask & 1) != 0 ? "" : "SYNTH-BCR-0", ["right-bcr-code"] = (mask & 2) != 0 ? "" : "SYNTH-BCR-1"
            } },
            DeferredConnections = readers.Where((_, i) => (mask & (1 << i)) != 0).Select(r => r.DeviceId)
                .Concat((mask & 4) != 0 ? new[] { c.Wcs.InstanceId } : []).ToArray()
        };
    }

    static async Task DeferredCombination(string root, int mask)
    {
        using var left = new TcpListener(IPAddress.Loopback, 0); left.Start();
        using var right = new TcpListener(IPAddress.Loopback, 0); right.Start();
        TcpListener[] cameras = [left, right];
        await using var peer = new Peer();
        var c = DeferredCandidate(cameras, peer, mask);
        var prefix = "deferred mask " + mask + ": ";
        var offlineLaunch = new BusinessLaunch(Path.Combine(root, "offline-config"), Path.Combine(root, "offline-data"), null, null);
        var offline = new ParcelStartupMaintenance(offlineLaunch, new SportmasterFlow(), () => new ParcelCapabilityFactory(offlineLaunch), () => { });
        var document = await offline.ReadAsync();
        await using (var validation = new ParcelCapabilityFactory(offlineLaunch))
        {
            var draft = new StartupConfigurationDraft(c, validation.Catalog());
            Check(draft.CreateParameterEditor((p, codes) => Task.FromResult(BarcodeFiltering.Select(p, codes))).ValidateFields(), prefix + "editor accepts independently deferred fields");
            var report = await offline.CheckAsync(c);
            Check(report.Checks.All(x => x.Status != "FAIL"), prefix + "actual Production first-run preflight agrees with editor; qualification remains separate");
            var saved = await offline.RepairInitialAsync(document.ConfigurationHash, c, "synthetic independent deferral");
            Check(Wire.Encode(saved.Candidate) == Wire.Encode(c) && !cameras.Any(x => x.Pending()) && peer.Requests.IsEmpty &&
                (!Directory.Exists(offlineLaunch.DataRoot) || !Directory.EnumerateFiles(offlineLaunch.DataRoot, "*.db", SearchOption.AllDirectories).Any()),
                prefix + "initial save preserves candidate without sessions, network requests or a business database");
            // A forgotten active identity still fails even if another camera is deferred.
            if ((mask & 4) == 0 && (mask & 3) != 3)
            {
                var missingCodes = c.Wcs! with { Parameters = new(c.Wcs.Parameters) { ["left-bcr-code"] = "", ["right-bcr-code"] = "" } };
                try { validation.ValidateStructure(c with { Wcs = missingCodes }); throw new Exception("Missing active bcr-code accepted"); }
                catch (InvalidDataException) { Check(true, prefix + "missing active camera identity still fails closed"); }
            }
        }
        c = c with { Mode = RunMode.Simulation };
        var launch = new BusinessLaunch(Path.Combine(root, "config"), Path.Combine(root, "data"), null, null);
        Directory.CreateDirectory(launch.ConfigurationDirectory);
        ExtensionStore.AtomicWrite(Path.Combine(launch.ConfigurationDirectory, "business.json"), c);
        var clients = new List<TcpClient>();
        async Task AcceptConfigured(int selectedMask)
        {
            for (var i = 0; i < 2; i++)
                if ((selectedMask & (1 << i)) == 0) clients.Add(await cameras[i].AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        }
        ParcelRuntime Open() => new(launch, new SportmasterFlow(), DeferredFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        try
        {
            await using (var app = Open())
            {
                await app.StartAsync(default); await AcceptConfigured(mask);
                var s = await app.SnapshotAsync();
                var allowed = !ParcelConnectionSetup.BlocksWork(c);
                if (allowed) s = await DeferredWait(app, x => x.Availability!.Resume);
                Check((allowed ? s.Availability!.Resume : !s.Availability!.Admit && !s.Availability.Resume) && s.Operations.Length == 0 && s.Configuration.Confirmation?.Required == false,
                    prefix + "runtime confirms configuration and requires RCS plus at least one configured ready camera");
                for (var i = 0; i < 2; i++)
                    if ((mask & (1 << i)) != 0)
                        Check(!cameras[i].Pending() && s.Devices!.Single(d => d.DeviceId == c.Topology!.Readers[i].DeviceId).Health != DeviceHealth.Ready,
                            prefix + "deferred camera " + i + " has no TCP session or Ready state");
                if ((mask & 4) == 0 && (mask & 3) != 3)
                {
                    await Until(() => !peer.Requests.IsEmpty);
                    using var json = JsonDocument.Parse(peer.Requests.First().Body);
                    var data = json.RootElement.GetProperty("data").EnumerateArray().ToArray();
                    var expected = c.Topology!.Readers.Where(r => !ParcelConnectionSetup.IsDeferred(c, r.DeviceId)).Select(r => c.Wcs!.Parameters[ParcelBindings.Positions(c).Single(p => p.ReaderDeviceId == r.DeviceId).Id + "-bcr-code"]);
                    Check(data.Select(x => x.GetProperty("params").GetProperty("bcrCode").GetString()).SequenceEqual(expected) &&
                        data.Select((x, i) => x.GetProperty("index").GetInt32() == i + 1).All(x => x), prefix + "heartbeat contains only configured identities with contiguous indexes");
                }
                else
                {
                    await Task.Delay(1200);
                    s = await app.SnapshotAsync();
                    Check(peer.Requests.IsEmpty && !s.Health.Single(h => h.InstanceId == c.Wcs!.InstanceId).Ready,
                        prefix + "deferred RCS or zero configured readers sends no empty heartbeat and reports unavailable");
                }
                await app.CommandAsync(new(Wire.Id(), (await app.SnapshotAsync()).Revision, "Pause"));
                await ManagementModeFixture.Authorize(app);
                var before = await app.SnapshotAsync();
                foreach (var id in c.DeferredConnections!)
                    Check((await app.ReconnectDeviceAsync(id)).Status == "Rejected", prefix + "explicit reconnect of " + id + " is rejected without enabling it");
                var receipt = await ReconnectAfterMonitorDrain(app, app.ReconnectAsync);
                Check(receipt.Status == (mask == 7 ? "Rejected" : "Reconnected"), prefix + "all-device reconnect selects only configured connections; actual=" + receipt.Status + "; reason=" + receipt.Reason);
                if (mask != 7) await AcceptConfigured(mask);
                s = await app.SnapshotAsync();
                Check(Wire.Encode(s.Configuration.Effective) == Wire.Encode(before.Configuration.Effective) && s.Operations.Length == 0 &&
                    s.Devices!.Where(d => ParcelConnectionSetup.IsDeferred(c, d.DeviceId)).All(d => d.Health != DeviceHealth.Ready && d.Epoch == before.Devices!.Single(b => b.DeviceId == d.DeviceId).Epoch),
                    prefix + "reconnect preserves Effective and deferred epochs, with no parcel request or fabricated readiness");
                Check(cameras.Where((_, i) => (mask & (1 << i)) != 0).All(x => !x.Pending()), prefix + "reconnect never acquires a deferred device session");
                var completed = DeferredCandidate(cameras, peer, 0) with { Mode = RunMode.Simulation, DeferredConnections = null };
                Check((await DeferredStage(app, completed, "synthetic completion of independent connections")).Status == "PendingRestart",
                    prefix + "completed parameters use the existing Pending revision");
                foreach (var id in c.DeferredConnections!)
                    Check((await app.ReconnectDeviceAsync(id)).Status == "Rejected", prefix + "Pending does not enable " + id + " through reconnect");
                s = await app.SnapshotAsync();
                Check(Wire.Encode(s.Configuration.Effective) == Wire.Encode(c) && s.Configuration.Pending is not null &&
                    cameras.Where((_, i) => (mask & (1 << i)) != 0).All(x => !x.Pending()), prefix + "saved completion retains old Effective and no deferred session is opened");
                await app.StopAsync(default);
            }
            foreach (var client in clients) client.Dispose(); clients.Clear();
            await using (var restored = Open())
            {
                await restored.StartAsync(default); await AcceptConfigured(0);
                var s = await restored.SnapshotAsync();
                Check(s.Configuration.Pending is null && s.Configuration.Effective.DeferredConnections is null && s.Configuration.Confirmation?.Required == false,
                    prefix + "controlled restart activates and confirms completed configuration");
                var heartbeatStart = peer.Requests.Count;
                await Until(() => peer.Requests.Count > heartbeatStart);
                using var json = JsonDocument.Parse(peer.Requests.Last().Body);
                Check(json.RootElement.GetProperty("data").GetArrayLength() == 2, prefix + "restored runtime uses both configured heartbeat identities");
                await restored.CommandAsync(new(Wire.Id(), s.Revision, "Resume"));
                await DeferredWait(restored, x => x.Positions!.All(p => p.Admit));
                foreach (var client in clients) await client.GetStream().WriteAsync(Encoding.UTF8.GetBytes("000123\n"));
                var accepted = await DeferredWait(restored, x => x.Operations.Count(o => o.State == ParcelState.SlotResolved) == 2);
                Check(accepted.Operations.Select(o => o.AdmissionId).Distinct().Count() == 2 && accepted.Operations.All(o =>
                    JsonDocument.Parse(o.Wcs!.Request.IntegrationRequest!.Body).RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("quantity").GetInt32() == 1),
                    prefix + "restored left/right same-code scans remain separate original requests with quantity one");
                if (mask == 7) await DeferredOriginalCustody(restored, launch, peer, clients[0], c);
                await restored.StopAsync(default);
            }
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }

    static async Task<ParcelSnapshot> DeferredWait(ParcelRuntime app, Func<ParcelSnapshot, bool> predicate)
    {
        ParcelSnapshot? last = null;
        await Until(async () => predicate(last = await app.SnapshotAsync()));
        return last!;
    }

    static async Task<BusinessReceipt> DeferredStage(ParcelRuntime app, ParcelConfiguration candidate, string reason)
    {
        BusinessReceipt? result = null;
        await Until(async () =>
        {
            var s = await app.SnapshotAsync();
            if (!s.Availability!.Configure) return false; // Wait for an in-progress health read to drain.
            result = await app.StageConfigurationAsync(s.Configuration.Revision, candidate, reason);
            // The health pulse can acquire a port between Snapshot and Stage. Only
            // this pre-mutation busy guard is retried; all validation/CAS failures fail.
            return result.Status != "Rejected" || result.Reason != "Pause and authenticate maintenance before configuration";
        });
        if (result!.Status != "PendingRestart") throw new InvalidOperationException(Wire.Encode(result));
        return result;
    }

    static async Task DeferredOriginalCustody(ParcelRuntime app, BusinessLaunch launch, Peer peer, TcpClient camera, ParcelConfiguration deferred)
    {
        peer.Mode = "wrong-id";
        await camera.GetStream().WriteAsync(Encoding.UTF8.GetBytes("000999\n"));
        var s = await DeferredWait(app, x => x.Operations.Any(o => o.Wcs?.State == StageState.Unknown));
        var original = s.Operations.Single(o => o.Wcs?.State == StageState.Unknown);
        var request = Wire.Encode(original.Wcs!.Request);
        int Destinations() => peer.Requests.Count(x => x.Body.Contains("sorter.task.dest_request", StringComparison.Ordinal));
        var sends = Destinations();
        deferred = deferred with { Wcs = deferred.Wcs! with { Parameters = new(deferred.Wcs.Parameters) { ["left-bcr-code"] = "SYNTH-PENDING-NEW-CODE" } } };
        await app.CommandAsync(new(Wire.Id(), s.Revision, "Pause")); await ManagementModeFixture.Authorize(app);
        s = await app.SnapshotAsync();
        BusinessReceipt? switchAttempt = null;
        await Until(async () =>
        {
            var state = await app.SnapshotAsync();
            if (!state.Availability!.Configure) return false;
            switchAttempt = await app.StageConfigurationAsync(state.Configuration.Revision, deferred, "synthetic producer deferral with unknown original");
            return switchAttempt.Reason != "Pause and authenticate maintenance before configuration";
        });
        Check(switchAttempt is { Status: "Rejected" } && switchAttempt.Reason!.Contains("ProducerSwitchBlocked") &&
            (await app.SnapshotAsync()).Configuration.Pending is null,
            "unknown original refuses producer deferral before any Pending mutation");
        // Keep the original RCS custody scenario: only RCS is deferred, with the
        // existing reader bindings and reader availability left intact.
        deferred = deferred with { DeferredConnections = [deferred.Wcs!.InstanceId] };
        Check((await DeferredStage(app, deferred, "synthetic RCS-only pending deferral")).Status == "PendingRestart",
            "unknown original permits a reviewed RCS-only Pending candidate, never an immediate target replacement");
        var reconnected = await ReconnectAfterMonitorDrain(app, () => app.ReconnectDeviceAsync(deferred.Wcs!.InstanceId));
        Check(reconnected.Status == "Reconnected", "RCS reconnect uses original Effective while new camera code and deferral are Pending; actual=" + reconnected.Status + "; reason=" + reconnected.Reason);
        s = await app.SnapshotAsync();
        Check(Wire.Encode(s.Operations.Single(o => o.Id == original.Id).Wcs!.Request) == request && Destinations() == sends,
            "reconnect does not retry Unknown or change request ID, payload, contract, target, hash or frozen configuration");
        await app.StopAsync(default); await app.DisposeAsync();
        await using var restarted = new ParcelRuntime(launch, new SportmasterFlow(), DeferredFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await restarted.StartAsync(default); await Task.Delay(1200);
        s = await restarted.SnapshotAsync();
        Check(s.Configuration.Effective.DeferredConnections is null && s.Configuration.Pending?.DeferredConnections?.SequenceEqual(new[] { deferred.Wcs!.InstanceId }) == true &&
            s.Operations.Single(o => o.Id == original.Id).Wcs is { State: StageState.Unknown } old && Wire.Encode(old.Request) == request && Destinations() == sends,
            "restart retains Unknown and original Effective; unresolved responsibility prevents pending deferral and automatic resend");
        await restarted.StopAsync(default);
    }

    static async Task<BusinessReceipt> ReconnectAfterMonitorDrain(ParcelRuntime app, Func<Task<BusinessReceipt>> reconnect)
        => await LifecycleOptimization.AfterMonitorDrain(app, reconnect);

    static IParcelCapabilityFactory DeferredFactory(BusinessLaunch launch) => OperatingSystem.IsWindows()
        ? new WindowsDeferredFixtureFactory(new ParcelCapabilityFactory(launch), Path.Combine(launch.DataRoot, "fixture-camera-leases")) : new ParcelCapabilityFactory(launch);

    // Windows developer machines need not be installed/provisioned to exercise
    // application state transitions. This fixture does not bypass protected paths
    // in production code. Linux CI exercises the unchanged real factory instead.
    sealed class WindowsDeferredFixtureFactory(ParcelCapabilityFactory inner, string leaseRoot) : IParcelCapabilityFactory
    {
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public void ValidateStructure(ParcelConfiguration c) => inner.ValidateStructure(c);
        public CapabilityRequirements Requirements(CapabilityBinding b) => inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c)
        {
            inner.Validate(c);
            var readers = ParcelBindings.Readers(c).ToDictionary(r => r.DeviceId, r => ParcelConnectionSetup.IsDeferred(c, r.DeviceId)
                ? (IControlledObservationSource)new DeferredObservationSource(r.DeviceId) : new Ipc.Drivers.TcpLineObservationSource(r, key => new ResourceLease(leaseRoot, [key])));
            IWcsPort wcs = ParcelConnectionSetup.IsDeferred(c, c.Wcs!.InstanceId) ? new DeferredWcsPort(c.Wcs.InstanceId)
                : new Ipc.Integrations.SportmasterRcs.SportmasterRcsTaskService(c.Wcs);
            if (wcs is IWcsRuntimeConfiguration runtime) runtime.ConfigureRuntime(c);
            return new(readers[c.Reader.DeviceId], null, wcs, null,
                new Dictionary<string, string> { [wcs.InstanceId] = Wire.Hash(wcs.GetType().FullName!) }, Readers: readers);
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
