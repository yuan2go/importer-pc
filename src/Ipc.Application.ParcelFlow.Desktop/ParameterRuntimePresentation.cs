using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed record ParameterDiagnosticTarget(string InstanceId, string? DeviceId, string Label);
public sealed record ParameterConnectionPresentation(string Status, string Address, string Details, string Notice, string Tone)
{
    public string Summary => Status + (Address.Length == 0 ? "" : "\n运行地址：" + Address) +
        (Details.Length == 0 ? "" : "\n" + Details) + (Notice.Length == 0 ? "" : "\n" + Notice);
}

public partial class ParcelViewModel
{
    public string ParameterRuntimeSummary(ParameterDiagnosticTarget target) => ParameterConnection(target).Summary;
    public ParameterConnectionPresentation ParameterConnection(ParameterDiagnosticTarget target)
    {
        if (Snapshot is not { } snapshot) return new("尚未取得运行快照", "", "", "编辑值不表示已连接。", "neutral");
        if (!Fresh) return new("运行状态未知", "", ReadStatus, "编辑值不表示已连接；恢复状态读取后核对。", "warning");
        var current = snapshot.Configuration;
        var effective = current.Effective;
        if (ParcelConnectionSetup.IsDeferred(effective, target.DeviceId ?? target.InstanceId))
            return new("连接待配置", "", "当前运行配置明确延后此连接；未创建设备或服务会话。", "填写连接参数并取消“稍后配置此连接”，保存后受控重启。重连不会启用待配置连接。", "warning");
        var now = DateTimeOffset.UtcNow;
        string identity, address, status, evidence = "", notice = ""; bool ready;
        if (target.DeviceId is { } device)
        {
            var reader = ParcelBindings.Readers(effective).SingleOrDefault(r => r.DeviceId == device);
            if (reader is null) return new("相机未在运行拓扑中", "", "", "请核对原实例；编辑值不表示已连接。", "warning");
            var extension = effective.Extensions?.Instances.SingleOrDefault(x => reader.Driver == "extension:" + x.Id);
            var package = effective.Extensions?.Packages.SingleOrDefault(x => x.Id == extension?.PackageId);
            identity = package is null ? reader.Driver : package.Id + " @ " + package.Version;
            address = reader.Parameters?.GetValueOrDefault("device.ip") is { } tcpIp ? reader.Driver == "tcp-line" ? tcpIp + ":" + reader.Parameters.GetValueOrDefault("device.port") : tcpIp : extension?.Parameters.GetValueOrDefault("device.ip") ?? "未声明 IP · " + device;
            var health = snapshot.Devices?.SingleOrDefault(x => x.DeviceId == device);
            ready = health?.IsValid(now) == true;
            status = ready ? reader.Driver == "tcp-line" ? "TCP 已连接" : "设备已确认" : "未就绪";
            if (!ready) notice = health?.ValidityReason(now) ?? "尚无设备确认";
            if (reader.Driver == "tcp-line")
                notice += (notice.Length == 0 ? "" : "\n") + (reader.Parameters?.GetValueOrDefault("profile", "plain") == "sequenced"
                    ? "sequenced：握手与连续序号、生产者心跳均须有效；故障后暂停并核对原包裹，再受控重连。"
                    : "plain：丢失不可检测，静默断网可能延后发现；没有生产者握手或业务心跳；一行一实体须有明确证据。");
            if (extension?.PackageId == "hikrobot.idmvs-reader")
            {
                status = ready ? "SDK 连接已确认" : status;
                notice += (notice.Length == 0 ? "" : "\n") + (extension.Parameters.GetValueOrDefault("entity.mode", "observe-only") == "observe-only"
                    ? "仅观察，不创建供包作业。连接确认不代表已经读到条码；请核对读码观察和设备适用性。"
                    : "触发关联已选择；连接确认不证明包裹边界可信。读码、业务准入和本件RCS格口需分别核对。");
            }
            if (reader.Driver == "hikrobot-mvs" || extension?.PackageId == "hikrobot.mvs-acquisition")
            {
                status = health?.Health == DeviceHealth.Faulted ? "采集故障；缺少条码识别组件" : "仅采图；缺少条码识别组件";
                notice = "采集连接及帧计数在诊断中核对；尚未获得真实条码，不创建供包作业。取得适用识别组件并完成实际接入前，正式供包保持阻断。";
            }
            if (IsManagementMode && health is not null)
                evidence = "\n设备确认：" + health.ConfirmedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") +
                    "\n连接周期：" + health.Epoch + " · 已处理序号：" + health.Sequence + "\n连接证据：" + health.Source;
            if (!ready) notice += "\n请检查本相机连接证据和现场实体，暂停并进入管理模式后按当前运行配置受控重连。";
        }
        else
        {
            var binding = ParcelBindings.Bound(effective).SingleOrDefault(x => x.InstanceId == target.InstanceId);
            if (binding is null) return new("能力未绑定到运行角色", "", "", "保存和安装不表示已启用。", "warning");
            identity = binding.Implementation + " @ " + binding.Version;
            address = binding.Target;
            var health = snapshot.Health.SingleOrDefault(x => x.InstanceId == binding.InstanceId);
            ready = health is { Ready: true } && health.ConfirmedAt <= now && health.ValidUntil > now && health.ValidUntil > health.ConfirmedAt;
            status = ready ? "本地就绪" : "未就绪";
            evidence = "\n运行证据：" + (health?.Detail ?? "尚无运行确认");
            notice = "本地就绪不表示远端接受；心跳与业务结果请在诊断中分别核对。";
            if (!ready) notice = (health?.Detail ?? "尚无运行确认") + "\n" + notice;
        }
        // The envelope revision is not an Effective version. Use the actual
        // Effective fingerprint, never the edited candidate or Pending identity.
        var fingerprint = Wire.Hash(Wire.Encode(effective))[..12];
        var confirmation = current.Status == "ActivationFailed" || current.Confirmation is not { Required: false } ? " · 启动证据需核对" : "";
        if (current.Pending is not null) notice += (notice.Length == 0 ? "" : "\n") + "另有已保存设置待重启；当前连接仍使用以上运行配置。";
        return new($"运行连接：{status}{confirmation}", address, $"运行版本：{identity} · 配置标识 {fingerprint}" + evidence,
            notice, !ready || confirmation.Length > 0 ? "warning" : "ready");
    }
}
