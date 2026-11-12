using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Desktop;

static partial class Program
{
    static async Task VerifyStartupClose(string evidence, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS STARTUP CLOSE " + message); }
        Task Noop() => Task.CompletedTask;
        foreach (var titleBar in new[] { true, false })
        {
            var window = new ParcelStartupWindow(); var release = new TaskCompletionSource(); var calls = 0; var closed = false;
            window.Closed += (_, _) => closed = true;
            window.Fail("Synthetic startup failure", async () => { calls++; await release.Task; window.AllowClose = true; window.Close(); }, Noop, Noop, Noop);
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(Descendants(window).OfType<Button>().Count(b => b.IsDefault) == 1, "exactly one retry is default even when delegates are shared");
            if (titleBar) window.Close();
            else Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "停止并退出")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(calls == 1 && !closed, "title-bar and exit share one stop and wait for real cleanup; titleBar=" + titleBar);
            release.SetResult(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(closed, "successful cleanup closes the startup failure window; titleBar=" + titleBar);
        }
        var failed = new ParcelStartupWindow(); var tries = 0;
        failed.Fail("Synthetic startup failure", () => { tries++; throw new IOException("Synthetic retained handle"); }, Noop, Noop, Noop);
        failed.Show(); failed.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(failed.IsVisible && Descendants(failed).OfType<TextBox>().Any(t => t.Text.Contains("Synthetic retained handle")), "failed cleanup keeps its reason visible and retains the window");
        failed.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(tries == 2, "title-bar close can retry after a cleanup failure");
        failed.AllowClose = true; failed.Close();
        var maintenance = new ParcelStartupWindow(); var ended = false;
        maintenance.AfterMaintenance(() => { ended = true; maintenance.AllowClose = true; maintenance.Close(); return Task.CompletedTask; }, Noop);
        maintenance.Show(); maintenance.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(ended && !maintenance.IsVisible, "title-bar close also exits the stopped maintenance completion page");
    }
}
