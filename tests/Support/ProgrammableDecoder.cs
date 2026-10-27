using System.Collections.Concurrent;
using Ipc.Platform;
using Ipc.Drivers.Hikrobot.Mvs;

// Test-only source, linked into verification/replay engineering executables.
// Never referenced by a production source project or extension registration.
internal sealed class ProgrammableDecoder : IFrameDecoder
{
    internal readonly ConcurrentQueue<Func<FrameView, CancellationToken, ValueTask<IReadOnlyList<DecodedCode>>>> Steps = new();
    internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Func<FrameView, IReadOnlyList<DecodedCode>>? Default;
    internal bool PassSelfTest = true;
    internal int Calls, Active, Disposals;
    internal static IReadOnlyList<DecodedCode> Code(FrameView frame, string value = "SYNTH-001", string type = "Code39") => [new(value, type, new(0, 0, frame.Width, frame.Height), .9)];
    public async ValueTask<IReadOnlyList<DecodedCode>> DecodeAsync(FrameView frame, CancellationToken token)
    {
        Interlocked.Increment(ref Active);
        try
        {
            if (frame.FrameNumber == 0 && PassSelfTest) return Code(frame, DecoderSelfTest.ExpectedText, DecoderSelfTest.ExpectedSymbology);
            Interlocked.Increment(ref Calls); Entered.TrySetResult();
            if (Steps.TryDequeue(out var step)) return await step(frame, token);
            token.ThrowIfCancellationRequested(); return Default?.Invoke(frame) ?? Code(frame);
        }
        finally { Interlocked.Decrement(ref Active); }
    }
    internal void Delay(TimeSpan duration) => Steps.Enqueue(async (frame, token) => { await Task.Delay(duration, token); return Code(frame); });
    internal void Hang() => Delay(Timeout.InfiniteTimeSpan);
    internal void Throw() => Steps.Enqueue((_, _) => throw new InvalidOperationException("SYNTHETIC-BARCODE-SECRET SERIAL-SECRET"));
    public ValueTask DisposeAsync() { if (Active != 0) throw new InvalidOperationException("Disposed while decoder owns a view"); Interlocked.Increment(ref Disposals); return ValueTask.CompletedTask; }
}
