using System.Windows;
using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    readonly Border authorizationGuide;
    readonly TextBlock guideHeading = Text("");
    readonly MaintenancePage parameters = new() { Audience = SmParameterAudience.Technical };
    readonly TextBlock status = new() { FontSize = UiTypography.Label };
    readonly Border statusPill = new() { CornerRadius = new(12), Padding = new(9, 4, 9, 4) };
    readonly TextBlock authorizationNotice = Text("");
    readonly TextBox message = new() { MaxHeight = 72, Margin = new(0, 4, 0, 4) };
    readonly TextBox technicalFailure = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly Button enterDebug = new() { Content = "进入设备调试（暂停整站供包）", MinHeight = 44 };
    readonly Button leaveDebug = new() { Content = "结束调试并核对恢复", MinHeight = 44, Visibility = Visibility.Collapsed };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    string window = Wire.Id();
    readonly DockPanel device = new();
    readonly Button evidenceViewToggle = new() { Content = "历史证据", Visibility = Visibility.Collapsed };
    readonly TextBlock deviceHeading = Text("浏览连接诊断");
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
    public FrameworkElement AuthorizationStatus => statusPill;
    public Button AuthorizationAction { get; }
    public FrameworkElement AuthorizationDetails { get; }
    public FrameworkElement Feedback => message;

    public OperationsWorkspaceControl(IParcelApplication application, Func<Task> connect, Func<bool> connected, Func<Task> offline)
    {
        model = new(application) { ParameterAudience = SmParameterAudience.Technical, RequireOwnedManagementSession = true }; authority = (IWorkstationAdministration)application;
        this.connect = connect; this.connected = connected; this.offline = offline;
        DataContext = model; parameters.DataContext = model;
        model.PropertyChanged += (_, change) =>
        {
            if ((change.PropertyName is nameof(model.IsManagementMode) or nameof(model.Fresh)) && (!model.IsManagementMode || !model.Fresh)) Lock();
        };
        Resources.MergedDictionaries.Add(new() { Source = new Uri("/Ipc.Application.ParcelFlow.Desktop;component/ParameterSettingsTheme.xaml", UriKind.Relative) });
        Resources.MergedDictionaries.Add(new() { Source = new Uri("/Ipc.Application.Sportmaster.Desktop;component/OperationsWorkspaceTheme.xaml", UriKind.Relative) });
        statusPill.Child = status;
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        SetAuthorizationStatus("只读 · 未授权", "查看、登录和切页不暂停供包。调试须明确进入。");
        AuthorizationAction = Action("连接并验证管理密码…", Authenticate);
        AuthorizationAction.Margin = new(0);
        AuthorizationAction.SetResourceReference(StyleProperty, "Primary");
        var authorizePage = Action("验证管理密码", Authenticate);
        authorizePage.HorizontalAlignment = HorizontalAlignment.Left;
        authorizePage.Margin = new(0, 10, 0, 0);
        authorizePage.SetResourceReference(StyleProperty, "Primary");
        AutomationProperties.SetAutomationId(authorizePage, "Operations.AuthorizePage");
        guideHeading.FontSize = UiTypography.Position;
        guideHeading.FontWeight = UiTypography.Emphasis;
        var guide = new StackPanel();
        guide.Children.Add(guideHeading);
        guide.Children.Add(Text("使用本站管理密码授权本窗口。验证和浏览保留工作站当前的供包状态。"));
        guide.Children.Add(authorizePage);
        authorizationGuide = new Border { Child = guide, Padding = new(24), Margin = new(0, 20, 0, 0), MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        authorizationGuide.SetResourceReference(StyleProperty, "WorkstationCard");
        AutomationProperties.SetAutomationId(AuthorizationAction, "Operations.Authorize");

        var details = new StackPanel();
        details.Children.Add(authorizationNotice);
        var actions = new WrapPanel();
        details.Children.Add(actions);
        actions.Children.Add(Action("延长本窗授权", async () => { await authority.RenewManagementModeAsync(); await model.RefreshManagementModeAsync(); }));
        actions.Children.Add(Action("管理密码…", async () =>
        {
            var dialog = new ManagementModeWindow(authority, bindSession: true) { Owner = Window.GetWindow(this) };
            dialog.ShowDialog();
            if (dialog.EnteredSessionId is { } entered) { grant = entered; model.OwnedManagementSession = entered; }
            await model.RefreshManagementModeAsync();
        }));
        AutomationProperties.SetAutomationId(technicalFailure, "Operations.Failure");
        details.Children.Add(technicalFailure);
        AuthorizationDetails = details;

        var noticeStyle = new Style(typeof(TextBox), (Style)FindResource("OperatorCopyableNotice"));
        var emptyNotice = new Trigger { Property = TextBox.TextProperty, Value = "" };
        emptyNotice.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        noticeStyle.Triggers.Add(emptyNotice);
        message.Style = backupOutcome.Style = technicalFailure.Style = noticeStyle;
        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
        message.SetBinding(TextBox.TextProperty, new Binding(nameof(model.Message)) { Source = model, Mode = BindingMode.OneWay });
        Content = page;
        var debugActions = new WrapPanel();
        debugActions.Children.Add(enterDebug);
        debugActions.Children.Add(leaveDebug);
        debugActions.Children.Add(deviceViewToggle);
        debugActions.Children.Add(evidenceViewToggle);
        evidenceViewToggle.Click += (_, _) => evidenceViewToggle.Content = debug?.ToggleEvidence() == true ? "返回扫码调试" : "历史证据";
        enterDebug.SetResourceReference(StyleProperty, "OperationsDebugAction");
        var debugBar = new DockPanel();
        DockPanel.SetDock(debugActions, Dock.Right);
        debugBar.Children.Add(debugActions);
        deviceHeading.FontSize = UiTypography.Group;
        deviceHeading.FontWeight = UiTypography.Emphasis;
        deviceHeading.VerticalAlignment = VerticalAlignment.Center;
        debugBar.Children.Add(deviceHeading);
        var debugToolbar = new Border { Child = debugBar, Padding = new(14, 2, 6, 2) };
        debugToolbar.SetResourceReference(StyleProperty, "WorkstationCard");
        DockPanel.SetDock(debugToolbar, Dock.Top);
        device.Children.Add(debugToolbar);
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
    void SetAuthorizationStatus(string label, string detail, string tone = "Muted")
    {
        status.Text = label;
        status.ToolTip = authorizationNotice.Text = detail;
        status.SetResourceReference(TextBlock.ForegroundProperty, tone);
        statusPill.SetResourceReference(Border.BackgroundProperty, tone == "Muted" ? "Soft" : tone + "Background");
    }
    void Navigate(string target) { if (NavigationRequested is { } navigate) navigate(this, target); else ShowPage(target); }
    Button Action(string label, Func<Task> action)
    {
        var button = new Button { Content = label, MinHeight = 44, Margin = new(0,0,8,4) };
        actionButtons.Add(button);
        button.Click += async (_, _) => await Run(action); return button;
    }
    async Task Run(Func<Task> action, Func<Exception, string>? failureMessage = null)
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
            model.Message = failureMessage?.Invoke(error) ?? ChineseInterfaceText.ParameterMessage(error.Message, "操作未完成，请核对原结果；输入和原责任保留。");
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
        model.Message = "";
        ShowPage(selected);
    }
    async Task Refresh()
    {
        polling = true;
        try
        {
            if (!connected())
            {
                if (grant is null)
                    SetAuthorizationStatus("只读 · 未授权", "运维控制需要验证管理密码。查看、登录和切页不暂停供包。");
                else
                    SetAuthorizationStatus("控制未连接", "控制连接不可用。原命令结果须核对；未连接不代表已停机。", "Warning");
                Lock(); return;
            }
            await model.RefreshManagementModeAsync();
            if (!model.IsManagementMode || model.ManagementStatus.SessionId != grant)
            {
                SetAuthorizationStatus("授权未确认", "本窗授权已结束或未确认。请验证管理密码，编辑输入保留。", "Warning");
                Lock();
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
            if (workspace)
                SetAuthorizationStatus("调试中 · 整站暂停", "设备调试已暂停整站供包及人工提交。按原调试会话结束并核对恢复。", "Warning");
            else
            {
                var expiry = model.ManagementStatus.ExpiresAt?.ToLocalTime().ToString("HH:mm");
                SetAuthorizationStatus(expiry is null ? "已授权" : "已授权 · " + expiry + " 到期", "浏览与编辑不暂停供包。", "Success");
            }
            if (ReferenceEquals(page.Content, authorizationGuide)) ShowPage(selected);
            parameters.IsEnabled = model.Fresh;
            if (logs is not null) logs.IsEnabled = model.Fresh;
            diagnostic.IsEnabled = model.Fresh;
            if (diagnostic.Content is OperationsDeviceDiagnostics devices) devices.DebugActive = workspace;
            enterDebug.IsEnabled = !acting && !workspace && model.Fresh;
            leaveDebug.IsEnabled = !acting && workspace;
        }
        catch (Exception error)
        {
            SetAuthorizationStatus("授权状态待核对", "状态读取未确认：" + DiagnosticText.Protect(error.Message), "Warning");
            Lock();
        }
        finally { polling = false; }
    }
    void Lock()
    {
        parameters.IsEnabled = false; diagnostic.IsEnabled = false;
        enterDebug.IsEnabled = false;
        logs?.Revoke(); debug?.Revoke();
        if (logs is not null) logs.IsEnabled = false;
    }
    void ShowAuthorizationGuide()
    {
        guideHeading.Text = selected == "debug" ? "设备调试需管理授权" : "专业参数需管理授权";
        page.Content = authorizationGuide;
    }
    public void ShowPage(string target)
    {
        selected = target;
        if (target is ("debug" or "parameters") && !workspace && (!model.IsManagementMode || !connected()))
        {
            ShowAuthorizationGuide();
            return;
        }
        page.Content = target switch
        {
            "parameters" => parameters,
            "debug" => device,
            "logs" => logs ??= CreateLogs(),
            "maintenance" => maintenance ??= Maintenance(),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        if (target == "debug" && diagnostic.Content is null)
            diagnostic.Content = new OperationsDeviceDiagnostics(model, OpenDiagnosticSettings,
                position => { if (workspace) { debug?.SelectPosition(position); ShowDebugView(); } else model.Message = "请先明确进入设备调试。"; });
        parameters.IsEnabled = model.IsManagementMode && model.Fresh;
        diagnostic.IsEnabled = model.IsManagementMode && model.Fresh;
        if (diagnostic.Content is OperationsDeviceDiagnostics devices) devices.DebugActive = workspace;
        if (target == "logs" && model.IsManagementMode && model.Fresh) logs?.ResumeAfterCancelledClose();
        enterDebug.IsEnabled = !acting && model.IsManagementMode && model.Fresh && !workspace;
        leaveDebug.IsEnabled = !acting && workspace;
    }
    Task OpenDiagnosticSettings(string? reader, string field) => Run(async () =>
    {
        var originalGrant = grant;
        await model.EnsureMaintenanceLoadedAsync();
        if (closing || !diagnostic.IsVisible) return;
        if (!connected() || !model.Fresh) throw new IOException("工作站连接或状态读取已中断，请检查连接后重试。");
        if (!model.IsManagementMode || model.ManagementStatus.SessionId != originalGrant)
            throw new UnauthorizedAccessException("本窗管理授权已变化。");

        var path = field;
        if (field != "HistoryDays")
        {
            var instance = reader is null
                ? model.Snapshot?.Configuration.Effective.Wcs?.InstanceId ?? model.CapabilityDrafts.FirstOrDefault(ParameterOwnership.IsRcs)?.InstanceId
                : model.ReaderDrafts.SingleOrDefault(r => r.Original.DeviceId == reader)?.Capability.InstanceId;
            path = instance + "/" + field;
        }
        try
        {
            Navigate("parameters");
            parameters.UpdateLayout();
            if (!parameters.NavigateToParameter(path))
                throw new InvalidOperationException("当前配置没有对应的专业参数，请核对设备配置。");
        }
        catch
        {
            Navigate("debug");
            throw;
        }
        model.Message = field == "HistoryDays" ? "已打开数据与记录设置。" :
            "已打开专业参数。相机 IP、相机编号和 RCS 地址请在供包工作站的“设置”修改。";
    }, error =>
    {
        if (!connected() || !model.ManagementStatus.Available)
        {
            return "专业参数读取中断。请检查工作站连接，恢复后验证管理密码并重试；已填写的内容保留。";
        }
        if (error is UnauthorizedAccessException)
        {
            return "本窗管理授权已失效。请重新验证管理密码后打开专业参数；已填写的内容保留。";
        }
        var reason = ChineseInterfaceText.ParameterMessage(error.Message, "请打开顶部的环境信息查看诊断详情。");
        return "专业参数未打开。" + reason + " 核对后可重试，已填写的内容保留。";
    });

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
        debugHost.Content = debug;
        deviceView.Content = debugHost;
        deviceViewToggle.Content = "查看连接诊断";
        evidenceViewToggle.Visibility = Visibility.Visible;
        deviceHeading.Text = "扫码调试";
        enterDebug.Visibility = Visibility.Collapsed;
        leaveDebug.Visibility = Visibility.Visible;
    }
    void ShowDiagnosticsView()
    {
        deviceView.Content = diagnostic;
        evidenceViewToggle.Visibility = Visibility.Collapsed;
        deviceViewToggle.Content = workspace ? "返回调试画面" : "连接诊断";
        deviceHeading.Text = "浏览连接诊断";
        enterDebug.Visibility = workspace ? Visibility.Collapsed : Visibility.Visible;
        leaveDebug.Visibility = workspace ? Visibility.Visible : Visibility.Collapsed;
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
        var body = new StackPanel { Margin = new(24, 10, 24, 4) };

        StackPanel Group(string title, string id, bool danger = false)
        {
            var content = new StackPanel();
            var heading = Text(title);
            heading.FontSize = UiTypography.Position;
            heading.FontWeight = UiTypography.Emphasis;
            if (danger) heading.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
            var divider = new Border { Child = heading, BorderThickness = new(0, 0, 0, 1), Padding = new(0, 0, 0, 6),
                Margin = new(4, 0, 4, 6) };
            divider.SetResourceReference(Border.BorderBrushProperty, "Line");
            content.Children.Add(divider);

            var card = new Border { Child = content, Padding = new(14, 8, 14, 8), Margin = new(0, 0, 0, 10) };
            card.SetResourceReference(StyleProperty, danger ? "OperationsDangerCard" : "WorkstationCard");
            AutomationProperties.SetAutomationId(card, id);
            body.Children.Add(card);
            return content;
        }

        void AddTile(UniformGrid grid, Button action, string detail)
        {
            action.Margin = new(4);
            action.Tag = detail;
            AutomationProperties.SetHelpText(action, detail);
            action.SetResourceReference(StyleProperty, "OperationsMaintenanceAction");
            grid.Children.Add(action);
        }

        var daily = Group("日常保障与诊断", "Operations.Maintenance.Daily");
        var dailyActions = new UniformGrid { Columns = 4 };
        dailyActions.SizeChanged += (_, args) => dailyActions.Columns = args.NewSize.Width < 760 ? 2 : 4;
        daily.Children.Add(dailyActions);
        backupAction = Action(pendingBackup is null ? "创建完整备份…" : "核对原次完整备份", Backup);
        AutomationProperties.SetAutomationId(backupAction, "CompleteBackup.Operations");
        var cancelExport = new Button { Content = "取消导出", MinHeight = 44, Visibility = Visibility.Collapsed };
        cancelExport.Click += (_, _) => { diagnosticExport?.Cancel(); cancelExport.IsEnabled = false; model.Message = "已请求取消诊断导出，正在等待原服务收口。"; };
        AddTile(dailyActions, Action("导出诊断包…", async () =>
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
        }), "导出系统日志与状态快照。");
        AddTile(dailyActions, backupAction, "备份前明确暂停新供包。");
        AddTile(dailyActions, Action("完整备份核验、交付与保管…", BackupSets), "按完整集合核验与交付。");
        AddTile(dailyActions, Action("检查并恢复供包", async () =>
        {
            await model.RequireManagementModeAsync();
            await model.CommandAsync("Resume");
            await model.RefreshAsync();
        }), "由工作站检查当前责任和供包条件。");
        cancelExport.HorizontalAlignment = HorizontalAlignment.Left;
        daily.Children.Add(cancelExport);
        daily.Children.Add(backupOutcome);
        var original = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 160 };
        original.SetBinding(TextBox.TextProperty, new Binding(nameof(model.BackupStatusText)) { Source = model, Mode = BindingMode.OneWay });
        daily.Children.Add(new Expander { Header = "备份计划与执行状态", Content = original, Margin = new(4, 4, 4, 0) });

        var responsibility = Group("异常与责任核对", "Operations.Maintenance.Responsibilities");
        var laneActions = new UniformGrid { Columns = 2 };
        responsibility.Children.Add(laneActions);
        AddTile(laneActions, Action("核对左位未确认责任…", () => ConfirmRemotePositionRecovery("left")), "按左位原请求和外部依据核对。");
        AddTile(laneActions, Action("核对右位未确认责任…", () => ConfirmRemotePositionRecovery("right")), "按右位原请求和外部依据核对。");
        var responsibilityActions = new UniformGrid { Columns = 3 };
        responsibilityActions.SizeChanged += (_, args) => responsibilityActions.Columns = args.NewSize.Width < 680 ? 2 : 3;
        responsibility.Children.Add(responsibilityActions);
        AddTile(responsibilityActions, Action("恢复备份后核对…", () =>
        {
            parameters.ShowTechnicalTool(MaintenanceTool.RestoreHandoff);
            page.Content = parameters;
            return Task.CompletedTask;
        }), "查看原恢复状态及待核对事项。");
        AddTile(responsibilityActions, Action("核对异常作业与未完成输入…", async () =>
        {
            await model.RequireManagementModeAsync(); await model.EnsureMaintenanceLoadedAsync();
            parameters.ShowTechnicalTool(MaintenanceTool.Recovery); page.Content = parameters;
        }), "按原作业或原扫码输入检查处理状态。");
        AddTile(responsibilityActions, Action("核对原业务记录与请求责任…", async () =>
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
        }), "按原任务查看记录及相关日志。");

        var danger = Group("高危离线维护", "DangerZoneCard", danger: true);
        var offlineNotice = new TextBox { Text = "将关闭本工具并交接给同版受保护维护程序。离线维护会核验工作站已真正关闭，断线不代表停机。", IsTabStop = true };
        offlineNotice.SetResourceReference(StyleProperty, "ReadonlyValue");
        offlineNotice.Margin = new(4, 0, 4, 4);
        offlineNotice.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        danger.Children.Add(offlineNotice);
        var openOffline = Action("打开离线维护…", offline);
        openOffline.HorizontalAlignment = HorizontalAlignment.Left;
        openOffline.Margin = new(4);
        const string offlineScope = "备份保管、离线配置、恢复、回滚、清理与卸载。";
        openOffline.Tag = offlineScope;
        AutomationProperties.SetHelpText(openOffline, offlineScope);
        openOffline.SetResourceReference(StyleProperty, "OperationsOfflineAction");
        AutomationProperties.SetAutomationId(openOffline, "Operations.Maintenance.Offline");
        danger.Children.Add(openOffline);
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
        var control = new SoftwareLogControl(authority); control.UseParentHeading();
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
