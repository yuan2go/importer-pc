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
        var app = DispatchProxy.Create<IManagementParcelFixture, NoWindowBusinessCalls>();
        {
            var stops = 0; var root = new SupplyStationWindow(app, null, null, () => { stops++; return Task.CompletedTask; });
            var child = new SupplyStationWindow(app, null, null, owner: root); Show(root); Show(child);
            child.ViewModel.ConfigurationDraft = "SYNTHETIC uncommitted draft";
            root.Close(); await Task.Delay(30);
            Check(root.IsVisible && child.IsVisible && stops == 0 && child.ViewModel.ConfigurationDraft == "SYNTHETIC uncommitted draft",
                "owner close defaults to retaining an auxiliary unsaved draft before stopping the Application");
            Check(root.IsEnabled && child.IsEnabled && Timer(root).IsEnabled && Timer(child).IsEnabled, "cancelling owner close resumes all surviving windows");
            var decisions = 0;
            child.ConfirmDiscardDrafts = count => { decisions++; return false; };
            child.Close(); await Until(() => child.IsEnabled);
            Check(decisions == 1 && child.IsVisible && stops == 0, "auxiliary close can cancel without changing owner lifetime");
            var rootWait = new TaskCompletionSource(); Set(root, "pendingRefresh", rootWait.Task);
            root.ConfirmDiscardDrafts = count => { decisions++; Check(count == 1, "owner decision includes the auxiliary draft exactly once"); return false; };
            var childWait = new TaskCompletionSource(); Set(child, "pendingRefresh", childWait.Task);
            child.Close(); root.Close(); childWait.SetResult(); await Task.Delay(30);
            Check(!child.IsEnabled && stops == 0 && decisions == 1, "overlapping auxiliary close hands its dirty draft to the still-draining root without an independent decision");
            rootWait.SetResult(); await Until(() => root.IsEnabled);
            Check(decisions == 2 && child.IsVisible && child.IsEnabled && stops == 0, "owner cancellation retains every overlapping draft");
            root.ConfirmDiscardDrafts = count => true; root.Close(); await Until(() => !root.IsVisible);
            Check(stops == 1 && !child.IsVisible, "explicit owner discard closes the entire group and stops once");
        }
        {
            var stops = 0; var confirmations = 0;
            var root = new SupplyStationWindow(app, null, null, () => ++stops == 1 ? Task.FromException(new IOException("SYNTHETIC stop failure with draft")) : Task.CompletedTask);
            root.ConfirmDiscardDrafts = count => { confirmations++; return true; }; Show(root);
            root.ViewModel.ConfigurationDraft = "SYNTHETIC invalid JSON draft"; root.Close(); await Until(() => root.IsEnabled);
            Check(root.IsVisible && stops == 1 && root.ViewModel.ConfigurationDraft == "SYNTHETIC invalid JSON draft", "failed stop after explicit discard still retains the draft for retry");
            root.Close(); await Until(() => !root.IsVisible);
            Check(stops == 2 && confirmations == 2, "a later close retries the original stop and asks again for the retained draft");
        }
        {
            var root = new SupplyStationWindow(app, null, null, () => Task.CompletedTask); Show(root);
            root.ViewModel.ConfigurationDraft = "SYNTHETIC save in flight";
            var saved = new TaskCompletionSource(); var operation = root.ViewModel.ServiceAsync(async () => { await saved.Task; root.ViewModel.ConfigurationDraft = ""; });
            var confirmations = 0; root.ConfirmDiscardDrafts = _ => { confirmations++; return false; };
            root.Close(); Check(root.IsVisible && !root.IsEnabled, "close waits for the complete save workflow before deciding draft status");
            saved.SetResult(); await operation; await Until(() => !root.IsVisible);
            Check(confirmations == 0, "completed save is evaluated after drain and needs no obsolete discard decision");
        }
        for (var iteration = 0; iteration < 10; iteration++)
        {
            var stops = 0; var childDecisions = 0; var rootDecisions = 0;
            var root = new SupplyStationWindow(app, null, null, () => { stops++; return Task.CompletedTask; });
            var child = new SupplyStationWindow(app, null, null, owner: root); Show(root); Show(child);
            child.ViewModel.ConfigurationDraft = "SYNTHETIC race draft";
            child.ConfirmDiscardDrafts = _ => { childDecisions++; return true; };
            root.ConfirmDiscardDrafts = _ => { rootDecisions++; return false; };
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Set(child, "pendingRefresh", pending.Task);
            child.Close(); root.Close(); pending.SetResult(); await Until(() => root.IsEnabled); await Task.Delay(20);
            Check(rootDecisions == 1 && childDecisions == 0 && stops == 0 && child.IsVisible,
                "root cancellation invalidates a late adopted child close, race " + iteration);
            root.ConfirmDiscardDrafts = _ => true; root.Close(); await Until(() => !root.IsVisible);
        }
        foreach (var field in new[] { "pendingRefresh", "pendingServiceCall" })
        {
            var stops = 0;
            var root = new SupplyStationWindow(app, null, null, () => ++stops == 1 ? Task.FromException(new IOException("SYNTHETIC stop failure")) : Task.CompletedTask);
            var child = new SupplyStationWindow(app, null, null, owner: root); Show(root); Show(child);
            var pending = new TaskCompletionSource();
            // Viewport-triggered pausing was removed in SM-AUTO-CONNECTION-WORKFLOW-01.
            // Cover the actual current command owner rather than reflecting a retired field.
            if (field == "pendingRefresh") Set(child, field, pending.Task);
            else _ = child.ViewModel.ServiceAsync(() => pending.Task);
            child.Close(); root.Close();
            Check(stops == 0 && !root.IsEnabled && !child.IsEnabled, field + " drains before owner stop");
            pending.SetResult(); await Until(() => stops == 1 && root.IsEnabled);
            Check(root.IsVisible && child.IsVisible && Timer(child).IsEnabled, field + " owner failure retains overlapping child and its live timer");
            child.Close(); await Until(() => !child.IsVisible);
            var reads = ((NoWindowBusinessCalls)(object)app).Calls; Timer(child).Fire();
            Check(((NoWindowBusinessCalls)(object)app).Calls == reads && !Timer(child).IsEnabled, field + " independently closed child cannot poll or restart");
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
            var rootRead = new TaskCompletionSource(); var childRead = new TaskCompletionSource();
            Set(root, "pendingRefresh", rootRead.Task); Set(child, "pendingRefresh", childRead.Task);
            child.Close(); root.Close(); root.Close();
            childRead.SetException(new IOException("SYNTHETIC child read failure"));
            await Until(() => child.ViewModel.Message.Contains("SYNTHETIC"));
            Check(!child.IsEnabled && !Timer(child).IsEnabled && stops == 0, "child failure cannot unfreeze a root close still draining");
            rootRead.SetResult(); await Until(() => root.IsEnabled);
            Check(root.IsVisible && child.IsVisible && stops == 0, "failed drain retains both windows and prevents premature owner stop");
            Set(child, "pendingRefresh", Task.CompletedTask); root.Close(); await Until(() => !root.IsVisible);
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
