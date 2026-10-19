using Ipc.Platform;

namespace Ipc.Application.Contracts;

public sealed record StartupMaintenanceDocument(ParcelConfiguration? Candidate, CapabilityCatalog Catalog,
    string ConfigurationHash, long Revision, bool ExistingConfiguration, string? RuntimeDirectory, string[] Diagnostics, string ConfigurationStatus = "Unknown");
public sealed record StartupRecoveryPreview(string BackupSha256, string Destination, string Summary, string DataRoot);
public sealed record StartupEditorState(Dictionary<string, string> Values);
public sealed record StartupSavedDraft(string BaseConfigurationHash, string DraftHash, DateTimeOffset At, StartupEditorState Editor, bool BaseChanged, string? SourceIdentity = null);
public sealed record StartupCheck(string Layer, string Status, string Reason);
public sealed record StartupCheckReport(string CandidateHash, DateTimeOffset At, StartupCheck[] Checks);
public sealed record StartupBackupItem(string Path, string Status, DateTimeOffset? At, long Bytes, string? Identity, string Summary);
public sealed record StartupChangePreview(string Identity, string Purpose, bool Allowed, string Summary, string ConfigurationHash, string? BackupDestination);
public sealed record StartupSiteTemplate(int SchemaVersion, string Application, string SourceIdentity, StartupEditorState Defaults);
public sealed record StartupRestoreHandoff(string Id, string SourceDirectory, string TargetDirectory, string BackupIdentity,
    string SourceConfigurationHash, string TargetManifestHash, string MachinePolicyHash, DateTimeOffset At);

// Independent of IBusinessRuntime: no device/session is required to inspect or repair startup.
public interface IStartupMaintenance
{
    Task<StartupMaintenanceDocument> ReadAsync();
    Task<BusinessReceipt> ValidateAsync(ParcelConfiguration candidate);
    Task<StartupCheckReport> CheckAsync(ParcelConfiguration candidate);
    Task<StartupMaintenanceDocument> StageAsync(string expectedHash, ParcelConfiguration candidate, string reason);
    Task<StartupMaintenanceDocument> RepairInitialAsync(string expectedHash, ParcelConfiguration candidate, string reason);
    Task<StartupSavedDraft?> ReadDraftAsync();
    Task<StartupSavedDraft> SaveDraftAsync(string expectedConfigurationHash, string? expectedDraftHash, StartupEditorState editor);
    Task ExportAsync(string destination);
    Task<StartupRecoveryPreview> PlanRestoreAsync(string backup, string destination);
    Task RestoreAsync(string backup, string destination, string expectedBackupHash);
    Task<StartupBackupItem[]> ListBackupsAsync(string directory, CancellationToken token);
    Task<StartupChangePreview> PlanChangeAsync(string purpose, string? backupDestination);
    Task<StartupChangePreview> PrepareChangeAsync(string expectedPlan, string purpose, string backupDestination);
    Task BackupAsync(string destination);
    Task AdoptRestoreAsync(string backup, string restoredDataRoot, string expectedBackupHash, string expectedRegistrationHash);
}
