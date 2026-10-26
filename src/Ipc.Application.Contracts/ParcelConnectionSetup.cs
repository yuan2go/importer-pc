using Ipc.Platform;

namespace Ipc.Application.Contracts;

// Explicit unconfigured connections, never a simulation mode or successful fallback.
public static class ParcelConnectionSetup
{
    public static bool IsDeferred(ParcelConfiguration c, string identity) => c.DeferredConnections?.Contains(identity, StringComparer.Ordinal) == true;
    public static bool HasActiveConnections(ParcelConfiguration c) => ParcelBindings.Readers(c).Any(r => !IsDeferred(c, r.DeviceId)) ||
        ParcelBindings.Bound(c).Any(b => !IsDeferred(c, b.InstanceId));
    public static string[] Validate(ParcelConfiguration c)
    {
        if (c.DeferredConnections is not { Length: > 0 } pending) return [];
        var allowed = ParcelBindings.Readers(c).Select(r => r.DeviceId).Concat(c.Wcs is { Contract: "SPORTMASTER-RCS-SORTER-v1" } w ? new[] { w.InstanceId } : []).ToHashSet(StringComparer.Ordinal);
        return c.ApplicationId != "sportmaster" || pending.Length > 3 || pending.Distinct(StringComparer.Ordinal).Count() != pending.Length || pending.Any(x => x is null || !allowed.Contains(x))
            ? ["延后配置只适用于当前Sportmaster左右相机和RCS连接，身份不得重复或缺失。"] : [];
    }
}
