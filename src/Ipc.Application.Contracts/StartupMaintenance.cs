using Ipc.Platform;

namespace Ipc.Application.Contracts;

public sealed record StartupMaintenanceDocument(ParcelConfiguration? Candidate, CapabilityCatalog Catalog,
    string ConfigurationHash, long Revision, bool ExistingConfiguration, string? RuntimeDirectory, string[] Diagnostics, string ConfigurationStatus = "Unknown");
public sealed record StartupClassification(bool ExistingConfiguration, bool Readable, string[] Diagnostics);
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
public sealed record StartupRetentionPlan(string Identity, string ConfigurationHash, string DatabaseHash, string BackupDestination, int Days, string Summary);
public sealed record StartupRetentionOutcome(string Status, string Identity, string Detail, string? Run = null, string? BackupHash = null);
public interface IStartupRetentionMaintenance
{
    Task<StartupRetentionPlan> PlanRetentionAsync(string destination, CancellationToken token = default);
    Task<StartupRetentionOutcome> ExecuteRetentionAsync(StartupRetentionPlan plan);
    Task<StartupRetentionOutcome> InspectRetentionAsync(StartupRetentionPlan plan);
}

// Independent of IBusinessRuntime: no device/session is required to inspect or repair startup.
public interface IStartupMaintenance
{
    Task<StartupClassification> ClassifyAsync() => throw new NotSupportedException("Startup routing metadata unavailable");
    Task<BarcodeFilterResult> TestBarcodeFilterAsync(BarcodeFilterPolicy policy, string[] candidates) => throw new NotSupportedException("Barcode filter preview unavailable");
    Task<StartupMaintenanceDocument> ReadAsync();
    Task<BusinessReceipt> ValidateAsync(ParcelConfiguration candidate);
    Task<StartupCheckReport> CheckAsync(ParcelConfiguration candidate);
    Task<StartupMaintenanceDocument> StageAsync(string expectedHash, ParcelConfiguration candidate, string reason);
    Task<StartupMaintenanceDocument> RepairInitialAsync(string expectedHash, ParcelConfiguration candidate, string reason);
    Task<StartupSavedDraft?> ReadDraftAsync();
    Task<StartupSavedDraft> SaveDraftAsync(string expectedConfigurationHash, string? expectedDraftHash, StartupEditorState editor);
    Task<StartupRecoveryPreview> PlanRestoreAsync(string backup, string destination);
    Task RestoreAsync(string backup, string destination, string expectedBackupHash);
    Task<StartupBackupItem[]> ListBackupsAsync(string directory, CancellationToken token);
    Task<StartupBackupItem[]> ListBackupsAsync(string directory,IProgress<MaintenanceProgress>? progress,CancellationToken token)
        => throw new NotSupportedException("此维护实现不支持真实进度与取消");
    Task<StartupChangePreview> PlanChangeAsync(string purpose, string? backupDestination);
    Task<StartupChangePreview> PrepareChangeAsync(string expectedPlan, string purpose, string backupDestination);
    Task<StartupChangePreview> PlanChangeAsync(string purpose, string? backupDestination, IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => throw new NotSupportedException("此维护实现不支持真实进度与取消");
    Task<StartupChangePreview> PrepareChangeAsync(string expectedPlan, string purpose, string backupDestination, IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => throw new NotSupportedException("此维护实现不支持真实进度与取消");
    Task<StartupRecoveryPreview> PlanRestoreAsync(string backup, string destination, IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => throw new NotSupportedException("此维护实现不支持真实进度与取消");
    Task BackupAsync(string destination);
    Task BackupAsync(string destination, IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => throw new NotSupportedException("此维护实现不支持真实进度与取消");
    Task RestoreAsync(string backup, string destination, string expectedBackupHash, IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => throw new NotSupportedException("此维护实现不支持真实进度与取消");
    Task AdoptRestoreAsync(string backup, string restoredDataRoot, string expectedBackupHash, string expectedRegistrationHash);
}
