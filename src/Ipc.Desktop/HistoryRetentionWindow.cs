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
    public HistoryRetentionWindow(IStartupRetentionMaintenance service, IManagementMode management) : base("已结历史到期维护")
    {
        this.service = service;
        Note("保存保留天数不执行清理。请先暂停并完全退出作业窗口，进入管理模式后预检。先完整备份，再按原内核删除到期已结明细；未决责任和永久身份保留，不自动压缩磁盘。预检期间可按Esc或关闭窗口取消读取；提交后等待原回执。");
        Action("进入 / 核对管理模式…", async () =>
        {
            if (!(await management.ManagementModeStatusAsync()).Authorized) new ManagementModeWindow(management) { Owner = this }.ShowDialog();
            Message((await management.ManagementModeStatusAsync()).Authorized ? "管理模式有效；请继续核对原计划。" : "尚未授权；原计划及输入保留。通过现有管理密码进入后再继续。");
        }, readOnly: true);
        destination = Field("新的完整备份文件（数据库与同名恢复目录）", "", _ => { plan = null; });
        Action("选择完整备份文件…", () =>
        {
            var picker = new Microsoft.Win32.SaveFileDialog { Filter = "完整恢复集数据库|*.db", FileName = "pre-retention-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db" };
            if (picker.ShowDialog(this) == true) destination.Text = picker.FileName;
            return Task.CompletedTask;
        });
        Action("预检原账与完整备份计划", async () =>
        {
            plan = null; var value = destination.Text;
            using var cancellation = new CancellationTokenSource(); planning = cancellation;
            try { plan = await Task.Run(() => service.PlanRetentionAsync(value, cancellation.Token)); }
            finally { planning = null; }
            Message("预检完成，尚未执行。保留 " + plan.Days + " 天。\n目标：" + plan.BackupDestination + "\n" + plan.Summary);
        });
        Action("完整备份并清理到期已结历史…", async () =>
        {
            var original = plan ?? throw new InvalidOperationException("先预检当前计划");
            if (destination.Text != original.BackupDestination) throw new InvalidOperationException("备份目标已改变，重新预检");
            if (MessageBox.Show(this, "将执行当前已审阅的固定计划，保留 " + original.Days + " 天。\n完整备份：" + original.BackupDestination + "\n未决及永久责任继续保留；不自动恢复作业。", "确认已结历史到期维护", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            // Until a trusted terminal arrives, only inspection may be used, even on a known helper failure.
            UnknownOutcome = true;
            var result = await Task.Run(() => service.ExecuteRetentionAsync(original));
            UnknownOutcome = false; Message(result.Detail); plan = null;
        });
        Action("只读核对原维护回执", async () =>
        {
            var original = plan ?? throw new InvalidOperationException("本窗没有待核对原计划；保留原数据及维护记录");
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
