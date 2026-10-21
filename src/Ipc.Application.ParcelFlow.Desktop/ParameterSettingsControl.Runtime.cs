using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed partial class ParameterSettingsControl
{
    public static readonly DependencyProperty RuntimeContextProperty = DependencyProperty.Register(nameof(RuntimeContext), typeof(ParcelViewModel), typeof(ParameterSettingsControl), new PropertyMetadata(null, RuntimeContextChanged));
    public ParcelViewModel? RuntimeContext { get => (ParcelViewModel?)GetValue(RuntimeContextProperty); set => SetValue(RuntimeContextProperty, value); }
    public event EventHandler<ParameterDiagnosticTarget>? DiagnosticsRequested;
    readonly List<Action> runtimeUpdates = [];
    static void RuntimeContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ParameterSettingsControl)d;
        if (e.OldValue is ParcelViewModel old) PropertyChangedEventManager.RemoveHandler(old, view.RuntimeChanged, "");
        if (e.NewValue is ParcelViewModel next) PropertyChangedEventManager.AddHandler(next, view.RuntimeChanged, "");
        view.RefreshRuntime();
    }
    void RuntimeChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(ParcelViewModel.Snapshot) or nameof(ParcelViewModel.Fresh)) RefreshRuntime(); }
    void RefreshRuntime() { foreach (var update in runtimeUpdates) update(); }
    void RuntimeSummary(ObjectPage page, CapabilityDraft draft)
    {
        var target = new ParameterDiagnosticTarget(draft.InstanceId, draft.Kind == "observation" ? draft.Target : null, page.Title);
        var row = new DockPanel();
        var diagnostic = new Button { Content = "连接诊断", MinHeight = 44, VerticalAlignment = VerticalAlignment.Top, Margin = new(16, 0, 0, 0) };
        AutomationProperties.SetName(diagnostic, page.Title + "连接诊断");
        diagnostic.Click += (_, _) => DiagnosticsRequested?.Invoke(this, target);
        DockPanel.SetDock(diagnostic, Dock.Right); row.Children.Add(diagnostic);
        var summary = Text(""); summary.FontWeight = FontWeights.SemiBold; summary.Margin = new(0, 0, 0, 6);
        AutomationProperties.SetAutomationId(summary, draft.InstanceId + "/RuntimeSummary");
        var address = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, MinHeight = 0, Padding = new(0), FontSize = 13 };
        AutomationProperties.SetName(address, "当前运行地址");
        var notice = Text(""); notice.FontSize = 13; notice.Margin = new(0, 8, 0, 0);
        var main = new StackPanel(); main.Children.Add(summary); main.Children.Add(address); main.Children.Add(notice); row.Children.Add(main);
        var details = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, Padding = new(0), FontSize = 13 };
        page.Technical.Children.Add(details);
        void Update()
        {
            var state = RuntimeContext?.ParameterConnection(target) ?? new ParameterConnectionPresentation("尚未启动", "", "", "这里只编辑候选设置，连接状态需启动后核对。", "neutral");
            summary.Text = state.Status;
            summary.Foreground = Brush(state.Tone switch { "ready" => "#16704A", "warning" => "#915300", _ => "#5A6878" });
            address.Text = state.Address.Length == 0 ? "" : "运行地址：" + state.Address;
            address.Visibility = state.Address.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            notice.Text = state.Notice; notice.Visibility = state.Notice.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            notice.Foreground = Brush(state.Tone == "warning" ? "#915300" : "#5A6878");
            details.Text = "运行版本与证据\n" + state.Details; details.Visibility = state.Details.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            diagnostic.IsEnabled = RuntimeContext is not null && DiagnosticsRequested is not null;
            diagnostic.ToolTip = diagnostic.IsEnabled ? "查看该对象的运行证据；不会检查编辑地址或发送业务请求。" : "启动前没有运行诊断；完成配置并启动后核对。";
            ToolTipService.SetShowOnDisabled(diagnostic, true);
        }
        runtimeUpdates.Add(Update); Update(); page.Body.Children.Add(new Border { Child = row, BorderBrush = Brush("#D8E1EB"), BorderThickness = new(0, 0, 0, 1), Padding = new(0, 0, 0, 12), Margin = new(0, 0, 0, 8) });
    }
}
