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
            var path = Path.GetFullPath(directory!);
            var drive = DriveInfo.GetDrives().Where(d => path == d.Name.TrimEnd(Path.DirectorySeparatorChar) || path.StartsWith(d.Name.EndsWith(Path.DirectorySeparatorChar) ? d.Name : d.Name + Path.DirectorySeparatorChar, StringComparison.Ordinal)).OrderByDescending(d => d.Name.Length).First();
            freeStorageBytes = drive.AvailableFreeSpace;
            storageCapacityFailure = freeStorageBytes < Retention.MinimumFreeBytes ? "Storage reserve exhausted; preserve original history and use approved external retention procedure" : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { storageCapacityFailure = "Storage capacity unavailable: " + ex.GetType().Name; }
        storageChecked = DateTimeOffset.UtcNow;
    }
}
