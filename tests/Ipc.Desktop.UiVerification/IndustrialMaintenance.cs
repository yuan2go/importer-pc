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
                Check(!Button("备份并回退到上一版本…").IsEnabled, "rollback disabled before review");
                await Invoke("ReviewRollback");
                Check(!Button("备份并回退到上一版本…").IsEnabled, "incompatible release remains disabled with reason");
                allowed = true; await Invoke("ReviewRollback");
                Check(Button("备份并回退到上一版本…").IsEnabled, "compatible and settled reviewed plan enables explicit action");
                Set("ConfirmVersionRollback", (Func<string, bool>)(_ => false)); await Invoke("ExecuteRollback");
                Check(executeCalls == 0, "declining confirmation never invokes maintenance");
                Set("ConfirmVersionRollback", (Func<string, bool>)(_ => true));
                try { await Invoke("ExecuteRollback"); throw new Exception("lost receipt accepted"); } catch (InstallationOutcomeUnknownException) { }
                Check(executeCalls == 1 && !Button("备份并回退到上一版本…").IsEnabled, "lost receipt cannot silently resubmit rollback");
                await Invoke("InspectRollback", false);
                Check(inspectedOriginal && Button("修复上次回退的入口…").IsEnabled, "inspection retains original operation and offers projection repair");
                await Invoke("InspectRollback", true);
                Check(repairedOriginal && !Button("修复上次回退的入口…").IsEnabled && executeCalls == 1, "repair finishes original without another switch");
                Check(!Descendants(window).OfType<TextBox>().Any(t => t.Text.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase)), "new maintenance backup default stays on D drive");
                Check(Button("检查回退条件").MinHeight >= 44, "native actions retain 44 DIP target");
                Button("检查回退条件").BringIntoView();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Capture("industrial-maintenance-rollback-960.png");
                window.Width = 680; window.Height = 500; window.UpdateLayout();
                foreach (var button in new[] { Button("检查回退条件"), Button("备份并回退到上一版本…"), Button("核对上次回退") })
                    Check(button.ActualWidth <= window.ActualWidth - 40, "narrow window keeps action width: " + button.Content);
                Button("修复上次回退的入口…").BringIntoView();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }
            var image = Path.Combine(evidence, baseline ? "industrial-maintenance-before.png" : "industrial-maintenance-after.png");
            Capture(Path.GetFileName(image));
            lines.Add("PASS IE01-UI native WPF maintenance form rendered without production actions; " + image);
        }
        finally { window.Close(); }
    }
}
