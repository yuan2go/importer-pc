using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    public static int ReadStorageVersion(string database, string application)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
        using var c = db.CreateCommand(); c.CommandText = "SELECT application,version,sha FROM platform_migrations ORDER BY version";
        using var r = c.ExecuteReader(); var version = 0;
        while (r.Read())
        {
            version++;
            if (r.GetString(0) != application || version > migrations.Length || r.GetInt64(1) != version || r.GetString(2) != Wire.Hash(migrations[version - 1].Sql))
                throw new InvalidDataException("Unknown/changed original database; matching maintenance reader required");
        }
        if (version is < 2 or > 7) throw new InvalidDataException("This maintenance reader supports exact Parcel DB2/3/4/5/6/7 only");
        return version;
    }
    // Offline, no provider construction or schema upgrade. Restores only a derived main
    // projection using existing committed raw; unresolved attachment/custody is untouched.
    public static int RepairLegacyMain(string database, string expectedHash, string backup, FlowDefinition definition, string reason,
        Action<string, ParcelConfiguration>? registerBackup = null)
    {
        database = Path.GetFullPath(database); backup = Path.GetFullPath(backup);
        RecoveryFiles.RequireOrdinaryAncestors(database); RecoveryFiles.RequireOrdinaryAncestors(backup);
        var directory = Path.GetDirectoryName(database)!;
        using var owner = new ResourceLease(directory, ["application-owner"]);
        if (!ExtensionSchema.IsHash(expectedHash) || Wire.FileHash(database) != expectedHash || string.IsNullOrWhiteSpace(reason) || reason.Length > 1024)
            throw new InvalidDataException("Exact original database hash and maintenance reason required");
        if (Path.GetFileName(database) != definition.Id + ".db" || backup.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Use original Application database and a separate new backup destination");
        var version = ReadStorageVersion(database, definition.Id);
        var configFile = Path.Combine(directory, "configuration.json");
        var config = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(configFile));
        if (config.Effective.ApplicationId != definition.Id || !Enum.IsDefined(config.Effective.Mode) || Path.GetFileName(directory) != definition.Id ||
            Directory.GetParent(directory)?.Name != config.Effective.StationId || Directory.GetParent(directory)?.Parent?.Name != config.Effective.Mode.ToString() ||
            config.Pending is { } p && (p.ApplicationId != definition.Id || p.StationId != config.Effective.StationId || p.Mode != config.Effective.Mode))
            throw new InvalidDataException("Original deployment identity differs");
        if (registerBackup is null && (config.Effective.Extensions?.Packages.Length > 0 || config.Pending?.Extensions?.Packages.Length > 0))
            throw new InvalidDataException("Extension consumer registration is required before backing up this deployment");
        using var repository = new ParcelRepository(directory, definition.Id, version);
        var changes = repository.Load().Where(op => op.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached &&
            op.State is ParcelState.Blocked or ParcelState.ImageUnknown && repository.HasMainReceiptCustody(op) &&
            !repository.PendingReceipts().Any(r => r.Operation == op.Id && r.Stage != "image-upload"))
            .Select(op => (Before: op, After: definition.RestoreMainProjection(op))).Where(x => x.Before != x.After).ToArray();
        if (changes.Length == 0) return 0;
        var recovery = backup + ".recovery";
        if (File.Exists(backup) || Directory.Exists(recovery)) throw new IOException("Preserve prior backup; choose a new destination");
        var names = new List<string> { "configuration.json" };
        names.AddRange(RecoveryFiles.Enumerate(directory, "receipts")); names.AddRange(RecoveryFiles.Enumerate(directory, "images", skipImageLeases: true));
        if (File.Exists(Path.Combine(directory, "overload.json"))) names.Add("overload.json");
        RecoveryFiles.ValidateNames(directory, names);
        var hashes = names.ToDictionary(n => n, n => Wire.FileHash(RecoveryFiles.Resolve(directory, n)));
        var bytes = checked(repository.StorageBytes + RecoveryFiles.MaximumManifestBytes + names.Sum(n => new FileInfo(RecoveryFiles.Resolve(directory, n)).Length));
        if (!ParcelStorageVolume.Plan(backup, bytes, config.Effective.Retention?.MinimumFreeBytes ?? 67108864).Fits) throw new IOException("Backup volume lacks copy capacity and configured reserve");
        Directory.CreateDirectory(recovery);
        registerBackup?.Invoke(backup, config.Effective); if (config.Pending is { } pending) registerBackup?.Invoke(backup, pending);
        repository.Backup(backup);
        foreach (var name in names)
        {
            var target = RecoveryFiles.Resolve(recovery, name); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            RecoveryFiles.CopyVerified(RecoveryFiles.Resolve(directory, name), target, hashes[name]);
        }
        ExtensionStore.AtomicWrite(Path.Combine(recovery, "COMPLETED.json"), new ParcelRecoveryManifest(definition.Id, Path.GetFileName(backup), Wire.FileHash(backup), hashes, DateTimeOffset.UtcNow, "Before offline main projection repair; original schema and all custody retained"));
        foreach (var change in changes)
            repository.Commit(change.After with { RecoveryRequired = false }, "LegacyMainProjectionRecovered", Wire.Encode(new { Original = change.Before,
                Actor = Environment.UserName, Reason = reason, Backup = backup, OriginalSchema = version, Basis = "Committed main-stage raw; no external call or attachment disposition" }), paused: true);
        return changes.Length;
    }
}
