using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    public Func<Task>? RequestActivationAsync { get; set; }
    public Func<Task>? RequestRetentionAsync { get; set; }
    public string BackupStatusText => !IsManagementMode ? "进入管理模式后查看备份状态。" : Snapshot?.BackupScheduleStatus is not { } status ? "尚未取得备份运行状态；计划保存不等于备份成功。" :
        "当前运行计划：" + (status.Enabled ? "已启用" : "已关闭") + " · " + (status.Outcome == "BackupCreated" ? "最近检查成功" : status.Outcome.StartsWith("Failed:", StringComparison.Ordinal) ? "最近检查失败" : status.Outcome.StartsWith("Pending:", StringComparison.Ordinal) ? "等待执行条件" : "尚未执行") +
        "\n最近检查：" + (status.CheckedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "无") + " · 下次到期：" + (status.NextDue?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "无") +
        "\n最近成功：" + (status.LastSuccessfulAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "尚无成功记录") + "\n成功位置：" + (status.LastSuccessfulBackup ?? "尚无") +
        "\n当前目录：" + status.Directory + " · 份数上限：" + status.MaximumSets + "；达到上限停止新增，保留原恢复集。\n" + status.Detail;
    public bool CanRequestActivation => RequestActivationAsync is not null && !Busy && IsManagementMode && Fresh && Snapshot?.Configuration.Pending is not null && !HasUnconfirmedConfigurationSubmission;
    public async Task CheckActivationAsync()
    {
        await RequireManagementModeAsync(); RequireFreshSnapshot();
        if (Snapshot?.Configuration.Pending is null || HasUnconfirmedConfigurationSubmission) throw new InvalidOperationException("先核对原保存结果，只有已保存候选可进入生效流程。");
        await PauseAsync(); await RefreshAsync(); await RequireManagementModeAsync();
        if (!Fresh || Snapshot?.Paused != true || Snapshot.Availability?.Configure != true) throw new InvalidOperationException("暂停或外呼排空尚未确认。原实例继续保有资源，请稍后核对。");
    }
    public string ExitImpact(bool activation) =>
        (activation ? "将检查并停止原实例，再从原启动契约重新打开。新配置须启动读回后才算生效。" : "将停止整个业务实例并关闭其全部辅助窗口。") +
        "\n当前读取：" + (Fresh ? "状态有效" : "状态已失效，不能推断原请求结果") +
        "\n待生效配置：" + (Snapshot?.HasPendingConfiguration is { } pending ? pending ? "已保存，重连不会应用" : "无" : IsManagementMode ? Snapshot?.Configuration.Pending is null ? "无" : "已保存，重连不会应用" : "状态尚未披露，不能推断为无") +
        "\n本次可见作业：" + Operations.Length + "；结果未知阶段：" + Operations.Sum(o => new[] { o.Wcs?.State, o.Plc?.State, o.Upload?.State, o.Capture?.State }.Count(s => s == StageState.Unknown)) +
        "\n停止软件不会取消已发送请求或清偿原责任；历史责任以原账为准。草稿在下一步逐窗保护，停止失败保留原实例，不开第二实例。软件暂停不是安全急停。";
}
