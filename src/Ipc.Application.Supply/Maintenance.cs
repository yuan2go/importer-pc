using Ipc.Platform;
using Microsoft.Data.Sqlite;
using System.Text;

namespace Ipc.Supply;

// These are local maintenance receipts. Database/backup and receipt paths may contain
// private operational information and are not diagnostic exports or publication assets.
public sealed record StorageInspection(int FormatVersion, int MinimumReader, int SelectedReader,
    bool SelectedReaderCompatible, int PendingRequests, int OpenSessions,
    bool Quarantined, string[] RecoveryBlockers);
public sealed record StorageBackupReceipt(string BackupId, string Sha256, DateTimeOffset CreatedAt,
    StorageInspection Source, StorageInspection Backup);
public sealed partial class SupplyStore
{
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
        const string unsupported = "仅支持当前Supply 6数据库；原文件保留，请使用全新数据目录。";
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('schema_history','storage_format')";
        if (Convert.ToInt64(command.ExecuteScalar()) != 2) throw new InvalidDataException(unsupported);
        command.CommandText = "SELECT id,checksum FROM schema_history";
        using (var reader = command.ExecuteReader())
            if (!reader.Read() || reader.GetInt32(0) != CurrentFormatVersion || reader.GetString(1) != Ipc.Platform.Storage.Database.BaselineHash(Schema) || reader.Read())
                throw new InvalidDataException(unsupported);
        command.CommandText = "SELECT id,version,minimum_reader FROM storage_format";
        using (var reader = command.ExecuteReader())
            if (!reader.Read() || reader.GetInt32(0) != 1 || reader.GetInt32(1) != CurrentFormatVersion || reader.GetInt32(2) != CurrentFormatVersion || reader.Read())
                throw new InvalidDataException(unsupported);
        return CurrentFormatVersion;
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
        {
            command.CommandText = "SELECT id,request,status,json FROM receipt_occurrences";
            using var receipts = command.ExecuteReader();
            while (receipts.Read())
            {
                var occurrence = Wire.Decode<ReceiptResponsibility>(receipts.GetString(3));
                if (occurrence.Id != receipts.GetString(0) || occurrence.RequestId != receipts.GetString(1) || occurrence.Status != receipts.GetString(2) || !ReceiptResponsibilityRules.IsKnown(occurrence.Status))
                    throw new InvalidDataException("Unknown receipt responsibility status; original retained");
            }
            receipts.Close();
            // Count requests once even if many independently unresolved receipts exist.
            pending = Convert.ToInt32(MaintenanceScalar(connection, "SELECT count(*) FROM requests WHERE status IN ('Prepared','Dispatching','Unknown') OR id IN (SELECT request FROM response_conflicts) OR id IN (SELECT request FROM receipt_occurrences WHERE " + ReceiptResponsibilityRules.UnresolvedSql("status") + ") OR id IN (SELECT json_extract(b.value,'$.Source') FROM state,json_each(state.json,'$.Blocks') b WHERE json_extract(b.value,'$.Code')='ReceiptPersistenceUncertain')"));
        }
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
        return new(format, format, CurrentFormatVersion, format == CurrentFormatVersion,
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
        if ((!owned && inspection.RecoveryBlockers.Contains("ReceiptPersistenceRecoveryRequired")) || Directory.Exists(rawCustody) && Directory.EnumerateFiles(rawCustody).Any())
            throw new InvalidOperationException("Recover receipt custody in its original data root before database-only backup; evidence files must not be omitted");
        var directory = System.IO.Path.GetDirectoryName(fullDestination)!; Directory.CreateDirectory(directory);
        var temporary = System.IO.Path.Combine(directory, "." + System.IO.Path.GetFileName(fullDestination) + "." + Wire.Id() + ".unverified");
        // A failure leaves any partial output explicitly named unverified and retains source.
        Ipc.Platform.Storage.Database.Backup(connection, temporary);
        var saved = InspectReadOnly(temporary);
        if (saved.FormatVersion != inspection.FormatVersion || saved.MinimumReader != inspection.MinimumReader)
            throw new InvalidDataException("Backup format changed while copying; original and unverified output retained");
        var receipt = new StorageBackupReceipt(Wire.Id(), Wire.FileHash(temporary), DateTimeOffset.UtcNow, inspection, saved);
        File.Move(temporary, fullDestination); // Does not replace a concurrently created output.
        return receipt;
    }
}
