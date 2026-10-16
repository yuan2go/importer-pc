using System.Threading.Channels;

namespace Ipc.Platform;

// One owned background consumer. Reserved control capacity cannot be filled by ordinary
// commands/reads. No caller executes storage work inline, including uncontended calls.
public sealed class QueueAdmissionException(string message) : InvalidOperationException(message);

public sealed class StationDispatcher : IAsyncDisposable
{
    sealed record Work(Func<Task> Run, Action<Exception> Fail);
    readonly Channel<Work> ordinary, control;
    readonly Task worker;
    public StationDispatcher(int capacity, int controlCapacity = 16)
    {
        ordinary = Channel.CreateBounded<Work>(new BoundedChannelOptions(capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        control = Channel.CreateBounded<Work>(new BoundedChannelOptions(controlCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        worker = Task.Run(Consume);
    }
    async Task Consume()
    {
        // At most one waiter per channel; consuming does not accumulate abandoned WaitToRead tasks.
        Task<bool>? normalReady = null, controlReady = null;
        bool normalDone = false, controlDone = false;
        while (!normalDone || !controlDone)
        {
            Work? item = null;
            if (control.Reader.TryRead(out var urgent)) item = urgent;
            else if (ordinary.Reader.TryRead(out var normal)) item = normal;
            if (item is not null)
            {
                try { await item.Run().ConfigureAwait(false); }
                catch (Exception ex) { item.Fail(ex); }
                continue;
            }
            normalReady ??= ordinary.Reader.WaitToReadAsync().AsTask();
            controlReady ??= control.Reader.WaitToReadAsync().AsTask();
            var pending = new List<Task<bool>>();
            if (!normalDone) pending.Add(normalReady);
            if (!controlDone) pending.Add(controlReady);
            if (pending.Count == 0) break;
            await Task.WhenAny(pending).ConfigureAwait(false);
            if (normalReady.IsCompleted) { normalDone = !await normalReady.ConfigureAwait(false); normalReady = null; }
            if (controlReady.IsCompleted) { controlDone = !await controlReady.ConfigureAwait(false); controlReady = null; }
        }
    }
    public Task<T> Invoke<T>(Func<Task<T>> action, bool priority = false, bool waitForCapacity = false)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = new Work(async () => completion.TrySetResult(await action().ConfigureAwait(false)), ex => completion.TrySetException(ex));
        var writer = priority ? control.Writer : ordinary.Writer;
        if (writer.TryWrite(work)) return completion.Task;
        if (!waitForCapacity) return Task.FromException<T>(new QueueAdmissionException("Background queue full or stopping; input not admitted"));
        return Enqueue();
        async Task<T> Enqueue() { await writer.WriteAsync(work).ConfigureAwait(false); return await completion.Task.ConfigureAwait(false); }
    }
    public Task Invoke(Func<Task> action, bool priority = false, bool waitForCapacity = true) => Invoke(async () => { await action().ConfigureAwait(false); return true; }, priority, waitForCapacity);
    public async ValueTask DisposeAsync()
    { ordinary.Writer.TryComplete(); control.Writer.TryComplete(); await worker.ConfigureAwait(false); }
}
