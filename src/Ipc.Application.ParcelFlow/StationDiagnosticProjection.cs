using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

// No raw text survives projection. Unknown/nested log fields are excluded,
// rather than relying on regex redaction of arbitrary business payloads.
internal sealed class StationDiagnosticProjection
{
    readonly Dictionary<string, string> aliases = new(StringComparer.Ordinal);
    string? Alias(string kind, string? value)
    {
        if (value is null) return null;
        var key = kind + "\0" + value;
        if (!aliases.TryGetValue(key, out var alias)) aliases.Add(key, alias = kind + "-" + (aliases.Count + 1));
        return alias;
    }

    public object Station(ParcelSnapshot snapshot) => new
    {
        Schema = 2,
        snapshot.RuntimeId,
        snapshot.Revision,
        snapshot.Paused,
        Configuration = new
        {
            snapshot.Configuration.Revision,
            RequiresRestart = snapshot.Configuration.Pending is not null,
            Mode = snapshot.Configuration.Effective.Mode.ToString(),
            snapshot.Configuration.Effective.QueueCapacity,
            snapshot.Configuration.Effective.TimeoutMs,
            snapshot.Configuration.Effective.MaximumOperations,
            HistoryDays = snapshot.Configuration.Effective.Retention?.HistoryDays,
            DiagnosticLogDays = snapshot.Configuration.Effective.Retention?.DiagnosticLogDays
        },
        Health = snapshot.Health.Select(h => new { Capability = Alias("capability", h.InstanceId), h.Ready, h.ConfirmedAt, h.ValidUntil }).ToArray(),
        Capacity = new
        {
            Unresolved = snapshot.Capacity?.Unresolved,
            Resident = snapshot.Capacity?.Resident,
            Archived = snapshot.Capacity?.Archived,
            MaximumUnresolved = snapshot.Capacity?.MaximumUnresolved,
            Available = snapshot.Capacity?.Available,
            FreeStorageBytes = snapshot.Capacity?.FreeStorageBytes,
            MinimumFreeBytes = snapshot.Capacity?.MinimumFreeBytes
        },
        OperationsTotal = snapshot.Operations.Length,
        OperationsOmitted = Math.Max(0, snapshot.Operations.Length - 100),
        Operations = snapshot.Operations.OrderByDescending(o => o.ObservedAt).Take(100).Select(o => new
        {
            Operation = Alias("operation", o.Id),
            Position = Alias("position", o.PositionId),
            o.ObservedAt,
            State = o.State.ToString(),
            Result = o.Wcs?.State.ToString(),
            o.RecoveryRequired
        }).ToArray()
    };

    public object Event(SoftwareLogEntry entry) => new
    {
        entry.At,
        Source = entry.Source is "RCS结果" or "相机通信" or "参数" or "管理模式" or "存储" or "业务控制" or "进程" or "日志" ? entry.Source : "其他",
        Level = entry.Level is "信息" or "警告" or "错误" or "致命" or "调试" or "跟踪" ? entry.Level : "未知",
        Operation = Alias("operation", entry.OperationId)
    };

    internal sealed record ReleaseSummary(bool Published, string? BusinessVersion, string? SourceCommit, string BinarySha256, string Integrity);
    public static ReleaseSummary Release(Assembly business, string application)
    {
        var hash = Wire.FileHash(business.Location);
        var version = BusinessCompatibility.VersionOf(business);
        var path = Path.Combine(AppContext.BaseDirectory, "application-release.json");
        if (!File.Exists(path)) return new(false, version, null, hash, "SourceBuild");
        try
        {
            RecoveryFiles.RequireOrdinaryAncestors(path);
            if (new FileInfo(path).Length > 4 * 1024 * 1024) return new(false, version, null, hash, "Unavailable");
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var value = document.RootElement;
            if (value.GetProperty("ApplicationId").GetString() != application) return new(false, version, null, hash, "IdentityMismatch");
            var source = value.GetProperty("SourceCommit").GetString();
            if (source is not { Length: 40 } || !source.All(char.IsAsciiHexDigit)) return new(false, version, null, hash, "InvalidIdentity");
            // Runtime startup validates installed bytes independently.
            // Metadata export does not claim Authenticode verification.
            return new(true, version, source, hash, "MetadataOnly");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        { return new(false, version, null, hash, "Unavailable"); }
    }

    public static object EnvironmentSummary(string dataRoot)
    {
        long? free = null, total = null;
        try { var disk = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dataRoot))!); free = disk.AvailableFreeSpace; total = disk.TotalSize; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        object[] networks = [];
        var networkStatus = "Collected";
        try
        {
            networks = NetworkInterface.GetAllNetworkInterfaces()
                .GroupBy(n => new { Kind = n.NetworkInterfaceType.ToString(), Status = n.OperationalStatus.ToString() })
                .Select(g => (object)new { g.Key.Kind, g.Key.Status, Count = g.Count() }).ToArray();
        }
        catch (NetworkInformationException) { networkStatus = "Unavailable"; }
        return new
        {
            CapturedAt = DateTimeOffset.UtcNow,
            OSVersion = Environment.OSVersion.Version.ToString(),
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            FreeBytes = free,
            TotalBytes = total,
            DiskStatus = free is null ? "Unavailable" : "Collected",
            NetworkStatus = networkStatus,
            Networks = networks
        };
    }

    public static object Installation(string application)
    {
        if (!OperatingSystem.IsWindows()) return new { Status = "Unavailable", Reason = "WindowsOnly" };
        try
        {
            var current = WindowsDeploymentSetup.Current(application);
            if (current is null) return new { Status = "Unavailable", Reason = "NoProtectedRegistration" };
            return new
            {
                Status = "RegistrationOnly",
                State = current.LifecycleState is "Installed" or "Retired" ? current.LifecycleState : "Unknown",
                SourceCommit = current.SourceCommit is { Length: 40 } && current.SourceCommit.All(char.IsAsciiHexDigit) ? current.SourceCommit : null,
                HasMaintenanceCache = current.MaintenanceRoot is not null,
                TransactionReceipt = "NotCollected"
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { return new { Status = "Unavailable", Reason = "ProtectedRegistrationUnreadable" }; }
    }
}
