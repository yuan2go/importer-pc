using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Assembly;
using Ipc.Platform;
using Microsoft.Win32;

namespace Ipc.Desktop;

public abstract partial class StartupWorkflowWindow : Window
{
    protected readonly StackPanel Body = new();
    protected readonly ContentControl WorkflowBody = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    protected readonly WrapPanel Actions = new() { HorizontalAlignment = HorizontalAlignment.Right };
    protected ScrollViewer? InstallationOptionsScroll;
    protected readonly DockPanel FooterHost = new();
    protected readonly WrapPanel SessionActions = new() { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(12, 0, 0, 0) };
    protected readonly StackPanel AuthorizationGate = new() { Visibility = Visibility.Collapsed, Margin = new(8, 16, 8, 16), MaxWidth = 570, HorizontalAlignment = HorizontalAlignment.Left };
    protected readonly TextBlock WorkflowHeading = new() { FontSize = UiTypography.Position, FontWeight = UiTypography.Emphasis };
    protected readonly TextBlock WorkflowBusinessName = new() { FontSize = UiTypography.Label, Foreground = Brushes.SlateGray, Margin = new(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    protected StackPanel Form;
    protected readonly TextBlock Status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 12) };
    protected readonly TextBlock InstallationHeading = new() { Text = "安装菜鸟供包台", FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis };
    protected readonly TextBlock VersionLabel = new() { FontSize = UiTypography.Body, Margin = new(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    readonly TextBlock[] installationSteps = new TextBlock[3];
    protected void InstallationStep(int step)
    {
        for (var i = 0; i < installationSteps.Length; i++)
        {
            installationSteps[i].SetResourceReference(TextBlock.ForegroundProperty, i == step ? "Accent" : "Muted");
            installationSteps[i].FontWeight = i == step ? UiTypography.Emphasis : FontWeights.Normal;
            if (i == step) installationSteps[i].SetResourceReference(TextBlock.BackgroundProperty, "AccentBackground"); else installationSteps[i].Background = Brushes.Transparent;
            installationSteps[i].Padding = new(8, 12, 8, 12);
            installationSteps[i].FontSize = UiTypography.Label;
        }
    }
    protected bool Busy;
    protected bool UnknownOutcome;
    protected bool CloseAfterCancellation;
    protected StartupWorkflowWindow(string title, bool installation = false)
    {
        Form = Body;
        ProductBrand.Apply(this); UiTypography.Apply(this);
        VersionLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Title = title; FontSize = UiTypography.Body; Width = 960; Height = 760; MinWidth = 680; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Background"); SetResourceReference(ForegroundProperty, "Ink");
        if (installation)
        {
            Width = 880; Height = 560; MinWidth = 600; MinHeight = 380; StartupAppearance.Apply(this);
            var layout = new Grid(); layout.ColumnDefinitions.Add(new() { Width = new(176) }); layout.ColumnDefinitions.Add(new());
            var rail = new StackPanel { Margin = new(16, 28, 16, 24) };
            rail.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Ipc.Desktop;component/Assets/cainiao.png")), Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Left });
            rail.Children.Add(new TextBlock { Text = "菜鸟供包台", FontSize = UiTypography.Body, FontWeight = UiTypography.Emphasis, Margin = new(0, 16, 0, 16) });
            var stepLabels = ParcelStartupServices.ApplicationId == "sportmaster"
                ? new[] { ("1  欢迎与位置", 0), ("2  安装", 1), ("3  安装完成", 2) }
                : new[] { ("1  安装选项", 0), ("2  确认并安装", 1), ("3  安装完成", 2) };
            foreach (var (label, index) in stepLabels)
            { installationSteps[index] = new TextBlock { Text = label, Margin = new(0, 12, 0, 12), TextWrapping = TextWrapping.Wrap }; rail.Children.Add(installationSteps[index]); }
            var sidebar = new Border { Child = rail }.Themed(Border.BackgroundProperty, "Soft"); layout.Children.Add(sidebar);
            var content = new Grid { Margin = new(28, 24, 28, 20) }; Grid.SetColumn(content, 1);
            content.RowDefinitions.Add(new() { Height = GridLength.Auto }); content.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); content.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var header = new StackPanel(); header.Children.Add(InstallationHeading); header.Children.Add(VersionLabel); header.Children.Add(new ScrollViewer { Content = Status, MaxHeight = 48, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); header.Children.Add(CreateMaintenancePanel(compact: true));
            var options = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(options, 1);
            InstallationOptionsScroll = options;
            Actions.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetRow(Actions, 2);
            content.Children.Add(header); content.Children.Add(options); content.Children.Add(Actions); layout.Children.Add(content); Content = layout;
            SizeChanged += (_, _) => { layout.ColumnDefinitions[0].Width = new(ActualWidth < 760 ? 136 : 176); content.Margin = new(ActualWidth < 760 ? 20 : 28, 24, ActualWidth < 760 ? 20 : 28, 20); };
            Closing += (_, e) => { if (Busy) e.Cancel = true; }; return;
        }
        var grid = new Grid { Margin = new(20, 12, 20, 16) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel(); WorkflowHeading.Text = title.Split(" · ")[0]; heading.Children.Add(WorkflowHeading);
        heading.Children.Add(WorkflowBusinessName);
        heading.Children.Add(new ScrollViewer { Content = Status, MaxHeight = 64, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var diagnostic = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        diagnostic.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source = Status });
        heading.Children.Add(CreateMaintenancePanel());
        // Full diagnostics remain selectable through the named tools menu.
        Status.ToolTip = diagnostic;
        WorkflowBody.Content = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var bodyHost = new Grid(); bodyHost.Children.Add(WorkflowBody); bodyHost.Children.Add(AuthorizationGate); Grid.SetRow(bodyHost, 1);
        FooterHost.Children.Add(Actions); Grid.SetRow(FooterHost, 2);
        var sessionHeader = new DockPanel(); DockPanel.SetDock(SessionActions, Dock.Right); sessionHeader.Children.Add(SessionActions); sessionHeader.Children.Add(heading);
        grid.Children.Add(sessionHeader); grid.Children.Add(bodyHost); grid.Children.Add(FooterHost); Content = grid;
        Closing += (_, e) => { if (Busy) e.Cancel = true; };
    }
    protected Button Action(string label, Func<Task> action, bool readOnly = false)
    {
        var button = new Button { Content = label, MinHeight = 44, MinWidth = 100, Margin = new(0, 12, 10, 0), Padding = new(12, 6, 12, 6) };
        button.Click += async (_, _) =>
        {
            if (UnknownOutcome && !readOnly) { Message("上次操作结果未确认，请勿重复执行。请先查看结果，或关闭窗口（记录会保留）。"); return; }
            await Run(action);
        }; Actions.Children.Add(button); return button;
    }
    protected async Task Run(Func<Task> action)
    {
        if (Busy) return; Busy = true; FooterHost.IsEnabled = false; SessionActions.IsEnabled = false; Actions.IsEnabled = false; Body.IsEnabled = false; WorkflowBody.IsEnabled = false;
        try { await action(); }
        catch (OperationCanceledException) when (CloseAfterCancellation) { Busy = false; Close(); }
        catch (Exception ex) { OperationFailed(ex); }
        finally { Busy = false; FooterHost.IsEnabled = true; SessionActions.IsEnabled = true; Actions.IsEnabled = true; Body.IsEnabled = true; WorkflowBody.IsEnabled = true; OperationCompleted(); }
    }
    protected virtual void OperationCompleted() { }
    protected virtual void OperationFailed(Exception error)
    {
        if (error is MaintenanceCancelledException) { Message(error.Message); return; }
        UnknownOutcome |= error is InstallationOutcomeUnknownException;
        Status.Text = UnknownOutcome ? "上次维护操作结果未确认，请勿重复执行。\n" + ChineseInterfaceText.ParameterMessage(error.Message)
            : "未完成：" + ChineseInterfaceText.ParameterMessage(error.Message) + "\n请核对原回执和当前状态后再试；失败提示不表示已执行步骤被回滚。";
        Status.SetResourceReference(TextBlock.ForegroundProperty, UnknownOutcome ? "Warning" : "Danger");
    }
    protected void Message(string text) { Status.Text = text; Status.Foreground = Foreground; }
    protected void Note(string text) => Form.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 8) });
    protected TextBox Field(string label, string value, Action<string> changed, bool readOnly = false)
    {
        Note(label); var box = new TextBox { Text = value, MinHeight = 44, IsReadOnly = readOnly, MaxLength = 65536, Margin = new(0, 0, 0, 4) };
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        box.TextChanged += (_, _) => changed(box.Text); Form.Children.Add(box); return box;
    }
    protected TextBox Folder(string label, string value)
    {
        var box = Field(label, value, _ => { });
        var browse = new Button { Content = "选择文件夹…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        browse.Click += (_, _) => { var picker = new OpenFolderDialog { Title = label }; if (picker.ShowDialog(this) == true) box.Text = picker.FolderName; };
        Form.Children.Add(browse); return box;
    }
    protected void Choice<T>(string label, T selected, Action<T> changed) where T : struct, Enum
    {
        Note(label); var box = new ComboBox { ItemsSource = Enum.GetValues<T>(), ItemTemplate = ChineseChoiceTextConverter.CreateTemplate(), SelectedItem = selected, MinHeight = 36 };
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is T value) changed(value); }; Form.Children.Add(box);
    }
    protected static string? FilePick(Window owner, string title, string filter = "所有文件|*.*")
    { var picker = new OpenFileDialog { Title = title, Filter = filter }; return picker.ShowDialog(owner) == true ? picker.FileName : null; }
    protected void CompleteDialog() { Busy = false; DialogResult = true; }
}

sealed class StartupRepairWindow : StartupWorkflowWindow
{
    readonly bool initialConfiguration;
    readonly UIElement[] pathFields;
    TabControl? tasks;
    bool rendering = true;
    string? savedInput, checkedInput;
    ParameterEditorModel? parameterEditor;
    ParameterSettingsControl? parameterControl;
    bool startupSaveUncertain;
    bool lastSaveConfirmed;
    SmDisplayPreferenceDraft? observedPreferences;
    string? preservedDraftInput;
    bool SaveIsComplete => lastSaveConfirmed && !startupSaveUncertain && savedInput == Input() && draft?.DisplayPreferences?.HasChanges != true;
    bool savingCandidate;
    string? checkedCandidateHash;
    enum RepairTask { Directory, Parameters, Review, Recovery }
    enum RepairAction { Read, Check, SaveDraft, ContinueDraft, ExportTemplate, Import, Save, Repair, Backup, Retention, PlanRestore, ReadRestore, OpenRestore, ExecuteRestore, AdoptRestore, Rollback, Install, Start, Previous, Next, SaveAndStart, Reload, Exit, Tools }
    readonly Dictionary<RepairAction, Button> actionButtons = [];
    readonly Dictionary<RepairTask, TabItem> taskPages = [];
    RepairTask selectedTask;
    int selectedParameterCategory;
    bool authorityAllowed;
    readonly ManagementModeMenu managementMenu = new() { UnauthorizedLabel = "未授权" };
    readonly TextBlock saveState = new() { Text = "未保存", FontSize = UiTypography.Label, Foreground = Brushes.SlateGray, VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 12, 0) };
    Button? headerImport;
    Action? refreshReview;
    internal Func<bool> ConfirmDiscardInputs { get; set; } = null!;
    IStartupMaintenance maintenance = null!;
    bool maintenanceReleased, maintenanceClosing, maintenanceReleasePending;
    StartupMaintenanceDocument? document;
    StartupConfigurationDraft? draft;
    StartupSavedDraft? savedDraft;
    TextBox? restorePlanPreview;
    StartupRecoveryPreview? recovery;
    StartupRecoveryPreview? restoredPreview;
    public bool RestoreAdopted { get; private set; }
    string? restoredBackup;
    readonly TextBox config, data;
    string reason = "", backup = "", restoreRoot = "", backupCatalogRoot = "";
    public BusinessLaunch SelectedLaunch { get; private set; }
    IManagementMode ManagementMode => ParcelStartupServices.ManagementMode(SelectedLaunch);
    public bool OpenRecoveryScenario { get; set; }
    public bool SceneOnly { get; set; }
    public bool RetryRequested { get; private set; }
    public bool ReplacementLaunched { get; private set; }
    public bool ConfigurationSaved { get; private set; }
    public StartupRepairWindow(BusinessLaunch launch, string? failure = null, bool initialConfiguration = false) : base(initialConfiguration ? "首次配置 · 菜鸟供包台" : "启动修复 · 离线维护")
    {
        SelectedLaunch = launch;
        this.initialConfiguration = initialConfiguration;
        if (initialConfiguration) { StartupAppearance.Apply(this); Width = 960; Height = 656; WorkflowHeading.FontSize = UiTypography.PageTitle; WorkflowBusinessName.Text = ChineseInterfaceText.BusinessName(ParcelStartupServices.ApplicationId); WorkflowBusinessName.Visibility = Visibility.Visible; MinHeight = 360; WorkflowBody.Visibility = Visibility.Collapsed; FooterHost.Visibility = Visibility.Collapsed; Status.Visibility = Visibility.Collapsed; }
        ConfirmDiscardInputs = () => MessageBox.Show(this, "有未保存的输入。返回可保存为草稿；继续退出将丢弃这些输入，已保存的配置和草稿不受影响。", "有未保存的配置", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK;
        if (initialConfiguration) reason = "首次配置供包台";
        Message("无需先启动软件。检查和编辑不会连接设备；保存后在下次启动时生效。" + (failure is null ? "" : "\n启动诊断：" + failure));
        config = Folder("配置目录（包含 business.json）", launch.ConfigurationDirectory);
        data = Folder("数据目录（含 Simulation/Production、站点和应用子目录）", launch.DataRoot);
        pathFields = Body.Children.Cast<UIElement>().ToArray();
        SessionActions.Children.Add(managementMenu);
        var administratorLogin = new Button { Content = "进入管理模式…", MinHeight = 44 };
        var gateTitle = new TextBlock { FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis, TextWrapping = TextWrapping.Wrap };
        var gateDescription = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 20), FontSize = UiTypography.Body }.Themed(TextBlock.ForegroundProperty, "Muted");
        var gateActions = new WrapPanel();
        AuthorizationGate.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Ipc.Application.ParcelFlow.Desktop;component/StartupAppearance.xaml", UriKind.Relative) });
        var gateExit = new Button { Content = "退出配置", MinHeight = 40, MinWidth = 88, Margin = new(12, 0, 0, 0) };
        gateExit.Click += (_, _) => { if (!Busy && MayDiscard()) Close(); };
        AuthorizationGate.Children.Add(gateTitle); AuthorizationGate.Children.Add(gateDescription); AuthorizationGate.Children.Add(gateActions); gateActions.Children.Add(gateExit);
        bool seenAuthorization = false, manuallyExited = false;
        bool gateLayout = false;
        double editorWidth = Width, editorHeight = Height;
        bool checkingAuthority = false, authorityWasBlocked = false;
        string? statusBeforeAuthorityBlock = null;
        async Task ManagementAction()
        {
            if (!administratorLogin.IsEnabled || Busy) return;
            administratorLogin.IsEnabled = false;
            managementMenu.IsEnabled = false;
            try
            {
                if ((await ManagementMode.ManagementModeStatusAsync()).Authorized) { await ManagementMode.ExitManagementModeAsync(); manuallyExited = true; }
                else new ManagementModeWindow(ManagementMode) { Owner = this }.ShowDialog();
                var grant = await ManagementMode.ManagementModeStatusAsync();
                await RefreshAuthorization();
                if (grant.Authorized && draft is null && !Busy) await Run(Read);
            }
            catch (Exception error) { Message("管理模式操作未完成：" + error.Message + " 已填写的内容保留。"); }
            finally { administratorLogin.IsEnabled = true; managementMenu.IsEnabled = true; }
        }
        administratorLogin.Click += async (_, _) => await ManagementAction();
        managementMenu.ModeActionRequested += async (_, _) => await ManagementAction();
        gateActions.Children.Insert(0, administratorLogin);
        Action(RepairAction.Read, "读取所选配置", Read);
        Action(RepairAction.Check, "检查并预览更改", CheckCandidate);
        Action(RepairAction.SaveDraft, "保存草稿", async () =>
        {
            if (draft is null || document is null) throw new InvalidOperationException("请先读取配置");
            RequireDirectories();
            savedDraft = await Task.Run(() => maintenance.SaveDraftAsync(document.ConfigurationHash, savedDraft?.DraftHash, draft.Capture()));
            preservedDraftInput = savedInput = Input();
            Message("未完成输入已保存；下次打开可继续填写。");
            if (initialConfiguration) { Busy = false; Close(); }
        });
        Action(RepairAction.ContinueDraft, "继续已保存草稿", () =>
        {
            if (savedDraft is null || draft is null) throw new InvalidOperationException("没有可继续草稿");
            if (savedDraft.BaseChanged) throw new InvalidOperationException("草稿保存后配置已被修改，草稿未自动套用。请先对比当前配置。");
            if (savedDraft.Editor.Values.GetValueOrDefault("editor") == "live-v2") throw new InvalidOperationException("这是软件运行时保存的设置草稿，请在软件的“参数设置”页继续。当前输入和草稿均保留。");
            if (document?.Candidate?.ApplicationId == "sportmaster") (draft.DisplayPreferences ??= new()).Restore(savedDraft.Editor.Values);
            draft = draft.WithRestored(savedDraft.Editor); Render(false); Message("已恢复上次未完成的输入。参数有改动，请重新检查。"); return Task.CompletedTask;
        });
        Action(RepairAction.ExportTemplate, "导出设置模板…", () =>
        {
            if (draft is null || document?.Candidate is null) throw new InvalidOperationException("先读取配置");
            var picker = new SaveFileDialog { FileName = "site-template.json", Filter = "设置模板|*.json" };
            if (picker.ShowDialog(this) == true) { using var file = new StreamWriter(new FileStream(picker.FileName, FileMode.CreateNew)); file.Write(Wire.Encode(StartupSiteTemplates.Export(document.Candidate.ApplicationId, draft.Capture()))); Message("已导出设置模板。模板只包含通用设置，不含站点、设备、格口、密码、编号范围和业务数据。"); }
            return Task.CompletedTask;
        });
        headerImport = Action(RepairAction.Import, "导入配置文件…", ImportConfiguration);
        Action(RepairAction.Save, initialConfiguration ? "保存首次配置…" : "保存配置（下次启动生效）…", SaveCandidate);
        Action(RepairAction.Repair, "修复未生效的配置…", async () =>
        {
            RequireChecked(); var candidate = RequireDraft();
            if (MessageBox.Show(this, "原配置和记录都会保留。只有在软件已完全退出、没有未处理完的作业且新配置检查通过时，下次启动才会尝试新配置；尝试失败会继续使用原配置。", "确认修复配置", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            document = await Task.Run(() => maintenance.RepairInitialAsync(document!.ConfigurationHash, candidate, reason)); Render();
            Message("修复已保存。重新启动时会再次检查，全部通过后新配置才会生效。");
        });
        Action(RepairAction.Backup, "创建完整备份…", async () =>
        {
            var picker = new SaveFileDialog { FileName = "complete-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db", Filter = "完整恢复集数据库|*.db" };
            if (picker.ShowDialog(this) != true) return;
            await RunMaintenance("创建完整备份\n数据：" + (document?.RuntimeDirectory ?? launch.DataRoot) + "\n目标：" + picker.FileName + " 及同名 .recovery 目录",
                (progress, token) => maintenance.BackupAsync(picker.FileName, progress, token)); backupCatalogRoot = Path.GetDirectoryName(picker.FileName)!;
            Message("完整备份已完成。请把数据库文件和同名 .recovery 目录一起保存。");
        });
        Action(RepairAction.Retention, "清理过期记录…", () =>
        {
            if (maintenance is not IStartupRetentionMaintenance retention) throw new NotSupportedException("当前版本不支持清理过期记录");
            new HistoryRetentionWindow(retention, ManagementMode) { Owner = this }.ShowDialog(); return Task.CompletedTask;
        });
        Action(RepairAction.PlanRestore, "检查恢复计划", async () =>
        {
            recovery = await RunMaintenance("检查恢复计划\n备份：" + backup + "\n目标：" + restoreRoot + "\n尚未复制或恢复文件。",
                (progress, token) => maintenance.PlanRestoreAsync(backup, restoreRoot, progress, token), readOnly: true);
            if (restorePlanPreview is not null) restorePlanPreview.Text = recovery.Summary + "\n目标：" + recovery.Destination;
            var checkedRestore = recovery;
            SelectRestoreStage(checkedRestore);
            Message(checkedRestore.Summary);
        });
        Action(RepairAction.ReadRestore, "读取原恢复…", async () =>
        {
            var originals = await Task.Run(() => maintenance.ReadRestoreOperationsAsync());
            if (originals.Length == 0) { Message("没有已登记的原恢复操作。可选择完整备份并检查计划。"); return; }
            var dialog = new Window { Owner = this, Title = "选择原恢复", Width = 760, Height = 440, MinWidth = 560, MinHeight = 320, FontFamily = UiTypography.Interface, FontSize = UiTypography.Body, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            StartupAppearance.FitToWorkArea(dialog);
            var layout = new DockPanel { Margin = new(20) };
            var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) };
            var cancel = new Button { Content = "取消", IsCancel = true, IsDefault = true, MinHeight = 44, MinWidth = 96 };
            var select = new Button { Content = "查看原结果", MinHeight = 44, Margin = new(12, 0, 0, 0) };
            var list = new ListBox { ItemsSource = originals.Select(p => p.State + " · " + p.Station + "\n备份：" + p.Backup + "\n目标：" + p.DataRoot + "\n" + p.Summary).ToArray() };
            cancel.Click += (_, _) => dialog.DialogResult = false;
            select.Click += (_, _) => { if (list.SelectedIndex >= 0) dialog.DialogResult = true; };
            select.SetResourceReference(StyleProperty, "Primary"); footer.Children.Add(cancel); footer.Children.Add(select); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer); layout.Children.Add(list); dialog.Content = layout;
            dialog.Loaded += (_, _) => cancel.Focus();
            if (dialog.ShowDialog() == true)
            {
                var original = originals[list.SelectedIndex]; backup = original.Backup!; restoreRoot = original.DataRoot;
                SelectRestoreStage(original); Render(false); Message(original.Summary);
            }
        });
        Action(RepairAction.OpenRestore, "从完整备份恢复本站…", async () =>
        {
            if (!MayDiscard()) return;
            await ReleaseMaintenanceAsync();
            var restore = new StartupRepairWindow(SelectedLaunch) { Owner = this, OpenRecoveryScenario = true, SceneOnly = true };
            restore.ShowDialog();
            if (restore.RestoreAdopted) { RestoreAdopted = true; SelectedLaunch = restore.SelectedLaunch; Busy = false; Close(); }
            else { maintenanceReleased = false; await Read(true); }
        });
        Action(RepairAction.ExecuteRestore, "按计划恢复到新目录…", async () =>
        {
            if (!MayDiscard()) return;
            var selected = recovery ?? throw new InvalidOperationException("先检查恢复计划。");
            await RunMaintenance("恢复备份到新目录\n备份：" + backup + "\n目标：" + selected.Destination + "\n恢复完成后仍需检查配置才能生产。",
                (progress, token) => maintenance.RestoreAsync(backup, selected.Destination, selected.BackupSha256, progress, token));
            restoredPreview = selected with { State = "Isolated" }; restoredBackup = backup; recovery = null;
            Message("备份已恢复到新目录，原目录保留。尚未启动；请核对接管条件、原请求和设备后再明确启动。");
        });
        Action(RepairAction.AdoptRestore, "核对并接管原恢复…", async () =>
        {
            if (restoredPreview is null || restoredBackup is null) throw new InvalidOperationException("请先完成完整恢复，并确认新旧目录都没有正在运行的软件");
            var registration = WindowsDeploymentSetup.Registration(ParcelStartupServices.ApplicationId);
            if (!File.Exists(registration)) throw new InvalidOperationException("找不到原机的安装登记，不能接管。请联系供应商处理换机。");
            if (MessageBox.Show(this, "将禁止从原目录启动，并把安装登记切换到新目录。原目录和数据不会删除。切换后需进入管理模式，核对未处理完的作业、设备和编号范围后才能生产。", "确认换机接管", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            await Task.Run(() => maintenance.AdoptRestoreAsync(restoredBackup, restoredPreview.DataRoot, restoredPreview.BackupSha256, Wire.FileHash(registration)));
            RestoreAdopted = true;
            SelectedLaunch = SelectedLaunch with { DataRoot = restoredPreview.DataRoot };
            Message("已切换到新目录，原目录不能再启动。请重新启动软件，在维护页核对交接内容；旧作业不会自动恢复或重发。");
        });
        Action(RepairAction.Rollback, "上一部署回滚…", () =>
        {
            new StartupRollbackWindow(ManagementMode) { Owner = this }.ShowDialog();
            return Task.CompletedTask;
        });
        Action(RepairAction.Install, "安装 / 修复程序…", async () =>
        {
            if (!MayDiscard()) return;
            var setup = new StartupSetupWindow(SelectedLaunch) { Owner = this }; setup.ShowDialog();
            if (setup.Launched) { ReplacementLaunched = true; Busy = false; Close(); return; }
            if (setup.Installed is { } installed) { config.Text = installed.ConfigurationDirectory; data.Text = installed.DataRoot; await Read(true); }
        });
        Action(RepairAction.Start, initialConfiguration ? "按此配置启动" : "重试正常启动", async () => { if (checkedInput != Input()) await CheckCandidate(); await StartReviewed(); });
        Action(RepairAction.Previous, "上一步", () => { if (initialConfiguration) parameterControl?.MovePrevious(); else SelectTask(selectedTask == RepairTask.Review ? RepairTask.Parameters : RepairTask.Directory); return Task.CompletedTask; });
        Action(RepairAction.Next, "下一步", async () =>
        {
            if (initialConfiguration)
            {
                if (parameterControl is null || !await parameterControl.MoveNextAsync()) { LocateFirstIssue(); throw new InvalidOperationException("请修正当前页标出的参数后再继续；其他输入保留。"); }
                saveState.Text = SaveIsComplete ? "已保存，启动时检查" : "未保存";
                if (parameterControl.IsReviewPage) await CheckCandidate();
            }
            else if (selectedTask == RepairTask.Directory) SelectTask(RepairTask.Parameters);
            else { SelectTask(RepairTask.Review); await CheckCandidate(); }
        });
        Action(RepairAction.SaveAndStart, initialConfiguration ? "保存配置" : "保存并启动", async () =>
        {
            if (!(lastSaveConfirmed && !startupSaveUncertain && document?.Candidate is { } confirmed && Wire.Encode(RequireDraft()) == Wire.Encode(confirmed))) await CheckCandidate();
            savingCandidate = true;
            await SaveCandidate();
            if (!lastSaveConfirmed || startupSaveUncertain || savedInput != Input() || Wire.Encode(RequireDraft()) != Wire.Encode(document!.Candidate))
                throw new InvalidOperationException("尚未确认业务配置保存成功，请按提示操作。已填写的内容保留。");
            if (draft?.DisplayPreferences?.HasChanges == true)
                throw new InvalidOperationException("业务配置已保存；本机偏好未全部保存，输入保留。请检查偏好文件与权限后重试，本次配置不会重复提交。" + draft.DisplayPreferences.SaveSummary);
            if (initialConfiguration) { await ReleaseMaintenanceAsync(); ConfigurationSaved = true; CompleteDialog(); }
            else { await CheckCandidate(); await StartReviewed(); }
        });
        if (initialConfiguration) Action(RepairAction.Reload, "重新读取配置…", Read);
        Action(RepairAction.Exit, initialConfiguration ? "稍后配置" : "关闭", () => { Busy = false; Close(); return Task.CompletedTask; });
        Action(RepairAction.Tools, "配置工具…", () => { OpenTools(); return Task.CompletedTask; });
        if (initialConfiguration)
        {
            foreach (var (kind, button) in actionButtons)
                if (kind is not (RepairAction.Previous or RepairAction.Next or RepairAction.SaveAndStart or RepairAction.Reload or RepairAction.Import or RepairAction.SaveDraft or RepairAction.Exit or RepairAction.OpenRestore)) Actions.Children.Remove(button);
            var saveDraft = actionButtons[RepairAction.SaveDraft]; saveDraft.Content = "保存草稿并退出";
            DockPanel.SetDock(saveState, Dock.Left); FooterHost.Children.Insert(0, saveState);
            Actions.HorizontalAlignment = HorizontalAlignment.Right;
            Actions.Children.Remove(headerImport); headerImport.Content = "导入配置"; headerImport.Margin = new(0, 0, 12, 0); SessionActions.Children.Insert(0, headerImport);
        }
        var editEvents = initialConfiguration ? (UIElement)WorkflowBody : Body;
        editEvents.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, e) => { if (e.OriginalSource is TextBox { IsReadOnly: false }) Changed(); }));
        editEvents.AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler((_, e) => { if (e.OriginalSource is ComboBox) Changed(); }));
        editEvents.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler((_, _) => Changed()));
        editEvents.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler((_, _) => Changed()));
        editEvents.AddHandler(PasswordBox.PasswordChangedEvent, new RoutedEventHandler((_, _) => Changed()));
        Closing += async (_, e) =>
        {
            if (e.Cancel || maintenanceReleased) return;
            e.Cancel = true;
            if (Busy || maintenanceClosing || !RetryRequested && !ReplacementLaunched && !MayDiscard()) return;
            maintenanceClosing = true;
            try
            {
                await System.Windows.Threading.Dispatcher.Yield();
                await Run(ReleaseMaintenanceAsync);
                if (maintenanceReleased) Close();
            }
            finally { maintenanceClosing = false; }
        };
        var authorizationTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        async Task RefreshAuthorization()
        {
            if (RestoreAdopted)
            {
                AuthorizationGate.Visibility = Visibility.Collapsed; FooterHost.Visibility = Visibility.Visible;
                WorkflowBody.Visibility = Visibility.Visible; WorkflowBody.IsEnabled = false; SessionActions.IsEnabled = false;
                UpdateActions(); return; // Completed result/close needs no grant from the retired root.
            }
            if (checkingAuthority) return; checkingAuthority = true;
            try
            {
                var identity = await ManagementMode.ManagementModeStatusAsync(); var allowed = identity.Authorized;
                if (RestoreAdopted) return;
                authorityAllowed = allowed; if (allowed) { seenAuthorization = true; manuallyExited = false; }
                if (initialConfiguration && gateLayout == allowed)
                {
                    if (allowed) { Width = editorWidth; Height = editorHeight; }
                    else { editorWidth = Width; editorHeight = Height; Width = Math.Min(720, MaxWidth); Height = Math.Min(440, MaxHeight); }
                    gateLayout = !allowed;
                }
                managementMenu.Status = identity;
                administratorLogin.Content = allowed ? "退出管理模式" : !identity.Configured && !identity.HasUnsupportedCredentials ? "设置管理密码" : "进入管理模式…";
                administratorLogin.IsEnabled = identity.Available && !Busy;
                gateTitle.Text = !identity.Available ? "管理授权服务不可用" : !identity.Configured && !identity.HasUnsupportedCredentials ? "配置前，请先设置管理密码" : manuallyExited ? "管理模式已退出" : seenAuthorization ? "管理权限已到期，请重新验证后继续" : "配置前，请进入管理模式";
                gateDescription.Text = !identity.Available ? "请用原安装包修复软件。已填写的内容暂存在本窗口，尚未保存。" : seenAuthorization ? "已填写的内容暂存在本窗口，尚未保存。重新输入管理密码后可继续。" : "管理密码用于保护参数设置，与操作员账号和 Windows 账户无关。检查和编辑不会启动设备。";
                WorkflowBody.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
                AuthorizationGate.Visibility = allowed ? Visibility.Collapsed : Visibility.Visible;
                FooterHost.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
                if (initialConfiguration && headerImport is not null) headerImport.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
                StartupAppearance.Primary(administratorLogin);
                if (!allowed && !authorityWasBlocked) { statusBeforeAuthorityBlock = Status.Text; Message(""); }
                else if (allowed && authorityWasBlocked) Message(statusBeforeAuthorityBlock ?? "已进入管理模式，已填写的内容已恢复。");
                authorityWasBlocked = !allowed;
            }
            catch (Exception error) { if (RestoreAdopted) return; authorityAllowed = false; managementMenu.Status = new(false, false, null, Available: false); WorkflowBody.Visibility = Visibility.Collapsed; FooterHost.Visibility = Visibility.Collapsed; if (initialConfiguration && headerImport is not null) headerImport.Visibility = Visibility.Collapsed; AuthorizationGate.Visibility = Visibility.Visible; gateTitle.Text = "管理授权状态无法读取"; gateDescription.Text = "已填写的内容暂存在本窗口。" + error.Message; }
            finally { checkingAuthority = false; }
        }
        authorizationTimer.Tick += async (_, _) => await RefreshAuthorization();
        Loaded += (_, _) => authorizationTimer.Start(); Closed += (_, _) => { authorizationTimer.Stop(); if (observedPreferences is { } preferences) preferences.PropertyChanged -= PreferencesChanged; };
        Loaded += async (_, _) => { await RefreshAuthorization(); if (authorityAllowed) await Run(Read); };
    }
    async Task ValidateBarcodeRule()
    {
        if (parameterEditor?.ApplicationId != "sportmaster") return;
        var policy = parameterEditor.Policy.BuildBarcodePolicy();
        var result = await Task.Run(() => parameterEditor.Preview(policy, ["__format_check__"]));
        if (result.Outcome is "InvalidPattern" or "FilterTimeout")
        {
            parameterEditor.Report(result.Detail); LocateFirstIssue();
            throw new InvalidOperationException("请修正条码规则后继续。");
        }
    }
    async Task CheckCandidate()
    {
        if (startupSaveUncertain) throw new InvalidOperationException("上次保存结果未确认，请先点击“重新读取配置…”。已填写的内容保留，未重复保存。");
        if (parameterEditor is { } editor && !await editor.PrepareCameraIdentitiesAsync())
        { LocateFirstIssue(); throw new InvalidOperationException("未能读取相机序列号，请检查标出的相机设置后重新保存。"); }
        var candidate = RequireDraft();
        await ValidateBarcodeRule();
        var report = await Task.Run(() => maintenance.CheckAsync(candidate));
        diagnosticDetails = string.Join("\n", report.Checks.Select(c => c.Layer + " · " + c.Status + " · " + c.Reason));
        checkedInput = null;
        if (report.Checks.Any(c => c.Status == "FAIL"))
        {
            foreach (var check in report.Checks.Where(c => c.Status == "FAIL")) parameterEditor?.Report(check.Reason);
            LocateFirstIssue();
            throw new InvalidOperationException("配置检查未通过：" + string.Join("；", report.Checks.Where(c => c.Status == "FAIL").Select(c => ChineseInterfaceText.ParameterMessage(c.Reason))));
        }
        checkedInput = Input(); checkedCandidateHash = report.CandidateHash;
        if (initialConfiguration) saveState.Text = SaveIsComplete ? "已保存，启动时检查" : report.Checks.Any(c => c.Status == "BLOCKED") ? "部署条件未满足" : "检查通过 · 未保存";
        Message(report.Checks.Any(c => c.Status == "BLOCKED") ? "参数检查通过，但部署条件未满足。可以先保存，启动前需处理部署条件。" : "参数检查通过。保存和启动时还会检查实际运行条件。");
        diagnosticDetails = string.Join("\n", report.Checks.Select(c => c.Layer + " · " + c.Status + " · " + c.Reason));
    }
    string diagnosticDetails = "";
    async Task SaveCandidate()
    {
        if (lastSaveConfirmed && !startupSaveUncertain && draft is not null && document?.Candidate is { } confirmed && Wire.Encode(RequireDraft()) == Wire.Encode(confirmed))
        {
            await RequireMaintenance();
            var preferences = draft.DisplayPreferences;
            preferences?.Save(); savedInput = Input();
            saveState.Text = preferences?.HasChanges == true ? "业务配置已保存；本机偏好待保存" : "已保存，启动时检查";
            Message("业务配置已保存，本次未重复提交。" + (preferences?.SaveSummary ?? "本机偏好未修改。"));
            refreshReview?.Invoke(); UpdateActions(); return;
        }
        lastSaveConfirmed = false;
        RequireChecked(); var candidate = RequireDraft(); var expected = document!.ConfigurationHash;
        if (startupSaveUncertain) throw new InvalidOperationException("上次保存结果未确认，请先重新读取配置，勿重复保存");
        startupSaveUncertain = true;
        try
        {
            var saved = await Task.Run(() => initialConfiguration ? maintenance.RepairInitialAsync(expected, candidate, reason) : maintenance.StageAsync(expected, candidate, reason));
            ConfirmSaved(saved);
            Message("业务配置已保存，下次启动检查生效；" + ChineseInterfaceText.Status(saved.ConfigurationStatus) + "。设备尚未启动。" + draft?.DisplayPreferences?.SaveSummary);
        }
        catch (Exception error)
        {
            diagnosticDetails += "\n保存结果：" + Ipc.Platform.DiagnosticText.Protect(error.ToString());
            var actual = await Task.Run(maintenance.ReadAsync);
            if (actual.Candidate is not null && (actual.ConfigurationStatus == "PendingRestart" || initialConfiguration && actual.ConfigurationStatus == "Seed") && Ipc.Platform.Wire.Hash(Ipc.Platform.Wire.Encode(actual.Candidate)) == checkedCandidateHash)
            { ConfirmSaved(actual); Message("已读回确认业务配置保存成功；本次未重复提交，下次启动核验。" + draft?.DisplayPreferences?.SaveSummary); }
            else if (actual.ConfigurationHash == expected) { startupSaveUncertain = false; Message("配置未变化，未保存；已填写的内容保留：" + error.Message); }
            else Message("保存结果未确认，请勿重复保存。保存结果：" + error.Message + "；当前配置：" + actual.ConfigurationStatus + "。重新读取前可先保存草稿。");
        }

    }
    void ConfirmSaved(StartupMaintenanceDocument saved)
    {
        if (saved.Candidate is null || Wire.Hash(Wire.Encode(saved.Candidate)) != checkedCandidateHash)
            throw new IOException("保存后的配置与本次检查的内容不一致，请重新读取配置后核对。");
        var preferences = draft?.DisplayPreferences;
        document = saved; draft = new(saved.Candidate, saved.Catalog) { DisplayPreferences = preferences };
        lastSaveConfirmed = true; startupSaveUncertain = false;
        if (saved.Candidate.ApplicationId == "sportmaster") preferences?.Save();
        Render(false); savedInput = Input();
        saveState.Text = preferences?.HasChanges == true ? "业务配置已保存；本机偏好待保存" : "已保存，启动时检查";
    }
    async Task ReleaseMaintenanceAsync()
    {
        if (maintenanceReleased) return;
        maintenanceReleasePending = true;
        if (maintenance is not null) await maintenance.DisposeAsync();
        maintenanceReleased = true; maintenanceReleasePending = false;
    }
    async Task StartReviewed()
    {
        RequireChecked(); var candidate = RequireDraft();
        if ((initialConfiguration && !lastSaveConfirmed) || startupSaveUncertain || savedInput != Input() || draft?.DisplayPreferences?.HasChanges == true || Wire.Encode(candidate) != Wire.Encode(document!.Candidate))
            throw new InvalidOperationException("配置尚未保存，请先保存。");
        SelectedLaunch = SelectedLaunch with { ConfigurationDirectory = config.Text, DataRoot = data.Text };
        await ReleaseMaintenanceAsync(); RetryRequested = true; CompleteDialog();
    }
    async Task ImportConfiguration()
    {
        await RequireMaintenance();
        if (draft is null || document?.Candidate is null) throw new InvalidOperationException("先读取当前配置。");
        var path = FilePick(this, "导入配置文件", "配置或设置模板|*.json");
        if (path is null) return;
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("配置文件超出4兆字节限制。");
        var replacement = StartupConfigurationFiles.Import(File.ReadAllText(path), document.Candidate, draft, document.Catalog);
        var comparison = new Window { FontFamily = UiTypography.Interface, FontSize = UiTypography.Body, Owner = this, Title = "导入配置预览", Width = 720, Height = 560, MinWidth = 520, MinHeight = 320, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        StartupAppearance.FitToWorkArea(comparison);
        var panel = new DockPanel { Margin = new(20) };
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, IsDefault = true, MinHeight = 44, MinWidth = 100, Margin = new(0, 0, 8, 0) };
        var apply = new Button { Content = "应用到当前填写内容", MinHeight = 44 };
        cancel.Click += (_, _) => comparison.DialogResult = false;
        apply.Click += (_, _) => comparison.DialogResult = true;
        footer.Children.Add(cancel); footer.Children.Add(apply); DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        comparison.Loaded += (_, _) => cancel.Focus();
        panel.Children.Add(new TextBox { Text = "只导入设置，不会保存或启动；本供包台的身份和模拟/生产用途不变。\n\n" + StartupConfigurationFiles.Describe(draft, replacement), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        apply.SetResourceReference(StyleProperty, "Primary"); comparison.Content = panel;
        if (comparison.ShowDialog() == true) { await RequireMaintenance(); draft = replacement; Render(false); Message("配置已导入填写区。检查后再保存配置。"); }
    }
    void OpenTools()
    {
        var menu = new ContextMenu { FontFamily = UiTypography.Interface, FontSize = UiTypography.Body };
        foreach (var kind in new[] { RepairAction.Import, RepairAction.SaveDraft, RepairAction.ContinueDraft }) Add(menu.Items, kind);
        menu.Items.Add(new Separator());
        var advanced = new MenuItem { Header = "备份、诊断与修复" };
        foreach (var kind in new[] { RepairAction.Read, RepairAction.Check, RepairAction.Save, RepairAction.ExportTemplate, RepairAction.Backup, RepairAction.Retention, RepairAction.Repair, RepairAction.AdoptRestore, RepairAction.Install }) Add(advanced.Items, kind);
        var diagnostic = new MenuItem { Header = "查看完整诊断…" };
        System.Windows.Automation.AutomationProperties.SetAutomationId(diagnostic, "Startup.OpenDiagnosticDetails");
        diagnostic.Click += (_, _) => ShowStartupDiagnostics();
        advanced.Items.Add(diagnostic); menu.Items.Add(advanced);
        var tools = actionButtons[RepairAction.Tools];
        menu.PlacementTarget = tools; tools.ContextMenu = menu; menu.IsOpen = true;
        void Add(ItemCollection items, RepairAction kind)
        {
            if (!actionButtons.TryGetValue(kind, out var target)) return;
            var item = new MenuItem { Header = target.Content, IsEnabled = CanExecute(kind) };
            item.Click += (_, _) => target.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); items.Add(item);
        }
    }

    void ShowStartupDiagnostics()
    {
        var dialog = new Window { FontFamily = UiTypography.Interface, FontSize = UiTypography.Body, Owner = this, Title = "启动诊断",
            Width = 760, Height = 520, MinWidth = 520, MinHeight = 320, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        StartupAppearance.FitToWorkArea(dialog);
        System.Windows.Automation.AutomationProperties.SetAutomationId(dialog, "StartupDiagnosticDetails");
        var content = new DockPanel { Margin = new(20) };
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) };
        var close = new Button { Content = "关闭", IsCancel = true, IsDefault = true, MinHeight = 44, MinWidth = 100 };
        System.Windows.Automation.AutomationProperties.SetAutomationId(close, "StartupDiagnostic.Close");
        close.Click += (_, _) => dialog.Close(); footer.Children.Add(close); DockPanel.SetDock(footer, Dock.Bottom); content.Children.Add(footer);
        content.Children.Add(new TextBox { Text = diagnosticDetails + "\n" + Status.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        dialog.Content = content; dialog.Loaded += (_, _) => close.Focus(); dialog.ShowDialog();
    }
    string Input() => config.Text + "\n" + data.Text + "\n" + reason + "\n" + (draft is null ? "" : Wire.Encode(draft.Capture()));
    Button Action(RepairAction kind, string label, Func<Task> execute)
    {
        var readOnly = kind is RepairAction.Read or RepairAction.Reload or RepairAction.PlanRestore or RepairAction.ReadRestore or RepairAction.Tools or RepairAction.Exit;
        var button = base.Action(label, async () =>
        {
            if (!CanExecute(kind)) throw new InvalidOperationException("当前任务不能执行此操作，请先核对原结果或读取配置。");
            if (kind is not (RepairAction.Read or RepairAction.Reload or RepairAction.Install or RepairAction.Previous or RepairAction.Next or RepairAction.Tools or RepairAction.Exit)) await RequireMaintenance();
            await execute();
        }, readOnly);
        button.Tag = kind;
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, "Startup." + kind);
        actionButtons.Add(kind, button); return button;
    }
    bool CanExecute(RepairAction kind)
    {
        if (RestoreAdopted) return kind == RepairAction.Exit;
        // A failed Dispose still owns the original SDK object. Permit only
        // retrying that cleanup or completing its already-confirmed save.
        if (maintenanceReleasePending && kind is not (RepairAction.Read or RepairAction.Reload or RepairAction.Exit or RepairAction.Tools) &&
            !(kind == RepairAction.SaveAndStart && SaveIsComplete) && !(kind == RepairAction.Start && savedInput == Input() && checkedInput == Input())) return false;
        return kind switch
        {
            RepairAction.Read or RepairAction.Reload or RepairAction.Install or RepairAction.Tools or RepairAction.Exit => true,
            RepairAction.Start => draft is not null && !startupSaveUncertain && savedInput == Input() && draft.DisplayPreferences?.HasChanges != true,
            RepairAction.Save or RepairAction.Repair => draft is not null && checkedInput == Input() && !startupSaveUncertain,
            RepairAction.Previous or RepairAction.Next => draft is not null,
            RepairAction.PlanRestore => maintenance is not null && !string.IsNullOrWhiteSpace(backup) && !string.IsNullOrWhiteSpace(restoreRoot),
            RepairAction.ReadRestore or RepairAction.OpenRestore => maintenance is not null,
            RepairAction.ExecuteRestore => recovery is not null && !UnknownOutcome,
            RepairAction.AdoptRestore => restoredPreview?.State is "Isolated" or "HandoffPrepared" or "ProjectionPending" or "AdoptedIsolated" && restoredBackup is not null,
            RepairAction.Rollback => ParcelStartupServices.ApplicationId == "sportmaster",
            RepairAction.ContinueDraft => draft is not null && savedDraft is not null,
            _ => draft is not null && (!startupSaveUncertain || kind is RepairAction.SaveDraft or RepairAction.ExportTemplate)
        };
    }
    void SelectRestoreStage(StartupRecoveryPreview original)
    {
        if (original.State is "Planned" or "CopyIncomplete" or "Isolated" or "HandoffPrepared" or "ProjectionPending" or "AdoptedIsolated") ResolveKnownMaintenanceOutcome();
        if (original.State is "Isolated" or "HandoffPrepared" or "ProjectionPending" or "AdoptedIsolated")
        { restoredPreview = original; restoredBackup = original.Backup ?? backup; recovery = null; }
        else { recovery = original.State is "Planned" or "CopyIncomplete" ? original : null; restoredPreview = null; restoredBackup = null; }
        if (restorePlanPreview is not null) restorePlanPreview.Text = original.Summary + "\n目标：" + original.Destination;
    }
    void InvalidateRestorePlan()
    {
        var hadPlan = recovery is not null; recovery = null;
        restoredPreview = null; restoredBackup = null;
        if (restorePlanPreview is not null) restorePlanPreview.Text = "恢复路径尚未检查，请重新检查恢复计划。";
        if (!rendering) { if (hadPlan) Message("恢复路径已修改，原计划已撤销，请重新检查。"); UpdateActions(); }
    }
    void SelectTask(RepairTask task)
    {
        if (!taskPages.TryGetValue(task, out var page) || page.Visibility != Visibility.Visible) return;
        selectedTask = task; tasks!.SelectedItem = page; UpdateActions();
    }
    async Task RequireMaintenance()
    {
        RequireDirectories();
        if (!(await ManagementMode.ManagementModeStatusAsync()).Authorized) throw new UnauthorizedAccessException("请先进入管理模式；已填写的内容保留。");
        if (maintenance is null) throw new InvalidOperationException("请先读取所选目录。");
    }
    bool MayDiscard() => preservedDraftInput == Input() || draft?.DisplayPreferences?.HasChanges != true && (savedInput is null || savedInput == Input()) || ConfirmDiscardInputs();
    void PreferencesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SmDisplayPreferenceDraft.OpeningMode) or nameof(SmDisplayPreferenceDraft.ShowPace) or nameof(SmDisplayPreferenceDraft.CueVolume)) Changed();
    }
    void Changed()
    {
        if (rendering || Busy) return;
        if (savedInput == Input()) return;
        checkedInput = null;
        saveState.Text = startupSaveUncertain ? "保存结果未确认" : "未保存";
        refreshReview?.Invoke(); UpdateActions();
        if (!initialConfiguration) Message("尚未保存；填写完成后点击“检查配置”。");
    }
    void RequireChecked()
    {
        if (checkedInput is null || checkedInput != Input()) throw new InvalidOperationException("检查结果尚未确认或填写内容已改变，请先检查当前配置。");
    }
    ParcelConfiguration RequireDraft()
    {
        if (draft is null || document is null) throw new InvalidOperationException("请先读取配置。配置文件损坏时，请用完整备份恢复或修复安装。");
        RequireDirectories();
        if (parameterEditor?.ValidateFields() == false) { LocateFirstIssue(); throw new InvalidOperationException("请修正标出的参数。"); }
        return draft.Build();
    }
    Action? focusAfterOperation;
    protected override void OperationCompleted()
    {
        savingCandidate = false;
        refreshReview?.Invoke(); UpdateActions();
        // Keep a partially saved first setup visible above the scrolling form;
        // a successful business CAS must not be presented as a failed check.
        if (initialConfiguration) Status.Visibility = maintenanceReleasePending || lastSaveConfirmed && draft?.DisplayPreferences?.HasChanges == true ? Visibility.Visible : Visibility.Collapsed;
        var focus = focusAfterOperation; focusAfterOperation = null;
        if (focus is not null) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, focus);
    }
    protected override void OperationFailed(Exception error)
    {
        if (!initialConfiguration) { base.OperationFailed(error); return; }
        diagnosticDetails = Ipc.Platform.DiagnosticText.Protect(diagnosticDetails + "\n" + error + "\n" + Status.Text);
        if (maintenanceReleasePending)
        {
            saveState.Text = lastSaveConfirmed ? "配置已保存；维护资源待释放" : "维护资源待释放";
            Message((startupSaveUncertain ? "上次保存结果仍未确认。" : lastSaveConfirmed ? "业务配置已保存。" : "原配置和填写内容保留。") +
                "维护服务尚未释放，原服务和窗口保留。处理原因后再次完成或关闭，将重试原服务。" + ChineseInterfaceText.ParameterMessage(error.Message));
            refreshReview?.Invoke(); return;
        }
        saveState.Text = error is UnauthorizedAccessException ? "需要重新验证" : startupSaveUncertain ? "保存结果未确认" : lastSaveConfirmed && draft?.DisplayPreferences?.HasChanges == true ? "业务配置已保存；本机偏好待保存" : savingCandidate ? "保存未完成" : "检查未通过";
        refreshReview?.Invoke();
    }
    void LocateFirstIssue()
    {
        var issue = parameterEditor?.Issues.FirstOrDefault();
        if (issue is null) return;
        if (!initialConfiguration) SelectTask(RepairTask.Parameters);
        var control = parameterControl; var path = issue.Path; var expectedInput = Input();
        focusAfterOperation = () =>
        {
            // Run releases disabled controls before this focus continuation.
            // A newer edit must never be interrupted by this older validation.
            if (!Busy && ReferenceEquals(control, parameterControl) && expectedInput == Input()) control?.NavigateTo(path);
        };
    }
    void RequireDirectories()
    {
        if (config.Text != SelectedLaunch.ConfigurationDirectory || data.Text != SelectedLaunch.DataRoot) throw new InvalidOperationException("目录已更改，请先重新读取；草稿不能保存到其他目录");
    }
    Task Read() => Read(false);
    async Task Read(bool discardAccepted)
    {
        if (!discardAccepted && !MayDiscard()) return;
        // Do not discard the old service (or its failed SDK owner) when rereading
        // another directory. Failed cleanup leaves this window available to retry.
        if (maintenance is not null) await maintenance.DisposeAsync();
        document = null; draft = null; savedDraft = null; lastSaveConfirmed = false; maintenance = null!; checkedInput = savedInput = null;
        try
        {
            SelectedLaunch = SelectedLaunch with { ConfigurationDirectory = Path.GetFullPath(config.Text), DataRoot = Path.GetFullPath(data.Text) };
            config.Text = SelectedLaunch.ConfigurationDirectory; data.Text = SelectedLaunch.DataRoot;
            maintenance = ParcelStartupServices.Maintenance(SelectedLaunch);
            maintenanceReleased = false;
            document = await Task.Run(maintenance.ReadAsync); startupSaveUncertain = false; recovery = null; savedDraft = null;
            string? unavailableDraft = null;
            try { if (document.Candidate is not null) savedDraft = await Task.Run(maintenance.ReadDraftAsync); }
            catch (System.ComponentModel.Win32Exception error)
            {
                // A recovered machine-DPAPI draft is private, non-authoritative input.
                // Keep its bytes and CAS protection; it cannot hide the repair editor.
                unavailableDraft = "\n草稿不可用：本机无法解密该草稿（" + error.NativeErrorCode + "）。草稿文件保留不覆盖；请在当前配置上重新填写密码类参数后保存。";
            }
            Render();
            var resumed = initialConfiguration && savedDraft is { BaseChanged: false } && draft is not null && savedDraft.Editor.Values.GetValueOrDefault("editor") != "live-v2";
            if (resumed) { if (document?.Candidate?.ApplicationId == "sportmaster") (draft!.DisplayPreferences ??= new()).Restore(savedDraft!.Editor.Values); draft = draft!.WithRestored(savedDraft!.Editor); Render(false); preservedDraftInput = savedInput = Input(); }
            diagnosticDetails = string.Join("\n", document!.Diagnostics) + "\n" + document.Catalog.InventoryDiagnostic + unavailableDraft;
            Message(initialConfiguration
                ? document.Candidate is null ? "无法读取配置，原文件已保留。请使用启动修复处理后重新打开。"
                    : unavailableDraft is not null ? "草稿不可用：原文件已保留，请重新填写。"
                    : resumed ? "已继续上次未完成配置。" : "填写当前供包台，或从下方导入配置文件。" + (savedDraft is null ? "" : "草稿与当前配置不同，草稿已保留。请核对后填写。")
                : diagnosticDetails);
        }
        catch (Exception error)
        {
            diagnosticDetails = Ipc.Platform.DiagnosticText.Protect(error.ToString());
            Render(); throw;
        }
    }
    void Render(bool replaceDraft = true)
    {
        rendering = true; checkedInput = null;
        // Reparent the same directory controls; navigation never reconstructs a draft or runtime.
        if (tasks is not null) foreach (TabItem tab in tasks.Items) if (tab.Content is Panel panel) panel.Children.Clear();
        Body.Children.Clear();
        foreach (var field in pathFields)
        {
            if (LogicalTreeHelper.GetParent(field) is Panel oldParent) oldParent.Children.Remove(field);
            Body.Children.Add(field);
        }
        var policyStart = Body.Children.Count; var reviewStart = policyStart;
        ParameterSettingsControl? sharedEditor = null;
        if (replaceDraft) draft = document?.Candidate is { } candidate ? new(candidate, document.Catalog, initialConfiguration && !document.ExistingConfiguration) : null;
        if (draft is not null)
        {
            var c = document!.Candidate!;
            Note($"{ChineseInterfaceText.BusinessName(c.ApplicationId)} · {ChineseInterfaceText.Choice(c.Mode)} · 修订 {document.Revision}\n供包台身份只读；设备尚未启动。");
            Body.Children.Add(new Expander { Header = "配置信息", Content = new TextBox { IsReadOnly = true, Text = $"配置校验码：{document.ConfigurationHash}\n设备列表只表示可选项，不表示已连接。启动时会完整检查运行条件。", TextWrapping = TextWrapping.Wrap }, Margin = new(0, 8, 0, 8) });
            policyStart = Body.Children.Count;
            Note("首次配置与日常设置使用相同的参数和检查规则。投递位数量和设备身份不在此修改。");
            parameterEditor = draft.CreateParameterEditor((p, samples) => maintenance.TestBarcodeFilterAsync(p, samples), new(config.Text, data.Text));
            if (!ReferenceEquals(observedPreferences, parameterEditor.DisplayPreferences))
            {
                if (observedPreferences is { } prior) prior.PropertyChanged -= PreferencesChanged;
                observedPreferences = parameterEditor.DisplayPreferences;
                if (observedPreferences is { } current) current.PropertyChanged += PreferencesChanged;
            }
            parameterEditor.CameraSerialReader = async (ip, engine) =>
            {
                try { return await maintenance.ReadCameraSerialAsync(ip, engine); }
                catch (Exception error) { diagnosticDetails += "\n相机身份读取：" + Ipc.Platform.DiagnosticText.Protect(error.ToString()); throw; }
            };
            sharedEditor = parameterControl = new ParameterSettingsControl { FirstConfiguration = initialConfiguration, OpeningModeDraft = parameterControl?.OpeningModeDraft, Model = parameterEditor, MinHeight = initialConfiguration ? 0 : 300, Height = initialConfiguration ? double.NaN : 430 };
            Body.Children.Add(sharedEditor);
            reviewStart = Body.Children.Count;
            if (!initialConfiguration) Field("维护原因（必填，记入审计）", reason, v => reason = v);
        }
        var recoveryStart = Body.Children.Count;
        if (!initialConfiguration)
        {
            Note("完整备份恢复：校验备份数据库和同名 .recovery 目录，恢复到一个新目录。恢复后软件不会自动开始作业。");
            var backupFolder = Folder("备份目录（最多显示 256 份）", backupCatalogRoot);
            backupFolder.TextChanged += (_, _) => backupCatalogRoot = backupFolder.Text;
            var backupRows = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, MinHeight = 100, MaxHeight = 260, RowHeight = 44 };
            foreach (var column in new[] { ("时间", "At"), ("完整性", "Status"), ("字节", "Bytes"), ("备份", "Path") }) backupRows.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new System.Windows.Data.Binding(column.Item2), Width = column.Item2 == "Path" ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto });
            var listBackups = new Button { Content = "读取并校验备份列表", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
            listBackups.Click += async (_, _) => await Run(async () => { await RequireMaintenance(); var selected = backupCatalogRoot; backupRows.ItemsSource = await RunMaintenance("校验备份目录\n目录：" + selected + "\n不修改任何文件。", (progress, token) => maintenance.ListBackupsAsync(selected, progress, token), readOnly: true); Message("已读取备份列表。“Verified”表示备份文件完整，可用于恢复。"); });
            Body.Children.Add(listBackups); Body.Children.Add(backupRows);
            var backupBox = Field("完整备份数据库", backup, v => { backup = v; InvalidateRestorePlan(); });
            System.Windows.Automation.AutomationProperties.SetAutomationId(backupBox, "Startup.RestoreBackup");
            backupRows.SelectionChanged += (_, _) => { if (backupRows.SelectedItem is StartupBackupItem item) { backupBox.Text = item.Path; Message(item.Status + " · " + item.Summary); } };
            var choose = new Button { Content = "选择完整备份…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
            choose.Click += (_, _) => { var selected = FilePick(this, "选择完整备份数据库", "SQLite 备份|*.db;*.sqlite;*.backup|所有文件|*.*"); if (selected is not null) backupBox.Text = selected; }; Body.Children.Add(choose);
            var destination = Folder("恢复到新目录（不覆盖原目录）", restoreRoot);
            System.Windows.Automation.AutomationProperties.SetAutomationId(destination, "Startup.RestoreDestination");
            destination.TextChanged += (_, _) => { restoreRoot = destination.Text; InvalidateRestorePlan(); };
            restorePlanPreview = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80,
                Text = (recovery ?? restoredPreview) is { } prior ? prior.Summary + "\n目标：" + prior.Destination : "选择完整备份和独立恢复目录，或读取原恢复。检查不会复制、接管或启动。" };
            Body.Children.Add(restorePlanPreview);
        }
        var fields = Body.Children.Cast<UIElement>().ToArray(); Body.Children.Clear();
        taskPages.Clear();
        tasks = initialConfiguration ? null : new TabControl { Name = "StartupTasks", Margin = new(0, 8, 0, 8) };
        void Section(RepairTask task, string title, string guidance, IEnumerable<UIElement> items)
        {
            var panel = new StackPanel { Margin = new(16) };
            panel.Children.Add(new TextBlock { Text = guidance, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16), FontSize = UiTypography.Body });
            foreach (var item in items) panel.Children.Add(item);
            var page = new TabItem { Header = title, Content = panel, MinHeight = 44, Tag = task };
            System.Windows.Automation.AutomationProperties.SetAutomationId(page, "StartupTask." + task);
            taskPages.Add(task, page); tasks!.Items.Add(page);
        }
        if (initialConfiguration)
        {
            if (sharedEditor is null)
            {
                WorkflowBody.Content = new TextBlock { Text = "无法读取配置，请查看诊断后使用启动修复。原文件保留。", TextWrapping = TextWrapping.Wrap };
            }
            else
            {
                var review = new StackPanel();
                var state = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16) };
                var summary = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = Brushes.Transparent, Padding = new(0) };
                var diagnostic = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = Brushes.Transparent };
                review.Children.Add(state);
                var sections = new StackPanel(); review.Children.Add(sections);
                review.Children.Add(new Expander { Header = "本次更改与配置详情", Content = summary });
                review.Children.Add(new Expander { Header = "配置检查诊断详情", Content = diagnostic, Margin = new(0, 16, 0, 0) });
                refreshReview = () =>
                {
                    state.Text = maintenanceReleasePending ? "维护资源尚未释放；处理原因后再次完成或关闭，原服务和保存结果保留。" : startupSaveUncertain ? "上次保存结果未确认，暂不重复保存。" : SaveIsComplete ? "配置已保存，完成后自动检查并启动供包台。" : lastSaveConfirmed && draft?.DisplayPreferences?.HasChanges == true ? "业务配置已保存；本机偏好尚未全部保存，请重试保存。本次不会重复提交业务配置。" : "检查填写内容并保存配置，完成后自动检查并启动。";
                    diagnostic.Text = Ipc.Platform.DiagnosticText.Protect(diagnosticDetails);
                    sections.Children.Clear();
                    foreach (var section in StartupConfigurationFiles.ReviewSections(draft!))
                    {
                        var back = new Button { Content = new TextBlock { Text = section.Title + "\n" + section.Summary, TextWrapping = TextWrapping.Wrap }, MinHeight = 44, Margin = new(0, 0, 0, 6), HorizontalContentAlignment = HorizontalAlignment.Left };
                        back.Click += (_, _) => sharedEditor.SelectCategory(section.Category); sections.Children.Add(back);
                    }
                    try { summary.Text = StartupConfigurationFiles.Summary(draft!, document!.Candidate!) + "\n\n本次更改\n" + StartupConfigurationFiles.Describe(new StartupConfigurationDraft(document.Candidate!, document.Catalog), draft!); }
                    catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
                    { summary.Text = "还有参数需要修改，请按检查结果逐项修正。"; diagnostic.Text += "\n" + Ipc.Platform.DiagnosticText.Protect(error.Message); }
                };
                sharedEditor.SetReviewContent(review);
                WorkflowBody.Content = sharedEditor;
                sharedEditor.SelectedPageChanged += (_, _) =>
                {
                    WorkflowBusinessName.Text = sharedEditor.PageProgress + " · 相机和 RCS 可稍后配置";
                    WorkflowBusinessName.Visibility = Visibility.Visible;
                    selectedParameterCategory = sharedEditor.SelectedCategory;
                    if (sharedEditor.IsReviewPage) refreshReview?.Invoke(); UpdateActions();
                };
                sharedEditor.MaintenanceRequested += (_, _) => sharedEditor.ShowReview();
                sharedEditor.SelectCategory(selectedParameterCategory);
                refreshReview();
            }
        }
        else
        {
            Section(RepairTask.Directory, "目录与工位", "确认配置目录和数据目录。更改目录后需重新读取。", fields.Take(policyStart));
            Section(RepairTask.Parameters, "设备与参数", "为每个投递位选择相机等设备。在此选择设备不会连接设备。", fields.Skip(policyStart).Take(reviewStart - policyStart));
            Section(RepairTask.Review, "检查与启动", "先检查配置，再保存并启动。配置未能生效时，可在“恢复与维护”中修复。", fields.Skip(reviewStart).Take(recoveryStart - reviewStart));
            Section(RepairTask.Recovery, "恢复与维护", "备份、恢复、换机接管和程序修复都需单独确认后执行，切换到本页不会自动执行。", fields.Skip(recoveryStart));
            if (draft is null)
            {
                // A read failure is not an empty configuration. Keep original
                // directories and recovery available; never offer an empty save form.
                var failure = (StackPanel)taskPages[RepairTask.Directory].Content;
                var explanation = new TextBlock { Text = "原配置未载入，参数没有被清空。请核对原目录或查看诊断；不要重新首配或覆盖原账。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 16, 0, 12) };
                System.Windows.Automation.AutomationProperties.SetAutomationId(explanation, "StartupReadFailure");
                failure.Children.Insert(0, explanation);
                failure.Children.Insert(1, new Expander { Header = "读取失败诊断", Content = new TextBox { Text = document is null ? diagnosticDetails : string.Join("\n", document!.Diagnostics), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
                foreach (var task in new[] { RepairTask.Parameters, RepairTask.Review }) taskPages[task].Visibility = Visibility.Collapsed;
                if (selectedTask is RepairTask.Parameters or RepairTask.Review) selectedTask = RepairTask.Directory;
            }
            if (OpenRecoveryScenario) { selectedTask = RepairTask.Recovery; OpenRecoveryScenario = false; }
            tasks!.SelectedItem = taskPages[selectedTask];
            if (SceneOnly && draft is not null)
            {
                if (selectedTask == RepairTask.Parameters) SelectTask(RepairTask.Directory);
                var selectedContent = new FrameworkElementFactory(typeof(ContentPresenter));
                selectedContent.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding("SelectedContent") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
                tasks.Template = new ControlTemplate(typeof(TabControl)) { VisualTree = selectedContent };
                var recoveryScene = selectedTask == RepairTask.Recovery;
                foreach (var (task, page) in taskPages) page.IsEnabled = recoveryScene ? task == RepairTask.Recovery : task is RepairTask.Directory or RepairTask.Review;
                var routes = new WrapPanel { Margin = new(0, 0, 0, 8) };
                foreach (var route in recoveryScene ? new[] { ("恢复备份与换机", RepairTask.Recovery) } : new[] { ("检查目录与配置", RepairTask.Directory), ("检查并启动", RepairTask.Review) })
                {
                    var button = new Button { Content = route.Item1, MinHeight = 44, Margin = new(0, 0, 8, 0) };
                    button.Click += (_, _) => { SelectTask(route.Item2); }; routes.Children.Add(button);
                }
                Body.Children.Insert(0, routes);
            }
        }
        if (!initialConfiguration && tasks is not null)
        {
            tasks.SelectionChanged += (_, e) => { if (e.OriginalSource == tasks) { if (tasks.SelectedItem is TabItem { Tag: RepairTask task }) selectedTask = task; UpdateActions(); } };
            Body.Children.Add(tasks);
        }
        UpdateActions(); rendering = false;
        if (replaceDraft) savedInput = Input();
    }
    void UpdateActions()
    {
        foreach (var (kind, button) in actionButtons)
        {
            var visible = initialConfiguration ? draft is null ? kind is RepairAction.Reload or RepairAction.Exit or RepairAction.OpenRestore : kind switch
            {
                RepairAction.Import or RepairAction.SaveDraft or RepairAction.Exit or RepairAction.OpenRestore => true,
                RepairAction.Previous => parameterControl?.HasPreviousPage == true,
                RepairAction.Next => parameterControl?.NextPageTitle is not null && !startupSaveUncertain,
                RepairAction.SaveAndStart => parameterControl?.IsReviewPage == true && !startupSaveUncertain,
                RepairAction.Reload => startupSaveUncertain,
                _ => false
            } : draft is null ?
                kind is RepairAction.Read or RepairAction.Install or RepairAction.Tools or RepairAction.Exit || selectedTask == RepairTask.Recovery && kind is RepairAction.PlanRestore or RepairAction.ReadRestore or RepairAction.ExecuteRestore or RepairAction.AdoptRestore or RepairAction.Rollback :
                kind is RepairAction.Tools or RepairAction.Exit || kind switch
                {
                    RepairAction.Read => selectedTask == RepairTask.Directory,
                    RepairAction.Import => selectedTask == RepairTask.Parameters && !SceneOnly,
                    RepairAction.SaveDraft => selectedTask == RepairTask.Parameters && !SceneOnly,
                    RepairAction.Check or RepairAction.Start => selectedTask == RepairTask.Review,
                    RepairAction.SaveAndStart => selectedTask == RepairTask.Review && !SceneOnly && savedInput != Input(),
                    RepairAction.Install or RepairAction.PlanRestore or RepairAction.ReadRestore => selectedTask == RepairTask.Recovery,
                    RepairAction.Rollback => selectedTask == RepairTask.Recovery && document?.Candidate?.ApplicationId == "sportmaster",
                    RepairAction.ExecuteRestore => selectedTask == RepairTask.Recovery && recovery is not null,
                    RepairAction.AdoptRestore => selectedTask == RepairTask.Recovery && restoredPreview is not null,
                    RepairAction.Previous => !SceneOnly && selectedTask is RepairTask.Parameters or RepairTask.Review,
                    RepairAction.Next => !SceneOnly && selectedTask is RepairTask.Directory or RepairTask.Parameters,
                    _ => false
                };
            button.Visibility = (RestoreAdopted ? kind == RepairAction.Exit : visible) ? Visibility.Visible : Visibility.Collapsed;
            button.IsEnabled = CanExecute(kind);
            button.IsDefault = false;
            if (kind == RepairAction.Next) button.Content = initialConfiguration
                ? parameterControl?.NextPageIsReview == true ? "检查配置" : "继续：" + parameterControl?.NextPageTitle
                : selectedTask == RepairTask.Directory ? "继续填写参数" : "检查配置";
            if (kind == RepairAction.SaveAndStart && initialConfiguration) button.Content = SaveIsComplete ? "完成并启动" : "保存配置";
            button.IsCancel = kind == RepairAction.Exit;
        }
        var primary = RestoreAdopted ? RepairAction.Exit : initialConfiguration ? draft is null ? RepairAction.Reload : parameterControl?.IsReviewPage == true ? RepairAction.SaveAndStart : RepairAction.Next
            : selectedTask switch { RepairTask.Directory => RepairAction.Read, RepairTask.Parameters => RepairAction.Next, RepairTask.Review => SceneOnly || savedInput == Input() ? RepairAction.Start : RepairAction.SaveAndStart, _ => restoredPreview is not null ? RepairAction.AdoptRestore : recovery is not null ? RepairAction.ExecuteRestore : RepairAction.PlanRestore };
        foreach (var (kind, button) in actionButtons)
        {
            if (button.Visibility != Visibility.Visible) continue;
            button.ClearValue(StyleProperty); button.ClearValue(Button.BackgroundProperty); button.ClearValue(Button.ForegroundProperty); button.ClearValue(Button.BorderBrushProperty);
            if (kind == primary) { button.SetResourceReference(StyleProperty, initialConfiguration ? "StartupPrimary" : "Primary"); button.IsDefault = kind is not (RepairAction.Backup or RepairAction.Install or RepairAction.ExecuteRestore or RepairAction.AdoptRestore); }
        }
    }
}
