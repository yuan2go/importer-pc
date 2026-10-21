using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Ipc.Application.ParcelFlow.Desktop;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    readonly Dictionary<string, FrameworkElement> diagnosticObjects = [];
    TextBlock? selectedDiagnostic;
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
        diagnosticTarget = target; SetPage("diagnostics");
        selectedDiagnostic!.Text = target.Label + "\n" + ViewModel.ParameterRuntimeSummary(target);
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
        new Window { Owner = this, DataContext = ViewModel, Title = "操作员身份", Width = 520, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = body }.Show();
    }
    void Diagnostics_Click(object sender, RoutedEventArgs e)
        => SetPage("diagnostics");
    FrameworkElement CreateDiagnostics()
    {
        var body = new StackPanel { Margin = new(20) };
        var header = new StackPanel { Margin = new(20, 12, 20, 0) };
        header.Children.Add(new TextBlock { Text = "诊断与恢复", FontSize = 24, FontWeight = FontWeights.SemiBold });
        selectedDiagnostic = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 8) };
        header.Children.Add(selectedDiagnostic);
        ViewModel.PropertyChanged += (_, e) => { if (diagnosticTarget is { } target && e.PropertyName == nameof(ViewModel.Fresh)) selectedDiagnostic.Text = target.Label + "\n" + ViewModel.ParameterRuntimeSummary(target); };
        var backToObject = new Button { Content = "返回当前对象设置", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        backToObject.Click += async (_, _) =>
        {
            var target = diagnosticTarget; SetPage("maintenance");
            await ViewModel.RunAsync(ViewModel.EnsureMaintenanceLoadedAsync, "读取关联对象参数");
            if (target is null || !ViewModel.HasLoadedConfiguration || ViewModel.SelectedPage != "maintenance") return;
            var instance = target.DeviceId is { } device ? ViewModel.ReaderDrafts.FirstOrDefault(r => r.Original.DeviceId == device)?.Capability.InstanceId : target.InstanceId;
            if (instance is not null) maintenance.NavigateToParameter(instance + "/Provider");
            else ViewModel.Message = "本次候选未包含该设备身份；请核对原拓扑，未替换对象。";
        };
        header.Children.Add(backToObject);
        body.Children.Add(new TextBlock { Text = "客户端生命周期、心跳网络交付 与业务回执分别核对。当前调度协议未提供权威结果查询；重连不应用待生效配置，也不重发未知原请求。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 16) });
        var positions = new Grid(); positions.ColumnDefinitions.Add(new()); positions.ColumnDefinitions.Add(new());
        foreach (var (position, index) in new[] { (ViewModel.Left, 0), (ViewModel.Right, 1) })
        {
            var panel = new StackPanel { DataContext = position, Margin = new(0, 0, 16, 16), Focusable = true };
            diagnosticObjects[position.ReaderId] = panel;
            var title = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold }; title.SetBinding(TextBlock.TextProperty, new Binding("Heading")); panel.Children.Add(title);
            foreach (var property in new[] { "ReaderId", "DeviceStatus", "Status", "BlockingReasons", "OriginalResponsibility", "OperationId", "RequestId" })
            {
                var text = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent };
                text.SetBinding(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.OneWay }); panel.Children.Add(text);
            }
            var edit = new Button { Content = "查看本位驱动参数", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
            edit.Click += async (_, _) =>
            {
                SetPage("maintenance");
                await ViewModel.RunAsync(ViewModel.EnsureMaintenanceLoadedAsync, "读取本位参数");
                var draft = ViewModel.ReaderDrafts.FirstOrDefault(r => r.Original.DeviceId == position.ReaderId)?.Capability;
                if (draft is not null) maintenance.NavigateToParameter(draft.InstanceId + "/Provider");
                else ViewModel.Message = "本次候选未包含该设备身份；请核对原拓扑，未替换对象。";
            };
            panel.Children.Add(edit);
            var original = new Button { Content = "查看本位原作业", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
            original.SetBinding(IsEnabledProperty, new Binding("CanReconcile"));
            original.Click += async (_, _) => { if (position.Operation is { } operation) await ShowDetailsAsync(operation); }; panel.Children.Add(original);
            Grid.SetColumn(panel, index); positions.Children.Add(panel);
        }
        body.Children.Add(positions);
        var shared = new TextBlock { Text = "共享服务与配置证据", FontSize = 20, FontWeight = FontWeights.SemiBold, Focusable = true };
        diagnosticObjects["shared"] = shared; body.Children.Add(shared);
        foreach (var property in new[] { nameof(ViewModel.Station), nameof(ViewModel.TopologyStatus), nameof(ViewModel.Session), nameof(ViewModel.ConfigurationState), nameof(ViewModel.Health), nameof(ViewModel.CapacityState), nameof(ViewModel.BlockingReasons), nameof(ViewModel.AuxiliaryState), nameof(ViewModel.ReadStatus), nameof(ViewModel.Message) })
        {
            var text = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, Margin = new(0, 0, 0, 12) };
            text.SetBinding(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.OneWay }); body.Children.Add(text);
        }
        var actions = new WrapPanel();
        var reconnect = new Button { Content = "按当前运行配置受控重连", MinHeight = 44 }; reconnect.SetBinding(IsEnabledProperty, new Binding(nameof(ViewModel.CanStartOperation)));
        reconnect.Click += async (_, _) => await ViewModel.RunAsync(ViewModel.ReconnectAsync, "受控重连");
        var configurationState = new Button { Content = "已保存与运行中", MinHeight = 44 };
        configurationState.Click += async (_, _) => { SetPage("maintenance"); await ViewModel.RunAsync(ViewModel.EnsureMaintenanceLoadedAsync); maintenance.ShowTechnicalTool(2); };
        var originalLedger = new Button { Content = "原配置账核对", MinHeight = 44 };
        originalLedger.Click += async (_, _) => { SetPage("maintenance"); await ViewModel.RunAsync(ViewModel.EnsureMaintenanceLoadedAsync); maintenance.ShowTechnicalTool(2); };
        var restore = new Button { Content = "原机恢复后核对", MinHeight = 44 };
        restore.Click += async (_, _) => { SetPage("maintenance"); await ViewModel.RunAsync(ViewModel.EnsureMaintenanceLoadedAsync); maintenance.ShowTechnicalTool(6); };
        var originals = new Button { Content = "原作业与接收责任", MinHeight = 44 };
        originals.Click += async (_, _) => { SetPage("maintenance"); await ViewModel.RunAsync(ViewModel.EnsureMaintenanceLoadedAsync); maintenance.ShowTechnicalTool(8); };
        actions.Children.Add(reconnect); actions.Children.Add(configurationState); actions.Children.Add(originalLedger); actions.Children.Add(restore); actions.Children.Add(originals); body.Children.Add(actions);
        var root = new DockPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); return root;
    }
}
