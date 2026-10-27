using System.Runtime.InteropServices;
using Ipc.Reader.Core;

var checks = 0;
void Check(bool value, string risk)
{ if (!value) throw new InvalidOperationException(risk); checks++; Console.WriteLine("PASS " + risk); }
void Reject(Action action)
{
    try { action(); }
    catch (Exception ex) when (ex is InvalidDataException or OperationCanceledException or InvalidOperationException or TimeoutException) { return; }
    throw new Exception("Expected explicit rejection");
}
var budget = new BufferBudget(3, 1, 64, 1, 2097152);
Reject(() => new FramePool(budget with { MaximumResidentBytes = 64 }));
Reject(() => new FramePool(budget with { QueueCapacity = 2 }));
var pool = new FramePool(budget);
var native = Marshal.AllocHGlobal(64);
try
{
    Marshal.Copy(Enumerable.Range(0, 64).Select(i => (byte)i).ToArray(), 0, native, 64);
    CaptureFrame Capture(ulong frame = 1, ulong trigger = 1) => new(native, 8, 8, 8, FramePixelFormat.Mono8, frame, 12, trigger, 64);
    var leases = Enumerable.Range(0, 3).Select(_ => pool.TryCopy(Capture(), "boot")!).ToArray();
    Check(pool.TryCopy(Capture(), "boot") is null && pool.InUse == 3, "fixed pool cannot grow when exhausted");
    foreach (var lease in leases) { lease.Dispose(); lease.Dispose(); }
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    Reject(() => pool.TryCopy(Capture(), "boot", cancelled.Token));
    Reject(() => pool.TryCopy(Capture() with { Length = 65 }, "boot"));
    Check(pool.InUse == 0, "rejection, cancellation and repeated release cannot leak a slot");
    using var owned = pool.TryCopy(Capture(), "boot")!;
    Marshal.WriteByte(native, 0, 200);
    Check(owned.View.Pixels.Span[0] == 0 && owned.View.FrameNumber == 1 && owned.View.DeviceTimestamp == 12,
        "copy owns its bytes after SDK buffer reuse and preserves source metadata");

    var selector = new WaybillSelector(new("rule-v1", 3, 10, "00", "[0-9]+", ["Code128"]));
    DecodedCode Code(string text) => new(text, "Code128", new(0, 0, 2, 2), .5);
    Check(selector.Select(owned.View, [Code("001"), Code("002")]).Issue == "AmbiguousCodes" &&
          selector.Select(owned.View, [Code("123")]).Text is null,
          "multiple matching candidates and unmatched candidates cannot become a guessed waybill");
    Reject(() => selector.Select(owned.View, [Code("00\ufeff1")]));
    Reject(() => new WaybillSelector(new("rule-v1", 1, 10, "", "(?=a)a", ["Code128"])));
    Check(true, "invalid candidate controls and unsupported backtracking rules fail closed");

    var groups = new TriggerAssembler("boot", 2, TimeSpan.FromSeconds(5), selector);
    using var first = pool.TryCopy(Capture(1, 7), "boot")!;
    Check(groups.Add(first, [Code("001")]) is null, "partial trigger group cannot publish an entity");
    first.Dispose();
    using var second = pool.TryCopy(Capture(2, 7), "boot")!;
    Check(groups.Add(second, [Code("001")]) is { Trigger: 7, Selection.Text: "001" }, "fixed multi-frame physical trigger produces one complete result");
    second.Dispose();
    using var third = pool.TryCopy(Capture(3, 8), "boot")!;
    groups.Add(third, [Code("001")]); third.Dispose();
    using var fourth = pool.TryCopy(Capture(4, 8), "boot")!;
    Check(groups.Add(fourth, [Code("002")])?.Selection.Issue == "AmbiguousCodes", "cross-frame conflict cannot choose a convenient barcode");
    fourth.Dispose();
    using var stale = pool.TryCopy(Capture(5, 9), "old-boot")!;
    Reject(() => groups.Add(stale, [Code("001")])); stale.Dispose();
    using var extra = pool.TryCopy(Capture(5, 8), "boot")!;
    var one = new TriggerAssembler("boot", 1, TimeSpan.FromSeconds(5), selector);
    one.Add(extra, []); extra.Dispose();
    using var repeated = pool.TryCopy(Capture(6, 8), "boot")!;
    Reject(() => one.Add(repeated, [])); repeated.Dispose();
    Check(true, "old startup results and a repeated physical trigger cannot create a new entity");

    var release = new TaskCompletionSource<IReadOnlyList<DecodedCode>>(TaskCreationOptions.RunContinuationsAsynchronously);
    var decoder = new ProgrammableDecoder((_, _) => new(release.Task));
    var runner = new DecoderRunner(decoder, TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(30));
    var task = runner.DecodeAsync(owned.View, default);
    try { await task; throw new Exception("Expected timeout"); } catch (OperationCanceledException) { }
    Check(!runner.Idle, "deadline cannot pretend a cancellation-ignoring decoder released its view");
    release.SetResult([]); await runner.StopAsync();
    Check(runner.Idle && decoder.Disposals == 1, "actual decoder completion permits one cleanup");
}
finally { Marshal.FreeHGlobal(native); }
Check(pool.InUse == 0, "all frame ownership returns after cancellation, trigger rejection and decode timeout");
var complete = new ReadinessEvidence(true, true, true, true, true, true, true);
var readiness = new ReadinessGate("current"); readiness.BeginSelfTest();
Check(readiness.Confirm("current", complete), "business requires every independently supplied readiness gate");
Check(!readiness.Confirm("current", complete with { LicenseValid = false }) &&
      !readiness.Confirm("current", complete), "license loss latches failure and later success cannot silently resume business");
var staleGate = new ReadinessGate("current"); staleGate.BeginSelfTest();
Check(!staleGate.Confirm("old", complete), "old boot readiness cannot enable a restarted service");
foreach (var evidence in new[] { complete with { VmVersionMatches = false }, complete with { SolutionHashMatches = false },
    complete with { NodesMatch = false }, complete with { TriggerMatches = false }, complete with { SelectionValid = false }, complete with { SelfTestPassed = false } })
{
    var gate = new ReadinessGate("current"); gate.BeginSelfTest();
    if (gate.Confirm("current", evidence)) throw new Exception("Incomplete readiness accepted");
}
Check(true, "version, solution, node, trigger, selection and self-test failures each close readiness");

var pointer = Marshal.AllocHGlobal(64);
try
{
    Marshal.Copy(new byte[64], 0, pointer, 64);
    CaptureFrame Frame(ulong n) => new(pointer, 8, 8, 8, FramePixelFormat.Mono8, n, 0, n, 64);
    var selector = new WaybillSelector(new("rule-v1", 1, 10, "", "", ["Code128"]));
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var slow = new ProgrammableDecoder(async (_, token) => { entered.TrySetResult(); try { await Task.Delay(Timeout.Infinite, token); } finally { released.TrySetResult(); } return []; });
    var pipeline = new DecodePipeline("boot", budget, slow, selector, 1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(1), _ => throw new Exception("No result expected"), () => { });
    pipeline.Start(); pipeline.Submit(pipeline.Copy(Frame(1), "boot")!); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
    pipeline.Submit(pipeline.Copy(Frame(2), "boot")!);
    pipeline.Submit(pipeline.Copy(Frame(3), "boot")!);
    Check(pipeline.Faulted && pipeline.QueueOverloads == 1 && pipeline.FrameGaps == 0,
        "bounded queue overflow is visible and classified separately from SDK frame loss");
    await Task.WhenAll(pipeline.StopAsync(), pipeline.StopAsync());
    Check(released.Task.IsCompleted && pipeline.BuffersInUse == 0 && slow.Disposals == 1,
        "concurrent stop drains in-flight cancellation and returns each queue slot once");

    var gap = new DecodePipeline("boot", budget, new ProgrammableDecoder((_, _) => new(Array.Empty<DecodedCode>())),
        selector, 1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), _ => true, () => { });
    gap.Start(); var firstCopy = gap.Copy(Frame(1), "boot")!;
    Check(gap.Copy(Frame(3), "boot") is null && gap.FrameGaps == 1 && gap.QueueOverloads == 0,
        "SDK frame discontinuity faults before another frame can enter the decode queue");
    gap.RejectCopy(firstCopy); await gap.StopAsync();
    Check(gap.BuffersInUse == 0, "SDK Free failure rejects its copied frame without leaking ownership");

    var pending = new DecodePipeline("boot", budget, new ProgrammableDecoder((_, _) => new(Array.Empty<DecodedCode>())),
        selector, 1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), _ => true, () => { });
    pending.Start(); var loan = pending.Copy(Frame(1), "boot")!;
    try { await pending.StopAsync(); throw new Exception("False stop"); } catch (InvalidOperationException) { }
    Check(pending.BuffersInUse == 1, "stop cannot release a copied frame still held by the SDK-return path");
    pending.RejectCopy(loan); await pending.StopAsync();
    Check(pending.BuffersInUse == 0, "actual capture completion permits explicit stop retry");
}
finally { Marshal.FreeHGlobal(pointer); }
var rates = OfflineReplay.Summarize([
    new("Code128", "CorrectRead", 1), new("Code128", "NoRead", 2),
    new("Code128", "WrongRead", 3), new("<none>", "WrongRead", 4), new("Code128", "Error", 5)]);
Check(rates.Total == 5 && rates.WrongReadRate == .4 && rates.NoReadRate == .2 && rates.ErrorRate == .2 &&
      rates.ReadRate == .6 && rates.CorrectReadRate == .2 && rates.P50Milliseconds == 3 && rates.P95Milliseconds == 5 && rates.P99Milliseconds == 5,
      "wrong reads and errors stay in the denominator and nearest-rank percentiles include every attempt");
Console.WriteLine($"PASS {checks} reader core risk checks");

sealed class ProgrammableDecoder(Func<FrameView, CancellationToken, ValueTask<IReadOnlyList<DecodedCode>>> run) : IFrameDecoder
{
    public int Disposals { get; private set; }
    public ValueTask<IReadOnlyList<DecodedCode>> DecodeAsync(FrameView frame, CancellationToken cancellationToken) => run(frame, cancellationToken);
    public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
}
