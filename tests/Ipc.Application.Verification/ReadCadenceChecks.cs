using System.Diagnostics;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Platform;

static class ReadCadenceChecks
{
    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); Console.WriteLine("PASS READ-CADENCE " + message); }
    static RecognitionSymbol Symbol(string code) => new(code, "Code128", [new(20, 20), new(120, 20), new(120, 60), new(20, 60)], 0);
    static async Task Pause(CameraRoundHarness h)
    {
        var snapshot = await h.App.SnapshotAsync();
        Check((await h.App.CommandAsync(new(Wire.Id(), snapshot.Revision, "Pause", Value: "SYNTH-CADENCE"))).Status == "Persisted", "pause is committed before the observation-only load");
    }
    static Task Idle(CameraRoundHarness h, string position, long processed) => CameraRoundHarness.Until(() =>
    {
        var lane = h.App.WorkerProgress().Single(p => p.Position == position);
        return Task.FromResult(lane.Processed >= processed && lane.State == "Waiting" && lane.QueueDepth == 0);
    });
    static async Task<JsonElement[]> Logs(CameraRoundHarness h, string action = "扫码阶段耗时与队列汇总")
    {
        var page = await h.App.QueryLogsAsync(new(DateTimeOffset.UtcNow.AddMinutes(-4), DateTimeOffset.UtcNow.AddMinutes(1)) { KeyEvents = true }, null, 0, default);
        try
        {
            return page.Entries.Select(e => JsonDocument.Parse(e.Text)).Select(d =>
            { using (d) return d.RootElement.Clone(); }).Where(e => e.TryGetProperty("Event", out var name) && name.GetString() == action)
                .Select(e => e.GetProperty("Detail").Clone()).ToArray();
        }
        finally { h.App.ReleaseLogSelection(page.Selection); }
    }
    static async Task<JsonElement[]> WaitStages(CameraRoundHarness h, params string[] stages)
    {
        JsonElement[] logs = [];
        await CameraRoundHarness.Until(async () =>
        {
            logs = await Logs(h);
            return stages.All(stage => logs.Any(l => l.GetProperty("MaximumStageMs").TryGetProperty(stage, out var ms) && ms.GetDouble() >= 5000));
        });
        return logs;
    }
    public static async Task Run(string workspace, string root)
    {
        await SlowPersistenceAndRepeats(workspace, Path.Combine(root, "durable"));
        await QueueAndGate(workspace, Path.Combine(root, "queued"));
    }
    static async Task SlowPersistenceAndRepeats(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, root, timeoutMs: 1000, nativeResults: true);
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync(); await Pause(h);
        var pixels = CameraRoundHarness.Barcode("SYNTH-CADENCE-A");
        var delayed = 0;
        h.App.Boundary = point => { if (point == "BeforeIngressDurable" && Interlocked.Exchange(ref delayed, 1) == 0) Thread.Sleep(5500); };
        h.Api(h.Left).EnqueueNative(pixels, 1, [Symbol("SYNTH-CADENCE-A")]);
        await Idle(h, "left", 1); h.App.Boundary = null;
        var input = (await h.App.SnapshotAsync()).Inputs!.Single(i => i.PositionId == "left");
        Check(input.Reason.StartsWith("ObservationExpired;") && (input.RecordedAt - input.Observation.ObservedAt).TotalSeconds >= 5,
            "a 5.5 second durable handoff reproduces the field expired observation without an RCS request");
        var logs = await WaitStages(h, "IngressPersistence");
        var stalled = logs.First(l => l.GetProperty("MaximumStageMs").GetProperty("IngressPersistence").GetDouble() >= 5000);
        Check(stalled.GetProperty("Slowest").GetProperty("TotalMs").GetDouble() >= 5500 &&
            stalled.GetProperty("Slowest").GetProperty("Camera").GetProperty("DecodeMs").GetDouble() == 12.5,
            "key logs distinguish slow durable storage from the measured native decode duration");
        Check(stalled.GetProperty("Slowest").GetProperty("SinceDecodeMs").GetDouble() >= 5500 &&
            stalled.GetProperty("Slowest").GetProperty("IngressId").GetString() is { Length: > 0 },
            "the slow trace correlates input custody and monotonic decode-to-completion latency");
        var original = new ParcelPictureQuery(ObservationId: input.Observation.Id);
        Check((await h.App.ReadParcelPictureAsync(original, default)).Image!.Gray8.SequenceEqual(pixels.Data), "an expired paused observation retains its exact original");
        var baseline = h.App.WorkerProgress().Single(p => p.Position == "left");
        var start = Stopwatch.GetTimestamp();
        for (uint i = 2; i <= 101; i++) h.Api(h.Left).EnqueueNative(pixels, i, [Symbol("SYNTH-CADENCE-A")]);
        await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(p => p.Position == "left").ObservedOnly == baseline.ObservedOnly + 100));
        var after = h.App.WorkerProgress().Single(p => p.Position == "left");
        var burstMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Console.WriteLine("READ-CADENCE-MEASUREMENT " + Wire.Encode(new { RepeatedFrames = 100, BurstMs = burstMs,
            AdditionalDurableInputs = after.Received - baseline.Received, after.QueueDepth }));
        Check(after.Received == baseline.Received && after.Processed == baseline.Processed && after.QueueDepth == 0,
            "100 repeated paused observations add zero durable inputs and zero queued business work");
        Check(burstMs < 5000 && h.Peer.Requests.Count == 0,
            "repeat observation load completes below the field expiry window and sends no request");
        Check((await h.App.ReadParcelPictureAsync(original, default)).Image!.Gray8.SequenceEqual(pixels.Data), "the first exact image survives the presence-only burst");
        var s = await h.App.SnapshotAsync();
        Check((await h.App.CommandAsync(new(Wire.Id(), s.Revision, "Resume"))).Status == "Persisted", "resume still executes the existing command guard");
        await h.WaitForCameraAdmissionAsync();
        h.Peer.ReplySlot = "";
        h.Api(h.Left).EnqueueNative(pixels, 102, [Symbol("SYNTH-CADENCE-A")]);
        await Idle(h, "left", baseline.Processed + 1);
        var op = (await h.App.SnapshotAsync()).Operations.Single();
        Check(op.State == ParcelState.WcsUnknown && h.Peer.Requests.Count == 1, "first current read after resume creates one request and retains an unknown response");
        var frozen = Wire.Encode(op.Wcs);
        h.Api(h.Left).EnqueueNative(pixels, 103, [Symbol("SYNTH-CADENCE-A")]);
        await Idle(h, "left", baseline.Processed + 2);
        Check(h.Peer.Requests.Count == 1 && Wire.Encode((await h.App.OperationAsync(op.Id))!.Wcs) == frozen,
            "same-placement read after an unknown response preserves the original request and never resends");
        await Pause(h);
        h.Api(h.Left).EnqueueNative(pixels, 104, [Symbol("SYNTH-CADENCE-B")]);
        await Idle(h, "left", baseline.Processed + 3);
        var changed = (await h.App.SnapshotAsync()).Inputs!.Single(i => i.PositionId == "left");
        Check(changed.Observation.Codes.Single() == "SYNTH-CADENCE-B" && (await h.App.ReadParcelPictureAsync(original, default)).Image is null,
            "a changed barcode remains a real input and retires the old image instead of being coalesced");
        h.Api(h.Left).EnqueueNative(pixels, 105, [Symbol("SYNTH-CADENCE-B")]);
        await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(p => p.Position == "left").ObservedOnly == after.ObservedOnly + 1));
        Check(h.Peer.Requests.Count == 1 && Wire.Encode((await h.App.OperationAsync(op.Id))!.Wcs) == frozen,
            "paused presence coalescing cannot mutate the previous unknown request or its original response");
    }
    static async Task QueueAndGate(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, root, timeoutMs: 1000, queueCapacity: 2, nativeResults: true);
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync(); await Pause(h);
        var pixels = CameraRoundHarness.Barcode("SYNTH-QUEUE");
        h.Api(h.Left).EnqueueNative(pixels, 1, [Symbol("SYNTH-QUEUE-A")]);
        await Idle(h, "left", 1);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var held = 0;
        h.App.Boundary = point =>
        {
            if (point == "BeforeIngressHandler" && Interlocked.Exchange(ref held, 1) == 0)
            { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Synthetic business stall not released"); }
        };
        try
        {
            h.Api(h.Left).EnqueueNative(pixels, 2, [Symbol("SYNTH-QUEUE-B")]);
            await CameraRoundHarness.Until(() => Task.FromResult(entered.IsSet));
            for (uint i = 3; i <= 5; i++) h.Api(h.Left).EnqueueNative(pixels, i, [Symbol(i == 3 ? "SYNTH-QUEUE-A" : "SYNTH-QUEUE-" + i)]);
            h.Api(h.Right).EnqueueNative(pixels, 1, [Symbol("SYNTH-OTHER-POSITION")]);
            await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(p => p.Position == "left").QueueDepth == 2 &&
                h.App.WorkerProgress().Single(p => p.Position == "left").ProducerStage == "WaitingCapacity" &&
                h.App.WorkerProgress().Single(p => p.Position == "right").State == "WaitingBusinessGate"));
            Check(h.App.WorkerProgress().Single(p => p.Position == "right").QueueDepth == 1,
                "an input waiting for the shared business gate remains visible in queue depth");
            await Task.Delay(5500);
            Check(h.App.WorkerProgress().Single(p => p.Position == "left").ProducerStageMs >= 5000,
                "in-flight capacity backpressure exposes its stage and monotonic age before the blocked handoff completes");
            var waitingLogs = await Logs(h, "扫码队列等待采样");
            Check(waitingLogs.Any(l => l.GetProperty("Workers").EnumerateArray().Any(w =>
                    w.GetProperty("Position").GetString() == "left" && w.GetProperty("ProducerStage").GetString() == "WaitingCapacity") &&
                l.GetProperty("Workers").EnumerateArray().Any(w => w.GetProperty("Position").GetString() == "right" &&
                    w.GetProperty("State").GetString() == "WaitingBusinessGate" && w.GetProperty("QueueDepth").GetInt32() == 1)),
                "default key logs expose the live stall while the business gate is still held, before completion");
        }
        finally { release.Set(); h.App.Boundary = null; }
        await Idle(h, "left", 5); await Idle(h, "right", 1);
        var logs = await WaitStages(h, "BusinessHandler", "BusinessQueue", "BusinessGate", "CapacityWait");
        Check(logs.Any(l => l.GetProperty("MaximumDepth").GetInt32() >= 2), "key logs retain queue high-water depth for the saturated lane");
        Check(logs.Select(l => l.GetProperty("Slowest")).Any(t => t.GetProperty("TotalMs").GetDouble() >= 5500 && t.GetProperty("TotalMs").GetDouble() < 8000),
            "a controlled stall reproduces the field 5 to 8 second end-to-end delay");
        Check(h.App.WorkerProgress().Sum(p => p.Received) == 6 && h.App.WorkerProgress().Sum(p => p.Processed) == 6 &&
            h.App.WorkerProgress().All(p => p.Interrupted == 0 && p.QueueDepth == 0),
            "all six observations including A-to-B-to-A retain custody and finish without overwrite, loss or stuck capacity");
        Check(h.Peer.Requests.Count == 0 && (await h.App.SnapshotAsync()).Operations.Length == 0,
            "delayed expired observations never create jobs or bypass paused admission");
    }
}
