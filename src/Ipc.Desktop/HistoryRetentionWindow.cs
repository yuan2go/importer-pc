using System.Windows;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;

namespace Ipc.Desktop;

// Offline task over the existing kernel; never starts a business instance or device.
sealed class HistoryRetentionWindow : StartupWorkflowWindow
{
    readonly IStartupRetentionMaintenance service;
    StartupRetentionPlan? plan;
    readonly System.Windows.Controls.TextBox destination;
    CancellationTokenSource? planning;
    public HistoryRetentionWindow(IStartupRetentionMaintenance service, IManagementMode management) : base("清理过期记录")
    {
        this.service = service;
        Note("设置保留天数后，先预检再清理。清理前请暂停供包并完全退出作业窗口，进入管理模式；系统会先完整备份，再删除超过保留天数且已完成的记录。未处理完的作业不会删除。预检时可按 Esc 取消。");
        Action("进入管理模式…", async () =>
        {
            if (!(await management.ManagementModeStatusAsync()).Authorized) new ManagementModeWindow(management) { Owner = this }.ShowDialog();
            Message((await management.ManagementModeStatusAsync()).Authorized ? "已进入管理模式，可以继续。" : "尚未进入管理模式。请输入管理密码后继续。");
        }, readOnly: true);
        destination = Field("清理前的完整备份位置（数据库及同名 .recovery 目录）", "", _ => { plan = null; });
        Action("选择完整备份文件…", () =>
        {
            var picker = new Microsoft.Win32.SaveFileDialog { Filter = "完整恢复集数据库|*.db", FileName = "pre-retention-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db" };
            if (picker.ShowDialog(this) == true) destination.Text = picker.FileName;
            return Task.CompletedTask;
        });
        Action("预检", async () =>
        {
            plan = null; var value = destination.Text;
            using var cancellation = new CancellationTokenSource(); planning = cancellation;
            try { plan = await Task.Run(() => service.PlanRetentionAsync(value, cancellation.Token)); }
            finally { planning = null; }
            Message("预检完成，尚未清理。保留 " + plan.Days + " 天。\n目标：" + plan.BackupDestination + "\n" + plan.Summary);
        });
        Action("备份并清理过期记录…", async () =>
        {
            var original = plan ?? throw new InvalidOperationException("先预检当前计划");
            if (destination.Text != original.BackupDestination) throw new InvalidOperationException("备份目标已改变，重新预检");
            if (MessageBox.Show(this, "将按预检结果清理，保留 " + original.Days + " 天。\n完整备份：" + original.BackupDestination + "\n未处理完的作业不会删除。", "确认清理过期记录", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            // Until a trusted terminal arrives, only inspection may be used, even on a known helper failure.
            UnknownOutcome = true;
            var result = await Task.Run(() => service.ExecuteRetentionAsync(original));
            UnknownOutcome = false; Message(result.Detail); plan = null;
        });
        Action("查看上次清理结果", async () =>
        {
            var original = plan ?? throw new InvalidOperationException("没有需要查看的清理记录");
            var result = await Task.Run(() => service.InspectRetentionAsync(original));
            Message(result.Detail);
            if (result.Status is "Completed" or "Interrupted") { UnknownOutcome = false; plan = null; }
        }, readOnly: true);
        Action("关闭", () => { Busy = false; Close(); return Task.CompletedTask; }, readOnly: true);
        Closing += (_, _) => planning?.Cancel();
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape && planning is not null) { e.Handled = true; planning.Cancel(); } };
    }
    protected override void OperationCompleted() => destination.IsEnabled = !UnknownOutcome;
    protected override void OperationFailed(Exception error)
    { if (error is OperationCanceledException && !UnknownOutcome) Message("预检已取消，原数据未修改。可重新选择目标并预检。"); else base.OperationFailed(error); }
}
