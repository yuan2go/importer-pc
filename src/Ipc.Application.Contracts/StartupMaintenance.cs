using Ipc.Platform;

namespace Ipc.Application.Contracts;

public sealed record StartupMaintenanceDocument(ParcelConfiguration? Candidate, CapabilityCatalog Catalog,
    string ConfigurationHash, long Revision, bool ExistingConfiguration, string? RuntimeDirectory, string[] Diagnostics, string ConfigurationStatus = "Unknown");
public sealed record StartupRecoveryPreview(string BackupSha256, string Destination, string Summary, string DataRoot);

// Independent of IBusinessRuntime: no device/session is required to inspect or repair startup.
public interface IStartupMaintenance
{
    Task<StartupMaintenanceDocument> ReadAsync();
    Task<BusinessReceipt> ValidateAsync(ParcelConfiguration candidate);
    Task<StartupMaintenanceDocument> StageAsync(string expectedHash, ParcelConfiguration candidate, string reason);
    Task ExportAsync(string destination);
    Task<StartupRecoveryPreview> PlanRestoreAsync(string backup, string destination);
    Task RestoreAsync(string backup, string destination, string expectedBackupHash);
}
