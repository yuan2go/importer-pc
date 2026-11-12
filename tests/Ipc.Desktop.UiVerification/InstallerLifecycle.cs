using System.Reflection;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyInstallerLifecycle(string evidence, List<string> lines)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = Assembly.LoadFrom(Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP")!).GetType("Ipc.Desktop.StartupSetupWindow", true)!;
        var workflow = type.BaseType!;
        void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); lines.Add("PASS INSTALLER-LIFECYCLE " + why); }
        async Task WaitUntil(Func<bool> condition, string reason)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!condition()) { await Task.Delay(10, deadline.Token); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
            Check(true, reason);
        }
        var source = new string('a', 40);
        using var identity = WindowsIdentity.GetCurrent();
        var installed = new DeploymentLocation("sportmaster", source, "D:/synthetic/releases/current-12345678", "D:/synthetic/config", "D:/synthetic/data", identity.User!.Value);
        var launch = new BusinessLaunch(Path.Combine(evidence, "no-config"), Path.Combine(evidence, "no-data"), null, null);
        var centerType = type.Assembly.GetType("Ipc.Desktop.MaintenanceCenterWindow", true)!;
        var center = (Window)Activator.CreateInstance(centerType, [launch])!;
        var handoff = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        centerType.GetProperty("LaunchUninstaller", flags)!.SetValue(center, (Func<uint>)(() =>
        {
            if (Interlocked.Increment(ref attempts) == 1) throw new IOException("synthetic protected cache unavailable");
            return handoff.Task.GetAwaiter().GetResult();
        }));
        center.Show();
        var uninstall = Descendants(center).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "Maintenance.Uninstall");
        uninstall.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await WaitUntil(() => uninstall.IsEnabled, "failed uninstall handoff restores the offline action");
        Check(center.IsVisible && !(bool)centerType.GetProperty("UninstallLaunched")!.GetValue(center)!, "cache launch failure retains original window and does not claim removal");
        uninstall.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        uninstall.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        center.Close();
        Check(center.IsVisible, "pending handoff retains original host until launch result is known");
        handoff.SetResult(42);
        await WaitUntil(() => !center.IsVisible, "successful detached handoff closes the offline host");
        Check(attempts == 2 && (bool)centerType.GetProperty("UninstallLaunched")!.GetValue(center)!, "retry launches exactly once and returns explicit host-exit routing");
        var startupType = type.Assembly.GetType("Ipc.Desktop.ParcelStartupWindow", true)!;
        var failedHost = (Window)Activator.CreateInstance(startupType)!;
        var retries = 0;
        Func<Task> closeHost = () => { startupType.GetProperty("AllowClose")!.SetValue(failedHost, true); failedHost.Close(); return Task.CompletedTask; };
        startupType.GetMethod("UninstallFailed")!.Invoke(failedHost, ["synthetic host wait timeout", closeHost, (Func<Task>)(() => { retries++; return Task.CompletedTask; })]);
        failedHost.Show();
        var retryHost = Descendants(failedHost).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "UninstallHost.Retry");
        retryHost.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Check(retries == 1 && failedHost.IsVisible, "failed uninstall retries its original entry rather than starting a business runtime");
        failedHost.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(!failedHost.IsVisible, "failed uninstall host remains closeable through the original close callback");
        Window Make(DeploymentLocation? current)
        {
            var window = (Window)Activator.CreateInstance(type, [launch, null, "1.0.0"])!;
            type.GetProperty("CurrentInstallation", flags)!.SetValue(window, (Func<string, DeploymentLocation?>)(_ => current));
            return window;
        }
        void Set(Window w, string field, object? value) => type.GetField(field, flags)!.SetValue(w, value);
        void Render(Window w) { type.GetMethod("Render", flags | BindingFlags.DeclaredOnly)!.Invoke(w, null); w.UpdateLayout(); }
        Button Button(Window w, string label) => Descendants(w).OfType<Button>().Single(b => b.IsVisible && Equals(b.Content, label));
        async Task Idle(Window w)
        {
            for (var i = 0; i < 300 && (bool)workflow.GetField("Busy", flags)!.GetValue(w)!; i++) await Task.Delay(20);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(!(bool)workflow.GetField("Busy", flags)!.GetValue(w)!, "native operation completed without leaving the footer disabled"); w.UpdateLayout();
        }
        foreach (var (current, purpose) in new (DeploymentLocation?, string)[]
        {
            (null, "安装"), (installed, "修复安装"), (installed with { SourceCommit = new string('b', 40) }, "更新"),
            (installed with { LifecycleState = "RemovedDataRetained" }, "重新安装"), (installed with { LifecycleState = "Purged" }, "重新安装")
        })
        {
            var w = Make(current); w.Show(); await Idle(w);
            Set(w, "media", new DeploymentMedia("D:/synthetic/media", "sportmaster", source, [])); Set(w, "step", 1); Render(w);
            var text = string.Join("\n", Descendants(w).OfType<TextBlock>().Select(t => t.Text));
            if (current?.LifecycleState == "Purged") Check(((TextBlock)workflow.GetField("Status", flags)!.GetValue(w)!).Text.Contains("原本站数据已删除") && !text.Contains("重新安装不会清除已有业务记录"), "reinstalling a purged station does not imply deleted records were retained or will be restored");
            else if (current is not null) Check(text.Contains("本次" + purpose + "将保留账户、数据目录和配置"), "options identify " + purpose + " from source and retirement state while preserving the retained data purpose");
            Set(w, "reviewed", new WindowsDeploymentRequest("D:/synthetic/media", "sportmaster", source, identity.User.Value, current?.DataRoot ?? launch.DataRoot,
                launch.ConfigurationDirectory, RunMode.Simulation, null, null, [], ProgramRoot: "D:/synthetic/program", Shortcuts: new(true, true), OperationId: Wire.Id()));
            Set(w, "step", 2); Render(w);
            Check(((TextBlock)workflow.GetField("Status", flags)!.GetValue(w)!).Text.Contains("本次操作：" + purpose), "confirmation shows the actual " + purpose + " operation above the scroll area without treating version 1.0.0 as source equality");
            if (purpose == "重新安装") { w.Width = 680; w.Height = 500; w.UpdateLayout(); CaptureInternal(evidence, "installer-reinstall-confirmation", w, lines); }
            w.Close();
        }
        var completion = Make(installed); completion.Show(); await Idle(completion);
        Set(completion, "installed", installed); Set(completion, "step", 3); Set(completion, "needsFirstConfiguration", true); Render(completion);
        var installCalls = 0; var launchCalls = 0;
        type.GetProperty("Install", flags)!.SetValue(completion, (Func<WindowsDeploymentRequest, IProgress<WindowsInstallationStage>, IProgress<MaintenanceProgress>, CancellationToken, Task<DeploymentLocation>>)((_, _, _, _) => { installCalls++; throw new Exception("Completion must never reinstall"); }));
        type.GetProperty("LaunchDesktop", flags)!.SetValue(completion, (Func<DeploymentLocation, uint>)(location =>
        { launchCalls++; Check(location == installed, "completion hands off the exact installed release"); if (launchCalls == 1) throw new IOException("合成：普通权限进程暂时无法创建"); return 123; }));
        Button(completion, "完成").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Idle(completion);
        Check(completion.IsVisible && launchCalls == 1 && installCalls == 0 &&
            ((TextBlock)workflow.GetField("Status", flags)!.GetValue(completion)!).Text.Contains("安装已完成，但打开供包台未成功") && Button(completion, "完成").IsEnabled,
            "launch failure keeps the completed receipt and retry-open action instead of suggesting another install");
        CaptureInternal(evidence, "installer-complete-launch-failed", completion, lines);
        Button(completion, "完成").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Idle(completion);
        Check(!completion.IsVisible && launchCalls == 2 && installCalls == 0 && (bool)type.GetProperty("Launched")!.GetValue(completion)!, "retry opens only the installed release once and exits the completed wizard");
        var close = Make(installed); close.Show(); await Idle(close); Set(close, "installed", installed); Set(close, "step", 3); Render(close);
        type.GetProperty("LaunchDesktop", flags)!.SetValue(close, (Func<DeploymentLocation, uint>)(_ => throw new Exception("Unchecked completion cannot launch")));
        Descendants(close).OfType<CheckBox>().Single(c => Equals(c.Content, "完成后打开供包台")).IsChecked = false;
        Button(close, "完成").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Idle(close);
        Check(!close.IsVisible, "unchecked completion closes without launching or reinstalling");
        var originalRequest = new WindowsDeploymentRequest("D:/synthetic/old-extraction", "sportmaster", source, identity.User.Value,
            installed.DataRoot, installed.ConfigurationDirectory, RunMode.Production, null, null, [], ProgramRoot: "D:/synthetic/program", Shortcuts: new(true, false), OperationId: Wire.Id());
        var continuationCalls = 0;
        foreach (var status in new[] { "NotCommitted", "CommittedNeedsSystemRepair", "Completed" })
        {
            var resumed = Make(installed); resumed.Show(); await Idle(resumed);
            Set(resumed, "media", new DeploymentMedia("D:/synthetic/new-extraction", "sportmaster", source, []));
            type.GetProperty("Install", flags)!.SetValue(resumed,
                (Func<WindowsDeploymentRequest, IProgress<WindowsInstallationStage>, IProgress<MaintenanceProgress>, CancellationToken, Task<DeploymentLocation>>)((_, _, _, _) => throw new Exception("Continuation cannot replay installation")));
            type.GetMethod("ApplyOriginal", flags)!.Invoke(resumed, [new InstallationContinuation(originalRequest, new(status, "synthetic persisted original", status == "NotCommitted" ? null : installed), "D:/synthetic/original-operation.json")]);
            Render(resumed);
            if (status == "NotCommitted")
            {
                type.GetProperty("ConcludeInstallation", flags)!.SetValue(resumed,
                    (Func<WindowsDeploymentRequest, IProgress<MaintenanceProgress>, CancellationToken, Task<InstallationInspection>>)((request, _, _) =>
                    { Check(request.OperationId == originalRequest.OperationId, "reopened precommit action retains its persisted operation ID"); continuationCalls++; return Task.FromResult(new InstallationInspection("Failed", "closed original")); }));
                Button(resumed, "结束原次并重新审阅").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Idle(resumed);
                Check((int)type.GetField("step", flags)!.GetValue(resumed)! == 1 && type.GetField("reviewed", flags)!.GetValue(resumed) is null,
                    "confirmed precommit closure returns to review without executing an installation");
            }
            else if (status == "CommittedNeedsSystemRepair")
            {
                type.GetProperty("InspectInstallation", flags)!.SetValue(resumed, (Func<WindowsDeploymentRequest, InstallationInspection>)(_ => new("CommittedNeedsSystemRepair", "original committed", installed)));
                var repairs = 0;
                type.GetProperty("RepairRegistration", flags)!.SetValue(resumed,
                    (Func<WindowsDeploymentRequest, IProgress<MaintenanceProgress>, CancellationToken, Task>)((request, _, _) =>
                    { Check(request.OperationId == originalRequest.OperationId, "system repair retains original committed installation identity"); if (++repairs == 1) throw new IOException("synthetic original system-entry repair failed"); continuationCalls++; return Task.CompletedTask; }));
                Button(resumed, "修复开始菜单和程序入口…").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Idle(resumed);
                Check(resumed.IsVisible && Button(resumed, "修复开始菜单和程序入口…").IsEnabled && !(bool)type.GetProperty("CanCleanPreparation")!.GetValue(resumed)!,
                    "failed original repair retains its window, media ownership and retry without exposing installation replay");
                Button(resumed, "修复开始菜单和程序入口…").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Idle(resumed);
                Check((int)type.GetField("step", flags)!.GetValue(resumed)! == 3 && repairs == 2, "retry repairs only original system projections and reaches the completed result");
            }
            else
            {
                Check(Descendants(resumed).OfType<CheckBox>().Single(c => Equals(c.Content, "完成后打开供包台")).IsChecked == false,
                    "reopening completed installation shows the original result without requesting an automatic launch");
                Button(resumed, "重新检查安装选项").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Idle(resumed);
                Check((int)type.GetField("step", flags)!.GetValue(resumed)! == 1, "another installation requires an explicit return to current-option review");
            }
            CaptureInternal(evidence, "installer-continuation-" + status, resumed, lines); resumed.Close();
        }
        Check(continuationCalls == 2, "reopened operations perform only one original closure and one successful original repair");
        var rollbackType = type.Assembly.GetType("Ipc.Desktop.StartupRollbackWindow", true)!;
        var authority = new LifecycleAuthority();
        var rollback = (Window)Activator.CreateInstance(rollbackType, [authority])!;
        rollbackType.GetProperty("LastOperation", flags)!.SetValue(rollback, (Func<string?>)(() => null));
        rollback.Show(); await Idle(rollback);
        var pending = new TaskCompletionSource<DeploymentRollbackReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rollbackId = Wire.Id(); var executions = 0; var repairCalls = 0;
        var rollbackPlan = new DeploymentRollbackPlan(Wire.Hash("plan"), rollbackId, Wire.Hash("registration"), installed,
            installed with { SourceCommit = new string('b', 40), ReleaseRoot = "D:/synthetic/releases/previous-12345678" }, true, "verified current format", "1.0.0", "1.0.0");
        rollbackType.GetField("reviewed", flags)!.SetValue(rollback, new StartupRollbackReview(rollbackPlan, new("business", "Rollback", true, "historical Unknown retained", "config", "D:/synthetic/before.db")));
        rollbackType.GetField("frozenBackup", flags)!.SetValue(rollback, "D:/synthetic/before.db");
        ((TextBox)rollbackType.GetField("destination", flags)!.GetValue(rollback)!).Text = "D:/synthetic/before.db";
        // Editing invalidates a review; install the controlled review afterward.
        rollbackType.GetField("reviewed", flags)!.SetValue(rollback, new StartupRollbackReview(rollbackPlan, new("business", "Rollback", true, "historical Unknown retained", "config", "D:/synthetic/before.db")));
        rollbackType.GetProperty("ExecuteRollback", flags)!.SetValue(rollback,
            (Func<StartupRollbackReview, string, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentRollbackReceipt>>)((review, _, _, _) =>
            { executions++; Check(review.Deployment.OperationId == rollbackId, "rollback executes the frozen original identity"); return pending.Task; }));
        Button RollbackButton(string id) => Descendants(rollback).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "Rollback." + id);
        rollbackType.GetMethod("UpdateActions", flags)!.Invoke(rollback, null);
        RollbackButton("Execute").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await WaitUntil(() => executions == 1, "native rollback starts one managed operation");
        RollbackButton("Execute").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        rollback.Close(); Check(rollback.IsVisible && executions == 1, "busy rollback prevents duplicate switch and closing before original result");
        pending.SetResult(new(rollbackId, "CommittedNeedsSystemRepair", "original registration committed", "D:/synthetic/original-rollback.json") { Plan = rollbackPlan }); await Idle(rollback);
        rollbackType.GetProperty("InspectRollback", flags)!.SetValue(rollback,
            (Func<string, bool, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentRollbackReceipt>>)((id, repair, _, _) =>
            { Check(id == rollbackId, "original rollback inspection retains identity"); if (repair && ++repairCalls == 1) throw new IOException("synthetic projection failure"); return Task.FromResult(new DeploymentRollbackReceipt(id, repair ? "Completed" : "CommittedNeedsSystemRepair", "original result", "D:/synthetic/original-rollback.json") { Plan = rollbackPlan }); }));
        RollbackButton("Repair").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Idle(rollback);
        Check(rollback.IsVisible && RollbackButton("Inspect").IsVisible && executions == 1, "failed projection repair retains inspectable original and no duplicate switch");
        RollbackButton("Repair").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Idle(rollback);
        Check(repairCalls == 2 && executions == 1, "retry only repairs the original system entry");
        var sourceEvidence = ((TextBox)rollbackType.GetField("evidence", flags)!.GetValue(rollback)!).Text;
        Check(sourceEvidence.Contains(rollbackPlan.Current.SourceCommit) && sourceEvidence.Contains(rollbackPlan.Target!.SourceCommit), "original result inspection retains both exact build identities after repair");
        rollback.Height = 440; rollback.UpdateLayout(); CaptureInternal(evidence, "rollback-original-result", rollback, lines);
        authority.Active = false;
        RollbackButton("Close").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Idle(rollback);
        Check(!rollback.IsVisible, "management expiry does not prevent closing the original rollback window");
        lines.Add("BOUNDARY actual WPF HWND/Dispatcher; synthetic installation and launch callbacks. Real token and protected lifecycle are separate Windows checks; no device/UAC dialog was automated.");
    }
    sealed class LifecycleAuthority : IManagementMode
    {
        public bool Active = true;
        public Task<ManagementModeStatus> ManagementModeStatusAsync() => Task.FromResult(new ManagementModeStatus(true, Active, DateTimeOffset.UtcNow.AddMinutes(5)));
        public Task ConfigureManagementPasswordAsync(string _) => throw new NotSupportedException();
        public Task<bool> EnterManagementModeAsync(string _) => throw new NotSupportedException();
        public Task ExitManagementModeAsync() { Active = false; return Task.CompletedTask; }
        public Task ChangeManagementPasswordAsync(string _) => throw new NotSupportedException();
    }
}
