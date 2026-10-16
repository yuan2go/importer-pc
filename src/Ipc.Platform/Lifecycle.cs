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
// Ownership remains held while a callback/worker may still use a downstream resource.
public interface IRuntimeOwnership
{
    bool HasActiveWork { get; }
    bool HasUnreleasedResources { get; }
    string[] OwnershipDiagnostics { get; }
}
public sealed class ModuleCatalog(IEnumerable<IRuntimeModule> modules) : IAsyncDisposable
{
    readonly IRuntimeModule[] entries = modules.ToArray();
    readonly List<IRuntimeModule> owned = [];
    readonly HashSet<IRuntimeModule> disposed = [];
    public Dictionary<string, ModuleState> States { get; } = [];
    public bool HasUnreleasedResources => entries.Any(x => !disposed.Contains(x));
    public bool HasActiveWork => owned.Any(x => x is IRuntimeOwnership lifetime ? lifetime.HasActiveWork : States.GetValueOrDefault(x.Id) != ModuleState.Stopped);
    public string[] OwnershipDiagnostics => owned.SelectMany(x => x is IRuntimeOwnership lifetime ? lifetime.OwnershipDiagnostics : [x.Id + ": stop has not completed"]).ToArray();
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
                try { await next.StartAsync(token).ConfigureAwait(false); States[next.Id] = ModuleState.Ready; }
                catch { States[next.Id] = ModuleState.Faulted; throw; }
            }
        }
        catch (Exception startup)
        {
            try { await StopAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException("Module startup failed; shutdown also reported failures", startup, cleanup); }
            throw;
        }
    }
    public async Task StopAsync(CancellationToken token)
    {
        List<Exception> errors = [];
        foreach (var module in owned.AsEnumerable().Reverse())
        {
            if (disposed.Contains(module) || States.GetValueOrDefault(module.Id) == ModuleState.Stopped) continue;
            States[module.Id] = ModuleState.Stopping;
            try { await module.StopAsync(token).ConfigureAwait(false); States[module.Id] = ModuleState.Stopped; }
            catch (Exception ex) { States[module.Id] = ModuleState.Faulted; errors.Add(new InvalidOperationException("Stop: " + module.Id, ex)); }
        }
        if (errors.Count != 0) throw new AggregateException("Module shutdown failures; ownership retained where still active", errors);
    }
    public async ValueTask DisposeAsync()
    {
        List<Exception> errors = [];
        try { await StopAsync(CancellationToken.None).ConfigureAwait(false); } catch (Exception ex) { errors.Add(ex); }
        foreach (var module in entries.Reverse())
        {
            if (disposed.Contains(module)) continue;
            // Ownership-aware modules clean independent resources themselves and retain any
            // still-live native/worker resources. Unknown modules require a successful stop.
            if (owned.Contains(module) && module is not IRuntimeOwnership && States.GetValueOrDefault(module.Id) != ModuleState.Stopped)
            { errors.Add(new InvalidOperationException("Dispose deferred while ownership is uncertain: " + module.Id)); continue; }
            try { await module.DisposeAsync().ConfigureAwait(false); disposed.Add(module); }
            catch (Exception ex)
            {
                // A faulted worker may be terminal and all resources safely released even
                // though its original failure is still reported. Keep that failure visible.
                if (module is IRuntimeOwnership lifetime && !lifetime.HasUnreleasedResources) disposed.Add(module);
                errors.Add(new InvalidOperationException("Dispose: " + module.Id, ex));
            }
        }
        if (errors.Count != 0) throw new AggregateException("Module disposal failures", errors);
    }
}

// FileShare.None covers the same canonical root across processes/login sessions. The file
// is retained; removing a lock pathname could create a second lock identity. This mechanism
// does not prove or replace the device vendor's control-ownership protocol.
public sealed class ResourceLease : IDisposable
{
    readonly List<FileStream> locks = [];
    public ResourceLease(string root, IEnumerable<string> resources)
    {
        Directory.CreateDirectory(root);
        try
        {
            foreach (var resource in resources.Distinct().Order())
            {
                var stream = new FileStream(Path.Combine(root, Wire.Hash(resource) + ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                locks.Add(stream);
                stream.SetLength(0); using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write($"pid={Environment.ProcessId}; resource={resource}"); writer.Flush(); stream.Flush(true);
            }
        }
        catch (Exception acquisition)
        {
            try { Dispose(); } catch (Exception cleanup) { throw new AggregateException("Lease acquisition and partial cleanup failed", acquisition, cleanup); }
            throw;
        }
    }
    public void Dispose()
    {
        List<Exception> errors = [];
        foreach (var stream in locks.ToArray())
            try { stream.Dispose(); locks.Remove(stream); } catch (Exception ex) { errors.Add(ex); }
        if (errors.Count != 0) throw new AggregateException("Lease release failure", errors);
    }
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
