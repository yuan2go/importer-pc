using System.Diagnostics;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Platform;

internal static class DualCameraPerformance
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS PERF33 " + text); }
    static JsonElement Progress(CameraRoundHarness h) => JsonSerializer.SerializeToElement(h.App.RuntimeProgress());
    static JsonElement[] Events(CameraRoundHarness h) => Progress(h).GetProperty("Flow").GetProperty("Recent").EnumerateArray().ToArray();
    internal static async Task Run(string workspace, string root)
    {
        Console.WriteLine("Synthetic MVS APIs, actual ZXing/SQLite/built-in RCS loopback; composition reports are synthetic, physical MVS/customer RCS/IPC NOT_RUN.");
        await IndependentSamePosition(workspace, root);
        await using (var h = new CameraRoundHarness(workspace, Path.Combine(root, "dual-performance")))
        {
            foreach (var reader in h.Config.Topology!.Readers) reader.Parameters!["code.formats"] = "[\"Code128\"]";
            ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config);
            await h.StartAsync();
            var baseline = Progress(h);
            Check(baseline.GetProperty("SchemaVersion").GetInt32() == 2 && baseline.GetProperty("RuntimeElapsedMs").GetDouble() >= 0,
                "existing progress exposes version and process-monotonic elapsed time without a new endpoint");
            Check(baseline.GetProperty("Connections").EnumerateArray().All(c => c.GetProperty("Performance").GetProperty("Acquisition").ValueKind == JsonValueKind.Object) &&
                !Wire.Encode(h.Factory.Sources[h.Left].ReadDiagnostics()).Contains("RecentFrames", StringComparison.Ordinal),
                "heavy frame windows are supervisor-only and never inflate normal UI acquisition counters");
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-PERF-LEFT"), 1);
            h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH-PERF-RIGHT"), 1);
            await CameraRoundHarness.Until(() => Task.FromResult(Events(h).Count(e => e.GetProperty("Kind").GetString() == "RcsSubmitCompleted") == 2));
            var events = Events(h);
            foreach (var position in new[] { "left", "right" })
            {
                var result = events.Single(e => e.GetProperty("Position").GetString() == position && e.GetProperty("Kind").GetString() == "RcsSubmitCompleted");
                var source = h.Factory.Sources[position == "left" ? h.Left : h.Right];
                await CameraRoundHarness.Until(() => Task.FromResult(source.Timings.Snapshot().RecentFrames.Length == 1));
                var frame = source.Timings.Snapshot().RecentFrames.Single();
                Check(frame.ObservationId == result.GetProperty("ObservationId").GetString() && frame.DecodeAttempted && frame.Delivered &&
                    frame.Number == 1 && frame.Width > 0 && frame.Height > 0 && frame.TraceSequence == 1 && !frame.SkippedBeforeDecode && frame.HostDeliveryToDecodeMs >= frame.DecodeMs,
                    position + " completion correlates to its exact epoch/frame/observation, even when both cameras use frame number one");
                Check(result.GetProperty("State").GetString() == "Accepted" && result.GetProperty("ElapsedMs").GetDouble() >= 0 &&
                    result.GetProperty("SinceDecodeMs").GetDouble() >= result.GetProperty("ElapsedMs").GetDouble() &&
                    events.Any(e => e.GetProperty("Kind").GetString() == "IngressQueue" && e.GetProperty("ObservationId").GetString() == frame.ObservationId),
                    position + " ingress wait, original submit and persisted RCS result are distinct numeric stages");
                var feedback = (await h.App.ReadPresentationFeedbackAsync(default)).Single(r => r.PositionId == position);
                await h.App.ReportPresentationAsync(feedback.RuntimeId, feedback.ObservationId, "SlotComposed", Stopwatch.GetTimestamp(), 0, default);
            }
            Check(Progress(h).GetProperty("Presentation").GetProperty("Total").GetInt64() == 2,
                "existing presentation reports include a total for collector window-loss detection");
            var beforeRequests = h.Peer.Requests.Count;
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-PERF-LEFT"), 2);
            await CameraRoundHarness.Until(() => Task.FromResult(Events(h).Any(e => e.GetProperty("Kind").GetString() == "SamePlacement")));
            Check(h.Peer.Requests.Count == beforeRequests && Events(h).Count(e => e.GetProperty("Kind").GetString() == "RcsSubmitStarted") == 2,
                "same-placement observation increases its own counter and causes no extra submit");
            h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH-PERF-RIGHT-NEW"), 2);
            await CameraRoundHarness.Until(() => Task.FromResult(Events(h).Count(e => e.GetProperty("Kind").GetString() == "RcsSubmitStarted") == 3));
            Check(Events(h).Count(e => e.GetProperty("Kind").GetString() == "RcsSubmitCompleted") == 2,
                "an in-flight request has a start without an invented result");
            h.Peer.Hold.TrySetResult();
            await CameraRoundHarness.Until(() => Task.FromResult(Events(h).Count(e => e.GetProperty("Kind").GetString() == "RcsSubmitCompleted") == 3));
            var serialized = Wire.Encode(h.App.RuntimeProgress());
            Check(!serialized.Contains("SYNTH-PERF-LEFT", StringComparison.Ordinal) && !serialized.Contains("SYNTH-PERF-RIGHT", StringComparison.Ordinal) &&
                !serialized.Contains(h.Peer.Endpoint, StringComparison.Ordinal) && !serialized.Contains("SYNTH-CHUTE", StringComparison.Ordinal),
                "new progress retains no barcode, target URL, raw response or slot value");
            Check(h.Api(h.Left).Acquires == h.Api(h.Left).Releases && h.Api(h.Right).Acquires == h.Api(h.Right).Releases,
                "passive diagnostics preserve both camera SDK loan balances");
            await CameraRoundHarness.Until(() => Task.FromResult(File.Exists(Path.Combine(h.Launch.DataRoot, "Simulation", h.Config.StationId, "sportmaster", "runtime-progress.json"))));
            var progressFile = Path.Combine(h.Launch.DataRoot, "Simulation", h.Config.StationId, "sportmaster", "runtime-progress.json");
            using var file = JsonDocument.Parse(File.ReadAllText(progressFile));
            Check(file.RootElement.GetProperty("Resource").GetProperty("CpuTotalMs").GetDouble() >= 0 &&
                file.RootElement.GetProperty("Resource").GetProperty("LogicalProcessors").GetInt32() > 0,
                "supervision file carries whole-process CPU without opening an additional process or device");
        }
        await using (var h = new CameraRoundHarness(workspace, Path.Combine(root, "timeout-performance"), 300))
        {
            await h.StartAsync(); h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-PERF-TIMEOUT"), 1);
            await CameraRoundHarness.Until(() => Task.FromResult(Events(h).Any(e => e.GetProperty("Kind").GetString() == "RcsSubmitCompleted")));
            var result = Events(h).Single(e => e.GetProperty("Kind").GetString() == "RcsSubmitCompleted");
            Check(result.GetProperty("State").GetString() == "Unknown" && result.GetProperty("LocalFailure").GetString() == "DeadlineExpired" && h.Peer.Requests.Count == 1,
                "held HTTP response produces Unknown/deadline metrics and never retries the original submit");
            h.Peer.Hold.TrySetResult();
        }
    }
    static async Task IndependentSamePosition(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "same-position-independent"), maximumOperations: 1);
        h.Config.Wcs!.Parameters["request-timeout-ms"] = "12000";
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config);
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        var hold = h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Peer.HoldWhen = body => body.Contains("SYNTH-FIRST-HELD", StringComparison.Ordinal);
        try
        {
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-FIRST-HELD"), 1);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 1 && h.App.WorkerProgress().Single(p => p.Position == "left").Processed >= 1));
            var first = (await h.App.SnapshotAsync()).Operations.Single(o => o.ParcelCode == "SYNTH-FIRST-HELD");
            var original = Wire.Encode(first.Wcs!.Request);
            Check(!hold.Task.IsCompleted && first.Wcs.State == StageState.Dispatching,
                "camera input completes without waiting for this parcel's held RCS response");
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-FIRST-HELD"), 2);
            await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(p => p.Position == "left").Processed >= 2));
            Check(h.Peer.Requests.Count == 1, "reobserving the held parcel creates no second request");
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-NEXT-READY"), 3);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-NEXT-READY" && o.State == ParcelState.SlotResolved));
            var before = await h.App.SnapshotAsync();
            var next = before.Operations.Single(o => o.ParcelCode == "SYNTH-NEXT-READY");
            Check(!hold.Task.IsCompleted && before.Operations.Single(o => o.Id == first.Id).Wcs!.State == StageState.Dispatching && h.Peer.Requests.Count == 2,
                "a fresh different parcel on the same position gets its own result while the predecessor is still in flight");
            Check(before.Configuration.Effective.MaximumOperations == 1 && before.Capacity is { Available: true, Unresolved: >= 1 },
                "an old pending-operation limit of one cannot block the next SM parcel or rewrite the saved configuration");
            Check(before.Positions!.Single(p => p.Id == "left").CurrentOperation == next.Id,
                "the card follows the new parcel rather than the predecessor's unfinished call");
            hold.TrySetResult();
            await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(first.Id))?.State == ParcelState.SlotResolved);
            var after = await h.App.SnapshotAsync();
            Check(Wire.Encode((await h.App.OperationAsync(first.Id))!.Wcs!.Request) == original &&
                after.Positions!.Single(p => p.Id == "left").CurrentOperation == next.Id && h.Peer.Requests.Count == 2,
                "the late reply updates only its frozen original request and does not replace the new parcel's card or resend either request");
        }
        finally { hold.TrySetResult(); }
    }
}
