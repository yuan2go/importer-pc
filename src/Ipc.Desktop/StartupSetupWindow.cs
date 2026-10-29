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
    internal Func<DeploymentLocation, uint> LaunchDesktop { get; set; } = location => OrdinaryRuntimeProcess.LaunchForCurrentUser(
        Path.Combine(location.ReleaseRoot, "desktop", "Ipc.Desktop.exe"), ["--after-install"], location.ReleaseRoot);
    internal Func<DeploymentMedia,string,string,string,string?,string[],bool,string?,CancellationToken,WindowsDeploymentRequest> Review { get; set; } = ParcelStartupServices.Review;
    internal Func<WindowsDeploymentRequest,IProgress<WindowsInstallationStage>,IProgress<MaintenanceProgress>,CancellationToken,Task<DeploymentLocation>> Install { get; set; } = ParcelStartupServices.InstallWithPreflightAsync;
    internal Func<WindowsDeploymentRequest, InstallationInspection> InspectInstallation { get; set; } = ParcelStartupServices.InspectInstallation;
    internal Func<WindowsDeploymentRequest,IProgress<MaintenanceProgress>,CancellationToken,Task> RepairRegistration { get; set; } = (request,progress,token) =>
        ParcelStartupServices.RepairRegistrationAsync(Wire.FileHash(WindowsDeploymentSetup.Registration(request.ApplicationId)),request,progress,token);
    readonly Func<IProgress<InstallationPreparationProgress>, CancellationToken, Task<string>>? prepareMedia;
    CancellationTokenSource? preparing;
    readonly ProgressBar preparationProgress = new() { Height = 6, Margin = new(0, 20, 0, 12), Foreground = new SolidColorBrush(Color.FromRgb(8, 102, 232)) };
    readonly TextBlock preparationFile = new() { TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label };
    readonly TextBlock installationStage = new() { FontSize = UiTypography.Body, TextWrapping = TextWrapping.Wrap, Margin = new(0, 24, 0, 14) };
    readonly List<string> installationDetails = [];
    DeploymentMedia? media;
    WindowsDeploymentRequest? reviewed;
    DeploymentComponentReview[] components = [];
    DeploymentLocation? installed;
    public DeploymentLocation? Installed => installed;
    public bool Launched { get; private set; }
    // An independent elevated helper may still need the prepared media after a lost receipt.
    public bool CanCleanPreparation => !installing && !outcomeUnknown;
    TextBox data = null!, program = null!;
    CheckBox startMenu = null!, desktopIcon = null!, runAfter = null!;
    bool installing, failed, outcomeUnknown;
    bool? needsFirstConfiguration;
    string configurationMessage = "", failureMessage = "", preflightError = "";
    string newStation = "", selectedProgram = "";
    string? enteredData;
    string? installationRecordDirectory;
    readonly TextBlock recordFeedback = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, FontSize = UiTypography.Label, Margin = new(0, 10, 0, 0) };
    bool selectedStartMenu = true, selectedDesktop = true;
    int step;
    string installationKind = "安装";
    double optionsHeight = 560;
    readonly Button next, back, records, cancel, inspect, resumeRemoval, repairCommitted;
    InstallationInspection? originalInspection;
    public StartupSetupWindow(BusinessLaunch launch, Func<IProgress<InstallationPreparationProgress>, CancellationToken, Task<string>>? prepareMedia = null, string? businessVersion = null) : base("安装 · 菜鸟供包台", installation: true)
    {
        this.prepareMedia = prepareMedia;
        if (businessVersion is not null) { VersionLabel.Text = "版本: " + (ApplicationId == "sportmaster" ? "SM " : "") + "V" + businessVersion + " Windows 64位"; VersionLabel.Visibility = Visibility.Visible; }
        back = Action("上一步", () => { CaptureInputs(); if (step > 0) step--; Render(); return Task.CompletedTask; });
        next = Action("检查安装媒介", Next); StartupAppearance.Primary(next);
        records = Action("打开安装记录目录", OpenRecordDirectory, readOnly:true);
        inspect = Action("只读核对本次安装", InspectOriginal, readOnly: true);
        repairCommitted = Action("修复原安装的系统入口…", async () =>
        {
            if (reviewed is null || originalInspection?.Status != "CommittedNeedsSystemRepair") return;
            var current = await Task.Run(() => InspectInstallation(reviewed));
            if (current.Status != "CommittedNeedsSystemRepair" || current.Location is null) throw new InvalidOperationException("原登记已变化，先只读核对");
            ResolveKnownMaintenanceOutcome(); // The read-back above resolved the original committed identity.
            await RunMaintenance("修复已提交安装的系统入口\n原安装："+reviewed.OperationId+"\n保留原数据和配置。",(progress,token)=>RepairRegistration(reviewed,progress,token));
            installed = current.Location; outcomeUnknown = false; step = 3; Render();
        }, readOnly: true);
        resumeRemoval = Action("核对并继续原卸载清理…", () => { new ProductMaintenanceWindow(media!) { Owner = this }.ShowDialog(); Render(); return Task.CompletedTask; }, readOnly: true);
        cancel = Action("取消", () => { Busy = false; Close(); return Task.CompletedTask; },readOnly:true);
        next.IsDefault = false; cancel.IsCancel = true;
        AutomationProperties.SetLiveSetting(recordFeedback, AutomationLiveSetting.Polite);
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
        Body.Children.Add(new TextBlock { Text = label, FontSize = UiTypography.Group, FontWeight = UiTypography.Emphasis, Margin = new(0, 20, 0, 10) });
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
        var box = new TextBox { Text = value, MinWidth = 0, FontSize = UiTypography.Label, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
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
        InstallationHeading.Text = outcomeUnknown ? "安装结果待确认" : failed ? "安装未完成" : installing ? "正在安装" : step switch { 0 => "准备安装", 1 => "安装选项", 2 => "确认安装", _ => "安装完成" };
        back.Visibility = step == 2 && !installing && !failed && !outcomeUnknown ? Visibility.Visible : Visibility.Collapsed;
        cancel.Visibility = installing || step == 3 ? Visibility.Collapsed : Visibility.Visible;
        cancel.Content = failed || outcomeUnknown ? "关闭" : "取消";
        next.Visibility = installing || outcomeUnknown ? Visibility.Collapsed : Visibility.Visible;
        records.Visibility = outcomeUnknown || failed && Directory.Exists(installationRecordDirectory) ? Visibility.Visible : Visibility.Collapsed;
        records.SetResourceReference(StyleProperty, typeof(Button));
        inspect.SetResourceReference(StyleProperty, "StartupPrimary");
        records.IsDefault = false; next.IsDefault = step < 2 && !outcomeUnknown;
        inspect.Visibility = outcomeUnknown ? Visibility.Visible : Visibility.Collapsed;
        repairCommitted.Visibility = outcomeUnknown && originalInspection?.Status == "CommittedNeedsSystemRepair" ? Visibility.Visible : Visibility.Collapsed;
        inspect.IsDefault = outcomeUnknown;
        resumeRemoval.Visibility = step == 1 && CurrentInstallation(ApplicationId) is { LifecycleState: not "Installed" } ? Visibility.Visible : Visibility.Collapsed;
        recordFeedback.Text = "";
        recordFeedback.ToolTip = null;
        if (outcomeUnknown)
        {
            Height = Math.Min(520, MaxHeight);
            Message(originalInspection?.Status == "CommittedNeedsSystemRepair"
                ? "原安装已提交，系统入口修复结果待确认。请先核对原操作。"
                : "未能确认本次安装的最终结果。");
            Note("先只读核对本次固定操作。核对不会重新安装；记录不可读或仍在执行时继续保留原身份。");
            var caution = new TextBlock { Text = "确认前，请勿重复安装。", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(121, 85, 27)) };
            AutomationProperties.SetLiveSetting(caution, AutomationLiveSetting.Assertive);
            Body.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(255, 248, 231)), CornerRadius = new(4), Padding = new(14, 11, 14, 11), Margin = new(0, 16, 0, 18), Child = caution });
            Details(historical: true);
            Body.Children.Add(new TextBlock { Text = "关闭不会取消可能仍在执行的安装；准备文件将保留。", TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label, Foreground = new SolidColorBrush(Color.FromRgb(98, 107, 120)), Margin = new(0, 16, 0, 0) });
            Body.Children.Add(recordFeedback);
            return;
        }
        if (failed)
        {
            Height = Math.Min(optionsHeight, MaxHeight);
            Message(ChineseInterfaceText.ParameterMessage(failureMessage)); Status.Foreground = Brushes.Firebrick;
            Note("请查看失败详情和安装记录。安装登记可能已提交，系统入口可能仍需修复；返回后将重新核对原部署。未自动回滚。");
            Details(); next.Content = "返回安装选项"; Body.Children.Add(recordFeedback); return;
        }
        if (installing)
        {
            Height = Math.Min(440, MaxHeight);
            Message("请稍候，安装完成后将显示结果。");
            Body.Children.Add(installationStage); Body.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6, Foreground = new SolidColorBrush(Color.FromRgb(8, 102, 232)) });
            Note("安装提交期间无法安全取消，请等待结果。"); return;
        }
        Message(step switch { 0 => "正在检查安装文件，请稍候…", 1 => "选择安装位置和安装设置，然后点击下一步。", 2 => "本次操作：" + installationKind + "。核对以下选项后开始。", _ => "菜鸟供包台已安装。" });
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
            installationKind = current is null ? "安装" : current.LifecycleState != "Installed" ? "重新安装" : current.SourceCommit == media?.SourceCommit ? "修复安装" : "更新";
            if (current is not null) Message("本次操作：" + installationKind + "。原账户、数据和配置将保留。");
            if (current is not null) Note("本次" + installationKind + "保留原账户、数据目录和配置；先核对原责任并完成所需完整备份。" +
                (current.LifecycleState != "Installed" ? "重新安装不会代替原卸载清理，也不会清除原业务记录。" : ""));
            if (preflightError.Length != 0)
            {
                var detail = new TextBox { Text = preflightError, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 140, Margin = new(0, 8, 0, 0) };
                AutomationProperties.SetName(detail, "安装预检详情");
                Body.Children.Add(new Expander { Header = "本次安装预检详情", IsExpanded = true, Content = detail, Margin = new(0, 12, 0, 0) });
            }
            next.Content = "下一步";
        }
        else if (step == 2)
        {
            var r = reviewed!;
            Section("安装位置"); Summary("程序目录", r.ProgramRoot!); Summary("数据目录", r.DataRoot);
            Section("安装设置");
            Summary("开始菜单", r.Shortcuts!.StartMenu ? "创建快捷方式" : "不创建快捷方式"); Summary("桌面", r.Shortcuts.Desktop ? "创建快捷方式" : "不创建快捷方式");
            if (r.BackupDestination is not null) { Section("原责任与备份"); Note("执行前检查原责任并完成备份。有阻断时停止；失败结果以实际安装记录为准。"); Summary("备份位置", r.BackupDestination); }
            foreach (var component in components.Where(c => c.Prerequisites.Length > 0)) Note("安装所需依赖：" + string.Join("；", component.Prerequisites));
            Note("安装需要 Windows 授权。应用管理密码与 Windows 授权分别验证。"); next.Content = "安装";
        }
        else
        {
            Height = Math.Min(440, MaxHeight);
            if (needsFirstConfiguration == true)
            { Section("首次使用"); Note(ApplicationId == "sportmaster" ? "首次打开后先设置供包台。相机和RCS可稍后配置；待配置时可进入软件，相关业务保持阻断。" : "首次打开后，请完成设备与服务配置，再按检查结果启动。"); }
            else Note(needsFirstConfiguration == false ? "已有配置已识别。打开后仍需主动启动，启动时检查设备与服务条件。" : configurationMessage.Length > 0 ? configurationMessage : "打开后将核对配置状态；安装完成不代表设备已可运行。");
            var canRun = ParcelStartupServices.IsRuntimeAccount(installed!);
            runAfter = new CheckBox { Content = "完成后打开供包台", IsChecked = canRun, IsEnabled = canRun, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 16, 12, 0) };
            Actions.Children.Insert(0, runAfter); if (!canRun) Note("请使用原供包台的 Windows 账户打开程序。"); next.Content = "完成";
        }
    }
    void Details(bool historical = false)
    {
        var content = new StackPanel();
        var original = installationDetails.Count == 0 ? "未收到阶段消息。" : string.Join("\n", installationDetails);
        if (historical)
        {
            content.Children.Add(new TextBlock { Text = "历史消息", FontSize = UiTypography.Label, FontWeight = UiTypography.Emphasis, Margin = new(0, 0, 0, 6) });
            content.Children.Add(new TextBlock { Text = original.Replace("等待 Windows 授权，请在授权窗口确认", "等待 Windows 授权。", StringComparison.Ordinal), FontSize = UiTypography.Label, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Text = "历史消息仅供核对，不代表当前仍在等待授权。", TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label, Margin = new(0, 8, 0, 12) });
            content.Children.Add(new TextBlock { Text = "诊断结果", FontSize = UiTypography.Label, FontWeight = UiTypography.Emphasis, Margin = new(0, 0, 0, 6) });
            content.Children.Add(new TextBox { Text = failureMessage, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label, MinHeight = 0, MaxHeight = 100, BorderThickness = new(0), Background = Brushes.Transparent, Padding = new(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            content.Children.Add(new Expander { Header = "原始消息文本", FontSize = UiTypography.Label, Margin = new(0, 12, 0, 0), Content = new TextBox { Text = original + "\n" + failureMessage, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label, MinHeight = 0, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            Body.Children.Add(new Expander { Header = "安装详情", Content = new Border { Background = new SolidColorBrush(Color.FromRgb(246, 248, 251)), CornerRadius = new(4), Padding = new(12), Margin = new(0, 8, 0, 0), Child = content }, IsExpanded = false });
            return;
        }
        content.Children.Add(new TextBox { Text = original, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 0, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        content.Children.Add(new TextBlock { Text = failureMessage, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        Body.Children.Add(new Expander { Header = "查看本次安装详情", Content = content, IsExpanded = true, Margin = new(0, 12, 0, 0) });
    }
    Task OpenRecordDirectory()
    {
        try
        {
            var directory = installationRecordDirectory ?? throw new IOException("本次安装尚未提供记录目录。");
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("记录目录尚未生成或当前无法访问：" + directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            recordFeedback.ToolTip = error.Message;
            recordFeedback.Text = (error is DirectoryNotFoundException ? "安装记录目录尚未生成或当前无法访问。" : "无法打开安装记录目录：" + ChineseInterfaceText.ParameterMessage(error.Message))
                + (outcomeUnknown ? " 最终结果仍待确认，请勿重复安装。" : " 请核对本次失败详情。");
        }
        return Task.CompletedTask;
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
        if (step == 3 && installed is not null)
        {
            string details;
            try { details = DiagnosticText.Protect(error.Message); }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { details = "启动诊断无法安全显示，请核对启动记录。"; }
            Message("安装已完成，但打开供包台未成功。可点击完成重试打开，或取消勾选后关闭。\n" + details);
            Status.Foreground = Brushes.Firebrick;
            return;
        }
        if (step == 2 || installing)
        {
            // Only a trusted original-operation inspection can release Unknown.
            // A failed system-entry repair must never offer another installation.
            installing = false; outcomeUnknown = outcomeUnknown || error is InstallationOutcomeUnknownException; failed = !outcomeUnknown;
            failureMessage = error.Message; Render();
        }
        else
        {
            if (step == 1)
            {
                CaptureInputs();
                try { preflightError = DiagnosticText.Protect(error.Message); }
                catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { preflightError = "预检诊断无法安全显示；请核对路径与配置后重新检查。"; }
                Render(); Message("安装预检未通过，请查看本次预检详情。"); Status.Foreground = Brushes.Firebrick;
                return;
            }
            if (step == 0) { next.Visibility = Visibility.Visible; next.Content = "重试检查"; cancel.Content = "取消"; cancel.IsEnabled = true; }
            Message("未完成：" + ChineseInterfaceText.ParameterMessage(error.Message)); Status.Foreground = Brushes.Firebrick;
        }
    }
    protected override void OperationCompleted()
    {
        if (outcomeUnknown && IsVisible) inspect.Focus();
    }
    async Task Next()
    {
        if (outcomeUnknown) return; // Never replay a transaction whose terminal receipt is untrusted.
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
            CaptureInputs(); preflightError = ""; Render(); var current = CurrentInstallation(ApplicationId);
            var selectedAccount = current?.RuntimeAccountSid ?? MachineRuntimePolicy.SharedUsersSid;
            var configPath = current?.ConfigurationDirectory ?? Path.Combine(media!.Root, "app", "config");
            if (current is null) { var seedDirectory = Path.Combine(Path.GetTempPath(), "ipc-new-site-" + Wire.Id()); await Task.Run(() => ParcelStartupServices.CreateSiteSeed(media!, seedDirectory, newStation, RunMode.Production)); configPath = seedDirectory; }
            var selected = media!.Files.Keys.Where(p => p.StartsWith("offline-extensions/", StringComparison.Ordinal) && p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).Select(p => Path.Combine(media.Root, p)).ToArray();
            var preview=await RunMaintenance("只读审阅安装选项\n介质："+media!.Root+"\n程序："+selectedProgram+"\n数据："+enteredData+"\n取消将在当前文件或组件核验结束后停止。",(progress,token)=>
            {
                try
                {
                    progress.Report(new(MaintenanceStage.Verifying,null,null,true));
                    var candidate=Review(media!,selectedAccount,enteredData!,configPath,null,selected,false,selectedProgram,token) with{Shortcuts=new(selectedStartMenu,selectedDesktop)};
                    var review=DeploymentComponentReviewing.Inspect(candidate,token);token.ThrowIfCancellationRequested();return Task.FromResult((candidate,review));
                }
                catch(OperationCanceledException)when(token.IsCancellationRequested){throw new MaintenanceCancelledException("安装选项预检已停止；尚未进入机器变更，准备文件保留。");}
            },readOnly:true);
            reviewed=preview.candidate;components=preview.review;
            var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(reviewed.ConfigurationSource, "business.json")));
            if (current is not null || File.Exists(Path.Combine(reviewed.DataRoot, seed.Mode.ToString(), seed.StationId, ApplicationId, ApplicationId + ".db"))) reviewed = reviewed with { BackupDestination = Path.Combine(reviewed.DataRoot, "backups", ApplicationId + "-pre-upgrade-" + Wire.Id() + ".db") };
            step = 2; Render();
        }
        else if (step == 2)
        {
            optionsHeight = Height;
            installationRecordDirectory = Path.GetDirectoryName(WindowsDeploymentSetup.Registration(reviewed!.ApplicationId));
            installing = true; installationDetails.Clear(); installationStage.Text = "正在准备安装请求"; Render();
            var stages=new Progress<WindowsInstallationStage>(StageOnDispatcher);
            installed = await RunMaintenance("安装 / 升级 / 同源修复\n介质："+reviewed!.MediaRoot+"\n程序："+(reviewed.ProgramRoot??TrustedProgramRoots.Default)+"\n保留数据："+reviewed.DataRoot,
                (progress,token)=>Install(reviewed,stages,progress,token));
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
                _ = LaunchDesktop(installed!); Launched = true;
            }
            Busy = false; Close();
        }
    }
    async Task InspectOriginal()
    {
        if (!outcomeUnknown || reviewed is null) return;
        var result = await Task.Run(() => InspectInstallation(reviewed));
        originalInspection = result;
        if (result.Status == "Completed" && result.Location is { } location)
        { ResolveKnownMaintenanceOutcome(); installed = location; outcomeUnknown = false; installing = false; step = 3; Render(); }
        else if (result.Status is "Failed" or "NotCommitted") { ResolveKnownMaintenanceOutcome(); failureMessage = result.Detail; failed = true; outcomeUnknown = false; Render(); }
        else { Render(); Message(result.Detail + "\n原操作：" + reviewed.OperationId); }
    }
    void StageOnDispatcher(WindowsInstallationStage value) => Dispatcher.BeginInvoke(() => Stage(value));
}
