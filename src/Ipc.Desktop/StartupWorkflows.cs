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

public abstract class StartupWorkflowWindow : Window
{
    protected readonly StackPanel Body = new();
    protected readonly WrapPanel Actions = new();
    protected StackPanel Form;
    protected readonly TextBlock Status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 12) };
    protected readonly TextBlock VersionLabel = new() { FontSize = 14, Margin = new(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    protected bool Busy;
    protected bool CloseAfterCancellation;
    protected StartupWorkflowWindow(string title, bool installation = false)
    {
        Form = Body;
        ProductBrand.Apply(this);
        VersionLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Title = title; FontSize = 16; Width = 960; Height = 760; MinWidth = 680; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(241, 244, 247)); Foreground = new SolidColorBrush(Color.FromRgb(25, 39, 54));
        if (installation)
        {
            Width = 840; Height = 700; MinWidth = 760; MinHeight = 560; Background = Brushes.White;
            var layout = new Grid(); layout.ColumnDefinitions.Add(new() { Width = new(176) }); layout.ColumnDefinitions.Add(new());
            var rail = new StackPanel { Margin = new(24, 40, 24, 24) };
            rail.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Ipc.Desktop;component/Assets/cainiao.png")), Width = 80, Height = 80, HorizontalAlignment = HorizontalAlignment.Left });
            rail.Children.Add(new TextBlock { Text = "菜鸟供包台", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new(0, 20, 0, 16) });
            rail.Children.Add(new TextBlock { Text = "1  安装选项\n\n2  确认安装\n\n3  安装完成", TextWrapping = TextWrapping.Wrap });
            var sidebar = new Border { Background = new SolidColorBrush(Color.FromRgb(237, 244, 255)), Child = rail }; layout.Children.Add(sidebar);
            var content = new Grid { Margin = new(28, 24, 28, 20) }; Grid.SetColumn(content, 1);
            content.RowDefinitions.Add(new() { Height = GridLength.Auto }); content.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); content.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var header = new StackPanel(); header.Children.Add(new TextBlock { Text = "安装菜鸟供包台", FontSize = 24, FontWeight = FontWeights.Bold }); header.Children.Add(VersionLabel); header.Children.Add(Status);
            var options = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(options, 1);
            Actions.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetRow(Actions, 2);
            content.Children.Add(header); content.Children.Add(options); content.Children.Add(Actions); layout.Children.Add(content); Content = layout;
            Closing += (_, e) => { if (Busy) e.Cancel = true; }; return;
        }
        var grid = new Grid { Margin = new(20, 12, 20, 16) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel(); heading.Children.Add(new TextBlock { Text = title.Split(" · ")[0], FontSize = 18, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new ScrollViewer { Content = Status, MaxHeight = 64, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var diagnostic = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        diagnostic.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source = Status });
        // Full diagnostics remain selectable through the named tools menu.
        Status.ToolTip = diagnostic;
        var scroll = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 1); Grid.SetRow(Actions, 2);
        grid.Children.Add(heading); grid.Children.Add(scroll); grid.Children.Add(Actions); Content = grid;
        Closing += (_, e) => { if (Busy) e.Cancel = true; };
    }
    protected Button Action(string label, Func<Task> action)
    {
        var button = new Button { Content = label, MinHeight = 44, MinWidth = 100, Margin = new(0, 12, 10, 0), Padding = new(12, 6, 12, 6) };
        button.Click += async (_, _) => await Run(action); Actions.Children.Add(button); return button;
    }
    protected async Task Run(Func<Task> action)
    {
        if (Busy) return; Busy = true; Actions.IsEnabled = false; Body.IsEnabled = false;
        try { await action(); }
        catch (OperationCanceledException) when (CloseAfterCancellation) { Busy = false; Close(); }
        catch (Exception ex) { Status.Text = "未完成：" + ChineseInterfaceText.ParameterMessage(ex.Message) + "\n已有数据与配置保留。可修正输入后重试。"; Status.Foreground = Brushes.Firebrick; }
        finally { Busy = false; Actions.IsEnabled = true; Body.IsEnabled = true; }
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
    bool startupSaveUncertain;
    bool lastSaveConfirmed;
    string? checkedCandidateHash;
    int selectedTask;
    bool showMaintenanceActions;
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
    public bool RetryRequested { get; private set; }
    public bool ReplacementLaunched { get; private set; }
    public StartupRepairWindow(BusinessLaunch launch, string? failure = null, bool initialConfiguration = false) : base(initialConfiguration ? "首次配置 · 菜鸟供包台" : "启动修复 · 离线维护")
    {
        SelectedLaunch = launch;
        this.initialConfiguration = initialConfiguration;
        ConfirmDiscardInputs = () => MessageBox.Show(this, "有尚未保存的输入。返回可保存加密草稿；继续将放弃当前未保存输入，既有配置账和已保存草稿保留。", "保护未完成配置", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
        if (initialConfiguration) reason = "首次配置供包台";
        Message("无需业务启动成功。检查和编辑不连接设备；保存为 Pending，正常启动仍检查原恢复责任。" + (failure is null ? "" : "\n启动诊断：" + failure));
        config = Folder("原配置目录（包含 business.json）", launch.ConfigurationDirectory);
        data = Folder("原数据根（保留 Simulation/Production、站和 Application 层级）", launch.DataRoot);
        pathFields = Body.Children.Cast<UIElement>().ToArray();
        Action("读取所选配置", Read);
        Action("分层检查与变更预览", CheckCandidate);
        Action("保存未完成草稿", async () =>
        {
            if (draft is null || document is null) throw new InvalidOperationException("先读取原配置");
            RequireDirectories();
            savedDraft = await Task.Run(() => maintenance.SaveDraftAsync(document.ConfigurationHash, savedDraft?.DraftHash, draft.Capture()));
            savedInput = Input();
            Message("未完成输入已保存；不要求连接/Production资格，不写Pending。Windows草稿使用DPAPI及私有ACL；换机需重新配置秘密。");
        });
        Action("继续已保存草稿", () =>
        {
            if (savedDraft is null || draft is null) throw new InvalidOperationException("没有可继续草稿");
            if (savedDraft.BaseChanged) throw new InvalidOperationException("草稿基线已变化；先比较当前配置，不自动套用或覆盖。");
            if (savedDraft.Editor.Values.GetValueOrDefault("editor") == "live-v1") throw new InvalidOperationException("这是运行内统一设置的草稿；在原Application的设备与维护页选择继续未完成设置。当前首配输入与加密原草稿均保留。");
            draft = draft.WithRestored(savedDraft.Editor); Render(false); Message("已恢复未完成输入；参数改变后原检查失效。请重新检查候选。"); return Task.CompletedTask;
        });
        Action("导出新站模板…", () =>
        {
            if (draft is null || document?.Candidate is null) throw new InvalidOperationException("先读取配置");
            var picker = new SaveFileDialog { FileName = "site-template.json", Filter = "无业务状态模板|*.json" };
            if (picker.ShowDialog(this) == true) { using var file = new StreamWriter(new FileStream(picker.FileName, FileMode.CreateNew)); file.Write(Wire.Encode(StartupSiteTemplates.Export(document.Candidate.ApplicationId, draft.Capture()))); Message("已导出同版安全模板：只含非生效策略和提供者选择，不含原站/设备/目标/秘密/编号范围或业务数据。"); }
            return Task.CompletedTask;
        });
        Action("导入配置文件…", ImportConfiguration);
        Action(initialConfiguration ? "保存首配待生效…" : "保存待生效修订…", SaveCandidate);
        Action("受控修复未确认配置…", async () =>
        {
            RequireChecked(); var candidate = RequireDraft();
            if (MessageBox.Show(this, "将保留原配置及确认/失败记录。仅在原实例已释放、原责任已清且候选严格校验通过时，允许下一次启动直接尝试候选。失败不会替换Effective。", "确认初始配置修复", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            document = await Task.Run(() => maintenance.RepairInitialAsync(document!.ConfigurationHash, candidate, reason)); Render();
            Message("修复意图已保存并读回；重试正常启动会再次检查原责任及资格，资源全部成功后才确认Effective。");
        });
        Action("导出支持摘要…", async () =>
        {
            if (MessageBox.Show(this, "将导出本版Application/源码/公共契约、配置hash/修订/状态及诊断计数。排除完整参数、诊断原文、条码、业务报文、数据库和原件；不自动上传。", "预览离线支持范围", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            var picker = new SaveFileDialog { FileName = "startup-support.json", Filter = "支持摘要|*.json", OverwritePrompt = true };
            if (picker.ShowDialog(this) == true) { if (File.Exists(picker.FileName)) throw new IOException("请选择新文件，保留既有摘要"); await Task.Run(() => maintenance.ExportAsync(picker.FileName)); Message("已导出摘要；不含配置参数、业务报文或数据库。"); }
        });
        Action("创建完整备份…", async () =>
        {
            var picker = new SaveFileDialog { FileName = "complete-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db", Filter = "完整恢复集数据库|*.db" };
            if (picker.ShowDialog(this) != true) return;
            await Task.Run(() => maintenance.BackupAsync(picker.FileName)); backupCatalogRoot = Path.GetDirectoryName(picker.FileName)!;
            Message("完整备份已完成；数据库和同名.recovery目录必须一起保留。可在备份列表重新校验并预检恢复。");
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
        Action("离线帮助", () => { new Window { Owner = this, Title = "菜鸟供包台 · 离线帮助", Width = 860, Height = 700, Content = ProductHelp.CreateView() }.Show(); return Task.CompletedTask; });
        Action("安装 / 修复程序…", async () =>
        {
            if (!MayDiscard()) return;
            var setup = new StartupSetupWindow(SelectedLaunch) { Owner = this }; setup.ShowDialog();
            if (setup.Launched) { ReplacementLaunched = true; Busy = false; Close(); return; }
            if (setup.Installed is { } installed) { config.Text = installed.ConfigurationDirectory; data.Text = installed.DataRoot; await Read(true); }
        });
        Action(initialConfiguration ? "按当前首配启动" : "重试正常启动", StartReviewed);
        Action("上一步", () => { if (tasks is not null) tasks.SelectedIndex = Math.Max(0, tasks.SelectedIndex - 1); return Task.CompletedTask; });
        var next = Action("下一步", async () =>
        {
            if (tasks is null) return;
            if (initialConfiguration && selectedTask < 4)
            {
                if (parameterEditor is null) throw new InvalidOperationException("先读取可编辑候选；保留原目录和诊断。");
                if (!parameterEditor.ValidateFields(new[] { 1, 0, 2, 3 }[selectedTask])) throw new InvalidOperationException("请修正本步骤标出的参数；其他步骤输入保留。");
                if (selectedTask == 0 && parameterEditor.ApplicationId == "sportmaster")
                {
                    var pattern = parameterEditor.Policy.BarcodePattern;
                    var result = await Task.Run(() => parameterEditor.Preview(new(pattern, string.IsNullOrWhiteSpace(parameterEditor.Policy.BarcodeExclusionPattern) ? null : parameterEditor.Policy.BarcodeExclusionPattern), ["__format_check__"]));
                    if (result.Outcome is "InvalidPattern" or "FilterTimeout") { parameterEditor.Report(result.Detail); throw new InvalidOperationException("请修正条码规则后继续。"); }
                }
            }
            tasks.SelectedIndex = Math.Min(initialConfiguration ? 4 : 3, tasks.SelectedIndex + 1);
            Message(tasks.SelectedIndex == 4 ? "核对设置后保存并启动。" : "继续填写当前步骤；内容会保留。");
        });
        next.Background = new SolidColorBrush(Color.FromRgb(32, 95, 206)); next.Foreground = Brushes.White;
        Action("维护操作", () => { showMaintenanceActions = !showMaintenanceActions; UpdateActions(); return Task.CompletedTask; });
        Action("保存并启动", async () =>
        {
            await CheckCandidate();
            await SaveCandidate();
            if (!lastSaveConfirmed || startupSaveUncertain || savedInput != Input() || Wire.Encode(RequireDraft()) != Wire.Encode(document!.Candidate))
                throw new InvalidOperationException("尚未确认保存成功；请按提示核对，当前输入保留。");
            await CheckCandidate();
            await StartReviewed();
        });
        Action("稍后配置", () => { Busy = false; Close(); return Task.CompletedTask; });
        Action("配置工具…", () => { OpenTools(); return Task.CompletedTask; });
        Body.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, e) => { if (e.OriginalSource is TextBox { IsReadOnly: false }) Changed(); }));
        Body.AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler((_, e) => { if (e.OriginalSource is ComboBox) Changed(); }));
        Body.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler((_, _) => Changed()));
        Body.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler((_, _) => Changed()));
        Body.AddHandler(PasswordBox.PasswordChangedEvent, new RoutedEventHandler((_, _) => Changed()));
        Closing += (_, e) => { if (!Busy && !RetryRequested && !ReplacementLaunched && !MayDiscard()) e.Cancel = true; };
        Loaded += async (_, _) => await Run(async () => { try { await Read(); } finally { if (tasks is null) Render(); } });
    }
    async Task CheckCandidate()
    {
        var candidate = RequireDraft();
        var report = await Task.Run(() => maintenance.CheckAsync(candidate));
        diagnosticDetails = string.Join("\n", report.Checks.Select(c => c.Layer + " · " + c.Status + " · " + c.Reason));
        checkedInput = null;
        if (report.Checks.Any(c => c.Status == "FAIL"))
            throw new InvalidOperationException("配置检查未通过：" + string.Join("；", report.Checks.Where(c => c.Status == "FAIL").Select(c => c.Layer + "：" + c.Reason)));
        checkedInput = Input(); checkedCandidateHash = report.CandidateHash;
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
    Task ImportConfiguration()
    {
        RequireMaintenance();
        if (draft is null || document?.Candidate is null) throw new InvalidOperationException("先读取当前配置。");
        var path = FilePick(this, "导入配置文件", "配置或设置模板|*.json");
        if (path is null) return Task.CompletedTask;
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("配置文件超出4兆字节限制。");
        var replacement = StartupConfigurationFiles.Import(File.ReadAllText(path), document.Candidate, draft, document.Catalog);
        var comparison = new Window { Owner = this, Title = "导入配置预览", Width = 720, Height = 560, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new(20) };
        var apply = new Button { Content = "应用到当前填写内容", MinHeight = 44, Margin = new(0, 12, 0, 0) };
        DockPanel.SetDock(apply, Dock.Bottom); apply.Click += (_, _) => comparison.DialogResult = true; panel.Children.Add(apply);
        panel.Children.Add(new TextBox { Text = "仅导入设置，不保存、不启动。保留本供包台身份与模拟/生产用途。\n\n" + StartupConfigurationFiles.Describe(draft, replacement), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        comparison.Content = panel;
        if (comparison.ShowDialog() == true) { draft = replacement; Render(false); Message("配置已导入填写区。检查后再保存并启动。"); }
        return Task.CompletedTask;
    }
    void OpenTools()
    {
        var menu = new ContextMenu();
        foreach (var label in new[] { "导入配置文件…", "保存未完成草稿", "继续已保存草稿", "离线帮助" })
            Add(menu.Items, label);
        menu.Items.Add(new Separator());
        var advanced = new MenuItem { Header = "备份、诊断与修复" };
        foreach (var label in new[] { "读取所选配置", "分层检查与变更预览", "保存首配待生效…", "按当前首配启动", "导出新站模板…", "导出支持摘要…", "创建完整备份…", "受控修复未确认配置…", "接管已恢复的原机目录…", "安装 / 修复程序…" })
            Add(advanced.Items, label);
        var diagnostic = new MenuItem { Header = "查看完整诊断…" };
        diagnostic.Click += (_, _) => new Window { Owner = this, Title = "启动诊断", Width = 760, Height = 520, Content = new TextBox { Text = diagnosticDetails + "\n" + Status.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }.ShowDialog();
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
    new Button Action(string label, Func<Task> action) => base.Action(label, async () =>
    {
        if (label is not ("读取所选配置" or "离线帮助" or "安装 / 修复程序…" or "上一步" or "下一步" or "维护操作" or "配置工具…" or "稍后配置")) RequireMaintenance();
        await action();
    });
    void RequireMaintenance()
    {
        RequireDirectories();
        if (maintenance is null) throw new InvalidOperationException("先读取所选目录；失败读取不能沿用另一目录的维护实例。");
    }
    bool MayDiscard() => savedInput is null || savedInput == Input() || ConfirmDiscardInputs();
    void Changed()
    {
        if (rendering || Busy) return;
        checkedInput = null;
        Message("尚未保存；填写完成后点击下一步。");
    }
    void RequireChecked()
    {
        if (checkedInput is null || checkedInput != Input()) throw new InvalidOperationException("请先对当前输入执行‘分层检查与变更预览’。检查不等于生产资格，实际命令仍会重新验证。");
    }
    ParcelConfiguration RequireDraft()
    {
        if (draft is null || document is null) throw new InvalidOperationException("先读取有效配置。损坏原账请使用完整备份恢复或安装修复。");
        RequireDirectories();
        if (parameterEditor?.ValidateFields() == false) throw new InvalidOperationException("参数校验失败；点击错误定位，其他输入保留");
        return draft.Build();
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
        diagnosticDetails = string.Join("\n", document.Diagnostics) + "\n" + document.Catalog.InventoryDiagnostic + unavailableDraft;
        Message(initialConfiguration
            ? document.Candidate is null ? "无法读取配置，原文件已保留。请从配置工具选择原配置或查看诊断。"
                : unavailableDraft is not null ? "草稿不可用：原文件已保留，可重新填写；详细原因见配置工具。"
                : "填写当前供包台，或导入已有配置文件。" + (savedDraft is null ? "" : "已找到未完成内容，可从配置工具继续。")
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
        if (replaceDraft) draft = document?.Candidate is { } candidate ? new(candidate, document.Catalog) : null;
        if (draft is not null)
        {
            var c = document!.Candidate!;
            Note($"{ChineseInterfaceText.BusinessName(c.ApplicationId)} · {ChineseInterfaceText.Choice(c.Mode)} · 修订 {document.Revision}\n供包台身份只读；设备尚未启动。");
            Body.Children.Add(new Expander { Header = "配置身份与检查边界", Content = new TextBox { IsReadOnly = true, Text = $"原账散列：{document.ConfigurationHash}\n设备与服务沿已交付精确目录选择；未枚举/未连接不等于已验证。显式启动仍受完整资格和原责任检查。", TextWrapping = TextWrapping.Wrap }, Margin = new(0, 8, 0, 8) });
            policyStart = Body.Children.Count;
            Note("首次引导与日常设置共用四类参数、精确描述和完整候选校验。固定位置、实例数量与身份不作为普通参数。");
            deviceStart = Body.Children.Count;
            parameterEditor = draft.CreateParameterEditor((p, samples) => maintenance.TestBarcodeFilterAsync(p, samples), new(config.Text, data.Text));
            sharedEditor = new ParameterSettingsControl { Model = parameterEditor, MinHeight = 300, Height = initialConfiguration ? 470 : 430 };
            Body.Children.Add(sharedEditor);
            reviewStart = Body.Children.Count;
            if (!initialConfiguration) Field("维护原因（必填；保留审计与原配置字节）", reason, v => reason = v);
        }
        var recoveryStart = Body.Children.Count;
        Note("完整备份恢复：验证原库和 .recovery 同伴目录，复制到新的隔离目录。恢复标记继续阻止自动业务恢复。");
        var backupFolder = Folder("浏览完整备份目录（最多256份；校验整个恢复集，保留无效项）", backupCatalogRoot);
        backupFolder.TextChanged += (_, _) => backupCatalogRoot = backupFolder.Text;
        var backupRows = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, MinHeight = 100, MaxHeight = 260, RowHeight = 44 };
        foreach (var column in new[] { ("时间", "At"), ("完整性", "Status"), ("字节", "Bytes"), ("备份", "Path") }) backupRows.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new System.Windows.Data.Binding(column.Item2), Width = column.Item2 == "Path" ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto });
        var listBackups = new Button { Content = "读取并校验备份列表", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        listBackups.Click += async (_, _) => await Run(async () => { RequireMaintenance(); var selected = backupCatalogRoot; backupRows.ItemsSource = await Task.Run(() => maintenance.ListBackupsAsync(selected, CancellationToken.None)); Message("备份列表已读回；Verified表示完整字节集可预检恢复，不代表外部业务已经核对。"); });
        Body.Children.Add(listBackups); Body.Children.Add(backupRows);
        var backupBox = Field("完整备份数据库", backup, v => { backup = v; recovery = null; });
        backupRows.SelectionChanged += (_, _) => { if (backupRows.SelectedItem is StartupBackupItem item) { backupBox.Text = item.Path; Message(item.Status + " · " + item.Summary); } };
        var choose = new Button { Content = "选择完整备份…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        choose.Click += (_, _) => { var selected = FilePick(this, "选择完整备份数据库", "SQLite 备份|*.db;*.sqlite;*.backup|所有文件|*.*"); if (selected is not null) backupBox.Text = selected; }; Body.Children.Add(choose);
        var destination = Folder("新的隔离数据根（不覆盖原目录）", restoreRoot);
        destination.TextChanged += (_, _) => { restoreRoot = destination.Text; recovery = null; };
        var plan = new Button { Content = "检查恢复计划", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        var preview = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80 };
        plan.Click += async (_, _) => await Run(async () => { RequireMaintenance(); recovery = await Task.Run(() => maintenance.PlanRestoreAsync(backup, restoreRoot)); preview.Text = recovery.Summary + "\n目标：" + recovery.Destination; Message("完整备份已校验；尚未恢复。检查目标后执行下方恢复。"); });
        Body.Children.Add(plan); Body.Children.Add(preview);
        var restore = new Button { Content = "按计划恢复到新目录…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        restore.Click += async (_, _) => await Run(async () =>
        {
            RequireMaintenance(); if (!MayDiscard()) return;
            var selected = recovery ?? throw new InvalidOperationException("先检查恢复计划");
            await Task.Run(() => maintenance.RestoreAsync(backup, selected.Destination, selected.BackupSha256));
            restoredPreview = selected; restoredBackup = backup;
            data.Text = selected.DataRoot; await Read(true);
            Message("完整备份已恢复并重新读取新目录。原目录保留；重试启动时隔离恢复责任仍由业务守卫处理。"); recovery = null;
        }); Body.Children.Add(restore);
        var fields = Body.Children.Cast<UIElement>().ToArray(); Body.Children.Clear();
        tasks = new TabControl { Name = "StartupTasks", Margin = new(0, 8, 0, 8) };
        void Section(string title, string guidance, IEnumerable<UIElement> items)
        {
            var panel = new StackPanel { Margin = new(16) };
            panel.Children.Add(new TextBlock { Text = guidance, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16), FontSize = 16 });
            foreach (var item in items) panel.Children.Add(item);
            tasks.Items.Add(new TabItem { Header = title, Content = panel, MinHeight = 44 });
        }
        if (initialConfiguration)
        {
            var hosts = new StackPanel[4];
            foreach (var (title, index) in new[] { ("1 业务模块", 0), ("2 系统", 1), ("3 驱动", 2), ("4 组件", 3) })
            {
                var host = new StackPanel { Margin = new(12) }; hosts[index] = host;
                if (index == 0) host.Children.Add(new TextBlock { Text = "当前业务：" + ChineseInterfaceText.BusinessName(document?.Candidate?.ApplicationId ?? ""), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
                tasks.Items.Add(new TabItem { Header = title, Content = host, MinHeight = 44 });
            }
            var import = new Button { Content = "导入配置文件…", HorizontalAlignment = HorizontalAlignment.Left, MinHeight = 44, Margin = new(0, 0, 0, 12) };
            import.Click += async (_, _) => await Run(ImportConfiguration); hosts[0].Children.Insert(0, import);
            Section("5 检查与完成", "核对以下设置，点击保存并启动。启动失败会保留已保存内容，并说明需要处理的项目。", fields.Skip(reviewStart).Take(recoveryStart - reviewStart));
            var review = (StackPanel)((TabItem)tasks.Items[4]).Content;
            var changes = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            review.Children.Add(changes);
            var locations = new StackPanel(); foreach (var item in fields.Take(policyStart)) locations.Children.Add(item);
            hosts[1].Children.Add(new Expander { Header = "本机目录与配置身份", Content = locations, Margin = new(0, 0, 0, 12) });
            var repair = new StackPanel(); foreach (var item in fields.Skip(recoveryStart)) repair.Children.Add(item);
            review.Children.Add(new Expander { Header = "完整备份与隔离恢复", Content = repair });
            void MoveEditor()
            {
                if (sharedEditor is null) return;
                foreach (var host in hosts) host.Children.Remove(sharedEditor);
                if (tasks.SelectedIndex is >= 0 and < 4)
                {
                    sharedEditor.ShowCategory(new[] { 1, 0, 2, 3 }[tasks.SelectedIndex]);
                    if (tasks.SelectedIndex == 3 && parameterEditor?.Components.FirstOrDefault(x => x.Kind == "integration") is { } connection)
                        sharedEditor.NavigateTo(connection.InstanceId + "/Target");
                    hosts[tasks.SelectedIndex].Children.Add(sharedEditor);
                }
                else if (draft is not null)
                    try { changes.Text = StartupConfigurationFiles.Summary(draft, document!.Candidate!) + "\n\n本次更改\n" + StartupConfigurationFiles.Describe(new StartupConfigurationDraft(document!.Candidate!, document.Catalog), draft); }
                    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Text.Json.JsonException) { changes.Text = "候选尚不能完成比较：" + ex.Message + "。输入保留，请返回对应步骤修正。"; }
            }
            tasks.SelectionChanged += (_, e) => { if (e.OriginalSource == tasks) MoveEditor(); };
            if (sharedEditor is not null) sharedEditor.MaintenanceRequested += (_, _) => tasks.SelectedIndex = 4;
            tasks.SelectedIndex = Math.Clamp(selectedTask, 0, 4); MoveEditor();
        }
        else
        {
        Section("1 目录与工位", "先核对原目录和工位。改变目录后显式重新读取；目录授权仍由受保护安装机制执行。", fields.Take(policyStart));
        Section("2 设备与参数", "按业务位绑定精确组件与稳定设备身份；登录按组合可选。离线选择不表示已连接或已获资格。", fields.Skip(deviceStart).Take(reviewStart - deviceStart));
        Section("3 参数说明", "系统、业务模块、驱动、组件的参数统一在设备与参数页编辑；保存与启动分开。", fields.Skip(policyStart).Take(deviceStart - policyStart));
        Section("4 检查与启动", "检查当前输入，再保存待生效修订并重试启动。未确认配置失败可在恢复维护中受控修复；原失败与原请求保留。", fields.Skip(reviewStart).Take(recoveryStart - reviewStart));
        Section("恢复与维护", "完整备份、隔离恢复、原机接管和程序维护是独立任务；不会因切换本页执行。原机迁移仍需原根退役和外部责任核对。", fields.Skip(recoveryStart));
        tasks.SelectedIndex = selectedTask;
        }
        tasks.SelectionChanged += (_, e) => { if (e.OriginalSource == tasks) { selectedTask = tasks.SelectedIndex; UpdateActions(); } };
        Body.Children.Add(tasks); UpdateActions(); rendering = false;
        if (replaceDraft) savedInput = Input();
    }
    void UpdateActions()
    {
        if (initialConfiguration)
        {
            foreach (var button in Actions.Children.OfType<Button>())
            {
                var label = (string)button.Content;
                button.Visibility = label is "配置工具…" or "稍后配置" || label == "上一步" && selectedTask > 0 || label == "下一步" && selectedTask < 4 || label == "保存并启动" && selectedTask == 4 ? Visibility.Visible : Visibility.Collapsed;
                if (label == "保存并启动") { button.Background = new SolidColorBrush(Color.FromRgb(32, 95, 206)); button.Foreground = Brushes.White; }
            }
            return;
        }
        var groups = new[] { 0, 3, -1, 0, 4, 1, 3, 4, 4, 4, 4, -1, 4, 3, -1, -2, 4 };
        for (var i = 0; i < Actions.Children.Count; i++)
            Actions.Children[i].Visibility = i < groups.Length && (groups[i] == -1 || groups[i] == selectedTask || groups[i] == -2 && selectedTask < 3) ? Visibility.Visible : Visibility.Collapsed;
    }
}

public sealed record InstallationPreparationProgress(string Stage, string File, double Percent);

public sealed class StartupSetupWindow : StartupWorkflowWindow
{
    readonly BusinessLaunch launch;
    readonly Func<IProgress<InstallationPreparationProgress>, CancellationToken, Task<string>>? prepareMedia;
    CancellationTokenSource? preparing;
    readonly ProgressBar preparationProgress = new() { Height = 6, Margin = new(0, 24, 0, 12) };
    readonly TextBlock preparationFile = new() { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 };
    DeploymentMedia? media;
    WindowsDeploymentRequest? reviewed;
    DeploymentComponentReview[] components = [];
    DeploymentLocation? installed;
    public DeploymentLocation? Installed => installed;
    public bool Launched { get; private set; }
    TextBox account = null!, data = null!, configuration = null!, trust = null!, program = null!, station = null!, upgradeBackup = null!;
    CheckBox repairSeed = null!, newSite = null!, startMenu = null!, desktopIcon = null!, runAfter = null!;
    ComboBox siteMode = null!;
    string newStation = "", selectedProgram = "", selectedBackup = "";
    string? enteredAccount, enteredData, enteredConfiguration, enteredTrust;
    bool? enteredNewSite, enteredRepair;
    bool selectedStartMenu = true, selectedDesktop = true;
    RunMode selectedMode = RunMode.Simulation;
    string[] packages = [];
    int step;
    readonly Button next, back, cancel;
    public StartupSetupWindow(BusinessLaunch launch, Func<IProgress<InstallationPreparationProgress>, CancellationToken, Task<string>>? prepareMedia = null, string? businessVersion = null) : base("安装 · 菜鸟供包台", installation: true)
    {
        this.launch = launch; this.prepareMedia = prepareMedia;
        if (businessVersion is not null) { VersionLabel.Text = "版本: " + (ParcelStartupServices.ApplicationId == "sportmaster" ? "SM " : "") + "V" + businessVersion + " Windows 64位"; VersionLabel.Visibility = Visibility.Visible; }
        back = Action("上一步", () => { CaptureInputs(); if (step > 0) step--; Render(); return Task.CompletedTask; });
        next = Action("检查安装媒介", Next);
        cancel = Action("取消", () => { Busy = false; Close(); return Task.CompletedTask; });
        next.IsDefault = true; cancel.IsCancel = true;
        Render();
        Loaded += async (_, _) => await Run(Next);
        Closing += (_, _) => { if (preparing is not null) { CloseAfterCancellation = true; preparing.Cancel(); } };
    }
    async Task ResumeRemoval()
    {
        var cleanupMedia = await Task.Run(() => DeploymentMedia.Inspect(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")), ApplicationId));
        var registration = WindowsDeploymentSetup.Registration(ApplicationId);
        var result = await Task.Run(() => ParcelStartupServices.ResumeRemovalAsync(cleanupMedia, Wire.FileHash(registration)));
        Message(result.Status + "\n" + result.OperationFile + (result.RestartRequired ? "\n需重启Windows完成占用文件清理" : ""));
    }
    TextBox InstallationFolder(string label, string value)
    {
        Note(label);
        var row = new Grid { Margin = new(0, 0, 0, 8) }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var box = new TextBox { Text = value, MinHeight = 44 }; System.Windows.Automation.AutomationProperties.SetName(box, label);
        var browse = new Button { Content = "浏览…", MinHeight = 44, MinWidth = 80, Margin = new(8, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(browse, "浏览" + label);
        browse.Click += (_, _) => { var picker = new OpenFolderDialog { Title = label }; if (picker.ShowDialog(this) == true) box.Text = picker.FolderName; };
        Grid.SetColumn(browse, 1); row.Children.Add(box); row.Children.Add(browse); Form.Children.Add(row); return box;
    }
    string ApplicationId => ParcelStartupServices.ApplicationId;
    void CaptureInputs()
    {
        if (step != 1) return;
        enteredAccount = account.Text; enteredData = data.Text; enteredConfiguration = configuration.Text; enteredTrust = trust.Text;
        selectedProgram = program.Text; selectedBackup = upgradeBackup.Text; enteredNewSite = newSite.IsChecked == true; enteredRepair = repairSeed.IsChecked == true;
        selectedStartMenu = startMenu.IsChecked == true; selectedDesktop = desktopIcon.IsChecked == true;
    }
    void Render()
    {
        Body.Children.Clear(); Form = Body;
        back.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        cancel.Visibility = step == 3 ? Visibility.Collapsed : Visibility.Visible;
        Message(step switch { 0 => "正在检查安装文件，请稍候…", 1 => "选择安装位置和快捷方式，然后点击下一步。", 2 => "确认以下选项，点击安装后由 Windows 请求授权。", _ => "安装完成，可立即运行菜鸟供包台。" });
        if (step == 0)
        {
            preparationProgress.IsIndeterminate = prepareMedia is null;
            Body.Children.Add(preparationProgress); Body.Children.Add(preparationFile);
            Note("正在核对版本与完整性，完成后自动显示安装选项。"); next.Content = "重试检查";
        }
        else if (step == 1)
        {
            var current = WindowsDeploymentSetup.Current(ApplicationId);
            program = InstallationFolder("安装目录", selectedProgram.Length > 0 ? selectedProgram : reviewed?.ProgramRoot ?? (current is null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CainiaoSupplyStation") : Directory.GetParent(Directory.GetParent(current.ReleaseRoot)!.FullName)!.FullName));
            Note("升级保留旧版本和原始数据。");
            startMenu = new CheckBox { Content = "在开始菜单创建快捷方式", IsChecked = selectedStartMenu, MinHeight = 44 }; Body.Children.Add(startMenu);
            desktopIcon = new CheckBox { Content = "在桌面创建快捷方式", IsChecked = selectedDesktop, MinHeight = 44 }; Body.Children.Add(desktopIcon);
            data = InstallationFolder("数据目录", enteredData ?? reviewed?.DataRoot ?? current?.DataRoot ?? launch.DataRoot);
            var advanced = new StackPanel(); Body.Children.Add(new Expander { Header = "其他安装选项（通常无需更改）", Content = advanced, Margin = new(0, 20, 0, 12) }); Form = advanced;
            Group("系统检查（只读）");
            var environment = WindowsClientCompatibility.Capture();
            Form.Children.Add(new Expander { Header = "系统兼容性诊断（只读）", Content = new TextBox { Text = "兼容性：" + environment.Assessment.Reason + "\n\n" + Wire.Encode(environment), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            Group("Windows 账号（默认当前账号）");
            var defaultAccount = ParcelStartupServices.DefaultRuntimeAccount();
            account = Field("使用此软件的 Windows 账号（默认当前账号）", enteredAccount ?? defaultAccount, _ => { });
            Group("供包台身份与用途（仅新装时设置）", current is null);
            newSite = new CheckBox { Content = "新供包台：使用随包初始配置", IsChecked = enteredNewSite ?? (current is null && reviewed is null), IsEnabled = current is null, MinHeight = 44 }; Form.Children.Add(newSite);
            station = Field("供包台内部标识（初装使用；升级保留）", newStation, v => newStation = v, readOnly: current is not null);
            Note("使用用途：模拟用于本地试用；生产连接现场设备，需安装对应驱动并完成资格检查。安装后不能直接切换用途。");
            siteMode = new ComboBox { ItemsSource = Enum.GetValues<RunMode>(), ItemTemplate = ChineseChoiceTextConverter.CreateTemplate(), SelectedItem = selectedMode, IsEnabled = current is null, MinHeight = 44 }; siteMode.SelectionChanged += (_, _) => { if (siteMode.SelectedItem is RunMode value) selectedMode = value; }; Form.Children.Add(siteMode);
            Group("原配置与升级备份（迁移或修复时使用）");
            configuration = Folder("已有配置目录（仅迁移或修复时更改）", enteredConfiguration ?? reviewed?.ConfigurationSource ?? current?.ConfigurationDirectory ?? Path.Combine(media!.Root, "app", "config"));
            upgradeBackup = Folder("升级前完整备份目录（自动生成新的完整集合，不覆盖原备份）", selectedBackup.Length > 0 ? selectedBackup : Path.Combine(data.Text, "backups"));
            repairSeed = new CheckBox { Content = "修复初始配置（仅配置损坏时使用，保留原件）", IsChecked = enteredRepair ?? reviewed?.RepairSeed ?? false, MinHeight = 44 }; Form.Children.Add(repairSeed);
            Group("离线驱动与组件（使用签名扩展时填写）");
            trust = Field("可选：独立离线信任文件（无扩展时可留空）", enteredTrust ?? reviewed?.TrustFile ?? "", _ => { });
            var chooseTrust = new Button { Content = "选择信任文件…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
            chooseTrust.Click += (_, _) => { var selected = FilePick(this, "选择独立离线信任政策", "信任文件|*.json"); if (selected is not null) trust.Text = selected; }; Form.Children.Add(chooseTrust);
            Note("不创建测试或 Production 信任。已安装信任只允许相同字节；轮换须另行审查。需要真实 SDK 的部署仍需独立安装及资格证据。");
            var selectedPackages = new TextBlock { Text = string.Join("\n", packages), TextWrapping = TextWrapping.Wrap };
            var choosePackages = new Button { Content = "选择可选离线扩展包…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
            choosePackages.Click += (_, _) => { var picker = new OpenFileDialog { Title = "选择原始签名扩展包", Multiselect = true, Filter = "离线扩展包|*.zip;*.ipcpkg|所有文件|*.*" }; if (picker.ShowDialog(this) == true) { packages = picker.FileNames; selectedPackages.Text = string.Join("\n", packages); } };
            Form.Children.Add(choosePackages); Form.Children.Add(selectedPackages);
            var cleanup = new Button { Content = "继续退役程序的中断清理…", MinHeight = 44, Margin = new(0, 12, 0, 0) }; cleanup.Click += async (_, _) => await Run(ResumeRemoval); Form.Children.Add(cleanup);
            void Group(string label, bool applicable = true)
            {
                var panel = new StackPanel { Margin = new(12, 4, 0, 12) };
                advanced.Children.Add(new Expander { Header = label, Content = panel, Visibility = applicable ? Visibility.Visible : Visibility.Collapsed, Margin = new(0, 4, 0, 4) }); Form = panel;
            }
            Form = Body;
            Note("安装不连接设备。新供包台默认用于模拟试用，首次启动再填写业务参数。"); next.Content = "下一步";
        }
        else if (step == 2)
        {
            var r = reviewed!;
            Note("安装目录：" + r.ProgramRoot + "\n数据目录：" + r.DataRoot + "\n开始菜单快捷方式：" + (r.Shortcuts!.StartMenu ? "创建" : "不创建") + "\n桌面快捷方式：" + (r.Shortcuts.Desktop ? "创建" : "不创建") + "\n使用用途：" + ChineseInterfaceText.Choice(r.Mode));
            Note("点击安装后只需一次 Windows 授权。安装时检查原责任并完成备份；有阻断则保留原部署，不执行升级。");
            if (r.BackupDestination is not null) Note("升级前完整备份：" + r.BackupDestination);
            var details = new StackPanel(); Body.Children.Add(new Expander { Header = "安装诊断详情", Content = details, Margin = new(0, 16, 0, 12) }); Form = details;
            Note($"业务：{ChineseInterfaceText.BusinessName(r.ApplicationId)}\n程序来源标识：{r.SourceCommit}\nWindows账户安全标识：{r.RuntimeAccountSid}\n初始配置目录：{r.ConfigurationSource}\n扩展信任文件：{r.TrustFile ?? "未选择"}\n升级备份：{r.BackupDestination ?? "新供包台无需备份"}");
            foreach (var component in components) Note("组件 " + component.Package + " @ " + component.Version + " · " + component.ExpandedBytes + "字节\n能力 " + string.Join(", ", component.Capabilities) + "\n精确依赖 " + string.Join(", ", component.Dependencies) + "\n系统前置条件 " + (component.Prerequisites.Length == 0 ? "本包未声明额外厂商安装项" : string.Join("；", component.Prerequisites)) + "\n仅签名/闭包检查；尚未绑定、激活、连接或允许业务。");
            Form = Body; next.Content = "安装";
        }
        else
        {
            Note("菜鸟供包台已安装到：\n" + installed!.ReleaseRoot);
            Note("快捷方式已按所选选项处理。数据目录：\n" + installed.DataRoot);
            var canRun = ParcelStartupServices.IsRuntimeAccount(installed);
            runAfter = new CheckBox { Content = "安装完成后运行菜鸟供包台", IsChecked = canRun, IsEnabled = canRun, MinHeight = 44 }; Body.Children.Add(runAfter);
            if (!canRun) Note("请登录指定运行账户后运行程序。");
            next.Content = "完成";
        }
    }
    async Task Next()
    {
        if (step == 0)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("安装向导需要 Windows");
            var environment = WindowsClientCompatibility.Capture();
            if (!environment.Assessment.CanStart) throw new PlatformNotSupportedException(environment.Assessment.Reason);
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
            if (prepareMedia is not null)
            {
                using var cancellation = new CancellationTokenSource(); preparing = cancellation;
                var progress = new Progress<InstallationPreparationProgress>(value =>
                { Message(value.Stage); preparationProgress.Value = value.Percent; preparationFile.Text = value.File; });
                try { root = await prepareMedia(progress, cancellation.Token); }
                finally { preparing = null; }
            }
            media = await Task.Run(() => DeploymentMedia.Inspect(root, ApplicationId));
            VersionLabel.Text = "版本: " + (ApplicationId == "sportmaster" ? "SM " : "") + "V" + Wire.Decode<ApplicationRelease>(File.ReadAllText(Path.Combine(media.Root, "desktop", "application-release.json"))).BusinessVersion + " Windows 64位";
            VersionLabel.Visibility = Visibility.Visible;
            newStation = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(media.Root, "app", "config", "business.json"))).StationId;
            step = 1; Render();
        }
        else if (step == 1)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            CaptureInputs();
            var selectedAccount = account.Text; var dataRoot = data.Text; var configPath = configuration.Text;
            var trustPath = trust.Text; var selected = packages.ToArray(); var repair = repairSeed.IsChecked == true;
            selectedProgram = program.Text; selectedBackup = upgradeBackup.Text;
            if (newSite.IsChecked == true)
            {
                var seedDirectory = Path.Combine(Path.GetTempPath(), "ipc-new-site-" + Wire.Id());
                await Task.Run(() => ParcelStartupServices.CreateSiteSeed(media!, seedDirectory, newStation, selectedMode)); configPath = seedDirectory;
            }
            reviewed = await Task.Run(() => ParcelStartupServices.Review(media!, selectedAccount, dataRoot, configPath, trustPath, selected, repair, selectedProgram));
            reviewed = reviewed with { Shortcuts = new(selectedStartMenu, selectedDesktop) };
            components = await Task.Run(() => DeploymentComponentReviewing.Inspect(reviewed));
            var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(reviewed.ConfigurationSource, "business.json")));
            if (WindowsDeploymentSetup.Current(ApplicationId) is not null || File.Exists(Path.Combine(reviewed.DataRoot, seed.Mode.ToString(), seed.StationId, ApplicationId, ApplicationId + ".db")))
            {
                var destination = Path.Combine(Path.GetFullPath(selectedBackup), ApplicationId + "-pre-upgrade-" + Wire.Id() + ".db");
                reviewed = reviewed with { BackupDestination = destination };
            }
            step = 2; Render();
        }
        else if (step == 2)
        {
            Message("正在安装菜鸟供包台，请在 Windows 授权窗口确认并等待安装完成。");
            Body.Children.Clear(); Body.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6, Margin = new(0, 24, 0, 24) });
            Note("正在复制和校验程序、保存安装登记并创建所选快捷方式…");
            try { installed = await Task.Run(() => ParcelStartupServices.InstallWithPreflightAsync(reviewed!)); step = 3; Render(); }
            catch { Render(); throw; }
        }
        else
        {
            if (runAfter.IsChecked == true)
            {
                if (!ParcelStartupServices.IsRuntimeAccount(installed!)) throw new InvalidOperationException("请登录指定运行账户后运行程序");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(installed!.ReleaseRoot, "desktop", "Ipc.Desktop.exe")) { UseShellExecute = true, WorkingDirectory = installed.ReleaseRoot });
                Launched = true;
            }
            Busy = false; Close();
        }
    }
}
