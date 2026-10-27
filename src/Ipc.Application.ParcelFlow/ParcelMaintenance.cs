using System.IO.Compression;
using System.Security.Cryptography;
using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

public sealed record ParcelMaintenanceProvider(string InstanceId, string Implementation, string Version, string Contract, string TargetSha256, string? PackageSha256);

public sealed record ParcelMaintenanceInspection(string Application, string Station, RunMode Mode, int DatabaseVersion,
    long Revision, bool Paused, long Resident, long Archived, long Unsettled, long PendingReceipts, long IdentityConflicts,
    long DatabaseBytes, long CustodyBytes, string DatabaseSha256, string? WalSha256, string ConfigurationSha256,
    bool OrdinaryUpgradeAllowed, string[] Reasons, DateTimeOffset At, string BusinessVersion, string SourceIdentity, string ApplicationContractSha256, int ConfigurationSchema, ParcelMaintenanceProvider[] Providers);

// Offline application-owned operations. No runtime, source, SDK or external port starts.
public sealed partial class ParcelRepository
{
    public static ParcelMaintenanceInspection InspectMaintenance(string database, FlowDefinition flow)
    {
        database = Path.GetFullPath(database); RecoveryFiles.RequireOrdinaryAncestors(database);
        if (!File.Exists(database)) throw new FileNotFoundException("Original database is required; no empty runtime is created", database);
        using var owner = new ResourceLease(Path.GetDirectoryName(database)!, ["application-owner"]);
        return InspectMaintenanceCore(database, flow);
    }
    internal static ParcelMaintenanceInspection InspectMaintenanceCore(string database, FlowDefinition flow, CancellationToken token = default)
    {
        if (!File.Exists(database)) throw new FileNotFoundException("Original database is required; no empty runtime is created", database);
        var directory = Path.GetDirectoryName(database)!;
        if (Path.GetFileName(database) != flow.Id + ".db") throw new InvalidDataException("Selected Application differs from original database");
        var version = ReadStorageVersion(database, flow.Id);
        var configuration = Path.Combine(directory, "configuration.json"); RecoveryFiles.RequireOrdinaryAncestors(configuration);
        var config = ConfigurationRevisions<ParcelConfiguration>.ReadCurrentEnvelope(File.ReadAllText(configuration));
        var errors = flow.Validate(config.Effective);
        if (errors.Length != 0 || config.Pending is { } candidate && flow.Validate(candidate).Length != 0)
            throw new InvalidDataException("Original Effective/Pending configuration is invalid; retain original release and data");
        token.ThrowIfCancellationRequested();
        var hash = Wire.FileHash(database, token); var wal = database + "-wal"; var walHash = File.Exists(wal) ? Wire.FileHash(wal, token) : null;
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
        SQLitePCL.raw.sqlite3_progress_handler(db.Handle, 1000, _ => token.IsCancellationRequested ? 1 : 0, null);
        using var tx = db.BeginTransaction(deferred: true);
        long Scalar(string sql) { using var c = db.CreateCommand(); c.Transaction = tx; c.CommandText = sql; return Convert.ToInt64(c.ExecuteScalar()); }
        var resident = Scalar("SELECT count(*) FROM parcel_operations");
        var archived = version >= 4 ? Scalar("SELECT count(*) FROM parcel_archives") : 0;
        var pending = Scalar("SELECT count(*) FROM parcel_receipts WHERE state NOT IN ('Committed','NoReply')");
        var conflicts = version >= 4 ? Scalar("SELECT count(*) FROM parcel_identity_conflicts WHERE acknowledged=0") : 0;
        long unsettled = 0;
        using (var c = db.CreateCommand())
        {
            c.Transaction = tx; c.CommandText = "SELECT body,NULL FROM parcel_operations" + (version >= 4 ? " UNION ALL SELECT body,body_hash FROM parcel_archives" : "");
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                token.ThrowIfCancellationRequested();
                var body = r.GetString(0); if (!r.IsDBNull(1) && Wire.Hash(body) != r.GetString(1)) throw new InvalidDataException("Archived original hash differs");
                var op = Wire.Decode<ParcelOperation>(body);
                if (op.ConfigurationHash != Wire.Hash(Wire.Encode(op.FrozenConfiguration))) throw new InvalidDataException("Original frozen configuration hash differs");
                var terminated = flow.MainSettled(op) || op.State is ParcelState.Abandoned or ParcelState.ImageRejected;
                if (!terminated || op.RecoveryRequired || new[] { op.Wcs, op.Plc, op.Capture, op.Upload }.OfType<StageFact>().Any(x => x.State is not (StageState.Accepted or StageState.Rejected or StageState.Cancelled)) ||
                    op.FrozenConfiguration.Images is not null && !op.ImageReleased) unsettled++;
                if (op.Image is { } image && !op.ImageReleased) _ = ParcelRuntime.ReadOriginalImage(directory, op, image);
            }
        }
        long invalidRaw = 0;
        using (var c = db.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = version >= 4 ? "SELECT r.id,r.raw_hash,a.body FROM parcel_receipts r LEFT JOIN parcel_archived_receipts a ON a.id=r.id WHERE r.raw_hash IS NOT NULL OR r.state='Committed'" :
                "SELECT id,raw_hash,NULL FROM parcel_receipts WHERE raw_hash IS NOT NULL OR state='Committed'";
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                token.ThrowIfCancellationRequested();
                var file = RecoveryFiles.Resolve(directory, "receipts/" + r.GetString(0) + ".json");
                var rawHash = r.IsDBNull(1) ? null : r.GetString(1);
                if (File.Exists(file)) { RecoveryFiles.RequireOrdinaryAncestors(file); if (Wire.FileHash(file, token) != rawHash) invalidRaw++; }
                else if (r.IsDBNull(2)) invalidRaw++;
                else { using var bytes = r.GetStream(2); if (Convert.ToHexString(SHA256.HashData(bytes)) != rawHash) invalidRaw++; }
            }
        }
        var reasons = new List<string>();
        if (version >= 9 && Scalar("SELECT count(*) FROM parcel_ingress WHERE state IN ('Pending','Interrupted')") != 0)
            reasons.Add("Original input custody requires matching-release inspection; never replay accepted source input");
        if (invalidRaw != 0) reasons.Add("Original receipt bytes missing/changed: " + invalidRaw);
        if (unsettled != 0) reasons.Add("Original business/image/recovery responsibilities require matching-release settlement");
        if (pending != 0) reasons.Add("Original receipt custody is unsettled; Query is not the missing original raw");
        if (conflicts != 0) reasons.Add("Persistent identity conflicts require authorized inspection");
        if (File.Exists(Path.Combine(directory, "overload.json"))) reasons.Add("Callback loss/overload requires original entity inspection");
        var names = new[] { "configuration.json" }.Concat(RecoveryFiles.Enumerate(directory, "receipts")).Concat(RecoveryFiles.Enumerate(directory, "images", skipImageLeases: true)).ToArray();
        RecoveryFiles.ValidateNames(directory, names);
        var inspection = new ParcelMaintenanceInspection(flow.Id, config.Effective.StationId, config.Effective.Mode, version,
            Scalar("SELECT revision FROM parcel_meta WHERE id=1"), Scalar("SELECT paused FROM parcel_meta WHERE id=1") != 0,
            resident, archived, unsettled, pending, conflicts, new FileInfo(database).Length + (File.Exists(wal) ? new FileInfo(wal).Length : 0),
            names.Sum(n => new FileInfo(RecoveryFiles.Resolve(directory, n)).Length), hash, walHash, Wire.FileHash(configuration), reasons.Count == 0, reasons.ToArray(), DateTimeOffset.UtcNow,
            BusinessCompatibility.VersionOf(flow.GetType().Assembly), flow.GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Unidentified source",
            Wire.FileHash(typeof(IParcelApplication).Assembly.Location), config.Effective.SchemaVersion,
            ParcelBindings.Bound(config.Effective).Select(b => new ParcelMaintenanceProvider(b.InstanceId, b.Implementation, b.Version, b.Contract, Wire.Hash(b.Target), b.Package?.ManifestSha256)).ToArray());
        tx.Commit();
        tx.Dispose(); db.Close(); // Windows WAL handles must close before byte-preservation hashes.
        if (Wire.FileHash(database) != hash || (File.Exists(wal) ? Wire.FileHash(wal) : null) != walHash) throw new IOException("Original database changed during offline inspection");
        return inspection;
    }
    public static ParcelMaintenanceInspection BackupOffline(string database, string destination, FlowDefinition flow, Action<string, ParcelConfiguration> registerBackup)
    {
        database = Path.GetFullPath(database); RecoveryFiles.RequireOrdinaryAncestors(database);
        var directory = Path.GetDirectoryName(database)!;
        if (!File.Exists(database)) throw new FileNotFoundException("Original database is required; no empty runtime is created", database);
        using var owner = new ResourceLease(directory, ["application-owner"]);
        var inspection = InspectMaintenanceCore(database, flow);
        var config = ConfigurationRevisions<ParcelConfiguration>.ReadCurrentEnvelope(File.ReadAllText(Path.Combine(directory, "configuration.json")));
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
        ParcelRecovery.CreateBackup(directory, destination, config.Effective, config.Pending, inspection.DatabaseBytes,
            file => Database.Backup(db, file), registerBackup);
        return inspection;
    }
    public static ParcelMaintenanceInspection ExportDiagnostics(string database, string destination, FlowDefinition flow)
    {
        var report = InspectMaintenance(database, flow);
        destination = Path.GetFullPath(destination); RecoveryFiles.RequireOrdinaryAncestors(destination);
        var directory = Path.GetDirectoryName(Path.GetFullPath(database))!;
        if (Path.GetRelativePath(directory, destination) is var relative && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative))
            throw new IOException("Diagnostic output must be outside the original runtime directory");
        using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = new StreamWriter(zip.CreateEntry("maintenance.json").Open()); entry.Write(Wire.Encode(report));
        }
        stream.Flush(flushToDisk: true);
        return report;
    }
}
public static partial class ParcelRecovery
{
    internal static void CreateBackup(string directory, string destination, ParcelConfiguration effective, ParcelConfiguration? pending,
        long databaseBytes, Action<string> backupDatabase, Action<string, ParcelConfiguration> registerBackup, Action? requireAuthority = null, CancellationToken cancellationToken = default)
    {
        requireAuthority?.Invoke();
        destination = Path.GetFullPath(destination); directory = Path.GetFullPath(directory);
        RecoveryFiles.RequireOrdinaryAncestors(directory); RecoveryFiles.RequireOrdinaryAncestors(destination);
        var relative = Path.GetRelativePath(directory, destination);
        if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new IOException("Backup must be outside the live application directory");
        var recovery = destination + ".recovery";
        if (Directory.Exists(recovery) || File.Exists(recovery) || File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Backup destination must be new");
        using var editor = new ResourceLease(Path.Combine(directory, "startup-maintenance"), ["configuration-editor"]);
        var names = new List<string> { "configuration.json" };
        if (File.Exists(Path.Combine(directory, ParcelMachineOrigin.FileName))) names.Add(ParcelMachineOrigin.FileName);
        names.AddRange(RecoveryFiles.Enumerate(directory, "receipts")); names.AddRange(RecoveryFiles.Enumerate(directory, "images", skipImageLeases: true));
        names.AddRange(RecoveryFiles.MaintenanceEvidence(directory));
        if (File.Exists(Path.Combine(directory, "overload.json"))) names.Add("overload.json");
        RecoveryFiles.ValidateNames(directory, names);
        var hashes = names.ToDictionary(n => n, n => Wire.FileHash(RecoveryFiles.Resolve(directory, n), cancellationToken), StringComparer.Ordinal);
        var bytes = checked(databaseBytes + names.Sum(n => new FileInfo(RecoveryFiles.Resolve(directory, n)).Length) + RecoveryFiles.MaximumManifestBytes);
        if (!ParcelStorageVolume.Plan(destination, bytes, effective.Retention?.MinimumFreeBytes ?? 67108864).Fits) throw new IOException("Backup requires database/custody capacity plus manifest and configured reserve");
        Directory.CreateDirectory(recovery); RecoveryFiles.RequireOrdinaryAncestors(recovery);
        registerBackup(destination, effective); if (pending is not null) registerBackup(destination, pending);
        requireAuthority?.Invoke(); backupDatabase(destination);
        foreach (var name in names)
        {
            requireAuthority?.Invoke();
            var target = RecoveryFiles.Resolve(recovery, name); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            RecoveryFiles.CopyVerified(RecoveryFiles.Resolve(directory, name), target, hashes[name], cancellationToken);
        }
        var manifest = new ParcelRecoveryManifest(effective.ApplicationId, Path.GetFileName(destination), Wire.FileHash(destination, cancellationToken), RecoveryFiles.Inventory(recovery, cancellationToken), DateTimeOffset.UtcNow,
            "Isolated original-deployment restore; reconcile external effects before admission; retire original numeric ranges");
        if (System.Text.Encoding.UTF8.GetByteCount(Wire.Encode(manifest)) > RecoveryFiles.MaximumManifestBytes) throw new InvalidDataException("Recovery manifest quota exceeded; incomplete backup retained");
        requireAuthority?.Invoke();
        ExtensionStore.AtomicWrite(Path.Combine(recovery, "COMPLETED.json"), manifest);
    }
}
