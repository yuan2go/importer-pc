using Ipc.Application.Contracts;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    long freeStorageBytes;
    string? storageCapacityFailure;
    string? storageCapacityWarning;
    internal TimeProvider StorageTimeForVerification { get; set; } = TimeProvider.System;
    long storageCheckedTimestamp, retentionCheckedTimestamp;
    DateTimeOffset storageChecked;
    DateTimeOffset retentionChecked;
    string? retentionWarning;
    ParcelRetentionPolicy Retention => Effective.Retention ?? new();
    void RefreshStorageCapacity()
    {
        var timestamp = StorageTimeForVerification.GetTimestamp();
        if (storageChecked != default && StorageTimeForVerification.GetElapsedTime(storageCheckedTimestamp, timestamp) < TimeSpan.FromSeconds(1)) return;
        try
        {
            freeStorageBytes = ParcelStorageVolume.Plan(directory!, 0, Retention.MinimumFreeBytes).AvailableBytes;
            storageCapacityFailure = freeStorageBytes < Retention.MinimumFreeBytes ? "Storage reserve exhausted; preserve original history and use approved external retention procedure" : null;
            storageCapacityWarning = freeStorageBytes < checked(Retention.MinimumFreeBytes * 2) ?
                "StorageGrowthWarning: less than twice the configured reserve remains; plan a maintenance backup/volume relocation before admission is blocked; archives do not reclaim disk" : null;
            if (repository?.StorageVersion >= 9 && (retentionChecked == default || StorageTimeForVerification.GetElapsedTime(retentionCheckedTimestamp, timestamp) >= TimeSpan.FromMinutes(1)))
            {
                retentionWarning = Retention.HistoryDays is not { } days ? "HistoryRetentionNotConfigured: select 7..30 days in system settings; no expiry deletion" :
                    repository.RetentionDue(days, StorageTimeForVerification.GetUtcNow()) ? "HistoryRetentionMaintenanceDue: aged history exists; pause, stop and run matching-release expire-history; unresolved originals are retained" : null;
                retentionChecked = StorageTimeForVerification.GetUtcNow();
                retentionCheckedTimestamp = timestamp;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { storageCapacityFailure = "Storage capacity unavailable: " + ex.GetType().Name; }
        storageChecked = StorageTimeForVerification.GetUtcNow();
        storageCheckedTimestamp = timestamp;
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
