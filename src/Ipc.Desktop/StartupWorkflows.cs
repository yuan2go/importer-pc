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
        var grid = new Grid { Margin = new(24) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel(); heading.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.Bold });
        heading.Children.Add(new ScrollViewer { Content = Status, MaxHeight = 116, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var diagnostic = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        diagnostic.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source = Status });
        heading.Children.Add(new Expander { Header = "完整诊断（可选择复制）", Content = diagnostic });
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
        catch (Exception ex) { Status.Text = "未完成：" + ex.Message + "\n已有数据与配置保留。可修正输入后重试。"; Status.Foreground = Brushes.Firebrick; }
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
        Note(label); var box = new ComboBox { ItemsSource = Enum.GetValues<T>(), SelectedItem = selected, MinHeight = 36 };
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is T value) changed(value); }; Form.Children.Add(box);
    }
    protected static string? FilePick(Window owner, string title, string filter = "所有文件|*.*")
    { var picker = new OpenFileDialog { Title = title, Filter = filter }; return picker.ShowDialog(owner) == true ? picker.FileName : null; }
    protected void CompleteDialog() { Busy = false; DialogResult = true; }
}

sealed class StartupRepairWindow : StartupWorkflowWindow
{
    readonly UIElement[] pathFields;
    TabControl? tasks;
    bool rendering = true;
    string? savedInput, checkedInput;
    int selectedTask;
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
    public StartupRepairWindow(BusinessLaunch launch, string? failure = null, bool initialConfiguration = false) : base(initialConfiguration ? "首次工位配置 · 菜鸟供包台" : "启动修复 · 离线维护")
    {
        SelectedLaunch = launch;
        ConfirmDiscardInputs = () => MessageBox.Show(this, "有尚未保存的输入。返回可保存加密草稿；继续将放弃当前未保存输入，既有配置账和已保存草稿保留。", "保护未完成配置", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
        if (initialConfiguration) reason = "首次工位配置";
        Message("无需业务启动成功。检查和编辑不连接设备；保存为 Pending，正常启动仍检查原恢复责任。" + (failure is null ? "" : "\n启动诊断：" + failure));
        config = Folder("原配置目录（包含 business.json）", launch.ConfigurationDirectory);
        data = Folder("原数据根（保留 Simulation/Production、站和 Application 层级）", launch.DataRoot);
        pathFields = Body.Children.Cast<UIElement>().ToArray();
        Action("读取所选配置", Read);
        Action("分层检查与变更预览", async () => { var candidate = RequireDraft(); var report = await Task.Run(() => maintenance.CheckAsync(candidate)); checkedInput = Input(); Message("检查时间 " + DateTimeOffset.Now.ToString("g") + "\n候选 " + report.CandidateHash + "\n" + string.Join("\n", report.Checks.Select(c => c.Layer + " · " + c.Status + " · " + c.Reason)) + "\n输入改变后检查失效。此检查不连接设备，不授予生产资格；保存和启动仍执行完整守卫。"); });
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
        Action("导入同版安全模板…", () =>
        {
            if (draft is null || document?.Candidate is null) throw new InvalidOperationException("先读取配置");
            var path = FilePick(this, "导入无业务状态模板", "模板|*.json"); if (path is null) return Task.CompletedTask;
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("模板超出配额");
            var values = StartupSiteTemplates.Import(document.Candidate.ApplicationId, Wire.Decode<StartupSiteTemplate>(File.ReadAllText(path)));
            draft = draft.WithRestored(values); Render(false); Message("模板只改变草稿；原站身份、设备绑定、目标和原责任保留，检查后再提交。"); return Task.CompletedTask;
        });
        Action(initialConfiguration ? "保存首配待生效…" : "保存待生效修订…", async () =>
        {
            RequireChecked(); var candidate = RequireDraft(); var expected = document!.ConfigurationHash;
            document = await Task.Run(() => initialConfiguration ? maintenance.RepairInitialAsync(expected, candidate, reason) : maintenance.StageAsync(expected, candidate, reason)); Render(); Message("保存后已读回状态：" + document.ConfigurationStatus + "。保存过程不改 Effective 或原数据库；重试启动时由业务决定能否激活。");
        });
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
        Action(initialConfiguration ? "按当前首配启动" : "重试正常启动", () => { RequireChecked(); var candidate = RequireDraft(); if (savedInput != Input() || Wire.Encode(candidate) != Wire.Encode(document!.Candidate)) throw new InvalidOperationException("当前输入尚未提交为待生效配置。请保存待生效修订并重新检查；未完成草稿不能作为启动配置。"); SelectedLaunch = SelectedLaunch with { ConfigurationDirectory = config.Text, DataRoot = data.Text }; RetryRequested = true; CompleteDialog(); return Task.CompletedTask; });
        Action("上一步", () => { if (tasks is not null) tasks.SelectedIndex = Math.Max(0, tasks.SelectedIndex - 1); return Task.CompletedTask; });
        var next = Action("下一步", () => { if (tasks is not null) tasks.SelectedIndex = Math.Min(3, tasks.SelectedIndex + 1); return Task.CompletedTask; });
        next.Background = new SolidColorBrush(Color.FromRgb(32, 95, 206)); next.Foreground = Brushes.White;
        Body.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, e) => { if (e.OriginalSource is TextBox { IsReadOnly: false }) Changed(); }));
        Body.AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler((_, e) => { if (e.OriginalSource is ComboBox) Changed(); }));
        Body.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler((_, _) => Changed()));
        Body.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler((_, _) => Changed()));
        Body.AddHandler(PasswordBox.PasswordChangedEvent, new RoutedEventHandler((_, _) => Changed()));
        Closing += (_, e) => { if (!Busy && !RetryRequested && !ReplacementLaunched && !MayDiscard()) e.Cancel = true; };
        Loaded += async (_, _) => await Run(async () => { try { await Read(); } finally { if (tasks is null) Render(); } });
    }
    string Input() => config.Text + "\n" + data.Text + "\n" + reason + "\n" + (draft is null ? "" : Wire.Encode(draft.Capture()));
    new Button Action(string label, Func<Task> action) => base.Action(label, async () =>
    {
        if (label is not ("读取所选配置" or "离线帮助" or "安装 / 修复程序…" or "上一步" or "下一步")) RequireMaintenance();
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
        Message("输入已改变：此前检查失效。可继续填写或保存未完成草稿；到‘检查与启动’重新检查，不会自动生效。");
    }
    void RequireChecked()
    {
        if (checkedInput is null || checkedInput != Input()) throw new InvalidOperationException("请先对当前输入执行‘分层检查与变更预览’。检查不等于生产资格，实际命令仍会重新验证。");
    }
    ParcelConfiguration RequireDraft()
    {
        if (draft is null || document is null) throw new InvalidOperationException("先读取有效配置。损坏原账请使用完整备份恢复或安装修复。");
        RequireDirectories();
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
        document = null; draft = null; savedDraft = null; maintenance = null!; checkedInput = savedInput = null;
        try
        {
        SelectedLaunch = SelectedLaunch with { ConfigurationDirectory = Path.GetFullPath(config.Text), DataRoot = Path.GetFullPath(data.Text) };
        config.Text = SelectedLaunch.ConfigurationDirectory; data.Text = SelectedLaunch.DataRoot;
        maintenance = ParcelStartupServices.Maintenance(SelectedLaunch);
        document = await Task.Run(maintenance.ReadAsync); recovery = null; savedDraft = null;
        string? unavailableDraft = null;
        try { if (document.Candidate is not null) savedDraft = await Task.Run(maintenance.ReadDraftAsync); }
        catch (System.ComponentModel.Win32Exception error)
        {
            // A recovered machine-DPAPI draft is private, non-authoritative input.
            // Keep its bytes and CAS protection; it cannot hide the repair editor.
            unavailableDraft = "\n草稿不可用：本机无法解密原DPAPI草稿（" + error.NativeErrorCode + "）。原密文保留、不覆盖；可从当前配置重新填写秘密并受控提交。换机恢复仍隔离，不能代替来源退役与资格核对。";
        }
        Render();
        Message(string.Join("\n", document.Diagnostics) + "\n" + document.Catalog.InventoryDiagnostic + unavailableDraft + (savedDraft is null ? "" : "\n发现未完成草稿：" + savedDraft.At + (savedDraft.BaseChanged ? "，基线已变化，保留但不自动应用" : "，可选择继续")));
        }
        catch { Render(); throw; }
    }
    void Render(bool replaceDraft = true)
    {
        rendering = true; checkedInput = null;
        // Reparent the same directory controls; navigation never reconstructs a draft or runtime.
        if (tasks is not null) foreach (TabItem tab in tasks.Items) if (tab.Content is Panel panel) panel.Children.Clear();
        Body.Children.Clear(); foreach (var field in pathFields) Body.Children.Add(field);
        var policyStart = Body.Children.Count; var deviceStart = policyStart; var reviewStart = policyStart;
        if (replaceDraft) draft = document?.Candidate is { } candidate ? new(candidate, document.Catalog) : null;
        if (draft is not null)
        {
            var c = document!.Candidate!;
            Note($"{c.ApplicationId} · {c.StationId} · {c.Mode} · 修订 {document.Revision} · {document.ConfigurationStatus}\n工位身份只读；设备尚未启动。可先保存未完成草稿。");
            Body.Children.Add(new Expander { Header = "配置身份与检查边界", Content = new TextBox { IsReadOnly = true, Text = $"原账散列：{document.ConfigurationHash}\n设备与服务沿已交付精确目录选择；未枚举/未连接不等于已验证。显式启动仍受完整资格和原责任检查。", TextWrapping = TextWrapping.Wrap }, Margin = new(0, 8, 0, 8) });
            policyStart = Body.Children.Count;
            Field("输入队列容量", draft.QueueCapacity, v => draft.QueueCapacity = v);
            Field("调用超时（ms）", draft.TimeoutMs, v => draft.TimeoutMs = v);
            Field("作业容量", draft.MaximumOperations, v => draft.MaximumOperations = v);
            Field("常驻历史条数（同库归档不释放磁盘）", draft.ResidentHistory, v => draft.ResidentHistory = v);
            Field("关键写入保留空间（MiB）", draft.MinimumFreeMiB, v => draft.MinimumFreeMiB = v);
            var scheduledBackup = new CheckBox { Content = "启用受控计划备份（仅在已暂停、维护授权有效且无在途外呼时触发；不会自动暂停业务）", IsChecked = draft.Policy.BackupEnabled, MinHeight = 44 };
            scheduledBackup.Checked += (_, _) => draft.Policy.BackupEnabled = true; scheduledBackup.Unchecked += (_, _) => draft.Policy.BackupEnabled = false; Body.Children.Add(scheduledBackup);
            var scheduleDirectory = Folder("计划备份目录（原业务目录以外；须有容量）", draft.Policy.BackupDirectory); scheduleDirectory.TextChanged += (_, _) => draft.Policy.BackupDirectory = scheduleDirectory.Text;
            Field("计划备份周期（分钟，15～10080；未启用可留空）", draft.Policy.BackupIntervalMinutes, v => draft.Policy.BackupIntervalMinutes = v);
            Field("备份容量份数上限（1～1000；到限保留所有旧备份并报错，不自动删除）", draft.Policy.BackupMaximumSets, v => draft.Policy.BackupMaximumSets = v);
            Choice("重量来源", draft.Policy.WeightSource, v => draft.Policy.WeightSource = v);
            Choice("重量单位", draft.Policy.Unit, v => draft.Policy.Unit = v);
            Field("配置重量（仅 Configured 使用）", draft.Policy.ConfiguredWeight, v => draft.Policy.ConfiguredWeight = v);
            Field("重量精度", draft.Policy.Precision, v => draft.Policy.Precision = v);
            Field("测量最大年龄（ms）", draft.Policy.MaximumAgeMs, v => draft.Policy.MaximumAgeMs = v);
            Field("等待重量期限（ms）", draft.Policy.WaitTimeoutMs, v => draft.Policy.WaitTimeoutMs = v);
            Choice("重量有效期", draft.Policy.Validity, v => draft.Policy.Validity = v);
            deviceStart = Body.Children.Count;
            foreach (var capability in draft.Readers.Concat(draft.Capabilities))
            {
                var previousForm = Form; Form = new StackPanel { Margin = new(12) };
                var role = capability.Role switch { "operator-login" => "操作员登录（可选）", "maintenance-auth" => "维护认证", "default-scale" => "默认实测秤（可选）", _ => capability.Kind == "observation" ? "读码来源 · " + capability.Target : capability.InstanceId == c.Wcs?.InstanceId ? "WCS 格口服务" : capability.Role };
                previousForm.Children.Add(new GroupBox { Header = role, Content = Form, Margin = new(0, 12, 0, 4) });
                Form.Children.Add(new Expander { Header = "精确组件来源", Content = new TextBox { Text = capability.InstanceId + "\n" + capability.ProviderIdentity, IsReadOnly = true, TextWrapping = TextWrapping.Wrap } });
                if (capability.Role is "operator-login" or "default-scale")
                {
                    var optional = new CheckBox { Content = capability.Role == "operator-login" ? "启用本组合的操作员登录（维护认证另有守卫）" : "绑定默认实测秤（仅选实测重量时生效）", IsChecked = capability.Bound, MinHeight = 44 };
                    optional.Checked += (_, _) => capability.Bound = true; optional.Unchecked += (_, _) => capability.Bound = false; Form.Children.Add(optional);
                }
                var providers = new ComboBox { ItemsSource = capability.Providers, SelectedItem = capability.Provider, MinHeight = 44, DisplayMemberPath = "Implementation" };
                System.Windows.Automation.AutomationProperties.SetName(providers, capability.Role + "精确提供者");
                providers.SelectionChanged += (_, _) => { if (providers.SelectedItem is CapabilityDescriptor selected && selected != capability.Provider) { capability.Provider = selected; Render(false); } }; Form.Children.Add(providers);
                Field(capability.Kind == "observation" ? "稳定设备身份（离线录入，待真实SDK核验；按上方业务位绑定）" : "目标（地址/实例由所选组件合同决定）", capability.Target, v => capability.Target = v, capability.Provider is null);
                foreach (var parameter in capability.Parameters)
                {
                    Note(parameter.Field.Label + " · " + parameter.EditorHelp);
                    if (parameter.Field.Sensitive)
                    {
                        var password = new PasswordBox { Password = parameter.Value, MinHeight = 36, IsEnabled = !parameter.Field.ReadOnly };
                        password.PasswordChanged += (_, _) => parameter.Value = password.Password; Form.Children.Add(password);
                    }
                    else if (parameter.Field.Choices is { Length: > 0 } choices)
                    {
                        var selected = new ComboBox { ItemsSource = choices, SelectedItem = parameter.Value, MinHeight = 36, IsEnabled = !parameter.Field.ReadOnly };
                        selected.SelectionChanged += (_, _) => parameter.Value = selected.SelectedItem as string ?? ""; Form.Children.Add(selected);
                    }
                    else Field(parameter.Field.Key, parameter.Value, v => parameter.Value = v, parameter.Field.ReadOnly);
                }
                Form = previousForm;
            }
            reviewStart = Body.Children.Count;
            Field("维护原因（必填；保留审计与原配置字节）", reason, v => reason = v);
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
        Section("1 目录与工位", "先核对原目录和工位。改变目录后显式重新读取；目录授权仍由受保护安装机制执行。", fields.Take(policyStart));
        Section("2 设备与服务", "按业务位绑定精确组件与稳定设备身份；登录按组合可选。离线选择不表示已连接或已获资格。", fields.Skip(deviceStart).Take(reviewStart - deviceStart));
        Section("3 运行策略", "沿用当前组合策略；不填生产默认。计划备份需要受控维护窗口，份数到限不会删除旧备份。", fields.Skip(policyStart).Take(deviceStart - policyStart));
        Section("4 检查与启动", "检查当前输入，再保存待生效修订并重试启动。未确认配置失败可在恢复维护中受控修复；原失败与原请求保留。", fields.Skip(reviewStart).Take(recoveryStart - reviewStart));
        Section("恢复与维护", "完整备份、隔离恢复、原机接管和程序维护是独立任务；不会因切换本页执行。原机迁移仍需原根退役和外部责任核对。", fields.Skip(recoveryStart));
        tasks.SelectedIndex = selectedTask;
        tasks.SelectionChanged += (_, e) => { if (e.OriginalSource == tasks) { selectedTask = tasks.SelectedIndex; UpdateActions(); } };
        Body.Children.Add(tasks); UpdateActions(); rendering = false;
        if (replaceDraft) savedInput = Input();
    }
    void UpdateActions()
    {
        var groups = new[] { 0, 3, -1, 0, 4, 1, 3, 4, 4, 4, 4, -1, 4, 3, -1, -2 };
        for (var i = 0; i < Actions.Children.Count; i++)
            Actions.Children[i].Visibility = groups[i] == -1 || groups[i] == selectedTask || (groups[i] == -2 && selectedTask < 3) ? Visibility.Visible : Visibility.Collapsed;
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
        if (businessVersion is not null) { VersionLabel.Text = "业务模块版本 " + businessVersion + " · Windows 64 位"; VersionLabel.Visibility = Visibility.Visible; }
        back = Action("上一步", () => { CaptureInputs(); if (step > 0) step--; Render(); return Task.CompletedTask; });
        next = Action("检查安装媒介", Next);
        cancel = Action("取消", () => { Busy = false; Close(); return Task.CompletedTask; });
        next.IsDefault = true; cancel.IsCancel = true;
        Render();
        Loaded += async (_, _) => await Run(Next);
        Closing += (_, _) => { if (preparing is not null) { CloseAfterCancellation = true; preparing.Cancel(); } };
    }
    Task SavePlan()
    {
        if (reviewed is null || step != 2) throw new InvalidOperationException("先完成参数/组件检查并进入摘要");
        var picker = new SaveFileDialog { FileName = "deployment-plan.json", Filter = "固定逐站部署计划|*.json" };
        if (picker.ShowDialog(this) == true) { using var output = new StreamWriter(new FileStream(picker.FileName, FileMode.CreateNew)); output.Write(Wire.Encode(DeploymentBatch.Freeze(reviewed))); Message("逐站计划已保存；不含业务数据库或请求。配置来源/原登记/包字节均固定，变化后须重新审阅。无人值守执行需要明确的Windows管理员部署会话。"); }
        return Task.CompletedTask;
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
            var advanced = new StackPanel(); Body.Children.Add(new Expander { Header = "高级设置（运行账户、工位与维护）", Content = advanced, Margin = new(0, 20, 0, 12) }); Form = advanced;
            var environment = WindowsClientCompatibility.Capture();
            Form.Children.Add(new Expander { Header = "运行环境（可复制）", Content = new TextBox { Text = Wire.Encode(environment), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            var defaultAccount = ParcelStartupServices.DefaultRuntimeAccount();
            account = Field("运行账户（已有 Windows 用户，如 机器名\\用户名；普通启动即可使用 UAC 过滤账户）", enteredAccount ?? reviewed?.RuntimeAccountSid ?? defaultAccount, _ => { });
            newSite = new CheckBox { Content = "新工位：使用介质通用模板", IsChecked = enteredNewSite ?? (current is null && reviewed is null), IsEnabled = current is null, MinHeight = 44 }; Form.Children.Add(newSite);
            station = Field("新工位标识（仅新工位填写；已部署身份只读）", newStation, v => newStation = v);
            Note("新工位模式：Simulation仅模拟；Production缺真实能力/合同/精确资格仍不能运行。模式和站标识写入后受原身份守卫约束。");
            siteMode = new ComboBox { ItemsSource = Enum.GetValues<RunMode>(), SelectedItem = selectedMode, MinHeight = 44 }; siteMode.SelectionChanged += (_, _) => { if (siteMode.SelectedItem is RunMode value) selectedMode = value; }; Form.Children.Add(siteMode);
            configuration = Folder("高级：已有部署 / 批准的配置来源（初装复制，升级保留原种子）", enteredConfiguration ?? reviewed?.ConfigurationSource ?? current?.ConfigurationDirectory ?? Path.Combine(media!.Root, "app", "config"));
            upgradeBackup = Folder("升级前完整备份目录（自动生成新的完整集合，不覆盖原备份）", selectedBackup.Length > 0 ? selectedBackup : Path.Combine(data.Text, "backups"));
            repairSeed = new CheckBox { Content = "并排修复种子配置（保留原种子）", IsChecked = enteredRepair ?? reviewed?.RepairSeed ?? false, MinHeight = 44 }; Form.Children.Add(repairSeed);
            trust = Field("可选：独立离线信任文件（无扩展时可留空）", enteredTrust ?? reviewed?.TrustFile ?? "", _ => { });
            var chooseTrust = new Button { Content = "选择信任文件…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
            chooseTrust.Click += (_, _) => { var selected = FilePick(this, "选择独立离线信任政策", "信任文件|*.json"); if (selected is not null) trust.Text = selected; }; Form.Children.Add(chooseTrust);
            Note("不创建测试或 Production 信任。已安装信任只允许相同字节；轮换须另行审查。需要真实 SDK 的部署仍需独立安装及资格证据。");
            var selectedPackages = new TextBlock { Text = string.Join("\n", packages), TextWrapping = TextWrapping.Wrap };
            var choosePackages = new Button { Content = "选择可选离线扩展包…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
            choosePackages.Click += (_, _) => { var picker = new OpenFileDialog { Title = "选择原始签名扩展包", Multiselect = true, Filter = "离线扩展包|*.zip;*.ipcpkg|所有文件|*.*" }; if (picker.ShowDialog(this) == true) { packages = picker.FileNames; selectedPackages.Text = string.Join("\n", packages); } };
            Form.Children.Add(choosePackages); Form.Children.Add(selectedPackages);
            var cleanup = new Button { Content = "继续退役程序的中断清理…", MinHeight = 44, Margin = new(0, 12, 0, 0) }; cleanup.Click += async (_, _) => await Run(ResumeRemoval); Form.Children.Add(cleanup);
            Form = Body;
            Note("安装不启动设备；新工位默认 Simulation（模拟）。"); next.Content = "下一步";
        }
        else if (step == 2)
        {
            var r = reviewed!;
            Note("安装目录：" + r.ProgramRoot + "\n数据目录：" + r.DataRoot + "\n开始菜单快捷方式：" + (r.Shortcuts!.StartMenu ? "创建" : "不创建") + "\n桌面快捷方式：" + (r.Shortcuts.Desktop ? "创建" : "不创建") + "\n运行模式：" + r.Mode);
            Note("点击安装后只需一次 Windows 授权。安装时检查原责任并完成备份；有阻断则保留原部署，不执行升级。");
            if (r.BackupDestination is not null) Note("升级前完整备份：" + r.BackupDestination);
            var details = new StackPanel(); Body.Children.Add(new Expander { Header = "查看安装详情与维护计划", Content = details, Margin = new(0, 16, 0, 12) }); Form = details;
            Note($"{r.ApplicationId}\n来源：{r.SourceCommit}\n运行账户 SID：{r.RuntimeAccountSid}\n种子来源：{r.ConfigurationSource}\n信任：{r.TrustFile ?? "未选择"}\n升级备份：{r.BackupDestination ?? "新工位尚无原库"}");
            foreach (var component in components) Note("组件 " + component.Package + " @ " + component.Version + " · " + component.ExpandedBytes + "字节\n能力 " + string.Join(", ", component.Capabilities) + "\n精确依赖 " + string.Join(", ", component.Dependencies) + "\n系统前置条件 " + (component.Prerequisites.Length == 0 ? "本包未声明额外厂商安装项" : string.Join("；", component.Prerequisites)) + "\n仅签名/闭包检查；尚未绑定、激活、连接或允许业务。");
            var save = new Button { Content = "保存已审阅的无人值守计划…", MinHeight = 44 }; save.Click += async (_, _) => await Run(SavePlan); Form.Children.Add(save);
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
            VersionLabel.Text = "业务模块版本 " + Wire.Decode<ApplicationRelease>(File.ReadAllText(Path.Combine(media.Root, "desktop", "application-release.json"))).BusinessVersion + " · Windows 64 位";
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
