using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;

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
        DockPanel.SetDock(expander, Dock.Top); positionHistory.Children.Add(expander);
        var rounds = RoundHistory(); DockPanel.SetDock(rounds, Dock.Top); positionHistory.Children.Add(rounds); positionHistory.Children.Add(history);
        return positionHistory;
    }
    sealed record RoundRow(SmParcelRound Round)
    {
        public string Time => Round.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        public string Position => Round.PositionId == "left" ? "左投递位" : "右投递位";
        public string Barcode => Round.SelectedCode ?? (Round.FormalCodes.Length == 0 ? "无条码" : string.Join(";", Round.FormalCodes));
        public string Identity => Round.Id;
        public string Request => Round.ProtocolRequestId?.ToString() ?? Round.RequestId ?? "未提交";
        public string Result => Round.RcsOutcome switch { "Accepted" => "格口已取得", "Rejected" => "业务拒绝", "Unknown" or "Conflict" => "结果未知", "Dispatching" => "调用中", "Prepared" => "已准备", _ => "未提交" };
        public string End => Round.EndReason switch { "TrustedRemoval" => "可信移出", "OperatorConfirmedRemoval" => "人工确认取下", "SuccessDisplayElapsed" => "成功显示3秒结束", "InterruptedOnRestart" => "重启中断，未重发", _ => Round.EndedAt is null ? "本轮尚未结束" : "已结束" };
    }
    FrameworkElement RoundHistory()
    {
        var panel = new DockPanel(); var controls = new WrapPanel();
        var side = new ComboBox { ItemsSource = new[] { "全部投递位", "左投递位", "右投递位" }, SelectedIndex = 0, MinWidth = 140, MinHeight = 44 };
        var from = new DatePicker { SelectedDate = DateTime.Today, MinHeight = 44, MinWidth = 140 };
        var to = new DatePicker { SelectedDate = DateTime.Today, MinHeight = 44, MinWidth = 140 };
        var search = new TextBox { MinWidth = 240, MinHeight = 44, ToolTip = "精确查询条码、轮次、请求号或结果（格口已取得、业务拒绝、结果未知）" };
        System.Windows.Automation.AutomationProperties.SetName(search, "轮次查询条码、编号或结果");
        System.Windows.Automation.AutomationProperties.SetName(from, "轮次开始日期"); System.Windows.Automation.AutomationProperties.SetName(to, "轮次结束日期");
        var load = new Button { Content = "查询轮次", MinHeight = 44 }; var picture = new Button { Content = "查看本轮原图", MinHeight = 44, IsEnabled = false };
        foreach (var control in new FrameworkElement[] { side, from, to, search, load, picture }) { control.Margin = new Thickness(0, 0, 8, 8); controls.Children.Add(control); }
        DockPanel.SetDock(controls, Dock.Top); panel.Children.Add(controls);
        var table = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, Height = 220 };
        foreach (var (label, path) in new[] { ("开始时间", "Time"), ("投递位", "Position"), ("正式条码", "Barcode"), ("结果", "Result"), ("结束原因", "End"), ("轮次", "Identity"), ("请求号", "Request") })
            table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(path), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        table.SelectionChanged += (_, _) => picture.IsEnabled = table.SelectedItem is RoundRow { Round.OriginalFrame.State: "Saved" };
        picture.Click += (_, _) => { if (table.SelectedItem is RoundRow selected) ParcelPictureWindow.Open(this, ViewModel, new(RoundId: selected.Round.Id), selected.Barcode); };
        panel.Children.Add(table);
        load.Click += async (_, _) =>
        {
            load.IsEnabled = false;
            try
            {
                var term = search.Text.Trim() switch { "格口已取得" => "Accepted", "业务拒绝" => "Rejected", "结果未知" => "Unknown", "调用中" => "Dispatching", "" => null, var text => text };
                var result = await ViewModel.ServiceAsync(() => ViewModel.Application.RoundHistoryAsync(side.SelectedIndex == 0 ? null : side.SelectedIndex == 1 ? "left" : "right", 100, term,
                    from.SelectedDate is { } first ? new DateTimeOffset(first) : null, to.SelectedDate is { } last ? new DateTimeOffset(last.AddDays(1)) : null));
                table.ItemsSource = result.Items.Select(r => new RoundRow(r)).ToArray();
            }
            catch (Exception) { ViewModel.Message = "轮次记录未能读取，请保留原数据，稍后重试。"; }
            finally { load.IsEnabled = true; }
        };
        return new Expander { Header = "包裹轮次（含无码与未提交）", Content = panel, Margin = new Thickness(0, 0, 0, 8) };
    }
}
