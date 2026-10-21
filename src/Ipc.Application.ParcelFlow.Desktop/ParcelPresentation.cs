using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed record ParcelStatusPresentation(string Text, string Icon, string Foreground, string Background, string Border);

public static class ParcelPresentation
{
    public static ParcelStatusPresentation Neutral(string text) => new(text, "○", "#536577", "#F6F8FA", "#D8E0E7");
    public static ParcelStatusPresentation Pending(string text) => new(text, "◷", "#205FCE", "#EEF4FF", "#BDD2F3");
    public static ParcelStatusPresentation Accepted(string text) => new(text, "✓", "#147450", "#EDF7F1", "#A7D6BD");
    public static ParcelStatusPresentation Rejected(string text) => new(text, "!", "#B52F38", "#FDF0F0", "#E7B0B4");
    public static ParcelStatusPresentation Unknown(string text) => new(text, "?", "#895509", "#FFF5DF", "#E6C889");
    public static ParcelStatusPresentation Stale() => new("旧状态已失效 · 等待有效快照", "⌛", "#536577", "#EDF0F3", "#9CAAB8");
    public static ParcelStatusPresentation Operation(ParcelOperation? op, bool fresh)
    {
        if (!fresh) return Stale();
        if (op is null) return Neutral("等待包裹入场");
        return op.State switch
        {
            ParcelState.SlotResolved => Accepted("上游格口已取得"),
            ParcelState.PlcAccepted => Accepted("控制器业务已接受 · 物理完成待核对"),
            ParcelState.PhysicalCompleted => Accepted("组件报告物理完成 · 见原回执"),
            ParcelState.WcsUnknown or ParcelState.PlcUnknown or ParcelState.ImageUnknown => Unknown("结果待核对 · 请勿重复放货"),
            ParcelState.WcsRejected or ParcelState.PlcRejected or ParcelState.ImageRejected => Rejected("业务明确拒绝 · 查看原始原因"),
            ParcelState.Blocked => Rejected("作业阻断 · 保留现场包裹"),
            ParcelState.WeightTimedOut => Rejected("本件称重超时"),
            ParcelState.UnpackRequired => Unknown("需拆包 · 本位等待检查处置"),
            ParcelState.Abandoned => Neutral("未发送意图已处置 · 原事实保留"),
            ParcelState.WaitingWeight => Pending("等待本件有效重量"),
            ParcelState.Prepared => Pending("原阶段待执行"),
            ParcelState.ResolvingWcs => Pending("上游格口处理中"),
            ParcelState.SubmittingPlc => Pending("控制器请求处理中"),
            ParcelState.CapturingImage => Pending("正在采集本件图像"),
            ParcelState.UploadingImage => Pending("正在上传原图"),
            _ => Pending("处理中 · " + op.State)
        };
    }
    public static bool IsInputFailure(ParcelInputSnapshot? input) => input?.Outcome is ParcelInputOutcome.Rejected or ParcelInputOutcome.Blocked;
    public static string InputText(ParcelInputSnapshot input) => input.Outcome switch
    {
        ParcelInputOutcome.Rejected => input.BarcodeSelection?.Outcome switch
        {
            "NoMatchingCode" => "条码筛选拒绝 · 没有匹配码", "ExcludedCodes" => "条码筛选拒绝 · 命中排除规则", "AmbiguousCodes" => "条码筛选拒绝 · 多码歧义",
            "IncompleteCandidates" => "候选不完整 · 拒绝准入", "FilterTimeout" => "条码筛选超时 · 拒绝准入", "InvalidPattern" => "筛选规则无效 · 拒绝准入",
            _ => input.Observation.Kind == "NoRead" || input.Observation.Codes.Length == 0 ? "本次识别失败 · 未取得包裹号" : input.Observation.Codes.Length > 1 ? "本次识别失败 · 多个候选码" : "本次识别未通过校验"
        },
        ParcelInputOutcome.Blocked => "本次输入未准入 · 请核对阻断原因",
        ParcelInputOutcome.Duplicate => "重复输入 · 未创建新作业",
        _ => "本次输入已准入"
    };
    public static bool IsUnadmitted(ParcelInputSnapshot? input) => input is not null && input.Outcome != ParcelInputOutcome.Admitted;
    public static ParcelStatusPresentation InputPresentation(ParcelInputSnapshot input) => IsInputFailure(input) ? Rejected(InputText(input)) : Neutral(InputText(input));
    public static string InputSummary(ParcelInputSnapshot? input) => input is null ? "尚无本位可信观察" :
        "最新观察：" + InputText(input) + "\n原码：" + InputCode(input) + "\n观察来源：" + input.Observation.DeviceId + " · " + input.Observation.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") +
        "\n筛选：" + (input.BarcodeSelection is { } selected ? ChineseInterfaceText.Status(selected.Outcome) + " · 选中码：" + (selected.SelectedCode ?? "无") + " · 有效码 " + selected.Matches : "旧观察未记录筛选证据") + "\n关联原作业：" + (input.OperationId ?? "未创建") + " · 原因：" + ChineseInterfaceText.Notice(input.Reason);
    public static string InputCode(ParcelInputSnapshot input) => input.Observation.Codes.Length == 0 ? "未识别到包裹号" : string.Join("\n", input.Observation.Codes.Take(6).Select(c => c.Length > 256 ? c[..256] + "…" : c)) + (input.Observation.Codes.Length > 6 ? "\n其余候选见原始输入证据（共" + input.Observation.Codes.Length + "个）" : "");
    public static string Health(CapabilityHealth health, DateTimeOffset now)
    {
        var localOnly = health.Detail.Contains("local lifecycle", StringComparison.OrdinalIgnoreCase);
        var valid = health.Ready && health.ConfirmedAt <= now && health.ValidUntil > now;
        var state = localOnly ? valid ? "客户端已启动，上游未确认" : "客户端未就绪，上游未确认" : valid ? "就绪（见来源证据）" : "未就绪或确认已过期";
        return health.InstanceId + " · " + state + " · " + health.ConfirmedAt.ToLocalTime().ToString("HH:mm:ss") + "\n来源证据：" + health.Detail;
    }
}

public partial class ParcelViewModel
{
    bool viewportFits = true, viewportRecoveryRequired, isCompact;
    public bool IsCompact { get => isCompact; set { if (isCompact == value) return; isCompact = value; Changed(); } }
    public bool ViewportFits => viewportFits;
    public bool ViewportReady => viewportFits && !viewportRecoveryRequired;
    public bool CanUseWorkspace => Fresh && ViewportReady;
    public bool CanResumeInViewport => CanUseWorkspace && CanResume;
    public bool CanAcknowledgeViewport => Fresh && viewportFits && Snapshot?.Paused == true;
    public System.Windows.Visibility ViewportWarningVisibility => ViewportReady ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    public string ViewportStatus => !viewportFits ? "窗口空间不足 · 请最大化或调整显示设置" : "空间已恢复 · 请核对状态并确认视图";
    public bool SetViewport(double width, double height)
    {
        var fits = double.IsFinite(width) && double.IsFinite(height) && width >= 800 && height >= 420;
        var entered = viewportFits && !fits; viewportFits = fits;
        if (!fits) viewportRecoveryRequired = true;
        RefreshFreshness(); return entered;
    }
    public void AcknowledgeViewport()
    {
        if (!CanAcknowledgeViewport) { Message = "先恢复有效客户区并确认应用已暂停；读取失败时不能解除视图保护。"; return; }
        viewportRecoveryRequired = false; RefreshFreshness();
        Message = "视图已确认；业务保持暂停，请核对现场后显式恢复新入场。";
    }
    public ParcelInputSnapshot? InputAt(string position) => Snapshot?.Inputs?.SingleOrDefault(x => x.PositionId == position);
    public ParcelInputSnapshot? CurrentInput => InputAt(SelectedPositionId);
    public bool CurrentInputFailed => ParcelPresentation.IsInputFailure(CurrentInput);
    public bool CurrentInputUnadmitted => ParcelPresentation.IsUnadmitted(CurrentInput);
    public string CurrentInputSummary => ParcelPresentation.InputSummary(CurrentInput);
    public string CurrentBarcode => CurrentInputUnadmitted ? ParcelPresentation.InputCode(CurrentInput!) : Current?.ParcelCode ?? "等待包裹";
    public string CurrentInputReason => CurrentInputUnadmitted ? CurrentInput!.Reason : "";
    public string CurrentResponsibility => CurrentInputUnadmitted && Current is { } op ? "当前保留原作业：" + op.ParcelCode + " · " + op.Id + " · " + ParcelPresentation.Operation(op, Fresh).Text + "；原观察 " + op.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") + "；其结果不属于本次观察。" : "";
    public ParcelStatusPresentation CurrentPresentation => !Fresh ? ParcelPresentation.Stale() : CurrentInputUnadmitted ? ParcelPresentation.InputPresentation(CurrentInput!) : ParcelPresentation.Operation(Current, true);
    public ParcelStatusPresentation StatusPresentation => !Fresh ? ParcelPresentation.Stale() : Snapshot?.Operations.Any(o => new[] { o.Wcs, o.Plc, o.Capture, o.Upload }.OfType<StageFact>().Any(s => s.State is StageState.Unknown or StageState.Conflict)) == true ? ParcelPresentation.Unknown("有原请求待核对 · 其他投递位按各自守卫作业") : Snapshot?.BlockingReasons.Length > 0 ? ParcelPresentation.Unknown(Status) : Snapshot?.Paused == true ? ParcelPresentation.Neutral("软件已暂停新入场") : ParcelPresentation.Neutral(Status);
    public string StructuredDetails => Selected is not { } op ? "选择一条作业查看原作业、请求与回执。" :
        "作业 " + op.Id + "\n投递位 " + (op.PositionId ?? "main") + " · 包裹号 " + op.ParcelCode + "\n实体准入 " + op.AdmissionId +
        "\n观察时间 " + op.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") + "\n结果 " + ParcelPresentation.Operation(op, true).Text + "\n原因 " + (op.Reason ?? "无") +
        "\n\n" + string.Join("\n\n", new[] { ("WCS", op.Wcs), ("PLC", op.Plc), ("采集", op.Capture), ("上传", op.Upload) }.Where(x => x.Item2 is not null).Select(x => StageDetails(x.Item1, x.Item2!)));
    static string StageDetails(string name, StageFact stage) => name + " 请求 " + stage.Request.Id + "\n状态 " + ChineseInterfaceText.Status(stage.State.ToString()) + " · 发送尝试 " + stage.Attempts +
        "\n准备时间 " + stage.Request.PreparedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") + "\n原目标 " + stage.Request.Binding.Target + "\n原合同 " + stage.Request.Binding.Contract +
        "\n报文SHA256 " + stage.Request.PayloadHash + "\n" + (stage.Failure is { } failure ? "失败 / 待核对原因 " + failure + "\n" : "") +
        (stage.Replies.Length == 0 ? "尚无回执；发送不等于接受" : string.Join("\n", stage.Replies.Select(reply => "回执 " + reply.Id + " · " + reply.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") +
            "\n传输 " + Known(reply.TransportSucceeded) + " · 协议确认 " + Known(reply.ProtocolAcknowledged) + " · 业务接受 " + Known(reply.BusinessAccepted) + " · 物理完成 " + Known(reply.PhysicalCompleted) + "\n证据 " + reply.Evidence)));
    static string Known(bool? value) => value is null ? "未知" : value.Value ? "是" : "否";
}
