using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

// Read-only wording and navigation hints. Application remains the command authority.
public sealed partial class SupplyStationViewModel
{
    public string[] MissingConnections
    {
        get
        {
            if (Snapshot is not { } s) return [];
            var missing = new List<string>(); var c = s.Configuration.Effective;
            if (c.Wcs is null || ParcelConnectionSetup.IsDeferred(c, c.Wcs.InstanceId)) missing.Add("RCS");
            return missing.ToArray();
        }
    }
    public bool ShowSetupGuidance => Fresh && MissingConnections.Length > 0 &&
        !Left.HasCurrentInput && !Right.HasCurrentInput && Snapshot?.Positions?.Any(p => p.Admit) != true &&
        Snapshot?.Operations.Any(o => o.Wcs?.State is StageState.Unknown or StageState.Conflict) != true;
    public string SetupHeading => $"还差 {MissingConnections.Length} 项连接配置";
    public string SetupItems => string.Join("、", MissingConnections);
    public string SetupInstruction => "配置并启用 RCS 后，可手动输入条码取格口；不要求配置或连接相机。";
    public string OperatorStatus => !Fresh ? "状态读取中断" : Snapshot?.Paused == true ? "等待维护完成" :
        Snapshot?.Positions is { } positions && positions.Any(p => p.Admit)
            ? positions.All(p => p.Admit) ? "可以供包" : string.Join("、", positions.Where(p => p.Admit).Select(p => p.Id == "left" ? "左投递位" : p.Id == "right" ? "右投递位" : p.Id)) + "可以供包" : "暂不能供包";
    public string OperatorNotice => !Fresh ? "等待状态恢复，请勿依据旧格口投递。" :
        Snapshot?.BlockingReasons.Any(x => x.Contains("authentication required", StringComparison.OrdinalIgnoreCase)) == true ?
            "请先登录操作员；如设备正在维护，请等待维护完成。" :
        Snapshot?.Paused == true ? "维护处理中，等待当前操作完成。" :
        Snapshot?.Positions?.Any(p => !p.Admit) == true ? "请按对应位置的提示处理。" : "";
    bool HasConfiguredWorkConnection => Snapshot is { } s &&
        (ParcelBindings.Readers(s.Configuration.Effective).Any(r => !ParcelConnectionSetup.IsDeferred(s.Configuration.Effective, r.DeviceId)) ||
         s.Configuration.Effective.Wcs is { } w && !ParcelConnectionSetup.IsDeferred(s.Configuration.Effective, w.InstanceId));
    public string ReconnectHint => !IsManagementMode ? "请先进入管理模式。" : !Fresh ? "等待状态读取恢复后再重连。" :
        !HasConfiguredWorkConnection ? "尚无已配置的相机或 RCS；请先完成配置、保存并重启。" :
        Busy ? "正在处理其他操作，请稍候。" :
        Snapshot?.Operations.Any(o => o.State == ParcelState.ResolvingWcs) == true ? "仍有任务等待回应，请稍后重连。" : "";
    public bool CanReconnectConfigured => ReconnectHint.Length == 0;
    public async Task ReconnectConfiguredAsync()
    {
        if (!CanReconnectConfigured) { Message = ReconnectHint; return; }
        await RunAsync(ReconnectAsync, "重新连接设备与服务");
    }
    public string LeftConnectionDiagnosis => CameraDiagnosis(Left);
    public string RightConnectionDiagnosis => CameraDiagnosis(Right);
    string CameraDiagnosis(SupplyStationPositionViewModel position)
    {
        if (!Fresh || Snapshot is not { } s) return "状态读取已中断。先恢复状态读取，再检查相机连接。";
        if (ParcelConnectionSetup.IsDeferred(s.Configuration.Effective, position.ReaderId))
            return "此相机暂未启用；可手动输入条码供包。需要扫码时填写连接参数，保存并重启。";
        var device = s.Devices?.SingleOrDefault(d => d.DeviceId == position.ReaderId);
        if (device is null) return "尚未收到相机状态，原因未确认。请核对驱动参数并查看日志。";
        if (device.Health == DeviceHealth.Unknown && device.Source.Contains("decoder=Unavailable", StringComparison.Ordinal))
        {
            var frameText = device.Source.Split(';').Select(x => x.Trim()).FirstOrDefault(x => x.StartsWith("frames=", StringComparison.Ordinal));
            return long.TryParse(frameText?[7..], out var frames) && frames > 0
                ? $"已收到相机图像（{frames}帧），但尚未接入条码识别，暂不能供包。请核对该型号配套的识码算法组件。"
                : "MVS已开始等待图像，尚未收到帧；条码识别也尚未接入。请先核对触发、采图及配套识码算法。";
        }
        if (device.IsValid(DateTimeOffset.UtcNow)) return s.Configuration.Effective.Mode == RunMode.Simulation
            ? "当前为模拟连接；真实相机尚未验证。" : "相机连接已确认；能否供包还需满足本位业务条件。";
        return device.Health switch
        {
            DeviceHealth.Connecting or DeviceHealth.Reconnecting => "正在建立相机连接。请等待；持续未连接时检查地址、供电和网络。",
            DeviceHealth.Stopped or DeviceHealth.Stopping => "相机连接正在停止或已停止；请等待当前维护操作完成。",
            DeviceHealth.Disconnected => "相机离线，软件会自动重连。请检查供电、网线和连接参数。",
            DeviceHealth.Faulted => "相机出现故障，请在连接诊断中查看具体原因。",
            DeviceHealth.Ready => "相机连接确认已过期或无效。请检查驱动和网络，等待新的有效确认。",
            _ => "尚未确认相机连接，原因未确认。请核对驱动参数并查看日志。"
        };
    }
    public string LeftConnectionTime => CameraTime(Left);
    public string RightConnectionTime => CameraTime(Right);
    string CameraTime(SupplyStationPositionViewModel position) => Snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == position.ReaderId) is { } d
        ? "状态更新：" + d.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "尚无状态更新时间";
    public string RcsConnectionDiagnosis
    {
        get
        {
            if (!Fresh || Snapshot is not { } s) return "状态读取已中断。先恢复状态读取，再检查 RCS。";
            if (s.Configuration.Effective.Wcs is not { } w || ParcelConnectionSetup.IsDeferred(s.Configuration.Effective, w.InstanceId))
                return "连接尚未配置。请填写 RCS 服务参数，保存并重启。";
            if (s.Configuration.Effective.Mode == RunMode.Simulation) return "当前为模拟连接；真实 RCS 尚未验证。";
            var health = s.Health.SingleOrDefault(h => h.InstanceId == w.InstanceId);
            if (health is null) return "尚未收到 RCS 连接状态，原因未确认。请核对服务参数并查看日志。";
            var now = DateTimeOffset.UtcNow;
            if (health.ConfirmedAt > now || health.ValidUntil <= now || health.ValidUntil <= health.ConfirmedAt)
                return "RCS 状态确认已过期或无效。请检查服务和网络，等待新的有效确认。";
            if (!health.Ready) return "RCS 连接未就绪。请检查服务地址、服务运行情况和网络；具体故障见日志。";
            return health.Detail.Contains("RCS heartbeat HTTP ", StringComparison.Ordinal)
                ? "最近心跳已收到 HTTP 回应；本次包裹仍以取格口回应为准。"
                : "本机客户端已启动，尚未确认远端连接。请等待心跳或查看日志。";
        }
    }
    public string RcsConnectionTime => Snapshot?.Health.SingleOrDefault(h => h.InstanceId == Snapshot.Configuration.Effective.Wcs?.InstanceId) is { } h
        ? "状态更新：" + h.ConfirmedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "尚无状态更新时间";
    public string DiagnosticConfiguration => Snapshot is null ? "尚未读取配置" :
        Snapshot.Configuration.Confirmation?.Required == true ? "配置运行情况尚未确认，请核对配置生效情况。" :
        Snapshot.HasPendingConfiguration == true ? "有已保存的修改，等待重启生效；当前连接仍使用原配置。" :
        Snapshot.Configuration.Failure is not null ? "配置应用出现问题，请核对配置生效情况。" : "正在使用当前运行配置。";
    public string DiagnosticCapacity => Snapshot?.Capacity is { } c
        ? $"待处理作业 {c.Unresolved} / {c.MaximumUnresolved}；本机保留近期记录 {c.Resident} 条、归档记录 {c.Archived} 条。"
        : "尚未取得容量信息";
    public string DiagnosticBlockers
    {
        get
        {
            if (!Fresh || Snapshot is not { } s) return "状态读取中断，请先恢复状态读取。";
            var reasons = new List<string>();
            if (MissingConnections.Length > 0) reasons.Add("连接参数未完成，请处理上方待配置项。");
            if (s.Paused) reasons.Add("维护处理中，等待当前操作完成。");
            if (s.BlockingReasons.Any(x => x.Contains("qualification", StringComparison.OrdinalIgnoreCase))) reasons.Add("部署检查尚未完成或已过期，请维护人员核对部署条件。");
            if (s.Capacity?.Available == false) reasons.Add("待处理作业或存储空间已达到限制，请处理积压并检查本机空间。");
            if (s.Operations.Any(o => o.Wcs?.State is StageState.Unknown or StageState.Conflict)) reasons.Add("有包裹结果待核对，请查看包裹记录；连接恢复不会确认该包裹结果。");
            if (reasons.Count == 0 && s.BlockingReasons.Length > 0) reasons.Add("供包条件尚未满足；请核对上方连接状态，并复制诊断信息交由维护支持人员检查。");
            return reasons.Count == 0 ? "当前未报告全站供包阻断。" : string.Join("\n", reasons);
        }
    }
    public string DiagnosticTechnicalSummary => string.Join("\n", new[] {
        "诊断信息（本机快照；不表示现场验收）", Station, ReadStatus, ConfigurationState, Health, CapacityState, BlockingReasons, Message,
        TechnicalPosition(Left), TechnicalPosition(Right) });
    static string TechnicalPosition(SupplyStationPositionViewModel p) => string.Join("\n", new[] {
        p.Heading, p.ReaderIdentity, p.DeviceStatus, p.BlockingReasons, p.ObservationIdentity,
        "扫描时间：" + p.ScanTime, "相机读取的全部条码：", p.RawCandidates, "用于申请任务的条码：" + p.SelectedCode,
        p.SelectionSummary, "作业：" + p.OperationId, "请求：" + p.RequestId, p.OriginalResponsibility, p.ResultReason });
    void NotifyOperatorWorkspace()
    {
        foreach (var name in new[] { nameof(ShowSetupGuidance), nameof(SetupHeading), nameof(SetupItems), nameof(SetupInstruction),
            nameof(OperatorStatus), nameof(OperatorNotice), nameof(CanReconnectConfigured), nameof(ReconnectHint),
            nameof(LeftConnectionDiagnosis), nameof(RightConnectionDiagnosis), nameof(RcsConnectionDiagnosis),
            nameof(LeftConnectionTime), nameof(RightConnectionTime), nameof(RcsConnectionTime), nameof(DiagnosticConfiguration), nameof(DiagnosticCapacity), nameof(DiagnosticBlockers) }) Changed(name);
    }
}
