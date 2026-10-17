using Ipc.Platform;
using Microsoft.Data.Sqlite;
using System.Text;

namespace Ipc.Supply;

// These are local maintenance receipts. Database/backup and receipt paths may contain
// private operational information and are not diagnostic exports or publication assets.
public sealed record StorageInspection(int FormatVersion, int MinimumReader, int SelectedReader,
    bool SelectedReaderCompatible, bool ExplicitUpgradeSupported, int PendingRequests, int OpenSessions,
    bool Quarantined, string[] UpgradeBlockers);
public sealed record StorageBackupReceipt(string BackupId, string Sha256, DateTimeOffset CreatedAt,
    StorageInspection Source, StorageInspection Backup);
public sealed record MaintenanceReleaseIdentity(string ExecutableSha256, string ProgramAssemblySha256,
    string ReleaseManifestSha256, Dictionary<string, string> ConfigurationSha256);
public sealed record StorageUpgradeReceipt(string OperationId, string Status, DateTimeOffset StartedAt,
    StorageBackupReceipt Backup, MaintenanceReleaseIdentity PreviousRelease, MaintenanceReleaseIdentity SelectedRelease,
    int DestinationFormat, string? Failure = null);

public sealed partial class SupplyStore
{
    // id1/schema and id2/SemanticFormatMigration are immutable historical contracts.
    // New input-validity/source-health JSON requires a reader that knows format3.
    const string ClosureFormatMigration = """
        UPDATE storage_format SET version=3,minimum_reader=3 WHERE id=1;
        """;

    const string ConformanceFormatMigration = """
        CREATE TABLE response_conflicts(id TEXT PRIMARY KEY,request TEXT NOT NULL REFERENCES requests(id),json TEXT NOT NULL);
        CREATE INDEX response_conflicts_request ON response_conflicts(request);
        UPDATE storage_format SET version=4,minimum_reader=4 WHERE id=1;
        """;

    static SqliteConnection OpenMaintenance(string path, SqliteOpenMode mode)
    {
        if (!File.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new FileNotFoundException("Existing regular station database required", path);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = System.IO.Path.GetFullPath(path), Mode = mode, Pooling = false, DefaultTimeout = 5 }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }
    static object? MaintenanceScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar();
    }
    static int ReadFormat(SqliteConnection connection)
    {
        if ((string?)MaintenanceScalar(connection, "PRAGMA integrity_check") != "ok")
            throw new InvalidDataException("SQLite integrity check failed; original retained");
        if (MaintenanceScalar(connection, "PRAGMA foreign_key_check") is not null)
            throw new InvalidDataException("SQLite foreign key check failed; original retained");
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,checksum FROM schema_history ORDER BY id";
        using var reader = command.ExecuteReader();
        var migrations = new Dictionary<int, string>();
        while (reader.Read()) migrations.Add(reader.GetInt32(0), reader.GetString(1));
        reader.Close();
        if (migrations.Count == 0 || migrations.GetValueOrDefault(1) != Wire.Hash(Schema) ||
            migrations.Keys.Any(id => id is < 1 or > CurrentFormatVersion))
            throw new InvalidDataException("Unknown schema or checksum; original retained");
        var format = 1;
        if (migrations.TryGetValue(2, out var second))
        {
            if (second != Wire.Hash(SemanticFormatMigration)) throw new InvalidDataException("Format2 migration checksum mismatch; original retained");
            format = 2;
        }
        if (migrations.TryGetValue(3, out var third))
        {
            if (format != 2 || third != Wire.Hash(ClosureFormatMigration)) throw new InvalidDataException("Format3 migration checksum mismatch; original retained");
            format = 3;
        }
        if (migrations.TryGetValue(4, out var fourth))
        {
            if (format != 3 || fourth != Wire.Hash(ConformanceFormatMigration)) throw new InvalidDataException("Format4 migration checksum mismatch; original retained");
            format = 4;
        }
        var hasStorageFormat = MaintenanceScalar(connection, "SELECT name FROM sqlite_master WHERE type='table' AND name='storage_format'") is not null;
        if (format == 1)
        {
            if (hasStorageFormat) throw new InvalidDataException("Storage format has no matching migration evidence; original retained");
        }
        else
        {
            command.CommandText = "SELECT id,version,minimum_reader FROM storage_format";
            using var values = command.ExecuteReader();
            if (!values.Read() || values.GetInt32(0) != 1 || values.GetInt32(1) != format || values.GetInt32(2) != format || values.Read())
                throw new InvalidDataException("Storage format and migration evidence disagree; original retained");
        }
        return format;
    }
    static StorageInspection InspectConnection(SqliteConnection connection, string sourcePath)
    {
        var format = ReadFormat(connection);
        using var command = connection.CreateCommand();
        // Inspect all statuses, rather than counting only known pending strings and treating
        // an unknown future value as terminal. Invalid JSON/status agreement also fails closed.
        command.CommandText = "SELECT status,json FROM requests";
        var pending = 0;
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                if (!Enum.TryParse<RequestStatus>(reader.GetString(0), out var status) || !Enum.IsDefined(status))
                    throw new InvalidDataException("Unrecognized request status; maintenance compatibility unknown");
                var request = Wire.Decode<BusinessRequest>(reader.GetString(1));
                if (request.Status != status || !Enum.IsDefined(request.Kind)) throw new InvalidDataException("Request status/kind evidence invalid");
                if (status is RequestStatus.Prepared or RequestStatus.Dispatching or RequestStatus.Unknown) pending++;
            }
        if (format >= 4)
            pending += Convert.ToInt32(MaintenanceScalar(connection, "SELECT count(*) FROM requests WHERE status NOT IN ('Prepared','Dispatching','Unknown') AND (id IN (SELECT request FROM response_conflicts) OR id IN (SELECT json_extract(b.value,'$.Source') FROM state,json_each(state.json,'$.Blocks') b WHERE json_extract(b.value,'$.Code')='ReceiptPersistenceUncertain'))"));
        command.CommandText = "SELECT o.state,r.status FROM outbox o LEFT JOIN requests r ON r.id=o.request";
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                if (!Enum.TryParse<RequestStatus>(reader.GetString(0), out var status) || !Enum.IsDefined(status) ||
                    reader.IsDBNull(1) || reader.GetString(0) != reader.GetString(1))
                    throw new InvalidDataException("Unrecognized/contradictory outbox status; maintenance compatibility unknown");
        command.CommandText = "SELECT json FROM sessions";
        var open = 0;
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var session = Wire.Decode<StationSession>(reader.GetString(0));
                if (!Enum.IsDefined(session.Status)) throw new InvalidDataException("Unrecognized session status; maintenance compatibility unknown");
                if (session.Status is not (SessionStatus.Closed or SessionStatus.Rejected)) open++;
            }
        command.CommandText = "SELECT json FROM state WHERE id=1";
        var document = command.ExecuteScalar() as string;
        var state = document is null ? new StationState() : Wire.Decode<StationState>(document);
        var quarantined = state.Quarantined || File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(sourcePath))!, "restore-quarantine"));
        var blockers = new List<string>();
        if (pending != 0) blockers.Add($"PendingRequests:{pending}");
        if (open != 0 || state.Session is { Status: not (SessionStatus.Closed or SessionStatus.Rejected) }) blockers.Add($"OpenSessions:{Math.Max(open, 1)}");
        if (state.Lanes.Values.Any(x => !Enum.IsDefined(x.Status) || !Enum.IsDefined(x.Occupancy)))
            throw new InvalidDataException("Unrecognized operation state; maintenance compatibility unknown");
        if (state.Lanes.Values.Any(x => x.Occupancy != Occupancy.Clear)) blockers.Add("EntityDispositionRequired");
        if (quarantined) blockers.Add("RestoreQuarantine");
        var receiptFence = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(sourcePath))!, "receipt-owner-fence.json");
        if (File.Exists(receiptFence) && Wire.Decode<string[]>(File.ReadAllText(receiptFence)).Length > 0) blockers.Add("ReceiptPersistenceRecoveryRequired");
        var ingress = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(sourcePath))!, "receipt-ingress");
        if (Directory.Exists(ingress) && Directory.EnumerateFiles(ingress).Any()) blockers.Add("ReceiptPersistenceRecoveryRequired");
        return new(format, format, CurrentFormatVersion, format == CurrentFormatVersion, format < CurrentFormatVersion,
            pending, open, quarantined, blockers.ToArray());
    }
    public static StorageInspection InspectReadOnly(string path)
    {
        using var connection = OpenMaintenance(path, SqliteOpenMode.ReadOnly);
        // No CREATE/UPDATE, journal-mode change, initialization or migration is reachable here.
        return InspectConnection(connection, path);
    }
    public static StorageBackupReceipt BackupReadOnly(string source, string destination) => BackupSnapshot(source, destination, false);
    static StorageBackupReceipt BackupSnapshot(string source, string destination, bool owned)
    {
        var fullSource = System.IO.Path.GetFullPath(source);
        var fullDestination = System.IO.Path.GetFullPath(destination);
        if (fullDestination == fullSource || File.Exists(fullDestination) || File.Exists(fullDestination + "-wal") || File.Exists(fullDestination + "-shm"))
            throw new IOException("Backup destination must be new; source retained");
        using var connection = OpenMaintenance(fullSource, SqliteOpenMode.ReadOnly);
        var inspection = InspectConnection(connection, fullSource);
        var rawCustody = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(fullSource)!, "receipt-ingress");
        if ((!owned && inspection.UpgradeBlockers.Contains("ReceiptPersistenceRecoveryRequired")) || Directory.Exists(rawCustody) && Directory.EnumerateFiles(rawCustody).Any())
            throw new InvalidOperationException("Recover receipt custody in its original data root before database-only backup; evidence files must not be omitted");
        var directory = System.IO.Path.GetDirectoryName(fullDestination)!; Directory.CreateDirectory(directory);
        var temporary = System.IO.Path.Combine(directory, "." + System.IO.Path.GetFileName(fullDestination) + "." + Wire.Id() + ".unverified");
        // A failure leaves any partial output explicitly named unverified and retains source.
        using (var target = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = temporary, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()))
        {
            target.Open(); connection.BackupDatabase(target);
        }
        var saved = InspectReadOnly(temporary);
        if (saved.FormatVersion != inspection.FormatVersion || saved.MinimumReader != inspection.MinimumReader)
            throw new InvalidDataException("Backup format changed while copying; original and unverified output retained");
        var receipt = new StorageBackupReceipt(Wire.Id(), Wire.FileHash(temporary), DateTimeOffset.UtcNow, inspection, saved);
        File.Move(temporary, fullDestination); // Does not replace a concurrently created output.
        return receipt;
    }
    static void ApplyForwardMigrations(SqliteConnection connection, SqliteTransaction transaction, int fromFormat)
    {
        void Apply(int id, string sql)
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO schema_history VALUES($id,$checksum,$at)";
            command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$checksum", Wire.Hash(sql));
            command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); command.ExecuteNonQuery();
        }
        if (fromFormat == 1) Apply(2, SemanticFormatMigration);
        if (fromFormat is 1 or 2) Apply(3, ClosureFormatMigration);
        if (fromFormat is 1 or 2 or 3) Apply(4, ConformanceFormatMigration);
    }
    static void SaveMaintenanceReceipt(string path, StorageUpgradeReceipt receipt, bool create)
    {
        if (create)
        {
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            output.Write(Encoding.UTF8.GetBytes(Wire.Encode(receipt))); output.Flush(true); return;
        }
        var temporary = path + "." + Wire.Id() + ".new";
        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { output.Write(Encoding.UTF8.GetBytes(Wire.Encode(receipt))); output.Flush(true); }
        File.Move(temporary, path, true);
    }
    public static StorageUpgradeReceipt UpgradeVerified(string source, string destinationBackup, string operationReceipt,
        MaintenanceReleaseIdentity previousRelease, MaintenanceReleaseIdentity selectedRelease)
    {
        var inspection = InspectReadOnly(source);
        if (inspection.UpgradeBlockers.Length != 0)
            throw new InvalidOperationException("Upgrade blocked; original retained: " + string.Join(", ", inspection.UpgradeBlockers));
        if (File.Exists(operationReceipt)) throw new IOException("Upgrade requires a new operation receipt filename");
        var backup = BackupReadOnly(source, destinationBackup);
        var receipt = new StorageUpgradeReceipt(Wire.Id(), "BackupVerifiedAwaitingUpgrade", DateTimeOffset.UtcNow,
            backup, previousRelease, selectedRelease, CurrentFormatVersion);
        // This durable evidence precedes opening a writer. Receipt failure cannot upgrade source.
        SaveMaintenanceReceipt(operationReceipt, receipt, true);
        try
        {
            using var connection = OpenMaintenance(source, SqliteOpenMode.ReadWrite);
            var before = InspectConnection(connection, source);
            if (before.FormatVersion != inspection.FormatVersion || before.UpgradeBlockers.Length != 0)
                throw new InvalidOperationException("Source changed after backup; retained source/backup; rerun maintenance with a new identity");
            using (var transaction = connection.BeginTransaction())
            {
                ApplyForwardMigrations(connection, transaction, inspection.FormatVersion);
                transaction.Commit();
            }
            ValidateStorageFormat(connection);
            receipt = receipt with { Status = inspection.SelectedReaderCompatible ? "CompatibleFormatRetained" : "ForwardMigrationCommitted" };
            SaveMaintenanceReceipt(operationReceipt, receipt, false);
            return receipt;
        }
        catch (Exception failure)
        {
            // A failure after Commit may leave the newer format in place. Never claim rollback
            // or successful launch; the original-format verified backup remains the recovery input.
            try { SaveMaintenanceReceipt(operationReceipt, receipt with { Status = "UpgradeFailedInspectBeforeRetry", Failure = failure.GetType().Name + ": " + failure.Message }, false); }
            catch (Exception receiptFailure) { throw new AggregateException("Upgrade and maintenance receipt update failed; source/backup/initial receipt retained", failure, receiptFailure); }
            throw;
        }
    }
}
