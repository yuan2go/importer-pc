using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

internal static class DecodingVerification
{
    static int checks;
    static void Check(bool ok, string risk) { if (!ok) throw new InvalidOperationException("DECODING FAIL: " + risk); checks++; Console.WriteLine("PASS DECODING: " + risk); }
    static async Task Until(Func<bool> condition) { using var limit = new CancellationTokenSource(5000); while (!condition()) await Task.Delay(5, limit.Token); }
    static async Task Reject(Func<Task> action) { try { await action(); } catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or OperationCanceledException or TimeoutException or AggregateException) { return; } throw new InvalidOperationException("Expected fail-closed result"); }
    static Dictionary<string, string> Settings(int frames = 1, bool trigger = true) => new()
    {
        ["device.ip"] = "192.0.2.1", ["image.maximum-payload-bytes"] = "1048576", ["decode.pool-size"] = "3", ["decode.queue-capacity"] = "1",
        ["decode.timeout-ms"] = "300", ["decode.stop-timeout-ms"] = "200", ["trigger.complete-timeout-ms"] = "1000",
        ["entity.mode"] = trigger ? "trigger" : "observe-only", ["acquisition.trigger"] = "on", ["trigger.expected-source"] = "2", ["trigger.expected-selector"] = "0",
        ["trigger.frames-per-entity"] = frames.ToString(), ["selection.version"] = "synthetic-v1", ["selection.prefix"] = "SYNTH-",
        ["selection.pattern"] = "SYNTH-[0-9A-Z]+", ["selection.symbologies"] = "Code39,QR"
    };
    static MvsAcquisition Source(Camera api, ProgrammableDecoder decoder, Dictionary<string, string>? settings = null)
        => new(new("test", "synthetic-camera", settings ?? Settings()), api, _ => api.Lease, decoderFactory: () => decoder);
    public static async Task Run(string root)
    {
        await PoolAndSelection(); await EndToEnd(); await Overload(); await CaptureIsolation(); await CancellationAndRetainedView();
        await StateAndTriggerFailures(); await EpochFence(); await Replay(root); await IsolatedWorker();
        Console.WriteLine($"DECODING_SELF_TEST_PASS checks={checks}; synthetic only; real SDK/trigger/accuracy NOT_RUN");
    }
    static async Task PoolAndSelection()
    {
        using var camera = new Camera();
        var pool = new FramePool(3, 1024);
        var a = pool.TryCopy(camera.Frame(1, 1), "one")!; var b = pool.TryCopy(camera.Frame(2, 2), "one")!; var c = pool.TryCopy(camera.Frame(3, 3), "one")!;
        Check(pool.TryCopy(camera.Frame(4, 4), "one") is null && pool.InUse == 3, "pool exhaustion cannot allocate beyond the configured resident byte budget");
        a.Dispose(); a.Dispose(); b.Dispose(); c.Dispose();
        Check(pool.InUse == 0 && pool.TryCopy(camera.Frame(5, 5), "two") is { } returned && Return(returned), "exception/finally ownership supports exactly-once return and subsequent reuse");
        static bool Return(IDisposable lease) { lease.Dispose(); return true; }
        foreach (var change in new[] { ("decode.pool-size", "32"), ("decode.queue-capacity", "3"), ("selection.pattern", "(a)\\1"), ("selection.version", ""), ("trigger.expected-source", "") })
        {
            var settings = Settings(); settings[change.Item1] = change.Item2;
            if (change.Item1 == "decode.pool-size") settings["decode.maximum-pool-bytes"] = "1048576";
            await Reject(() => { _ = new MvsDecodeOptions(settings); return Task.CompletedTask; });
        }
        Check(true, "candidate validation refuses excess memory, insufficient slots, unbounded regex, unversioned rules and missing trigger evidence");
        var selector = new WaybillSelector(new("synthetic-v1", 5, 20, "SYNTH-", "SYNTH-[0-9]+", ["Code39"]));
        var view = new FrameView(new byte[64], 8, 8, 8, FramePixelFormat.Mono8, 1, 0);
        var eligible = ProgrammableDecoder.Code(view);
        var one = selector.Select(view, eligible); var two = selector.Select(view, [.. eligible, .. eligible]); var other = selector.Select(view, ProgrammableDecoder.Code(view, "SYNTH-001", "QR")); var empty = selector.Select(view, []);
        Check(one.Text == "SYNTH-001" && two.Issue == "AmbiguousCodes" && other.Text is null && empty.Text is null,
            "selection requires one eligible full candidate; duplicate/high-quality codes, wrong symbology and no-code cannot be guessed; outcomes=" + string.Join(",", one.Issue, two.Issue, other.Issue, empty.Issue));
        await Reject(() => { selector.Select(view, [new("SYNTH-001", "Code39", new(0, 0, 99, 1))]); return Task.CompletedTask; });
        Check(true, "malformed result geometry cannot turn incomplete decoder output into an accepted code");
    }
    static async Task EndToEnd()
    {
        using var camera = new Camera(); var decoder = new ProgrammableDecoder(); var observations = new ConcurrentQueue<Observation>(); var health = new ConcurrentQueue<DeviceHealthConfirmation>();
        bool viewCorrect = false;
        decoder.Steps.Enqueue(async (f, t) => { await Task.Delay(25, t); viewCorrect = f.Pixels.Span[0] == 9 && f.DeviceTimestamp == 1234 && f.Stride == 8; return ProgrammableDecoder.Code(f); });
        await using var source = Source(camera, decoder, Settings(2));
        await source.StartControlledAsync("0001", o => { observations.Enqueue(o); return true; }, health.Enqueue, default);
        camera.Enqueue(1, 1); await Until(() => source.Statistics.Decoded == 1); camera.Enqueue(2, 1); await Until(() => observations.Count == 1);
        camera.Enqueue(3, 2); await Until(() => source.Statistics.Decoded == 3); camera.Enqueue(4, 2); await Until(() => observations.Count == 2);
        var first = observations.First(); var last = observations.Last();
        Check(viewCorrect && first.Kind == "Read" && first.Codes.Single() == "SYNTH-001" && first.Sequence == 1 && last.Sequence == 2 &&
            first.AdmissionId != last.AdmissionId && first.SourceEventId != last.SourceEventId && first.DeviceId == "synthetic-camera" && first.Epoch == "0001",
            "fake camera → copied read-only pixels/timestamp → decoder → one observation per complete trigger, same barcode on new trigger is a new entity");
        Check(health.Any(h => h.Health == DeviceHealth.Ready && h.ValidUntil > h.ConfirmedAt) && source.ConfirmationSource.Contains("triggerVerified=true") && source.ConfirmationSource.Contains("businessReady=true"),
            "business readiness requires acquisition, successful self-test, exact trigger settings and valid rule together");
        await source.StopAsync(default);
        Check(decoder.Active == 0 && camera.Freed == 4 && source.Statistics.BuffersInUse == 0 && camera.Lease.Releases == 1, "successful stop drains decoder and all copied/native buffers before releasing the physical lease");
        using var observeCamera = new Camera(); var observeDecoder = new ProgrammableDecoder(); var observe = new ConcurrentQueue<Observation>();
        await using var only = Source(observeCamera, observeDecoder, Settings(trigger: false));
        await only.StartControlledAsync("observation", o => { observe.Enqueue(o); return true; }, _ => { }, default);
        observeCamera.Enqueue(1, 1); await Until(() => observe.Count == 1);
        Check(observe.Single().Kind == "UncorrelatedRead" && observe.Single().AdmissionId == "" && only.ConfirmationSource.Contains("businessReady=false"), "observe-only never becomes a business entity even with a Ready decoder and valid selection rule");
    }
    static async Task Overload()
    {
        using var camera = new Camera(); var decoder = new ProgrammableDecoder(); decoder.Hang();
        var health = new ConcurrentQueue<DeviceHealthConfirmation>(); int observations = 0;
        var settings = Settings(); settings["decode.timeout-ms"] = "2000";
        await using var source = Source(camera, decoder, settings);
        await source.StartControlledAsync("overload", _ => { observations++; return true; }, health.Enqueue, default);
        camera.Enqueue(1, 1); await decoder.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        camera.Enqueue(2, 2); await Until(() => camera.Freed == 2); camera.Enqueue(3, 3);
        await Until(() => health.Last().Health == DeviceHealth.Faulted);
        Check(camera.Freed == 3 && source.Statistics.Dropped > 0 && source.Statistics.AcquisitionGaps == 0 && source.Statistics.QueueHighWater <= 1 &&
            observations == 0 && source.ConfirmationSource.Contains("businessReady=false"), "decode queue overflow immediately returns SDK loans, counts separately from frame gaps and visibly blocks trigger business");
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => source.StopAsync(default))).WaitAsync(TimeSpan.FromSeconds(3));
        Check(decoder.Active == 0 && decoder.Disposals == 1 && source.Statistics.BuffersInUse == 0 && camera.Lease.Releases == 1, "concurrent Stop joins one cancellation/drain/teardown and leaks no buffer or decoder owner");
    }
    static async Task CaptureIsolation()
    {
        foreach (bool blockCallback in new[] { true, false })
        {
            using var camera = new Camera(); var decoder = new ProgrammableDecoder();
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!blockCallback) decoder.Steps.Enqueue(async (f, token) =>
            {
                using var registration = token.Register(() => release.Wait());
                entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return ProgrammableDecoder.Code(f);
            });
            var settings = Settings(); settings["decode.timeout-ms"] = "3000";
            await using var source = Source(camera, decoder, settings);
            try
            {
                await source.StartControlledAsync("isolation", _ => { entered.TrySetResult(); release.Wait(); return true; }, _ => { }, default);
                camera.Enqueue(1, 1); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                camera.Enqueue(2, 2); await Until(() => camera.Freed == 2); camera.Enqueue(3, 3);
                await Until(() => camera.Freed == 3 && source.Statistics.Dropped > 0);
                Check(!release.IsSet && source.Statistics.QueueHighWater <= 1,
                    blockCallback ? "blocked Observation consumer cannot block capture or SDK buffer return; bounded overload remains visible" :
                    "blocking decoder cancellation callback cannot run on the SDK acquisition thread or hold its borrowed buffer");
            }
            finally { release.Set(); }
            await source.StopAsync(default);
            Check(source.Statistics.BuffersInUse == 0 && decoder.Active == 0, "blocked downstream/cancellation work is joined before copied buffers are released");
        }
    }
    static async Task CancellationAndRetainedView()
    {
        using var camera = new Camera(); var decoder = new ProgrammableDecoder(); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        decoder.Steps.Enqueue(async (f, _) => { await release.Task; return ProgrammableDecoder.Code(f); });
        int observations = 0; await using var source = Source(camera, decoder);
        try
        {
            await source.StartControlledAsync("uncooperative", _ => { observations++; return true; }, _ => { }, default);
            camera.Enqueue(1, 1); await decoder.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Reject(() => source.StopAsync(default));
            Check(source.Statistics.BuffersInUse == 1 && decoder.Active == 1 && camera.Lease.Releases == 0, "uncooperative managed decoder cannot cause use-after-return; bounded stop fails with original ownership retained");
            await Reject(() => source.StartControlledAsync("unsafe-new-epoch", _ => true, _ => { }, default));
        }
        finally { release.TrySetResult(); }
        await source.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(3));
        Check(observations == 0 && decoder.Active == 0 && source.Statistics.BuffersInUse == 0 && camera.Lease.Releases == 1, "late cancelled decode is rejected and explicit stop retry releases the original frame once");
        using var racingCamera = new Camera(); var racingDecoder = new ProgrammableDecoder(); racingDecoder.Hang();
        var racing = Source(racingCamera, racingDecoder); await racing.StartControlledAsync("dispose-race", _ => true, _ => { }, default);
        racingCamera.Enqueue(1, 1); await racingDecoder.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.WhenAll(racing.DisposeAsync().AsTask(), racing.StopAsync(default), racing.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(3));
        Check(racingDecoder.Active == 0 && racingCamera.Lease.Releases == 1 && racingDecoder.Disposals == 1, "Dispose racing Stop cancels an in-flight decode without duplicate release or residual work");
    }
    static async Task StateAndTriggerFailures()
    {
        using (var camera = new Camera())
        {
            var decoder = new ProgrammableDecoder { PassSelfTest = false };
            await using var source = Source(camera, decoder); var health = new ConcurrentQueue<DeviceHealthConfirmation>();
            await Reject(() => source.StartControlledAsync("bad-self-test", _ => true, health.Enqueue, default));
            Check(camera.Initialized == 0 && health.All(h => h.Health != DeviceHealth.Ready), "failed synthetic image self-test refuses readiness before acquiring the camera");
        }
        foreach (var scenario in new[] { "exception", "timeout", "source-mismatch", "incomplete", "changed", "extra", "sdk-gap", "free-failure" })
        {
            using var camera = new Camera(); var decoder = new ProgrammableDecoder(); var health = new ConcurrentQueue<DeviceHealthConfirmation>(); int observations = 0;
            var settings = Settings(scenario is "incomplete" or "changed" ? 2 : 1);
            if (scenario == "exception") decoder.Throw();
            if (scenario == "timeout") decoder.Hang();
            if (scenario == "source-mismatch") camera.TriggerSource = 3;
            if (scenario == "free-failure") camera.FailFree = true;
            await using var source = Source(camera, decoder, settings);
            if (scenario == "source-mismatch")
            {
                await Reject(() => source.StartControlledAsync(scenario, _ => true, health.Enqueue, default));
                Check(health.All(h => h.Health != DeviceHealth.Ready), "trigger source mismatch cannot pass the readiness gate"); continue;
            }
            await source.StartControlledAsync(scenario, _ => { observations++; return true; }, health.Enqueue, default);
            camera.Enqueue(1, 1);
            if (scenario is "changed" or "extra" or "sdk-gap")
            {
                await Until(() => camera.Freed == 1 && (scenario == "changed" || observations == 1));
                camera.Enqueue(scenario == "sdk-gap" ? 3u : 2u, scenario == "extra" ? 1u : 2u);
            }
            await Until(() => health.Last().Health == DeviceHealth.Faulted);
            Check(source.ConfirmationSource.Contains("businessReady=false") && !source.ConfirmationSource.Contains("SECRET") &&
                observations == (scenario is "extra" or "sdk-gap" ? 1 : 0) && (scenario != "sdk-gap" || source.Statistics.AcquisitionGaps == 1),
                scenario + ": fault remains latched, partial/late/corrupt input cannot become a new parcel and diagnostics omit decoder data");
            camera.FailFree = false; await source.StopAsync(default);
            Check(source.Statistics.BuffersInUse == 0 && decoder.Active == 0, scenario + ": exception/timeout/failed SDK release returns copied buffers only after the owner ends");
        }
        using var conflictCamera = new Camera(); var conflictDecoder = new ProgrammableDecoder();
        conflictDecoder.Steps.Enqueue((f, _) => ValueTask.FromResult(ProgrammableDecoder.Code(f, "SYNTH-001")));
        conflictDecoder.Steps.Enqueue((f, _) => ValueTask.FromResult(ProgrammableDecoder.Code(f, "SYNTH-002")));
        var results = new ConcurrentQueue<Observation>(); await using var conflict = Source(conflictCamera, conflictDecoder, Settings(2));
        await conflict.StartControlledAsync("conflict", o => { results.Enqueue(o); return true; }, _ => { }, default);
        conflictCamera.Enqueue(1, 1); await Until(() => conflict.Statistics.Decoded == 1); conflictCamera.Enqueue(2, 1); await Until(() => results.Count == 1);
        Check(results.Single().Kind == "NoRead" && results.Single().Codes.Length == 0 && results.Single().CandidateIssue == "AmbiguousCodes", "conflicting frame candidates within one trigger produce one explicit NoRead, never a guessed waybill");
    }
    static async Task EpochFence()
    {
        var source = new EpochSource(); var session = new ObservationSession(source, 12); var original = session.Epoch; int received = 0;
        await session.StartAsync(_ => { received++; return true; }, default); var late = source.Receive!; var health = source.Health!;
        await session.ReconnectAsync(_ => { received++; return true; }, default);
        Check(string.CompareOrdinal(session.Epoch, original) > 0 && !late(new("id", source.DeviceId, original, "event", 1, "entity", "Read", ["synthetic"], null, DateTimeOffset.UtcNow)),
            "controlled reconnect increments epoch and rejects old registration results before application delivery");
        health(new(source.DeviceId, original, DeviceHealth.Ready, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(3), "old"));
        var restarted = new ObservationSession(source, 13);
        Check(received == 0 && session.Health is null && string.CompareOrdinal(restarted.Epoch, session.Epoch) > 0, "new durable runtime generation dominates all prior connections; old health cannot reopen the new epoch");
    }
    static async Task Replay(string root)
    {
        var measured = OfflineReplay.Summarize([new("Code39", "CorrectRead", 1), new("Code39", "NoRead", 2), new("Code39", "WrongRead", 3), new("QR", "Error", 4)]);
        Check(measured.ReadRate == .5 && measured.CorrectReadRate == .25 && measured.WrongReadRate == .25 && measured.ErrorRate == .25 && measured.NoReadRate == .25 &&
            measured.P50Milliseconds == 2 && measured.P95Milliseconds == 4 && measured.P99Milliseconds == 4, "offline denominator includes failures; wrong reads remain separate and nearest-rank percentiles are exact");
        var fixtures = Path.Combine(root, "tests/fixtures/mvs-replay");
        var selector = new WaybillSelector(new("synthetic-v1", 1, 256, "SYNTH-", "", ["Code39", "QR"]));
        IFrameDecoder Factory() => new ProgrammableDecoder { Default = f => f.Pixels.Span[0] switch
        { (byte)'1' => ProgrammableDecoder.Code(f), (byte)'2' => [], (byte)'3' => ProgrammableDecoder.Code(f, "SYNTH-WRONG"), _ => ProgrammableDecoder.Code(f, "SYNTH-004", "QR") } };
        var report = await OfflineReplay.RunAsync(Path.Combine(fixtures, "frames"), OfflineReplay.ReadTruth(Path.Combine(fixtures, "truth.json")), selector, Factory, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        Check(report.Overall.Total == 4 && report.Overall.WrongRead == 1 && report.Overall.NoRead == 1 && report.Overall.CorrectRead == 2 && report.BySymbology["QR"].CorrectRead == 1,
            "Mono8 directory plus independent truth yields expected correct/no-read/wrong-read and per-symbology totals");
    }
    static async Task IsolatedWorker()
    {
        ProcessStartInfo Start(string mode)
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!);
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            info.ArgumentList.Add("--decoder-worker"); info.ArgumentList.Add(mode); return info;
        }
        var normal = new ProcessFrameDecoder(() => Start("normal")); var runner = new DecoderRunner(normal, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2));
        Check(DecoderSelfTest.Matches(await runner.DecodeAsync(DecoderSelfTest.CreateFrame(), default)), "supervised protocol transports pixels, metadata and bounded candidates without a camera or native library");
        await runner.StopAsync();
        var hung = new ProcessFrameDecoder(() => Start("hang")); var hungRunner = new DecoderRunner(hung, TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(2));
        var pending = hungRunner.DecodeAsync(DecoderSelfTest.CreateFrame(), default); await Until(() => hung.WorkerProcessId is not null); int pid = hung.WorkerProcessId!.Value;
        await Reject(() => pending); await hungRunner.StopAsync();
        bool exited; try { using var process = Process.GetProcessById(pid); exited = process.HasExited; } catch (ArgumentException) { exited = true; }
        Check(exited && hungRunner.Idle, "uncooperative isolated decoder is killed and actually reaped within deadline; no child/pipe remains after stop");
    }
    internal static async Task Worker(string mode)
    {
        if (mode == "hang") await Task.Delay(Timeout.InfiniteTimeSpan);
        await using var decoder = new ProgrammableDecoder();
        await ProcessFrameDecoder.RunWorkerAsync(decoder, Console.OpenStandardInput(), Console.OpenStandardOutput(), 33554432, default);
    }
    sealed class EpochSource : IControlledObservationSource
    {
        public string DeviceId => "synthetic"; public string Epoch { get; private set; } = ""; public string ConfirmationSource => "synthetic";
        public Func<Observation, bool>? Receive; public Action<DeviceHealthConfirmation>? Health;
        public Task StartControlledAsync(string epoch, Func<Observation, bool> receive, Action<DeviceHealthConfirmation> health, CancellationToken token) { Epoch = epoch; Receive = receive; Health = health; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken token) => Task.CompletedTask; public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    sealed class Camera : IMvsApi, IDisposable
    {
        readonly nint memory = Marshal.AllocHGlobal(64);
        readonly ConcurrentQueue<(uint Number, uint Trigger)> frames = new();
        public readonly MvsVerification.Lease Lease = new(); public int Freed, Initialized; public bool FailFree; public uint TriggerSource = 2;
        public bool HasFrame { get; private set; }
        public Camera() => Marshal.Copy(Enumerable.Repeat((byte)9, 64).ToArray(), 0, memory, 64);
        public void Enqueue(uint frame, uint trigger) => frames.Enqueue((frame, trigger));
        public MvsFrame Frame(uint number, uint trigger) => new(memory, 8, 8, MvsNative.Mono8, 64, number, trigger, 0, DeviceTimestamp: 1234);
        public void Initialize() => Initialized++; public MvsDevice[] Enumerate() => [new("192.0.2.1", "SYNTHETIC", "SYNTHETIC", "SYNTHETIC", [])];
        public void Create(MvsDevice device) { } public void Open() { }
        public uint EnumValue(string key) => key switch { "PixelFormat" => MvsNative.Mono8, "TriggerMode" => 1, "TriggerSource" => TriggerSource, _ => 0 };
        public long PayloadSize() => 64; public void ConfigureBuffers(uint nodes) { } public void Start() { }
        public MvsFrame? Acquire()
        {
            if (frames.TryDequeue(out var next)) { HasFrame = true; Marshal.Copy(Enumerable.Repeat((byte)9, 64).ToArray(), 0, memory, 64); return Frame(next.Number, next.Trigger); }
            Thread.Sleep(1); return null;
        }
        public void Release() { if (FailFree) throw new IOException("Synthetic release failure"); if (!HasFrame) return; Marshal.Copy(new byte[64], 0, memory, 64); HasFrame = false; Interlocked.Increment(ref Freed); }
        public bool Connected() => true; public void Stop() { } public void Close() { } public void Destroy() { } public void Shutdown() { }
        public void Dispose() => Marshal.FreeHGlobal(memory);
    }
}
