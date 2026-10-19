namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    readonly object callsGate = new();
    readonly HashSet<Task> pendingCalls = [];

    // Some contract implementations perform SQLite/file work before returning a Task.
    // Only that service invocation leaves the UI context. Awaiters still own projection updates.
    public Task<T> ServiceAsync<T>(Func<Task<T>> call) => Own(Task.Run(call));
    public Task ServiceAsync(Func<Task> call) => Own(Task.Run(call));
    Task<T> Own<T>(Task<T> task) { Track(task); return task; }
    Task Own(Task task) { Track(task); return task; }
    void Track(Task task)
    {
        lock (callsGate) pendingCalls.Add(task);
        _ = task.ContinueWith(completed => { lock (callsGate) pendingCalls.Remove(completed); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    // A UI timeout never cancels or forgets the underlying file/DB/command owner.
    public async Task DrainAsync()
    {
        while (true)
        {
            Task[] outstanding;
            lock (callsGate) outstanding = pendingCalls.ToArray();
            if (outstanding.Length == 0) return;
            await Task.WhenAll(outstanding);
        }
    }
}
