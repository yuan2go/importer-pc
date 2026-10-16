using System.Threading.Channels;

namespace Ipc.Platform;

public enum ModuleState { Registered, Initializing, Ready, Stopping, Stopped, Faulted }
public interface IRuntimeModule : IAsyncDisposable
{
    string Id { get; }
    string[] Dependencies { get; }
    Task StartAsync(CancellationToken token);
    Task StopAsync(CancellationToken token);
}
public sealed class ModuleCatalog(IEnumerable<IRuntimeModule> modules) : IAsyncDisposable
{
    readonly IRuntimeModule[] entries = modules.ToArray();
    readonly List<IRuntimeModule> owned = [];
    public Dictionary<string, ModuleState> States { get; } = [];
    public async Task StartAsync(CancellationToken token)
    {
        foreach (var module in entries) States.Add(module.Id, ModuleState.Registered);
        try
        {
            while (owned.Count != entries.Length)
            {
                var next = entries.FirstOrDefault(x => !owned.Contains(x) && x.Dependencies.All(d => States.GetValueOrDefault(d) == ModuleState.Ready))
                    ?? throw new InvalidDataException("Missing/cyclic module dependency");
                owned.Add(next); States[next.Id] = ModuleState.Initializing;
                try { await next.StartAsync(token); States[next.Id] = ModuleState.Ready; }
                catch { States[next.Id] = ModuleState.Faulted; throw; }
            }
        }
        catch { await StopAsync(CancellationToken.None); throw; }
    }
    public async Task StopAsync(CancellationToken token)
    {
        List<Exception> errors = [];
        foreach (var module in owned.AsEnumerable().Reverse())
        {
            States[module.Id] = ModuleState.Stopping;
            try { await module.StopAsync(token); States[module.Id] = ModuleState.Stopped; }
            catch (Exception ex) { States[module.Id] = ModuleState.Faulted; errors.Add(ex); }
        }
        owned.Clear();
        if (errors.Count != 0) throw new AggregateException("Module shutdown failure", errors);
    }
    public async ValueTask DisposeAsync()
    {
        if (owned.Count != 0) await StopAsync(CancellationToken.None);
        foreach (var module in entries.Reverse()) await module.DisposeAsync();
    }
}

// FileShare.None is enforced across processes/sessions. The file is retained: deleting an owned
// lock path would let a second Unix inode bypass the lock. No native device ownership is claimed.
public sealed class ResourceLease : IDisposable
{
    readonly List<FileStream> locks = [];
    public ResourceLease(string root, IEnumerable<string> resources)
    {
        Directory.CreateDirectory(root);
        try
        {
            foreach (var resource in resources.Order())
            {
                var stream = new FileStream(Path.Combine(root, Wire.Hash(resource) + ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                locks.Add(stream);
                stream.SetLength(0); using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write($"pid={Environment.ProcessId}; resource={resource}"); writer.Flush(); stream.Flush(true);
            }
        }
        catch { Dispose(); throw; }
    }
    public void Dispose() { foreach (var stream in locks) stream.Dispose(); locks.Clear(); }
}

public sealed class BoundedExecutor : IAsyncDisposable
{
    sealed record Work(Func<Task> Run, Action<Exception> Fail);
    readonly Channel<Work> queue;
    readonly Task worker;
    readonly Action overload;
    public BoundedExecutor(int capacity, Action onOverload)
    {
        overload = onOverload;
        queue = Channel.CreateBounded<Work>(new BoundedChannelOptions(capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        worker = Task.Run(async () => { await foreach (var w in queue.Reader.ReadAllAsync()) { try { await w.Run(); } catch (Exception ex) { w.Fail(ex); } } });
    }
    public Task<T> Execute<T>(Func<Task<T>> run)
    {
        var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.Writer.TryWrite(new Work(async () => source.SetResult(await run()), ex => source.TrySetException(ex))))
        { overload(); source.SetException(new InvalidOperationException("Command queue full/stopping; Overload latched")); }
        return source.Task;
    }
    public async ValueTask DisposeAsync() { queue.Writer.TryComplete(); await worker; }
}
