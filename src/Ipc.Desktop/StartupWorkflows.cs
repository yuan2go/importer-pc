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
    protected readonly TextBlock Status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 12) };
    protected bool Busy;
    protected StartupWorkflowWindow(string title)
    {
        Title = title; Width = 960; Height = 760; MinWidth = 680; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(241, 244, 247)); Foreground = new SolidColorBrush(Color.FromRgb(25, 39, 54));
        var grid = new Grid { Margin = new(24) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel(); heading.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.Bold }); heading.Children.Add(Status);
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
    protected void Note(string text) => Body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 8) });
    protected TextBox Field(string label, string value, Action<string> changed, bool readOnly = false)
    {
        Note(label); var box = new TextBox { Text = value, MinHeight = 36, IsReadOnly = readOnly, MaxLength = 4096, Margin = new(0, 0, 0, 4) };
        box.TextChanged += (_, _) => changed(box.Text); Body.Children.Add(box); return box;
    }
    protected TextBox Folder(string label, string value)
    {
        var box = Field(label, value, _ => { });
        var browse = new Button { Content = "选择文件夹…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        browse.Click += (_, _) => { var picker = new OpenFolderDialog { Title = label }; if (picker.ShowDialog(this) == true) box.Text = picker.FolderName; };
        Body.Children.Add(browse); return box;
    }
    protected void Choice<T>(string label, T selected, Action<T> changed) where T : struct, Enum
    {
        Note(label); var box = new ComboBox { ItemsSource = Enum.GetValues<T>(), SelectedItem = selected, MinHeight = 36 };
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is T value) changed(value); }; Body.Children.Add(box);
    }
    protected static string? FilePick(Window owner, string title, string filter = "所有文件|*.*")
    { var picker = new OpenFileDialog { Title = title, Filter = filter }; return picker.ShowDialog(owner) == true ? picker.FileName : null; }
    protected void CompleteDialog() { Busy = false; DialogResult = true; }
}

sealed class StartupRepairWindow : StartupWorkflowWindow
{
    IStartupMaintenance maintenance = null!;
    StartupMaintenanceDocument? document;
    StartupConfigurationDraft? draft;
    StartupRecoveryPreview? recovery;
    readonly TextBox config, data;
    string reason = "", backup = "", restoreRoot = "";
    public BusinessLaunch SelectedLaunch { get; private set; }
    public bool RetryRequested { get; private set; }
    public bool ReplacementLaunched { get; private set; }
    public StartupRepairWindow(BusinessLaunch launch, string? failure = null) : base("启动修复 · 离线维护")
    {
        SelectedLaunch = launch;
        Message("无需业务启动成功。检查和编辑不连接设备；保存为 Pending，正常启动仍检查原恢复责任。" + (failure is null ? "" : "\n启动诊断：" + failure));
        config = Folder("原配置目录（包含 business.json）", launch.ConfigurationDirectory);
        data = Folder("原数据根（保留 Simulation/Production、站和 Application 层级）", launch.DataRoot);
        Action("读取所选配置", Read);
        Action("校验草稿", async () => { var receipt = await Task.Run(() => maintenance.ValidateAsync(RequireDraft())); Message(receipt.Status + " · " + receipt.Reason); });
        Action("保存待生效修订…", async () =>
        {
            var candidate = RequireDraft(); var expected = document!.ConfigurationHash;
            document = await Task.Run(() => maintenance.StageAsync(expected, candidate, reason)); Render(); Message("保存后已读回状态：" + document.ConfigurationStatus + "。保存过程不改 Effective 或原数据库；重试启动时由业务决定能否激活。");
        });
        Action("导出支持摘要…", async () =>
        {
            var picker = new SaveFileDialog { FileName = "startup-support.json", Filter = "支持摘要|*.json", OverwritePrompt = true };
            if (picker.ShowDialog(this) == true) { if (File.Exists(picker.FileName)) throw new IOException("请选择新文件，保留既有摘要"); await Task.Run(() => maintenance.ExportAsync(picker.FileName)); Message("已导出摘要；不含配置参数、业务报文或数据库。"); }
        });
        Action("安装 / 修复程序…", async () =>
        {
            var setup = new StartupSetupWindow(SelectedLaunch) { Owner = this }; setup.ShowDialog();
            if (setup.Launched) { ReplacementLaunched = true; Busy = false; Close(); return; }
            if (setup.Installed is { } installed) { config.Text = installed.ConfigurationDirectory; data.Text = installed.DataRoot; await Read(); }
        });
        Action("重试正常启动", () => { SelectedLaunch = SelectedLaunch with { ConfigurationDirectory = config.Text, DataRoot = data.Text }; RetryRequested = true; CompleteDialog(); return Task.CompletedTask; });
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
        document = await Task.Run(maintenance.ReadAsync); recovery = null; Render();
        Message(string.Join("\n", document.Diagnostics) + "\n" + document.Catalog.InventoryDiagnostic);
    }
    void Render()
    {
        while (Body.Children.Count > 6) Body.Children.RemoveAt(6); // Preserve path selectors and their browse controls.
        draft = document?.Candidate is { } candidate ? new(candidate, document.Catalog) : null;
        if (draft is not null)
        {
            var c = document!.Candidate!;
            Note($"{c.ApplicationId} · {c.StationId} · {c.Mode} · 修订 {document.Revision} · {document.ConfigurationStatus}\n原账散列：{document.ConfigurationHash}\n身份、拓扑和精确提供者固定；此处修改现有参数和基本策略。");
            Field("输入队列容量", draft.QueueCapacity, v => draft.QueueCapacity = v);
            Field("调用超时（ms）", draft.TimeoutMs, v => draft.TimeoutMs = v);
            Field("作业容量", draft.MaximumOperations, v => draft.MaximumOperations = v);
            Choice("重量来源", draft.Policy.WeightSource, v => draft.Policy.WeightSource = v);
            Choice("重量单位", draft.Policy.Unit, v => draft.Policy.Unit = v);
            Field("配置重量（仅 Configured 使用）", draft.Policy.ConfiguredWeight, v => draft.Policy.ConfiguredWeight = v);
            Field("重量精度", draft.Policy.Precision, v => draft.Policy.Precision = v);
            Field("测量最大年龄（ms）", draft.Policy.MaximumAgeMs, v => draft.Policy.MaximumAgeMs = v);
            Field("等待重量期限（ms）", draft.Policy.WaitTimeoutMs, v => draft.Policy.WaitTimeoutMs = v);
            Choice("重量有效期", draft.Policy.Validity, v => draft.Policy.Validity = v);
            foreach (var capability in draft.Capabilities)
            {
                Note(capability.Role + " · " + capability.InstanceId + "\n" + capability.ProviderIdentity);
                Field("目标", capability.Target, v => capability.Target = v, capability.IsTargetReadOnly || capability.Provider is null);
                foreach (var parameter in capability.Parameters)
                {
                    Note(parameter.Field.Label + " · " + parameter.EditorHelp);
                    if (parameter.Field.Sensitive)
                    {
                        var password = new PasswordBox { Password = parameter.Value, MinHeight = 36, IsEnabled = !parameter.Field.ReadOnly };
                        password.PasswordChanged += (_, _) => parameter.Value = password.Password; Body.Children.Add(password);
                    }
                    else if (parameter.Field.Choices is { Length: > 0 } choices)
                    {
                        var selected = new ComboBox { ItemsSource = choices, SelectedItem = parameter.Value, MinHeight = 36, IsEnabled = !parameter.Field.ReadOnly };
                        selected.SelectionChanged += (_, _) => parameter.Value = selected.SelectedItem as string ?? ""; Body.Children.Add(selected);
                    }
                    else Field(parameter.Field.Key, parameter.Value, v => parameter.Value = v, parameter.Field.ReadOnly);
                }
            }
            Field("维护原因（必填；保留审计与原配置字节）", reason, v => reason = v);
        }
        Note("完整备份恢复：验证原库和 .recovery 同伴目录，复制到新的隔离目录。恢复标记继续阻止自动业务恢复。");
        var backupBox = Field("完整备份数据库", backup, v => { backup = v; recovery = null; });
        var choose = new Button { Content = "选择完整备份…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        choose.Click += (_, _) => { var selected = FilePick(this, "选择完整备份数据库", "SQLite 备份|*.db;*.sqlite;*.backup|所有文件|*.*"); if (selected is not null) backupBox.Text = selected; }; Body.Children.Add(choose);
        var destination = Folder("新的隔离数据根（不覆盖原目录）", restoreRoot);
        destination.TextChanged += (_, _) => { restoreRoot = destination.Text; recovery = null; };
        var plan = new Button { Content = "检查恢复计划", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        var preview = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80 };
        plan.Click += async (_, _) => await Run(async () => { recovery = await Task.Run(() => maintenance.PlanRestoreAsync(backup, restoreRoot)); preview.Text = recovery.Summary + "\n目标：" + recovery.Destination; Message("完整备份已校验；尚未恢复。检查目标后执行下方恢复。"); });
        Body.Children.Add(plan); Body.Children.Add(preview);
        var restore = new Button { Content = "按计划恢复到新目录…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        restore.Click += async (_, _) => await Run(async () =>
        {
            var selected = recovery ?? throw new InvalidOperationException("先检查恢复计划");
            await Task.Run(() => maintenance.RestoreAsync(backup, selected.Destination, selected.BackupSha256));
            data.Text = selected.DataRoot; await Read();
            Message("完整备份已恢复并重新读取新目录。原目录保留；重试启动时隔离恢复责任仍由业务守卫处理。"); recovery = null;
        }); Body.Children.Add(restore);
    }
}

sealed class StartupSetupWindow : StartupWorkflowWindow
{
    readonly BusinessLaunch launch;
    DeploymentMedia? media;
    WindowsDeploymentRequest? reviewed;
    DeploymentLocation? installed;
    public DeploymentLocation? Installed => installed;
    public bool Launched { get; private set; }
    TextBox account = null!, data = null!, configuration = null!, trust = null!;
    CheckBox repairSeed = null!;
    string[] packages = [];
    int step;
    readonly Button next, back;
    public StartupSetupWindow(BusinessLaunch launch) : base("安装与部署向导 · Importer PC")
    {
        this.launch = launch;
        back = Action("上一步", () => { if (step > 0) step--; Render(); return Task.CompletedTask; });
        next = Action("检查安装媒介", Next);
        Action("关闭", () => { Busy = false; Close(); return Task.CompletedTask; });
        Render();
    }
    string ApplicationId => ParcelStartupServices.ApplicationId;
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
            account = Field("运行账户（已有 Windows 用户，如 机器名\\用户名；普通启动即可使用 UAC 过滤账户）", reviewed?.RuntimeAccountSid ?? defaultAccount, _ => { });
            data = Folder("数据根（升级时保留原数据位置）", reviewed?.DataRoot ?? current?.DataRoot ?? launch.DataRoot);
            configuration = Folder("配置来源（初装复制；升级保留原种子）", reviewed?.ConfigurationSource ?? current?.ConfigurationDirectory ?? launch.ConfigurationDirectory);
            repairSeed = new CheckBox { Content = "重新部署所选种子配置到新目录（用于种子损坏修复；原种子保留）", IsChecked = reviewed?.RepairSeed ?? false, MinHeight = 44 }; Body.Children.Add(repairSeed);
            trust = Field("可选：独立离线信任文件（无扩展时可留空）", reviewed?.TrustFile ?? "", _ => { });
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
            Note("程序目标：%ProgramFiles%\\ImporterPc\\releases\\新的版本目录\n机级策略与部署登记：%ProgramData%\\ImporterPc\n开始菜单会提供运行、启动修复和安装维护入口。完成后仍须由正常启动校验配置、身份、原责任及实际能力。");
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
            var selectedAccount = account.Text; var dataRoot = data.Text; var configPath = configuration.Text;
            var trustPath = trust.Text; var selected = packages.ToArray(); var repair = repairSeed.IsChecked == true;
            reviewed = await Task.Run(() => ParcelStartupServices.Review(media!, selectedAccount, dataRoot, configPath, trustPath, selected, repair));
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
