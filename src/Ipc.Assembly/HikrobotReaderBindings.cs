using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Assembly;

internal static class HikrobotReaderBindings
{
    internal static void Validate(ParcelConfiguration c)
    {
        var bindings = ParcelBindings.Readers(c).Select(r => r.Driver).ToHashSet(StringComparer.Ordinal);
        var active = (c.Extensions?.Instances ?? []).Concat(ParcelBindings.Readers(c).Where(r => r.Driver is "hikrobot-mvs" or "hikrobot-idmvs" or "hikrobot-mvs-reader").Select(r => new ExtensionInstance(r.DeviceId, r.Driver == "hikrobot-idmvs" ? "hikrobot.idmvs-reader" : "hikrobot.mvs-acquisition", r.Driver == "hikrobot-idmvs" ? "idmvs-reader" : "mvs-acquisition", r.DeviceId, true, r.Parameters ?? []))).Where(i => (bindings.Contains("extension:" + i.Id) || ParcelBindings.Readers(c).Any(r => r.Driver is "hikrobot-mvs" or "hikrobot-idmvs" or "hikrobot-mvs-reader" && r.DeviceId == i.DeviceId)) &&
            i.PackageId is "hikrobot.idmvs-reader" or "hikrobot.mvs-acquisition" && !ParcelConnectionSetup.IsDeferred(c, i.DeviceId)).ToArray();
        if (active.GroupBy(i => i.Parameters.GetValueOrDefault("device.ip")).Any(g => g.Count() > 1))
            throw new InvalidDataException("驱动/device.ip: 左右读码实例不能占用同一物理IPv4地址");
        foreach (var instance in active)
        {
            var ip = instance.Parameters.GetValueOrDefault("device.ip", "");
            if (!System.Net.IPAddress.TryParse(ip, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || address.ToString() != ip || ip is "0.0.0.0" or "255.255.255.255")
                throw new InvalidDataException("驱动/" + instance.Id + "/device.ip: 需要规范的设备IPv4地址");
            var serial = instance.Parameters.GetValueOrDefault("device.serial", "");
            if (c.Mode == RunMode.Production && string.IsNullOrWhiteSpace(serial))
                throw new InvalidDataException("驱动/" + instance.Id + "/device.serial: 正式启用必须配置预期物理序列号，不能仅凭IP绑定左右相机");
            if (serial.Length > 32 || serial.Any(char.IsControl))
                throw new InvalidDataException("驱动/" + instance.Id + "/device.serial: 需要有效的物理序列号");
        }
        if (active.Where(i => !string.IsNullOrWhiteSpace(i.Parameters.GetValueOrDefault("device.serial")))
            .GroupBy(i => i.Parameters["device.serial"], StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new InvalidDataException("驱动/device.serial: 左右读码实例不能绑定同一物理序列号");
    }
}
