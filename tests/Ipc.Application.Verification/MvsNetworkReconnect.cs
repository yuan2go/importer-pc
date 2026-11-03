using Ipc.Application.Contracts;
using Ipc.Platform;

// Real application loop, MVS owner, decoder, SQLite and RCS loopback;
// only the SDK device and camera frames are synthetic.
internal static class MvsNetworkReconnect
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS MVS NETWORK " + text); }
    const int UdpReceive = unchecked((int)0x80000214);

    internal static async Task Run(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "network-reconnect"), heartbeatSeconds: 1);
        await h.StartAsync();
        await h.App.ExitManagementModeAsync();
        async Task<bool> Online() => (await h.App.SnapshotAsync()).Devices?.Any(d => d.DeviceId == h.Left && d.IsValid(DateTimeOffset.UtcNow)) == true;
        bool LeaseHeld()
        {
            try { using var other = new ResourceLease(Path.Combine(h.Launch.DataRoot, "synthetic-device-leases"), ["SYNTH-ROUND-0"]); return false; }
            catch (IOException) { return true; }
        }
        var left = h.Api(h.Left); var right = h.Api(h.Right);
        h.Peer.Mode = "malformed";
        left.Enqueue(CameraRoundHarness.Barcode("NETWORK-UNKNOWN"), 1);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.Wcs?.State == StageState.Unknown));
        var original = (await h.App.SnapshotAsync()).Operations.Single(o => o.PositionId == "left");
        var frozen = Wire.Encode(original.Wcs);
        h.Peer.Mode = "assigned";
        try
        {
            foreach (var stage in new[] { "StopGrabbing", "CloseDevice" })
            {
                var opens = left.Opens; var destroys = left.Destroys; var finalizes = left.Finalizes;
                var rightOpens = right.Opens; var priorEpoch = h.Factory.Sources[h.Left].Epoch;
                using var enteredDestroy = new ManualResetEventSlim();
                using var releaseDestroy = new ManualResetEventSlim();
                left.BeforeDestroy = () => { enteredDestroy.Set(); if (!releaseDestroy.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Synthetic held destroy"); };
                if (stage == "StopGrabbing") Volatile.Write(ref left.StopError, UdpReceive);
                else Volatile.Write(ref left.CloseError, UdpReceive);
                left.Discovered = false;
                try
                {
                    await CameraRoundHarness.Until(() => Task.FromResult(enteredDestroy.IsSet));
                    Check(left.Opens == opens && left.Destroys == destroys && left.Finalizes == finalizes && !await Online(), stage + " failure cannot reopen or report Ready before real destroy/finalize");
                    Check(h.Factory.Sources[h.Left].TeardownNotes.Contains("0x80000214", StringComparison.Ordinal), stage + " original UDP error is retained in teardown diagnostics");
                    Check(LeaseHeld(), stage + " transition error retains the physical serial lease until actual release");
                    var rightCode = "NETWORK-R-" + stage;
                    right.Enqueue(CameraRoundHarness.Barcode(rightCode), (uint)(stage == "StopGrabbing" ? 1 : 2));
                    await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == rightCode && o.State == ParcelState.SlotResolved));
                    Check(right.Opens == rightOpens && (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "right").BarcodeInput!.Camera, stage + " teardown leaves the other camera and RCS supplying");
                }
                finally { left.Discovered = true; releaseDestroy.Set(); }
                await CameraRoundHarness.Until(Online);
                left.BeforeDestroy = null;
                var snapshot = await h.App.SnapshotAsync();
                Check(left.Opens == opens + 1 && left.Destroys == destroys + 1 && left.Finalizes == finalizes + 1 && h.Factory.Sources[h.Left].Epoch != priorEpoch,
                    stage + " automatically reopens exactly once after confirmed destroy/finalize, on a new epoch");
                Check(!snapshot.Paused && !(await h.App.ManagementModeStatusAsync()).Active, stage + " recovery needs no operator command, pause or management session");
                Check((stage == "StopGrabbing" ? left.StopError : left.CloseError) == UdpReceive, stage + " recovery does not depend on the broken stream error clearing");
                Volatile.Write(ref left.StopError, 0); Volatile.Write(ref left.CloseError, 0);
                left.Enqueue(CameraRoundHarness.Barcode("NETWORK-UNKNOWN"), 1);
                await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Decoders[h.Left].Count >= (stage == "StopGrabbing" ? 2 : 3)));
                snapshot = await h.App.SnapshotAsync();
                Check(Wire.Encode(snapshot.Operations.Single(o => o.Id == original.Id).Wcs) == frozen && h.Peer.Requests.Count(r => r.Contains("NETWORK-UNKNOWN", StringComparison.Ordinal)) == 1,
                    stage + " recovery preserves the frozen Unknown request and never resends it");
            }

            var beforeStop = left.Stops; var beforeOpen = left.Opens;
            Volatile.Write(ref left.StopError, UdpReceive); Volatile.Write(ref left.DestroyError, UdpReceive); left.Discovered = false;
            await CameraRoundHarness.Until(() => Task.FromResult(left.Stops > beforeStop));
            await Task.Delay(2200);
            Check(left.Opens == beforeOpen && left.Stops == beforeStop + 1 && !await Online(), "actual DestroyHandle failure blocks automatic reopening");
            Check(LeaseHeld(), "actual destroy failure retains the original physical lease");
            var stopFailed = false;
            try { await h.App.StopAsync(default); }
            catch (Exception error) when (error is IOException || error is AggregateException aggregate && aggregate.Flatten().InnerExceptions.Any(e => e is IOException)) { stopFailed = true; }
            Check(stopFailed && left.Opens == beforeOpen, "shutdown reports failed native destruction instead of pretending release");
            Volatile.Write(ref left.DestroyError, 0); left.Discovered = true;
            await h.App.StopAsync(default);
            await Task.Delay(2200);
            Check(left.Opens == beforeOpen, "completed shutdown cancels recovery and never reopens the camera");
        }
        finally { left.BeforeDestroy = null; Volatile.Write(ref left.StopError, 0); Volatile.Write(ref left.CloseError, 0); Volatile.Write(ref left.DestroyError, 0); left.Discovered = true; }
    }
}
