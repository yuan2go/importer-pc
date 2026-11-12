using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelStartupMaintenance
{
    internal RestoreMachineEvidence? RestoreEvidence { get; set; }
    internal Func<DeploymentRestoreIntent[]>? RestoreIntentReader { get; set; }
    internal Func<string, string, string, string, DeploymentRestoreOperation>? RestoreIntentWriter { get; set; }
    internal Func<IDisposable>? RestoreInspectionOwner { get; set; }
    RestoreMachineEvidence RecoveryEvidence(ParcelConfiguration c) => HandoffMachineIdentity is null
        ? RestoreEvidence ?? RestoreMachineEvidence.For(c) : (RestoreEvidence ?? RestoreMachineEvidence.For(c)) with { PolicyIdentity = HandoffMachineIdentity };
    DeploymentRestoreIntent[] OriginalRestoreIntents() => RestoreIntentReader?.Invoke() ??
        (definition.Id == "sportmaster" && OperatingSystem.IsWindows() && File.Exists(Path.Combine(WindowsDeploymentSetup.MachineRoot, "runtime-policy.json"))
            ? WindowsDeploymentSetup.ReadRestoreIntents(definition.Id) : []);
    DeploymentRestoreOperation OpenOriginalRestoreCopy(string backup, string identity, string root, string target)
    {
        if (RestoreIntentWriter is not null) return RestoreIntentWriter(backup, identity, root, target);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("生产恢复使用受保护部署入口。");
        return WindowsDeploymentSetup.PrepareRestoreIntent(definition.Id, backup, identity, root, target);
    }
    internal static ParcelRecoveryManifest ReadIsolatedRestore(string target, string backupIdentity, bool verifyCopy)
    {
        var file = Path.Combine(target, "RESTORED-ISOLATED.json"); RecoveryFiles.RequireOrdinaryAncestors(file);
        if (new FileInfo(file).Length > RecoveryFiles.MaximumManifestBytes) throw new InvalidDataException("恢复完成标记超过预算。");
        var manifest = Wire.Decode<ParcelRecoveryManifest>(File.ReadAllText(file));
        if (Wire.Hash(manifest.DatabaseHash + Wire.FileHash(file)) != backupIdentity || File.Exists(Path.Combine(target, "RESTORE-INCOMPLETE.json")))
            throw new InvalidDataException("隔离目标不是原次完整恢复，或存在冲突标记。");
        if (verifyCopy && (Wire.FileHash(Path.Combine(target, manifest.Application + ".db")) != manifest.DatabaseHash ||
            manifest.Files.Any(f => Wire.FileHash(RecoveryFiles.Resolve(target, f.Key)) != f.Value)))
            throw new InvalidDataException("隔离恢复文件已缺失或变化；原件保留，不覆盖或猜测接管对象。");
        return manifest;
    }
    ParcelConfiguration RestoredConfiguration(string target, ParcelRecoveryManifest manifest)
    {
        var envelope = ConfigurationRevisions<ParcelConfiguration>.ReadCurrentEnvelope(File.ReadAllText(Path.Combine(target, "configuration.json")));
        var c = envelope.Effective;
        if (c.ApplicationId != definition.Id || manifest.Application != definition.Id || definition.Validate(c).Length != 0 ||
            envelope.Pending is { } pending && (definition.Validate(pending).Length != 0 || pending.ApplicationId != c.ApplicationId || pending.StationId != c.StationId || pending.Mode != c.Mode) ||
            !target.EndsWith(Path.DirectorySeparatorChar + Path.Combine(c.Mode.ToString(), c.StationId, definition.Id), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("恢复配置、站点和当前格式不一致，保持隔离。");
        return c;
    }
    public string RestoredSeed(string targetRoot, string backupIdentity)
    {
        Authorize();
        var intent = OriginalRestoreIntents().Single(i => i.TargetRoot == Path.GetFullPath(targetRoot) && i.BackupIdentity == backupIdentity);
        var manifest = ReadIsolatedRestore(intent.TargetDirectory, backupIdentity, verifyCopy: true);
        return Wire.Encode(RestoredConfiguration(intent.TargetDirectory, manifest));
    }
    public Task<StartupRecoveryPreview[]> ReadRestoreOperationsAsync()
    {
        Authorize();
        using var owner = RestoreInspectionOwner?.Invoke() ?? (definition.Id == "sportmaster" && OperatingSystem.IsWindows() &&
            File.Exists(Path.Combine(WindowsDeploymentSetup.MachineRoot, "runtime-policy.json")) ? WindowsDeploymentSetup.HoldRestoreInspection(definition.Id) : null);
        return Task.FromResult(OriginalRestoreIntents().Select(ProjectRestore).ToArray());
    }
    StartupRecoveryPreview ProjectRestore(DeploymentRestoreIntent intent)
    {
        StartupRecoveryPreview Result(string state, string summary, ParcelConfiguration? c = null) =>
            new(intent.BackupIdentity, intent.TargetDirectory, summary, intent.TargetRoot)
            { OperationId = intent.OperationId, Backup = intent.Backup, State = state, Station = c?.StationId, Mode = c?.Mode };
        try
        {
            WindowsDeploymentSetup.ValidateRestoreIntent(intent);
            var current = (RestoreEvidence ?? RestoreMachineEvidence.For(definition.InitialConfiguration())).Deployment(definition.Id);
            if (current is null && (RestoreEvidence ?? RestoreMachineEvidence.For(definition.InitialConfiguration())).RequireRegistration)
                return Result("Unknown", "缺少本机当前部署登记；先完成安装，不猜测原恢复可接管。");
            var isolated = File.Exists(Path.Combine(intent.TargetDirectory, "RESTORED-ISOLATED.json"));
            if (isolated)
            {
                var manifest = ReadIsolatedRestore(intent.TargetDirectory, intent.BackupIdentity, verifyCopy: false);
                var c = RestoredConfiguration(intent.TargetDirectory, manifest); var evidence = RecoveryEvidence(c);
                if (current?.LifecycleState == "Installed" && Path.GetFullPath(current.DataRoot) == intent.TargetRoot)
                {
                    var restoreProof = ParcelRestoreEvidence.Require(intent.TargetDirectory, c, evidence);
                    var status = OperatingSystem.IsWindows() && RestoreIntentReader is null ? WindowsDeploymentSetup.RestoreCommitStatus(definition.Id, intent.OperationId) : "AdoptedIsolated";
                    if (status is not ("ProjectionPending" or "AdoptedIsolated"))
                        return Result("Unknown", "恢复登记存在，但原提交回执缺失或冲突；原件保留，核对原回执和当前系统入口，不重复接管。", c);
                    if (status == "ProjectionPending") return Result(status, "原恢复已提交，系统入口待修复；尚未确认恢复责任，不自动供包。", c);
                    using var applicationOwner = ResourceLease.Existing(intent.TargetDirectory, ["application-owner"]);
                    using var copy = CopyForMaintenance(intent.TargetDirectory);
                    using var snapshot = ParcelRepository.OpenHandoffSnapshot(copy.Database);
                    var confirmed = ParcelRestoreEvidence.ConfirmationValid(intent.TargetDirectory,
                        () => ParcelRestoreEvidence.Require(intent.TargetDirectory, c, evidence),
                        (id, hash) => ParcelRepository.HasRestoreConfirmation(snapshot, id, hash));
                    using var query = snapshot.CreateCommand();
                    // Reconfirming this handoff updates verification evidence, not its first enablement boundary.
                    query.CommandText = "SELECT paused, EXISTS(SELECT 1 FROM parcel_audit WHERE event='AdmissionResumed' AND seq > (SELECT MIN(seq) FROM parcel_audit WHERE event='RestoreHandoffConfirmed' AND json_valid(detail) AND json_extract(detail,'$.HandoffId')=$id)) FROM parcel_meta WHERE id=1";
                    query.Parameters.AddWithValue("$id", restoreProof.Id);
                    using var row = query.ExecuteReader();
                    if (!row.Read()) throw new InvalidDataException("恢复业务状态缺失，不能推断已启用。");
                    var paused = row.GetBoolean(0); var enabled = row.GetBoolean(1);
                    if (!confirmed)
                        return Result(paused ? "AdoptedIsolated" : "Unknown", paused ? "原恢复接管已完成；恢复责任尚未确认，当前保持暂停；核对原请求、实物及编号后明确确认。" : "恢复责任尚未确认，但原业务状态未暂停，证据冲突，请核对原结果。", c)
                            with
                        { ResponsibilityStatus = "Unconfirmed", EverEnabled = null, BusinessPaused = paused };
                    if (!paused && !enabled) throw new InvalidDataException("当前业务启停与恢复启用审计不一致，须核对。");
                    return Result(enabled ? paused ? "EnabledPaused" : "Enabled" : "ConfirmedPaused",
                        "原恢复接管已完成；恢复责任已确认。" + (enabled ? paused ? "此后曾明确启用，当前正常暂停；不表示从未启用。" : "此后已明确启用，当前未暂停；具体投递准入仍由运行守卫决定。" : "尚无明确启用记录，当前保持暂停；核对后可明确恢复供包。"), c)
                        with
                    { ResponsibilityStatus = "Confirmed", EverEnabled = enabled, BusinessPaused = paused };
                }
                if (current is not null && (current.LifecycleState != "Installed" || current.RuntimeAccountSid != intent.Source.RuntimeAccountSid || Path.GetFullPath(current.DataRoot) != Path.GetFullPath(intent.Source.DataRoot)))
                    return Result("Superseded", "原恢复已被后续卸载、安装或接管取代；不能覆盖当前部署。", c);
                _ = ReadIsolatedRestore(intent.TargetDirectory, intent.BackupIdentity, verifyCopy: true);
                var handoff = File.Exists(Path.Combine(intent.TargetDirectory, "restore-handoff.json"));
                if (handoff) _ = ParcelRestoreEvidence.Require(intent.TargetDirectory, c, evidence, requireRegistration: false, requireSourceFence: false);
                return Result(handoff ? "HandoffPrepared" : "Isolated", handoff ? "原次交接已准备，尚未确认接管；继续核对同一原操作。" : "隔离恢复完整，尚未接管；确认原责任后可接管。", c);
            }
            if (current is not null && (current.LifecycleState != "Installed" || current.RuntimeAccountSid != intent.Source.RuntimeAccountSid || Path.GetFullPath(current.DataRoot) != Path.GetFullPath(intent.Source.DataRoot)))
                return Result("Superseded", "原复制来源已被取代，保留目标和证据，不重新接管。");
            if (!Directory.Exists(intent.TargetDirectory)) return Result("Planned", "原复制尚未落盘，可在核验原备份后继续。");
            using var execution = new MaintenanceExecution(null, default);
            var plan = ParcelRecovery.PlanRestore(intent.Backup, intent.TargetDirectory, Authorize, execution, intent);
            return Result(plan.State, "原复制未完成；续办复用原身份，核验已有文件，损坏的已知残件先隔离保管。");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        { return Result("Unknown", "原结果待核实，原件保留：" + error.Message); }
    }
}
