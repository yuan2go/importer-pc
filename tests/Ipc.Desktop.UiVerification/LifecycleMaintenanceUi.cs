using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyLifecycleMaintenanceUi(string evidence, List<string> lines)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var assembly = Assembly.LoadFrom(Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP") ?? throw new InvalidOperationException("Set same-build desktop assembly"));
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); lines.Add("PASS LIFECYCLE-UI " + message); }
        async Task Idle(Window window)
        {
            var type = window.GetType().BaseType!; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while ((bool)type.GetField("Busy", flags)!.GetValue(window)!) { await Task.Delay(10, timeout.Token); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
        }
        Button Find(Window window, string id) => Descendants(window).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == id);
        void Click(Window window, string id) => Find(window, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var authority = new LifecycleAuthority(); var pruneType = assembly.GetType("Ipc.Desktop.StartupVersionPruneWindow", true)!;
        var prune = (Window)Activator.CreateInstance(pruneType, [authority])!;
        pruneType.GetProperty("LastOperation", flags)!.SetValue(prune, (Func<string?>)(() => null));
        pruneType.GetProperty("RegistrationHash", flags)!.SetValue(prune, (Func<string>)(() => Wire.Hash("current")));
        var operation = Wire.Id(); var reviews = 0; var executions = 0; var resumed = 0;
        var plan = new StartupVersionPruneReview(new("review", operation, "sportmaster", Wire.Hash("current"), true, "三个同版构建的受控 UI 预检",
            [new("D:/synthetic/old", [], 8)], [new("D:/synthetic/current", "当前构建"), new("D:/synthetic/previous", "合法回滚候选")], true),
            new("business", "PruneVersions", true, "实际责任已收口", "config", "D:/synthetic/before.db") { OperatorSummary = "原业务责任允许清理" });
        pruneType.GetProperty("ReviewPruning", flags)!.SetValue(prune, (Func<string, string, bool, IProgress<MaintenanceProgress>?, CancellationToken, Task<StartupVersionPruneReview>>)((_, _, _, _, _) => { reviews++; return Task.FromResult(plan); }));
        pruneType.GetProperty("ExecutePruning", flags)!.SetValue(prune, (Func<StartupVersionPruneReview, string, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentPruneReceipt>>)((p, _, _, _) =>
        { executions++; Check(p.Deployment.OperationId == operation, "execution uses reviewed original identity"); return Task.FromException<DeploymentPruneReceipt>(new InstallationOutcomeUnknownException("controlled lost original reply")); }));
        pruneType.GetProperty("InspectPruning", flags)!.SetValue(prune, (Func<string, bool, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentPruneReceipt>>)((id, resume, _, _) =>
        { Check(id == operation, "inspection and continuation retain original operation"); if (resume) resumed++; return Task.FromResult(new DeploymentPruneReceipt(id, resume ? "Completed" : "Partial", resume ? "原清单已完成" : "原清单尚有占用余项", resume ? [] : ["D:/synthetic/old/file.dll"], "original.json")); }));
        prune.Show(); await Idle(prune);
        ((TextBox)pruneType.GetField("destination", flags)!.GetValue(prune)!).Text = "D:/synthetic/before.db";
        ((CheckBox)pruneType.GetField("observed", flags)!.GetValue(prune)!).IsChecked = true;
        Click(prune, "VersionPrune.Review"); await Idle(prune);
        var pruneDetails = (TextBox)pruneType.GetField("detail", flags)!.GetValue(prune)!;
        Check(reviews == 1 && pruneDetails.Text.Contains("D:/synthetic/old") && pruneDetails.Text.Contains("合法回滚候选") && Find(prune, "VersionPrune.Execute").IsVisible,
            "normal native review presents deleted roots, retained rollback candidate and explicit execution");
        prune.Width = 820; prune.Height = 500; prune.UpdateLayout(); CaptureInternal(evidence, "version-prune-review", prune, lines);
        Click(prune, "VersionPrune.Execute"); await Idle(prune);
        Check(executions == 1 && Find(prune, "VersionPrune.Inspect").IsVisible && !Find(prune, "VersionPrune.Execute").IsVisible, "lost reply exposes original query and prevents a duplicate new execution");
        Click(prune, "VersionPrune.Inspect"); await Idle(prune); Click(prune, "VersionPrune.Resume"); await Idle(prune);
        Check(resumed == 1 && executions == 1 && pruneDetails.Text.Contains("原清单已完成"), "original result query and explicit resume complete without resubmitting a new plan");
        authority.Active = false; Click(prune, "VersionPrune.Review"); await Idle(prune);
        Check(reviews == 1, "expired management authority is rechecked at execution, not inferred from enabled native button"); prune.Close();

        var api = DispatchProxy.Create<IStartupMaintenance, BackupSetUiProbe>(); var probe = (BackupSetUiProbe)(object)api;
        var backupType = assembly.GetType("Ipc.Desktop.BackupSetsWindow", true)!;
        var manager = (Window)Activator.CreateInstance(backupType, [api, "D:/synthetic/catalog", false])!;
        manager.Show(); await Idle(manager); Click(manager, "BackupSet.Read"); await Idle(manager);
        var grid = Descendants(manager).OfType<DataGrid>().Single(); grid.SelectedIndex = 0;
        Check(grid.SelectedItem is StartupBackupItem { Status: "Verified", Identity: not null }, "native async listing returns selectable verified backup evidence without cross-thread control access");
        ((TextBox)backupType.GetField("deliveryDestination", flags)!.GetValue(manager)!).Text = "D:/synthetic/delivery";
        Check(grid.Columns.OfType<DataGridTextColumn>().Any(c => c.Binding is System.Windows.Data.Binding b && b.Path.Path == "StatusText"), "backup list presents Chinese completeness status from typed result");
        Click(manager, "BackupSet.Export"); await Idle(manager); Click(manager, "BackupSet.Export"); await Idle(manager);
        Check(probe.Exports == 1 && probe.Inspections == 1, "native original export action inspects a lost reply without repeating the copy");
        CaptureInternal(evidence, "complete-backup-set", manager, lines); manager.Close();

        async Task<(Window Window, BackupDeletionUiProbe Probe, DataGrid Grid)> DeletionWindow()
        {
            var api = DispatchProxy.Create<IStartupMaintenance, BackupDeletionUiProbe>(); var probe = (BackupDeletionUiProbe)(object)api;
            var window = (Window)Activator.CreateInstance(backupType, [api, "D:/synthetic/deletion-catalog", true])!;
            backupType.GetProperty("ConfirmDeletion", flags)!.SetValue(window, (Func<StartupBackupItem, bool>)(_ => true));
            window.Show(); await Idle(window); Click(window, "BackupSet.Read"); await Idle(window);
            var grid = Descendants(window).OfType<DataGrid>().Single(); grid.SelectedIndex = 0;
            return (window, probe, grid);
        }
        bool Unknown(Window window) => (bool)window.GetType().BaseType!.GetField("UnknownOutcome", flags)!.GetValue(window)!;
        var known = await DeletionWindow(); known.Probe.LoseReply = false;
        Click(known.Window, "BackupSet.Delete"); await Idle(known.Window);
        Check(known.Probe.Deletions == 1 && known.Probe.Reads == 1 && known.Grid.SelectedItem is StartupBackupItem { Status: "Deleted" } &&
            !Find(known.Window, "BackupSet.Delete").IsEnabled && !Find(known.Window, "BackupSet.SelectRestore").IsEnabled,
            "known deletion immediately displays its authoritative terminal receipt and retires stale restore/delete actions without a refresh dependency");
        known.Window.Close();
        var lost = await DeletionWindow(); Click(lost.Window, "BackupSet.Delete"); await Idle(lost.Window);
        lost.Grid.SelectedItem = null;
        Check(Unknown(lost.Window) && Find(lost.Window, "BackupSet.Delete").IsEnabled && Equals(Find(lost.Window, "BackupSet.Delete").Content, "核对原删除"),
            "lost deletion reply retains original path/identity and a usable query action even without a selected row");
        Click(lost.Window, "BackupSet.Delete"); await Idle(lost.Window);
        Check(!Unknown(lost.Window) && lost.Probe.Deletions == 1 && lost.Probe.Reads == 2 && lost.Grid.SelectedItem is StartupBackupItem { Status: "Deleted" } &&
            ((TextBox)backupType.GetField("detail", flags)!.GetValue(lost.Window)!).Text.Contains("后来出现的文件已保留"),
            "original delete action reads the completed original receipt without another delete, preserving later-file custody facts");
        CaptureInternal(evidence, "backup-original-deletion-completed", lost.Window, lines); lost.Window.Close();
        var refreshed = await DeletionWindow(); Click(refreshed.Window, "BackupSet.Delete"); await Idle(refreshed.Window);
        Click(refreshed.Window, "BackupSet.Read"); await Idle(refreshed.Window);
        Check(!Unknown(refreshed.Window) && refreshed.Probe.Deletions == 1 && !Find(refreshed.Window, "BackupSet.Delete").IsEnabled,
            "ordinary read resolves matching deleted terminal status instead of disabling the only remaining original-result action"); refreshed.Window.Close();
        var partial = await DeletionWindow(); Click(partial.Window, "BackupSet.Delete"); await Idle(partial.Window); partial.Probe.OriginalStatus = "Deleting";
        Click(partial.Window, "BackupSet.Delete"); await Idle(partial.Window);
        Check(!Unknown(partial.Window) && partial.Probe.Deletions == 1 && Equals(Find(partial.Window, "BackupSet.Delete").Content, "继续原删除…"),
            "partial original receipt is a read-only query result and requires an explicit continuation");
        backupType.GetProperty("ConfirmDeletion", flags)!.SetValue(partial.Window, (Func<StartupBackupItem, bool>)(_ => false));
        Click(partial.Window, "BackupSet.Delete"); await Idle(partial.Window);
        Check(partial.Probe.Deletions == 1, "cancelling explicit original continuation performs no delete");
        partial.Probe.LoseReply = false; backupType.GetProperty("ConfirmDeletion", flags)!.SetValue(partial.Window, (Func<StartupBackupItem, bool>)(_ => true));
        Click(partial.Window, "BackupSet.Delete"); await Idle(partial.Window);
        Check(partial.Probe.Deletions == 2 && partial.Grid.SelectedItem is StartupBackupItem { Status: "Deleted" }, "explicit original continuation finishes with the same original backup identity"); partial.Window.Close();
        var cancelled = await DeletionWindow(); Click(cancelled.Window, "BackupSet.Delete"); await Idle(cancelled.Window); cancelled.Probe.OriginalStatus = "Verified";
        Click(cancelled.Window, "BackupSet.Delete"); await Idle(cancelled.Window);
        Check(Unknown(cancelled.Window) && cancelled.Probe.Deletions == 1, "a currently intact backup alone cannot prove the original unknown deletion never committed");
        cancelled.Probe.OriginalDeletionStatus = "NotStarted";
        Click(cancelled.Window, "BackupSet.Delete"); await Idle(cancelled.Window);
        Check(!Unknown(cancelled.Window) && cancelled.Probe.Deletions == 1 && Find(cancelled.Window, "BackupSet.SelectRestore").IsEnabled,
            "the matching original precommit cancellation receipt settles unknown without deletion and leaves verified backup available"); cancelled.Window.Close();
        var mismatched = await DeletionWindow(); Click(mismatched.Window, "BackupSet.Delete"); await Idle(mismatched.Window); mismatched.Probe.MismatchIdentity = true;
        Click(mismatched.Window, "BackupSet.Delete"); await Idle(mismatched.Window);
        Check(Unknown(mismatched.Window) && mismatched.Probe.Deletions == 1 && !Find(mismatched.Window, "BackupSet.SelectRestore").IsEnabled,
            "a different backup identity at the old path cannot settle the unknown original or reopen restore/delete admission"); mismatched.Window.Close();

        var historyType = assembly.GetType("Ipc.Desktop.MaintenanceHistoryWindow", true)!;
        var history = (Window)Activator.CreateInstance(historyType)!;
        var deployment = new DeploymentLocation("sportmaster", new string('a', 40), "D:/synthetic/original-program", "D:/synthetic/original-config", "D:/synthetic/original-data", "synthetic");
        var item = new DeploymentHistoryItem("assets", operation, "RestartRequired", "RestartRequired", "原部署的维护缓存尚待重启清理，新部署不在原清单内。", "old-assets.json", deployment,
            ["D:/synthetic/old-cache"], [], TotalRemaining: 1);
        historyType.GetProperty("ReadHistory", flags)!.SetValue(history, (Func<int, Task<DeploymentHistoryPage>>)(_ => Task.FromResult(new DeploymentHistoryPage([item], false))));
        historyType.GetProperty("InspectHistory", flags)!.SetValue(history, (Func<string, string, Task<DeploymentHistoryItem>>)((kind, id) => { Check(kind == "assets" && id == operation, "history query selects exact original kind and identity"); return Task.FromResult(item); }));
        history.Show(); await Idle(history); Descendants(history).OfType<ListBox>().Single().SelectedIndex = 0;
        Click(history, "MaintenanceHistory.Inspect"); await Idle(history);
        var historyDetail = (TextBox)historyType.GetField("detail", flags)!.GetValue(history)!;
        Check(historyDetail.Text.Contains(deployment.SourceCommit) && historyDetail.Text.Contains(deployment.DataRoot) && historyDetail.Text.Contains("新部署不在原清单内"), "history preserves old deployment and pending restart facts instead of substituting current installation scope");
        Check(!Find(history, "MaintenanceHistory.Resume").IsVisible, "read-only historical receipt does not silently execute a cleanup continuation");
        CaptureInternal(evidence, "maintenance-history-result", history, lines); history.Close();
    }
    public class BackupSetUiProbe : DispatchProxy
    {
        public int Exports, Inspections;
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            var identity = Wire.Hash("complete set");
            if (method!.Name == nameof(IStartupMaintenance.ListBackupsAsync)) return Task.FromResult(new[] { new StartupBackupItem("D:/synthetic/catalog/complete.db", "Verified", DateTimeOffset.UtcNow, 1024, identity, "完整集已核验") });
            if (method.Name == nameof(IStartupMaintenance.ExportBackupSetAsync)) { Exports++; return Task.FromException<StartupBackupSetResult>(new InstallationOutcomeUnknownException("controlled reply loss")); }
            if (method.Name == nameof(IStartupMaintenance.InspectBackupDeliveryAsync)) { Inspections++; return Task.FromResult(new StartupBackupSetResult("Exported", "D:/synthetic/delivery/complete.db", identity, "原交付已完成，未重复复制。", [])); }
            throw new NotSupportedException(method.Name);
        }
    }
    public class BackupDeletionUiProbe : DispatchProxy
    {
        public int Deletions, Reads;
        public bool LoseReply = true, MismatchIdentity;
        public string OriginalStatus = "Deleted";
        public string? OriginalDeletionStatus;
        const string Backup = "D:/synthetic/deletion-catalog/original.db";
        static string Identity => Wire.Hash("original deletion set");
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method!.Name == nameof(IStartupMaintenance.ListBackupsAsync))
            {
                Reads++;
                return Task.FromResult(new[] { new StartupBackupItem(Backup, Deletions == 0 ? "Verified" : OriginalStatus, null, 0,
                    MismatchIdentity ? Wire.Hash("later different set") : Identity, Deletions == 0 ? "完整集已核验" : "原删除回执已核对；后来出现的文件已保留。") { OriginalDeletionStatus = this.OriginalDeletionStatus } });
            }
            if (method.Name == nameof(IStartupMaintenance.DeleteBackupSetAsync))
            {
                if (!Equals(arguments![0], Backup) || !Equals(arguments[1], Identity)) throw new InvalidOperationException("UI changed original deletion identity");
                Deletions++;
                return LoseReply ? Task.FromException<StartupBackupSetResult>(new InstallationOutcomeUnknownException("controlled deletion reply loss"))
                    : Task.FromResult(new StartupBackupSetResult("Deleted", Backup, Identity, "原完整备份删除已完成，原回执保留。", []));
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
