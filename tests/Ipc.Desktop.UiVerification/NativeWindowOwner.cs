using System.Windows;
using Ipc.Application.Sportmaster.Desktop;

// Keep the test's runtime alive until its windows have drained pending reads.
internal sealed class NativeWindowOwner : IAsyncDisposable
{
    readonly Window window;
    readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal NativeWindowOwner(Window window)
    {
        this.window = window;
        window.Closed += (_, _) => closed.TrySetResult();
    }
    public async ValueTask DisposeAsync()
    {
        if (closed.Task.IsCompleted) return;
        if (window is SupplyStationWindow station) { station.ConfirmDiscardDrafts = _ => true; station.ConfirmOwnerExit = _ => true; }
        if (window is Ipc.Application.ParcelFlow.Desktop.ShellWindow shell) { shell.ConfirmDiscardDrafts = _ => true; shell.ConfirmOwnerExit = _ => true; }
        window.Close();
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!closed.Task.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(12))
        {
            await Task.WhenAny(closed.Task, Task.Delay(100));
            if (!closed.Task.IsCompleted && window.IsEnabled) window.Close();
        }
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }
}

internal sealed class NativeWindowOwners : IAsyncDisposable
{
    readonly List<NativeWindowOwner> owners = [];
    internal T Track<T>(T window) where T : Window { owners.Add(new(window)); return window; }
    public async ValueTask DisposeAsync()
    {
        List<Exception> failures = [];
        foreach (var owner in owners.AsEnumerable().Reverse())
            try { await owner.DisposeAsync(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Native fixture window cleanup failed", failures);
    }
}
