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
                return "CP-UNKNOWN · 原请求结果待核对。保留原实体，打开记录选择对应原请求；仅按合同允许的查询核对，不重发、不换目标。";
            if (s.Configuration.Confirmation?.Required == true) return "CP-CONFIG · 当前配置尚未确认。资源成功后在维护页读取原证据并认证确认；无法启动时使用独立启动修复。";
            if (s.BlockingReasons.Any(r => r.StartsWith("RestoreHandoffRequired"))) return "CP-RESTORE · 恢复根仍隔离。先完成原机目录接管并核对交接；换机身份、资格和外部编号范围缺失时继续阻断。";
            if (s.Capacity is { Available: false }) return "CP-STORAGE · 存储或未结容量不足。暂停受影响入场，保留原库/原件；检查完整备份和目标卷，不删除未决记录。";
            if (s.Health.Any(h => !h.Ready)) return "CP-HEALTH · 所需设备或服务未就绪/健康过期。检查具体来源与时间，授权受控重连后重新检查；连通不等于业务允许。";
            if (s.Paused) return "软件暂停新入场 · 核对现场及当前业务守卫后再显式恢复；此操作不是安全急停。";
            return s.Availability?.Admit == true ? "按当前投递位提示连续作业。取得格口/请求接受与实体完成分别保留；设备读码与页面焦点无关。" : "当前业务守卫仍有阻断，查看对应位/原请求及详细原因；页面切换不会重启连接或清除责任。";
        }
    }
}
