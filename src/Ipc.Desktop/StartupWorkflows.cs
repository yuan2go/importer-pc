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

abstract class StartupWorkflowWindow : Window
{
    protected readonly StackPanel Body = new();
    protected readonly WrapPanel Actions = new();
    protected StackPanel Form;
    protected readonly TextBlock Status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 12) };
    protected bool Busy;
    protected StartupWorkflowWindow(string title)
    {
        Form = Body;
        ProductBrand.Apply(this);
        Title = title; Width = 960; Height = 760; MinWidth = 680; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(241, 244, 247)); Foreground = new SolidColorBrush(Color.FromRgb(25, 39, 54));
        var grid = new Grid { Margin = new(24) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel(); heading.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.Bold }); var statusView = new ScrollViewer { Content = Status, MaxHeight = 116, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        heading.Children.Add(statusView);
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
    readonly TabControl steps = new();
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
    public StartupRepairWindow(BusinessLaunch launch, string? failure = null, bool initialConfiguration = false) : base(initialConfiguration ? "首次工位配置 · Importer PC" : "启动修复 · 离线维护")
    {
        SelectedLaunch = launch;
        if (initialConfiguration) reason = "首次工位配置";
        Message("无需业务启动成功。检查和编辑不连接设备；保存为 Pending，正常启动仍检查原恢复责任。" + (failure is null ? "" : "\n启动诊断：" + failure));
        config = Folder("原配置目录（包含 business.json）", launch.ConfigurationDirectory);
        data = Folder("原数据根（保留 Simulation/Production、站和 Application 层级）", launch.DataRoot);
        var sourceFields = new StackPanel();
        var sources = Body.Children.Cast<UIElement>().ToArray(); Body.Children.Clear();
        foreach (var item in sources) sourceFields.Children.Add(item);
        Body.Children.Add(new Expander { Header = "配置来源与数据目录", Content = sourceFields, IsExpanded = !initialConfiguration });
        Body.Children.Add(steps);
        Action("读取所选配置", Read);
        Action("分层检查与变更预览", async () => { var candidate = RequireDraft(); var report = await Task.Run(() => maintenance.CheckAsync(candidate)); Message("候选 " + report.CandidateHash + "\n" + string.Join("\n", report.Checks.Select(c => c.Layer + " · " + c.Status + " · " + c.Reason)) + "\n生效影响：设备、目标或策略变更须受控重启；原请求保持原配置。保存草稿不激活。"); });
        Action("保存未完成草稿", async () =>
        {
            if (draft is null || document is null) throw new InvalidOperationException("先读取原配置");
            savedDraft = await Task.Run(() => maintenance.SaveDraftAsync(document.ConfigurationHash, savedDraft?.DraftHash, draft.Capture()));
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
            var candidate = RequireDraft(); var expected = document!.ConfigurationHash;
            document = await Task.Run(() => initialConfiguration ? maintenance.RepairInitialAsync(expected, candidate, reason) : maintenance.StageAsync(expected, candidate, reason)); Render(); Message("保存后已读回状态：" + document.ConfigurationStatus + "。保存过程不改 Effective 或原数据库；重试启动时由业务决定能否激活。");
        });
        Action("受控修复未确认配置…", async () =>
        {
            var candidate = RequireDraft();
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
        Action("离线帮助", () => { new Window { Owner = this, Title = "Importer PC · 离线帮助", Width = 860, Height = 700, Content = new TextBox { Text = ProductHelp.QuickStart, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(16), FontSize = 16 } }.Show(); return Task.CompletedTask; });
        Action("安装 / 修复程序…", async () =>
        {
            var setup = new StartupSetupWindow(SelectedLaunch) { Owner = this }; setup.ShowDialog();
            if (setup.Launched) { ReplacementLaunched = true; Busy = false; Close(); return; }
            if (setup.Installed is { } installed) { config.Text = installed.ConfigurationDirectory; data.Text = installed.DataRoot; await Read(); }
        });
        Action(initialConfiguration ? "按当前首配启动" : "重试正常启动", () => { SelectedLaunch = SelectedLaunch with { ConfigurationDirectory = config.Text, DataRoot = data.Text }; RetryRequested = true; CompleteDialog(); return Task.CompletedTask; });
        var primaryLabels = new[] { "读取所选配置", "分层检查与变更预览", initialConfiguration ? "保存首配待生效…" : "保存待生效修订…", initialConfiguration ? "按当前首配启动" : "重试正常启动" };
        var secondary = new WrapPanel();
        foreach (var button in Actions.Children.OfType<Button>().Where(b => !primaryLabels.Contains(b.Content as string)).ToArray())
        { Actions.Children.Remove(button); secondary.Children.Add(button); }
        Actions.Children.Add(new Expander { Header = "草稿、模板、备份与支持", Content = secondary, Margin = new(0, 8, 0, 0), MaxWidth = 880 });
        Loaded += async (_, _) => await Run(Read);
    }
    ParcelConfiguration RequireDraft()
    {
        if (draft is null || document is null) throw new InvalidOperationException("先读取有效配置。损坏原账请使用完整备份恢复或安装修复。");
        if (config.Text != SelectedLaunch.ConfigurationDirectory || data.Text != SelectedLaunch.DataRoot) throw new InvalidOperationException("路径已更改，先重新读取，不能将旧草稿保存到另一个目录");
        return draft.Build();
    }
    async Task Read()
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
    void Render(bool replaceDraft = true)
    {
        var selectedStep = (steps.SelectedItem as TabItem)?.Header;
        steps.Items.Clear();
        void Step(string title)
        {
            Form = new StackPanel { Margin = new(16) };
            steps.Items.Add(new TabItem { Header = title, Content = Form });
        }
        Step("1 · 设备与角色");
        if (replaceDraft) draft = document?.Candidate is { } candidate ? new(candidate, document.Catalog) : null;
        if (draft is not null)
        {
            var c = document!.Candidate!;
            Note($"{c.StationId} · {c.Mode} · 配置{document.ConfigurationStatus}。本步骤填写读码来源和服务；设备尚未启动。");
            Form.Children.Add(new Expander { Header = "配置版本与来源（只读）", Content = new TextBox { Text = $"{c.ApplicationId} · 修订 {document.Revision}\n原账SHA256：{document.ConfigurationHash}\n工位身份只读。未枚举或未连接的能力仍待核验，启动受原责任和资格检查。", IsReadOnly = true, TextWrapping = TextWrapping.Wrap } });
            foreach (var capability in draft.Readers.Concat(draft.Capabilities))
            {
                var stepForm = Form; Form = new StackPanel { Margin = new(12) };
                var role = capability.Role switch { "operator-login" => "操作员登录（可选）", "maintenance-auth" => "维护认证", "default-scale" => "默认实测秤（可选）", _ => capability.Kind == "observation" ? "读码来源 · " + capability.Target : capability.InstanceId == c.Wcs?.InstanceId ? "WCS 格口服务" : capability.Role };
                stepForm.Children.Add(new GroupBox { Header = role, Content = Form, Margin = new(0, 12, 0, 4) });
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
                Form = stepForm;
            }
            var toPolicy = new Button { Content = "下一步：运行策略", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 16, 0, 0) };
            toPolicy.Click += (_, _) => steps.SelectedIndex = 1; Form.Children.Add(toPolicy);
            Step("2 · 运行策略");
            Field("输入队列容量", draft.QueueCapacity, v => draft.QueueCapacity = v);
            Field("调用超时（ms）", draft.TimeoutMs, v => draft.TimeoutMs = v);
            Field("作业容量", draft.MaximumOperations, v => draft.MaximumOperations = v);
            Field("常驻历史条数（同库归档不释放磁盘）", draft.ResidentHistory, v => draft.ResidentHistory = v);
            Field("关键写入保留空间（MiB）", draft.MinimumFreeMiB, v => draft.MinimumFreeMiB = v);
            var scheduledBackup = new CheckBox { Content = "启用受控计划备份（仅在已暂停、维护授权有效且无在途外呼时触发；不会自动暂停业务）", IsChecked = draft.Policy.BackupEnabled, MinHeight = 44 };
            scheduledBackup.Checked += (_, _) => draft.Policy.BackupEnabled = true; scheduledBackup.Unchecked += (_, _) => draft.Policy.BackupEnabled = false; Form.Children.Add(scheduledBackup);
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
            var toReview = new Button { Content = "下一步：检查与保存", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 16, 0, 0) };
            toReview.Click += (_, _) => steps.SelectedIndex = 2; Form.Children.Add(toReview);
            Step("3 · 检查与保存");
            Note("先填写维护原因，再使用下方“分层检查与变更预览”。核对结果后保存待生效配置，最后显式启动。启动仍检查原责任和实际能力。");
            Field("维护原因（必填；保留审计与原配置字节）", reason, v => reason = v);
        }
        Step("备份与恢复（维护）");
        Note("完整备份恢复：验证原库和 .recovery 同伴目录，复制到新的隔离目录。恢复标记继续阻止自动业务恢复。");
        var backupFolder = Folder("浏览完整备份目录（最多256份；校验整个恢复集，保留无效项）", backupCatalogRoot);
        backupFolder.TextChanged += (_, _) => backupCatalogRoot = backupFolder.Text;
        var backupRows = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, MinHeight = 100, MaxHeight = 260, RowHeight = 44 };
        foreach (var column in new[] { ("时间", "At"), ("完整性", "Status"), ("字节", "Bytes"), ("备份", "Path") }) backupRows.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new System.Windows.Data.Binding(column.Item2), Width = column.Item2 == "Path" ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto });
        var listBackups = new Button { Content = "读取并校验备份列表", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        listBackups.Click += async (_, _) => await Run(async () => { var selected = backupCatalogRoot; backupRows.ItemsSource = await Task.Run(() => maintenance.ListBackupsAsync(selected, CancellationToken.None)); Message("备份列表已读回；Verified表示完整字节集可预检恢复，不代表外部业务已经核对。"); });
        Form.Children.Add(listBackups); Form.Children.Add(backupRows);
        var backupBox = Field("完整备份数据库", backup, v => { backup = v; recovery = null; });
        backupRows.SelectionChanged += (_, _) => { if (backupRows.SelectedItem is StartupBackupItem item) { backupBox.Text = item.Path; Message(item.Status + " · " + item.Summary); } };
        var choose = new Button { Content = "选择完整备份…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        choose.Click += (_, _) => { var selected = FilePick(this, "选择完整备份数据库", "SQLite 备份|*.db;*.sqlite;*.backup|所有文件|*.*"); if (selected is not null) backupBox.Text = selected; }; Form.Children.Add(choose);
        var destination = Folder("新的隔离数据根（不覆盖原目录）", restoreRoot);
        destination.TextChanged += (_, _) => { restoreRoot = destination.Text; recovery = null; };
        var plan = new Button { Content = "检查恢复计划", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        var preview = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80 };
        plan.Click += async (_, _) => await Run(async () => { recovery = await Task.Run(() => maintenance.PlanRestoreAsync(backup, restoreRoot)); preview.Text = recovery.Summary + "\n目标：" + recovery.Destination; Message("完整备份已校验；尚未恢复。检查目标后执行下方恢复。"); });
        Form.Children.Add(plan); Form.Children.Add(preview);
        var restore = new Button { Content = "按计划恢复到新目录…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        restore.Click += async (_, _) => await Run(async () =>
        {
            var selected = recovery ?? throw new InvalidOperationException("先检查恢复计划");
            await Task.Run(() => maintenance.RestoreAsync(backup, selected.Destination, selected.BackupSha256));
            restoredPreview = selected; restoredBackup = backup;
            data.Text = selected.DataRoot; await Read();
            Message("完整备份已恢复并重新读取新目录。原目录保留；重试启动时隔离恢复责任仍由业务守卫处理。"); recovery = null;
        }); Form.Children.Add(restore);
        steps.SelectedItem = steps.Items.OfType<TabItem>().FirstOrDefault(t => Equals(t.Header, selectedStep)) ?? steps.Items[0];
        Form = Body;
    }
}

sealed class StartupSetupWindow : StartupWorkflowWindow
{
    readonly BusinessLaunch launch;
    DeploymentMedia? media;
    WindowsDeploymentRequest? reviewed;
    DeploymentComponentReview[] components = [];
    DeploymentLocation? installed;
    public DeploymentLocation? Installed => installed;
    public bool Launched { get; private set; }
    TextBox account = null!, data = null!, configuration = null!, trust = null!, program = null!, station = null!, upgradeBackup = null!;
    CheckBox repairSeed = null!, newSite = null!;
    ComboBox siteMode = null!;
    string newStation = "", selectedProgram = "", selectedBackup = "";
    string? enteredAccount, enteredData, enteredConfiguration, enteredTrust;
    bool? enteredNewSite, enteredRepair;
    RunMode selectedMode = RunMode.Simulation;
    string[] packages = [];
    int step;
    readonly Button next, back;
    public StartupSetupWindow(BusinessLaunch launch) : base("安装与部署向导 · Importer PC")
    {
        this.launch = launch;
        back = Action("上一步", () => { CaptureInputs(); if (step > 0) step--; Render(); return Task.CompletedTask; });
        next = Action("检查安装媒介", Next);
        Action("关闭", () => { Busy = false; Close(); return Task.CompletedTask; });
        Action("保存已审阅的无人值守计划…", () =>
        {
            if (reviewed is null || step != 2) throw new InvalidOperationException("先完成参数/组件检查并进入摘要");
            var picker = new SaveFileDialog { FileName = "deployment-plan.json", Filter = "固定逐站部署计划|*.json" };
            if (picker.ShowDialog(this) == true) { using var output = new StreamWriter(new FileStream(picker.FileName, FileMode.CreateNew)); output.Write(Wire.Encode(DeploymentBatch.Freeze(reviewed))); Message("逐站计划已保存；不含业务数据库或请求。配置来源/原登记/包字节均固定，变化后须重新审阅。无人值守执行需要明确的Windows管理员部署会话。"); }
            return Task.CompletedTask;
        });
        Action("继续退役程序的中断清理…", async () =>
        {
            var cleanupMedia = await Task.Run(() => DeploymentMedia.Inspect(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")), ApplicationId));
            var registration = WindowsDeploymentSetup.Registration(ApplicationId);
            var result = await Task.Run(() => ParcelStartupServices.ResumeRemovalAsync(cleanupMedia, Wire.FileHash(registration)));
            Message(result.Status + "\n" + result.OperationFile + (result.RestartRequired ? "\n需重启Windows完成占用文件清理" : ""));
        });
        Render();
    }
    string ApplicationId => ParcelStartupServices.ApplicationId;
    void CaptureInputs()
    {
        if (step != 1) return;
        enteredAccount = account.Text; enteredData = data.Text; enteredConfiguration = configuration.Text; enteredTrust = trust.Text;
        selectedProgram = program.Text; selectedBackup = upgradeBackup.Text; enteredNewSite = newSite.IsChecked == true; enteredRepair = repairSeed.IsChecked == true;
    }
    void Render()
    {
        Body.Children.Clear(); back.IsEnabled = step is 1 or 2;
        Message("步骤 " + (step + 1) + " / 4 · 安装不启动设备或业务。日常运行保持未提权，安装和维护通过 Windows 授权。");
        if (step == 0)
        {
            Note("从可信分发渠道取得并完整解压 Windows 安装包。下一步校验程序目录的登记、来源及全部文件；散列完整性不代替发布者信任。");
            Note("源目录：" + Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")));
            Note("升级和修复使用并排版本；原程序、配置、数据和租约保留。失败可在此重试，旧部署登记保持可用。"); next.Content = "检查安装媒介";
        }
        else if (step == 1)
        {
            var current = WindowsDeploymentSetup.Current(ApplicationId);
            Note("Application：" + ApplicationId + "\n来源：" + media!.SourceCommit);
            var defaultAccount = ParcelStartupServices.DefaultRuntimeAccount();
            account = Field("运行账户（已有 Windows 用户，如 机器名\\用户名；普通启动即可使用 UAC 过滤账户）", enteredAccount ?? reviewed?.RuntimeAccountSid ?? defaultAccount, _ => { });
            data = Folder("数据根（升级时保留原数据位置）", enteredData ?? reviewed?.DataRoot ?? current?.DataRoot ?? launch.DataRoot);
            program = Folder("程序根（本地固定卷、维护所有、运行账户只读；与数据分离）", selectedProgram.Length > 0 ? selectedProgram : reviewed?.ProgramRoot ?? (current is null ? TrustedProgramRoots.Default : Directory.GetParent(Directory.GetParent(current.ReleaseRoot)!.FullName)!.FullName));
            newSite = new CheckBox { Content = "新工位：从本介质的通用模板收集首配（无数据库、请求或旧编号）", IsChecked = enteredNewSite ?? (current is null && reviewed is null), IsEnabled = current is null, MinHeight = 44 }; Body.Children.Add(newSite);
            station = Field("新工位标识（仅新工位填写；已部署身份只读）", newStation, v => newStation = v);
            Note("新工位模式：Simulation仅模拟；Production缺真实能力/合同/精确资格仍不能运行。模式和站标识写入后受原身份守卫约束。");
            siteMode = new ComboBox { ItemsSource = Enum.GetValues<RunMode>(), SelectedItem = selectedMode, MinHeight = 44 }; siteMode.SelectionChanged += (_, _) => { if (siteMode.SelectedItem is RunMode value) selectedMode = value; }; Body.Children.Add(siteMode);
            configuration = Folder("高级：已有部署 / 批准的配置来源（初装复制，升级保留原种子）", enteredConfiguration ?? reviewed?.ConfigurationSource ?? current?.ConfigurationDirectory ?? Path.Combine(media!.Root, "app", "config"));
            upgradeBackup = Folder("升级前完整备份目录（自动生成新的完整集合，不覆盖原备份）", selectedBackup.Length > 0 ? selectedBackup : Path.Combine(data.Text, "backups"));
            repairSeed = new CheckBox { Content = "重新部署所选种子配置到新目录（用于种子损坏修复；原种子保留）", IsChecked = enteredRepair ?? reviewed?.RepairSeed ?? false, MinHeight = 44 }; Body.Children.Add(repairSeed);
            trust = Field("可选：独立离线信任文件（无扩展时可留空）", enteredTrust ?? reviewed?.TrustFile ?? "", _ => { });
            var chooseTrust = new Button { Content = "选择信任文件…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
            chooseTrust.Click += (_, _) => { var selected = FilePick(this, "选择独立离线信任政策", "信任文件|*.json"); if (selected is not null) trust.Text = selected; }; Body.Children.Add(chooseTrust);
            Note("不创建测试或 Production 信任。已安装信任只允许相同字节；轮换须另行审查。需要真实 SDK 的部署仍需独立安装及资格证据。");
            var selectedPackages = new TextBlock { Text = string.Join("\n", packages), TextWrapping = TextWrapping.Wrap };
            var choosePackages = new Button { Content = "选择可选离线扩展包…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
            choosePackages.Click += (_, _) => { var picker = new OpenFileDialog { Title = "选择原始签名扩展包", Multiselect = true, Filter = "离线扩展包|*.zip;*.ipcpkg|所有文件|*.*" }; if (picker.ShowDialog(this) == true) { packages = picker.FileNames; selectedPackages.Text = string.Join("\n", packages); } };
            Body.Children.Add(choosePackages); Body.Children.Add(selectedPackages); next.Content = "检查部署并查看摘要";
        }
        else if (step == 2)
        {
            var r = reviewed!;
            Note("请核对部署摘要。下一步仅提升固定安装 helper；未提权客户端不会创建机级策略。");
            Note($"{r.ApplicationId}\n来源：{r.SourceCommit}\n运行账户 SID：{r.RuntimeAccountSid}\n数据：{r.DataRoot}\n种子来源：{r.ConfigurationSource}\n种子操作：{(r.RepairSeed ? "并排修复，原种子保留" : "初装复制，既有种子保留")}\n模式：{r.Mode}\n信任：{r.TrustFile ?? "未选择"}\n离线扩展：{r.Packages.Count}");
            Note("程序目标：" + r.ProgramRoot + "\\releases\\新的版本目录\n机级策略与部署登记：%ProgramData%\\ImporterPc\n升级备份：" + (r.BackupDestination ?? "新工位尚无原库") + "\n开始菜单和系统已安装应用提供同源维护入口。安装完成后仍须首配与开工检查。");
            foreach (var component in components) Note("组件 " + component.Package + " @ " + component.Version + " · " + component.ExpandedBytes + "字节\n能力 " + string.Join(", ", component.Capabilities) + "\n精确依赖 " + string.Join(", ", component.Dependencies) + "\n系统前置条件 " + (component.Prerequisites.Length == 0 ? "本包未声明额外厂商安装项" : string.Join("；", component.Prerequisites)) + "\n仅签名/闭包检查；尚未绑定、激活、连接或允许业务。");
            next.Content = "授权并安装";
        }
        else
        {
            Note("安装已读回确认。程序：" + installed!.ReleaseRoot + "\n配置：" + installed.ConfigurationDirectory + "\n数据：" + installed.DataRoot);
            Note("已创建开始菜单快捷方式。旧版本和原始数据保留；此结果不代表 Windows 现场验收或 Production 准入。"); next.Content = "正常启动已安装程序";
        }
    }
    async Task Next()
    {
        if (step == 0)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("安装向导需要 Windows");
            media = await Task.Run(() => DeploymentMedia.Inspect(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")), ApplicationId)); step = 1; Render();
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
            components = await Task.Run(() => DeploymentComponentReviewing.Inspect(reviewed));
            var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(reviewed.ConfigurationSource, "business.json")));
            if (WindowsDeploymentSetup.Current(ApplicationId) is not null || File.Exists(Path.Combine(reviewed.DataRoot, seed.Mode.ToString(), seed.StationId, ApplicationId, ApplicationId + ".db")))
            {
                Directory.CreateDirectory(selectedBackup);
                var destination = Path.Combine(Path.GetFullPath(selectedBackup), ApplicationId + "-pre-upgrade-" + Wire.Id() + ".db");
                var plan = await Task.Run(() => ParcelStartupServices.ReviewDeploymentAsync(reviewed, destination));
                if (!plan.Allowed) throw new InvalidOperationException("原责任阻止升级：" + plan.Summary);
                reviewed = reviewed with { ChangePlanIdentity = plan.Identity, BackupDestination = destination };
            }
            step = 2; Render();
        }
        else if (step == 2) { installed = await Task.Run(() => ParcelStartupServices.InstallAsync(reviewed!)); step = 3; Render(); }
        else
        {
            if (!ParcelStartupServices.IsRuntimeAccount(installed!)) { Message("安装完成。请登录指定运行账户，然后使用开始菜单运行入口。"); return; }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(installed!.ReleaseRoot, "desktop", "Ipc.Desktop.exe")) { UseShellExecute = true, WorkingDirectory = installed.ReleaseRoot });
            Launched = true;
            Busy = false; Close();
        }
    }
}
