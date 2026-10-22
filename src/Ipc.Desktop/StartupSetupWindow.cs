using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Assembly;
using Ipc.Platform;
using Microsoft.Win32;

namespace Ipc.Desktop;

public sealed record InstallationPreparationProgress(string Stage, string File, double Percent);

public sealed class StartupSetupWindow : StartupWorkflowWindow
{
    internal Func<string, DeploymentLocation?> CurrentInstallation { get; set; } = WindowsDeploymentSetup.Current;
    internal Func<WindowsDeploymentRequest, IProgress<WindowsInstallationStage>, Task<DeploymentLocation>> Install { get; set; } = ParcelStartupServices.InstallWithPreflightAsync;
    readonly Func<IProgress<InstallationPreparationProgress>, CancellationToken, Task<string>>? prepareMedia;
    CancellationTokenSource? preparing;
    readonly ProgressBar preparationProgress = new() { Height = 6, Margin = new(0, 20, 0, 12), Foreground = new SolidColorBrush(Color.FromRgb(8, 102, 232)) };
    readonly TextBlock preparationFile = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    readonly TextBlock installationStage = new() { FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new(0, 24, 0, 14) };
    readonly List<string> installationDetails = [];
    DeploymentMedia? media;
    WindowsDeploymentRequest? reviewed;
    DeploymentComponentReview[] components = [];
    DeploymentLocation? installed;
    public DeploymentLocation? Installed => installed;
    public bool Launched { get; private set; }
    TextBox data = null!, program = null!;
    CheckBox startMenu = null!, desktopIcon = null!, runAfter = null!;
    bool installing, failed, outcomeUnknown;
    bool? needsFirstConfiguration;
    string configurationMessage = "", failureMessage = "";
    string newStation = "", selectedProgram = "";
    string? enteredData;
    bool selectedStartMenu = true, selectedDesktop = true;
    int step;
    double optionsHeight = 680;
    readonly Button next, back, cancel;
    public StartupSetupWindow(BusinessLaunch launch, Func<IProgress<InstallationPreparationProgress>, CancellationToken, Task<string>>? prepareMedia = null, string? businessVersion = null) : base("安装 · 菜鸟供包台", installation: true)
    {
        this.prepareMedia = prepareMedia;
        if (businessVersion is not null) { VersionLabel.Text = "版本: " + (ApplicationId == "sportmaster" ? "SM " : "") + "V" + businessVersion + " Windows 64位"; VersionLabel.Visibility = Visibility.Visible; }
        back = Action("上一步", () => { CaptureInputs(); if (step > 0) step--; Render(); return Task.CompletedTask; });
        next = Action("检查安装媒介", Next); StartupAppearance.Primary(next);
        cancel = Action("取消", () => { Busy = false; Close(); return Task.CompletedTask; });
        next.IsDefault = true; cancel.IsCancel = true;
        cancel.Click += (_, _) =>
        {
            if (preparing is null) return;
            CloseAfterCancellation = true; preparing.Cancel(); cancel.IsEnabled = false;
            Message("正在取消安装文件准备，请稍候…");
        };
        foreach (var button in Actions.Children.OfType<Button>()) { button.MinHeight = 40; button.Margin = new(10, 16, 0, 0); }
        Render(); Loaded += async (_, _) => await Run(Next);
        Closing += (_, _) => { if (preparing is not null) { CloseAfterCancellation = true; preparing.Cancel(); Message("正在取消安装文件准备，请稍候…"); } };
    }
    string ApplicationId => ParcelStartupServices.ApplicationId;
    void Section(string label)
    {
        Body.Children.Add(new TextBlock { Text = label, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new(0, 20, 0, 10) });
        Body.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(231, 235, 239)), Margin = new(0, 0, 0, 6) });
    }
    Grid Row(string label, UIElement value, UIElement? end = null)
    {
        var row = new Grid { Margin = new(0, 6, 0, 6) };
        row.ColumnDefinitions.Add(new() { Width = new(82) }); row.ColumnDefinitions.Add(new());
        var heading = new Label { Content = label, Foreground = new SolidColorBrush(Color.FromRgb(98, 107, 120)), Padding = new(0, 6, 8, 6), VerticalAlignment = VerticalAlignment.Top };
        if (value is Control control) heading.Target = control;
        row.Children.Add(heading); Grid.SetColumn(value, 1); row.Children.Add(value);
        if (end is not null) { row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); Grid.SetColumn(end, 2); row.Children.Add(end); }
        Body.Children.Add(row); return row;
    }
    TextBox InstallationFolder(string label, string value)
    {
        var box = new TextBox { Text = value, MinWidth = 0, FontSize = 13, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetName(box, label);
        box.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(TextBox.Text)) { Source = box });
        var browse = new Button { Content = "浏览…", MinWidth = 72, Margin = new(10, 0, 0, 0) };
        AutomationProperties.SetName(browse, "浏览" + label);
        browse.Click += (_, _) => { var picker = new OpenFolderDialog { Title = label }; if (picker.ShowDialog(this) == true) box.Text = picker.FolderName; };
        Row(label, box, browse); return box;
    }
    void Summary(string label, string value)
    {
        var box = new TextBox { Text = value, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 0, Padding = new(0, 6, 0, 6), BorderThickness = new(0), Background = Brushes.Transparent };
        AutomationProperties.SetName(box, label); Row(label, box);
    }
    void CaptureInputs()
    {
        if (step != 1 || data is null) return;
        enteredData = data.Text; selectedProgram = program.Text;
        selectedStartMenu = startMenu.IsChecked == true; selectedDesktop = desktopIcon.IsChecked == true;
    }
    void Render()
    {
        Body.Children.Clear(); Form = Body; Body.Margin = new(0);
        if (runAfter is not null) Actions.Children.Remove(runAfter);
        InstallationStep(step == 3 ? 2 : step >= 2 ? 1 : 0);
        InstallationHeading.Text = failed ? "安装未完成" : installing ? "正在安装" : step switch { 0 => "准备安装", 1 => "安装选项", 2 => "确认安装", _ => "安装完成" };
        back.Visibility = step == 2 && !installing && !failed ? Visibility.Visible : Visibility.Collapsed;
        cancel.Visibility = installing || step == 3 ? Visibility.Collapsed : Visibility.Visible;
        cancel.Content = failed ? "关闭" : "取消";
        next.Visibility = installing || failed && outcomeUnknown ? Visibility.Collapsed : Visibility.Visible;
        if (failed)
        {
            Height = Math.Min(optionsHeight, MaxHeight);
            Message(failureMessage); Status.Foreground = Brushes.Firebrick;
            Note(outcomeUnknown ? "安装结果尚未确认。授权进程可能仍在执行；请先核对安装记录，不要重复安装。" : "请查看失败详情和安装记录。安装登记可能已提交，系统入口可能仍需修复；返回后将重新核对原部署。未自动回滚。");
            Details(); next.Content = "返回安装选项"; AddRecordDirectory(); return;
        }
        if (installing)
        {
            Height = Math.Min(440, MaxHeight);
            Message("请稍候，安装完成后将显示结果。");
            Body.Children.Add(installationStage); Body.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6, Foreground = new SolidColorBrush(Color.FromRgb(8, 102, 232)) });
            Note("安装提交期间无法安全取消，请等待结果。"); return;
        }
        Message(step switch { 0 => "正在检查安装文件，请稍候…", 1 => "选择安装位置和安装设置，然后点击下一步。", 2 => "核对以下选项后开始安装。", _ => "菜鸟供包台已安装。" });
        if (step == 0)
        {
            preparationProgress.IsIndeterminate = prepareMedia is null;
            Body.Children.Add(preparationProgress); Body.Children.Add(preparationFile);
            Note("正在核对版本与完整性，完成后显示安装选项。"); next.Content = "重试检查";
        }
        else if (step == 1)
        {
            Height = Math.Min(optionsHeight, MaxHeight);
            var current = CurrentInstallation(ApplicationId);
            Section("安装位置");
            program = InstallationFolder("程序目录", selectedProgram.Length > 0 ? selectedProgram : reviewed?.ProgramRoot ?? (current is null ? WindowsStoragePaths.ProgramRoot("CainiaoSupplyStation") : Directory.GetParent(Directory.GetParent(current.ReleaseRoot)!.FullName)!.FullName));
            data = InstallationFolder("数据目录", enteredData ?? reviewed?.DataRoot ?? current?.DataRoot ?? Path.Combine(WindowsDeploymentSetup.MachineRoot, "data"));
            Section("安装设置");
            startMenu = new CheckBox { Content = "在开始菜单创建快捷方式", IsChecked = selectedStartMenu };
            desktopIcon = new CheckBox { Content = "在桌面创建快捷方式", IsChecked = selectedDesktop };
            var shortcuts = new StackPanel(); shortcuts.Children.Add(startMenu); shortcuts.Children.Add(desktopIcon); Row("快捷方式", shortcuts);
            if (current is not null) Note("本次升级保留原数据目录；安装前仍须检查原责任并完成完整备份。");
            next.Content = "下一步";
        }
        else if (step == 2)
        {
            var r = reviewed!;
            Section("安装位置"); Summary("程序目录", r.ProgramRoot!); Summary("数据目录", r.DataRoot);
            Section("安装设置");
            Summary("开始菜单", r.Shortcuts!.StartMenu ? "创建快捷方式" : "不创建快捷方式"); Summary("桌面", r.Shortcuts.Desktop ? "创建快捷方式" : "不创建快捷方式");
            if (r.BackupDestination is not null) { Section("升级检查"); Note("安装前检查原责任并完成备份。有阻断时停止升级；失败结果以实际安装记录为准。"); Summary("备份位置", r.BackupDestination); }
            foreach (var component in components.Where(c => c.Prerequisites.Length > 0)) Note("安装所需依赖：" + string.Join("；", component.Prerequisites));
            Note("安装需要 Windows 授权。应用管理密码与 Windows 授权分别验证。"); next.Content = "安装";
        }
        else
        {
            Height = Math.Min(440, MaxHeight);
            if (needsFirstConfiguration == true)
            { Section("首次使用"); Note(ApplicationId == "sportmaster" ? "首次打开后，请完成相机及 RCS 服务配置，再按检查结果启动。" : "首次打开后，请完成设备与服务配置，再按检查结果启动。"); }
            else Note(needsFirstConfiguration == false ? "已有配置已识别。打开后仍需主动启动，启动时检查设备与服务条件。" : configurationMessage.Length > 0 ? configurationMessage : "打开后将核对配置状态；安装完成不代表设备已可运行。");
            var canRun = ParcelStartupServices.IsRuntimeAccount(installed!);
            runAfter = new CheckBox { Content = "完成后打开供包台", IsChecked = canRun, IsEnabled = canRun, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 16, 12, 0) };
            Actions.Children.Insert(0, runAfter); if (!canRun) Note("请使用原供包台的 Windows 账户打开程序。"); next.Content = "完成";
        }
    }
    void Details()
    {
        var text = new TextBox { Text = string.Join("\n", installationDetails), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Body.Children.Add(new Expander { Header = "查看本次安装详情", Content = text, IsExpanded = true, Margin = new(0, 12, 0, 0) });
    }
    void AddRecordDirectory()
    {
        var directory = Path.Combine(WindowsDeploymentSetup.MachineRoot, "deployments");
        if (!Directory.Exists(directory)) return;
        var open = new Button { Content = "打开安装记录目录", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 12, 0, 0) };
        open.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true }); } catch (Exception error) { Message("无法打开安装记录目录：" + error.Message); } }; Body.Children.Add(open);
    }
    void Stage(WindowsInstallationStage value)
    {
        if (!installing) return;
        installationStage.Text = value switch
        {
            WindowsInstallationStage.WaitingForAuthorization => "等待 Windows 授权，请在授权窗口确认",
            WindowsInstallationStage.CheckingResponsibilities => "正在核对安装条件与原有责任",
            WindowsInstallationStage.PreparingBackup => "正在检查原部署并创建完整备份",
            WindowsInstallationStage.CopyingFiles => "正在复制并校验程序文件",
            WindowsInstallationStage.PreparingConfiguration => "正在准备配置与数据目录",
            WindowsInstallationStage.InstallingComponents => "正在安装已核验组件",
            WindowsInstallationStage.RegisteringManagementService => "正在登记管理授权服务",
            WindowsInstallationStage.CommittingInstallation => "正在提交安装登记",
            WindowsInstallationStage.CreatingShortcuts => "正在创建所选快捷方式",
            WindowsInstallationStage.RegisteringApplication => "正在登记 Windows 程序入口",
            _ => "正在核对安装结果"
        };
        AutomationProperties.SetLiveSetting(installationStage, AutomationLiveSetting.Polite);
        installationDetails.Add(installationStage.Text);
    }
    protected override void OperationFailed(Exception error)
    {
        if (step == 2 || installing)
        {
            installing = false; failed = true; outcomeUnknown = error is InstallationOutcomeUnknownException;
            failureMessage = ChineseInterfaceText.ParameterMessage(error.Message); installationDetails.Add(failureMessage); Render();
        }
        else
        {
            if (step == 0) { next.Visibility = Visibility.Visible; next.Content = "重试检查"; cancel.Content = "取消"; cancel.IsEnabled = true; }
            Message("未完成：" + ChineseInterfaceText.ParameterMessage(error.Message)); Status.Foreground = Brushes.Firebrick;
        }
    }
    async Task Next()
    {
        if (failed) { failed = false; step = 1; reviewed = null; Render(); return; }
        if (step == 0)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("安装向导需要 Windows");
            var environment = WindowsClientCompatibility.Capture(); if (!environment.Assessment.CanStart) throw new PlatformNotSupportedException(environment.Assessment.Reason);
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
            if (prepareMedia is not null)
            {
                using var cancellation = new CancellationTokenSource(); preparing = cancellation;
                next.Visibility = Visibility.Collapsed; cancel.Content = "取消准备"; Actions.IsEnabled = true; cancel.IsEnabled = true;
                var progress = new Progress<InstallationPreparationProgress>(value => { Message(value.Stage); preparationProgress.Value = value.Percent; preparationFile.Text = value.File; });
                try { root = await prepareMedia(progress, cancellation.Token); } finally { preparing = null; Actions.IsEnabled = false; cancel.IsEnabled = true; }
            }
            media = await Task.Run(() => DeploymentMedia.Inspect(root, ApplicationId));
            VersionLabel.Text = "版本: " + (ApplicationId == "sportmaster" ? "SM " : "") + "V" + Wire.Decode<ApplicationRelease>(File.ReadAllText(Path.Combine(media.Root, "desktop", "application-release.json"))).BusinessVersion + " Windows 64位";
            VersionLabel.Visibility = Visibility.Visible; newStation = SystemParameterDefaults.InitialStationId; step = 1; Render();
        }
        else if (step == 1)
        {
            CaptureInputs(); var current = CurrentInstallation(ApplicationId);
            var selectedAccount = current?.RuntimeAccountSid ?? MachineRuntimePolicy.SharedUsersSid;
            var configPath = current?.ConfigurationDirectory ?? Path.Combine(media!.Root, "app", "config");
            if (current is null) { var seedDirectory = Path.Combine(Path.GetTempPath(), "ipc-new-site-" + Wire.Id()); await Task.Run(() => ParcelStartupServices.CreateSiteSeed(media!, seedDirectory, newStation, RunMode.Production)); configPath = seedDirectory; }
            var selected = media!.Files.Keys.Where(p => p.StartsWith("offline-extensions/", StringComparison.Ordinal) && p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).Select(p => Path.Combine(media.Root, p)).ToArray();
            reviewed = await Task.Run(() => ParcelStartupServices.Review(media!, selectedAccount, enteredData!, configPath, null, selected, false, selectedProgram));
            reviewed = reviewed with { Shortcuts = new(selectedStartMenu, selectedDesktop) }; components = await Task.Run(() => DeploymentComponentReviewing.Inspect(reviewed));
            var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(reviewed.ConfigurationSource, "business.json")));
            if (current is not null || File.Exists(Path.Combine(reviewed.DataRoot, seed.Mode.ToString(), seed.StationId, ApplicationId, ApplicationId + ".db"))) reviewed = reviewed with { BackupDestination = Path.Combine(reviewed.DataRoot, "backups", ApplicationId + "-pre-upgrade-" + Wire.Id() + ".db") };
            step = 2; Render();
        }
        else if (step == 2)
        {
            optionsHeight = Height;
            installing = true; installationDetails.Clear(); installationStage.Text = "正在准备安装请求"; Render();
            installed = await Task.Run(() => Install(reviewed!, new Progress<WindowsInstallationStage>(StageOnDispatcher)));
            installing = false;
            try { var classification = await ParcelStartupServices.Maintenance(new(installed.ConfigurationDirectory, installed.DataRoot, null, null)).ClassifyAsync(); needsFirstConfiguration = classification.Readable ? !classification.ExistingConfiguration : null; if (!classification.Readable) configurationMessage = "配置状态暂时无法核对。打开后请按实际检查结果处理；设备尚未启动。"; }
            catch (Exception error) { configurationMessage = "配置状态暂时无法核对：" + error.Message + "。设备尚未启动。"; }
            step = 3; Render();
        }
        else
        {
            if (runAfter.IsChecked == true)
            {
                if (!ParcelStartupServices.IsRuntimeAccount(installed!)) throw new InvalidOperationException("请登录指定运行账户后运行程序");
                var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(installed!.ReleaseRoot, "desktop", "Ipc.Desktop.exe")) { UseShellExecute = true, WorkingDirectory = installed.ReleaseRoot }; start.ArgumentList.Add("--after-install");
                System.Diagnostics.Process.Start(start); Launched = true;
            }
            Busy = false; Close();
        }
    }
    void StageOnDispatcher(WindowsInstallationStage value) => Dispatcher.BeginInvoke(() => Stage(value));
}
