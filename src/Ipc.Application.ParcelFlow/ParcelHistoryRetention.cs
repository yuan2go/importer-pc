using Ipc.Application.Contracts;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;

namespace Ipc.Application.ParcelFlow;

public sealed record ParcelRetentionResult(string Run, int Days, DateTimeOffset Cutoff, long Operations, long Inputs, string BackupHash, long ReusableBytes);
public sealed partial class ParcelRepository
{
    internal (string Id, string Admission, string Observation, string Position)? RetiredIdentity(string admission, string observation)
    {
        if (StorageVersion < 9) return null;
        using var c = db.CreateCommand(); c.CommandText = "SELECT h.id,h.admission,h.observation,h.position FROM parcel_history_index h JOIN parcel_retired_operations t ON t.id=h.id WHERE h.admission=$a OR h.observation=$o LIMIT 1";
        c.Parameters.AddWithValue("$a", admission); c.Parameters.AddWithValue("$o", observation); using var r = c.ExecuteReader();
        return r.Read() ? (r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)) : null;
    }
    internal (string Id, string Position, bool Same)? RetiredMeasurement(WeightMeasurement measurement)
    {
        if (StorageVersion < 9) return null;
        using var c = db.CreateCommand(); c.CommandText = "SELECT h.id,h.position,m.body_hash FROM parcel_retired_measurements m JOIN parcel_history_index h ON h.admission=m.admission WHERE m.id=$id";
        c.Parameters.AddWithValue("$id", measurement.Id); using var r = c.ExecuteReader();
        return r.Read() ? (r.GetString(0), r.GetString(1), r.GetString(2) == Wire.Hash(Wire.Encode(measurement))) : null;
    }
    internal bool RetentionDue(int days, DateTimeOffset now)
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT EXISTS(SELECT 1 FROM parcel_history_projection WHERE observed<julianday($cutoff))";
        c.Parameters.AddWithValue("$cutoff", now.AddDays(-days).ToString("O")); return (long)c.ExecuteScalar()! != 0;
    }
    bool RetentionSettled(ParcelOperation op, FlowDefinition flow) => HasTerminalRecoveryBasis(op, flow.Id) &&
        (flow.MainSettled(op) || op.State is ParcelState.Abandoned or ParcelState.ImageRejected) && !op.RecoveryRequired &&
        (op.FrozenConfiguration.Images is null || op.ImageReleased) &&
        new[] { op.Wcs, op.Plc, op.Capture, op.Upload }.OfType<StageFact>().All(s => s.State is StageState.Accepted or StageState.Rejected or StageState.Cancelled) &&
        (op.Upload is null || SettledStageHasCustody(op, op.Upload));
    void PrepareRetentionCommandOwners()
    {
        // Offline maintenance owns this connection exclusively. Materialize once,
        // rather than scan all command-admission audits for every expired operation.
        // TEMP is derived and connection-scoped; original commands/audits stay authoritative.
        using var c = db.CreateCommand(); c.CommandText = """
            CREATE TEMP TABLE IF NOT EXISTS parcel_retention_pending_owners(owner TEXT PRIMARY KEY) WITHOUT ROWID;
            DELETE FROM temp.parcel_retention_pending_owners;
            INSERT OR IGNORE INTO temp.parcel_retention_pending_owners
            SELECT json_extract(a.detail,'$.Owner') FROM parcel_audit a
            JOIN parcel_commands c ON c.id=json_extract(a.detail,'$.Id')
            WHERE a.event='CommandAdmitted' AND json_valid(a.detail)
                AND json_type(a.detail,'$.Owner')='text' AND json_extract(c.receipt,'$.Status')='Pending';
            """;
        c.ExecuteNonQuery();
    }
    bool RetentionClear(string id, DateTimeOffset cutoff)
    {
        using var c = db.CreateCommand(); c.CommandText = """
            SELECT NOT EXISTS(SELECT 1 FROM parcel_receipts WHERE operation=$id AND state NOT IN ('Committed','NoReply'))
            AND NOT EXISTS(SELECT 1 FROM parcel_identity_conflicts WHERE acknowledged=0 AND json_extract(body,'$.OriginalOperation')=$id)
            AND NOT EXISTS(SELECT 1 FROM parcel_ingress i JOIN parcel_history_index h ON h.id=$id
                WHERE i.state IN ('Pending','Interrupted') AND coalesce(json_extract(i.body,'$.Observation.AdmissionId'),json_extract(i.body,'$.Measurement.AdmissionId'))=h.admission)
            AND NOT EXISTS(SELECT 1 FROM parcel_audit WHERE owner=$id AND julianday(at)>=julianday($cutoff))
            AND NOT EXISTS(SELECT 1 FROM temp.parcel_retention_pending_owners WHERE owner=$id)
            """;
        c.Parameters.AddWithValue("$id", id); c.Parameters.AddWithValue("$cutoff", cutoff.ToString("O")); return (long)c.ExecuteScalar()! != 0;
    }
    // Explicit maintenance command only. No runtime/provider starts. The lease spans
    // original inspection, complete backup, bounded deletion transactions and verification.
    public static ParcelRetentionResult ExpireHistory(string database, string expectedHash, string backup, FlowDefinition flow,
        Action<string, ParcelConfiguration> registerBackup, bool compact = false)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        return ExpireHistoryAuthorized(database, expectedHash, backup, flow, registerBackup, compact, MachineRuntimePolicy.RequireExtensionInstallerAuthority);
    }
    internal static ParcelRetentionResult ExpireHistoryAuthorized(string database, string expectedHash, string backup, FlowDefinition flow,
        Action<string, ParcelConfiguration> registerBackup, bool compact, Action authority)
    {
        database = Path.GetFullPath(database); RecoveryFiles.RequireOrdinaryAncestors(database);
        var directory = Path.GetDirectoryName(database)!; using var lease = new ResourceLease(directory, ["application-owner"]);
        authority(); if (Wire.FileHash(database) != expectedHash) throw new InvalidDataException("Original database hash changed; inspect again");
        if (File.Exists(database + "-wal") && new FileInfo(database + "-wal").Length != 0) throw new InvalidOperationException("Nonempty WAL requires orderly matching-release shutdown before retention");
        var envelope = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(Path.Combine(directory, "configuration.json")));
        if (envelope.Effective.ApplicationId != flow.Id || envelope.Effective.Retention?.HistoryDays is not (>= 7 and <= 30) || envelope.Confirmation?.Required == true)
            throw new InvalidOperationException("Activate a confirmed 7..30 day retention policy first; no implicit default deletion");
        if (ReadStorageVersion(database, flow.Id) != 9) throw new InvalidDataException("Use the matching DB9 release; retention never performs a schema upgrade");
        using var repository = new ParcelRepository(directory, flow.Id);
        if (!repository.Paused) throw new InvalidOperationException("Pause and fully stop the application before retention");
        using var timeBudget = new CancellationTokenSource(TimeSpan.FromHours(2));
        SQLitePCL.raw.sqlite3_progress_handler(repository.db.Handle, 1000, _ => timeBudget.IsCancellationRequested ? 1 : 0, null);
        using (var integrity = repository.db.CreateCommand())
        { integrity.CommandText = "PRAGMA integrity_check"; if (integrity.ExecuteScalar() as string != "ok") throw new InvalidDataException("Original database integrity failed; no retention deletion"); }
        var originalAuthority = authority;
        authority = () => { timeBudget.Token.ThrowIfCancellationRequested(); originalAuthority(); };
        repository.ValidateRawInventory();
        repository.PrepareRetentionCommandOwners();
        var now = DateTimeOffset.UtcNow; var days = envelope.Effective.Retention.HistoryDays.Value; var cutoff = now.AddDays(-days);
        var resident = repository.Load().Where(op => repository.RetentionSettled(op, flow) && repository.RetentionClear(op.Id, cutoff)).Select(op => op.Id).ToArray();
        foreach (var batch in resident.Chunk(128)) repository.Archive(batch, "Confirmed retention maintenance; originals archived before complete backup");
        ParcelRecovery.CreateBackup(directory, backup, envelope.Effective, envelope.Pending, repository.StorageBytes, repository.Backup, registerBackup, authority);
        var manifest = Wire.Decode<ParcelRecoveryManifest>(File.ReadAllText(Path.Combine(backup + ".recovery", "COMPLETED.json")));
        var result = repository.ExpireHistoryCore(flow, days, now, manifest.DatabaseHash, authority);
        repository.ValidateRawInventory();
        using var c = repository.db.CreateCommand(); c.CommandText = "PRAGMA integrity_check";
        if (c.ExecuteScalar() as string != "ok") throw new InvalidDataException("Post-retention integrity check failed; preserve original and complete backup");
        if (compact)
        {
            authority();
            if (!ParcelStorageVolume.Plan(directory, checked(repository.StorageBytes * 2), envelope.Effective.Retention.MinimumFreeBytes).Fits)
                throw new IOException("Retention committed; VACUUM requires two database sizes plus reserve; retained free pages remain reusable");
            c.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)"; c.ExecuteNonQuery();
            c.CommandText = "VACUUM"; c.ExecuteNonQuery();
            c.CommandText = "PRAGMA integrity_check"; if (c.ExecuteScalar() as string != "ok") throw new InvalidDataException("Compacted database integrity failed");
        }
        return result;
    }
    internal ParcelRetentionResult ExpireHistoryCore(FlowDefinition flow, int days, DateTimeOffset now, string backupHash, Action authority)
    {
        if (days is < 7 or > 30 || !Paused || StorageVersion != 9) throw new InvalidOperationException("Invalid retention maintenance state");
        var cutoff = now.AddDays(-days); var run = Wire.Id(); long removed = 0, inputs = 0, cursor = 0;
        PrepareRetentionCommandOwners();
        using (var start = db.CreateCommand())
        {
            start.CommandText = "INSERT INTO parcel_retention_runs VALUES($id,$at,$days,$cutoff,$backup,'InProgress; committed tombstones remain authoritative after interruption')";
            start.Parameters.AddWithValue("$id", run); start.Parameters.AddWithValue("$at", now.ToString("O")); start.Parameters.AddWithValue("$days", days);
            start.Parameters.AddWithValue("$cutoff", cutoff.ToString("O")); start.Parameters.AddWithValue("$backup", backupHash); start.ExecuteNonQuery();
        }
        while (true)
        {
            authority(); List<(long Sequence, string Id)> candidates = [];
            using (var c = db.CreateCommand())
            {
                c.CommandText = "SELECT h.seq,h.id FROM parcel_history_index h JOIN parcel_archives a ON a.id=h.id JOIN parcel_history_projection p ON p.id=h.id WHERE h.seq>$after AND p.observed<julianday($cutoff) ORDER BY h.seq LIMIT 64";
                c.Parameters.AddWithValue("$after", cursor); c.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));
                using var r = c.ExecuteReader(); while (r.Read()) candidates.Add((r.GetInt64(0), r.GetString(1)));
            }
            if (candidates.Count == 0) break;
            foreach (var candidate in candidates)
            {
                cursor = candidate.Sequence; var op = ReadOperation(candidate.Id)!;
                if (!RetentionSettled(op, flow) || !RetentionClear(op.Id, cutoff)) continue;
                // Archive must have removed all redundant spool files before deleting
                // its DB copy; this makes process interruption atomic without orphan files.
                using (var files = db.CreateCommand())
                {
                    files.CommandText = "SELECT id FROM parcel_receipts WHERE operation=$id"; files.Parameters.AddWithValue("$id", op.Id);
                    using var r = files.ExecuteReader(); while (r.Read()) if (File.Exists(Path.Combine(rawRoot, r.GetString(0) + ".json")))
                        throw new IOException("Archived raw spool cleanup still pending; original retained");
                }
                authority(); using var tx = db.BeginTransaction();
                using var c = db.CreateCommand(); c.Transaction = tx;
                c.Parameters.AddWithValue("$id", op.Id); c.Parameters.AddWithValue("$run", run); c.Parameters.AddWithValue("$at", now.ToString("O")); c.Parameters.AddWithValue("$admission", op.AdmissionId);
                c.CommandText = "INSERT INTO parcel_retired_operations SELECT id,body_hash,$at,$run FROM parcel_archives WHERE id=$id"; c.ExecuteNonQuery();
                c.CommandText = "SELECT id,body FROM parcel_measurements WHERE admission=$admission";
                List<(string Id, string Hash)> measurements = []; using (var r = c.ExecuteReader()) while (r.Read()) measurements.Add((r.GetString(0), Wire.Hash(r.GetString(1))));
                foreach (var m in measurements)
                {
                    using var save = db.CreateCommand(); save.Transaction = tx; save.CommandText = "INSERT INTO parcel_retired_measurements VALUES($id,$admission,$hash)";
                    save.Parameters.AddWithValue("$id", m.Id); save.Parameters.AddWithValue("$admission", op.AdmissionId); save.Parameters.AddWithValue("$hash", m.Hash); save.ExecuteNonQuery();
                }
                c.CommandText = """
                    DELETE FROM parcel_receipt_custody WHERE occurrence IN (SELECT id FROM parcel_receipts WHERE operation=$id);
                    DELETE FROM parcel_archived_receipts WHERE id IN (SELECT id FROM parcel_receipts WHERE operation=$id);
                    DELETE FROM parcel_receipts WHERE operation=$id;
                    DELETE FROM parcel_measurements WHERE admission=$admission;
                    DELETE FROM parcel_archive_versions WHERE operation=$id;
                    DELETE FROM parcel_audit WHERE owner=$id;
                    DELETE FROM parcel_audit WHERE event='InputObserved' AND json_valid(detail) AND json_extract(detail,'$.OperationId')=$id;
                    DELETE FROM parcel_archives WHERE id=$id;
                    DELETE FROM parcel_history_projection WHERE id=$id;
                    UPDATE parcel_meta SET revision=revision+1 WHERE id=1;
                    """;
                c.ExecuteNonQuery(); Boundary?.Invoke("BeforeRetentionCommit"); tx.Commit(); Revision++; removed++; Boundary?.Invoke("AfterRetentionCommit");
            }
        }
        // Preserve unprocessed/interrupted inputs and every input tied to a retained
        // operation; old handled non-admissions have a compact durable disposition.
        long inputCursor = 0;
        while (true)
        {
            authority(); List<(long Row, string Id)> expired = [];
            using (var select = db.CreateCommand())
            {
                select.CommandText = """
                    SELECT rowid,id FROM parcel_ingress i WHERE rowid>$cursor AND state IN ('Processed','Inspected') AND julianday(completed_at)<julianday($cutoff)
                    AND NOT EXISTS(SELECT 1 FROM parcel_history_index h WHERE h.admission=coalesce(json_extract(i.body,'$.Observation.AdmissionId'),json_extract(i.body,'$.Measurement.AdmissionId'))
                        AND NOT EXISTS(SELECT 1 FROM parcel_retired_operations t WHERE t.id=h.id)) ORDER BY rowid LIMIT 256
                    """;
                select.Parameters.AddWithValue("$cursor", inputCursor); select.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));
                using var r = select.ExecuteReader(); while (r.Read()) expired.Add((r.GetInt64(0), r.GetString(1)));
            }
            if (expired.Count == 0) break;
            using var tx = db.BeginTransaction();
            foreach (var input in expired)
            {
                using var c = db.CreateCommand(); c.Transaction = tx;
                c.CommandText = "INSERT INTO parcel_retired_inputs SELECT id,body_hash,state,$at FROM parcel_ingress WHERE id=$id; DELETE FROM parcel_audit WHERE seq IN (SELECT seq FROM parcel_ingress_audits WHERE input=$id); DELETE FROM parcel_ingress_audits WHERE input=$id; DELETE FROM parcel_ingress WHERE id=$id";
                c.Parameters.AddWithValue("$at", now.ToString("O")); c.Parameters.AddWithValue("$id", input.Id); c.ExecuteNonQuery();
                inputCursor = input.Row; inputs++;
            }
            tx.Commit();
        }
        using var finish = db.CreateCommand(); finish.CommandText = "UPDATE parcel_retention_runs SET summary=$summary WHERE id=$id";
        finish.Parameters.AddWithValue("$id", run); finish.Parameters.AddWithValue("$summary", Wire.Encode(new { Status = "Completed", Operations = removed, Inputs = inputs, Policy = "Permanent identity/reply/measurement/numeric ownership retained; no replay" })); finish.ExecuteNonQuery();
        finish.CommandText = "PRAGMA freelist_count"; var pages = (long)finish.ExecuteScalar()!; finish.CommandText = "PRAGMA page_size";
        return new(run, days, cutoff, removed, inputs, backupHash, pages * (long)finish.ExecuteScalar()!);
    }
}
