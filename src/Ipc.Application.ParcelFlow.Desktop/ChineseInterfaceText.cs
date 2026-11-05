using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

// Presentation only. Never write translated text into contracts, audit or drafts.
public static class ChineseInterfaceText
{
    public static string BusinessName(string id) => id switch { "sportmaster" => "SM 供包业务", "direct-plc" => "设备直连业务", "wcs-to-plc" => "调度转发业务", _ => "当前业务" };
    public static string ParameterName(string key, string? label = null) => key switch
    {
        "BarcodePattern" or "BarcodeFilter.Pattern" => "条码筛选规则",
        "BarcodeExclusionPattern" or "BarcodeFilter.ExclusionPattern" => "条码排除规则",
        "RejectMultipleCodes" or "BarcodeFilter.RejectMultipleCodes" => "拒绝多码",
        "MultiCodeSeparator" or "BarcodeFilter.MultiCodeSeparator" => "多条码分隔符",
        "decision" => "模拟业务结果",
        "reply-mode" => "模拟返回方式",
        "complete-on-query" => "查询时报告完成",
        "queue" or "QueueCapacity" => "扫码消息缓冲上限",
        "timeout" or "TimeoutMs" => "外部服务等待时间",
        "operations" or "MaximumOperations" => "待处理作业上限",
        "resident" or "ResidentHistory" => "常驻记录条数",
        "reserveMiB" or "MinimumFreeMiB" => "磁盘保留空间",
        "StationCode" => "供包台编号",
        "DiagnosticLogDays" => "诊断日志保留天数",
        "HistoryDays" => "已结数据保留天数",
        "SessionLifetimeMs" => "登录会话时长",
        "WeightRange.MinimumWeight" or "MinimumWeight" => "包裹重量下限",
        "WeightRange.MaximumWeight" or "MaximumWeight" => "包裹重量上限",
        "DisplayNameDraft" => "站点显示名称",
        "TargetEfficiencyDraft" => "目标效率",
        "MaximumAgeMs" => "测量新鲜度",
        "WaitTimeoutMs" => "测量等待期限",
        "Precision" => "重量精度",
        "ImageBytes" => "单件图像容量",
        "TotalBytes" => "图像缓存容量",
        "MaximumPending" => "图像待处理数量",
        "WeightSource" => "重量来源",
        "ConfiguredWeight" => "配置重量",
        "Unit" => "重量单位",
        "Validity" => "重量有效期",
        "BackupEnabled" => "自动备份",
        "BackupDirectory" => "备份目录",
        "BackupIntervalMinutes" => "备份间隔",
        "BackupMaximumSets" => "备份保留份数",
        "device.ip" => "相机网络地址",
        "device.serial" => "相机序列号",
        "code.encoding" => "条码字符编码",
        "reader.engine" => "识码方式",
        "code.formats" => "需要识别的条码种类",
        "code.try-invert" => "识别反色条码",
        "entity.mode" => "包裹关联方式",
        "queue.capacity" => "驱动队列容量",
        "quantity" => "请求数量",
        "specified-chute" => "指定格口",
        "chute-code" => "格口编码",
        "request-timeout-ms" => "取格口请求超时",
        "zone-id" => "仓ID",
        "project-id" => "生产线ID",
        "request-id-base" => "请求编号起点",
        "request-id-count" => "请求编号数量",
        "left-station-code" or "right-station-code" => "供包台编号",
        "left-bcr-code" => "左相机编码",
        "right-bcr-code" => "右相机编码",
        "source" => "协议来源",
        "locale" => "协议语言",
        "endpoint" => "服务地址",
        "subjects" => "允许登录的账号",
        "roles" => "账号权限",
        _ => label is not null && label.Any(c => c >= '\u4e00' && c <= '\u9fff') ? label : "其他设置"
    };
    public static string InstallationFailure(string text) => text.Contains("SM安装与维护须使用D盘", StringComparison.Ordinal)
        ? "安装所需的 D 盘存储位置未准备好，请返回安装选项重试；仍失败时由实施人员查看安装详情。"
        : ParameterMessage(text, "安装未完成，请展开安装详情查看原因，处理后返回安装选项重试。");
    public static string ParameterMessage(string text) => ParameterMessage(text, "参数检查未通过，请查看诊断详情并修正输入。");
    public static string ParameterMessage(string text, string fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        text = Notice(text);
        if (text.Contains(": required", StringComparison.Ordinal) || text.Contains(": invalid value", StringComparison.Ordinal))
            return "有参数尚未设置或格式不正确，请修正标出的字段。";
        if (text.Contains("Invalid pattern", StringComparison.Ordinal)) return "条码规则格式不正确，请检查括号、符号和转义。";
        foreach (var key in new[] { "BarcodeFilter.MultiCodeSeparator", "BarcodeFilter.RejectMultipleCodes", "MultiCodeSeparator", "RejectMultipleCodes", "BarcodeFilter.ExclusionPattern", "BarcodeFilter.Pattern", "BarcodeExclusionPattern", "BarcodePattern", "WeightRange.MinimumWeight", "WeightRange.MaximumWeight", "BackupIntervalMinutes", "BackupMaximumSets", "BackupDirectory", "BackupEnabled", "DisplayNameDraft", "TargetEfficiencyDraft", "MaximumAgeMs", "WaitTimeoutMs", "ConfiguredWeight", "MinimumWeight", "MaximumWeight", "WeightSource", "ImageBytes", "TotalBytes", "MaximumPending", "Precision", "SessionLifetimeMs", "QueueCapacity", "MaximumOperations", "ResidentHistory", "MinimumFreeMiB", "DiagnosticLogDays", "HistoryDays", "TimeoutMs", "StationCode", "device.ip", "device.serial", "entity.mode", "queue.capacity", "code.encoding", "left-station-code", "right-station-code", "left-bcr-code", "right-bcr-code", "request-id-base", "request-id-count", "specified-chute", "chute-code", "zone-id", "project-id", "quantity", "source", "locale", "reply-mode", "decision" })
            text = text.Replace(key, ParameterName(key), StringComparison.Ordinal);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^\w*Exception:\s*", "");
        // Raw property paths and exception types belong to diagnostic details.
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b[A-Z][a-z]+(?:[A-Z][A-Za-z0-9]+)+\b|\b[a-z]+(?:\.[a-zA-Z_][a-zA-Z0-9_-]*)+\b"))
            return fallback;
        return text.Any(c => c is >= '\u4e00' and <= '\u9fff') ? Notice(text) : fallback;
    }
    public static string Status(string value) => value switch
    {
        "JoinedCodes" => "多码已拼接",
        "JoinedCodeTooLong" => "拼接结果过长",
        "Authenticated" => "身份验证成功",
        "LoggedOut" => "操作员已退出",
        "Authorized" => "授权通过",
        "Unavailable" => "服务暂不可用",
        "Prepared" => "已准备",
        "Dispatching" => "发送中",
        "Unknown" => "结果未确认",
        "Rejected" => "已拒绝",
        "Accepted" => "已接受",
        "Conflict" => "存在冲突",
        "Cancelled" => "已取消",
        "Effective" => "运行中",
        "PendingRestart" => "待生效（重启后）",
        "ActivationFailed" => "启动未确认",
        "Persisted" => "已提交",
        "Validated" => "检查通过",
        "NotReady" => "尚未就绪",
        "QueryUnsupported" => "当前接口不支持结果查询",
        "NotSaved" => "未保存",
        "Selected" => "已选中唯一条码",
        "NoMatchingCode" => "没有匹配条码",
        "AmbiguousCodes" => "多个条码匹配",
        "IncompleteCandidates" => "条码不完整",
        "FilterTimeout" => "筛选超时",
        "InvalidPattern" => "筛选规则无效",
        "NoCode" => "未识别条码",
        "MultipleCodes" => "存在多个条码",
        "UntrustedObservation" => "本次读码来源、连接周期或时效无效，请重新扫描",
        "ExcludedCodes" => "命中排除规则",
        "InvalidCode" => "条码无效",
        "StrictSingleCode" => "严格单码校验",
        "WaitingWeight" => "等待有效重量",
        "ResolvingWcs" => "正在获取格口",
        "WcsUnknown" => "格口未确认",
        "WcsRejected" => "未分配格口",
        "SlotResolved" => "格口已取得",
        "SubmittingPlc" => "控制器请求处理中",
        "PlcUnknown" => "控制器结果未确认",
        "PlcRejected" => "控制器明确拒绝",
        "PlcAccepted" => "控制器已接受",
        "PhysicalCompleted" => "已报告物理完成",
        "Blocked" => "已受阻",
        "CapturingImage" => "正在采集图像",
        "UploadingImage" => "正在上传原图",
        "ImageUnknown" => "图片上传未确认",
        "ImageRejected" => "图像阶段拒绝",
        "WeightTimedOut" => "称重超时",
        "Abandoned" => "未发送 · 已撤销",
        "UnpackRequired" => "需拆包检查",
        _ => value
    };
    public static string Notice(string value)
    {
        // Match only our complete local receipts; never translate remote identifiers.
        const string noIntent = "; no operation or external intent created";
        foreach (var outcome in new[] { "NoMatchingCode", "ExcludedCodes", "AmbiguousCodes", "IncompleteCandidates", "FilterTimeout", "InvalidPattern", "NoCode", "MultipleCodes", "InvalidCode", "JoinedCodeTooLong" })
            if (value == outcome + noIntent) return Status(outcome) + "；本件未发送，请检查条码或筛选规则后重新扫描。";
        if (value == "Admitted; external result pending") return "条码已受理，正在等待本件结果。";
        if (value is "Consistent database plus configuration/receipt custody; COMPLETED.json identifies the whole recovery set" or
            "BackupCreated · Consistent database plus configuration/receipt custody; COMPLETED.json identifies the whole recovery set" or
            "BackupCreated: Consistent database plus configuration/receipt custody; COMPLETED.json identifies the whole recovery set")
            return "完整备份已完成，请将数据库和同名恢复目录一起保管；核对状态后再检查并恢复供包。";
        if (value == "BackupCreated") return "完整备份已完成";
        if (value.StartsWith("Authentication unavailable:", StringComparison.Ordinal)) return "操作员认证服务暂不可用，请稍后重试或查看诊断。";
        // Translate known application explanations without changing source facts
        // or replacing identifiers supplied by an adapter or remote service.
        foreach (var (source, text) in Notices) value = value.Replace(source, text, StringComparison.Ordinal);
        return value;
    }
    static readonly (string Source, string Text)[] Notices =
    [
        ("UntrustedObservation", "本次读码未受理，请重新扫描"),
        ("Global software pause", "供包已暂停"),
        ("SIMULATION identity; frozen only when a parcel is admitted", "模拟身份已验证，受理时记入本件作业"),
        ("Authenticated provider identity; frozen only at admission", "身份已验证，受理时记入本件作业"),
        ("Login is disabled or application stopped", "操作员登录未启用或业务已停止"),
        ("Authentication resource is under maintenance", "操作员认证服务正在维护"),
        ("Protect original unfinished operations before changing operator", "请先处理原有未完成作业，再更换操作员"),
        ("Invalid identity input", "操作员账号或口令格式不正确"),
        ("Authentication rejected; no operator changed", "身份验证未通过，原操作员保持不变"),
        ("Authentication evidence is invalid", "身份凭据无效，请重新验证"),
        ("No session or original unfinished operations remain protected", "没有可退出的登录，或仍有未完成的作业，请先处理"),
        ("New admission requires authentication; old snapshots retained", "新包裹入场前须重新验证身份，历史记录保留"),
        ("Production NotReady: exact deployment qualification missing/expired", "生产部署检查未通过或已过期，请联系管理人员"),
        ("Position resource maintenance in progress; original intent retained", "本位设备维护中"),
        ("Unresolved capacity exhausted", "待处理包裹已达上限，请维护人员处理积压"),
        ("Software admission paused; this is not an emergency stop", "软件已暂停接收新包裹（不是安全急停）"),
        ("Operator authentication required for new admission", "请先登录操作员"),
        ("Reader confirmation missing/expired", "相机状态未确认"),
        ("Original stage outcome or receipt responsibility is unresolved", "上一件格口未确认，请通知维护人员处理"),
        ("Current entity main result is incomplete", "当前包裹仍在获取格口"),
        ("Position dependency/health or original responsibility blocked", "本位设备未就绪或上一件待处理"),
        ("Transport/protocol facts do not establish business acceptance", "RCS 未明确返回结果，格口未确认。"),
        ("Contradictory or invalid original-stage receipt", "RCS 返回的结果互相矛盾，格口未确认。"),
        ("Application command committed", "操作已记录"),
        ("Unsupported command or an application guard is closed", "命令不受支持或当前执行条件未满足"),
        ("Unresolved operation capacity exhausted; original responsibilities cannot be archived", "未完成作业已达上限，请先处理未完成的作业"),
        ("Temporary image capacity/backlog exhausted; existing originals retained", "临时图片空间或待处理队列已满"),
        ("ConfigurationConfirmationRequired: current resources must start successfully before admission", "设备或服务未启动成功，暂不能供包"),
        ("Overload: rejected callback; inspect parcel and acknowledge while paused", "输入过载，已拒收回调；暂停后检查包裹并确认"),
        ("IngressInterrupted:left; pause, enter management mode and inspect original input responsibilities", "左位有未处理的扫码；请暂停供包，进入管理模式，在“异常作业处理”中逐笔处理"),
        ("IngressInterrupted:right; pause, enter management mode and inspect original input responsibilities", "右位有未处理的扫码；请暂停供包，进入管理模式，在“异常作业处理”中逐笔处理"),
        ("IngressInterrupted:main; pause, enter management mode and inspect original input responsibilities", "有未处理的扫码；请暂停供包，进入管理模式，在“异常作业处理”中逐笔处理"),
        ("RestoreHandoffRequired: keep isolated; verify same-machine source retirement and authenticate maintenance; cross-machine evidence still required", "数据已从备份恢复，需完成换机交接后才能供包"),
        ("MaintenanceInProgress:", "维护进行中："), ("Capability unavailable: ", "能力不可用："),
        ("Measurement missing/expired for admission ", "本件测量数据缺失或已过期："),
        ("Application stopped", "业务已停止"), ("Refresh the current application projection", "请重新读取当前业务状态"),
        ("Stable command identity is required", "需要稳定的命令标识")
    ];
    public static string Message(string value)
    {
        var parts = value.Split(" · ", 2);
        return Notice(Status(parts[0])) + (parts.Length == 2 ? " · " + Notice(parts[1]) : "");
    }
    public static string Choice(object? value) => value switch
    {
        RunMode.Simulation => "模拟模式",
        RunMode.Production => "生产模式",
        WeightSource.Disabled => "不使用重量",
        WeightSource.Configured => "配置重量",
        WeightSource.Measured => "实测重量",
        WeightUnit.Kg => "千克",
        WeightUnit.G => "克",
        WeightValidity.UntilDispatch => "发送前持续有效",
        WeightValidity.AtAssociation => "关联时有效",
        UploadStrategy.BeforeMain => "主请求前上传",
        UploadStrategy.Attached => "附加上传",
        "external-trigger" => "外部触发（一包一次）",
        "continuous" => "连续采集",
        "line" => "每行一个包裹",
        "observe-only" => "仅观察",
        "read-result" => "识别结果交给供包台处理",
        "zxing-cpp" => "软件识别",
        "mvid-native" => "海康原生识码",
        "trigger" => "触发关联",
        "utf-8" => "通用字符编码",
        "ascii" => "基础英文字符编码",
        "existing" => "沿用设备设置",
        "on" => "要求设备已开启",
        "off" => "要求设备已关闭",
        "true" => "启用",
        "false" => "关闭",
        "normal" => "正常返回",
        "unavailable" => "不可用",
        "lose-after-commit" => "提交后未收到返回",
        "accept" => "接受",
        "reject" => "拒绝",
        _ => value?.ToString() ?? "未设置"
    };
    public static string CapabilityKind(string kind) => kind switch
    {
        "login" => "身份认证",
        "weight" => "称重",
        "camera" => "图像采集",
        "upload" => "图像上传",
        "integration" => "上游服务",
        "plc" => "控制器",
        "observation" => "读码来源",
        _ => "组件实例"
    };
}
