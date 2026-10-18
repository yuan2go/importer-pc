using Ipc.Application.Contracts;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    long freeStorageBytes;
    string? storageCapacityFailure;
    DateTimeOffset storageChecked;
    ParcelRetentionPolicy Retention => Effective.Retention ?? new();
    void RefreshStorageCapacity()
    {
        if (DateTimeOffset.UtcNow - storageChecked < TimeSpan.FromSeconds(1)) return;
        try
        {
            freeStorageBytes = ParcelStorageVolume.Plan(directory!, 0, Retention.MinimumFreeBytes).AvailableBytes;
            storageCapacityFailure = freeStorageBytes < Retention.MinimumFreeBytes ? "Storage reserve exhausted; preserve original history and use approved external retention procedure" : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { storageCapacityFailure = "Storage capacity unavailable: " + ex.GetType().Name; }
        storageChecked = DateTimeOffset.UtcNow;
    }
}

public sealed record ParcelRecoveryCapacity(long CopyBytes, long MinimumFreeBytes, long AvailableBytes, long RemainingGrowthBytes, bool Fits);
internal static class ParcelStorageVolume
{
    public static ParcelRecoveryCapacity Plan(string destination, long copyBytes, long reserve)
    {
        var path = Path.GetFullPath(destination);
        var drive = DriveInfo.GetDrives().Where(d => path == d.Name.TrimEnd(Path.DirectorySeparatorChar) ||
            path.StartsWith(d.Name.EndsWith(Path.DirectorySeparatorChar) ? d.Name : d.Name + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .OrderByDescending(d => d.Name.Length).First();
        var free = drive.AvailableFreeSpace;
        return new(copyBytes, reserve, free, free - copyBytes - reserve, free >= checked(copyBytes + reserve));
    }
}
