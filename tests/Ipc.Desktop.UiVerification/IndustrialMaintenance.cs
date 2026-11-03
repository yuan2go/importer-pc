using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ipc.Platform;
using Ipc.Application.Contracts;

static partial class Program
{
    static async Task VerifyIndustrialMaintenanceUi(string evidence, List<string> lines, bool baseline)
    {
        var path = Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP") ?? throw new InvalidOperationException("Set matching built desktop assembly");
        var assembly = Assembly.LoadFrom(path);
        var type = assembly.GetType("Ipc.Desktop.ProductMaintenanceWindow", true)!;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var window = (Window)Activator.CreateInstance(type, true)!;
        // Baseline renders the incumbent initial form without invoking the real
        // installed station's Loaded handler. No production maintenance executes.
        if (baseline) type.GetField("removalCompleted", flags)!.SetValue(window, true);
        else type.GetProperty("AutomaticRead", flags)!.SetValue(window, false);
        window.ShowActivated = false; window.Width = 960; window.Height = 760;
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            void Capture(string name)
            {
                window.UpdateLayout();
                var target = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                target.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
                using var output = File.Create(Path.Combine(evidence, name)); encoder.Save(output);
            }
            if (!baseline)
            {
                void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); lines.Add("PASS IE01-UI " + why); }
                void Set(string property, object value) => type.GetProperty(property, flags)!.SetValue(window, value);
                async Task Invoke(string method, params object[] arguments)
                { await (Task)type.GetMethod(method, flags)!.Invoke(window, arguments)!; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
                Button Button(string name) => Descendants(window).OfType<Button>().Single(b => Equals(b.Content, name));
                var version = Descendants(window).OfType<Expander>().Single(e => Equals(e.Header, "版本回退")); version.IsExpanded = true;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var operation = Wire.Id();
                var location = new DeploymentLocation("sportmaster", new string('a', 40), @"D:\synthetic-current", @"D:\synthetic-config", @"D:\synthetic-data", "synthetic");
                var plan = new DeploymentRollbackPlan(new string('f', 64), operation, new string('d', 64), location,
                    location with { ReleaseRoot = @"D:\synthetic-previous" }, false, "上一版本没有可核验的兼容声明。", "1.1.0", "1.0.0");
                var allowed = false; var executeCalls = 0; var inspectedOriginal = false; var repairedOriginal = false;
                Set("CurrentDeployment", (Func<DeploymentLocation?>)(() => location));
                Set("RegistrationIdentity", (Func<string>)(() => new string('d', 64)));
                Set("LastVersionRollback", (Func<string?>)(() => operation));
                Set("ReviewVersionRollback", (Func<string, string, IProgress<MaintenanceProgress>?, CancellationToken, Task<StartupRollbackReview>>)((_, backup, _, _) =>
                    Task.FromResult(new StartupRollbackReview(plan with { Allowed = allowed }, allowed ? new("synthetic-business", "Rollback", true, "synthetic settled state", "synthetic", backup) : null))));
                Set("ExecuteVersionRollback", (Func<StartupRollbackReview, string, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentRollbackReceipt>>)((_, _, _, _) =>
                {
                    executeCalls++; return Task.FromException<DeploymentRollbackReceipt>(new InstallationOutcomeUnknownException("synthetic lost receipt"));
                }));
                Set("InspectVersionRollback", (Func<string, bool, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentRollbackReceipt>>)((id, repair, _, _) =>
                {
                    if (id != operation) throw new Exception("original operation changed"); inspectedOriginal = true; repairedOriginal |= repair;
                    return Task.FromResult(new DeploymentRollbackReceipt(id, repair ? "Completed" : "CommittedNeedsSystemRepair", repair ? "原回退已修复，数据未回滚。" : "原登记已提交，请修复入口。"));
                }));
                Check(!Button("备份并回退…").IsEnabled, "rollback disabled before review");
                await Invoke("ReviewRollback");
                Check(!Button("备份并回退…").IsEnabled, "incompatible release remains disabled with reason");
                allowed = true; await Invoke("ReviewRollback");
                Check(Button("备份并回退…").IsEnabled, "compatible and settled reviewed plan enables explicit action");
                Set("ConfirmVersionRollback", (Func<string, bool>)(_ => false)); await Invoke("ExecuteRollback");
                Check(executeCalls == 0, "declining confirmation never invokes maintenance");
                Set("ConfirmVersionRollback", (Func<string, bool>)(_ => true));
                try { await Invoke("ExecuteRollback"); throw new Exception("lost receipt accepted"); } catch (InstallationOutcomeUnknownException) { }
                Check(executeCalls == 1 && !Button("备份并回退…").IsEnabled, "lost receipt cannot silently resubmit rollback");
                await Invoke("InspectRollback", false);
                Check(inspectedOriginal && Button("修复上次回退的入口…").IsEnabled, "inspection retains original operation and offers projection repair");
                await Invoke("InspectRollback", true);
                Check(repairedOriginal && !Button("修复上次回退的入口…").IsEnabled && executeCalls == 1, "repair finishes original without another switch");
                Check(!Descendants(window).OfType<TextBox>().Any(t => t.Text.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase)), "new maintenance backup default stays on D drive");
                Check(Button("检查回退条件").MinHeight >= 44, "native actions retain 44 DIP target");
                Button("检查回退条件").BringIntoView();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Capture("industrial-maintenance-rollback-960.png");
                Descendants(window).OfType<Expander>().Single(e => Equals(e.Header, "清理旧版本")).IsExpanded = true;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var confirmation = (CheckBox)type.GetField("ordinaryStartupConfirmed", flags)!.GetValue(window)!;
                var pruneId = Wire.Id(); var pruneReviewCalls = 0; var pruneExecuteCalls = 0; var pruneResumeCalls = 0;
                Set("ReviewOldVersions", (Func<string, string, bool, IProgress<MaintenanceProgress>?, CancellationToken, Task<StartupVersionPruneReview>>)((_, backup, observed, _, _) =>
                {
                    pruneReviewCalls++;
                    return Task.FromResult(new StartupVersionPruneReview(new("synthetic-prune", pruneId, "sportmaster", "synthetic-registration", observed, "已核对合成旧版本范围。",
                        [new(@"D:\synthetic-older", new() { ["app/Ipc.Synthetic.dll"] = new string('a', 64) })], [new(location.ReleaseRoot, "当前版本")], observed),
                        new("synthetic-business", "PruneVersions", true, "合成已结状态", "synthetic", backup)));
                }));
                Set("ExecuteOldVersions", (Func<StartupVersionPruneReview, string, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentPruneReceipt>>)((review, _, _, _) =>
                {
                    pruneExecuteCalls++;
                    if (review.Deployment.OperationId != pruneId) throw new Exception("prune original changed");
                    return Task.FromException<DeploymentPruneReceipt>(new InstallationOutcomeUnknownException("synthetic lost prune receipt"));
                }));
                Set("InspectOldVersions", (Func<string, bool, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentPruneReceipt>>)((id, resume, _, _) =>
                {
                    if (id != pruneId) throw new Exception("prune original changed");
                    if (resume) pruneResumeCalls++;
                    return Task.FromResult(new DeploymentPruneReceipt(id, resume ? "CompletedWithRetainedFiles" : "Partial", resume ? "原清单完成；清单外原件保留。" : "原文件被占用，可继续原清单。", [@"D:\synthetic-older\unlisted.txt"], "synthetic-receipt"));
                }));
                var keepCalls = 0;
                Set("KeepOldVersionRemainder", (Func<string, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentPruneReceipt>>)((id, _, _) =>
                {
                    if (id != pruneId) throw new Exception("keep disposition changed original"); keepCalls++;
                    return Task.FromResult(new DeploymentPruneReceipt(id, "RetainedUnverified", "原范围余项保留，未逐项核对。", [@"D:\synthetic-older"], "synthetic-receipt"));
                }));
                Check(confirmation.IsChecked != true && !Button("备份并清理旧版本…").IsEnabled, "version cleanup requires explicit startup observation and review");
                await Invoke("ReviewPruneVersions");
                Check(pruneReviewCalls == 0, "missing startup observation never calls elevated cleanup review");
                confirmation.IsChecked = true; await Invoke("ReviewPruneVersions");
                Check(pruneReviewCalls == 1 && Button("备份并清理旧版本…").IsEnabled, "observed settled reviewed cleanup can be explicitly confirmed");
                Set("ConfirmVersionPruning", (Func<string, bool>)(_ => false)); await Invoke("PruneVersions");
                Check(pruneExecuteCalls == 0, "declining cleanup confirmation leaves all versions untouched");
                Set("ConfirmVersionPruning", (Func<string, bool>)(_ => true));
                try { await Invoke("PruneVersions"); throw new Exception("lost prune receipt accepted"); } catch (InstallationOutcomeUnknownException) { }
                Check(pruneExecuteCalls == 1 && !Button("备份并清理旧版本…").IsEnabled, "lost cleanup receipt disables another delete submission");
                await Invoke("InspectPruneVersions", false);
                Check(Button("继续原清单清理…").IsEnabled, "original Partial receipt offers fixed-list continuation");
                await Invoke("InspectPruneVersions", true);
                Check(pruneResumeCalls == 1 && pruneExecuteCalls == 1 && !Button("继续原清单清理…").IsEnabled, "retained extras finish original cleanup without new delete scope");
                await Invoke("InspectPruneVersions", false);
                Set("ConfirmVersionPruning", (Func<string, bool>)(_ => false)); await Invoke("KeepPruneRemainder");
                Check(keepCalls == 0, "keeping unverified remainder requires explicit disposition");
                Set("ConfirmVersionPruning", (Func<string, bool>)(_ => true)); await Invoke("KeepPruneRemainder");
                Check(keepCalls == 1 && pruneExecuteCalls == 1 && !Button("保留余项，结束清理…").IsEnabled, "keep disposition finishes original without another filesystem deletion");
                Button("检查旧版本").BringIntoView(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Capture("industrial-maintenance-pruning-960.png");
                window.Width = 680; window.Height = 500; window.UpdateLayout();
                foreach (var button in new[] { Button("检查回退条件"), Button("备份并回退…"), Button("核对上次回退") })
                    Check(button.ActualWidth <= window.ActualWidth - 40, "narrow window keeps action width: " + button.Content);
                Button("修复上次回退的入口…").BringIntoView();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Descendants(window).OfType<Expander>().Single(e => Equals(e.Header, "软件或配置无法打开时")).IsExpanded = true;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var backup = (CheckBox)type.GetField("backupEnabled", flags)!.GetValue(window)!;
                var opaqueAllowed = false; var opaqueReviews = 0; var opaqueExecutions = 0;
                Set("ReviewOpaqueProgramRemoval", (Func<string, bool, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentOpaqueRemovalPlan>>)((hash, skip, _, _) =>
                {
                    if (!skip) throw new Exception("implicit backup skip"); opaqueReviews++;
                    return Task.FromResult(new DeploymentOpaqueRemovalPlan("synthetic-review", "sportmaster", hash, location.ReleaseRoot, location.DataRoot, location.ConfigurationDirectory,
                        opaqueAllowed, opaqueAllowed ? "合成停机条件已核对；仅作原生交互测试。" : "原安装未提供可核验停机能力，原件保留。", [], 2));
                }));
                Set("ExecuteOpaqueProgramRemoval", (Func<DeploymentOpaqueRemovalPlan, IProgress<MaintenanceProgress>?, CancellationToken, Task<WindowsRemovalReceipt>>)((review, _, _) =>
                {
                    if (review.Identity != "synthetic-review") throw new Exception("opaque plan changed"); opaqueExecutions++;
                    return Task.FromException<WindowsRemovalReceipt>(new InstallationOutcomeUnknownException("synthetic lost opaque removal receipt"));
                }));
                Check(!Button("仅移除程序，保留数据…").IsEnabled, "opaque removal disabled until explicit no-backup review");
                await Invoke("ReviewOpaque");
                Check(backup.IsChecked == true && opaqueReviews == 0, "normal default backup cannot silently become an opaque skip");
                backup.IsChecked = false; backup.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                await Invoke("ReviewOpaque");
                Check(!Button("仅移除程序，保留数据…").IsEnabled, "missing runtime fence cannot authorize opaque removal");
                opaqueAllowed = true; await Invoke("ReviewOpaque");
                Check(Button("仅移除程序，保留数据…").IsEnabled, "explicit no-backup and reviewed ownership enable only program removal");
                Set("ConfirmOpaqueProgramRemoval", (Func<string, bool>)(_ => false)); await Invoke("RemoveOpaque");
                Check(opaqueExecutions == 0, "declining opaque confirmation preserves all program files");
                Set("ConfirmOpaqueProgramRemoval", (Func<string, bool>)(_ => true));
                try { await Invoke("RemoveOpaque"); throw new Exception("opaque lost receipt accepted"); } catch (InstallationOutcomeUnknownException) { }
                Check(opaqueExecutions == 1 && !Button("仅移除程序，保留数据…").IsEnabled, "opaque lost receipt cannot silently submit another program removal");
                try { await Invoke("ReviewOpaque"); throw new Exception("opaque Unknown review accepted"); } catch (InvalidOperationException) { }
                Check(opaqueReviews == 2, "unknown opaque result must be inspected before another review");
                Check(Button("卸载程序…").Content is string removalLabel && !removalLabel.Contains("备份"), "program removal action does not falsely promise an explicitly skipped backup");
                Button("仅移除程序，保留数据…").BringIntoView(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Capture("industrial-maintenance-opaque-680.png");
                var removalOriginal = Wire.Id(); var closeOriginalCalls = 0;
                var unfinished = new WindowsRemovalReceipt("NotCommittedNeedsClosure", "synthetic-original-removal", [location.DataRoot], false) { OperationId = removalOriginal };
                Set("InspectRemoval", (Func<WindowsRemovalReceipt>)(() => unfinished));
                Set("CloseOriginalRemoval", (Func<string, string, Task<WindowsRemovalReceipt>>)((id, _) =>
                {
                    if (id != removalOriginal) throw new Exception("original removal identity changed"); closeOriginalCalls++;
                    return Task.FromResult(unfinished with { Status = "NotCommitted" });
                }));
                await Invoke("InspectOriginal"); await Invoke("InspectOriginal");
                Check(Descendants(window).OfType<Button>().Count(button => Equals(button.Content, "结束这次未提交的卸载…")) == 1,
                    "repeated read-only inspection offers one original closure action");
                Check(opaqueExecutions == 1 && closeOriginalCalls == 0, "inspection neither resubmits removal nor silently closes intent");
                Set("ConfirmCloseOriginalRemoval", (Func<string, bool>)(_ => false)); await Invoke("CloseOriginalRemovalRecord");
                Check(closeOriginalCalls == 0, "declining precommit closure preserves original Unknown restriction");
                Set("ConfirmCloseOriginalRemoval", (Func<string, bool>)(_ => true)); await Invoke("CloseOriginalRemovalRecord");
                Check(closeOriginalCalls == 1 && Descendants(window).OfType<Button>().All(button => !Equals(button.Content, "结束这次未提交的卸载…")),
                    "explicit closure resolves original identity without removing program or data");
                Check(Descendants(window).OfType<TextBox>().All(box => !box.Text.Contains("上次操作结果未确认")), "resolved original no longer displays stale Unknown progress");
            }
            var image = Path.Combine(evidence, baseline ? "industrial-maintenance-before.png" : "industrial-maintenance-after.png");
            Capture(Path.GetFileName(image));
            lines.Add("PASS IE01-UI native WPF maintenance form rendered without production actions; " + image);
        }
        finally { window.Close(); }
    }
}
