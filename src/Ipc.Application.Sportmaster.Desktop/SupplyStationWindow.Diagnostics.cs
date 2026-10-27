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
        var actions = new WrapPanel(); var login = new Button { Content = "验证身份" }; var logout = new Button { Content = "登出" };
        login.Click += async (_, _) => { var value = proof.Password; proof.Clear(); await ViewModel.RunAsync(() => ViewModel.LoginAsync(value), "认证操作员"); };
        logout.Click += async (_, _) => await ViewModel.RunAsync(ViewModel.LogoutAsync, "登出");
        actions.Children.Add(login); actions.Children.Add(logout); body.Children.Add(actions);
        var status = new TextBlock(); status.SetBinding(TextBlock.TextProperty, new Binding(nameof(ViewModel.Session))); body.Children.Add(status);
        var feedback = new TextBlock(); feedback.SetBinding(TextBlock.TextProperty, new Binding(nameof(ViewModel.OperatorFeedback))); body.Children.Add(feedback);
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
        backToDiagnosticSettings = new Button { Content = "返回当前对象设置", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = diagnosticTarget is null ? Visibility.Collapsed : Visibility.Visible, Margin = new(0, 8, 0, 0) };
        backToDiagnosticSettings.Click += async (_, _) => await OpenOperatorSettingsAsync(diagnosticTarget?.DeviceId, diagnosticTarget?.InstanceId);
        header.Children.Add(backToDiagnosticSettings);
        var status = new TextBlock { Margin = new(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(ViewModel.ReadStatus))); header.Children.Add(status);
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxHeight = 64, Margin = new(0, 4, 0, 0) };
        feedback.SetBinding(TextBlock.TextProperty, new Binding(nameof(ViewModel.TaskFeedback))); header.Children.Add(feedback);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var body = new StackPanel { Margin = new(24, 0, 24, 16) };
        TextBlock Text(string property, double size = UiTypography.Body)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = size, Margin = new(0, 4, 0, 4) };
            text.SetBinding(TextBlock.TextProperty, new Binding(property)); return text;
        }
        void Connection(string label, string diagnosis, string time, Func<Task> configure, SupplyStationPositionViewModel? position = null)
        {
            var row = new Grid { Margin = new(0, 0, 0, 16) };
            row.ColumnDefinitions.Add(new() { Width = new(100) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, FontSize = UiTypography.Position, FontWeight = UiTypography.Emphasis, Margin = new(0, 8, 12, 0) });
            var facts = new StackPanel { Margin = new(0, 0, 16, 0) };
            facts.Children.Add(Text(diagnosis)); facts.Children.Add(Text(time, UiTypography.Label));
            Grid.SetColumn(facts, 1); row.Children.Add(facts);
            var actions = new StackPanel();
            var edit = new Button { Content = "设置" + label, MinHeight = 44, MinWidth = 112 };
            edit.Click += async (_, _) => await configure(); actions.Children.Add(edit);
            if (position is not null)
            {
                var original = new Button { Content = "查看包裹记录", MinHeight = 44, Margin = new(0, 4, 0, 0), DataContext = position };
                original.SetBinding(IsEnabledProperty, new Binding(nameof(position.CanReconcile)));
                original.SetBinding(VisibilityProperty, new Binding(nameof(position.CanReconcile)) { Converter = new BooleanToVisibilityConverter() });
                original.Click += async (_, _) => { if (position.Operation is { } operation) await ShowDetailsAsync(operation); }; actions.Children.Add(original);
            }
            Grid.SetColumn(actions, 2); row.Children.Add(actions); body.Children.Add(row);
            diagnosticObjects[position?.ReaderId ?? "shared"] = row;
        }
        Connection("左相机", nameof(ViewModel.LeftConnectionDiagnosis), nameof(ViewModel.LeftConnectionTime), () => OpenOperatorSettingsAsync(ViewModel.Left.ReaderId), ViewModel.Left);
        Connection("右相机", nameof(ViewModel.RightConnectionDiagnosis), nameof(ViewModel.RightConnectionTime), () => OpenOperatorSettingsAsync(ViewModel.Right.ReaderId), ViewModel.Right);
        Connection("RCS", nameof(ViewModel.RcsConnectionDiagnosis), nameof(ViewModel.RcsConnectionTime), () => OpenOperatorSettingsAsync(instanceId: ViewModel.Snapshot?.Configuration.Effective.Wcs?.InstanceId));
        body.Children.Add(new Separator { Margin = new(0, 0, 0, 12) });
        body.Children.Add(new TextBlock { Text = "供包条件", FontWeight = UiTypography.Emphasis });
        body.Children.Add(Text(nameof(ViewModel.DiagnosticBlockers)));
        body.Children.Add(Text(nameof(ViewModel.DiagnosticConfiguration)));
        body.Children.Add(Text(nameof(ViewModel.DiagnosticCapacity)));
        var reconnect = new Button { Content = "重新连接设备与服务", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 12, 0, 0) };
        reconnect.SetBinding(IsEnabledProperty, new Binding(nameof(ViewModel.CanReconnectConfigured)));
        reconnect.Click += async (_, _) => await ViewModel.ReconnectConfiguredAsync(); body.Children.Add(reconnect);
        body.Children.Add(Text(nameof(ViewModel.ReconnectHint), UiTypography.Label));
        body.Children.Add(new TextBlock { Text = "重连后仍保持暂停，使用当前运行配置；不重发结果未知的原请求。", TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label });
        var tools = new WrapPanel { Margin = new(0, 8, 0, 0) };
        foreach (var (label, index) in new[] { ("核对配置生效情况", 2), ("核对备份恢复结果", 6), ("处理待核对作业", 8) })
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
            details.Children.Add(new TextBlock { Text = "相机读取的全部条码" });
            details.Children.Add(Text(nameof(position.RawCandidates)));
            details.Children.Add(new TextBlock { Text = "用于申请任务的条码" });
            details.Children.Add(Text(nameof(position.SelectedCode)));
            var simulation = new StackPanel();
            simulation.SetBinding(VisibilityProperty, new Binding(nameof(ViewModel.SimulationVisibility)) { Source = ViewModel });
            simulation.Children.Add(new TextBlock { Text = "模拟输入（仅模拟模式）", Margin = new(0, 12, 0, 4) });
            var code = new TextBox { MinHeight = 44 }; code.SetBinding(TextBox.TextProperty, new Binding(nameof(position.ParcelCode)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); simulation.Children.Add(code);
            var simulateActions = new WrapPanel();
            var next = new Button { Content = "新包裹身份", MinHeight = 44 }; next.Click += (_, _) => position.NewParcel(); simulateActions.Children.Add(next);
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
            try { Clipboard.SetText(ViewModel.DiagnosticTechnicalSummary); copied.Text = "已复制当前状态与技术标识，可交给维护支持人员。"; }
            catch (System.Runtime.InteropServices.ExternalException) { copied.Text = "剪贴板正被占用，请稍后再复制。"; }
        };
        body.Children.Add(copy); body.Children.Add(copied);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return root;
    }
}
