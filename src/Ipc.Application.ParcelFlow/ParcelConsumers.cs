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
        var application = RequireCurrentStorage(db, tx: tx);
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
        var config = ConfigurationRevisions<ParcelConfiguration>.ReadCurrentEnvelope(File.ReadAllText(configPath));
        ValidateConsumerConfiguration(config.Effective, application, null);
        if (config.Pending is { } candidate) ValidateConsumerConfiguration(candidate, application, config.Effective);
        HashSet<PackageReference> required = [.. config.Effective.Extensions?.Packages ?? [], .. config.Pending?.Extensions?.Packages ?? []];
        c.CommandText = "SELECT count(*) FROM parcel_receipts WHERE state NOT IN ('Awaiting','RawDurable','Committed','NoReply')";
        if (Convert.ToInt64(c.ExecuteScalar()) != 0) throw new InvalidDataException("Unknown consumer receipt responsibility; use matching release");
        c.CommandText = "SELECT operation FROM parcel_receipts WHERE state IN ('Awaiting','RawDurable')";
        HashSet<string> pendingReceipts = []; using (var r = c.ExecuteReader()) while (r.Read()) pendingReceipts.Add(r.GetString(0));
        c.CommandText = "SELECT body FROM parcel_operations UNION ALL SELECT body FROM parcel_archives WHERE id IN (SELECT operation FROM parcel_receipts WHERE state IN ('Awaiting','RawDurable'))";
        using (var r = c.ExecuteReader()) while (r.Read())
        {
            var op = Wire.Decode<ParcelOperation>(r.GetString(0));
            ValidateConsumerConfiguration(op.FrozenConfiguration, application, config.Effective);
            if (!Enum.IsDefined(op.State) || op.ConfigurationHash != Wire.Hash(Wire.Encode(op.FrozenConfiguration)))
                throw new InvalidDataException("Unknown/changed frozen operation format; retain packages");
            foreach (var stage in new[] { op.Wcs, op.Plc, op.Capture, op.Upload }.OfType<StageFact>())
                if (!Enum.IsDefined(stage.State) || stage.Request is null || stage.Replies is null || stage.Attempts is < 0 or > 1 ||
                    stage.Request.ApplicationId != application || stage.Request.OperationId != op.Id || stage.Request.ConfigurationHash != op.ConfigurationHash ||
                    !ParcelBindings.All(op.FrozenConfiguration).Any(b => Wire.Encode(b) == Wire.Encode(stage.Request.Binding)))
                    throw new InvalidDataException("Unknown/changed frozen stage identity; retain packages");
            var terminal = op.State is ParcelState.Abandoned or ParcelState.ImageRejected or ParcelState.WcsRejected or ParcelState.PlcRejected or ParcelState.PhysicalCompleted || application == "sportmaster" && op.State == ParcelState.SlotResolved;
            var pendingReceipt = pendingReceipts.Remove(op.Id);
            var unsettledStage = new[] { op.Wcs, op.Plc, op.Capture, op.Upload }.OfType<StageFact>().Any(s => s.State is StageState.Prepared or StageState.Dispatching or StageState.Unknown or StageState.Conflict);
            if (!terminal || op.RecoveryRequired || unsettledStage || op.FrozenConfiguration.Images is not null && !op.ImageReleased || pendingReceipt)
                foreach (var package in op.FrozenConfiguration.Extensions?.Packages ?? []) required.Add(package);
        }
        if (pendingReceipts.Count != 0) throw new InvalidDataException("Original receipt occurrence lacks its owning operation; retain packages");
        return required.ToArray();
    }
    static void ValidateConsumerConfiguration(ParcelConfiguration configuration, string application, ParcelConfiguration? owner)
    {
        if (configuration is null || owner is not null && (configuration.StationId != owner.StationId || configuration.Mode != owner.Mode))
            throw new InvalidDataException("Consumer configuration owner differs; retain packages");
        var errors = ParcelConfigurationRules.Validate(configuration, application, application == "sportmaster" ? ["left", "right"] : ["main"]);
        if (errors.Length != 0) throw new InvalidDataException("Unknown/invalid consumer configuration: " + string.Join("; ", errors));
        if (application == "sportmaster" ? configuration.Wcs is null || configuration.Plc is not null :
            configuration.Plc is null || application == "direct-plc" && configuration.Wcs is not null || application == "wcs-to-plc" && configuration.Wcs is null)
            throw new InvalidDataException("Consumer main capability ownership differs from declared Application");
        var deployment = configuration.Extensions;
        var readers = ParcelBindings.Readers(configuration);
        foreach (var reader in readers)
        {
            if (reader.Driver is "manual" or "tcp-line" or "hikrobot-mvs") continue;
            var instance = deployment?.Instances.SingleOrDefault(i => "extension:" + i.Id == reader.Driver);
            if (instance is null || !instance.Enabled || instance.DeviceId != reader.DeviceId || deployment!.Packages.Count(p => p.Id == instance.PackageId) != 1)
                throw new InvalidDataException("Dangling/unknown reader extension reference; retain packages");
        }
        if (deployment?.Instances.Any(i => !readers.Any(r => r.Driver == "extension:" + i.Id)) == true)
            throw new InvalidDataException("Unbound observation extension instance; retain packages");
    }

}
