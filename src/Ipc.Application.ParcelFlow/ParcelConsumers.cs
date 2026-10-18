using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

// Read-only, application-owned consumer inspection. Unknown formats and unavailable roots
// retain responsibility; the platform never interprets this application's SQL or stages.
public sealed partial class ParcelRepository
{
    public static PackageReference[] InspectRequiredPackages(string database)
    {
        database = Path.GetFullPath(database); RecoveryFiles.RequireOrdinaryAncestors(database);
        if (!File.Exists(database)) throw new InvalidDataException("Offline consumer unavailable; retain packages: " + database);
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
        using var tx = db.BeginTransaction(deferred: true);
        using var c = db.CreateCommand(); c.Transaction = tx;
        c.CommandText = "SELECT application,version,sha FROM platform_migrations ORDER BY version";
        string? application = null; int version = 0;
        using (var r = c.ExecuteReader())
        {
            while (r.Read())
            {
                application ??= r.GetString(0); version++;
                if (r.GetString(0) != application || version > migrations.Length || r.GetInt64(1) != version || r.GetString(2) != Wire.Hash(migrations[version - 1].Sql))
                    throw new InvalidDataException("Unknown/changed consumer migration; use matching release for inspection");
            }
        }
        if (application is not ("sportmaster" or "direct-plc" or "wcs-to-plc") || version < 1) throw new InvalidDataException("Unknown consumer application; matching application reader required");
        var configPath = Path.Combine(Path.GetDirectoryName(database)!, "configuration.json");
        if (Directory.Exists(database + ".recovery"))
        {
            var manifestFile = Path.Combine(database + ".recovery", "COMPLETED.json");
            if (!File.Exists(manifestFile)) throw new InvalidDataException("Incomplete registered backup consumer; preserve packages");
            var manifest = Wire.Decode<ParcelRecoveryManifest>(File.ReadAllText(manifestFile));
            configPath = Path.Combine(database + ".recovery", "configuration.json");
            if (manifest.Application != application || manifest.DatabaseHash != Wire.FileHash(database) || manifest.Files.GetValueOrDefault("configuration.json") != Wire.FileHash(configPath))
                throw new InvalidDataException("Backup consumer custody differs");
        }
        RecoveryFiles.RequireOrdinaryAncestors(configPath);
        var config = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(configPath));
        if (config.Effective.ApplicationId != application || config.Effective.SchemaVersion is < 1 or > 3 || config.Pending is { } pending && pending.ApplicationId != application)
            throw new InvalidDataException("Consumer configuration identity/schema unavailable");
        HashSet<PackageReference> required = [.. config.Effective.Extensions?.Packages ?? [], .. config.Pending?.Extensions?.Packages ?? []];
        c.CommandText = "SELECT operation FROM parcel_receipts WHERE state IN ('Awaiting','RawDurable')";
        HashSet<string> pendingReceipts = []; using (var r = c.ExecuteReader()) while (r.Read()) pendingReceipts.Add(r.GetString(0));
        c.CommandText = "SELECT body FROM parcel_operations";
        using (var r = c.ExecuteReader()) while (r.Read())
        {
            var op = Wire.Decode<ParcelOperation>(r.GetString(0));
            var terminal = op.State is ParcelState.Abandoned or ParcelState.ImageRejected or ParcelState.WcsRejected or ParcelState.PlcRejected or ParcelState.PhysicalCompleted || application == "sportmaster" && op.State == ParcelState.SlotResolved;
            if (!terminal || op.FrozenConfiguration.Images is not null && !op.ImageReleased || pendingReceipts.Contains(op.Id))
                foreach (var package in op.FrozenConfiguration.Extensions?.Packages ?? []) required.Add(package);
        }
        return required.ToArray();
    }
}
