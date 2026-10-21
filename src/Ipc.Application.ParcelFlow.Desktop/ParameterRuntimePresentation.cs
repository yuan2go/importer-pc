using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed record ParameterDiagnosticTarget(string InstanceId, string? DeviceId, string Label);

public partial class ParcelViewModel
{
    public string ParameterRuntimeSummary(ParameterDiagnosticTarget target)
    {
        if (Snapshot is not { } snapshot) return "尚未取得运行快照；编辑值不表示已连接。";
        if (!Fresh) return "运行状态未知 · " + ReadStatus + "\n编辑值不表示已连接；恢复状态读取后核对。";
        var current = snapshot.Configuration;
        var effective = current.Effective;
        var now = DateTimeOffset.UtcNow;
        string identity, address, status, evidence = "";
        if (target.DeviceId is { } device)
        {
            var reader = ParcelBindings.Readers(effective).SingleOrDefault(r => r.DeviceId == device);
            if (reader is null) return "该相机未出现在当前运行拓扑中；请核对原实例。";
            var extension = effective.Extensions?.Instances.SingleOrDefault(x => reader.Driver == "extension:" + x.Id);
            var package = effective.Extensions?.Packages.SingleOrDefault(x => x.Id == extension?.PackageId);
            identity = package is null ? reader.Driver : package.Id + " @ " + package.Version;
            address = extension?.Parameters.GetValueOrDefault("device.ip") ?? "未声明 IP · " + device;
            var health = snapshot.Devices?.SingleOrDefault(x => x.DeviceId == device);
            status = health?.IsValid(now) == true ? "已确认" : "未就绪 · " + (health?.ValidityReason(now) ?? "尚无设备确认");
        }
        else
        {
            var binding = ParcelBindings.All(effective).SingleOrDefault(x => x.InstanceId == target.InstanceId);
            if (binding is null) return "该组件未绑定到当前运行角色；保存和安装不表示已启用。";
            identity = binding.Implementation + " @ " + binding.Version;
            address = binding.Target;
            var health = snapshot.Health.SingleOrDefault(x => x.InstanceId == binding.InstanceId);
            status = health is { Ready: true } && health.ConfirmedAt <= now && health.ValidUntil > now && health.ValidUntil > health.ConfirmedAt
                ? "组件本地就绪" : "未就绪";
            evidence = "\n运行证据：" + (health?.Detail ?? "尚无运行确认") + "\n本地就绪不表示远端接受；心跳与业务结果请在诊断中分别核对。";
        }
        // The envelope revision is not an Effective version. Use the actual
        // Effective fingerprint, never the edited candidate or Pending identity.
        var fingerprint = Wire.Hash(Wire.Encode(effective))[..12];
        var confirmation = current.Status == "ActivationFailed" || current.Confirmation is not { Required: false } ? " · 启动证据需核对" : "";
        return $"运行连接：{status}{confirmation}\n运行地址：{address}\n运行版本：{identity} · 配置标识 {fingerprint}" + evidence +
            (current.Pending is null ? "" : "\n另有已保存设置待重启；当前连接仍使用以上运行配置。");
    }
}
