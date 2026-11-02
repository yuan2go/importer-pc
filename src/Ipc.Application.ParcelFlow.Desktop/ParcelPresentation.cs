using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public enum ParcelPresentationTone { Neutral, Pending, Accepted, Rejected, Unknown, Stale }
public sealed record ParcelStatusPresentation(string Text, string Icon, string Foreground, string Background, string Border)
{
    public ParcelPresentationTone Tone { get; init; }
    public bool NeedsAttention => Tone is ParcelPresentationTone.Rejected or ParcelPresentationTone.Unknown;
}

public static class ParcelPresentation
{
    public const string RejectedInk = "#B52F38", UnknownInk = "#895509";
    public static ParcelStatusPresentation Neutral(string text) => new(text, "○", "#536577", "#F6F8FA", "#D8E0E7") { Tone = ParcelPresentationTone.Neutral };
    public static ParcelStatusPresentation Pending(string text) => new(text, "◷", "#205FCE", "#EEF4FF", "#BDD2F3") { Tone = ParcelPresentationTone.Pending };
    public static ParcelStatusPresentation Accepted(string text) => new(text, "✓", "#147450", "#EDF7F1", "#A7D6BD") { Tone = ParcelPresentationTone.Accepted };
    public static ParcelStatusPresentation Rejected(string text) => new(text, "!", RejectedInk, "#FDF0F0", "#E7B0B4") { Tone = ParcelPresentationTone.Rejected };
    public static ParcelStatusPresentation Unknown(string text) => new(text, "?", UnknownInk, "#FFF5DF", "#E6C889") { Tone = ParcelPresentationTone.Unknown };
    public static ParcelStatusPresentation Stale() => new("状态中断 · 旧格口已失效", "⌛", "#536577", "#EDF0F3", "#9CAAB8") { Tone = ParcelPresentationTone.Stale };
    public static bool RcsTimedOut(ParcelOperation? op) => op?.State == ParcelState.WcsUnknown &&
        op.Wcs is { Request.Binding.Contract: "SPORTMASTER-RCS-SORTER-v1" } stage &&
        (stage.Failure?.StartsWith("TimeoutException:", StringComparison.Ordinal) == true ||
         stage.Replies.LastOrDefault()?.Evidence.StartsWith("RCS_REQUEST_TIMEOUT:", StringComparison.Ordinal) == true);
    public static bool RcsNotDelivered(ParcelOperation? op) => op?.State == ParcelState.WcsUnknown &&
        op.Wcs is { State: StageState.Unknown, Request.Binding.Contract: "SPORTMASTER-RCS-SORTER-v1" } stage &&
        stage.Replies.LastOrDefault() is { SenderDelivery: SenderDeliveryEvidence.ConnectionRefusedBeforeBody, TransportSucceeded: false };
    public static string? RcsException(ParcelOperation? op)
    {
        if (op?.Wcs is not { Request.Binding.Contract: "SPORTMASTER-RCS-SORTER-v1" } stage || stage.State == StageState.Conflict) return null;
        if (RcsTimedOut(op)) return OperatorCopy.TimeoutUnconfirmed;
        if (RcsNotDelivered(op)) return "RCS未连接，本次请求未送达；连接恢复后请重新扫描。";
        if (op.State == ParcelState.UnpackRequired) return "需要拆包，请查看拆包明细。";
        var reply = stage.Replies.LastOrDefault();
        if (op.State == ParcelState.WcsRejected && reply?.Destination is { } destination)
            return destination.Message ?? destination.Code ?? "RCS未返回有效格口。";
        const string prefix = "RCS_RESPONSE_ERROR: ";
        if (op.State == ParcelState.WcsUnknown && reply?.Evidence.StartsWith(prefix, StringComparison.Ordinal) == true)
            return reply.Evidence[prefix.Length..];
        return op.State == ParcelState.WcsUnknown ? "RCS 未返回有效格口，请查看异常信息。" : null;
    }
    public static ParcelStatusPresentation Operation(ParcelOperation? op, bool fresh)
    {
        if (!fresh) return Stale();
        if (op is null) return Neutral("等待包裹");
        if (RcsTimedOut(op)) return Unknown(OperatorCopy.TimeoutUnconfirmed);
        if (RcsNotDelivered(op)) return Unknown("RCS未连接 · 本次请求未送达");
        if (op.State == ParcelState.WcsRejected && RcsException(op) is { } message) return Rejected(message);
        return op.State switch
        {
            ParcelState.SlotResolved => Accepted("格口已取得"),
            ParcelState.PlcAccepted => Accepted("控制器已接受 · 落格未确认"),
            ParcelState.PhysicalCompleted => Accepted("组件报告已落格"),
            ParcelState.WcsUnknown or ParcelState.PlcUnknown or ParcelState.ImageUnknown => Unknown("格口未确认 · 勿重复上件"),
            ParcelState.WcsRejected or ParcelState.PlcRejected or ParcelState.ImageRejected => Rejected("未分配格口 · 查看原因"),
            ParcelState.Blocked => Rejected("已受阻 · 包裹暂放一旁"),
            ParcelState.WeightTimedOut => Rejected("本件称重超时"),
            ParcelState.UnpackRequired => Unknown("需拆包检查"),
            ParcelState.Abandoned => Neutral("未发送 · 已撤销"),
            ParcelState.WaitingWeight => Pending("等待本件有效重量"),
            ParcelState.Prepared => Pending("待处理"),
            ParcelState.ResolvingWcs => Pending("正在获取格口"),
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
            "NoMatchingCode" => "条码不符合规则", "ExcludedCodes" => "条码被排除规则拦截", "AmbiguousCodes" => "读到多个符合规则的条码",
            "IncompleteCandidates" => "条码读取不完整", "FilterTimeout" => "条码判断超时", "InvalidPattern" => "条码规则配置错误",
            _ => input.Observation.Kind == "NoRead" || input.Observation.Codes.Length == 0 ? "未读到条码" : input.Observation.Codes.Length > 1 ? "读到多个条码" : "条码校验未通过"
        },
        ParcelInputOutcome.Blocked => "本次扫描未受理",
        ParcelInputOutcome.Duplicate => "重复扫描 · 已忽略",
        _ => "本次扫描已受理"
    };
    public static bool IsUnadmitted(ParcelInputSnapshot? input) => input is not null && input.Outcome != ParcelInputOutcome.Admitted;
    public static string InputReason(ParcelInputSnapshot input) =>
        input.Outcome == ParcelInputOutcome.Rejected && input.BarcodeSelection is { } selection &&
        input.Reason == selection.Outcome + "; no operation or external intent created"
            ? ChineseInterfaceText.Status(selection.Outcome) + "；本件未发送。"
            : input.Reason == "Position=" + input.PositionId + "; paused/faulted/unresolved responsibility or operator not authorized"
                ? "本位当前不能受理新包裹。" : ChineseInterfaceText.Notice(input.Reason);
    public static ParcelStatusPresentation InputPresentation(ParcelInputSnapshot input) => IsInputFailure(input) ? Rejected(InputText(input)) : Neutral(InputText(input));
    public static string InputSummary(ParcelInputSnapshot? input) => input is null ? "尚无本位可信观察" :
        "最新观察：" + InputText(input) + "\n原码：" + InputCode(input) + "\n输入方式：" + (input.Observation.OperatorInput is { } manual ?
            (manual.Kind == "PdaKeyboard" ? "PDA键盘" : "手动键盘") + " · 提交主体：" + manual.Actor : "设备读码") +
        "\n观察来源：" + input.Observation.DeviceId + " · " + input.Observation.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") +
        "\n筛选：" + (input.BarcodeSelection is { } selected ? ChineseInterfaceText.Status(selected.Outcome) + " · 选中码：" + (selected.SelectedCode ?? "无") + " · 有效码 " + selected.Matches : "旧观察未记录筛选证据") + "\n关联作业：" + (input.OperationId ?? "未创建") + " · 原因：" + ChineseInterfaceText.Notice(input.Reason);
    public static string InputCode(ParcelInputSnapshot input) => input.Observation.Codes.Length == 0 ? "未识别到包裹号" : string.Join("\n", input.Observation.Codes.Take(6).Select(c => c.Length > 256 ? c[..256] + "…" : c)) + (input.Observation.Codes.Length > 6 ? "\n其余条码见原始扫描记录（共" + input.Observation.Codes.Length + "个）" : "");
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
    public string ViewportStatus => !viewportFits ? "窗口空间不足 · 请最大化或调整显示设置" : "窗口已恢复 · 请确认状态";
    public bool SetViewport(double width, double height)
    {
        var fits = double.IsFinite(width) && double.IsFinite(height) && width >= 800 && height >= 420;
        var entered = viewportFits && !fits; viewportFits = fits;
        if (Snapshot?.ApplicationId == "sportmaster") viewportRecoveryRequired = false;
        else if (!fits) viewportRecoveryRequired = true;
        RefreshFreshness(); return entered;
    }
    public void AcknowledgeViewport()
    {
        if (!CanAcknowledgeViewport) { Message = "先恢复有效客户区并确认应用已暂停；读取失败时不能解除视图保护。"; return; }
        viewportRecoveryRequired = false; RefreshFreshness();
        Message = "已确认当前状态。供包仍处于暂停，确认现场无误后点击“恢复供包”。";
    }
    public ParcelInputSnapshot? InputAt(string position) => Snapshot?.Inputs?.SingleOrDefault(x => x.PositionId == position);
    public ParcelInputSnapshot? CurrentInput => InputAt(SelectedPositionId);
    public bool CurrentInputFailed => ParcelPresentation.IsInputFailure(CurrentInput);
    public bool CurrentInputUnadmitted => ParcelPresentation.IsUnadmitted(CurrentInput);
    public string CurrentInputSummary => ParcelPresentation.InputSummary(CurrentInput);
    public string CurrentBarcode => CurrentInputUnadmitted ? ParcelPresentation.InputCode(CurrentInput!) : Current?.ParcelCode ?? "等待包裹";
    public string CurrentInputReason => CurrentInputUnadmitted ? CurrentInput!.Reason : "";
    public string CurrentResponsibility => CurrentInputUnadmitted && Current is { } op ? "上一件仍待处理：" + op.ParcelCode + " · " + ParcelPresentation.Operation(op, Fresh).Text + "（作业 " + op.Id + "，扫描于 " + op.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") + "）；该结果不属于本次扫描。" : "";
    public ParcelStatusPresentation CurrentPresentation => !Fresh ? ParcelPresentation.Stale() : CurrentInputUnadmitted ? ParcelPresentation.InputPresentation(CurrentInput!) : ParcelPresentation.Operation(Current, true);
    public ParcelStatusPresentation StatusPresentation => !Fresh ? ParcelPresentation.Stale() : Snapshot?.Operations.Any(o => new[] { o.Wcs, o.Plc, o.Capture, o.Upload }.OfType<StageFact>().Any(s => s.State is StageState.Unknown or StageState.Conflict)) == true ? ParcelPresentation.Unknown("有包裹格口未确认 · 其他投递位照常作业") : Snapshot?.BlockingReasons.Length > 0 ? ParcelPresentation.Unknown(Status) : Snapshot?.Paused == true ? ParcelPresentation.Neutral("维护中 · 暂停接收新包裹") : ParcelPresentation.Neutral(Status);
    public string StructuredDetails => structuredDetailsText;
    static string FormatStructuredDetails(ParcelOperation? op) => op is null ? "选择一条作业，查看作业、请求与 RCS 返回。" :
        "作业 " + op.Id + "\n投递位 " + (op.PositionId ?? "main") + " · 包裹号 " + op.ParcelCode + "\n受理 " + op.AdmissionId +
        (op.DebugRequest is { } debug ? "\n调试手动请求 · 轮次 " + debug.RoundId + " · 原帧 " + debug.FrameId + "\n管理主体 " + debug.ManagementActor + "；调试扫描不进入生产" : "") +
        "\n输入来源 " + (op.OperatorInput is { } manual ? (manual.Kind == "PdaKeyboard" ? "PDA键盘" : "手动键盘") + " · 提交主体 " + manual.Actor + " · 命令 " + manual.CommandId : "设备读码") +
        "\n观察时间 " + op.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") + "\n结果 " + ParcelPresentation.Operation(op, true).Text + "\n原因 " + (op.Reason ?? "无") +
        (op.OriginalFrame is { } original ? "\n\n原识码帧 " + (original.State switch { "Pending" => "后台保存中", "Saved" => "已保存原始灰度PNG", "Failed" => "保存失败", _ => "保存中断，需检查原文件" }) +
            "\n尺寸 " + original.Width + " × " + original.Height + " · 帧 " + original.Acquisition.SourceEventId +
            "\n原图文件 " + original.RelativeFile + (original.Failure is null ? "" : "\n留档说明 " + original.Failure) : "") +
        "\n\n" + string.Join("\n\n", new[] { ("WCS", op.Wcs), ("PLC", op.Plc), ("采集", op.Capture), ("上传", op.Upload) }.Where(x => x.Item2 is not null).Select(x => StageDetails(x.Item1, x.Item2!)));
    static string StageDetails(string name, StageFact stage) => name + " 请求 " + stage.Request.Id + "\n状态 " + ChineseInterfaceText.Status(stage.State.ToString()) + " · 发送尝试 " + stage.Attempts +
        "\n准备时间 " + stage.Request.PreparedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") + "\n原目标 " + stage.Request.Binding.Target + "\n原合同 " + stage.Request.Binding.Contract +
        "\n报文SHA256 " + stage.Request.PayloadHash + "\n" + (stage.Failure is { } failure ? "失败 / 未确认原因 " + failure + "\n" : "") +
        (stage.Replies.Length == 0 ? "尚无返回；已发送不代表已接受" : string.Join("\n", stage.Replies.Select(reply => "返回 " + reply.Id + " · " + reply.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") +
            "\n传输 " + Known(reply.TransportSucceeded) + " · 协议确认 " + Known(reply.ProtocolAcknowledged) + " · 业务接受 " + Known(reply.BusinessAccepted) + " · 物理完成 " + Known(reply.PhysicalCompleted) + "\n证据 " + reply.Evidence)));
    static string Known(bool? value) => value is null ? "未知" : value.Value ? "是" : "否";
}
