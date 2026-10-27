using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot.Mvs;

// Isolated native-adapter transport, not a vendor binding or a code discovery
// mechanism. The composition root owns qualification of the supplied executable.
public sealed class ProcessFrameDecoder(Func<ProcessStartInfo> startInfo, int maximumPayload = 33554432) : IAbortableFrameDecoder
{
    const int Protocol = 1, MaximumResultBytes = 262144;
    readonly SemaphoreSlim serial = new(1, 1);
    readonly object gate = new();
    Process? process;
    Task? aborting, stderrDrain;
    bool retired;
    long sequence;
    internal int? WorkerProcessId { get; private set; }
    sealed record Request(int Protocol, long Sequence, int Width, int Height, int Stride, FramePixelFormat PixelFormat, ulong FrameNumber, ulong DeviceTimestamp, int Length);
    sealed record Response(int Protocol, long Sequence, DecodedCode[] Codes);
    public async ValueTask<IReadOnlyList<DecodedCode>> DecodeAsync(FrameView frame, CancellationToken token)
    {
        if (maximumPayload is < 1 or > 33554432) throw new InvalidDataException("Invalid process frame budget");
        ValidateFrame(frame, maximumPayload);
        await serial.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Process current;
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(retired, this);
                if (process is null)
                {
                    var info = startInfo();
                    info.UseShellExecute = false; info.CreateNoWindow = true;
                    info.RedirectStandardInput = info.RedirectStandardOutput = true;
                    // Do not pipe unbounded vendor stderr or expose exception text.
                    info.RedirectStandardError = true;
                    process = Process.Start(info) ?? throw new IOException("Decoder worker did not start");
                    WorkerProcessId = process.Id;
                    stderrDrain = DiscardDiagnostics(process.StandardError);
                }
                current = process;
            }
            var id = checked(++sequence);
            var request = new Request(Protocol, id, frame.Width, frame.Height, frame.Stride, frame.PixelFormat, frame.FrameNumber, frame.DeviceTimestamp, frame.Pixels.Length);
            await WriteMessage(current.StandardInput.BaseStream, JsonSerializer.SerializeToUtf8Bytes(request), token).ConfigureAwait(false);
            await current.StandardInput.BaseStream.WriteAsync(frame.Pixels, token).ConfigureAwait(false);
            await current.StandardInput.BaseStream.FlushAsync(token).ConfigureAwait(false);
            var raw = await ReadMessage(current.StandardOutput.BaseStream, MaximumResultBytes, token).ConfigureAwait(false);
            var response = JsonSerializer.Deserialize<Response>(raw) ?? throw new InvalidDataException("Missing decoder response");
            if (response.Protocol != Protocol || response.Sequence != id) throw new InvalidDataException("Decoder protocol/correlation mismatch");
            WaybillSelector.ValidateResult(frame, response.Codes);
            return response.Codes;
        }
        catch
        {
            using var termination = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await AbortAsync(termination.Token).ConfigureAwait(false);
            throw new IOException("Decoder worker failed or was cancelled (details omitted)");
        }
        finally { serial.Release(); }
    }
    public Task AbortAsync(CancellationToken token)
    {
        Task owned;
        lock (gate)
        {
            retired = true;
            if (aborting is null || aborting.IsFaulted || aborting.IsCanceled) aborting = TerminateAsync();
            owned = aborting;
        }
        return owned.WaitAsync(token);
    }
    async Task TerminateAsync()
    {
        await Task.Yield();
        var current = process;
        if (current is null) return;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        if (!current.HasExited) current.Kill(entireProcessTree: true);
        await current.WaitForExitAsync(budget.Token).ConfigureAwait(false);
        // Exit is established before stream/handle release. If kill/wait fails,
        // retain the process handle for explicit cleanup retry.
        if (stderrDrain is not null) await stderrDrain.WaitAsync(budget.Token).ConfigureAwait(false);
        current.StandardInput.Dispose(); current.StandardOutput.Dispose(); current.StandardError.Dispose(); current.Dispose();
        process = null;
    }
    static async Task DiscardDiagnostics(StreamReader reader)
    {
        var buffer = new char[1024];
        while (await reader.ReadAsync(buffer).ConfigureAwait(false) != 0) { }
    }
    public ValueTask DisposeAsync() => new(AbortAsync(CancellationToken.None));

    // Vendor worker entry points call this after their own NativeSdkBoundary and
    // deployment checks. stdout is reserved for this bounded versioned protocol.
    public static async Task RunWorkerAsync(IFrameDecoder decoder, Stream input, Stream output, int maximumPayload, CancellationToken token)
    {
        if (maximumPayload is < 1 or > 33554432) throw new InvalidDataException("Invalid worker frame budget");
        var pixels = GC.AllocateUninitializedArray<byte>(maximumPayload, pinned: true);
        long prior = 0;
        while (!token.IsCancellationRequested)
        {
            byte[] raw;
            try { raw = await ReadMessage(input, 4096, token).ConfigureAwait(false); }
            catch (EndOfStreamException) { return; }
            var request = JsonSerializer.Deserialize<Request>(raw) ?? throw new InvalidDataException("Missing decoder request");
            if (request.Protocol != Protocol || request.Sequence != checked(prior + 1) || request.Length < 1 || request.Length > maximumPayload)
                throw new InvalidDataException("Decoder request budget/protocol/correlation mismatch");
            prior = request.Sequence;
            var frame = new FrameView(pixels.AsMemory(0, request.Length), request.Width, request.Height, request.Stride, request.PixelFormat, request.FrameNumber, request.DeviceTimestamp);
            ValidateFrame(frame, maximumPayload);
            await input.ReadExactlyAsync(pixels.AsMemory(0, request.Length), token).ConfigureAwait(false);
            try
            {
                var codes = await decoder.DecodeAsync(frame, token).ConfigureAwait(false);
                WaybillSelector.ValidateResult(frame, codes);
                var reply = JsonSerializer.SerializeToUtf8Bytes(new Response(Protocol, prior, codes.ToArray()));
                if (reply.Length > MaximumResultBytes) throw new InvalidDataException("Decoder response byte budget exceeded");
                await WriteMessage(output, reply, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            finally { pixels.AsSpan(0, request.Length).Clear(); }
        }
    }
    public static void ValidateFrame(FrameView frame, int maximumPayload)
    {
        if (frame.PixelFormat != FramePixelFormat.Mono8 || frame.Width < 1 || frame.Height < 1 || frame.Stride < frame.Width ||
            (long)frame.Stride * frame.Height != frame.Pixels.Length || frame.Pixels.Length > maximumPayload)
            throw new InvalidDataException("Invalid Mono8 frame layout/budget");
    }
    static async Task WriteMessage(Stream stream, byte[] bytes, CancellationToken token)
    {
        var size = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(size, bytes.Length);
        await stream.WriteAsync(size, token).ConfigureAwait(false);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
    }
    static async Task<byte[]> ReadMessage(Stream stream, int maximum, CancellationToken token)
    {
        var size = new byte[4]; await stream.ReadExactlyAsync(size, token).ConfigureAwait(false);
        var count = BinaryPrimitives.ReadInt32LittleEndian(size);
        if (count < 1 || count > maximum) throw new InvalidDataException("Decoder message byte budget exceeded");
        var bytes = new byte[count]; await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false); return bytes;
    }
}
