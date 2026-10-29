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
        var source = new string('a', 40);
        using var identity = WindowsIdentity.GetCurrent();
        var installed = new DeploymentLocation("sportmaster", source, "D:/synthetic/releases/current-12345678", "D:/synthetic/config", "D:/synthetic/data", identity.User!.Value);
        var launch = new BusinessLaunch(Path.Combine(evidence, "no-config"), Path.Combine(evidence, "no-data"), null, null);
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
            (installed with { LifecycleState = "RemovedDataRetained" }, "重新安装")
        })
        {
            var w = Make(current); w.Show(); await Idle(w);
            Set(w, "media", new DeploymentMedia("D:/synthetic/media", "sportmaster", source, [])); Set(w, "step", 1); Render(w);
            var text = string.Join("\n", Descendants(w).OfType<TextBlock>().Select(t => t.Text));
            if (current is not null) Check(text.Contains("本次" + purpose + "保留原账户、数据目录和配置"), "options identify " + purpose + " from source and retirement state while preserving the retained data purpose");
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
        type.GetProperty("Install", flags)!.SetValue(completion, (Func<WindowsDeploymentRequest,IProgress<WindowsInstallationStage>,IProgress<MaintenanceProgress>,CancellationToken,Task<DeploymentLocation>>)((_,_,_,_) => { installCalls++; throw new Exception("Completion must never reinstall"); }));
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
        lines.Add("BOUNDARY actual WPF HWND/Dispatcher; synthetic installation and launch callbacks. Real token and protected lifecycle are separate Windows checks; no device/UAC dialog was automated.");
    }
}
