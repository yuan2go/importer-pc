namespace Ipc.Desktop;

// The page supplies all effects, including error and busy cleanup. A superseded load
// owns none of them. Used by actual package/device/service selection event handlers.
public sealed class SelectionLoad
{
    long generation;
    public void Invalidate() => generation++;
    public async Task Run<T>(Func<Task<T>> read, Action<T> apply, Action<Exception> fail, Action settled)
    {
        var ticket = ++generation;
        try { var result = await read(); if (ticket == generation) apply(result); }
        catch (Exception ex) { if (ticket == generation) fail(ex); }
        finally { if (ticket == generation) settled(); }
    }
}
