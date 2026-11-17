using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
using Microsoft.Win32;

namespace Ipc.Application.Sportmaster.Desktop;

// Presentation over the remote Application contract. No Assembly, runtime,
// driver, HTTP sender or business repository belongs to this component.
public sealed partial class OperationsWorkspaceControl : UserControl
{
    readonly SupplyStationViewModel model;
    readonly IWorkstationAdministration authority;
    readonly Func<Task> connect, offline;
    readonly Func<bool> connected;
    readonly ContentControl page = new();
    readonly MaintenancePage parameters = new() { Audience = SmParameterAudience.Technical };
    readonly TextBlock status = Text("查看、登录和切页不暂停供包。调试须明确进入。");
    readonly Expander authorizationPanel = new() { IsExpanded = true };
    readonly TextBox message = new() { MaxHeight = 72, Margin = new(0, 4, 0, 4) };
    readonly TextBox technicalFailure = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly Button enterDebug = new() { Content = "进入设备调试（暂停整站供包）", MinHeight = 44 };
    readonly Button leaveDebug = new() { Content = "结束调试并核对恢复", MinHeight = 44 };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    string window = Wire.Id();
    readonly DockPanel device = new();
    readonly ContentControl deviceView = new();
    readonly Button deviceViewToggle = new() { Content = "查看连接诊断", MinHeight = 44 };
    readonly List<Button> actionButtons = [];
    readonly ContentControl diagnostic = new(), debugHost = new();
    StationDebugControl? debug;
    SoftwareLogControl? logs;
    FrameworkElement? maintenance;
    HistoryPage? responsibilities;
    string? pendingBackup, latestBackupDirectory;
    Button? backupAction;
    readonly TextBox backupOutcome = new() { MaxHeight = 120 };
    CancellationTokenSource? diagnosticExport;
    string selected = "parameters";
    string? grant;
    bool polling, closing, workspace, acting;
    Task refresh = Task.CompletedTask;
    public event EventHandler<string>? NavigationRequested;

    public OperationsWorkspaceControl(IParcelApplication application, Func<Task> connect, Func<bool> connected, Func<Task> offline)
    {
        model = new(application) { ParameterAudience = SmParameterAudience.Technical, RequireOwnedManagementSession = true }; authority = (IWorkstationAdministration)application;
        this.connect = connect; this.connected = connected; this.offline = offline;
        DataContext = model; parameters.DataContext = model;
        model.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(model.IsManagementMode) && model.IsManagementMode) authorizationPanel.IsExpanded = false;
            if ((change.PropertyName is nameof(model.IsManagementMode) or nameof(model.Fresh)) && (!model.IsManagementMode || !model.Fresh)) Lock();
        };
        Resources.MergedDictionaries.Add(new() { Source = new Uri("/Ipc.Application.ParcelFlow.Desktop;component/ParameterSettingsTheme.xaml", UriKind.Relative) });
        var root = new DockPanel(); var top = new StackPanel { Margin = new(12, 4, 12, 4) };
        var header = new ScrollViewer { Content = top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 160 };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        SizeChanged += (_, _) => header.MaxHeight = ActualHeight < 500 ? 92 : 160;
        var actions = new WrapPanel(); var authorization = new DockPanel();
        var login = Action("连接并验证管理密码…", Authenticate); DockPanel.SetDock(login, Dock.Left); authorization.Children.Add(login);
        var authorizationDetails = new Expander { Header = "授权与诊断", Content = actions, Margin = new(8, 0, 0, 0) };
        authorization.Children.Add(authorizationDetails);
        actions.Children.Add(Action("延长本窗授权", async () => { await authority.RenewManagementModeAsync(); await model.RefreshManagementModeAsync(); }));
        actions.Children.Add(Action("管理密码…", async () =>
        {
            var dialog = new ManagementModeWindow(authority, bindSession: true) { Owner = Window.GetWindow(this) };
            dialog.ShowDialog();
            if (dialog.EnteredSessionId is { } entered) { grant = entered; model.OwnedManagementSession = entered; }
            await model.RefreshManagementModeAsync();
        }));
        authorizationPanel.Header = status; authorizationPanel.Content = authorization;
        top.Children.Add(authorizationPanel); top.Children.Add(message);
        System.Windows.Automation.AutomationProperties.SetAutomationId(technicalFailure, "Operations.Failure");
        actions.Children.Add(technicalFailure);
        var noticeStyle = new Style(typeof(TextBox), (Style)FindResource("OperatorCopyableNotice"));
        var emptyNotice = new Trigger { Property = TextBox.TextProperty, Value = "" };
        emptyNotice.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed)); noticeStyle.Triggers.Add(emptyNotice); message.Style = backupOutcome.Style = noticeStyle;
        message.SetBinding(TextBox.TextProperty, new Binding(nameof(model.Message)) { Source = model, Mode = BindingMode.OneWay });
        root.Children.Add(page); Content = root;
        var debugActions = new WrapPanel(); debugActions.Children.Add(enterDebug); debugActions.Children.Add(leaveDebug); DockPanel.SetDock(debugActions, Dock.Top); device.Children.Add(debugActions);
        debugActions.Children.Add(deviceViewToggle);
        deviceViewToggle.Click += (_, _) => { if (ReferenceEquals(deviceView.Content, diagnostic) && workspace) ShowDebugView(); else ShowDiagnosticsView(); };
        debugHost.Content = Text("尚未进入调试。请先明确进入，并核对整站影响范围。");
        deviceView.Content = diagnostic; device.Children.Add(deviceView);
        enterDebug.Click += async (_, _) => await Run(BeginDebug);
        leaveDebug.Click += async (_, _) => await Run(EndDebug);
        parameters.DiagnosticsRequested += (_, _) => Navigate("debug");
        parameters.CompleteBackupAsync = Backup; parameters.ManageBackupSetsAsync = BackupSets;
        model.RequestRetentionAsync = offline;
        timer.Tick += (_, _) => { if (!polling && !closing) refresh = Refresh(); };
        timer.Start(); ShowPage(selected);
    }

    static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0,4,0,4) };
    void Navigate(string target) { if (NavigationRequested is { } navigate) navigate(this, target); else ShowPage(target); }
    Button Action(string label, Func<Task> action)
    {
        var button = new Button { Content = label, MinHeight = 44, Margin = new(0,0,8,4) };
        actionButtons.Add(button);
        button.Click += async (_, _) => await Run(action); return button;
    }
    async Task Run(Func<Task> action)
    {
        if (acting || closing) { model.Message = "原操作尚未收口，请等待原结果。"; return; }
        acting = true;
        foreach (var button in actionButtons) button.IsEnabled = false;
        enterDebug.IsEnabled = leaveDebug.IsEnabled = false;
        try { await action(); }
        catch (Exception error)
        {
            technicalFailure.Text = DiagnosticText.Protect(error.ToString());
            System.Diagnostics.Trace.TraceError("Operations UI action: {0}", technicalFailure.Text);
            model.Message = ChineseInterfaceText.ParameterMessage(error.Message, "操作未完成，请核对原结果；输入和原责任保留。");
        }
        finally
        {
            acting = false;
            foreach (var button in actionButtons) button.IsEnabled = !closing;
            enterDebug.IsEnabled = !closing && !workspace && model.Fresh && model.IsManagementMode;
            leaveDebug.IsEnabled = !closing && workspace;
        }
    }
    async Task Authenticate()
    {
        closing = false; timer.Start();
        await connect();
        var password = new ManagementModeWindow(authority, "供包运维", bindSession: true) { Owner = Window.GetWindow(this) };
        password.ShowDialog();
        if (password.EnteredSessionId is { } entered) { grant = entered; model.OwnedManagementSession = entered; }
        if (!password.Entered) return;
        await model.RefreshManagementModeAsync();
        if (model.ManagementStatus.SessionId != grant || !model.IsManagementMode) throw new UnauthorizedAccessException("本窗授权尚未确认。");
        await model.RefreshDisplayAsync();
        await model.EnsureMaintenanceLoadedAsync();
        await Refresh();
        ShowPage(selected);
    }
    async Task Refresh()
    {
        polling = true;
        try
        {
            if (!connected()) { status.Text = "控制连接不可用。原命令结果须核对；未连接不代表已停机。"; Lock(); return; }
            await model.RefreshManagementModeAsync();
            if (!model.IsManagementMode || model.ManagementStatus.SessionId != grant)
            {
                status.Text = "本窗授权已结束或未确认，编辑输入保留。"; Lock();
                return;
            }
            await model.RefreshDisplayAsync();
            var actualOwner = await ((IManagementWorkspace)model.Application).ReadManagementWorkspaceOwnerAsync(CancellationToken.None);
            if (workspace && actualOwner != window)
            {
                workspace = false; debug?.Revoke(); debug?.ObserveReleasedWorkspace();
                debugHost.Content = Text("本会话的调试资源已由工作站核对释放。实际生产资格以当前工作站状态为准。");
            }
            else if (!workspace && actualOwner == window) { workspace = true; ShowDebugView(); }
            status.Text = workspace ? "调试中：整站供包及人工提交暂停" :
                "已授权 · 浏览与编辑不暂停 · 授权至 " + model.ManagementStatus.ExpiresAt?.ToLocalTime().ToString("HH:mm");
            parameters.IsEnabled = model.Fresh;
            if (logs is not null) logs.IsEnabled = model.Fresh;
            diagnostic.IsEnabled = model.Fresh;
            if (diagnostic.Content is OperationsDeviceDiagnostics devices) devices.DebugActive = workspace;
            enterDebug.IsEnabled = !acting && !workspace && model.Fresh;
            leaveDebug.IsEnabled = !acting && workspace;
        }
        catch (Exception error) { status.Text = "状态读取未确认：" + DiagnosticText.Protect(error.Message); Lock(); }
        finally { polling = false; }
    }
    void Lock()
    {
        parameters.IsEnabled = false; diagnostic.IsEnabled = false;
        enterDebug.IsEnabled = false;
        logs?.Revoke(); debug?.Revoke();
        if (logs is not null) logs.IsEnabled = false;
    }
    public void ShowPage(string target)
    {
        selected = target;
        page.Content = target switch
        {
            "parameters" => parameters,
            "debug" => device,
            "logs" => logs ??= CreateLogs(),
            "maintenance" => maintenance ??= Maintenance(),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        if (target == "debug" && diagnostic.Content is null)
            diagnostic.Content = new OperationsDeviceDiagnostics(model, async (reader, instance) =>
            {
                Navigate("parameters"); await model.EnsureMaintenanceLoadedAsync();
                var target = reader is null ? instance ?? model.CapabilityDrafts.FirstOrDefault(ParameterOwnership.IsRcs)?.InstanceId :
                    model.ReaderDrafts.SingleOrDefault(r => r.Original.DeviceId == reader)?.Capability.InstanceId;
                if (target is not null) parameters.NavigateToParameter(target + (reader is null ? "/heartbeat-interval-ms" : "/reader.engine"));
            },
                position => { if (workspace) { debug?.SelectPosition(position); ShowDebugView(); } else model.Message = "请先明确进入设备调试。"; },
                path => parameters.NavigateToParameter(path));
        parameters.IsEnabled = model.IsManagementMode && model.Fresh;
        diagnostic.IsEnabled = model.IsManagementMode && model.Fresh;
        if (diagnostic.Content is OperationsDeviceDiagnostics devices) devices.DebugActive = workspace;
        if (target == "logs" && model.IsManagementMode && model.Fresh) logs?.ResumeAfterCancelledClose();
        enterDebug.IsEnabled = !acting && model.IsManagementMode && model.Fresh && !workspace;
        leaveDebug.IsEnabled = !acting && workspace;
    }
    async Task BeginDebug()
    {
        await model.RequireManagementModeAsync();
        if (MessageBox.Show(Window.GetWindow(this), "进入设备调试将暂停整站自动供包和人工提交，保留相机预览及原请求。结束后按原状态及实际资源核对恢复。", "进入设备调试",
            MessageBoxButton.OKCancel, MessageBoxImage.Information, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        window = Wire.Id();
        var receipt = await ((IManagementWorkspace)model.Application).SetManagementWorkspaceAsync(window, true, CancellationToken.None);
        if (receipt.Status is not ("Paused" or "PausedWithCameraWarning" or "Opened")) throw new InvalidOperationException(receipt.Reason);
        workspace = true;
        ShowDebugView(); await Refresh();
    }
    void ShowDebugView()
    {
        debug ??= new StationDebugControl(model, _ => { Navigate("parameters"); return Task.CompletedTask; }, ShowOperation);
        debugHost.Content = debug; deviceView.Content = debugHost; deviceViewToggle.Content = "查看连接诊断";
    }
    void ShowDiagnosticsView()
    {
        deviceView.Content = diagnostic;
        deviceViewToggle.Content = workspace ? "返回调试画面" : "连接诊断";
    }
    async Task EndDebug()
    {
        if (debug is not null) await debug.QuiesceAsync();
        var receipt = await ((IManagementWorkspace)model.Application).SetManagementWorkspaceAsync(window, false, CancellationToken.None);
        if (receipt.Status != "Restored") throw new InvalidOperationException(receipt.Reason);
        workspace = false; debug?.ConfirmWorkspaceClosed(receipt); debugHost.Content = Text("调试已结束。原请求仍按原身份收口。"); ShowDiagnosticsView();
        await Refresh();
    }
    FrameworkElement Maintenance()
    {
        var body = new StackPanel { Margin = new(20) };
        body.Children.Add(Text("在线操作由当前工作站执行。离线维护使用同版受保护维护程序；将先关闭本工具释放程序文件。断线不证明工作站已停止。"));
        backupAction = Action(pendingBackup is null ? "创建完整备份…" : "核对原次完整备份", Backup);
        System.Windows.Automation.AutomationProperties.SetAutomationId(backupAction, "CompleteBackup.Operations"); body.Children.Add(backupAction);
        body.Children.Add(backupOutcome);
        body.Children.Add(Action("完整备份核验、交付与保管…", BackupSets));
        body.Children.Add(Action("检查并恢复供包", async () => { await model.RequireManagementModeAsync(); await model.CommandAsync("Resume"); await model.RefreshAsync(); }));
        var cancelExport = new Button { Content = "取消导出", MinHeight = 44, Visibility = Visibility.Collapsed };
        cancelExport.Click += (_, _) => { diagnosticExport?.Cancel(); cancelExport.IsEnabled = false; model.Message = "已请求取消诊断导出，正在等待原服务收口。"; };
        body.Children.Add(Action("导出诊断包…", async () =>
        {
            await model.RequireManagementModeAsync();
            var picker = new SaveFileDialog { Filter = "诊断包|*.zip", FileName = "station-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip" };
            if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
            using var cancellation = new CancellationTokenSource(); diagnosticExport = cancellation;
            cancelExport.IsEnabled = true; cancelExport.Visibility = Visibility.Visible;
            model.Message = "正在导出诊断包，业务状态保持原状。";
            try
            {
                var result = await ((IDiagnosticPackageApplication)model.Application).ExportDiagnosticPackageReportAsync(picker.FileName, cancellation.Token);
                model.Message = result.Status + " · " + result.Path + "\n" + string.Join("\n", result.Notices);
            }
            finally { diagnosticExport = null; cancelExport.Visibility = Visibility.Collapsed; }
        }));
        body.Children.Add(cancelExport);
        body.Children.Add(Action("完整备份保管、离线配置、恢复、回滚、清理与卸载…", offline));
        body.Children.Add(Action("核对左位未确认责任…", () => ConfirmRemotePositionRecovery("left")));
        body.Children.Add(Action("核对右位未确认责任…", () => ConfirmRemotePositionRecovery("right")));
        body.Children.Add(Action("恢复备份后核对…", () => { parameters.ShowTechnicalTool(MaintenanceTool.RestoreHandoff); page.Content = parameters; return Task.CompletedTask; }));
        body.Children.Add(Action("核对异常作业与未完成输入…", async () =>
        {
            await model.RequireManagementModeAsync(); await model.EnsureMaintenanceLoadedAsync();
            parameters.ShowTechnicalTool(MaintenanceTool.Recovery); page.Content = parameters;
        }));
        body.Children.Add(Action("核对原业务记录与请求责任…", async () =>
        {
            await model.RequireManagementModeAsync();
            if (responsibilities is null)
            {
                responsibilities = new HistoryPage { DataContext = model };
                responsibilities.LogsRequested += async (_, id) =>
                {
                    Navigate("logs"); logs ??= CreateLogs(); await logs.ShowOperationAsync(id);
                };
            }
            await model.OpenHistoryAsync(); page.Content = responsibilities;
        }));
        var original = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 260 };
        original.SetBinding(TextBox.TextProperty, new Binding(nameof(model.BackupStatusText)) { Source = model, Mode = BindingMode.OneWay }); body.Children.Add(original);
        return new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    async Task Backup()
    {
        await model.RequireManagementModeAsync();
        if (pendingBackup is not null) { await InspectPendingBackup(); return; }
        var picker = new SaveFileDialog { Filter = "完整备份数据库|*.db", FileName = "station-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db" };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        if (MessageBox.Show(Window.GetWindow(this), "创建完整备份前将暂停新供包。完成后根据原责任明确恢复。", "创建完整备份", MessageBoxButton.OKCancel, MessageBoxImage.Information, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        await model.PauseAsync("运维创建完整备份前暂停"); await model.RefreshAsync();
        if (model.Snapshot?.Paused != true || !model.Fresh) throw new InvalidOperationException("暂停尚未确认，未创建备份。");
        pendingBackup = picker.FileName; latestBackupDirectory = Path.GetDirectoryName(pendingBackup);
        backupAction!.Content = "核对原次完整备份";
        backupOutcome.Text = "原目标：" + pendingBackup + "\n正在等待原工作站执行完整备份；完成前保持暂停。";
        try
        {
            var receipt = await model.Application.BackupAsync(pendingBackup);
            model.Message = receipt.Status + " · " + receipt.Reason;
            backupOutcome.Text = "原目标：" + pendingBackup + "\n" + model.Message;
            if (receipt.Status is "BackupCreated" or "Rejected") { pendingBackup = null; backupAction.Content = "创建完整备份…"; }
        }
        catch (MaintenanceNotStartedException error)
        {
            backupOutcome.Text = "原目标：" + pendingBackup + "\n原命令尚未提交：" + DiagnosticText.Protect(error.Message);
            pendingBackup = null; backupAction.Content = "创建完整备份…"; throw;
        }
        catch
        {
            backupOutcome.Text = "原目标：" + pendingBackup + "\n原次结果未确认。请核对原次完整备份，不重复创建；暂停状态按工作站真实状态处理。";
            throw;
        }
    }
    async Task InspectPendingBackup()
    {
        var path = pendingBackup!;
        var sets = await ((ICompleteBackupSetMaintenance)model.Application).ListBackupsAsync(Path.GetDirectoryName(path)!, CancellationToken.None);
        var original = sets.SingleOrDefault(item => Path.GetFullPath(item.Path).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        backupOutcome.Text = "原目标：" + path + "\n" + (original is null ? "当前未取得完整备份证据；不能推定原操作尚未执行。" : original.StatusText + " · " + original.Summary);
        if (original is { Status: "Verified", Identity: not null })
        {
            backupOutcome.Text += "\n完整集合身份：" + original.Identity;
            // A complete set currently at the path is useful evidence, but a
            // path alone cannot identify a lost original operation receipt.
            backupOutcome.Text += "\n当前集合完整不等于原命令结果已确认；原次未知保留。";
            model.Message = "当前目标的完整集合已核验，可在备份保管中按该身份处理。原命令结果仍未确认，本次未重新创建；供包按实际状态明确恢复。";
        }
        else model.Message = "原目标尚未确认为完整。请在完整备份保管中核对原集合；本次没有重新创建。";
    }
    async Task BackupSets()
    {
        await model.RequireManagementModeAsync();
        var directory = latestBackupDirectory ?? model.Snapshot?.Configuration.Effective.BackupSchedule?.Directory ?? "D:\\";
        new Ipc.Desktop.BackupSetsWindow((ICompleteBackupSetMaintenance)model.Application, directory) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
    SoftwareLogControl CreateLogs()
    {
        var control = new SoftwareLogControl(authority);
        control.OperationRequested += async (_, id) => await Run(() => ShowOperation(id));
        return control;
    }
    async Task ShowOperation(string id)
    {
        await model.RequireManagementModeAsync();
        model.Selected = await model.Application.OperationAsync(id);
        if (model.Selected is null) throw new InvalidOperationException("原作业详情当前不可用，请沿原任务编号在记录中核对。");
        var dialog = new Window { Title = "原业务记录 · " + id, Owner = Window.GetWindow(this), Width = 1000, Height = 700,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.SetResourceReference(BackgroundProperty, "Surface");
        UiTypography.Apply(dialog);
        var body = new DockPanel(); var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(16) };
        var close = new Button { Content = "关闭", MinHeight = 44, IsCancel = true }; close.Click += (_, _) => dialog.Close(); footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        body.Children.Add(new TextBox { Text = model.Details, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(16), BorderThickness = new(0) });
        dialog.Content = body; dialog.ShowDialog();
    }
    public async Task CloseAsync(bool activeHandoff = false)
    {
        if (closing) return;
        if (acting && !activeHandoff) throw new InvalidOperationException("运维操作仍在执行，请先等待原结果或结束本轮调试。");
        ConfigurationCloseGuard.CommitAndFindInvalid(parameters);
        if (ConfigurationCloseGuard.HasUnsaved(model, parameters) && !ConfigurationCloseGuard.ConfirmExit(Window.GetWindow(this), "专业设置尚未保存。关闭将放弃本工具中的草稿，继续关闭？"))
            throw new OperationCanceledException("已保留专业设置草稿。");
        closing = true; timer.Stop();
        try
        {
            await refresh;
            if (workspace)
            {
                if (connected()) await EndDebug();
                else
                {
                    if (debug is not null) await debug.QuiesceAsync();
                    // The workstation owns the SDK and lease cleanup. Closing
                    // this disconnected view must not fabricate a release receipt
                    // or prevent the operator releasing the tool's program files.
                    System.Diagnostics.Trace.TraceWarning("Operations view closed after connection loss; original debug scope {0} remains owned/reconciled by workstation.", window);
                }
            }
            if (logs is not null) await logs.SuspendAndDrainAsync();
            await model.DrainAsync();
            if (grant is not null && connected()) await authority.ExitManagementModeAsync(grant);
        }
        catch { ResumeAfterFailedHandoff(); throw; }
    }
    public void ResumeAfterFailedHandoff()
    {
        closing = false;
        logs?.ResumeAfterCancelledClose(); timer.Start();
        if (!acting) foreach (var button in actionButtons) button.IsEnabled = true;
    }
}
