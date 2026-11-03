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
        ShowObjectDiagnostics(new(instance, position.ReaderId, position.PositionId == "left" ? "左相机" : "右相机"));
    }
    void ShowObjectDiagnostics(ParameterDiagnosticTarget target)
    {
        if (!ViewModel.IsManagementMode) { ViewModel.Message = "请先进入管理模式。"; return; }
        diagnosticTarget = target; SetPage("diagnostics");
        selectedDiagnostic!.Text = target.Label;
        selectedDiagnostic.Visibility = Visibility.Visible;
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
        var body = new StackPanel { Margin = new(24, 16, 24, 24) };
        body.Children.Add(ProductText("设备与诊断", true));
        body.Children.Add(ProductText("状态自动更新。相机断线后自动重连；在线状态不代表本件已取得格口。"));
        selectedDiagnostic = ProductText(""); selectedDiagnostic.Visibility = diagnosticTarget is null ? Visibility.Collapsed : Visibility.Visible; body.Children.Add(selectedDiagnostic);
        backToDiagnosticSettings = new Button { Content = "返回该设备设置", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        backToDiagnosticSettings.SetBinding(IsEnabledProperty, new Binding(nameof(ViewModel.CanStartOperation)) { Source = ViewModel });
        backToDiagnosticSettings.Click += async (_, _) => await OpenOperatorSettingsAsync(diagnosticTarget?.DeviceId, diagnosticTarget?.InstanceId);
        backToDiagnosticSettings.Visibility = diagnosticTarget is null ? Visibility.Collapsed : Visibility.Visible; body.Children.Add(backToDiagnosticSettings);
        var unresolved = new Border { Padding = new(12), Margin = new(0, 8, 0, 12), Visibility = Visibility.Collapsed };
        unresolved.SetResourceReference(Border.BackgroundProperty, "WarningBackground");
        var attention = new DockPanel(); var pending = ProductText("");
        var records = ProductButton("去记录核对", async () => { SetPage("history"); await ViewModel.OpenHistoryAsync(); });
        DockPanel.SetDock(records, Dock.Right); attention.Children.Add(records); attention.Children.Add(pending); unresolved.Child = attention; body.Children.Add(unresolved);
        void RefreshAttention()
        {
            var count = ViewModel.Snapshot?.Operations.Count(o => o.Wcs?.State is StageState.Unknown or StageState.Conflict) ?? 0;
            unresolved.Visibility = ViewModel.Fresh && count > 0 ? Visibility.Visible : Visibility.Collapsed;
            pending.Text = $"有 {count} 件作业的格口结果未确认，请到记录核对；不会自动重发。";
        }
        System.ComponentModel.PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName is nameof(ViewModel.Snapshot) or nameof(ViewModel.Fresh)) RefreshAttention(); };
        body.Loaded += (_, _) => { ViewModel.PropertyChanged += changed; RefreshAttention(); };
        body.Unloaded += (_, _) => ViewModel.PropertyChanged -= changed;
        body.Children.Add(CreateDiagnosticRows());
        var evidence = new StackPanel();
        foreach (var property in new[] { nameof(ViewModel.DiagnosticBlockers), nameof(ViewModel.DiagnosticConfiguration), nameof(ViewModel.DiagnosticCapacity), nameof(ViewModel.DiagnosticTechnicalSummary) })
        {
            var value = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new(0), MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 8, 0, 0) };
            value.SetBinding(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.OneWay }); evidence.Children.Add(value);
        }
        body.Children.Add(new Expander { Header = "详情与原始证据", Content = evidence, Margin = new(0, 12, 0, 12) });
        body.Children.Add(new Expander { Header = "维护工具", Content = CreateMaintenanceTools(), Margin = new(0, 12, 0, 0) });
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return scroll;
    }
}
