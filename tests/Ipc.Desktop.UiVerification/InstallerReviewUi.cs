using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Platform;
using Expression = System.Linq.Expressions.Expression;

static partial class Program
{
    static async Task VerifyInstallerReviewUi(string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS INSTALLER-REVIEW " + text); }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var assembly = Assembly.LoadFrom(Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP") ?? throw new InvalidOperationException("Set same-build desktop assembly"));
        var type = assembly.GetType("Ipc.Desktop.StartupSetupWindow", true)!;
        var prepareType = type.GetConstructors().Single().GetParameters()[1].ParameterType;
        var parameters = prepareType.GetMethod("Invoke")!.GetParameters().Select(p => Expression.Parameter(p.ParameterType)).ToArray();
        var preparations = 0;
        Func<Task<string>> rejectPreparation = () => { preparations++; return Task.FromException<string>(new InvalidOperationException("Synthetic preparation has no installation media")); };
        var preparation = Expression.Lambda(prepareType, Expression.Invoke(Expression.Constant(rejectPreparation)), parameters).Compile();
        var launch = new BusinessLaunch(System.IO.Path.Combine(evidence, "synthetic-install-config"), System.IO.Path.Combine(evidence, "synthetic-install-data"), null, null);
        var version = BusinessCompatibility.VersionOf(typeof(Ipc.Application.Sportmaster.SportmasterFlow).Assembly);
        var window = (Window)Activator.CreateInstance(type, [launch, preparation, version])!;
        type.GetProperty("CurrentInstallation", flags)!.SetValue(window, (Func<string, DeploymentLocation?>)(_ => null));
        window.Show();
        try
        {
            for (var i = 0; i < 100; i++) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (preparations == 1 && !(bool)type.GetField("Busy", flags)!.GetValue(window)!) break; await Task.Delay(20); }
            Check(preparations == 1 && (int)type.GetField("step", flags)!.GetValue(window)! == 0 && !(bool)type.GetField("installing", flags)!.GetValue(window)!, "synthetic media preparation stops before any real installation");
            type.GetField("failed", flags)!.SetValue(window, false); type.GetField("step", flags)!.SetValue(window, 2); type.GetField("installing", flags)!.SetValue(window, true);
            type.GetMethod("Render", flags | BindingFlags.DeclaredOnly)!.Invoke(window, null);
            type.GetMethod("Stage", flags)!.Invoke(window, [WindowsInstallationStage.WaitingForAuthorization]);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var stage = (TextBlock)type.GetField("installationStage", flags)!.GetValue(window)!;
            Check(stage.IsVisible && stage.Text.Contains("Windows 授权") && AutomationProperties.GetLiveSetting(stage) == AutomationLiveSetting.Assertive && stage.Padding.Left == 12,
                "Windows authorization is a visible assertive stage notice");
            Check(Descendants(window).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "版本: SM V" + version + " Windows 64位"), "installer displays the actual supplied SM business version");
            var details = Descendants(window).OfType<Expander>().Single(e => Equals(e.Header, "查看本次安装详情"));
            Check(!details.IsExpanded, "installation paths and technical evidence start folded");
            CaptureInternal(evidence, "review-installer-authorization", (FrameworkElement)window.Content, lines);
            type.GetMethod("Stage", flags)!.Invoke(window, [WindowsInstallationStage.CopyingFiles]); window.UpdateLayout();
            Check(stage.Text == "正在复制并校验程序文件" && !Descendants(window).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("/ 6 步")), "installation phases do not introduce a second numbered step system");
            type.GetField("installing", flags)!.SetValue(window, false);
            type.GetField("step", flags)!.SetValue(window, 3);
            type.GetField("installed", flags)!.SetValue(window, new DeploymentLocation("sportmaster", new string('a', 40),
                System.IO.Path.Combine(evidence, "synthetic-release"), launch.ConfigurationDirectory, launch.DataRoot, MachineRuntimePolicy.SharedUsersSid));
            type.GetField("needsFirstConfiguration", flags)!.SetValue(window, false);
            type.GetMethod("Render", flags | BindingFlags.DeclaredOnly)!.Invoke(window, null);
            window.UpdateLayout();
            Check(Descendants(window).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("打开后自动检查并启动") && t.Text.Contains("具体原因")) &&
                !Descendants(window).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("仍需主动启动")),
                "existing-configuration completion explains automatic startup and unmet conditions");
            CaptureInternal(evidence, "review-installer-automatic-start", (FrameworkElement)window.Content, lines);
        }
        finally { type.GetField("installing", flags)!.SetValue(window, false); window.Close(); }
    }
}
