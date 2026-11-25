using System.Windows;
using System.Windows.Controls;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.Sportmaster.Desktop;

public sealed partial class OperationsWorkspaceControl
{
    async Task ConfirmRemotePositionRecovery(string position)
    {
        await model.RequireManagementModeAsync();
        if (model.Application is not ISmRemoteRecovery service) return;
        var snapshot = await model.Application.SnapshotAsync();
        var lane = snapshot.Positions?.SingleOrDefault(p => p.Id == position);
        if (lane?.SupplyControl?.Mode != StationPositionMode.Disabled || lane.Arrival is not { } arrival)
            throw new InvalidOperationException("请先停用要核对的投递位，并等待本轮调用、回执和撤车确认。");
        var dialog = new Window
        {
            Owner = Window.GetWindow(this), Title = position == "left" ? "核对左位未决请求" : "核对右位未决请求",
            Width = 620, Height = 420, MinWidth = 520, MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResize,
            FontFamily = FontFamily, FontSize = FontSize
        };
        var layout = new DockPanel { Margin = new(24) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, MinHeight = 44, MinWidth = 88, Margin = new(0, 0, 12, 0) };
        var confirm = new Button { Content = "记录核对结果", MinHeight = 44, MinWidth = 128 };
        footer.Children.Add(cancel); footer.Children.Add(confirm); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var body = new StackPanel();
        body.Children.Add(Text("请在客户RCS中核对：原取格口请求已完成绑定或已被明确取消，不会再迟到绑定当前小车；旧心跳成员变更均已结束，当前停用状态已确认。"));
        body.Children.Add(Text("软件超时和新的心跳成功不能证明上述事实。填写客户日志、工单或核对人员及结论；没有外部依据请取消，保留本位待核对。"));
        var evidence = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 96, MaxLength = 2048, Margin = new(0, 12, 0, 0) };
        body.Children.Add(evidence);
        var feedback = Text(""); body.Children.Add(feedback);
        layout.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        dialog.Content = layout;
        confirm.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(evidence.Text)) { feedback.Text = "请填写实际外部核对依据。"; evidence.Focus(); return; }
            dialog.DialogResult = true;
        };
        dialog.Loaded += (_, _) => cancel.Focus();
        if (dialog.ShowDialog() != true) return;
        var receipt = await service.ConfirmRemoteRecoveryAsync(new(Wire.Id(), snapshot.RuntimeId, position, arrival.Generation,
            evidence.Text.Replace("\r", " ").Replace("\n", " ").Trim())
        { ExpectedControlRevision = lane.SupplyControl.Revision, ExpectedTargetOperation = arrival.TargetOperation, ExpectedRecoveryToken = arrival.RecoveryToken }, CancellationToken.None);
        model.Message = receipt.Reason;
        await model.RefreshAsync();
    }
}
