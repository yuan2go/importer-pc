using Ipc.Application.Contracts;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    long freeStorageBytes;
    string? storageCapacityFailure;
    string? storageCapacityWarning;
    string? storageCapacityEstimate;
    long storageCheckedTimestamp;
    long retentionCheckedTimestamp;
    string? lastStorageLevel;
    readonly Queue<(long At, long Sequence, long Bytes)> storageSamples = new();
    internal TimeProvider StorageTimeForVerification { get; set; } = TimeProvider.System;
    DateTimeOffset storageChecked;
    DateTimeOffset retentionChecked;
    string? retentionWarning;
    internal Func<long>? StorageAvailableBytesForVerification { get; set; }
    ParcelRetentionPolicy Retention => Effective.Retention ?? new();
    void RefreshStorageCapacity()
    {
        var timestamp = StorageTimeForVerification.GetTimestamp();
        if (storageChecked != default && StorageTimeForVerification.GetElapsedTime(storageCheckedTimestamp, timestamp) < TimeSpan.FromSeconds(1)) return;
        try
        {
            freeStorageBytes = StorageAvailableBytesForVerification?.Invoke() ?? ParcelStorageVolume.Plan(directory!, 0, Retention.MinimumFreeBytes).AvailableBytes;
            storageCapacityFailure = freeStorageBytes < Retention.MinimumFreeBytes ? "Storage reserve exhausted; preserve original history and use approved external retention procedure" : null;
            var warning = Retention.WarningFreeBytes ?? checked(Retention.MinimumFreeBytes * 4);
            var critical = Retention.CriticalFreeBytes ?? checked(Retention.MinimumFreeBytes * 2);
            var level = storageCapacityFailure is not null ? "Blocked" : freeStorageBytes < critical ? "Critical" : freeStorageBytes < warning ? "Warning" : "Normal";
            storageCapacityWarning = level == "Normal" ? null : "StorageGrowthWarning: Level=" + level + "; free=" + freeStorageBytes + "; warning=" + warning + "; critical=" + critical + "; reserve=" + Retention.MinimumFreeBytes + "; preserve originals; plan authorized maintenance/relocation";
            if (level != lastStorageLevel)
            { Diagnostic("StorageCapacity", level, level == "Normal" ? "信息" : "警告", storageCapacityWarning); lastStorageLevel = level; }
            if (repository is not null && (storageSamples.Count == 0 || StorageTimeForVerification.GetElapsedTime(storageSamples.Last().At, timestamp) >= TimeSpan.FromMinutes(1)))
            {
                var sequence = repository.SelectHistory(new()).UpperSequence;
                var bytes = repository.StorageBytes;
                if (storageSamples.Count != 0 && (sequence < storageSamples.Last().Sequence || bytes < storageSamples.Last().Bytes)) storageSamples.Clear();
                storageSamples.Enqueue((timestamp, sequence, bytes)); while (storageSamples.Count > 12) storageSamples.Dequeue();
                var first = storageSamples.Peek(); var elapsed = StorageTimeForVerification.GetElapsedTime(first.At, timestamp).TotalSeconds;
                var jobs = sequence - first.Sequence; var growth = bytes - first.Bytes;
                double? bytesPerJob = jobs > 0 && growth > 0 ? (double)growth / jobs : null;
                double? remainingDays = elapsed > 0 && bytesPerJob is { } perJob ? Math.Max(0, freeStorageBytes - Retention.MinimumFreeBytes) / (perJob * jobs / elapsed * 86400) : null;
                storageCapacityEstimate = "StorageForecast:" + Ipc.Platform.Wire.Encode(new { Result = remainingDays is null ? "InsufficientSamples" : "EstimateOnly", Jobs = jobs, SampleSeconds = elapsed, DatabaseGrowthBytes = growth,
                    BytesPerJob = bytesPerJob, RemainingDays = remainingDays, Basis = "Recent physical SQLite page growth and admission sequence; exclude backups/images/logs; no deletion or site guarantee" });
                Diagnostic("StorageCapacity", "Forecast", "信息", storageCapacityEstimate);
            }
            if (repository is not null && (retentionChecked == default || StorageTimeForVerification.GetElapsedTime(retentionCheckedTimestamp, timestamp) >= TimeSpan.FromMinutes(1)))
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
