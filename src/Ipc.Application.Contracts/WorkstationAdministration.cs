using Ipc.Platform;
namespace Ipc.Application.Contracts;

public interface IWorkstationAdministration
{
    Task<AdministratorStatus> AdministratorStatusAsync();
    Task ProvisionAdministratorAsync(string account, string password);
    Task<bool> LoginAdministratorAsync(string account, string password);
    Task LogoutAdministratorAsync();
    Task<AdministratorAccount[]> AdministratorAccountsAsync();
    Task MaintainAdministratorAsync(string account, string? password, bool enabled);
    Task<SoftwareLogPage> QueryLogsAsync(SoftwareLogQuery query, string? selection, int offset, CancellationToken token);
    Task<SoftwareLogContext> LogContextAsync(string selection, SoftwareLogLocation location, int before, int after, CancellationToken token);
    Task<long> ExportLogsAsync(string selection, string destination, bool allInRange, int context, CancellationToken token);
}
public sealed record SoftwareLogQuery(DateTimeOffset From, DateTimeOffset ToExclusive, string Keyword = "", bool Regex = false, string Level = "", string Source = "");
public sealed record SoftwareLogLocation(string FileId, long Line, string Hash, string File = "");
public sealed record SoftwareLogEntry(SoftwareLogLocation Location, string Source, DateTimeOffset? At, string Level, string Text);
public sealed record SoftwareLogPage(string Selection, SoftwareLogEntry[] Entries, bool HasMore, string[] Notices);
public sealed record SoftwareLogContext(SoftwareLogEntry[] Entries, string[] Notices);
