using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    public string NextAction
    {
        get
        {
            if (!Fresh) return "状态已过期 · 当前允许动作须重新读取。软件暂停入口仍可提交；不要依据旧绿灯继续放货。";
            if (Snapshot is not { } s) return "等待启动检查和当前状态。";
            if (s.Operations.Any(op => new[] { op.Wcs, op.Plc, op.Capture, op.Upload }.OfType<StageFact>().Any(stage => stage.State is StageState.Unknown or StageState.Conflict)))
                return s.ApplicationId == "sportmaster" ? "原请求结果未确认，不会自动重发。请按本位状态处理：允许重新放件时先取下包裹再重新放上；仍不能供包时，到“记录”查看原作业并通知维护人员核对。" :
                    "包裹格口未确认。包裹暂放一旁，在“记录”中选中该作业查询结果；不会重发，也不会换格口。";
            if (s.Configuration.Confirmation?.Required == true) return "当前设置尚未确认。请进入管理模式，查看诊断并完成启动检查；无法启动时使用独立启动修复。";
            if (s.BlockingReasons.Any(r => r.StartsWith("RestoreHandoffRequired"))) return "数据已从备份恢复，尚未完成换机交接。请先完成换机接管并核对交接内容，完成前不能供包。";
            if (s.Capacity is { Available: false }) return "磁盘空间或未完成作业容量不足，受影响的投递位已暂停。请检查磁盘空间和备份，勿删除未完成的记录。";
            if (s.Health.Any(h => !h.Ready)) return "所需设备或服务未就绪，或状态长时间未更新。请检查对应设备，必要时进入管理模式手动重连。";
            if (s.Paused) return "供包已暂停 · 确认现场无误后点击“恢复供包”；软件暂停不是安全急停。";
            return s.Availability?.Admit == true ? s.ApplicationId == "sportmaster" ? "同次托盘放置只请求一次格口，按本次有效格口投递。相机持续识别，取下后重新放置再发起新请求。" : "按对应投递位的本件结果作业；相机读码持续运行。" : "当前不能供包，请查看对应投递位的提示。";
        }
    }
}
