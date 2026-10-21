using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelStartupMaintenance
{
    internal Func<string>? HandoffMachineIdentity { get; set; }
    public Task AdoptRestoreAsync(string backup, string restoredDataRoot, string expectedBackupHash, string expectedRegistrationHash) =>
        throw new InvalidOperationException("接管需要本机受保护部署登记，由安装装配入口执行；不能在通用业务层改登记");
    public async Task AdoptRestoreOwnedAsync(string backup, string targetRoot, string expectedBackupHash, Func<StartupRestoreHandoff, Task> commit)
    {
        Authorize();
        if (BackupIdentity(backup) != expectedBackupHash) throw new InvalidDataException("备份在预览后变化");
        var c = BackupConfiguration(backup);
        var source = Path.GetFullPath(Path.Combine(launch.DataRoot, c.Mode.ToString(), c.StationId, definition.Id));
        var target = Path.GetFullPath(Path.Combine(targetRoot, c.Mode.ToString(), c.StationId, definition.Id));
        RecoveryFiles.RequireOrdinaryAncestors(source); RecoveryFiles.RequireOrdinaryAncestors(target);
        if (source == target || !Directory.Exists(source)) throw new InvalidOperationException("原机接管须能核验原根；换机来源退役证据缺失时保持隔离");
        using var sourceOwner = new ResourceLease(source, ["application-owner"]);
        using var targetOwner = new ResourceLease(target, ["application-owner"]);
        var isolated = Path.Combine(target, "RESTORED-ISOLATED.json");
        if (!File.Exists(isolated) || Wire.FileHash(isolated) != Wire.FileHash(Path.Combine(backup + ".recovery", "COMPLETED.json"))) throw new InvalidDataException("目标不是本备份的完整隔离恢复根");
        var manifest = Wire.Decode<ParcelRecoveryManifest>(File.ReadAllText(isolated));
        // Verify immutable restoration inputs before the first startup mutates the DB.
        if (Wire.FileHash(Path.Combine(target, definition.Id + ".db")) != manifest.DatabaseHash || manifest.Files.Any(f => Wire.FileHash(RecoveryFiles.Resolve(target, f.Key)) != f.Value))
            throw new InvalidDataException("目标在恢复后已变化；重新恢复到新根并核对，不猜测接管对象");
        // A historical backup must not retire a newer source containing admissions,
        // requests or evidence absent from the restored DB. Compare logical SQLite
        // contents on private copies (checkpoint/WAL byte layout may differ).
        using var sourceCopy = CopyForMaintenance(source);
        using var targetCopy = CopyForMaintenance(target);
        if (ParcelRepository.ReadStorageVersion(sourceCopy.Database, definition.Id) != 8 || ParcelRepository.ReadStorageVersion(targetCopy.Database, definition.Id) != 8)
            throw new InvalidDataException("原机接管要求两根均为DB8：先在原根按新发布清偿并受控迁移，再创建/恢复新的完整备份，防止不认识退役围栏的旧程序启动原根");
        ParcelMachineOrigin.Require(source, c);
        if (!manifest.Files.ContainsKey(ParcelMachineOrigin.FileName)) throw new InvalidDataException("该备份没有原机器来源保管证据，保持隔离；不能事后给旧备份补盖证明");
        if (ParcelRepository.HandoffDatabaseIdentity(sourceCopy.Database) != ParcelRepository.HandoffDatabaseIdentity(targetCopy.Database) ||
            Wire.FileHash(Path.Combine(source, "configuration.json")) != manifest.Files["configuration.json"] ||
            manifest.Files.Any(f => !File.Exists(RecoveryFiles.Resolve(source, f.Key)) || Wire.FileHash(RecoveryFiles.Resolve(source, f.Key)) != f.Value) ||
            !new[] { "configuration.json", "overload.json", ParcelMachineOrigin.FileName }.Where(n => File.Exists(Path.Combine(source, n))).Concat(RecoveryFiles.Enumerate(source, "receipts")).Concat(RecoveryFiles.Enumerate(source, "images", skipImageLeases: true))
                .Concat(RecoveryFiles.MaintenanceEvidence(source))
                .Order(StringComparer.Ordinal).SequenceEqual(manifest.Files.Keys.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("原根在备份后已变化；先保全原责任，停机后创建并恢复新的完整备份。不能用旧备份退役较新的原根");
        var policy = Path.Combine(WindowsDeploymentSetup.MachineRoot, "runtime-policy.json");
        var machine = HandoffMachineIdentity?.Invoke() ?? (OperatingSystem.IsWindows() ? Wire.FileHash(policy) : c.Mode == RunMode.Simulation ? "PortableSimulation" : throw new InvalidOperationException("换机Production须真实交接资格"));
        var proof = new StartupRestoreHandoff(Wire.Id(), source, target, expectedBackupHash,
            File.Exists(Path.Combine(source, "configuration.json")) ? Wire.FileHash(Path.Combine(source, "configuration.json")) : "MissingPreserved",
            Wire.FileHash(isolated), machine, DateTimeOffset.UtcNow);
        var fence = Path.Combine(source, "MIGRATION-SOURCE-FENCED.json");
        if (File.Exists(fence))
        {
            var existing = Wire.Decode<StartupRestoreHandoff>(File.ReadAllText(fence));
            if (existing.TargetDirectory != target || existing.BackupIdentity != expectedBackupHash || existing.MachinePolicyHash != machine) throw new InvalidOperationException("原根已有不同迁移围栏；保留原交接并核对");
            proof = existing;
        }
        ExtensionStore.AtomicWrite(Path.Combine(target, "restore-handoff.json"), proof);
        ExtensionStore.AtomicWrite(fence, proof); // Source is retired before the deployment pointer can change.
        await commit(proof);
    }
}

public sealed partial class ParcelRuntime
{
    bool RestoreConfirmed => !File.Exists(Path.Combine(directory!, "RESTORED-ISOLATED.json")) || RestoreConfirmationValid();
    bool RestoreConfirmationValid()
    {
        var file = Path.Combine(directory!, "RESTORE-CONFIRMED.json");
        if (!File.Exists(file)) return false;
        try
        {
            var proof = ReadRestoreHandoff();
            var json = File.ReadAllText(file);
            return Wire.Decode<RestoreConfirmation>(json).HandoffId == proof.Id && repository?.HasRestoreConfirmation(proof.Id, Wire.Hash(json)) == true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException) { return false; }
    }
    sealed record RestoreConfirmation(string HandoffId, string Actor, string Reason, DateTimeOffset At);
    StartupRestoreHandoff ReadRestoreHandoff()
    {
        var handoff = Path.Combine(directory!, "restore-handoff.json"); RecoveryFiles.RequireOrdinaryAncestors(handoff);
        static string Read(string file) { RecoveryFiles.RequireOrdinaryAncestors(file); if (new FileInfo(file).Length > 65536) throw new InvalidDataException("交接证据超出预算"); return File.ReadAllText(file); }
        var proof = Wire.Decode<StartupRestoreHandoff>(Read(handoff));
        if (!Path.IsPathFullyQualified(proof.SourceDirectory) || proof.SourceDirectory.StartsWith(@"\\", StringComparison.Ordinal)) throw new InvalidDataException("原机退役路径必须是本地绝对目录");
        RecoveryFiles.RequireOrdinaryAncestors(proof.SourceDirectory);
        ParcelMachineOrigin.Require(proof.SourceDirectory, Effective);
        var manifestFile = Path.Combine(directory!, "RESTORED-ISOLATED.json"); RecoveryFiles.RequireOrdinaryAncestors(manifestFile);
        if (new FileInfo(manifestFile).Length > RecoveryFiles.MaximumManifestBytes) throw new InvalidDataException("恢复来源清单超出预算");
        var manifest = Wire.Decode<ParcelRecoveryManifest>(File.ReadAllText(manifestFile));
        if (!manifest.Files.TryGetValue(ParcelMachineOrigin.FileName, out var originHash) ||
            Wire.FileHash(Path.Combine(directory!, ParcelMachineOrigin.FileName)) != originHash || Wire.FileHash(Path.Combine(proof.SourceDirectory, ParcelMachineOrigin.FileName)) != originHash)
            throw new InvalidDataException("恢复集的原机器来源证据缺失或变化；不能解除隔离");
        var sourceFence = Path.Combine(proof.SourceDirectory, "MIGRATION-SOURCE-FENCED.json");
        var policy = Path.Combine(WindowsDeploymentSetup.MachineRoot, "runtime-policy.json");
        var machine = OperatingSystem.IsWindows() ? Wire.FileHash(policy) : Effective.Mode == RunMode.Simulation ? "PortableSimulation" : "Unverified";
        if (proof.TargetDirectory != Path.GetFullPath(directory!) || proof.MachinePolicyHash != machine ||
            proof.SourceConfigurationHash != Wire.FileHash(Path.Combine(proof.SourceDirectory, "configuration.json")) ||
            proof.TargetManifestHash != Wire.FileHash(Path.Combine(directory!, "RESTORED-ISOLATED.json")) || Wire.Encode(Wire.Decode<StartupRestoreHandoff>(Read(sourceFence))) != Wire.Encode(proof))
            throw new InvalidDataException("原机退役围栏/目标/机器证据不匹配；不能以文字确认换机交接");
        if (OperatingSystem.IsWindows())
        {
            var deployment = WindowsDeploymentSetup.Current(Id);
            if (deployment?.LifecycleState != "Installed" || deployment.RestoreHandoffHash != Wire.Hash(Wire.Encode(proof)) ||
                Path.GetFullPath(Path.Combine(deployment.DataRoot, Effective.Mode.ToString(), Effective.StationId, Id)) != Path.GetFullPath(directory!))
                throw new InvalidDataException("缺少本机受保护登记的同一恢复接管身份；保持隔离");
        }
        return proof;
    }
    void ConfirmRestore(string? expectedId, string? reason)
    {
        if (!CanConfigure || string.IsNullOrWhiteSpace(reason) || reason.Length > 1024) throw new InvalidOperationException("先暂停并认证维护，填写原请求/设备/外部事实核对说明");
        var proof = ReadRestoreHandoff();
        if (expectedId != proof.Id || factory.AdmissionFailure(Effective) is not null) throw new InvalidOperationException("交接身份变化或部署资格不足");
        using var sourceOwner = new ResourceLease(proof.SourceDirectory, ["application-owner"]);
        var confirmation = new RestoreConfirmation(proof.Id, AdministratorIdentity!.Subject, reason, DateTimeOffset.UtcNow);
        ExtensionStore.AtomicWrite(Path.Combine(directory!, "RESTORE-CONFIRMED.json"), confirmation);
        repository!.Commit(null, "RestoreHandoffConfirmed", Wire.Encode(new { HandoffId = proof.Id, ConfirmationHash = Wire.Hash(Wire.Encode(confirmation)), Actor = confirmation.Actor, Reason = reason }), paused: true);
    }
}
public sealed partial class ParcelRepository
{
    internal static string HandoffDatabaseIdentity(string database)
    {
        using var db = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = database, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
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
    internal bool HasRestoreConfirmation(string id, string hash)
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT 1 FROM parcel_audit WHERE event='RestoreHandoffConfirmed' AND json_valid(detail) AND json_extract(detail,'$.HandoffId')=$id AND json_extract(detail,'$.ConfirmationHash')=$hash ORDER BY seq DESC LIMIT 1";
        c.Parameters.AddWithValue("$id", id); c.Parameters.AddWithValue("$hash", hash); return c.ExecuteScalar() is not null;
    }
}
