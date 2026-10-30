namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    readonly object readOwnership = new();
    readonly HashSet<Task> readOperations = [];
    readonly HashSet<Task> administrationOperations = [];
    readonly HashSet<Task> managementStatusOperations = [];
    internal Func<CancellationToken, Task>? BeforeRead { get; set; }
    internal Func<CancellationToken, Task>? BeforeManagementStatusRead { get; set; }
    // Reject excess readers instead of building an unbounded Task.Run backlog.
    Task<T> OwnedRead<T>(Func<CancellationToken, Task<T>> read, CancellationToken token = default)
        => OwnedOperation(read, readOperations, 4, token);
    // Reserved authority capacity: full history/log readers must not prevent revocation.
    Task<T> OwnedAdministration<T>(Func<CancellationToken, Task<T>> action)
        => OwnedOperation(action, administrationOperations, 2, default);
    Task<T> OwnedOperation<T>(Func<CancellationToken, Task<T>> read, HashSet<Task> owners, int capacity, CancellationToken token)
    {
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (readOwnership)
        {
            if (stopping.IsCancellationRequested || disposed) throw new InvalidOperationException("运行正在停止，不能开始读取");
            if (owners.Count >= capacity) throw new InvalidOperationException("后台操作并发已达上限，请等待当前操作结束");
            owners.Add(drained.Task);
        }
        return Task.Run(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, stopping.Token);
            linked.CancelAfter(TimeSpan.FromMinutes(2));
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                if (ReferenceEquals(owners, readOperations) && BeforeRead is { } before) await before(linked.Token).ConfigureAwait(false);
                return await read(linked.Token).ConfigureAwait(false);
            }
            finally
            {
                // True task termination owns removal, never timeout/caller cancellation alone.
                lock (readOwnership) { drained.TrySetResult(); owners.Remove(drained.Task); }
            }
        }, CancellationToken.None);
    }
    Task DrainReadOperations(CancellationToken token)
    {
        lock (readOwnership) return Task.WhenAll(readOperations.Concat(administrationOperations).Concat(managementStatusOperations).Concat(operatorInputOperations).Concat(cameraRoundWorkers).ToArray()).WaitAsync(token);
    }
}
