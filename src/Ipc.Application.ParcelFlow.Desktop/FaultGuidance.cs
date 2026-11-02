using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    public string NextAction
    {
        get
        {
            if (!Fresh) return "状态已过期，请等待更新；不要按旧格口投递。";
            if (Snapshot is not { } s) return "等待启动检查和当前状态。";
            if (s.Operations.Any(op => new[] { op.Wcs, op.Plc, op.Capture, op.Upload }.OfType<StageFact>().Any(stage => stage.State is StageState.Unknown or StageState.Conflict)))
                return "CP-UNKNOWN · 包裹格口未确认。包裹暂放一旁，在“记录”中查看本次作业并交给维护人员核对；不会自动重发。";
            if (s.Configuration.Confirmation?.Required == true) return "CP-CONFIG · 当前配置尚未确认。资源成功后在维护页读取原证据并认证确认；无法启动时使用独立启动修复。";
            if (s.BlockingReasons.Any(r => r.StartsWith("RestoreHandoffRequired"))) return "CP-RESTORE · 数据已从备份恢复，尚未完成换机交接。请先完成换机接管并核对交接内容，完成前不能供包。";
            if (s.Capacity is { Available: false }) return "CP-STORAGE · 磁盘空间或未完成作业容量不足，受影响的投递位已暂停。请检查磁盘空间和备份，勿删除未完成的记录。";
            if (s.Health.Any(h => !h.Ready)) return "CP-HEALTH · 所需设备或服务未就绪，或状态长时间未更新。请检查对应设备，必要时进入管理模式手动重连。";
            if (s.Paused) return "供包已暂停 · 确认现场无误后点击“恢复供包”；软件暂停不是安全急停。";
            return s.Availability?.Admit == true ? "按对应投递位的本件结果作业；相机读码持续运行。" : "当前不能供包，请查看对应投递位的提示。";
        }
    }
}
