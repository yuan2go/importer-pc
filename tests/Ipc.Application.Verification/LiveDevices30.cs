using Ipc.Application.Contracts;
using Ipc.Platform;

// Saving camera or RCS settings reconnects only those devices; the other camera keeps supplying.
// Real ZXing, real SQLite, real MVS driver over a synthetic SDK and the built-in RCS over loopback HTTP.
internal static class LiveDevices30
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS LIVE30 " + text); }
    static ParcelConfiguration WithReader(ParcelConfiguration c, string device, string key, string value)
    {
        var readers = c.Topology!.Readers.Select(r => r.DeviceId != device ? r : r with { Parameters = new Dictionary<string, string>(r.Parameters!) { [key] = value } }).ToArray();
        return c with { Topology = c.Topology with { Readers = readers }, Reader = readers.Single(r => r.DeviceId == c.Reader.DeviceId) };
    }
    static ParcelConfiguration WithRcs(ParcelConfiguration c, string? key = null, string? value = null, string? target = null) =>
        c with { Wcs = c.Wcs! with { Target = target ?? c.Wcs.Target, Parameters = key is null ? c.Wcs.Parameters : new Dictionary<string, string>(c.Wcs.Parameters) { [key] = value! } } };

    internal static async Task Run(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "live-devices-30")) { PlacementReleaseMs = 5000 };
        await using var secondRcs = new CameraRoundHarness.RoundPeer();
        await h.StartAsync();
        uint frame = 0;
        long Processed(string position) => h.App.WorkerProgress().Single(p => p.Position == position).Processed;
        async Task Read(string device, string position, string code)
        {
            var before = Processed(position);
            h.Api(device).Enqueue(CameraRoundHarness.Barcode(code), ++frame);
            await CameraRoundHarness.Until(() => Task.FromResult(Processed(position) > before));
        }
        async Task Requested(CameraRoundHarness.RoundPeer peer, string code) =>
            await CameraRoundHarness.Until(() => Task.FromResult(peer.Requests.Any(r => r.Contains(code, StringComparison.Ordinal))));
        async Task<bool> Online(string device)
        {
            var now = DateTimeOffset.UtcNow;
            return (await h.App.SnapshotAsync()).Devices?.Any(d => d.DeviceId == device && d.IsValid(now)) == true;
        }
        async Task<BusinessReceipt> Save(Func<ParcelConfiguration, ParcelConfiguration> change)
        {
            var snapshot = await h.App.SnapshotAsync(); var candidate = change(snapshot.Configuration.Effective);
            h.Factory.Expect(candidate);
            return await h.App.StageConfigurationAsync(snapshot.Configuration.Revision, candidate, "LIVE30 synthetic change");
        }

        // Baseline: both positions request chutes.
        await Read(h.Right, "right", "LIVE30-R-1"); await Requested(h.Peer, "LIVE30-R-1");
        await Read(h.Left, "left", "LIVE30-L-1"); await Requested(h.Peer, "LIVE30-L-1");
        var rightSource = h.Factory.Sources[h.Right]; var oldLeftApi = h.Api(h.Left); var oldLeft = h.Factory.Sources[h.Left];
        var revision = (await h.App.SnapshotAsync()).Configuration.Revision;

        // 1. A new left camera address: only the left camera reconnects, nothing restarts.
        var receipt = await Save(c => WithReader(c, h.Left, "device.ip", "192.0.2.41"));
        var snapshot = await h.App.SnapshotAsync();
        Check(receipt.Status == "Effective" && receipt.Reason.Contains("左相机", StringComparison.Ordinal) && snapshot.Configuration.Pending is null &&
            snapshot.Configuration.Effective.Topology!.Readers.Single(r => r.DeviceId == h.Left).Parameters!["device.ip"] == "192.0.2.41" &&
            snapshot.Configuration.Revision == revision + 1, "a left camera address is saved as effective in one revision without a pending restart");
        Check(h.Factory.ReaderBuilds == 1 && h.Factory.WcsBuilds == 0 && ReferenceEquals(h.Factory.Sources[h.Right], rightSource) && !ReferenceEquals(h.Factory.Sources[h.Left], oldLeft),
            "only the left camera is rebuilt; the right camera and RCS connection are the same objects");
        Check(oldLeftApi.Stops > 0, "the replaced left camera was stopped before the new one started");
        await CameraRoundHarness.Until(() => Online(h.Left));
        await Read(h.Left, "left", "LIVE30-L-2"); await Requested(h.Peer, "LIVE30-L-2");
        Check(true, "the reconnected left camera reads and requests a chute with the saved address");
        await Read(h.Right, "right", "LIVE30-R-2"); await Requested(h.Peer, "LIVE30-R-2");
        Check(true, "the right position keeps requesting chutes across the left camera change");
        var acquisition = await h.App.ReadCameraAcquisitionAsync(h.Left, default);
        Check(acquisition.Connection?.IsValid(DateTimeOffset.UtcNow) == true, "camera diagnostics and preview read the reconnected left camera, not the replaced one");
        var oldOpens = oldLeftApi.Opens;
        h.Api(h.Left).Discovered = false;
        await CameraRoundHarness.Until(async () => !await Online(h.Left));
        h.Api(h.Left).Discovered = true;
        await CameraRoundHarness.Until(() => Online(h.Left));
        Check(oldLeftApi.Opens == oldOpens, "automatic reconnect after a cable fault reopens the reconnected camera and never the replaced one");

        // 2. A new RCS camera code: only the RCS connection is rebuilt and new requests carry it.
        receipt = await Save(c => WithRcs(c, "left-bcr-code", "LIVE30-LEFT-CODE"));
        Check(receipt.Status == "Effective" && receipt.Reason.Contains("RCS", StringComparison.Ordinal) && h.Factory.WcsBuilds == 1 && h.Factory.ReaderBuilds == 1,
            "an RCS parameter is effective at once and rebuilds only the RCS connection");
        await Read(h.Left, "left", "LIVE30-L-3"); await Requested(h.Peer, "LIVE30-L-3");
        var request = h.Peer.Requests.Last(r => r.Contains("LIVE30-L-3", StringComparison.Ordinal));
        Check(request.Contains("LIVE30-LEFT-CODE", StringComparison.Ordinal) && !request.Contains("TEST-LEFT-READER", StringComparison.Ordinal),
            "the next request carries the saved camera code");

        // 3. A new RCS address: the next request goes to the new service.
        receipt = await Save(c => WithRcs(c, target: secondRcs.Endpoint));
        Check(receipt.Status == "Effective" && h.Factory.WcsBuilds == 2, "a new RCS address is effective at once");
        await Read(h.Right, "right", "LIVE30-R-3"); await Requested(secondRcs, "LIVE30-R-3");
        Check(!h.Peer.Requests.Any(r => r.Contains("LIVE30-R-3", StringComparison.Ordinal)), "requests after the change go to the new RCS address only");

        // 4. A camera that cannot be found takes only its own position offline; the save still applies.
        h.Factory.DiscoverReplacements = false;
        receipt = await Save(c => WithReader(c, h.Left, "device.ip", "192.0.2.42"));
        Check(receipt.Status == "Effective" && (await h.App.SnapshotAsync()).Configuration.Effective.Topology!.Readers.Single(r => r.DeviceId == h.Left).Parameters!["device.ip"] == "192.0.2.42",
            "an unreachable new camera address is still saved, as at startup");
        await Task.Delay(500);
        Check(!await Online(h.Left), "the unreachable left camera is offline");
        await Read(h.Right, "right", "LIVE30-R-4"); await Requested(secondRcs, "LIVE30-R-4");
        Check(true, "the right position keeps supplying while the left camera is offline");
        h.Factory.DiscoverReplacements = true;
        receipt = await Save(c => WithReader(c, h.Left, "device.ip", "192.0.2.43"));
        await CameraRoundHarness.Until(() => Online(h.Left));
        Check(receipt.Status == "Effective", "correcting the address brings the left camera back without a restart");

        // 5. Camera plus barcode rules: both apply together.
        receipt = await Save(c => WithReader(c, h.Right, "device.ip", "192.0.2.44") with { BarcodeFilter = new("^LIVE30-.*$") });
        snapshot = await h.App.SnapshotAsync();
        Check(receipt.Status == "Effective" && snapshot.Configuration.Effective.BarcodeFilter?.Pattern == "^LIVE30-.*$" && snapshot.Configuration.Pending is null,
            "a camera change together with new barcode rules applies both at once");
        await CameraRoundHarness.Until(() => Online(h.Right));

        // The two RCS fields whose descriptor used to promise a mandatory restart
        // use the same live path, retaining provider identity and both camera sessions.
        foreach (var (key, value) in new[] { ("request-timeout-ms", "2300"), ("heartbeat-interval-seconds", "1800") })
        {
            var cameraBuilds = h.Factory.ReaderBuilds;
            var wcsBuilds = h.Factory.WcsBuilds;
            receipt = await Save(c => WithRcs(c, key, value));
            snapshot = await h.App.SnapshotAsync();
            Check(receipt.Status == "Effective" && snapshot.Configuration.Pending is null &&
                snapshot.Configuration.Effective.Wcs!.Parameters[key] == value &&
                snapshot.Configuration.Effective.Wcs.Version == "1.0.0" &&
                snapshot.Configuration.Effective.Wcs.Contract == "SPORTMASTER-RCS-SORTER-v1" &&
                h.Factory.ReaderBuilds == cameraBuilds && h.Factory.WcsBuilds == wcsBuilds + 1,
                key + " applies live with unchanged provider contract and no camera rebuild");
        }

        // 6. Any other setting still uses the controlled restart, and so does a mix with it.
        var builds = (h.Factory.ReaderBuilds, h.Factory.WcsBuilds);
        receipt = await Save(c => WithReader(c, h.Left, "device.ip", "192.0.2.45") with { TimeoutMs = c.TimeoutMs + 1 });
        snapshot = await h.App.SnapshotAsync();
        Check(receipt.Status == "PendingRestart" && snapshot.Configuration.Pending is not null &&
            snapshot.Configuration.Effective.Topology!.Readers.Single(r => r.DeviceId == h.Left).Parameters!["device.ip"] == "192.0.2.43" &&
            (h.Factory.ReaderBuilds, h.Factory.WcsBuilds) == builds, "a camera change mixed with another setting waits for a controlled restart and rebuilds nothing now");
        receipt = await Save(c => WithReader(c, h.Right, "device.ip", "192.0.2.46"));
        Check(receipt.Status == "PendingRestart" && (h.Factory.ReaderBuilds, h.Factory.WcsBuilds) == builds, "with a saved restart pending, later device changes join it instead of applying live");
        Check(oldLeftApi.HasFrame == false && h.Factory.Apis.Values.All(a => !a.HasFrame), "every SDK frame loan is returned");

        // 7. An unconfirmed original request keeps its connection: the change waits for a controlled restart.
        await using var u = new CameraRoundHarness(workspace, Path.Combine(root, "live-devices-30-unknown"), timeoutMs: 1000);
        await u.StartAsync();
        u.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var before = u.App.WorkerProgress().Single(p => p.Position == "left").Processed;
        u.Api(u.Left).Enqueue(CameraRoundHarness.Barcode("LIVE30-UNKNOWN"), 1);
        await CameraRoundHarness.Until(async () => (await u.App.SnapshotAsync()).Operations.Any(o => o.Wcs?.State == StageState.Unknown));
        var unknownSnapshot = await u.App.SnapshotAsync(); var candidate = WithRcs(unknownSnapshot.Configuration.Effective, "left-bcr-code", "LIVE30-AFTER-UNKNOWN");
        u.Factory.Expect(candidate);
        receipt = await u.App.StageConfigurationAsync(unknownSnapshot.Configuration.Revision, candidate, "LIVE30 change with an unconfirmed original");
        Check(receipt.Status == "PendingRestart" && u.Factory.WcsBuilds == 0 && u.Factory.ReaderBuilds == 0 &&
            (await u.App.SnapshotAsync()).Configuration.Effective.Wcs!.Parameters["left-bcr-code"] == "TEST-LEFT-READER",
            "with an unconfirmed original request the RCS connection is not replaced; the change waits for a controlled restart");
        Check(u.Peer.Requests.Count(r => r.Contains("LIVE30-UNKNOWN", StringComparison.Ordinal)) == 1, "the unconfirmed original is not resent");
        u.Peer.Hold.TrySetResult();
    }
}
