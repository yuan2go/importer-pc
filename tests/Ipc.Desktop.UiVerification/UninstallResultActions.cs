using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyUninstallResultActions(string evidence, List<string> lines)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var assembly = Assembly.LoadFrom(Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP") ?? throw new InvalidOperationException("Set same-build desktop assembly"));
        var type = assembly.GetType("Ipc.Desktop.ProductMaintenanceWindow", true)!;
        var workflow = type.BaseType!;
        var source = new string('a', 40);
        var location = new DeploymentLocation("sportmaster", source, "D:/synthetic-release", "D:/synthetic-config", "D:/synthetic-data", "S-1-5-21-synthetic", "RemovedDataRetained");
        var pending = new WindowsRemovalReceipt("RestartRequired", "D:/synthetic-remove.json", [location.DataRoot], true, ["app/locked.dll"], location.ReleaseRoot);
        var clean = pending with { Status = "RemovedDataRetained", RestartRequired = false, RemainingFiles = [] };
        var inspectCalls = 0; var continueCalls = 0;
        void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); lines.Add("PASS UNINSTALL-ACTIONS " + why); }
        Window Make(string mediaSource, Func<WindowsRemovalReceipt> inspect, Func<DeploymentMedia, Task<WindowsRemovalReceipt>> resume)
        {
            var media = new DeploymentMedia("D:/synthetic-media", "sportmaster", mediaSource, []);
            var window = (Window)type.GetConstructor(flags, null, [typeof(DeploymentMedia)], null)!.Invoke([media]);
            type.GetProperty("CurrentDeployment", flags)!.SetValue(window, (Func<DeploymentLocation?>)(() => location));
            type.GetProperty("InspectRemoval", flags)!.SetValue(window, (Func<WindowsRemovalReceipt>)(() => { inspectCalls++; return inspect(); }));
            type.GetProperty("ContinueRemoval", flags)!.SetValue(window, (Func<DeploymentMedia, Task<WindowsRemovalReceipt>>)(media => { continueCalls++; return resume(media); }));
            return window;
        }
        bool Busy(Window w) => (bool)workflow.GetField("Busy", flags)!.GetValue(w)!;
        string Status(Window w) => ((TextBlock)workflow.GetField("Status", flags)!.GetValue(w)!).Text;
        Button Button(Window w, string label) => Descendants(w).OfType<Button>().Single(b => Equals(b.Content, label));
        void Click(Window w, string label) { var button = Button(w, label); Check(button.IsEnabled, label + " is enabled before input"); button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); }
        async Task Idle(Window w)
        {
            for (var i = 0; i < 300 && Busy(w); i++) await Task.Delay(20);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(!Busy(w), "operation returned to the native dispatcher within its wait budget"); w.UpdateLayout();
        }
        var mismatch = Make(new string('b', 40), () => pending, _ => Task.FromResult(clean));
        mismatch.Show(); await Idle(mismatch);
        Check(!Button(mismatch, "继续原清单清理…").IsEnabled && continueCalls == 0 && Descendants(mismatch).OfType<TextBlock>().Any(t => t.Text.Contains("原安装源码：" + source) && t.Text.Contains("当前安装包源码：") && t.Text.Contains("打开原版完整安装包")), "source mismatch is visible before a destructive action and cannot submit cleanup");
        CaptureInternal(evidence, "uninstall-source-mismatch", mismatch, lines);
        var before = inspectCalls; Click(mismatch, "只读核对原卸载结果"); await Idle(mismatch);
        Check(inspectCalls == before + 1 && Status(mismatch).Contains("核对完成") && continueCalls == 0 && !Button(mismatch, "继续原清单清理…").IsEnabled, "read-only button performs a new inspection and shows completion even when remaining files are unchanged");
        // Even a queued/forced event must preserve the source guard behind the disabled control.
        var rejected = false;
        try { await (Task)type.GetMethod("ResumeOriginal", flags)!.Invoke(mismatch, null)!; }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && continueCalls == 0, "source guard also rejects a queued call behind the disabled button");
        Click(mismatch, "关闭"); Check(!mismatch.IsVisible, "close exits the mismatched result window");

        var inspectionFails = false; var cleanupUnknown = false;
        var currentReceipt = pending;
        var matching = Make(source, () => inspectionFails ? throw new IOException("synthetic inspection unavailable") : currentReceipt,
            _ => cleanupUnknown ? throw new InstallationOutcomeUnknownException("synthetic cleanup receipt lost", new IOException()) : Task.FromResult(clean));
        matching.Show(); await Idle(matching);
        inspectionFails = true; Click(matching, "只读核对原卸载结果"); await Idle(matching);
        Check(Status(matching).Contains("synthetic inspection unavailable") && Descendants(matching).OfType<TextBox>().Any(t => t.Text.Contains("locked.dll")) && Button(matching, "关闭").IsEnabled, "failed inspection preserves the original evidence and restores close instead of freezing the window; status=" + Status(matching) + "; retained=" + Descendants(matching).OfType<TextBox>().Any(t => t.Text.Contains("locked.dll")) + "; close=" + Button(matching, "关闭").IsEnabled);
        inspectionFails = false; currentReceipt = clean; Click(matching, "只读核对原卸载结果"); await Idle(matching);
        Check(Status(matching).Contains("核对完成") && Status(matching).Contains("程序清单已清理") && !Descendants(matching).OfType<Button>().Any(b => Equals(b.Content, "继续原清单清理…")) && Descendants(matching).OfType<TextBox>().All(t => !t.Text.Contains("locked.dll")), "successful refresh removes stale residuals and no longer offers cleanup for a clean result");
        matching.Width = 680; matching.Height = 500; matching.UpdateLayout(); CaptureInternal(evidence, "uninstall-rechecked-compact", matching, lines);
        Click(matching, "关闭"); Check(!matching.IsVisible, "close also exits after inspection recovery");

        currentReceipt = pending; var same = Make(source, () => currentReceipt,
            _ => cleanupUnknown ? throw new InstallationOutcomeUnknownException("synthetic cleanup receipt lost", new IOException()) : Task.FromResult(clean));
        same.Show(); await Idle(same); cleanupUnknown = true; before = continueCalls;
        Click(same, "继续原清单清理…"); await Idle(same);
        Check(continueCalls == before + 1 && Status(same).Contains("结果待核对"), "uncertain cleanup reports the original unknown result");
        Click(same, "继续原清单清理…"); await Idle(same);
        Check(continueCalls == before + 1, "unknown cleanup cannot be resubmitted through the same button");
        Click(same, "只读核对原卸载结果"); await Idle(same); cleanupUnknown = false;
        Click(same, "继续原清单清理…"); await Idle(same);
        Check(continueCalls == before + 2 && Status(same).Contains("程序清单已清理") && !Descendants(same).OfType<Button>().Any(b => Equals(b.Content, "继续原清单清理…")), "same-source cleanup proceeds only after inspection and ends without a duplicate submission action");
        Click(same, "关闭");

        var completion = new TaskCompletionSource<WindowsRemovalReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = Make(source, () => pending, _ => completion.Task);
        busy.Show(); await Idle(busy); Click(busy, "继续原清单清理…");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(Busy(busy) && !Button(busy, "关闭").IsEnabled, "cleanup in progress prevents reentry and disables close");
        busy.Close(); Check(busy.IsVisible, "title-bar close cannot abandon an in-flight cleanup");
        completion.SetResult(clean); await Idle(busy); Click(busy, "关闭");
        Check(!busy.IsVisible, "close becomes available again after cleanup completes");

        var unknown = Make(source, () => pending, _ => Task.FromResult(clean)); unknown.Show(); await Idle(unknown);
        workflow.GetField("UnknownOutcome", flags)!.SetValue(unknown, true);
        Click(unknown, "关闭"); Check(!unknown.IsVisible, "unknown result can be closed without resubmitting maintenance or authenticating");

        var owner = new Window { Title = "Synthetic installer owner", Width = 700, Height = 500 }; owner.Show();
        var modal = Make(source, () => clean, _ => Task.FromResult(clean)); modal.Owner = owner;
        Exception? modalFailure = null;
        modal.Loaded += async (_, _) => { try { await Idle(modal); Click(modal, "关闭"); } catch (Exception error) { modalFailure = error; modal.Close(); } };
        modal.ShowDialog();
        Check(modalFailure is null && !modal.IsVisible && owner.IsEnabled && owner.IsVisible, "close returns from the actual modal result and restores the installer owner"); owner.Close();
        lines.Add("BOUNDARY real WPF HWND/Dispatcher and button events; synthetic deployment/inspection/cleanup callbacks. Protected uninstall/UAC/reboot/SDK/site NOT_RUN.");
    }
}
