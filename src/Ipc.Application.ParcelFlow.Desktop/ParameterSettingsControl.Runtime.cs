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
        if (draft.Kind != "observation" && !ParameterOwnership.IsRcs(draft)) return;
        var target = new ParameterDiagnosticTarget(draft.InstanceId, draft.Kind == "observation" ? draft.Target : null, page.Title);
        var row = new DockPanel { Margin = new(0, 0, 0, 16) };
        var diagnostic = new Button { Content = "连接诊断", MinHeight = 44, VerticalAlignment = VerticalAlignment.Top, Margin = new(16, 0, 0, 0) };
        AutomationProperties.SetName(diagnostic, page.Title + "连接诊断");
        diagnostic.Click += (_, _) => DiagnosticsRequested?.Invoke(this, target);
        DockPanel.SetDock(diagnostic, Dock.Right); row.Children.Add(diagnostic);
        var summary = Text(""); AutomationProperties.SetAutomationId(summary, draft.InstanceId + "/RuntimeSummary"); row.Children.Add(summary);
        void Update()
        {
            summary.Text = RuntimeContext?.ParameterRuntimeSummary(target) ?? "尚未启动 · 这里只编辑候选设置，连接状态需启动后核对。";
            diagnostic.IsEnabled = RuntimeContext is not null && DiagnosticsRequested is not null;
            diagnostic.ToolTip = diagnostic.IsEnabled ? "查看该对象的运行证据；不会检查编辑地址或发送业务请求。" : "启动前没有运行诊断；完成配置并启动后核对。";
            ToolTipService.SetShowOnDisabled(diagnostic, true);
        }
        runtimeUpdates.Add(Update); Update(); page.Body.Children.Add(row);
    }
}
