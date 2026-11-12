using System.Windows;
using System.Windows.Controls;
using Ipc.Application.ParcelFlow.Desktop;

static partial class Program
{
    static async Task VerifyUnsavedWindowActions(string evidence, List<string> lines)
    {
        var owner = new Window { Title = "未保存设置 · 定向验证", Width = 800, Height = 420 }; owner.Show();
        try
        {
            await VerifyUnsavedChoices(owner,
                (name, element) => CaptureInternal(evidence, name, element, lines),
                (ok, message) => { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS UNSAVED-ACTIONS " + message); });
        }
        finally { owner.Close(); }
    }
    static async Task VerifyUnsavedChoices(Window owner, Action<string, FrameworkElement> capture, Action<bool, string> check)
    {
        async Task<bool> Choose(string button, Func<Task>? save, bool failure = false, int count = 1)
        {
            var ticks = 0; var invoked = false; var sawFailure = false; var timedOut = false;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            timer.Tick += (_, _) =>
            {
                var dialog = owner.OwnedWindows.OfType<Window>().SingleOrDefault(x => x.Title == "保留未完成设置");
                if (dialog is null) return;
                if (++ticks > 60) { timedOut = true; dialog.DialogResult = false; return; }
                if (!invoked)
                {
                    capture("unsaved-" + (count > 1 ? "multiple" : failure ? "before-failure" : button == "继续编辑" ? "cancel" : "decision"), (FrameworkElement)dialog.Content);
                    invoked = true;
                    Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, button)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else if (failure && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text.Contains("SYNTHETIC-DRAFT-FAIL")))
                {
                    sawFailure = true; capture("unsaved-save-failed", (FrameworkElement)dialog.Content);
                    Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "继续编辑")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            };
            timer.Start();
            bool result;
            try { result = await ConfigurationCloseGuard.ReviewUnsavedAsync(owner, count, save, "关闭"); }
            finally { timer.Stop(); }
            check(!timedOut && invoked && (!failure || sawFailure), "unsaved decision dialog completes the selected action and retains failed-save feedback");
            return result;
        }
        var saves = 0;
        check(!await Choose("继续编辑", () => { saves++; return Task.CompletedTask; }) && saves == 0, "cancel preserves editor without saving");
        check(await Choose("放弃未保存输入并关闭", () => { saves++; return Task.CompletedTask; }) && saves == 0, "explicit discard does not save");
        check(await Choose("加密保留后关闭", () => { saves++; return Task.CompletedTask; }) && saves == 1, "keep calls encrypted draft persistence exactly once before close");
        check(!await Choose("加密保留后关闭", () => throw new IOException("SYNTHETIC-DRAFT-FAIL"), true), "failed preservation cannot authorize closing");
        check(!await Choose("继续编辑", null, count: 2), "multiple unsaved windows retain distinct candidates without merging them");
    }
}
