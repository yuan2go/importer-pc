using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    DockPanel? positionHistory;
    FrameworkElement PositionHistory()
    {
        if (positionHistory is not null) return positionHistory;
        positionHistory = new DockPanel { DataContext = ViewModel };
        var panel = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var refresh = new Button { Content = "最近记录", MinHeight = 44, Margin = new Thickness(0, 0, 8, 4) };
        var earlier = new Button { Content = "更早记录", MinHeight = 44, Margin = new Thickness(0, 0, 0, 4) };
        refresh.Click += async (_, _) => await ViewModel.RunAsync(() => ViewModel.LoadSupplyHistoryAsync(), "读取启停记录");
        earlier.Click += async (_, _) => await ViewModel.RunAsync(() => ViewModel.LoadSupplyHistoryAsync(true), "读取更早启停记录");
        actions.Children.Add(refresh); actions.Children.Add(earlier); DockPanel.SetDock(actions, Dock.Top); panel.Children.Add(actions);
        var table = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, Height = 180 };
        foreach (var (label, path) in new[] { ("时间", "Time"), ("投递位", "Position"), ("操作", "Action"), ("操作人", "Actor"), ("当时任务号", "Task") })
            table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(path), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        table.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(ViewModel.SupplyHistory))); panel.Children.Add(table);
        var expander = new Expander { Header = "投递位启停记录", Content = panel, Margin = new Thickness(0, 0, 0, 8) };
        expander.Expanded += async (_, _) => await ViewModel.RunAsync(() => ViewModel.LoadSupplyHistoryAsync(), "读取启停记录");
        DockPanel.SetDock(expander, Dock.Top); positionHistory.Children.Add(expander); positionHistory.Children.Add(history);
        return positionHistory;
    }
}
