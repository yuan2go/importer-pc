using Ipc.Application.Contracts;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelStartupMaintenance
{
    public Task<string> CloseNeverAttemptedAsync(string operation, string expectedBodyHash, string reason, string backup,
        IProgress<MaintenanceProgress>? progress = null, CancellationToken token = default) => ObserveMaintenance(progress, token, async execution =>
    {
        Authorize();
        if ((!Guid.TryParseExact(operation, "N", out _) && !ExtensionSchema.IsId(operation)) || !ExtensionSchema.IsHash(expectedBodyHash) || string.IsNullOrWhiteSpace(reason) || reason.Length > 1024)
            throw new ArgumentException("须提供原作业身份、精确原件哈希及1～1024字符的实体核对依据");
        var state = ReadState();
        using var owner = new ResourceLease(state.Directory, ["application-owner"]);
        if (ReadState().Hash != state.Hash) throw new InvalidOperationException("配置在取得所有权时变化");
        using var copy = CopyForMaintenance(state.Directory, execution);
        _ = ParcelRepository.ReadMaintenanceStorageVersion(copy.Database, definition.Id);
        _ = ParcelRepository.InspectMaintenanceCore(copy.Database, definition, execution.Token, allowStorage10: true);
        ParcelOperation original;
        using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy.Database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            source.Open();
            original = ParcelRepository.ReadNeverAttempted(source, operation, expectedBodyHash);
        }
        var changed = ParcelUnsubmittedIntent.Abandon(original, reason);
        // SQLite can create transient sidecars in its private copy. The original
        // identity is the immutable inventory captured before any SQLite open.
        var beforeHash = copy.OriginalHashes[definition.Id + ".db"];
        var walHash = copy.OriginalHashes.GetValueOrDefault(definition.Id + ".db-wal");
        await BackupCopy(state.Directory, backup, state, copy, execution, finalOperation: false);
        var manifest = ParcelRecovery.VerifyBackup(backup, Authorize, token, allowStorage10: true);
        Authorize(); execution.Checkpoint();
        if (ReadState().Hash != state.Hash) throw new InvalidOperationException("备份期间配置变化；保留备份，重新核对");
        var database = Path.Combine(state.Directory, definition.Id + ".db");
        if (Wire.FileHash(database) != beforeHash || (File.Exists(database + "-wal") ? Wire.FileHash(database + "-wal") : null) != walHash)
            throw new IOException("原DB/WAL在备份期间变化；保留原件重新核对");
        execution.BeginCommit(MaintenanceStage.Verifying);
        using var db = Database.Open(database, false);
        using var tx = db.BeginTransaction();
        _ = ParcelRepository.ReadNeverAttempted(db, operation, expectedBodyHash, tx);
        using var update = db.CreateCommand(); update.Transaction = tx;
        update.CommandText = "UPDATE parcel_operations SET body=$body WHERE id=$id AND body=$before";
        update.Parameters.AddWithValue("$body", Wire.Encode(changed)); update.Parameters.AddWithValue("$id", operation); update.Parameters.AddWithValue("$before", Wire.Encode(original));
        // Compare the stored bytes, not a reserialized substitute for the CAS.
        using (var raw = db.CreateCommand())
        {
            raw.Transaction = tx; raw.CommandText = "SELECT body FROM parcel_operations WHERE id=$id"; raw.Parameters.AddWithValue("$id", operation);
            update.Parameters["$before"].Value = (string)raw.ExecuteScalar()!;
        }
        if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException("原作业CAS失败；未执行关闭");
        Database.Execute(db, tx, "UPDATE parcel_meta SET paused=1,revision=revision+1 WHERE id=1;");
        using var audit = db.CreateCommand(); audit.Transaction = tx;
        audit.CommandText = "INSERT INTO parcel_audit(at,event,owner,detail) VALUES($at,'UnsubmittedIntentClosedOffline',$owner,$detail)";
        audit.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); audit.Parameters.AddWithValue("$owner", operation);
        audit.Parameters.AddWithValue("$detail", Wire.Encode(new { Operation = operation, BeforeBodyHash = expectedBodyHash, AfterBodyHash = Wire.Hash(Wire.Encode(changed)), Actor, Reason = reason,
            Backup = backup, BackupHash = manifest.DatabaseHash, Policy = "Explicit entity inspection; all stages Prepared and never attempted; frozen requests retained; no external call or physical completion" }));
        audit.ExecuteNonQuery(); tx.Commit();
        return Wire.Encode(new { Status = "UnsubmittedIntentClosed", Operation = operation, OriginalBodyHash = expectedBodyHash, Backup = backup, ExternalCalls = 0 });
    });
}

public sealed partial class ParcelRepository
{
    internal static ParcelOperation ReadNeverAttempted(SqliteConnection db, string operation, string expectedHash, SqliteTransaction? tx = null)
    {
        using var command = db.CreateCommand(); command.Transaction = tx;
        command.CommandText = "SELECT body FROM parcel_operations WHERE id=$id"; command.Parameters.AddWithValue("$id", operation);
        var body = command.ExecuteScalar() as string ?? throw new InvalidDataException("原驻留作业不存在；不能猜测其他作业");
        if (Wire.Hash(body) != expectedHash) throw new InvalidOperationException("原作业已变化；重新核对，不重复关闭");
        var original = Wire.Decode<ParcelOperation>(body);
        var stages = new[] { original.Wcs, original.Plc, original.Capture, original.Upload }.OfType<StageFact>().ToArray();
        if (original.Id != operation || original.State is ParcelState.Abandoned or ParcelState.ImageRejected || original.RecoveryRequired || original.Image is not null ||
            original.ConfigurationHash != Wire.Hash(Wire.Encode(original.FrozenConfiguration)) || stages.Length == 0 || stages.Any(s => s.State != StageState.Prepared || s.Attempts != 0))
            throw new InvalidDataException("只允许明确核对过、全部阶段尚未尝试且无图片责任的原意图；未知、冲突或已发送不得离线关闭");
        command.CommandText = "SELECT count(*) FROM parcel_receipts WHERE operation=$id";
        if (Convert.ToInt64(command.ExecuteScalar()) != 0) throw new InvalidDataException("存在原发送/回包登记，须使用原请求核对流程");
        command.CommandText = "SELECT paused FROM parcel_meta WHERE id=1";
        if (Convert.ToInt64(command.ExecuteScalar()) != 1) throw new InvalidOperationException("须先暂停并完全停止原软件");
        return original;
    }
}
