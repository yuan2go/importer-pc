using System.Threading.Channels;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    readonly object overloadSync = new();
    sealed record OverloadWrite(string? Body, string? Acknowledged, TaskCompletionSource? Completion);
    readonly Channel<OverloadWrite> overloadWrites = Channel.CreateBounded<OverloadWrite>(16);
    Task? overloadWorker;
    string? persistedOverloadIdentity;
    int pendingOverloadWrites;
    string OverloadIdentity() => Volatile.Read(ref persistedOverloadIdentity)
        ?? throw new InvalidOperationException("Overload persistence pending/failed; preserve barrier and inspect storage");
    void SignalOverload(string position)
    {
        lock (overloadSync)
        {
            if (!overloadedPositions.TryAdd(position, 0)) return;
            Interlocked.Exchange(ref overload, 1);
            var body = Wire.Encode(new { At = DateTimeOffset.UtcNow, Positions = overloadedPositions.Keys.Order().ToArray(), Reason = "Callback queue full; inspect original entity" });
            Interlocked.Increment(ref pendingOverloadWrites);
            if (!overloadWrites.Writer.TryWrite(new(body, null, null)))
            {
                Interlocked.Decrement(ref pendingOverloadWrites);
                Volatile.Write(ref failure, "Overload persistence owner unavailable; original input custody remains in SQLite");
                Interlocked.Exchange(ref accepting, 0);
            }
        }
    }
    void StartOverloadWriter() => overloadWorker = Task.Run(async () =>
    {
        await foreach (var write in overloadWrites.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (write.Body is { } body)
                {
                    Boundary?.Invoke("BeforeOverloadPersist");
                    ExtensionStore.AtomicWrite(overloadMarker!, System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(body));
                    Volatile.Write(ref persistedOverloadIdentity, Wire.Hash(File.ReadAllText(overloadMarker!)));
                }
                else if (write.Acknowledged == Volatile.Read(ref persistedOverloadIdentity))
                {
                    // This single writer owns writes and removal. New occurrences queued
                    // during deletion remain fenced and are persisted next.
                    File.Delete(overloadMarker!);
                    lock (overloadSync)
                    {
                        if (Volatile.Read(ref pendingOverloadWrites) == 0)
                        { overloadedPositions.Clear(); Interlocked.Exchange(ref overload, 0); Volatile.Write(ref persistedOverloadIdentity, null); }
                    }
                }
                write.Completion?.TrySetResult();
            }
            catch (Exception ex)
            {
                Volatile.Write(ref failure, "Overload custody write failed: " + ex.GetType().Name); Interlocked.Exchange(ref accepting, 0);
                write.Completion?.TrySetException(ex);
            }
            finally { if (write.Body is not null) Interlocked.Decrement(ref pendingOverloadWrites); }
        }
    });
    async Task RemoveAcknowledgedOverload(string identity)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await overloadWrites.Writer.WriteAsync(new(null, identity, completion)).ConfigureAwait(false);
        await completion.Task.ConfigureAwait(false);
    }
    // Startup only, before sources/workers. No business gate or callback lock owns disk I/O.
    void RecoverOverloadAcknowledgement()
    {
        if (overloadMarker is null || !File.Exists(overloadMarker)) return;
        var identity = Wire.Hash(File.ReadAllText(overloadMarker)); persistedOverloadIdentity = identity;
        if (!repository!.HasOverloadAcknowledgement(identity)) return;
        File.Delete(overloadMarker); overloadedPositions.Clear(); Interlocked.Exchange(ref overload, 0); persistedOverloadIdentity = null;
    }
}
