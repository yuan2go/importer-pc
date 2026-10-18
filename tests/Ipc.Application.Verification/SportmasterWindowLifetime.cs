using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;
using System.Windows.Threading;

static class SportmasterWindowLifetime
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(SupplyStationWindow window, string name, Task task) => typeof(SupplyStationWindow).GetField(name, Fields)!.SetValue(window, task);
    static DispatcherTimer Timer(SupplyStationWindow window) => (DispatcherTimer)typeof(SupplyStationWindow).GetField("timer", Fields)!.GetValue(window)!;
    static void Check(bool condition, string reason)
    {
        if (!condition) throw new Exception("FAIL WINDOW-CONTROL: " + reason);
        Console.WriteLine("PASS WINDOW-CONTROL: " + reason);
    }
    static async Task Until(Func<bool> predicate, int milliseconds = 1500)
    {
        using var timeout = new CancellationTokenSource(milliseconds);
        while (!predicate()) await Task.Delay(5, timeout.Token);
    }
    static void Show(SupplyStationWindow window) { window.Show(); Timer(window).Start(); }
    public static async Task Run()
    {
        var app = DispatchProxy.Create<IParcelApplication, NoWindowBusinessCalls>();
        foreach (var field in new[] { "pendingRefresh", "pendingViewportPause" })
        {
            var stops = 0;
            var root = new SupplyStationWindow(app, null, null, () => ++stops == 1 ? Task.FromException(new IOException("SYNTHETIC stop failure")) : Task.CompletedTask);
            var child = new SupplyStationWindow(app, null, null, owner: root); Show(root); Show(child);
            var pending = new TaskCompletionSource(); Set(child, field, pending.Task);
            child.Close(); root.Close();
            Check(stops == 0 && !root.IsEnabled && !child.IsEnabled, field + " drains before owner stop");
            pending.SetResult(); await Until(() => stops == 1 && root.IsEnabled);
            Check(root.IsVisible && !child.IsVisible && !Timer(child).IsEnabled, field + " owner failure cannot restart closed child timer");
            var reads = ((NoWindowBusinessCalls)(object)app).Calls; Timer(child).Fire();
            Check(((NoWindowBusinessCalls)(object)app).Calls == reads, field + " closed child cannot poll");
            root.Close(); await Until(() => !root.IsVisible);
            Check(stops == 2 && !Timer(root).IsEnabled && !Timer(child).IsEnabled, field + " successful retry leaves no orphan timer");
        }
        {
            var stops = 0; var root = new SupplyStationWindow(app, null, null, () => { stops++; return Task.CompletedTask; });
            var child = new SupplyStationWindow(app, null, null, owner: root); Show(root); Show(child);
            child.Close(); await Until(() => !child.IsVisible);
            Check(stops == 0 && root.IsVisible && !Timer(child).IsEnabled, "auxiliary close does not stop owner");
            root.Close(); await Until(() => !root.IsVisible); Check(stops == 1, "owner stops exactly once after ordinary auxiliary close");
        }
        {
            var stops = 0; var root = new SupplyStationWindow(app, null, null, () => { stops++; return Task.CompletedTask; });
            var child = new SupplyStationWindow(app, null, null, owner: root); Show(root); Show(child);
            var rootRead = new TaskCompletionSource(); var childPause = new TaskCompletionSource();
            Set(root, "pendingRefresh", rootRead.Task); Set(child, "pendingViewportPause", childPause.Task);
            child.Close(); root.Close(); root.Close();
            childPause.SetException(new IOException("SYNTHETIC child pause failure"));
            await Until(() => child.ViewModel.Message.Contains("SYNTHETIC"));
            Check(!child.IsEnabled && !Timer(child).IsEnabled && stops == 0, "child failure cannot unfreeze a root close still draining");
            rootRead.SetResult(); await Until(() => root.IsEnabled);
            Check(root.IsVisible && child.IsVisible && stops == 0, "failed drain retains both windows and prevents premature owner stop");
            Set(child, "pendingViewportPause", Task.CompletedTask); root.Close(); await Until(() => !root.IsVisible);
            Check(stops == 1 && !child.IsVisible && !Timer(child).IsEnabled, "retained windows can retry after original failure is resolved");
        }
        {
            var stops = 0; var root = new SupplyStationWindow(app, null, null, () => { stops++; return Task.CompletedTask; }); Show(root);
            var read = new TaskCompletionSource(); Set(root, "pendingRefresh", read.Task); root.Close();
            await Until(() => root.IsEnabled, 6500);
            Check(root.IsVisible && stops == 0 && root.ViewModel.Message.Contains("仍持有资源"), "five-second drain timeout keeps owner window and resources");
            read.SetResult(); root.Close(); await Until(() => !root.IsVisible);
            Check(stops == 1 && !Timer(root).IsEnabled, "timed-out close retries without duplicate owner stop");
        }
        Check(((NoWindowBusinessCalls)(object)app).Calls == 0, "window close races issue no business or device command");
        Console.WriteLine("PASS WINDOW-CONTROL COMPLETE; exact window source with event/timer substitutes; Windows native NOT_RUN");
    }
}

public class NoWindowBusinessCalls : DispatchProxy
{
    public int Calls;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    { Calls++; throw new InvalidOperationException("Unexpected business call from window close: " + method!.Name); }
}
