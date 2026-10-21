using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

// Presentation only. Never write translated text into contracts, audit or drafts.
public static class ChineseInterfaceText
{
    public static string Status(string value) => value switch
    {
        "Prepared" => "已准备", "Dispatching" => "发送中", "Unknown" => "结果待核对",
        "Rejected" => "已拒绝", "Accepted" => "已接受", "Conflict" => "存在冲突", "Cancelled" => "已取消",
        "Effective" => "运行中", "PendingRestart" => "已保存待重启", "ActivationFailed" => "启动未确认",
        "Persisted" => "已提交", "Validated" => "检查通过", "NotReady" => "尚未就绪",
        "NotSaved" => "未保存", "Selected" => "已选中唯一条码", "NoMatchingCode" => "没有匹配条码",
        "AmbiguousCodes" => "多个条码匹配", "IncompleteCandidates" => "候选不完整", "FilterTimeout" => "筛选超时",
        "InvalidPattern" => "筛选规则无效", "NoCode" => "未识别条码", "MultipleCodes" => "存在多个条码",
        "ExcludedCodes" => "命中排除规则", "InvalidCode" => "条码无效", "LegacySingleCode" => "旧版严格单码兼容",
        _ => value
    };
    public static string Notice(string value)
    {
        // Translate known application explanations without changing source facts
        // or replacing identifiers supplied by an adapter or remote service.
        foreach (var (source, text) in Notices) value = value.Replace(source, text, StringComparison.Ordinal);
        return value;
    }
    static readonly (string Source, string Text)[] Notices =
    [
        ("Software admission paused; this is not an emergency stop", "软件已暂停新入场；这不是安全急停"),
        ("Operator authentication required for new admission", "新入场前需要操作员认证"),
        ("Reader confirmation missing/expired", "读码设备确认缺失或已过期"),
        ("Original stage outcome or receipt responsibility is unresolved", "原阶段结果或回执责任尚未核对"),
        ("Unresolved operation capacity exhausted; original responsibilities cannot be archived", "未清偿作业容量已满；原责任不能归档"),
        ("Temporary image capacity/backlog exhausted; existing originals retained", "临时图像空间或待处理容量已满；原件保留"),
        ("ConfigurationConfirmationRequired: inspect the preserved legacy envelope; successful resource startup and authenticated current-revision confirmation are required", "需要确认配置：检查保留的原配置账；资源启动成功并经认证确认当前修订后才能继续"),
        ("Overload: rejected callback; inspect parcel and acknowledge while paused", "输入过载，已拒收回调；暂停后检查包裹并确认"),
        ("RestoreHandoffRequired: keep isolated; verify same-machine source retirement and authenticate maintenance; cross-machine evidence still required", "需要核对恢复交接：保持隔离，核对原机来源退出并完成维护认证；换机仍需独立证据"),
        ("MaintenanceInProgress:", "维护进行中："), ("Capability unavailable: ", "能力不可用："),
        ("Measurement missing/expired for admission ", "本次准入的测量缺失或已过期："),
        ("Application stopped", "业务已停止"), ("Refresh the current application projection", "请重新读取当前业务状态"),
        ("Stable command identity is required", "需要稳定的命令标识")
    ];
    public static string Message(string value)
    {
        var parts = value.Split(" · ", 2);
        return Notice(Status(parts[0]) + (parts.Length == 2 ? " · " + parts[1] : ""));
    }
    public static string Choice(object? value) => value switch
    {
        RunMode.Simulation => "模拟模式", RunMode.Production => "生产模式",
        WeightSource.Disabled => "不使用重量", WeightSource.Configured => "配置重量", WeightSource.Measured => "实测重量",
        WeightUnit.Kg => "千克", WeightUnit.G => "克",
        WeightValidity.UntilDispatch => "发送前持续有效", WeightValidity.AtAssociation => "关联时有效",
        UploadStrategy.BeforeMain => "主请求前上传", UploadStrategy.Attached => "附加上传",
        "observe-only" => "仅观察", "trigger" => "触发关联", "utf-8" => "通用字符编码", "ascii" => "基础英文字符编码",
        "true" => "启用", "false" => "关闭", "normal" => "正常返回", "unavailable" => "不可用",
        "lose-after-commit" => "提交后丢失回执", "accept" => "接受", "reject" => "拒绝", _ => value?.ToString() ?? "未设置"
    };
    public static string CapabilityKind(string kind) => kind switch
    {
        "login" => "身份认证", "weight" => "称重", "camera" => "图像采集", "upload" => "图像上传",
        "integration" => "上游服务", "plc" => "控制器", "observation" => "读码来源", _ => "组件实例"
    };
}
