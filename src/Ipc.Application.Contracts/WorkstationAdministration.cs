using Ipc.Platform;
namespace Ipc.Application.Contracts;

public interface IManagementMode
{
    Task<ManagementModeStatus> ManagementModeStatusAsync();
    Task ConfigureManagementPasswordAsync(string password);
    Task<bool> EnterManagementModeAsync(string password);
    Task ExitManagementModeAsync();
    Task ChangeManagementPasswordAsync(string password);
}
public interface IWorkstationAdministration
{
    Task<ManagementModeStatus> ManagementModeStatusAsync();
    Task ConfigureManagementPasswordAsync(string password);
    Task<bool> EnterManagementModeAsync(string password);
    Task ExitManagementModeAsync();
    Task ChangeManagementPasswordAsync(string password);
    Task<SoftwareLogPage> QueryLogsAsync(SoftwareLogQuery query, string? selection, int offset, CancellationToken token);
    Task<SoftwareLogContext> LogContextAsync(string selection, SoftwareLogLocation location, int before, int after, CancellationToken token);
    Task<long> ExportLogsAsync(string selection, string destination, bool allInRange, int context, CancellationToken token);
}
// Releasing a pinned read discloses no data and remains legal after revocation.
public interface ISoftwareLogSelectionLifetime
{
    void ReleaseLogSelection(string selection);
}
public sealed record SoftwareLogQuery(DateTimeOffset From, DateTimeOffset ToExclusive, string Keyword = "", bool Regex = false, string Level = "", string Source = "");
public sealed record SoftwareLogLocation(string FileId, long Line, string Hash, string File = "");
public sealed record SoftwareLogEntry(SoftwareLogLocation Location, string Source, DateTimeOffset? At, string Level, string Text);
public sealed record SoftwareLogPage(string Selection, SoftwareLogEntry[] Entries, bool HasMore, string[] Notices);
public sealed record SoftwareLogContext(SoftwareLogEntry[] Entries, string[] Notices);
