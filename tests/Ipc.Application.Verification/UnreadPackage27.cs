using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;
using ZXingCpp;

internal static class UnreadPackage27
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS UNREAD27 " + text); }
    internal static async Task Run(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "unread27"));
        await h.StartAsync();
        Decoder();
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-LEFT-27"), 1);
        h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH-RIGHT-27"), 1);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.State == ParcelState.SlotResolved) == 2);
        var blank = new CameraRoundHarness.RoundPixels(Enumerable.Repeat((byte)180, 640 * 240).ToArray(), 640, 240);
        h.Api(h.Left).Enqueue(blank, 2);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).CameraReads.Any(f => f.PositionId == "left" && f.Frame.Outcome == ObservationReadOutcome.NoCode));
        var snapshot = await h.App.SnapshotAsync();
        Check(snapshot.Operations.Length == 2 && h.Peer.Requests.Count == 2 && snapshot.CameraReads.Single(f => f.PositionId == "right").Frame.Outcome == ObservationReadOutcome.Codes,
            "no-code produces a position-specific decode fact without a new operation, image or RCS POST");
        using var subscriber = h.App.SubscribePresentation();
        var delivered = await subscriber.ReadAsync(default);
        Check(delivered.CameraReads.Single(f => f.PositionId == "left").Frame.Outcome == ObservationReadOutcome.NoCode,
            "a newly opened presentation subscriber receives the current no-code watermark");
        var source = h.Factory.Sources[h.Left];
        var handler = (Action<ObservationReadFeedback>)source.GetType().GetField("ReadFeedback", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(source)!;
        var prior = snapshot.CameraReads.Single(f => f.PositionId == "left").Frame;
        foreach (var stale in new[] { prior with { FrameNumber = 1, CompletedTicks = Stopwatch.GetTimestamp(), Outcome = ObservationReadOutcome.Codes, CandidateCount = 1 },
            prior with { Epoch = "retired", FrameNumber = 99, CompletedTicks = Stopwatch.GetTimestamp() },
            prior with { FrameNumber = 99, CompletedTicks = long.MaxValue },
            prior with { FrameNumber = 99, CompletedTicks = Stopwatch.GetTimestamp(), CandidateCount = 1 } }) handler(stale);
        Check((await h.App.SnapshotAsync()).CameraReads.Single(f => f.PositionId == "left").Frame == prior,
            "older frame, retired epoch, future clock and inconsistent candidate metadata cannot replace the watermark");
        h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var codesReached = new ManualResetEventSlim();
        using var releaseCodes = new ManualResetEventSlim();
        Action<ObservationReadFeedback> pauseBeforeObservation = frame =>
        {
            if (frame.FrameNumber != 3 || frame.Outcome != ObservationReadOutcome.Codes) return;
            codesReached.Set();
            if (!releaseCodes.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Codes/observation regression gate timed out");
        };
        ((IObservationReadFeedbackSource)source).ReadFeedback += pauseBeforeObservation;
        try
        {
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-DELAYED-27"), 3);
            await CameraRoundHarness.Until(() => Task.FromResult(codesReached.IsSet));
            using (var betweenCallbacks = h.App.SubscribePresentation())
            {
                var intermediate = await betweenCallbacks.ReadAsync(default);
                Check((await h.App.SnapshotAsync()).CameraReads.Single(f => f.PositionId == "left").Frame == prior &&
                    intermediate.CameraReads.Single(f => f.PositionId == "left").Frame == prior,
                    "new Codes before its observation preserves the retired-result watermark for snapshots and new subscribers");
            }
            releaseCodes.Set();
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 3));
            var matched = await h.App.SnapshotAsync();
            Check(matched.CameraReads.Single(f => f.PositionId == "left").Frame.FrameNumber == 3 &&
                (await h.App.ReadPresentationFeedbackAsync(default)).Single(f => f.PositionId == "left").DetectedTicks ==
                    matched.CameraReads.Single(f => f.PositionId == "left").Frame.CompletedTicks,
                "Codes becomes visible only together with its exact epoch and decode-clock observation cue");
            h.Api(h.Left).Enqueue(blank, 4);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).CameraReads.Any(f => f.PositionId == "left" && f.Frame.FrameNumber == 4));
        }
        finally
        {
            releaseCodes.Set();
            ((IObservationReadFeedbackSource)source).ReadFeedback -= pauseBeforeObservation;
            h.Peer.Hold.TrySetResult(); h.Peer.Hold = null;
        }
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.State == ParcelState.SlotResolved) == 3);
        Check((await h.App.SnapshotAsync()).CameraReads.Single(f => f.PositionId == "left").Frame.Outcome == ObservationReadOutcome.NoCode,
            "late RCS acceptance preserves its original fact without restoring a superseded camera frame");
        await h.ReplaceParcelAsync(); // Same label placed again: a new placement, not the resting parcel.
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-DELAYED-27"), 5);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.State == ParcelState.SlotResolved) == 4);
        var requests = h.Peer.Requests.Select(s => JsonDocument.Parse(s)).ToArray();
        try
        {
            Check(requests.Select(j => j.RootElement.GetProperty("requestId").GetInt64()).Distinct().Count() == 4 &&
                requests.All(j => j.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("quantity").GetInt32() == 1) &&
                requests.Count(j => j.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("loadCode").GetString() == "SYNTH-DELAYED-27") == 2,
                "same-code re-placements retain distinct original requests, original loadCode and quantity=1 for RCS handling");
        }
        finally { foreach (var j in requests) j.Dispose(); }
        h.Api(h.Right).NextLostPackets = 1; h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH-REJECT-27"), 2);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).CameraReads.Any(f => f.PositionId == "right" && f.Frame.Outcome == ObservationReadOutcome.RejectedFrame));
        Check(h.Peer.Requests.Count == 4 && h.Factory.Sources[h.Right].Failure.Length == 0,
            "rejected packet frame withdraws only its display authority without partial admission or whole-camera fault");
        await h.App.StopAsync(default);
        Check(h.Api(h.Left).Acquires == h.Api(h.Left).Releases && h.Api(h.Right).Acquires == h.Api(h.Right).Releases &&
            source.GetType().GetField("ReadFeedback", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(source) is null,
            "shutdown returns SDK loans and detaches the decode-feedback owner");
    }
    static void Decoder()
    {
        var image = CameraRoundHarness.Barcode("SYNTH-THRESHOLD-27");
        var dim = image.Data.Select(p => p == 0 ? (byte)60 : (byte)190).ToArray(); var original = dim.ToArray();
        // Force a genuinely empty native primary search on nonzero gray values.
        // The actual threshold fallback must recover complete, checked candidates.
        var decoder = new SoftwareDecoder("utf-8", () => { }, (_, _) => { }, r => r.Binarizer = Binarizer.BoolCast);
        decoder.Prepare();
        try
        {
            var code = decoder.DecodeDetailed(dim, dim.Length, image.Width, image.Height);
            Check(code is [{ Text: "SYNTH-THRESHOLD-27", Format: "Code 128" }] && dim.SequenceEqual(original),
                "empty primary search recovers a valid dim Code128 with original bytes and original-image coordinates");
            var both = CameraRoundHarness.Multiple("SYNTH-A-27", "SYNTH-B-27");
            var bothDim = both.Data.Select(p => p == 0 ? (byte)60 : (byte)190).ToArray();
            Check(decoder.Decode(bothDim, bothDim.Length, both.Width, both.Height).Length == 2,
                "fallback returns all two distinct candidates instead of selecting the first or deduplicating");
            Check(decoder.Decode(Enumerable.Repeat((byte)190, 10000).ToArray(), 10000, 100, 100).Length == 0,
                "blank frame remains no-code rather than fabricated recognition");
        }
        finally { decoder.Release(); }
        var qrOnly = new SoftwareDecoder("utf-8", () => { }, (_, _) => { }, r => r.Binarizer = Binarizer.BoolCast, new(["QRCode"], false)); qrOnly.Prepare();
        try { Check(qrOnly.Decode(dim, dim.Length, image.Width, image.Height).Length == 0, "fallback preserves configured formats and never broadens QR-only search to Code128"); }
        finally { qrOnly.Release(); }
        decoder.Prepare();
        try { Check(decoder.Decode(dim, dim.Length, image.Width, image.Height).Single() == "SYNTH-THRESHOLD-27", "both native readers can be released and prepared again"); }
        finally { decoder.Release(); }
    }
}
