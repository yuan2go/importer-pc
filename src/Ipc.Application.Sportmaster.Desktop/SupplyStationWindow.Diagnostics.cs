using Ipc.Application.Contracts;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Ipc.Application.ParcelFlow.Desktop;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    readonly Dictionary<string, FrameworkElement> diagnosticObjects = [];
    TextBlock? selectedDiagnostic;
    Button? backToDiagnosticSettings;
    ParameterDiagnosticTarget? diagnosticTarget;
    public void ShowPositionDiagnostics(SupplyStationPositionViewModel position)
    {
        var reader = ViewModel.Snapshot is { } snapshot ? Ipc.Application.Contracts.ParcelBindings.Readers(snapshot.Configuration.Effective)
            .SingleOrDefault(r => r.DeviceId == position.ReaderId) : null;
        var instance = reader?.Driver.StartsWith("extension:", StringComparison.Ordinal) == true ? reader.Driver["extension:".Length..] : position.ReaderId;
        ShowObjectDiagnostics(new(instance, position.ReaderId, position.Heading));
    }
    void ShowObjectDiagnostics(ParameterDiagnosticTarget target)
    {
        if (!ViewModel.IsManagementMode) { ViewModel.Message = "请先进入管理模式。"; return; }
        diagnosticTarget = target; SetPage("diagnostics");
        selectedDiagnostic!.Text = target.Label;
        backToDiagnosticSettings!.Visibility = Visibility.Visible;
        UpdateLayout();
        if (diagnosticObjects.TryGetValue(target.DeviceId ?? "shared", out var anchor)) { anchor.BringIntoView(new Rect(0, 0, 1, 1)); }
    }
    void OperatorLogin_Click(object sender, RoutedEventArgs e)
    {
        var body = new StackPanel { Margin = new(20) };
        body.SetBinding(IsEnabledProperty, new Binding(nameof(ViewModel.CanStartOperation)));
        body.Children.Add(new TextBlock { Text = "操作员账号" });
        var subject = new TextBox(); subject.SetBinding(TextBox.TextProperty, new Binding(nameof(ViewModel.OperatorSubject)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); body.Children.Add(subject);
        body.Children.Add(new TextBlock { Text = "操作员口令" }); var proof = new PasswordBox(); body.Children.Add(proof);
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var actions = new WrapPanel(); var login = new Button { Content = "验证身份" }; var logout = new Button { Content = "登出" };
        login.Click += async (_, _) => { var value = proof.Password; proof.Clear(); feedback.Text = "正在验证身份…"; await ViewModel.RunAsync(() => ViewModel.LoginAsync(value), "认证操作员"); feedback.Text = ViewModel.OperatorFeedback; };
        logout.Click += async (_, _) => { feedback.Text = "正在登出…"; await ViewModel.RunAsync(ViewModel.LogoutAsync, "登出"); feedback.Text = ViewModel.OperatorFeedback; };
        actions.Children.Add(login); actions.Children.Add(logout); body.Children.Add(actions);
        var status = new TextBlock(); status.SetBinding(TextBlock.TextProperty, new Binding(nameof(ViewModel.Session))); body.Children.Add(status);
        body.Children.Add(feedback);
        var dialog = new Window { FontFamily = UiTypography.Interface, FontSize = UiTypography.Body, Owner = this, DataContext = ViewModel, Title = "操作员身份", Width = 520, Height = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var close = new Button { Content = "关闭", IsCancel = true, MinHeight = 44 }; close.Click += (_, _) => dialog.Close(); actions.Children.Add(close);
        subject.MinHeight = proof.MinHeight = 44;
        var visibleFeedback = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; dialog.Content = visibleFeedback;
        dialog.Loaded += (_, _) => { if (string.IsNullOrEmpty(subject.Text)) subject.Focus(); else proof.Focus(); };
        dialog.Closed += (_, _) => proof.Clear(); dialog.Show();
    }
    void Diagnostics_Click(object sender, RoutedEventArgs e)
        => SetPage("diagnostics");
    FrameworkElement CreateDiagnostics()
    {
        var root = new DockPanel { Background = System.Windows.Media.Brushes.White };
        root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Ipc.Application.ParcelFlow.Desktop;component/QueryWorkspaceTheme.xaml", UriKind.Relative) });
        var header = new StackPanel { Margin = new(24, 16, 24, 12) };
        header.Children.Add(new TextBlock { Text = "诊断与恢复", FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis });
        selectedDiagnostic = new TextBlock { Margin = new(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        header.Children.Add(selectedDiagnostic);
        backToDiagnosticSettings = new Button { Content = "返回该设备设置", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = diagnosticTarget is null ? Visibility.Collapsed : Visibility.Visible, Margin = new(0, 8, 0, 0) };
        backToDiagnosticSettings.Click += async (_, _) => await OpenOperatorSettingsAsync(diagnosticTarget?.DeviceId, diagnosticTarget?.InstanceId);
        header.Children.Add(backToDiagnosticSettings);
        var status = new TextBlock { Margin = new(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(ViewModel.ReadStatus))); header.Children.Add(status);
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxHeight = 64, Margin = new(0, 4, 0, 0) };
        feedback.SetBinding(TextBlock.TextProperty, new Binding(nameof(ViewModel.TaskFeedback))); header.Children.Add(feedback);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var body = new StackPanel { Margin = new(24, 0, 24, 16) };
        var pending = ProductText("");
        void PendingState() { var count = ViewModel.Snapshot?.Operations.Count(o => o.Wcs?.State is StageState.Unknown or StageState.Conflict) ?? 0; pending.Text = $"格口未确认 {count} 件作业 · {(ViewModel.Snapshot?.Configuration.Pending is null ? "没有待重启设置" : "设置已保存，重启后生效")}"; }
        PendingState(); body.Children.Add(pending);
        var checkActions = new WrapPanel { Margin = new(0, 4, 0, 8) }; body.Children.Add(checkActions);
        checkActions.Children.Add(ProductButton("处理异常", () => { SetPage("exceptions"); return Task.CompletedTask; }));
        var healthSummary = ProductText("点击刷新体检，读取现有采集、RCS确认及存储事实。"); healthSummary.FontSize = UiTypography.Label;
        checkActions.Children.Add(ProductButton("刷新体检", async () => { healthSummary.Text = await ReadProductHealth(); PendingState(); }));
        checkActions.Children.Add(ProductButton("导出诊断包…", ExportProductDiagnostics));
        var cancelDiagnostic = new Button { Content = "取消导出", MinHeight = 44, Margin = new(0, 6, 0, 6), Padding = new(12, 4, 12, 4) };
        cancelDiagnostic.Click += (_, _) => productDiagnosticCancellation?.Cancel(); checkActions.Children.Add(cancelDiagnostic);
        body.Children.Add(healthSummary);
        var connections = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(connections);
        connections.SizeChanged += (_, _) =>
        {
            var columns = connections.ActualWidth >= 1020 ? 3 : 1;
            foreach (var card in connections.Children.OfType<Border>()) card.Width = Math.Max(240, connections.ActualWidth / columns - 12);
        };
        TextBlock Text(string property, double size = UiTypography.Body)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = size, Margin = new(0, 4, 0, 4) };
            text.SetBinding(TextBlock.TextProperty, new Binding(property)); return text;
        }
        void Connection(string label, string diagnosis, string time, Func<Task> configure, SupplyStationPositionViewModel? position = null)
        {
            var row = new StackPanel();
            var heading = new DockPanel { Margin = new(0, 0, 0, 8) };
            var badge = new TextBlock { FontSize = UiTypography.Label, VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) };
            badge.Padding = new(6, 3, 6, 3);
            badge.SetBinding(TextBlock.ForegroundProperty, new Binding(position is null ? "RcsPresentation.Foreground" : "DevicePresentation.Foreground") { Source = position is null ? ViewModel : position });
            badge.SetBinding(TextBlock.BackgroundProperty, new Binding(position is null ? "RcsPresentation.Background" : "DevicePresentation.Background") { Source = position is null ? ViewModel : position });
            badge.SetBinding(TextBlock.TextProperty, new Binding(position is null ? nameof(ViewModel.RcsServiceStatus) : nameof(position.DeviceLabel)) { Source = position is null ? ViewModel : position });
            DockPanel.SetDock(badge, Dock.Right); heading.Children.Add(badge);
            heading.Children.Add(new TextBlock { Text = label, FontSize = UiTypography.Position, FontWeight = UiTypography.Emphasis }); row.Children.Add(heading);
            var facts = new StackPanel();
            facts.Children.Add(Text(diagnosis)); facts.Children.Add(Text(time, UiTypography.Label));
            row.Children.Add(facts);
            var actions = new WrapPanel { Margin = new(0, 8, 0, 0) };
            var edit = new Button { Content = "设置" + label, MinHeight = 44, MinWidth = 112 };
            edit.Click += async (_, _) => await configure(); actions.Children.Add(edit);
            if (position is not null)
            {
                var debugButton = new Button { Content = "调试" + label, MinHeight = 44, Margin = new(8, 0, 0, 0) };
                debugButton.Click += (_, _) => ShowPositionDebug(position); actions.Children.Add(debugButton);
                var original = new Button { Content = "查看包裹记录", MinHeight = 44, Margin = new(0, 4, 0, 0), DataContext = position };
                original.SetBinding(IsEnabledProperty, new Binding(nameof(position.CanReconcile)));
                original.SetBinding(VisibilityProperty, new Binding(nameof(position.CanReconcile)) { Converter = new BooleanToVisibilityConverter() });
                original.Click += async (_, _) => { if (position.Operation is { } operation) await ShowDetailsAsync(operation); }; actions.Children.Add(original);
            }
            row.Children.Add(actions);
            connections.Children.Add(new Border { Child = row, BorderBrush = (System.Windows.Media.Brush)FindResource("StationLine"), BorderThickness = new(1),
                CornerRadius = new(8), Padding = new(16), Margin = new(0, 0, 12, 12), Background = System.Windows.Media.Brushes.White });
            diagnosticObjects[position?.ReaderId ?? "shared"] = row;
        }
        Connection("左相机", nameof(ViewModel.LeftConnectionDiagnosis), nameof(ViewModel.LeftConnectionTime), () => OpenOperatorSettingsAsync(ViewModel.Left.ReaderId), ViewModel.Left);
        Connection("右相机", nameof(ViewModel.RightConnectionDiagnosis), nameof(ViewModel.RightConnectionTime), () => OpenOperatorSettingsAsync(ViewModel.Right.ReaderId), ViewModel.Right);
        Connection("RCS", nameof(ViewModel.RcsConnectionDiagnosis), nameof(ViewModel.RcsConnectionTime), () => OpenOperatorSettingsAsync(instanceId: ViewModel.Snapshot?.Configuration.Effective.Wcs?.InstanceId));
        body.Children.Add(new Separator { Margin = new(0, 0, 0, 12) });
        body.Children.Add(new TextBlock { Text = "供包状态", FontWeight = UiTypography.Emphasis });
        body.Children.Add(Text(nameof(ViewModel.DiagnosticBlockers)));
        body.Children.Add(Text(nameof(ViewModel.DiagnosticConfiguration)));
        body.Children.Add(Text(nameof(ViewModel.DiagnosticCapacity)));
        var reconnect = new Button { Content = "重新连接相机和 RCS", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 12, 0, 0) };
        reconnect.SetBinding(IsEnabledProperty, new Binding(nameof(ViewModel.CanReconnectConfigured)));
        reconnect.Click += async (_, _) => await ViewModel.ReconnectConfiguredAsync();
        var controlled = new StackPanel(); controlled.Children.Add(reconnect); controlled.Children.Add(Text(nameof(ViewModel.ReconnectHint), UiTypography.Label));
        body.Children.Add(new Expander { Header = "连接没有恢复？手动重连", Content = controlled, Margin = new(0, 8, 0, 8) });
        body.Children.Add(new TextBlock { Text = "相机断线后会自动重连。格口未确认的包裹不会被重发。", TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label });
        var tools = new WrapPanel { Margin = new(0, 8, 0, 0) };
        foreach (var (label, index) in new[] { ("查看设置是否生效", MaintenanceTool.Running), ("查看备份恢复结果", MaintenanceTool.RestoreHandoff), ("处理格口未确认的包裹", MaintenanceTool.Recovery) })
        {
            var button = new Button { Content = label, MinHeight = 44, Margin = new(0, 0, 8, 8) };
            button.Click += async (_, _) => { await OpenOperatorSettingsAsync(); if (ViewModel.IsManagementMode && ViewModel.SelectedPage == "maintenance" && ViewModel.HasLoadedConfiguration) maintenance.ShowTechnicalTool(index); };
            tools.Children.Add(button);
        }
        body.Children.Add(new Expander { Header = "其他维护操作", Content = tools, Margin = new(0, 16, 0, 8) });
        var scans = new StackPanel();
        foreach (var position in new[] { ViewModel.Left, ViewModel.Right })
        {
            var details = new StackPanel { DataContext = position, Margin = new(0, 8, 0, 16) };
            details.Children.Add(new TextBlock { Text = position.Heading, FontWeight = UiTypography.Emphasis });
            details.Children.Add(Text(nameof(position.CameraAddress)));
            details.Children.Add(Text(nameof(position.ScanTime)));
            details.Children.Add(new TextBlock { Text = "相机读到的全部条码" });
            details.Children.Add(Text(nameof(position.RawCandidates)));
            details.Children.Add(new TextBlock { Text = "发给 RCS 的条码" });
            details.Children.Add(Text(nameof(position.SelectedCode)));
            var simulation = new StackPanel();
            simulation.SetBinding(VisibilityProperty, new Binding(nameof(ViewModel.SimulationVisibility)) { Source = ViewModel });
            simulation.Children.Add(new TextBlock { Text = "模拟输入（仅模拟模式）", Margin = new(0, 12, 0, 4) });
            var code = new TextBox { MinHeight = 44 }; code.SetBinding(TextBox.TextProperty, new Binding(nameof(position.ParcelCode)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); simulation.Children.Add(code);
            var simulateActions = new WrapPanel();
            var next = new Button { Content = "模拟新包裹", MinHeight = 44 }; next.Click += (_, _) => position.NewParcel(); simulateActions.Children.Add(next);
            var emit = new Button { Content = "模拟本位读码", MinHeight = 44 }; emit.SetBinding(IsEnabledProperty, new Binding(nameof(position.CanAdmit)));
            emit.Click += async (_, _) => await ViewModel.EmitPositionAsync(position); simulateActions.Children.Add(emit); simulation.Children.Add(simulateActions);
            details.Children.Add(simulation); scans.Children.Add(details);
        }
        body.Children.Add(new Expander { Header = "查看扫描内容", Content = scans, Margin = new(0, 4, 0, 8) });
        var copy = new Button { Content = "复制诊断信息", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        var copied = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label };
        copy.Click += (_, _) =>
        {
            if (!ViewModel.IsManagementMode) return;
            try { Clipboard.SetText(ViewModel.DiagnosticTechnicalSummary); copied.Text = "已复制诊断信息，可发给维护支持人员。"; }
            catch (System.Runtime.InteropServices.ExternalException) { copied.Text = "剪贴板正被占用，请稍后再复制。"; }
        };
        body.Children.Add(copy); body.Children.Add(copied);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return root;
    }
}
