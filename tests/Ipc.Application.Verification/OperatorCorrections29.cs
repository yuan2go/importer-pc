using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

internal static class OperatorCorrections29
{
    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); Console.WriteLine("PASS OP29 " + message); }
    internal static async Task Run(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "operator29"));
        await h.StartAsync();
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.All(p => p.Admit));
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Peer.Hold = hold; h.Peer.HoldWhen = body => body.Contains("TEST-LEFT-READER", StringComparison.Ordinal);
        var code = CameraRoundHarness.Barcode("SYNTH-PACED-29");
        try
        {
            h.Api(h.Left).Enqueue(code, 1);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 1));
            var original = (await h.App.SnapshotAsync()).Operations.Single();
            var originalRequest = Wire.Encode(original.Wcs!.Request);
            var decoderCount = h.Factory.Decoders[h.Left].Count;
            var pending = await h.App.SnapshotAsync();
            var frame = pending.CameraReads!.Single(c => c.PositionId == "left");
            var staleVideoCue = frame with { Frame = frame.Frame with { FrameNumber = 2, SourceEventId = "mvs.frame.2", CandidateCount = 0,
                Outcome = ObservationReadOutcome.RejectedFrame, CompletedTicks = System.Diagnostics.Stopwatch.GetTimestamp() } };
            var view = new SupplyStationViewModel(h.App); view.SetViewport(1366, 768);
            await view.AcceptPublishedSnapshotAsync(pending with { CameraReads = pending.CameraReads.Where(c => c.PositionId != "left").Append(staleVideoCue).ToArray() }, default);
            Check(view.Left.Barcode == original.ParcelCode && view.Left.IsProcessing && !view.Left.ShowConfirmedTask,
                "a later video rejection cannot replace the current waiting barcode/result with a request to rescan");
            // Preview remains passive/continuous while recognition waits for the result.
            using (((IObservationPreviewLeaseSource)h.Factory.Sources[h.Left]).ReadPreviewLease()) { }
            for (uint n = 2; n <= 8; n++) h.Api(h.Left).Enqueue(code, n);
            h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH-RIGHT-29"), 1);
            await CameraRoundHarness.Until(async () => h.Api(h.Left).Releases >= 8 && (await h.App.SnapshotAsync()).Operations.Any(o => o.PositionId == "right" && o.Wcs?.State == StageState.Accepted));
            using (var preview = h.Factory.Sources[h.Left].ReadPreviewLease())
                Check(preview?.Frame.FrameNumber >= 2, "left live video advances while its original RCS request is held");
            Check(h.Factory.Decoders[h.Left].Count == decoderCount && h.Peer.Requests.Count == 2 &&
                (await h.App.SnapshotAsync()).Operations.Count(o => o.PositionId == "left") == 1,
                "busy frames are acquired/returned but not decoded, queued as parcels or sent; right position proceeds independently");
            var diagnostic = await h.App.ReadCameraAcquisitionAsync(h.Left, default);
            Check(diagnostic.Diagnostics is { Frames: >= 8, DecodePaused: true } && diagnostic.Diagnostics.Epoch == diagnostic.Epoch,
                "passive diagnostic reports actual frames and paused recognition without relying on a preview thumbnail");
            hold.TrySetResult(); h.Peer.Hold = null;
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.Id == original.Id && o.Wcs?.State == StageState.Accepted));
            await Task.Delay(180);
            Check(h.Peer.Requests.Count == 2 && Wire.Encode((await h.App.OperationAsync(original.Id))!.Wcs!.Request) == originalRequest,
                "returning RCS result does not replay any frame captured during the wait or mutate the original request");
            h.Api(h.Left).Enqueue(code, 9);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.PositionId == "left" && o.Wcs?.State == StageState.Accepted) == 2);
            var left = (await h.App.SnapshotAsync()).Operations.Where(o => o.PositionId == "left").ToArray();
            Check(left.Select(o => o.AdmissionId).Distinct().Count() == 2 && left.Select(o => o.Wcs!.Request.Id).Distinct().Count() == 2 && h.Peer.Requests.Count == 3,
                "a new post-result frame with the same barcode remains an independent read and request, without barcode deduplication");
            h.Peer.Mode = "malformed"; h.Api(h.Left).Enqueue(code, 10);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.PositionId == "left" && o.Wcs?.State == StageState.Unknown));
            var unknown = (await h.App.SnapshotAsync()).Operations.Single(o => o.Wcs?.State == StageState.Unknown);
            var frozenUnknown = Wire.Encode(unknown.Wcs);
            h.Api(h.Left).Enqueue(code, 11);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Api(h.Left).Releases >= 11)); await Task.Delay(100);
            Check(h.Peer.Requests.Count == 4 && !(await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Admit &&
                Wire.Encode((await h.App.OperationAsync(unknown.Id))!.Wcs) == frozenUnknown,
                "unknown outcome keeps this position closed without replay, automatic next scan or mutation of its original responsibility");
            Check(h.Factory.Sources.Values.All(s => s.ReadDiagnostics().AcquireCalls >= s.ReadDiagnostics().Frames) &&
                h.Factory.Apis.Values.All(a => !a.HasFrame && a.Acquires == a.Releases),
                "all acquired SDK frame loans are returned while waiting and after reopening");
        }
        finally { hold.TrySetResult(); h.Peer.Hold = null; }
    }
}
