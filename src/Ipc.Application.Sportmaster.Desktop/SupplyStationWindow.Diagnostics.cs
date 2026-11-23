using Ipc.Application.Contracts;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Ipc.Application.ParcelFlow.Desktop;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    StackPanel? diagnosticLogDetails;
    readonly Dictionary<string, FrameworkElement> diagnosticObjects = [];
    TextBlock? selectedDiagnostic;
    ParameterDiagnosticTarget? diagnosticTarget;
    Expander? diagnosticTools;
    FrameworkElement? maintenanceTools;
    ScrollViewer? maintenanceToolsPage;
    FrameworkElement ShowDiagnosticOverview()
    {
        diagnostics ??= CreateDiagnostics();
        if (maintenanceToolsPage is not null) maintenanceToolsPage.Content = null;
        diagnosticTools!.Content = maintenanceTools ??= CreateMaintenanceTools();
        return diagnostics;
    }
    FrameworkElement ShowMaintenanceTools()
    {
        if (diagnosticTools is not null) diagnosticTools.Content = null;
        maintenanceToolsPage ??= new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(20, 12, 20, 12) };
        maintenanceToolsPage.Content = maintenanceTools ??= CreateMaintenanceTools();
        return maintenanceToolsPage;
    }
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
        diagnosticTarget = target;
        diagnostics ??= CreateDiagnostics();
        SetPage("diagnostics");
        selectedDiagnostic!.Text = target.Label;
        selectedDiagnostic.Visibility = Visibility.Visible;
        UpdateLayout();
        if (diagnosticObjects.TryGetValue(target.DeviceId ?? "shared", out var anchor)) { anchor.BringIntoView(new Rect(0, 0, 1, 1)); }
    }
    void Diagnostics_Click(object sender, RoutedEventArgs e)
        => SetPage("diagnostics");
    FrameworkElement CreateDiagnostics()
    {
        var body = new StackPanel { Margin = new(24, 16, 24, 24) };
        body.Children.Add(ProductText("状态自动更新。相机断线后自动重连；在线状态不代表本件已取得格口。"));
        selectedDiagnostic = ProductText(""); selectedDiagnostic.Visibility = diagnosticTarget is null ? Visibility.Collapsed : Visibility.Visible; body.Children.Add(selectedDiagnostic);
        var currentBlockers = ProductText("");
        currentBlockers.SetBinding(TextBlock.TextProperty, new Binding(nameof(ViewModel.DiagnosticBlockers)) { Mode = BindingMode.OneWay });
        body.Children.Add(currentBlockers);
        var unresolved = new Border { Padding = new(12), Margin = new(0, 8, 0, 12), Visibility = Visibility.Collapsed };
        unresolved.SetResourceReference(Border.BackgroundProperty, "Soft");
        var attention = new DockPanel(); var pending = ProductText("");
        var records = ProductButton("去记录核对", async () => { SetPage("history"); await ViewModel.OpenHistoryAsync(); });
        DockPanel.SetDock(records, Dock.Right); attention.Children.Add(records); attention.Children.Add(pending); unresolved.Child = attention; body.Children.Add(unresolved);
        void RefreshAttention()
        {
            var count = ViewModel.Snapshot?.Capacity?.HistoricalUnknown ?? 0;
            unresolved.Visibility = ViewModel.Fresh && count > 0 ? Visibility.Visible : Visibility.Collapsed;
            pending.Text = $"历史待核对 {count} 件。请在记录中查看原作业。";
        }
        System.ComponentModel.PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName is nameof(ViewModel.Snapshot) or nameof(ViewModel.Fresh)) RefreshAttention(); };
        body.Loaded += (_, _) => { ViewModel.PropertyChanged += changed; RefreshAttention(); };
        body.Unloaded += (_, _) => ViewModel.PropertyChanged -= changed;
        body.Children.Add(CreateDiagnosticRows());
        var evidence = new StackPanel();
        if (diagnosticLogDetails is { } logDetails) { logDetails.Margin = new(0, 0, 0, 12); evidence.Children.Add(logDetails); }
        foreach (var property in new[] { nameof(ViewModel.DiagnosticConfiguration), nameof(ViewModel.DiagnosticCapacity), nameof(ViewModel.DiagnosticTechnicalSummary) })
        {
            var value = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new(0), MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 8, 0, 0) };
            value.SetBinding(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.OneWay }); evidence.Children.Add(value);
        }
        body.Children.Add(new Expander { Header = "技术详情", Content = evidence, Margin = new(0, 12, 0, 12) });
        diagnosticTools = new Expander { Header = "维护工具", Margin = new(0, 12, 0, 0) };
        body.Children.Add(diagnosticTools);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return scroll;
    }
}
