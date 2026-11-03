using Ipc.Application.Contracts;
using Ipc.Platform;

// Integration of the operator corrections with main's one-placement workflow.
internal static class OperatorCorrections29
{
    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); Console.WriteLine("PASS OP29 " + message); }
    internal static async Task Run(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "operator29"));
        h.Config.Wcs!.Parameters!["request-timeout-ms"] = "30000";
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config);
        await h.StartAsync();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Peer.Hold = hold; h.Peer.HoldWhen = body => body.Contains("TEST-LEFT-READER", StringComparison.Ordinal);
        var code = CameraRoundHarness.Barcode("SYNTH-PLACEMENT-OP29");
        try
        {
            h.Api(h.Left).Enqueue(code, 1);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 1));
            var original = (await h.App.SnapshotAsync()).Operations.Single();
            var frozenRequest = Wire.Encode(original.Wcs!.Request);
            using (((IObservationPreviewLeaseSource)h.Factory.Sources[h.Left]).ReadPreviewLease()) { }
            for (uint frame = 2; frame <= 8; frame++)
            {
                var before = h.Factory.Decoders[h.Left].Count;
                h.Api(h.Left).Enqueue(code, frame);
                await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Decoders[h.Left].Count > before));
            }
            h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH-RIGHT-OP29"), 1);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.PositionId == "right" && o.Wcs?.State == StageState.Accepted));
            Check(h.Peer.Requests.Count == 2 && (await h.App.SnapshotAsync()).Operations.Count(o => o.PositionId == "left") == 1,
                "a held left RCS result creates no duplicate request while the right position completes independently");
            var acquisition = await h.App.ReadCameraAcquisitionAsync(h.Left, default);
            Check(acquisition.Diagnostics is { Frames: >= 8, DecodePaused: false, DecodeCompleted: > 1 },
                "capture and recognition remain active during the RCS wait so empty-tray facts cannot be lost");
            using (var preview = h.Factory.Sources[h.Left].ReadPreviewLease())
                Check(preview?.Frame.FrameNumber >= 2, "live video advances while the original RCS request is held");
            hold.TrySetResult(); h.Peer.Hold = null;
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.Id == original.Id && o.Wcs?.State == StageState.Accepted) &&
                h.App.WorkerProgress().Single(p => p.Position == "left").Processed >= 8);
            Check(h.Peer.Requests.Count == 2 && Wire.Encode((await h.App.OperationAsync(original.Id))!.Wcs!.Request) == frozenRequest,
                "busy same-placement observations drain without a second operation or mutation of the original request");
            var processed = h.App.WorkerProgress().Single(p => p.Position == "left").Processed;
            h.Api(h.Left).Enqueue(code, 9);
            await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(p => p.Position == "left").Processed > processed));
            Check(h.Peer.Requests.Count == 2 && ParcelInputReasons.IsSamePlacement((await h.App.SnapshotAsync()).Inputs!.Single(i => i.PositionId == "left")),
                "RCS acceptance alone does not release a parcel still resting on the tray");
            var next = await h.ReplaceParcelAsync(h.Left, 10);
            h.Api(h.Left).Enqueue(code, next++);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.PositionId == "left" && o.Wcs?.State == StageState.Accepted) == 2);
            var left = (await h.App.SnapshotAsync()).Operations.Where(o => o.PositionId == "left").ToArray();
            Check(h.Peer.Requests.Count == 3 && left.Select(o => o.Wcs!.Request.Id).Distinct().Count() == 2,
                "verified empty-tray release followed by the same code creates exactly one independent new request");
            h.Peer.Mode = "malformed";
            var unknownCode = CameraRoundHarness.Barcode("SYNTH-UNKNOWN-OP29");
            h.Api(h.Left).Enqueue(unknownCode, next++);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.Wcs?.State == StageState.Unknown));
            var unknown = (await h.App.SnapshotAsync()).Operations.Single(o => o.Wcs?.State == StageState.Unknown);
            var frozenUnknown = Wire.Encode(unknown.Wcs);
            var decoded = h.Factory.Decoders[h.Left].Count;
            h.Api(h.Left).Enqueue(unknownCode, next++);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Decoders[h.Left].Count > decoded));
            var refusal = await h.App.ReconcileAsync(unknown.Id);
            Check(refusal.Status == "QueryUnsupported" && h.Peer.Requests.Count == 4 &&
                Wire.Encode((await h.App.OperationAsync(unknown.Id))!.Wcs) == frozenUnknown,
                "a malformed original result is neither queried nor re-sent and its exact unknown-stage facts remain intact");
            await h.App.StopAsync(default);
            Check(h.Factory.Apis.Values.All(a => !a.HasFrame && a.Acquires == a.Releases),
                "normal stop returns every SDK frame loan after continuous placement observation");
        }
        finally { hold.TrySetResult(); h.Peer.Hold = null; }
        await ArchitectureQualityRemediation.VerifyUnsupportedReceiptQuery29(workspace, Path.Combine(root, "unsupported-receipt-query"));
    }
}
