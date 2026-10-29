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
    protected readonly WrapPanel Actions = new();
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
            installationSteps[i].Foreground = i == step ? new SolidColorBrush(Color.FromRgb(8, 102, 232)) : new SolidColorBrush(Color.FromRgb(98, 107, 120));
            installationSteps[i].FontWeight = i == step ? UiTypography.Emphasis : FontWeights.Normal;
        }
    }
    protected bool Busy;
    protected bool UnknownOutcome;
    protected bool CloseAfterCancellation;
    protected StartupWorkflowWindow(string title, bool installation = false)
    {
        Form = Body;
        ProductBrand.Apply(this);
        VersionLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Title = title; FontSize = UiTypography.Body; Width = 960; Height = 760; MinWidth = 680; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(241, 244, 247)); Foreground = new SolidColorBrush(Color.FromRgb(25, 39, 54));
        if (installation)
        {
            Width = 880; Height = 560; MinWidth = 600; MinHeight = 380; StartupAppearance.Apply(this);
            var layout = new Grid(); layout.ColumnDefinitions.Add(new() { Width = new(176) }); layout.ColumnDefinitions.Add(new());
            var rail = new StackPanel { Margin = new(24, 28, 24, 24) };
            rail.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Ipc.Desktop;component/Assets/cainiao.png")), Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Left });
            rail.Children.Add(new TextBlock { Text = "菜鸟供包台", FontSize = UiTypography.Body, FontWeight = UiTypography.Emphasis, Margin = new(0, 16, 0, 16) });
            foreach (var (label, index) in new[] { ("1  安装选项", 0), ("2  确认并安装", 1), ("3  安装完成", 2) })
            { installationSteps[index] = new TextBlock { Text = label, Margin = new(0, 12, 0, 12), TextWrapping = TextWrapping.Wrap }; rail.Children.Add(installationSteps[index]); }
            var sidebar = new Border { Background = new SolidColorBrush(Color.FromRgb(245, 248, 252)), Child = rail }; layout.Children.Add(sidebar);
            var content = new Grid { Margin = new(28, 24, 28, 20) }; Grid.SetColumn(content, 1);
            content.RowDefinitions.Add(new() { Height = GridLength.Auto }); content.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); content.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var header = new StackPanel(); header.Children.Add(InstallationHeading); header.Children.Add(VersionLabel); header.Children.Add(new ScrollViewer{Content=Status,MaxHeight=48,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}); header.Children.Add(CreateMaintenancePanel(compact:true));
            var options = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(options, 1);
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
            if (UnknownOutcome && !readOnly) { Message("原操作结果仍待核对，禁止重复提交。请使用只读核对或关闭并保留记录。"); return; }
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
        if(error is MaintenanceCancelledException){Message(error.Message);return;}
        UnknownOutcome |= error is InstallationOutcomeUnknownException;
        Status.Text = UnknownOutcome ? "原维护操作结果待核对。请保留原输入和回执，禁止重复提交。\n" + ChineseInterfaceText.ParameterMessage(error.Message)
            : "未完成：" + ChineseInterfaceText.ParameterMessage(error.Message) + "\n已有数据与配置保留。请核对原因后再操作。";
        Status.Foreground = UnknownOutcome ? Brushes.DarkGoldenrod : Brushes.Firebrick;
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
    bool savingCandidate;
    string? checkedCandidateHash;
    int selectedTask;
    bool showMaintenanceActions;
    bool authorityAllowed;
    readonly ManagementModeMenu managementMenu = new() { UnauthorizedLabel = "未授权" };
    readonly TextBlock saveState = new() { Text = "未保存", FontSize = UiTypography.Label, Foreground = Brushes.SlateGray, VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 12, 0) };
    Button? headerImport;
    Action? refreshReview;
    internal Func<bool> ConfirmDiscardInputs { get; set; } = null!;
    IStartupMaintenance maintenance = null!;
    StartupMaintenanceDocument? document;
    StartupConfigurationDraft? draft;
    StartupSavedDraft? savedDraft;
    StartupRecoveryPreview? recovery;
    StartupRecoveryPreview? restoredPreview;
    string? restoredBackup;
    readonly TextBox config, data;
    string reason = "", backup = "", restoreRoot = "", backupCatalogRoot = "";
    public BusinessLaunch SelectedLaunch { get; private set; }
    IManagementMode ManagementMode => ParcelStartupServices.ManagementMode(SelectedLaunch);
    public bool RetryRequested { get; private set; }
    public bool ReplacementLaunched { get; private set; }
    public bool ConfigurationSaved { get; private set; }
    public StartupRepairWindow(BusinessLaunch launch, string? failure = null, bool initialConfiguration = false) : base(initialConfiguration ? "首次配置 · 菜鸟供包台" : "启动修复 · 离线维护")
    {
        SelectedLaunch = launch;
        this.initialConfiguration = initialConfiguration;
        if (initialConfiguration) { StartupAppearance.Apply(this); Width = 960; Height = 656; WorkflowHeading.FontSize = UiTypography.PageTitle; WorkflowBusinessName.Text = ChineseInterfaceText.BusinessName(ParcelStartupServices.ApplicationId); WorkflowBusinessName.Visibility = Visibility.Visible; MinHeight = 360; WorkflowBody.Visibility = Visibility.Collapsed; FooterHost.Visibility = Visibility.Collapsed; Status.Visibility = Visibility.Collapsed; }
        ConfirmDiscardInputs = () => MessageBox.Show(this, "有尚未保存的输入。返回可保存加密草稿；继续将放弃当前未保存输入，既有配置账和已保存草稿保留。", "保护未完成配置", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
        if (initialConfiguration) reason = "首次配置供包台";
        Message("无需业务启动成功。检查和编辑不连接设备；保存为 Pending，正常启动仍检查原恢复责任。" + (failure is null ? "" : "\n启动诊断：" + failure));
        config = Folder("原配置目录（包含 business.json）", launch.ConfigurationDirectory);
        data = Folder("原数据根（保留 Simulation/Production、站和 Application 层级）", launch.DataRoot);
        pathFields = Body.Children.Cast<UIElement>().ToArray();
        SessionActions.Children.Add(managementMenu);
        var administratorLogin = new Button { Content = "进入管理模式…", MinHeight = 44 };
        var gateTitle = new TextBlock { FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis, TextWrapping = TextWrapping.Wrap };
        var gateDescription = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 20), FontSize = UiTypography.Body, Foreground = new SolidColorBrush(Color.FromRgb(83, 101, 119)) };
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
            catch (Exception error) { Message("管理模式操作未完成：" + error.Message + " 原输入保留。"); }
            finally { administratorLogin.IsEnabled = true; managementMenu.IsEnabled = true; }
        }
        administratorLogin.Click += async (_, _) => await ManagementAction();
        managementMenu.ModeActionRequested += async (_, _) => await ManagementAction();
        gateActions.Children.Insert(0, administratorLogin);
        Action("读取所选配置", Read);
        Action("分层检查与变更预览", CheckCandidate);
        Action("保存未完成草稿", async () =>
        {
            if (draft is null || document is null) throw new InvalidOperationException("先读取原配置");
            RequireDirectories();
            savedDraft = await Task.Run(() => maintenance.SaveDraftAsync(document.ConfigurationHash, savedDraft?.DraftHash, draft.Capture()));
            savedInput = Input();
            Message("未完成输入已保存；下次打开可继续填写。");
            if (initialConfiguration) { Busy = false; Close(); }
        });
        Action("继续已保存草稿", () =>
        {
            if (savedDraft is null || draft is null) throw new InvalidOperationException("没有可继续草稿");
            if (savedDraft.BaseChanged) throw new InvalidOperationException("草稿基线已变化；先比较当前配置，不自动套用或覆盖。");
            if (savedDraft.Editor.Values.GetValueOrDefault("editor") == "live-v2") throw new InvalidOperationException("这是运行内统一设置的草稿；在原Application的设备与维护页选择继续未完成设置。当前首配输入与加密原草稿均保留。");
            draft = draft.WithRestored(savedDraft.Editor); Render(false); Message("已恢复未完成输入；参数改变后原检查失效。请重新检查候选。"); return Task.CompletedTask;
        });
        Action("导出新站模板…", () =>
        {
            if (draft is null || document?.Candidate is null) throw new InvalidOperationException("先读取配置");
            var picker = new SaveFileDialog { FileName = "site-template.json", Filter = "无业务状态模板|*.json" };
            if (picker.ShowDialog(this) == true) { using var file = new StreamWriter(new FileStream(picker.FileName, FileMode.CreateNew)); file.Write(Wire.Encode(StartupSiteTemplates.Export(document.Candidate.ApplicationId, draft.Capture()))); Message("已导出同版安全模板：只含非生效策略和提供者选择，不含原站/设备/目标/秘密/编号范围或业务数据。"); }
            return Task.CompletedTask;
        });
        headerImport = Action("导入配置文件…", ImportConfiguration);
        Action(initialConfiguration ? "保存首配待生效…" : "保存待生效修订…", SaveCandidate);
        Action("受控修复未确认配置…", async () =>
        {
            RequireChecked(); var candidate = RequireDraft();
            if (MessageBox.Show(this, "将保留原配置及确认/失败记录。仅在原实例已释放、原责任已清且候选严格校验通过时，允许下一次启动直接尝试候选。失败不会替换Effective。", "确认初始配置修复", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            document = await Task.Run(() => maintenance.RepairInitialAsync(document!.ConfigurationHash, candidate, reason)); Render();
            Message("修复意图已保存并读回；重试正常启动会再次检查原责任及资格，资源全部成功后才确认Effective。");
        });
        Action("创建完整备份…", async () =>
        {
            var picker = new SaveFileDialog { FileName = "complete-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db", Filter = "完整恢复集数据库|*.db" };
            if (picker.ShowDialog(this) != true) return;
            await RunMaintenance("创建完整备份\n原数据："+(document?.RuntimeDirectory??launch.DataRoot)+"\n目标："+picker.FileName+" 及同名 .recovery 目录",
                (progress,token)=>maintenance.BackupAsync(picker.FileName,progress,token)); backupCatalogRoot = Path.GetDirectoryName(picker.FileName)!;
            Message("完整备份已完成；数据库和同名.recovery目录必须一起保留。可在备份列表重新校验并预检恢复。");
        });
        Action("已结历史到期维护…", () =>
        {
            if (maintenance is not IStartupRetentionMaintenance retention) throw new NotSupportedException("当前组合未提供独立到期维护");
            new HistoryRetentionWindow(retention, ManagementMode) { Owner = this }.ShowDialog(); return Task.CompletedTask;
        });
        Action("接管已恢复的原机目录…", async () =>
        {
            if (restoredPreview is null || restoredBackup is null) throw new InvalidOperationException("先完成完整恢复；新根必须尚未启动，原根/新根均无运行实例");
            var registration = WindowsDeploymentSetup.Registration(ParcelStartupServices.ApplicationId);
            if (!File.Exists(registration)) throw new InvalidOperationException("缺少原机安装登记；换机恢复保持隔离，须取得来源退役和重新资格证据");
            if (MessageBox.Show(this, "将先封存原根的新启动，再把受保护部署登记切到新根。原目录/数据不删除；新根继续隔离，仍须维护认证并核对原请求、设备、外部事实和编号范围。", "确认原机迁移接管", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            await Task.Run(() => maintenance.AdoptRestoreAsync(restoredBackup, restoredPreview.DataRoot, restoredPreview.BackupSha256, Wire.FileHash(registration)));
            Message("原机目录接管已提交。原根启动围栏保留，新根仍隔离；重试启动后先在维护页读取并核对交接。不能自动Resume或重发旧请求。");
        });
        Action("安装 / 修复程序…", async () =>
        {
            if (!MayDiscard()) return;
            var setup = new StartupSetupWindow(SelectedLaunch) { Owner = this }; setup.ShowDialog();
            if (setup.Launched) { ReplacementLaunched = true; Busy = false; Close(); return; }
            if (setup.Installed is { } installed) { config.Text = installed.ConfigurationDirectory; data.Text = installed.DataRoot; await Read(true); }
        });
        Action(initialConfiguration ? "按当前首配启动" : "重试正常启动", StartReviewed);
        Action("上一步", () => { if (initialConfiguration) parameterControl?.MovePrevious(); else if (tasks is not null) tasks.SelectedIndex = Math.Max(0, tasks.SelectedIndex - 1); return Task.CompletedTask; });
        var next = Action("下一步", async () =>
        {
            if (initialConfiguration)
            {
                if (parameterControl is null || !await parameterControl.MoveNextAsync()) { LocateFirstIssue(); throw new InvalidOperationException("请修正当前页标出的参数后再继续；其他输入保留。"); }
                saveState.Text = lastSaveConfirmed && savedInput == Input() ? "已保存待启动检查" : "未保存";
                if (parameterControl.IsReviewPage) await CheckCandidate();
            }
            else if (tasks is not null) tasks.SelectedIndex = Math.Min(3, tasks.SelectedIndex + 1);
        });
        next.Background = new SolidColorBrush(Color.FromRgb(32, 95, 206)); next.Foreground = Brushes.White;
        Action("维护操作", () => { showMaintenanceActions = !showMaintenanceActions; UpdateActions(); return Task.CompletedTask; });
        Action(initialConfiguration ? "保存配置" : "保存并启动", async () =>
        {
            await CheckCandidate();
            savingCandidate = true;
            await SaveCandidate();
            if (!lastSaveConfirmed || startupSaveUncertain || savedInput != Input() || Wire.Encode(RequireDraft()) != Wire.Encode(document!.Candidate))
                throw new InvalidOperationException("尚未确认保存成功；请按提示核对，当前输入保留。");
            savingCandidate = false;
            if (initialConfiguration) { saveState.Text = "已保存待启动检查"; refreshReview?.Invoke(); UpdateActions(); }
            else { await CheckCandidate(); await StartReviewed(); }
        });
        if (initialConfiguration) Action("完成", () =>
        {
            if (!lastSaveConfirmed || startupSaveUncertain || savedInput != Input() || Wire.Encode(RequireDraft()) != Wire.Encode(document!.Candidate)) throw new InvalidOperationException("请先核对并保存当前配置。");
            ConfigurationSaved = true; CompleteDialog(); return Task.CompletedTask;
        });
        if (initialConfiguration) Action("重新读取配置…", Read);
        Action("稍后配置", () => { Busy = false; Close(); return Task.CompletedTask; });
        Action("配置工具…", () => { OpenTools(); return Task.CompletedTask; });
        if (initialConfiguration)
        {
            foreach (var button in Actions.Children.OfType<Button>().ToArray())
                if ((string)button.Content is not ("上一步" or "下一步" or "保存配置" or "完成" or "重新读取配置…" or "导入配置文件…" or "保存未完成草稿")) Actions.Children.Remove(button);
            var saveDraft = Actions.Children.OfType<Button>().Single(b => (string)b.Content == "保存未完成草稿");
            saveDraft.Content = "保存草稿并退出";
            Actions.Children.Remove(saveDraft);
            var draftActions = new StackPanel { Orientation = Orientation.Horizontal }; draftActions.Children.Add(saveDraft); draftActions.Children.Add(saveState);
            DockPanel.SetDock(draftActions, Dock.Left); FooterHost.Children.Insert(0, draftActions); Actions.HorizontalAlignment = HorizontalAlignment.Right;
            Actions.Children.Remove(headerImport); headerImport.Content = "导入配置"; headerImport.Margin = new(0, 0, 12, 0); SessionActions.Children.Insert(0, headerImport);
        }
        var editEvents = initialConfiguration ? (UIElement)WorkflowBody : Body;
        editEvents.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, e) => { if (e.OriginalSource is TextBox { IsReadOnly: false }) Changed(); }));
        editEvents.AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler((_, e) => { if (e.OriginalSource is ComboBox) Changed(); }));
        editEvents.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler((_, _) => Changed()));
        editEvents.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler((_, _) => Changed()));
        editEvents.AddHandler(PasswordBox.PasswordChangedEvent, new RoutedEventHandler((_, _) => Changed()));
        Closing += (_, e) => { if (!Busy && !RetryRequested && !ReplacementLaunched && !MayDiscard()) e.Cancel = true; };
        var authorizationTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        async Task RefreshAuthorization()
        {
            if (checkingAuthority) return; checkingAuthority = true;
            try
            {
                var identity = await ManagementMode.ManagementModeStatusAsync(); var allowed = identity.Authorized;
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
                gateDescription.Text = !identity.Available ? "请使用同版安装器检查管理服务。原输入仅在当前进程中保留，尚未保存或应用。" : seenAuthorization ? "当前未保存输入仅保留在此进程中。重新验证管理密码后可继续，失权期间不能保存或应用。" : "管理密码用于保护参数设置，与操作员账号、Windows 授权分别验证。检查和编辑不会启动设备。";
                WorkflowBody.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
                AuthorizationGate.Visibility = allowed ? Visibility.Collapsed : Visibility.Visible;
                FooterHost.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
                if (initialConfiguration && headerImport is not null) headerImport.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
                StartupAppearance.Primary(administratorLogin);
                if (!allowed && !authorityWasBlocked) { statusBeforeAuthorityBlock = Status.Text; Message(""); }
                else if (allowed && authorityWasBlocked) Message(statusBeforeAuthorityBlock ?? "已进入管理模式，原填写内容已恢复。");
                authorityWasBlocked = !allowed;
            }
            catch (Exception error) { authorityAllowed = false; managementMenu.Status = new(false, false, null, Available: false); WorkflowBody.Visibility = Visibility.Collapsed; FooterHost.Visibility = Visibility.Collapsed; if (initialConfiguration && headerImport is not null) headerImport.Visibility = Visibility.Collapsed; AuthorizationGate.Visibility = Visibility.Visible; gateTitle.Text = "管理授权状态无法读取"; gateDescription.Text = "原输入仅在当前进程中保留。" + error.Message; }
            finally { checkingAuthority = false; }
        }
        authorizationTimer.Tick += async (_, _) => await RefreshAuthorization();
        Loaded += (_, _) => authorizationTimer.Start(); Closed += (_, _) => authorizationTimer.Stop();
        Loaded += async (_, _) => { await RefreshAuthorization(); if (authorityAllowed) await Run(Read); };
    }
    async Task ValidateBarcodeRule()
    {
        if (parameterEditor?.ApplicationId != "sportmaster") return;
        var pattern = parameterEditor.Policy.BarcodePattern; var exclusion = parameterEditor.Policy.BarcodeExclusionPattern;
        if (string.IsNullOrWhiteSpace(pattern) && string.IsNullOrWhiteSpace(exclusion)) return;
        var result = await Task.Run(() => parameterEditor.Preview(new(pattern, string.IsNullOrWhiteSpace(exclusion) ? null : exclusion), ["__format_check__"]));
        if (result.Outcome is "InvalidPattern" or "FilterTimeout")
        {
            parameterEditor.Report(result.Detail); LocateFirstIssue();
            throw new InvalidOperationException("请修正条码规则后继续。");
        }
    }
    async Task CheckCandidate()
    {
        if (startupSaveUncertain) throw new InvalidOperationException("上次保存结果待核对，请先重新读取权威配置；当前输入保留，未重复提交。");
        var candidate = RequireDraft();
        await ValidateBarcodeRule();
        var report = await Task.Run(() => maintenance.CheckAsync(candidate));
        diagnosticDetails = string.Join("\n", report.Checks.Select(c => c.Layer + " · " + c.Status + " · " + c.Reason));
        checkedInput = null;
        if (report.Checks.Any(c => c.Status == "FAIL"))
        {
            foreach (var check in report.Checks.Where(c => c.Status == "FAIL")) parameterEditor?.Report(check.Reason);
            LocateFirstIssue();
            throw new InvalidOperationException("配置检查未通过：" + string.Join("；", report.Checks.Where(c => c.Status == "FAIL").Select(c => c.Layer + "：" + c.Reason)));
        }
        checkedInput = Input(); checkedCandidateHash = report.CandidateHash;
        if (initialConfiguration) saveState.Text = lastSaveConfirmed && savedInput == Input() ? "已保存待启动检查" : report.Checks.Any(c => c.Status == "BLOCKED") ? "部署条件待核对" : "检查通过 · 未保存";
        Message(report.Checks.Any(c => c.Status == "BLOCKED") ? "参数格式检查完成；部署条件尚未齐备。可保存设置，启动仍需处理部署条件。" : "配置检查已完成；保存和启动仍会核验实际条件。");
        diagnosticDetails = string.Join("\n", report.Checks.Select(c => c.Layer + " · " + c.Status + " · " + c.Reason));
    }
    string diagnosticDetails = "";
    async Task SaveCandidate()
    {
        lastSaveConfirmed = false;
        RequireChecked(); var candidate = RequireDraft(); var expected = document!.ConfigurationHash;
        if (startupSaveUncertain) throw new InvalidOperationException("原保存结果尚未确认；保留输入，先重新读取权威账比较，禁止重复保存");
        startupSaveUncertain = true;
        try
        {
            var saved = await Task.Run(() => initialConfiguration ? maintenance.RepairInitialAsync(expected, candidate, reason) : maintenance.StageAsync(expected, candidate, reason));
            ConfirmSaved(saved);
            Message("已保存待生效；" + ChineseInterfaceText.Status(saved.ConfigurationStatus) + "。原草稿保留；尚未启动设备。应用须重新读取权威配置并执行原启动守卫。");
        }
        catch (Exception error)
        {
            diagnosticDetails += "\n保存回执：" + Ipc.Platform.DiagnosticText.Protect(error.ToString());
            var actual = await Task.Run(maintenance.ReadAsync);
            if (actual.Candidate is not null && (actual.ConfigurationStatus == "PendingRestart" || initialConfiguration && actual.ConfigurationStatus == "Seed") && Ipc.Platform.Wire.Hash(Ipc.Platform.Wire.Encode(actual.Candidate)) == checkedCandidateHash)
            { ConfirmSaved(actual); Message("已从权威配置账确认本次候选已保存待生效；原回执：" + error.Message + "。未重复提交，当前输入保留。"); }
            else if (actual.ConfigurationHash == expected) { startupSaveUncertain = false; Message("权威修订未改变，未保存；输入保留：" + error.Message); }
            else Message("保存结果需核对；输入保留，禁止重复提交。原回执：" + error.Message + "；权威状态：" + actual.ConfigurationStatus + "。重新读取前可保存加密草稿。");
        }

    }
    void ConfirmSaved(StartupMaintenanceDocument saved)
    {
        if (saved.Candidate is null || Wire.Hash(Wire.Encode(saved.Candidate)) != checkedCandidateHash)
            throw new IOException("保存后的配置与本次检查内容不一致；保留输入并核对原次保存。");
        document = saved; draft = new(saved.Candidate, saved.Catalog);
        lastSaveConfirmed = true; startupSaveUncertain = false;
        Render(false); savedInput = Input();
    }
    Task StartReviewed()
    {
        RequireChecked(); var candidate = RequireDraft();
        if ((initialConfiguration && !lastSaveConfirmed) || startupSaveUncertain || savedInput != Input() || Wire.Encode(candidate) != Wire.Encode(document!.Candidate))
            throw new InvalidOperationException("配置尚未确认保存；请先保存，当前输入保留。");
        SelectedLaunch = SelectedLaunch with { ConfigurationDirectory = config.Text, DataRoot = data.Text };
        RetryRequested = true; CompleteDialog(); return Task.CompletedTask;
    }
    async Task ImportConfiguration()
    {
        await RequireMaintenance();
        if (draft is null || document?.Candidate is null) throw new InvalidOperationException("先读取当前配置。");
        var path = FilePick(this, "导入配置文件", "配置或设置模板|*.json");
        if (path is null) return;
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("配置文件超出4兆字节限制。");
        var replacement = StartupConfigurationFiles.Import(File.ReadAllText(path), document.Candidate, draft, document.Catalog);
        var comparison = new Window { FontFamily = UiTypography.Interface, FontSize = UiTypography.Body, Owner = this, Title = "导入配置预览", Width = 720, Height = 560, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new(20) };
        var apply = new Button { Content = "应用到当前填写内容", MinHeight = 44, Margin = new(0, 12, 0, 0) };
        DockPanel.SetDock(apply, Dock.Bottom); apply.Click += (_, _) => comparison.DialogResult = true; panel.Children.Add(apply);
        panel.Children.Add(new TextBox { Text = "仅导入设置，不保存、不启动。保留本供包台身份与模拟/生产用途。\n\n" + StartupConfigurationFiles.Describe(draft, replacement), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        comparison.Content = panel;
        if (comparison.ShowDialog() == true) { await RequireMaintenance(); draft = replacement; Render(false); Message("配置已导入填写区。检查后再保存配置。"); }
    }
    void OpenTools()
    {
        var menu = new ContextMenu { FontFamily = UiTypography.Interface, FontSize = UiTypography.Body };
        foreach (var label in new[] { "导入配置文件…", "保存未完成草稿", "继续已保存草稿" })
            Add(menu.Items, label);
        menu.Items.Add(new Separator());
        var advanced = new MenuItem { Header = "备份、诊断与修复" };
        foreach (var label in new[] { "读取所选配置", "分层检查与变更预览", "保存首配待生效…", "按当前首配启动", "导出新站模板…", "创建完整备份…", "已结历史到期维护…", "受控修复未确认配置…", "接管已恢复的原机目录…", "安装 / 修复程序…" })
            Add(advanced.Items, label);
        var diagnostic = new MenuItem { Header = "查看完整诊断…" };
        diagnostic.Click += (_, _) => new Window { FontFamily = UiTypography.Interface, FontSize = UiTypography.Body, Owner = this, Title = "启动诊断", Width = 760, Height = 520, Content = new TextBox { Text = diagnosticDetails + "\n" + Status.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }.ShowDialog();
        advanced.Items.Add(diagnostic); menu.Items.Add(advanced);
        var tools = Actions.Children.OfType<Button>().Single(x => (string)x.Content == "配置工具…");
        menu.PlacementTarget = tools; tools.ContextMenu = menu; menu.IsOpen = true;
        void Add(ItemCollection items, string label)
        {
            var target = Actions.Children.OfType<Button>().FirstOrDefault(x => (string)x.Content == label);
            if (target is null) return;
            var item = new MenuItem { Header = label, IsEnabled = label != "继续已保存草稿" || savedDraft is not null };
            item.Click += (_, _) => target.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); items.Add(item);
        }
    }
    string Input() => config.Text + "\n" + data.Text + "\n" + reason + "\n" + (draft is null ? "" : Wire.Encode(draft.Capture()));
    Button Action(string label, Func<Task> action) => base.Action(label, async () =>
    {
        if (label is not ("进入管理模式…" or "读取所选配置" or "安装 / 修复程序…" or "上一步" or "下一步" or "维护操作" or "配置工具…" or "稍后配置")) await RequireMaintenance();
        await action();
    }, readOnly: label is "读取所选配置" or "配置工具…" or "稍后配置");
    async Task RequireMaintenance()
    {
        RequireDirectories();
        if (!(await ManagementMode.ManagementModeStatusAsync()).Authorized) throw new UnauthorizedAccessException("请先进入管理模式；原输入保留。");
        if (maintenance is null) throw new InvalidOperationException("先读取所选目录；失败读取不能沿用另一目录的维护实例。");
    }
    bool MayDiscard() => savedInput is null || savedInput == Input() || ConfirmDiscardInputs();
    void Changed()
    {
        if (rendering || Busy) return;
        if (savedInput == Input()) return;
        checkedInput = null;
        saveState.Text = startupSaveUncertain ? "保存结果待核对" : "未保存";
        refreshReview?.Invoke(); UpdateActions();
        if (!initialConfiguration) Message("尚未保存；填写完成后检查当前候选。");
    }
    void RequireChecked()
    {
        if (checkedInput is null || checkedInput != Input()) throw new InvalidOperationException("请重新点击保存并启动，核对当前填写内容。");
    }
    ParcelConfiguration RequireDraft()
    {
        if (draft is null || document is null) throw new InvalidOperationException("先读取有效配置。损坏原账请使用完整备份恢复或安装修复。");
        RequireDirectories();
        if (parameterEditor?.ValidateFields() == false) { LocateFirstIssue(); throw new InvalidOperationException("请修正已定位的参数；其他输入保留。"); }
        return draft.Build();
    }
    Action? focusAfterOperation;
    protected override void OperationCompleted()
    {
        refreshReview?.Invoke(); UpdateActions();
        var focus = focusAfterOperation; focusAfterOperation = null;
        if (focus is not null) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, focus);
    }
    protected override void OperationFailed(Exception error)
    {
        if (!initialConfiguration) { base.OperationFailed(error); return; }
        diagnosticDetails = Ipc.Platform.DiagnosticText.Protect(diagnosticDetails + "\n" + error + "\n" + Status.Text);
        saveState.Text = error is UnauthorizedAccessException ? "需要重新验证" : startupSaveUncertain ? "保存结果待核对" : savingCandidate ? "保存未完成" : "检查未通过";
        savingCandidate = false;
        refreshReview?.Invoke();
    }
    void LocateFirstIssue()
    {
        var issue = parameterEditor?.Issues.FirstOrDefault();
        if (issue is null) return;
        var step = issue.Category switch { "业务模块" => 0, "系统" => 1, "驱动" => 2, "组件" => 3, _ => -1 };
        if (initialConfiguration && step >= 0) parameterControl?.SelectCategory(new[] { 1, 0, 2, 3 }[step]);
        var control = parameterControl; var path = issue.Path; var expectedInput = Input();
        if (step >= 0) focusAfterOperation = () =>
        {
            // Run releases disabled controls before this focus continuation.
            // A newer edit must never be interrupted by this older validation.
            if (!Busy && ReferenceEquals(control, parameterControl) && expectedInput == Input()) control?.NavigateTo(path);
        };
    }
    void RequireDirectories()
    {
        if (config.Text != SelectedLaunch.ConfigurationDirectory || data.Text != SelectedLaunch.DataRoot) throw new InvalidOperationException("路径已更改，先重新读取，不能将旧草稿保存到另一个目录");
    }
    Task Read() => Read(false);
    async Task Read(bool discardAccepted)
    {
        if (!discardAccepted && !MayDiscard()) return;
        document = null; draft = null; savedDraft = null; lastSaveConfirmed = false; maintenance = null!; checkedInput = savedInput = null;
        try
        {
        SelectedLaunch = SelectedLaunch with { ConfigurationDirectory = Path.GetFullPath(config.Text), DataRoot = Path.GetFullPath(data.Text) };
        config.Text = SelectedLaunch.ConfigurationDirectory; data.Text = SelectedLaunch.DataRoot;
        maintenance = ParcelStartupServices.Maintenance(SelectedLaunch);
        document = await Task.Run(maintenance.ReadAsync); startupSaveUncertain = false; recovery = null; savedDraft = null;
        string? unavailableDraft = null;
        try { if (document.Candidate is not null) savedDraft = await Task.Run(maintenance.ReadDraftAsync); }
        catch (System.ComponentModel.Win32Exception error)
        {
            // A recovered machine-DPAPI draft is private, non-authoritative input.
            // Keep its bytes and CAS protection; it cannot hide the repair editor.
            unavailableDraft = "\n草稿不可用：本机无法解密原DPAPI草稿（" + error.NativeErrorCode + "）。原密文保留、不覆盖；可从当前配置重新填写秘密并受控提交。换机恢复仍隔离，不能代替来源退役与资格核对。";
        }
        Render();
        var resumed = initialConfiguration && savedDraft is { BaseChanged: false } && draft is not null && savedDraft.Editor.Values.GetValueOrDefault("editor") != "live-v2";
        if (resumed) { draft = draft!.WithRestored(savedDraft!.Editor); Render(false); savedInput = Input(); }
        diagnosticDetails = string.Join("\n", document.Diagnostics) + "\n" + document.Catalog.InventoryDiagnostic + unavailableDraft;
        Message(initialConfiguration
            ? document.Candidate is null ? "无法读取配置，原文件已保留。请使用启动修复处理后重新打开。"
                : unavailableDraft is not null ? "草稿不可用：原文件已保留，请重新填写。"
                : resumed ? "已继续上次未完成配置。" : "填写当前供包台，或从下方导入配置文件。" + (savedDraft is null ? "" : "原草稿与当前配置不同，已保留；请核对后填写。")
            : diagnosticDetails);
        }
        catch { Render(); throw; }
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
        var policyStart = Body.Children.Count; var deviceStart = policyStart; var reviewStart = policyStart;
        ParameterSettingsControl? sharedEditor = null;
        if (replaceDraft) draft = document?.Candidate is { } candidate ? new(candidate, document.Catalog, initialConfiguration && !document.ExistingConfiguration) : null;
        if (draft is not null)
        {
            var c = document!.Candidate!;
            Note($"{ChineseInterfaceText.BusinessName(c.ApplicationId)} · {ChineseInterfaceText.Choice(c.Mode)} · 修订 {document.Revision}\n供包台身份只读；设备尚未启动。");
            Body.Children.Add(new Expander { Header = "配置身份与检查边界", Content = new TextBox { IsReadOnly = true, Text = $"原账散列：{document.ConfigurationHash}\n设备与服务沿已交付精确目录选择；未枚举/未连接不等于已验证。显式启动仍受完整资格和原责任检查。", TextWrapping = TextWrapping.Wrap }, Margin = new(0, 8, 0, 8) });
            policyStart = Body.Children.Count;
            Note("首次引导与日常设置共用四类参数、精确描述和完整候选校验。固定位置、实例数量与身份不作为普通参数。");
            deviceStart = Body.Children.Count;
            parameterEditor = draft.CreateParameterEditor((p, samples) => maintenance.TestBarcodeFilterAsync(p, samples), new(config.Text, data.Text));
            sharedEditor = parameterControl = new ParameterSettingsControl { FirstConfiguration = initialConfiguration, Model = parameterEditor, MinHeight = initialConfiguration ? 0 : 300, Height = initialConfiguration ? double.NaN : 430 };
            Body.Children.Add(sharedEditor);
            reviewStart = Body.Children.Count;
            if (!initialConfiguration) Field("维护原因（必填；保留审计与原配置字节）", reason, v => reason = v);
        }
        var recoveryStart = Body.Children.Count;
        if (!initialConfiguration)
        {
        Note("完整备份恢复：验证原库和 .recovery 同伴目录，复制到新的隔离目录。恢复标记继续阻止自动业务恢复。");
        var backupFolder = Folder("浏览完整备份目录（最多256份；校验整个恢复集，保留无效项）", backupCatalogRoot);
        backupFolder.TextChanged += (_, _) => backupCatalogRoot = backupFolder.Text;
        var backupRows = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, MinHeight = 100, MaxHeight = 260, RowHeight = 44 };
        foreach (var column in new[] { ("时间", "At"), ("完整性", "Status"), ("字节", "Bytes"), ("备份", "Path") }) backupRows.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new System.Windows.Data.Binding(column.Item2), Width = column.Item2 == "Path" ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto });
        var listBackups = new Button { Content = "读取并校验备份列表", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        listBackups.Click += async (_, _) => await Run(async () => { await RequireMaintenance(); var selected = backupCatalogRoot; backupRows.ItemsSource = await RunMaintenance("只读校验备份目录\n目录："+selected+"\n保留所有原文件。",(progress,token)=>maintenance.ListBackupsAsync(selected,progress,token),readOnly:true); Message("备份列表已读回；Verified表示完整字节集可预检恢复，不代表外部业务已经核对。"); });
        Body.Children.Add(listBackups); Body.Children.Add(backupRows);
        var backupBox = Field("完整备份数据库", backup, v => { backup = v; recovery = null; });
        backupRows.SelectionChanged += (_, _) => { if (backupRows.SelectedItem is StartupBackupItem item) { backupBox.Text = item.Path; Message(item.Status + " · " + item.Summary); } };
        var choose = new Button { Content = "选择完整备份…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        choose.Click += (_, _) => { var selected = FilePick(this, "选择完整备份数据库", "SQLite 备份|*.db;*.sqlite;*.backup|所有文件|*.*"); if (selected is not null) backupBox.Text = selected; }; Body.Children.Add(choose);
        var destination = Folder("新的隔离数据根（不覆盖原目录）", restoreRoot);
        destination.TextChanged += (_, _) => { restoreRoot = destination.Text; recovery = null; };
        var plan = new Button { Content = "检查恢复计划", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        var preview = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80 };
        plan.Click += async (_, _) => await Run(async () => { await RequireMaintenance(); recovery = await RunMaintenance("只读检查恢复计划\n源："+backup+"\n目标："+restoreRoot+"\n尚未复制或恢复文件。",(progress,token)=>maintenance.PlanRestoreAsync(backup,restoreRoot,progress,token),readOnly:true); preview.Text = recovery.Summary + "\n目标：" + recovery.Destination; Message("完整备份已校验；尚未恢复。检查目标后执行下方恢复。"); });
        Body.Children.Add(plan); Body.Children.Add(preview);
        var restore = new Button { Content = "按计划恢复到新目录…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        restore.Click += async (_, _) => await Run(async () =>
        {
            await RequireMaintenance(); if (!MayDiscard()) return;
            var selected = recovery ?? throw new InvalidOperationException("先检查恢复计划");
            await RunMaintenance("恢复原备份到隔离目录\n源："+backup+"\n目标："+selected.Destination+"\n文件恢复不代表可以生产。",
                (progress,token)=>maintenance.RestoreAsync(backup,selected.Destination,selected.BackupSha256,progress,token));
            restoredPreview = selected; restoredBackup = backup;
            data.Text = selected.DataRoot; await Read(true);
            Message("完整备份已恢复并重新读取新目录。原目录保留；重试启动时隔离恢复责任仍由业务守卫处理。"); recovery = null;
        }); Body.Children.Add(restore);
        }
        var fields = Body.Children.Cast<UIElement>().ToArray(); Body.Children.Clear();
        tasks = initialConfiguration ? null : new TabControl { Name = "StartupTasks", Margin = new(0, 8, 0, 8) };
        void Section(string title, string guidance, IEnumerable<UIElement> items)
        {
            var panel = new StackPanel { Margin = new(16) };
            panel.Children.Add(new TextBlock { Text = guidance, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16), FontSize = UiTypography.Body });
            foreach (var item in items) panel.Children.Add(item);
            tasks!.Items.Add(new TabItem { Header = title, Content = panel, MinHeight = 44 });
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
                review.Children.Add(state); review.Children.Add(summary);
                review.Children.Add(new Expander { Header = "配置检查诊断详情", Content = diagnostic, Margin = new(0, 16, 0, 0) });
                refreshReview = () =>
                {
                    state.Text = startupSaveUncertain ? "上次保存结果待核对，暂不重复提交。" : lastSaveConfirmed && savedInput == Input() ? "配置已保存，尚未连接设备。完成后可明确选择启动供包台。" : "核对当前输入并保存配置；此操作不会启动设备作业。";
                    diagnostic.Text = Ipc.Platform.DiagnosticText.Protect(diagnosticDetails);
                    try { summary.Text = StartupConfigurationFiles.Summary(draft!, document!.Candidate!) + "\n\n本次更改\n" + StartupConfigurationFiles.Describe(new StartupConfigurationDraft(document.Candidate!, document.Catalog), draft!); }
                    catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
                    { summary.Text = "尚有参数需要修改，请按检查问题定位。当前输入保留。"; diagnostic.Text += "\n" + Ipc.Platform.DiagnosticText.Protect(error.Message); }
                };
                sharedEditor.SetReviewContent(review);
                WorkflowBody.Content = sharedEditor;
                sharedEditor.SelectedPageChanged += (_, _) =>
                {
                    WorkflowBusinessName.Text = sharedEditor.PageProgress + " · 相机/RCS可明确延后配置";
                    WorkflowBusinessName.Visibility = Visibility.Visible;
                    selectedTask = sharedEditor.SelectedCategory switch { 1 => 0, 0 => 1, 2 => 2, 3 => 3, _ => 4 };
                    if (selectedTask == 4) refreshReview?.Invoke(); UpdateActions();
                };
                sharedEditor.MaintenanceRequested += (_, _) => sharedEditor.ShowReview();
                sharedEditor.SelectCategory(new[] { 1, 0, 2, 3, 4 }[Math.Clamp(selectedTask, 0, 4)]);
                refreshReview();
            }
        }
        else
        {
        Section("1 目录与工位", "先核对原目录和工位。改变目录后显式重新读取；目录授权仍由受保护安装机制执行。", fields.Take(policyStart));
        Section("2 设备与参数", "按业务位绑定精确组件与稳定设备身份；登录按组合可选。离线选择不表示已连接或已获资格。", fields.Skip(deviceStart).Take(reviewStart - deviceStart));
        Section("3 参数说明", "系统、业务模块、驱动、组件的参数统一在设备与参数页编辑；保存与启动分开。", fields.Skip(policyStart).Take(deviceStart - policyStart));
        Section("4 检查与启动", "检查当前输入，再保存待生效修订并重试启动。未确认配置失败可在恢复维护中受控修复；原失败与原请求保留。", fields.Skip(reviewStart).Take(recoveryStart - reviewStart));
        Section("恢复与维护", "完整备份、隔离恢复、原机接管和程序维护是独立任务；不会因切换本页执行。原机迁移仍需原根退役和外部责任核对。", fields.Skip(recoveryStart));
        tasks!.SelectedIndex = selectedTask;
        }
        if (!initialConfiguration && tasks is not null) {
            tasks.SelectionChanged += (_, e) => { if (e.OriginalSource == tasks) { selectedTask = tasks.SelectedIndex; UpdateActions(); } };
            Body.Children.Add(tasks);
        }
        UpdateActions(); rendering = false;
        if (replaceDraft) savedInput = Input();
    }
    void UpdateActions()
    {
        if (initialConfiguration)
        {
            foreach (var button in Actions.Children.OfType<Button>())
            {
                var label = (string)button.Content;
                var next = label == "下一步" || label.StartsWith("继续：", StringComparison.Ordinal) || label == "检查配置";
                if (next) button.Content = parameterControl?.NextPageTitle is { } title ? title == "检查与完成" ? "检查配置" : "继续：" + title : "下一步";
                var confirmed = lastSaveConfirmed && !startupSaveUncertain && savedInput == Input();
                button.Visibility = next && parameterControl?.NextPageTitle is not null && !startupSaveUncertain || label == "上一步" && parameterControl?.HasPreviousPage == true || label == "保存配置" && selectedTask == 4 && !confirmed && !startupSaveUncertain || label == "完成" && selectedTask == 4 && confirmed || label == "重新读取配置…" && startupSaveUncertain ? Visibility.Visible : Visibility.Collapsed;
                if (label is "保存配置" or "完成" or "重新读取配置…") StartupAppearance.Primary(button);
            }
            return;
        }
        var groups = new[] { 0, 3, -1, 0, 4, 1, 3, 4, 4, 4, 4, -1, 4, 3, -1, -2, 4 };
        for (var i = 0; i < Actions.Children.Count; i++)
            Actions.Children[i].Visibility = i < groups.Length && (groups[i] == -1 || groups[i] == selectedTask || groups[i] == -2 && selectedTask < 3) ? Visibility.Visible : Visibility.Collapsed;
    }
}
