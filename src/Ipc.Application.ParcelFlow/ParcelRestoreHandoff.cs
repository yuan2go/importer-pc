using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelStartupMaintenance
{
    internal Func<string>? HandoffMachineIdentity { get; set; }
    public Task AdoptRestoreAsync(string backup, string restoredDataRoot, string expectedBackupHash, string expectedRegistrationHash) =>
        throw new InvalidOperationException("接管需要本机受保护部署登记，由安装装配入口执行；不能在通用业务层改登记");
    public async Task AdoptRestoreOwnedAsync(string backup, string targetRoot, string expectedBackupHash, Func<StartupRestoreHandoff, Task> commit, DesktopRecoveryMigration? ownership = null)
    {
        Authorize();
        if (definition.Id == "sportmaster" && OperatingSystem.IsWindows())
        {
            if (ownership is null) throw new InvalidOperationException("恢复接管必须持有原应用的真实停机所有权。");
            ownership.RequireStoppedSource(definition.Id, launch.DataRoot);
        }
        var targetRootFull = Path.GetFullPath(targetRoot);
        var intent = OriginalRestoreIntents().SingleOrDefault(i => i.TargetRoot == targetRootFull && i.BackupIdentity == expectedBackupHash);
        var c = intent is null ? BackupConfiguration(backup) : RestoredConfiguration(intent.TargetDirectory, ReadIsolatedRestore(intent.TargetDirectory, expectedBackupHash, verifyCopy: false));
        var target = Path.GetFullPath(Path.Combine(targetRootFull, c.Mode.ToString(), c.StationId, definition.Id));
        var source = Path.GetFullPath(Path.Combine(launch.DataRoot, c.Mode.ToString(), c.StationId, definition.Id));
        RecoveryFiles.RequireOrdinaryAncestors(source); RecoveryFiles.RequireOrdinaryAncestors(target);
        var evidence = RecoveryEvidence(c);
        var deployed = evidence.Deployment(definition.Id);
        if (deployed is not null && Path.GetFullPath(deployed.DataRoot) == targetRootFull && File.Exists(Path.Combine(target, "restore-handoff.json")))
        {
            var committed = ParcelRestoreEvidence.Require(target, c, evidence);
            if (committed.BackupIdentity != expectedBackupHash) throw new InvalidDataException("已接管目录属于另一恢复身份，原件保留。");
            return; // Positive original commit, never a second handoff.
        }
        if (intent is not null && deployed is not null && (deployed.LifecycleState != "Installed" || deployed.ApplicationId != intent.Source.ApplicationId ||
            deployed.RuntimeAccountSid != intent.Source.RuntimeAccountSid || Path.GetFullPath(deployed.DataRoot) != Path.GetFullPath(intent.Source.DataRoot)))
            throw new InvalidOperationException("原恢复对应的部署已变化，请核对原次，不接管到新的安装。");
        if (source == target) throw new InvalidOperationException("恢复接管需要独立隔离目录，不能覆盖原目录。");
        var manifest = ReadIsolatedRestore(target, expectedBackupHash, verifyCopy: true);
        if (!manifest.Files.TryGetValue(ParcelMachineOrigin.FileName, out var originHash) || !manifest.Files.TryGetValue("configuration.json", out var configurationHash))
            throw new InvalidDataException("恢复缺少原机器或配置保管证据，不能补盖来源。");
        if (definition.Id == "sportmaster" && intent is null && evidence.RequireRegistration && HandoffMachineIdentity is null)
            throw new InvalidDataException("隔离副本没有受保护原恢复意图，请核对当前恢复入口，不能猜测接管。");
        var sourceExists = Directory.Exists(source);
        if (definition.Id == "sportmaster" && Directory.Exists(launch.DataRoot) && OtherRootBlocker(source) is { } otherRoot)
            throw new InvalidDataException(otherRoot);
        using var sourceOwner = sourceExists && ownership is null ? new ResourceLease(source, ["application-owner"]) : null;
        using var targetOwner = new ResourceLease(target, ["application-owner"]);
        using var targetCopy = CopyForMaintenance(target);
        if (ParcelRepository.ReadStorageVersion(targetCopy.Database, definition.Id) != ParcelRepository.CurrentStorageVersion)
            throw new InvalidDataException("恢复交接仅支持当前 DB13，原件保留，不转换旧格式。");
        var minimum = ParcelRepository.ReadIntegrationRequestIdFloor(targetCopy.Database);
        DeploymentPurgedRestoreSource? purged = null;
        if (sourceExists)
        {
            ParcelMachineOrigin.Require(source, c, evidence);
            using var sourceCopy = CopyForMaintenance(source);
            if (ParcelRepository.ReadStorageVersion(sourceCopy.Database, definition.Id) != ParcelRepository.CurrentStorageVersion)
                throw new InvalidDataException("原来源不属于当前 DB13，保留原件。");
            ParcelRepository.RequireHandoffSourceEquivalent(sourceCopy.Database, targetCopy.Database);
            if (manifest.Files.Any(f => !File.Exists(RecoveryFiles.Resolve(source, f.Key)) || Wire.FileHash(RecoveryFiles.Resolve(source, f.Key)) != f.Value) ||
                !new[] { "configuration.json", "overload.json", ParcelMachineOrigin.FileName }.Where(n => File.Exists(Path.Combine(source, n))).Concat(RecoveryFiles.Enumerate(source, "receipts"))
                    .Concat(RecoveryFiles.Enumerate(source, "images", skipImageLeases: true)).Concat(RecoveryFiles.MaintenanceEvidence(source)).Order(StringComparer.Ordinal)
                    .SequenceEqual(manifest.Files.Keys.Order(StringComparer.Ordinal)))
                throw new InvalidDataException("备份后的原配置、回执、图片或维护证据有变化；保全原责任，不能忽略差异。");
            minimum = Math.Max(minimum, ParcelRepository.ReadIntegrationRequestIdFloor(sourceCopy.Database));
        }
        else
        {
            var current = deployed ?? throw new InvalidDataException("原目录已不存在且缺少本机登记，保持隔离。");
            purged = evidence.PurgedSource(current, source, configurationHash, originHash);
            evidence.VerifyPurge(purged);
            source = purged.SourceDirectory;
            ParcelMachineOrigin.RequireBackupClaim(Path.Combine(target, ParcelMachineOrigin.FileName), c);
        }
        var proofFile = Path.Combine(target, "restore-handoff.json");
        StartupRestoreHandoff proof;
        if (File.Exists(proofFile))
        {
            proof = ParcelRestoreEvidence.Require(target, c, evidence, requireRegistration: false, requireSourceFence: false);
            if (proof.BackupIdentity != expectedBackupHash || proof.SourceDirectory != source || Wire.Encode(proof.PurgedSource) != Wire.Encode(purged) ||
                intent is not null && proof.Id != intent.OperationId || proof.MinimumIntegrationId < minimum)
                throw new InvalidDataException("原恢复交接与当前意图或编号账不一致；保留原记录。");
        }
        else
        {
            proof = new StartupRestoreHandoff(intent?.OperationId ?? Wire.Id(), source, target, expectedBackupHash, configurationHash,
                Wire.FileHash(Path.Combine(target, "RESTORED-ISOLATED.json")), evidence.PolicyIdentity(), DateTimeOffset.UtcNow)
            { PurgedSource = purged, MinimumIntegrationId = minimum };
            ExtensionStore.AtomicWrite(proofFile, proof);
        }
        if (purged is null)
        {
            var fence = Path.Combine(source, "MIGRATION-SOURCE-FENCED.json");
            if (File.Exists(fence))
            {
                if (Wire.Encode(Wire.Decode<StartupRestoreHandoff>(ParcelRestoreEvidence.Read(fence))) != Wire.Encode(proof))
                    throw new InvalidDataException("原根已有不同退役围栏；保留原交接，不覆盖。");
            }
            else ExtensionStore.AtomicWrite(fence, proof);
        }
        try { Authorize(); await commit(proof); }
        catch (Exception error) { throw new InstallationOutcomeUnknownException("恢复接管结果未确认；原交接、隔离副本和退役证据保留，请核对同一原次。", error); }
    }
}

public sealed partial class ParcelRuntime
{
    bool RestoreConfirmed => !File.Exists(Path.Combine(directory!, "RESTORED-ISOLATED.json")) || RestoreConfirmationValid();
    bool RestoreConfirmationValid() => ParcelRestoreEvidence.ConfirmationValid(directory!, ReadRestoreHandoff,
        (id, hash) => repository?.HasRestoreConfirmation(id, hash) == true);
    internal RestoreMachineEvidence? RestoreEvidence { get; set; }
    StartupRestoreHandoff ReadRestoreHandoff() => ParcelRestoreEvidence.Require(directory!, Effective, RestoreEvidence ?? RestoreMachineEvidence.For(Effective));
    void ConfirmRestore(BusinessCommand command)
    {
        var expectedId = command.Owner;
        RestoreVerificationInput input;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(command.Value ?? "");
            if (!document.RootElement.TryGetProperty(nameof(RestoreVerificationInput.VerifiedRequestIdHighWater), out var floor) || !floor.TryGetInt64(out _))
                throw new InvalidDataException("必须明确填写已核实的请求编号高水位，缺失证据不能按零处理。");
            input = Wire.Decode<RestoreVerificationInput>(command.Value ?? "");
        }
        catch (System.Text.Json.JsonException error) { throw new InvalidDataException("填写恢复核对说明和已核实的请求编号高水位。", error); }
        var reason = input.Reason;
        if (!CanConfigure || string.IsNullOrWhiteSpace(reason) || reason.Length > 1024) throw new InvalidOperationException("先暂停并认证维护，填写原请求/设备/外部事实核对说明");
        var proof = ReadRestoreHandoff();
        if (expectedId != proof.Id) throw new InvalidOperationException("交接身份已变化，请重新核对原交接记录。");
        if (factory.AdmissionFailure(Effective) is { } readinessFailure) throw new InvalidOperationException(readinessFailure);
        if (input.VerifiedRequestIdHighWater < Math.Max(proof.MinimumIntegrationId, repository!.IntegrationRequestIdFloor()))
            throw new InvalidOperationException("核实的请求编号高水位不能低于原永久编号账；原记录和隔离状态保留。");
        if (input.VerifiedRequestIdHighWater == long.MaxValue)
            throw new InvalidOperationException("协议请求编号已耗尽，不能确认启用；原账与隔离状态保留，需核对真实 RCS 编号责任。");
        using var sourceOwner = proof.PurgedSource is null ? new ResourceLease(proof.SourceDirectory, ["application-owner"]) : null;
        repository.AdmitCommand(command);
        var confirmation = new ParcelRestoreEvidence.Confirmation(proof.Id, ManagementEvidence!.AuditActor, reason, DateTimeOffset.UtcNow, input.VerifiedRequestIdHighWater);
        ExtensionStore.AtomicWrite(Path.Combine(directory!, "RESTORE-CONFIRMED.json"), confirmation);
        repository!.Commit(null, "RestoreHandoffConfirmed", Wire.Encode(new { HandoffId = proof.Id, ConfirmationHash = Wire.Hash(Wire.Encode(confirmation)), Actor = confirmation.Actor, Reason = reason, confirmation.VerifiedRequestIdHighWater }), paused: true, command: command,
            restoreFloor: new RestoreNumberFloor(proof.Id, input.VerifiedRequestIdHighWater));
    }
}
public sealed partial class ParcelRepository
{
    internal static string HandoffDatabaseIdentity(string database, IReadOnlySet<string>? omitRows = null)
    {
        using var db = OpenHandoffSnapshot(database);
        using var tx = db.BeginTransaction(deferred: true);
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        void Add(string value) { var bytes = System.Text.Encoding.UTF8.GetBytes(value); hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes); }
        static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
        using var schema = db.CreateCommand(); schema.Transaction = tx; schema.CommandText = "SELECT name,sql FROM sqlite_master WHERE type='table' ORDER BY name";
        List<(string Name, string Sql)> tables = [];
        using (var r = schema.ExecuteReader()) while (r.Read()) { if (tables.Count == 128) throw new InvalidDataException("Unexpected storage table quota"); tables.Add((r.GetString(0), r.GetString(1))); }
        foreach (var table in tables)
        {
            Add(table.Name); Add(table.Sql);
            if (omitRows?.Contains(table.Name) == true) continue;
            using var info = db.CreateCommand(); info.Transaction = tx; info.CommandText = "PRAGMA table_info(" + Quote(table.Name) + ")";
            List<string> columns = [];
            using (var r = info.ExecuteReader()) while (r.Read()) { if (columns.Count == 64) throw new InvalidDataException("Unexpected storage column quota"); columns.Add(Quote(r.GetString(1))); }
            using var rows = db.CreateCommand(); rows.Transaction = tx; rows.CommandText = "SELECT * FROM " + Quote(table.Name) + " ORDER BY " + string.Join(",", columns);
            using var reader = rows.ExecuteReader();
            while (reader.Read())
                for (int n = 0; n < reader.FieldCount; n++) Add(reader.IsDBNull(n) ? "null" : reader.GetValue(n) switch { byte[] b => "bytes:" + Convert.ToBase64String(b), string s => "text:" + s, object v => v.GetType().Name + ":" + Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) });
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    internal bool HasRestoreConfirmation(string id, string hash) => HasRestoreConfirmation(db, id, hash);
    internal static bool HasRestoreConfirmation(Microsoft.Data.Sqlite.SqliteConnection connection, string id, string hash)
    {
        using var c = connection.CreateCommand(); c.CommandText = "SELECT 1 FROM parcel_audit WHERE event='RestoreHandoffConfirmed' AND json_valid(detail) AND json_extract(detail,'$.HandoffId')=$id AND json_extract(detail,'$.ConfirmationHash')=$hash ORDER BY seq DESC LIMIT 1";
        c.Parameters.AddWithValue("$id", id); c.Parameters.AddWithValue("$hash", hash); return c.ExecuteScalar() is not null;
    }
}
