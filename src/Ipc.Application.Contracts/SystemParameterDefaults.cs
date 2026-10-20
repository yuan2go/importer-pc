namespace Ipc.Application.Contracts;

// Software defaults, not customer protocol approval. Optional facilities stay off.
public static class SystemParameterDefaults
{
    public const int QueueCapacity = 64, TimeoutMs = 1500, MaximumOperations = 10000, ResidentHistory = 200;
    public const int BackupIntervalMinutes = 60, MaximumBackupSets = 10;
    public const long MinimumFreeBytes = 67108864;
    public const string InitialStationId = "station-01";
    public static string StationCode(string stationId) => stationId;
}
