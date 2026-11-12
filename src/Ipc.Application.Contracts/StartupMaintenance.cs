using Ipc.Platform;

namespace Ipc.Application.Contracts;

public sealed record StartupMaintenanceDocument(ParcelConfiguration? Candidate, CapabilityCatalog Catalog,
    string ConfigurationHash, long Revision, bool ExistingConfiguration, string? RuntimeDirectory, string[] Diagnostics, string ConfigurationStatus = "Unknown");
public sealed record StartupClassification(bool ExistingConfiguration, bool Readable, string[] Diagnostics);
public sealed record StartupRecoveryPreview(string BackupSha256, string Destination, string Summary, string DataRoot)
{
    public string State { get; init; } = "Planned";
    public string? OperationId { get; init; }
    public string? Station { get; init; }
    public RunMode? Mode { get; init; }
    public string? Backup { get; init; }
    public string? ResponsibilityStatus { get; init; }
    public bool? EverEnabled { get; init; }
    public bool? BusinessPaused { get; init; }
}
public sealed record StartupEditorState(Dictionary<string, string> Values);
public sealed record StartupSavedDraft(string BaseConfigurationHash, string DraftHash, DateTimeOffset At, StartupEditorState Editor, bool BaseChanged, string? SourceIdentity = null);
public sealed record StartupCheck(string Layer, string Status, string Reason);
public sealed record StartupCheckReport(string CandidateHash, DateTimeOffset At, StartupCheck[] Checks);
public sealed record StartupBackupItem(string Path, string Status, DateTimeOffset? At, long Bytes, string? Identity, string Summary)
{
    public bool CanRestore => Status == "Verified";
    public bool CanDelete => Identity is not null && Status is "Verified" or "Deleting" or "OrphanDatabase" or "InvalidOrIncomplete";
    public string StatusText => Status switch { "Verified" => "完整，已核验", "OrphanDatabase" => "孤立数据库", "Deleting" => "删除未完成", "Deleted" => "已删除", _ => "残缺或无法核验" };
}
public sealed record StartupBackupSetResult(string Status, string Path, string Identity, string Detail, string[] Remaining);
public sealed record StartupChangePreview(string Identity, string Purpose, bool Allowed, string Summary, string ConfigurationHash, string? BackupDestination)
{
    public string? OperatorSummary { get; init; }
}
public sealed record StartupRollbackReview(DeploymentRollbackPlan Deployment, StartupChangePreview? Business);
public sealed record StartupVersionPruneReview(DeploymentPrunePlan Deployment, StartupChangePreview? Business);
public sealed record ProgramUninstallResult(WindowsRemovalReceipt? Removal, DeploymentPurgeReceipt? DataDeletion, bool DeleteData)
{
    public UninstallAssetsReceipt? Assets { get; init; }
}
public sealed record StartupSiteTemplate(int SchemaVersion, string Application, string SourceIdentity, StartupEditorState Defaults);
public sealed record StartupRestoreHandoff(string Id, string SourceDirectory, string TargetDirectory, string BackupIdentity,
    string SourceConfigurationHash, string TargetManifestHash, string MachinePolicyHash, DateTimeOffset At)
{
    public DeploymentPurgedRestoreSource? PurgedSource { get; init; }
    public long MinimumIntegrationId { get; init; }
}
public sealed record RestoreVerificationInput(string Reason, long VerifiedRequestIdHighWater);
public sealed record RestoreNumberFloor(string Identity, long Value);
public sealed record StartupRetentionPlan(string Identity, string ConfigurationHash, string DatabaseHash, string BackupDestination, int Days, string Summary);
public sealed record StartupRetentionOutcome(string Status, string Identity, string Detail, string? Run = null, string? BackupHash = null);
public interface IStartupRetentionMaintenance
{
    Task<StartupRetentionPlan> PlanRetentionAsync(string destination, CancellationToken token = default);
    Task<StartupRetentionOutcome> ExecuteRetentionAsync(StartupRetentionPlan plan);
    Task<StartupRetentionOutcome> InspectRetentionAsync(StartupRetentionPlan plan);
}

// Independent of IBusinessRuntime: no device/session is required to inspect or repair startup.
public interface IStartupMaintenance : IAsyncDisposable
{
    ValueTask IAsyncDisposable.DisposeAsync() => ValueTask.CompletedTask;
    Task<string> ReadCameraSerialAsync(string ip, string readerEngine) => throw new NotSupportedException("相机身份读取不可用");
    Task<StartupClassification> ClassifyAsync() => throw new NotSupportedException("Startup routing metadata unavailable");
    Task<BarcodeFilterResult> TestBarcodeFilterAsync(BarcodeFilterPolicy? policy, string[] candidates) => throw new NotSupportedException("Barcode filter preview unavailable");
    Task<StartupMaintenanceDocument> ReadAsync();
    Task<BusinessReceipt> ValidateAsync(ParcelConfiguration candidate);
    Task<StartupCheckReport> CheckAsync(ParcelConfiguration candidate);
    Task<StartupMaintenanceDocument> StageAsync(string expectedHash, ParcelConfiguration candidate, string reason);
    Task<StartupMaintenanceDocument> RepairInitialAsync(string expectedHash, ParcelConfiguration candidate, string reason);
    Task<StartupSavedDraft?> ReadDraftAsync();
    Task<StartupSavedDraft> SaveDraftAsync(string expectedConfigurationHash, string? expectedDraftHash, StartupEditorState editor);
    Task<StartupRecoveryPreview> PlanRestoreAsync(string backup, string destination);
    Task<StartupRecoveryPreview[]> ReadRestoreOperationsAsync() => Task.FromResult(Array.Empty<StartupRecoveryPreview>());
    Task RestoreAsync(string backup, string destination, string expectedBackupHash);
    Task<StartupBackupItem[]> ListBackupsAsync(string directory, CancellationToken token);
    Task<StartupBackupItem[]> ListBackupsAsync(string directory, IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => throw new NotSupportedException("此维护实现不支持真实进度与取消");
    Task<StartupBackupSetResult> InspectBackupDeliveryAsync(string backup, string identity, string destination)
        => throw new NotSupportedException("原完整备份交付核对不可用");
    Task<StartupBackupSetResult> ExportBackupSetAsync(string backup, string identity, string destination, IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => throw new NotSupportedException("完整备份集交付不可用");
    Task<StartupBackupSetResult> DeleteBackupSetAsync(string backup, string identity, IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => throw new NotSupportedException("完整备份集删除不可用");
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
