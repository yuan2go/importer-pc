using Ipc.Application.Contracts;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

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
                var attempts = stage == "StopGrabbing" ? left.Stops : left.Closes;
                var rightOpens = right.Opens; var priorEpoch = h.Factory.Sources[h.Left].Epoch;
                if (stage == "StopGrabbing") Volatile.Write(ref left.StopError, UdpReceive);
                else Volatile.Write(ref left.CloseError, UdpReceive);
                left.Discovered = false;
                await CameraRoundHarness.Until(() => Task.FromResult((stage == "StopGrabbing" ? left.Stops : left.Closes) > attempts));
                left.Discovered = true;
                // The first failed Stop retired the health epoch. Recovery must
                // keep retrying its actual owner even though that entry is gone.
                await CameraRoundHarness.Until(() => Task.FromResult((stage == "StopGrabbing" ? left.Stops : left.Closes) >= attempts + 2));
                Check(left.Opens == opens && left.Destroys == destroys && left.Finalizes == finalizes && !await Online(), stage + " retry keeps the old native owner and never reopens or reports Ready early");
                var blocked = false;
                try { using var other = new ResourceLease(Path.Combine(h.Launch.DataRoot, "synthetic-device-leases"), ["SYNTH-ROUND-0"]); }
                catch (IOException) { blocked = true; }
                Check(blocked, stage + " pending cleanup retains the physical serial lease");
                var rightCode = "NETWORK-R-" + stage;
                right.Enqueue(CameraRoundHarness.Barcode(rightCode), (uint)(stage == "StopGrabbing" ? 1 : 2));
                await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Any(r => r.Contains(rightCode, StringComparison.Ordinal))));
                await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == rightCode && o.State == ParcelState.SlotResolved));
                Check(right.Opens == rightOpens && (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "right").BarcodeInput!.Camera, stage + " pending cleanup leaves the other camera and RCS supplying");
                Volatile.Write(ref left.StopError, 0); Volatile.Write(ref left.CloseError, 0);
                await CameraRoundHarness.Until(Online);
                var snapshot = await h.App.SnapshotAsync();
                Check(left.Opens == opens + 1 && left.Destroys == destroys + 1 && left.Finalizes == finalizes + 1 && h.Factory.Sources[h.Left].Epoch != priorEpoch,
                    stage + " automatically reopens exactly once after confirmed destroy/finalize, on a new epoch");
                Check(!snapshot.Paused && !(await h.App.ManagementModeStatusAsync()).Active, stage + " recovery needs no operator command, pause or management session");
                left.Enqueue(CameraRoundHarness.Barcode("NETWORK-UNKNOWN"), 1);
                await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Decoders[h.Left].Count >= (stage == "StopGrabbing" ? 2 : 3)));
                snapshot = await h.App.SnapshotAsync();
                Check(Wire.Encode(snapshot.Operations.Single(o => o.Id == original.Id).Wcs) == frozen &&
                    h.Peer.Requests.Count(r => r.Contains("NETWORK-UNKNOWN", StringComparison.Ordinal)) == 1,
                    stage + " recovery preserves the frozen Unknown request and never resends it");
            }

            var beforeStop = left.Stops; var beforeOpen = left.Opens;
            Volatile.Write(ref left.StopError, UdpReceive); left.Discovered = false;
            await CameraRoundHarness.Until(() => Task.FromResult(left.Stops > beforeStop));
            var stopFailed = false;
            try { await h.App.StopAsync(default); }
            catch (Exception error) when (error is ObservationCleanupPendingException || error is AggregateException aggregate && aggregate.Flatten().InnerExceptions.Any(e => e is ObservationCleanupPendingException)) { stopFailed = true; }
            Check(stopFailed && left.Opens == beforeOpen, "application shutdown reports retained native ownership instead of pretending Stop succeeded");
            Volatile.Write(ref left.StopError, 0); left.Discovered = true;
            await h.App.StopAsync(default);
            await Task.Delay(2200);
            Check(left.Opens == beforeOpen, "completed shutdown cancels pending retries and never reopens the camera");
        }
        finally { Volatile.Write(ref left.StopError, 0); Volatile.Write(ref left.CloseError, 0); left.Discovered = true; }

        foreach (var code in new[] { 0x80000206u, 0x80000213u, 0x80000214u, 0x80000215u, 0x80000216u })
        {
            var pending = false;
            try { MvsNative.CheckTeardownTransition(unchecked((int)code), "StopGrabbing"); } catch (ObservationCleanupPendingException) { pending = true; }
            Check(pending, "reviewed network teardown error remains retryable without claiming release: " + code.ToString("X8"));
        }
        foreach (var (code, stage) in new[] { (unchecked((int)0x80000006), "StopGrabbing"), (UdpReceive, "DestroyHandle"), (UdpReceive, "Finalize") })
        {
            var blocked = false;
            try { MvsNative.CheckTeardownTransition(code, stage); } catch (IOException error) when (error is not ObservationCleanupPendingException) { blocked = true; }
            Check(blocked, "non-network or destructive ownership failure still blocks: " + stage);
        }
    }
}
