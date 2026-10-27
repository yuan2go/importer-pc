using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot.Mvs;

// One caller at a time. Keep the actual task so timeout never releases its view.
public sealed class DecoderRunner(IFrameDecoder decoder, TimeSpan timeout, TimeSpan stopTimeout)
{
    Task<IReadOnlyList<DecodedCode>>? pending;
    CancellationTokenSource? cancellation;
    Task? cancellationDispatch, disposal;
    public bool Idle => pending is null || pending.IsCompleted;
    public async Task<IReadOnlyList<DecodedCode>> DecodeAsync(FrameView frame, CancellationToken token)
    {
        if (!Idle) throw new InvalidOperationException("Previous decoder call still owns its frame");
        if (cancellationDispatch is { IsCompleted: false }) throw new InvalidOperationException("Previous cancellation still has an owner");
        cancellation?.Dispose(); cancellation = CancellationTokenSource.CreateLinkedTokenSource(token); cancellationDispatch = null;
        // Cooperative implementations must return their task promptly. Potentially
        // blocking implementations must use ProcessFrameDecoder.
        try { pending = decoder.DecodeAsync(frame, cancellation.Token).AsTask(); }
        catch { throw new IOException("Decoder invocation failed (details omitted)"); }
        try
        {
            var result = await pending.WaitAsync(timeout, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            WaybillSelector.ValidateResult(frame, result);
            return result;
        }
        catch (Exception) when (token.IsCancellationRequested || !pending.IsCompleted)
        {
            cancellationDispatch ??= cancellation.CancelAsync();
            await DrainAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            throw new TimeoutException("Decoder deadline exceeded");
        }
        catch { throw new IOException("Decoder result failed (details omitted)"); }
    }
    async Task DrainAsync()
    {
        using var budget = new CancellationTokenSource(stopTimeout);
        if (!Idle && decoder is IAbortableFrameDecoder abortable)
            await abortable.AbortAsync(budget.Token).WaitAsync(budget.Token).ConfigureAwait(false);
        if (pending is not null)
        {
            try { await pending.WaitAsync(budget.Token).ConfigureAwait(false); }
            catch (Exception) when (pending.IsCompleted) { _ = pending.Exception; }
        }
        if (cancellationDispatch is not null) await cancellationDispatch.WaitAsync(budget.Token).ConfigureAwait(false);
    }
    public async Task StopAsync()
    {
        if (cancellation is not null) cancellationDispatch ??= cancellation.CancelAsync();
        await DrainAsync().ConfigureAwait(false);
        try
        {
            if (disposal is null || disposal.IsFaulted || disposal.IsCanceled) disposal = decoder.DisposeAsync().AsTask();
            await disposal.WaitAsync(stopTimeout).ConfigureAwait(false);
        }
        catch { throw new IOException("Decoder disposal incomplete; original owner retained (details omitted)"); }
        cancellation?.Dispose(); cancellation = null;
    }
}

public static class DecoderSelfTest
{
    public const string ExpectedText = "123", ExpectedSymbology = "Code39";
    // Code39 *123*, 3:1 wide:narrow, 10-module quiet zones. Symbol bit
    // patterns cross-checked against ZXing's Code39Reader character table.
    // Only format constants are used; no library or decoder implementation copied.
    public static FrameView CreateFrame()
    {
        const int width = 258, height = 64;
        var pixels = new byte[width * height]; pixels.AsSpan().Fill(255);
        int x = 20;
        foreach (var symbol in new[] { 0x094, 0x121, 0x061, 0x160, 0x094 })
        {
            for (var bit = 8; bit >= 0; bit--)
            {
                int run = (symbol & (1 << bit)) == 0 ? 2 : 6;
                if (bit % 2 == 0)
                    for (var y = 8; y < height - 8; y++) pixels.AsSpan(y * width + x, run).Clear();
                x += run;
            }
            x += 2;
        }
        return new(pixels, width, height, width, FramePixelFormat.Mono8, 0, 0);
    }
    public static bool Matches(IReadOnlyList<DecodedCode> codes) => codes.Count == 1 && codes[0].Text == ExpectedText && codes[0].Symbology == ExpectedSymbology;
}
