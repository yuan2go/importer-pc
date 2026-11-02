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
        if (Snapshot is not { } snapshot) return new("尚未读取运行状态", "", "", "编辑值不表示已连接。", "neutral");
        if (!Fresh) return new("运行状态未知", "", ReadStatus, "填写的值不代表已连接；状态读取恢复后再确认。", "warning");
        var current = snapshot.Configuration;
        var effective = current.Effective;
        if (ParcelConnectionSetup.IsDeferred(effective, target.DeviceId ?? target.InstanceId))
            return new("连接待配置", "", "当前运行配置尚未启用此连接。", "填写连接参数并保存，重启后启用连接。", "warning");
        var now = DateTimeOffset.UtcNow;
        string identity, address, status, evidence = "", notice = ""; bool ready;
        if (target.DeviceId is { } device)
        {
            var reader = ParcelBindings.Readers(effective).SingleOrDefault(r => r.DeviceId == device);
            if (reader is null) return new("相机未在运行配置中", "", "", "请检查相机配置；填写的值不代表已连接。", "warning");
            var extension = effective.Extensions?.Instances.SingleOrDefault(x => reader.Driver == "extension:" + x.Id);
            var package = effective.Extensions?.Packages.SingleOrDefault(x => x.Id == extension?.PackageId);
            identity = package is null ? reader.Driver : package.Id + " @ " + package.Version;
            address = reader.Parameters?.GetValueOrDefault("device.ip") is { } tcpIp ? reader.Driver == "tcp-line" ? tcpIp + ":" + reader.Parameters.GetValueOrDefault("device.port") : tcpIp : extension?.Parameters.GetValueOrDefault("device.ip") ?? "未声明 IP · " + device;
            var health = snapshot.Devices?.SingleOrDefault(x => x.DeviceId == device);
            ready = health?.IsValid(now) == true;
            status = ready ? reader.Driver == "tcp-line" ? "TCP 已连接" : "设备已确认" : "未就绪";
            if (!ready) notice = health?.ValidityReason(now) ?? "尚无设备确认";
            if (reader.Driver == "tcp-line")
                notice += (notice.Length == 0 ? "" : "\n") + "连接状态只反映本地 TCP 连接，没有心跳检测，网络静默断开可能稍后才发现。";
            if (reader.Driver is "hikrobot-idmvs" or "hikrobot-mvs-reader" || extension?.PackageId == "hikrobot.idmvs-reader")
            {
                status = ready ? "SDK 连接已确认" : status;
                notice += (notice.Length == 0 ? "" : "\n") + ((reader.Parameters ?? extension!.Parameters).GetValueOrDefault("entity.mode", "observe-only") == "observe-only"
                    ? "仅观察，不创建供包作业。已连接不代表能读到条码。"
                    : (reader.Parameters ?? extension!.Parameters).GetValueOrDefault("entity.mode") == "read-result" ? "逐次识别结果上报已选择，相同条码也会交给 RCS 去重。读码请求不代表物理包裹完成。"
                    : "已选择触发关联方式。已连接不代表包裹分割准确。");
            }
            if (reader.Driver == "hikrobot-mvs" || extension?.PackageId == "hikrobot.mvs-acquisition")
            {
                status = health?.Health == DeviceHealth.Faulted ? "采集故障；缺少条码识别组件" : "仅采图；缺少条码识别组件";
                notice = "相机连接和帧数可在诊断中查看。尚未读到条码，不创建供包作业；识别组件接入前不能正式供包。";
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
            notice = "本地已就绪不代表 RCS 已接受；心跳和返回结果请在诊断中查看。";
            if (!ready) notice = (health?.Detail ?? "尚无运行确认") + "\n" + notice;
        }
        // The envelope revision is not an Effective version. Use the actual
        // Effective fingerprint, never the edited candidate or Pending identity.
        var fingerprint = Wire.Hash(Wire.Encode(effective))[..12];
        var confirmation = current.Status == "ActivationFailed" || current.Confirmation is not { Required: false } ? " · 启动检查未完成" : "";
        if (current.Pending is not null) notice += (notice.Length == 0 ? "" : "\n") + "另有已保存设置待重启；当前连接仍使用以上运行配置。";
        return new($"运行连接：{status}{confirmation}", address, $"运行版本：{identity} · 配置标识 {fingerprint}" + evidence,
            notice, !ready || confirmation.Length > 0 ? "warning" : "ready");
    }
}
