using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyUninstallResultActions(string evidence, List<string> lines)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var assembly = Assembly.LoadFrom(Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP") ?? throw new InvalidOperationException("Set same-build desktop assembly"));
        var type = assembly.GetType("Ipc.Desktop.ProductMaintenanceWindow", true)!;
        var workflow = type.BaseType!;
        var clean = new WindowsRemovalReceipt("RemovedDataRetained", "D:/synthetic-remove.json", ["D:/synthetic-data"], false, [], "D:/synthetic-release");
        var assetsComplete = new UninstallAssetsReceipt("Completed", "D:/synthetic-assets.json", [], []);
        var partial = clean with { Status = "RemovedWithRetainedFiles", RemainingFiles = ["app/locked.dll"] };
        var calls = 0; var inspections = 0; var selectedDelete = false;
        void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); lines.Add("PASS UNINSTALL " + why); }
        Window Make(Func<bool, Task<ProgramUninstallResult>> execute, Func<ProgramUninstallResult>? inspect = null)
        {
            var window = (Window)Activator.CreateInstance(type)!;
            type.GetProperty("RegistrationIdentity", flags)!.SetValue(window, (Func<string>)(() => "synthetic-registration"));
            type.GetProperty("RegistrationScope", flags)!.SetValue(window, (Func<string>)(() => "synthetic-installation"));
            type.GetProperty("ExecuteUninstall", flags)!.SetValue(window,
                (Func<string, bool, IProgress<MaintenanceProgress>?, CancellationToken, Task<ProgramUninstallResult>>)((hash, delete, _, _) =>
                { Check(hash == "synthetic-registration", "original installation identity reaches the application contract"); calls++; selectedDelete = delete; return execute(delete); }));
            type.GetProperty("InspectUninstall", flags)!.SetValue(window, (Func<ProgramUninstallResult>)(() => { inspections++; return inspect!(); }));
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
        var keep = Make(_ => Task.FromResult(new ProgramUninstallResult(clean, null, false) { Assets = assetsComplete }));
        keep.Show(); await Idle(keep);
        Check(calls == 0 && inspections == 0, "opening uninstall neither reads business data nor starts maintenance");
        Check(Descendants(keep).OfType<TextBlock>().Any(t => t.Text.Contains("管理密码") && t.Text.Contains("保留")) &&
            Descendants(keep).OfType<TextBlock>().Any(t => t.Text.Contains("管理密码") && t.Text.Contains("创建新密码")),
            "both uninstall choices state exactly whether the management password survives");
        Check(Descendants(keep).OfType<RadioButton>().Single(b => Equals(b.Content, "保留本站数据")).IsChecked == true,
            "data retention is the default");
        Check(Descendants(keep).OfType<Button>().Where(b => b.IsVisible).Select(b => b.Content).SequenceEqual(new[] { "卸载", "取消" }),
            "one uninstall action and cancel replace review, backup, repair, rollback and purge workflows");
        Check(!Descendants(keep).OfType<TextBox>().Any(b => !b.IsReadOnly) && !Descendants(keep).OfType<CheckBox>().Any(),
            "no backup location or typed confirmation is required");
        CaptureInternal(evidence, "uninstall-options", keep, lines);
        keep.Width = 680; keep.Height = 420; keep.UpdateLayout();
        var cancelBounds = Button(keep, "取消").TransformToAncestor(keep).TransformBounds(new Rect(Button(keep, "取消").RenderSize));
        Check(cancelBounds.Bottom < keep.ActualHeight && Descendants(keep).OfType<RadioButton>().All(b => b.ActualHeight >= 44),
            "compact layout retains visible actions and accessible choice targets");
        CaptureInternal(evidence, "uninstall-options-compact", keep, lines);
        Click(keep, "卸载"); await Idle(keep);
        Check(calls == 1 && !selectedDelete && Status(keep).Contains("本站数据已保留"), "one click uninstalls with retained data without further dialogs");
        Check(Descendants(keep).OfType<Button>().Where(b => b.IsVisible).Select(b => b.Content).SequenceEqual(new[] { "关闭" }), "a completed uninstall only offers close");
        CaptureInternal(evidence, "uninstall-retained", keep, lines); Click(keep, "关闭");

        var deletion = new DeploymentPurgeReceipt("synthetic-purge", "Purged", "本站数据已删除。", [], "D:/synthetic-purge.json");
        var delete = Make(_ => Task.FromResult(new ProgramUninstallResult(clean, deletion, true) { Assets = assetsComplete }));
        delete.Show(); await Idle(delete);
        Descendants(delete).OfType<RadioButton>().Single(b => Equals(b.Content, "同时删除本站数据")).IsChecked = true;
        Click(delete, "卸载"); await Idle(delete);
        Check(calls == 2 && selectedDelete && Status(delete).Contains("本站数据已删除"), "explicit deletion choice reaches the application in the same uninstall action");
        Check(Status(delete).Contains("管理密码已清除") && !Descendants(delete).OfType<TextBlock>().Any(t => t.Text.Contains("管理安全信息")),
            "successful deletion reports password removal instead of claiming security information is retained");
        Check(Descendants(delete).OfType<TextBox>().All(b => !b.Text.Contains("保留数据：")), "deleted data is never described as retained");
        CaptureInternal(evidence, "uninstall-deleted", delete, lines); Click(delete, "关闭");

        var residualAssets = assetsComplete with { Status = "Partial", Remaining = ["D:/synthetic-cache"], TotalRemaining = 1 };
        var uncheckedAssets = Make(_ => Task.FromResult(new ProgramUninstallResult(clean, deletion, true)));
        uncheckedAssets.Show(); await Idle(uncheckedAssets); Click(uncheckedAssets, "卸载"); await Idle(uncheckedAssets);
        Check(!Status(uncheckedAssets).StartsWith("卸载完成", StringComparison.Ordinal) && Button(uncheckedAssets, "重试卸载").IsEnabled,
            "a missing historical asset receipt is not substituted with a successful purge result");
        Click(uncheckedAssets, "关闭");
        var oldAssets = Make(_ => Task.FromResult(new ProgramUninstallResult(clean, deletion, true) { Assets = residualAssets }));
        oldAssets.Show(); await Idle(oldAssets); Click(oldAssets, "卸载"); await Idle(oldAssets);
        Check(Status(oldAssets).Contains("历史程序或维护缓存尚未全部清理") && Button(oldAssets, "重试卸载").IsEnabled &&
            !Descendants(oldAssets).OfType<TextBox>().Any(t => t.IsVisible && System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "Maintenance.Progress"),
            "a successful data purge cannot hide incomplete historical assets");
        CaptureInternal(evidence, "uninstall-assets-partial", oldAssets, lines); Click(oldAssets, "关闭");
        var waitingAssets = Make(_ => Task.FromResult(new ProgramUninstallResult(clean, deletion, true) { Assets = residualAssets with { Status = "RestartRequired" } }));
        waitingAssets.Show(); await Idle(waitingAssets); Click(waitingAssets, "卸载"); await Idle(waitingAssets);
        Check(Status(waitingAssets).Contains("重启") && !Descendants(waitingAssets).OfType<Button>().Any(b => Equals(b.Content, "重试卸载")),
            "a queued self-cache stays visibly incomplete without a duplicate queue action");
        CaptureInternal(evidence, "uninstall-assets-restart", waitingAssets, lines); Click(waitingAssets, "关闭");
        var pendingData = Make(_ => Task.FromResult(new ProgramUninstallResult(null, deletion with { Status = "PartiallyPurged" }, true)
        { Assets = residualAssets with { Status = "RestartRequired" } }));
        pendingData.Show(); await Idle(pendingData); Click(pendingData, "卸载"); await Idle(pendingData);
        Check(Button(pendingData, "重试卸载").IsEnabled && Status(pendingData).Contains("部分数据尚未删除"),
            "an independent pending data purge remains retryable while a cache waits for reboot");
        Click(pendingData, "关闭");
        var open = Make(_ => Task.FromException<ProgramUninstallResult>(new InvalidOperationException("请先关闭供包台，再卸载。程序和数据未删除。")));
        open.Show(); await Idle(open); Click(open, "卸载"); await Idle(open);
        Check(Status(open).Contains("请先关闭供包台") && Button(open, "卸载").IsEnabled && !Status(open).Contains("卸载完成"),
            "a running workstation gets a clear close-first instruction without claiming removal");
        CaptureInternal(evidence, "uninstall-close-first", open, lines); Click(open, "取消");

        var pending = new TaskCompletionSource<ProgramUninstallResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = Make(_ => pending.Task); busy.Show(); await Idle(busy); var before = calls; Click(busy, "卸载");
        for (var i = 0; i < 100 && calls == before; i++) await Task.Delay(10);
        Check(Busy(busy) && !Button(busy, "取消").IsEnabled, "active uninstall prevents reentry and close");
        busy.Close(); Check(busy.IsVisible, "title bar cannot abandon an in-flight uninstall");
        pending.SetResult(new(partial, null, false)); await Idle(busy);
        Check(Status(busy).Contains("尚未删除") && Button(busy, "重试卸载").IsEnabled, "locked files remain an incomplete result with a single retry");
        type.GetProperty("ExecuteUninstall", flags)!.SetValue(busy,
            (Func<string, bool, IProgress<MaintenanceProgress>?, CancellationToken, Task<ProgramUninstallResult>>)((_, _, _, _) => Task.FromResult(new ProgramUninstallResult(clean, null, false) { Assets = assetsComplete })));
        Click(busy, "重试卸载"); await Idle(busy);
        Check(Status(busy).Contains("卸载完成"), "retry completes the same installation cleanup"); Click(busy, "关闭");

        var original = new ProgramUninstallResult(partial, null, false);
        var unknown = Make(_ => Task.FromException<ProgramUninstallResult>(new InstallationOutcomeUnknownException("合成卸载回执中断")), () => original);
        unknown.Show(); await Idle(unknown); Click(unknown, "卸载"); await Idle(unknown); before = calls;
        Click(unknown, "卸载"); await Idle(unknown);
        Check(calls == before && Status(unknown).Contains("结果未确认"), "lost receipt cannot submit a second uninstall");
        Click(unknown, "查看结果"); await Idle(unknown);
        Check(inspections == 1 && Button(unknown, "重试卸载").IsEnabled && Status(unknown).Contains("尚未删除"), "verified original partial result permits fixed-inventory retry");
        CaptureInternal(evidence, "uninstall-partial", unknown, lines); Click(unknown, "关闭");

        var changedChoice = Make(_ => Task.FromException<ProgramUninstallResult>(new InstallationOutcomeUnknownException("synthetic lost receipt")),
            () => new(clean, null, false) { Assets = assetsComplete });
        changedChoice.Show(); await Idle(changedChoice);
        Descendants(changedChoice).OfType<RadioButton>().Single(b => Equals(b.Content, "同时删除本站数据")).IsChecked = true;
        Click(changedChoice, "卸载"); await Idle(changedChoice);
        Descendants(changedChoice).OfType<RadioButton>().Single(b => Equals(b.Content, "保留本站数据")).IsChecked = true;
        Click(changedChoice, "查看结果"); await Idle(changedChoice);
        Check(Status(changedChoice).Contains("本站数据已保留") && !Descendants(changedChoice).OfType<Button>().Any(b => Equals(b.Content, "重试卸载")),
            "inspection preserves a later retention choice when no data-deletion operation exists");
        Click(changedChoice, "关闭");
        var purgeAttempts = 0;
        var purgeRetry = Make(_ => ++purgeAttempts == 1
                ? Task.FromException<ProgramUninstallResult>(new InstallationOutcomeUnknownException("synthetic purge interruption"))
                : Task.FromResult(new ProgramUninstallResult(null, deletion, true) { Assets = assetsComplete }),
            () => new(null, deletion with { Status = "PartiallyPurged" }, true));
        purgeRetry.Show(); await Idle(purgeRetry);
        Descendants(purgeRetry).OfType<RadioButton>().Single(b => Equals(b.Content, "同时删除本站数据")).IsChecked = true;
        Click(purgeRetry, "卸载"); await Idle(purgeRetry); before = calls;
        Click(purgeRetry, "查看结果"); await Idle(purgeRetry);
        Check(calls == before && Status(purgeRetry).Contains("部分数据尚未删除"), "inspecting an original partial data deletion never submits another deletion");
        Click(purgeRetry, "重试卸载"); await Idle(purgeRetry);
        Check(selectedDelete && Status(purgeRetry).Contains("本站数据已删除"), "an explicit retry continues a committed partial deletion rather than silently claiming retention");
        Click(purgeRetry, "关闭");
        var stale = Make(_ => Task.FromResult(new ProgramUninstallResult(clean, null, false) { Assets = assetsComplete }));
        stale.Show(); await Idle(stale); before = calls;
        type.GetProperty("RegistrationIdentity", flags)!.SetValue(stale, (Func<string>)(() => "replacement-registration"));
        Click(stale, "卸载"); await Idle(stale);
        Check(calls == before && Status(stale).Contains("重新打开"), "a registration changed after opening cannot authorize another uninstall");
        Click(stale, "取消");

        var replaced = Make(_ => Task.FromException<ProgramUninstallResult>(new InstallationOutcomeUnknownException("synthetic lost receipt")), () => new(clean, null, false) { Assets = assetsComplete });
        replaced.Show(); await Idle(replaced); Click(replaced, "卸载"); await Idle(replaced); before = inspections;
        type.GetProperty("RegistrationScope", flags)!.SetValue(replaced, (Func<string>)(() => "replacement-installation"));
        Click(replaced, "查看结果"); await Idle(replaced);
        Check(inspections == before && Status(replaced).Contains("重新打开"), "result inspection cannot rebind an old window to another installation");
        Click(replaced, "取消");

        var reopened = Make(_ => Task.FromResult(new ProgramUninstallResult(clean, null, false) { Assets = assetsComplete }));
        reopened.Show(); await Idle(reopened);
        Check(Descendants(reopened).OfType<RadioButton>().Single(b => Equals(b.Content, "保留本站数据")).IsChecked == true,
            "a newly opened window does not inherit an earlier deletion choice");
        Descendants(reopened).OfType<RadioButton>().Single(b => Equals(b.Content, "同时删除本站数据")).IsChecked = true;
        Descendants(reopened).OfType<RadioButton>().Single(b => Equals(b.Content, "保留本站数据")).IsChecked = true;
        Click(reopened, "卸载"); await Idle(reopened);
        Check(!selectedDelete, "switching deletion back to retention submits retention"); Click(reopened, "关闭");
        lines.Add("BOUNDARY real WPF HWND/Dispatcher and button events; synthetic uninstall callbacks. Protected uninstall/UAC/SDK/site NOT_RUN.");
    }
}
