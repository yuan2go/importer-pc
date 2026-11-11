namespace Ipc.Application.Contracts;

// Software defaults, not customer protocol approval. Optional facilities stay off.
public static class SystemParameterDefaults
{
    public const int QueueCapacity = 64, TimeoutMs = 1500, MaximumOperations = 10000, ResidentHistory = 200;
    public const int BackupIntervalMinutes = 60, MaximumBackupSets = 10;
    public const int DiagnosticLogDays = 7, HistoryDays = 7;
    public const int ReadTimeoutMs = 1000, MinimumReadTimeoutMs = 100, MaximumReadTimeoutMs = 60000;
    // SM keeps headroom beyond two 32MiB original writes, the 64MiB WAL warning
    // and seven bounded 256KiB replies. This is an admission floor, not a disk quota.
    public const long SmMinimumFreeBytes = 536870912;
    public const long MinimumFreeBytes = 67108864;
    public const string InitialStationId = "station-01";
    public static string StationCode(string stationId) => stationId;
}
