using Ipc.Application.Contracts;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    long? freeStorageBytes;
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
    ParcelRetentionPolicy Retention => Effective.Retention ?? new(MinimumFreeBytes: definition.DefaultDiskReserveBytes);
    void RefreshStorageCapacity()
    {
        var timestamp = StorageTimeForVerification.GetTimestamp();
        if (storageChecked != default && StorageTimeForVerification.GetElapsedTime(storageCheckedTimestamp, timestamp) < TimeSpan.FromSeconds(1)) return;
        freeStorageBytes = null;
        try
        {
            var free = StorageAvailableBytesForVerification?.Invoke() ?? ParcelStorageVolume.Plan(directory!, 0, Retention.MinimumFreeBytes).AvailableBytes;
            freeStorageBytes = free;
            storageCapacityFailure = free < Retention.MinimumFreeBytes ? "Storage reserve exhausted; preserve original history and use approved external retention procedure" : null;
            var warning = Retention.WarningFreeBytes ?? checked(Retention.MinimumFreeBytes * 4);
            var critical = Retention.CriticalFreeBytes ?? checked(Retention.MinimumFreeBytes * 2);
            var level = storageCapacityFailure is not null ? "Blocked" : free < critical ? "Critical" : free < warning ? "Warning" : "Normal";
            storageCapacityWarning = level == "Normal" ? null : "StorageGrowthWarning: Level=" + level + "; free=" + free + "; warning=" + warning + "; critical=" + critical + "; reserve=" + Retention.MinimumFreeBytes + "; preserve originals; plan authorized maintenance/relocation";
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
                double? remainingDays = elapsed > 0 && bytesPerJob is { } perJob ? Math.Max(0, free - Retention.MinimumFreeBytes) / (perJob * jobs / elapsed * 86400) : null;
                storageCapacityEstimate = "StorageForecast:" + Ipc.Platform.Wire.Encode(new
                {
                    Result = remainingDays is null ? "InsufficientSamples" : "EstimateOnly",
                    Jobs = jobs,
                    SampleSeconds = elapsed,
                    DatabaseGrowthBytes = growth,
                    BytesPerJob = bytesPerJob,
                    RemainingDays = remainingDays,
                    Basis = "Recent physical SQLite page growth and admission sequence; exclude backups/images/logs; no deletion or site guarantee"
                });
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
        {
            storageCapacityFailure = "Storage capacity unavailable: " + ex.GetType().Name;
            if (freeStorageBytes is null)
            {
                storageCapacityWarning = null;
                storageCapacityEstimate = null;
                if (lastStorageLevel != "Unavailable")
                    Diagnostic("StorageCapacity", "Unavailable", "警告", ex.ToString());
                lastStorageLevel = "Unavailable";
            }
        }
        storageChecked = StorageTimeForVerification.GetUtcNow();
        storageCheckedTimestamp = timestamp;
    }
}

public sealed record ParcelRecoveryCapacity(long CopyBytes, long MinimumFreeBytes, long AvailableBytes, long RemainingGrowthBytes, bool Fits)
{
    public string State { get; init; } = "Planned";
    public string? RetainedPartialDirectory { get; init; }
}
internal static class ParcelStorageVolume
{
    public static ParcelRecoveryCapacity Plan(string destination, long copyBytes, long reserve)
    {
        var free = Read(destination).Free;
        return new(copyBytes, reserve, free, free - copyBytes - reserve, free >= checked(copyBytes + reserve));
    }
    internal static (long Free, long Total) Read(string destination)
    {
        var path = Path.GetFullPath(destination);
        while (true)
        {
            try { if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) break; }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
            path = Path.GetDirectoryName(path) ?? throw new IOException("未能核对目标存储卷。");
        }
        if (OperatingSystem.IsWindows())
        {
            if (!GetDiskFreeSpace(path, out var available, out var total, out _))
                throw new IOException("未能读取目标存储卷容量。", new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error()));
            return (checked((long)available), checked((long)total));
        }
        var drive = DriveInfo.GetDrives().Where(d => path == d.Name.TrimEnd(Path.DirectorySeparatorChar) ||
            path.StartsWith(d.Name.EndsWith(Path.DirectorySeparatorChar) ? d.Name : d.Name + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .OrderByDescending(d => d.Name.Length).FirstOrDefault() ?? throw new IOException("未能核对目标存储卷。");
        return (drive.AvailableFreeSpace, drive.TotalSize);
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool GetDiskFreeSpace(string directory, out ulong available, out ulong total, out ulong free);
}
