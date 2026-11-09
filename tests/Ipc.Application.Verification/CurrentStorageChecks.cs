using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;

static class CurrentStorageChecks
{
    internal static async Task Run(string workspace, string root)
    {
        foreach (var version in new[] { 10, 11, 9, 13 })
        {
            var folder = Path.Combine(root, "unsupported-storage-" + version);
            var config = new SportmasterFlow().InitialConfiguration() with { Mode = RunMode.Simulation };
            var configDir = Path.Combine(folder, "config"); Directory.CreateDirectory(configDir);
            ExtensionStore.AtomicWrite(Path.Combine(configDir, "business.json"), config);
            var launch = new BusinessLaunch(configDir, Path.Combine(folder, "data"), null, null);
            var directory = Path.Combine(launch.DataRoot, config.Mode.ToString(), config.StationId, "sportmaster");
            Directory.CreateDirectory(directory);
            ExtensionStore.AtomicWrite(Path.Combine(directory, "configuration.json"), CurrentConfigurationFixture.Create<ParcelConfiguration>(3, config, null, "Effective", null));
            var database = Path.Combine(directory, "sportmaster.db");
            // Capture the writer's actual DB/WAL pair; tests open the copied closed snapshot, as after an interrupted writer.
            using var db = new SqliteConnection("Data Source=" + database + ";Pooling=False"); db.Open();
            // Frozen input schemas from 67a03763, not an upgrade implementation reproduced by the test.
            var schema = File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/fixtures", "parcel" + (version == 10 ? 10 : 11) + "-schema.sql")).TrimEnd('\r', '\n');
            var expectedHash = version == 10 ? "ECC9F99AFD48F914765248A6B39064A49F0DCBF3DDD2F208C87C75F8E07FEB23" : "15CBE8303697965F325E4A284903ACEF39C11D66FB852BA4520CCD4231CBD90A";
            if (Database.BaselineHash(schema) != expectedHash) throw new InvalidDataException("Unsupported-format fixture differs from its original schema");
            Database.CreateBaseline(db, "sportmaster", version, schema);
            using (var command = db.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; UPDATE platform_migrations SET version=$version; INSERT INTO parcel_audit(at,event,owner,detail) VALUES('2026-11-09','SyntheticOriginal','station','retain original bytes');";
                command.Parameters.AddWithValue("$version", version); command.ExecuteNonQuery();
            }
            byte[] CopyOpen(string path)
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
            }
            var databaseBytes = CopyOpen(database); var walBytes = CopyOpen(database + "-wal");
            db.Dispose();
            File.WriteAllBytes(database, databaseBytes); File.WriteAllBytes(database + "-wal", walBytes);
            string[] originals = [database, database + "-wal", Path.Combine(directory, "configuration.json"), Path.Combine(configDir, "business.json")];
            var hashes = originals.ToDictionary(p => p, p => Wire.FileHash(p));
            var service = new ParcelStartupMaintenance(launch, new SportmasterFlow(), () => new ParcelCapabilityFactory(launch), () => { })
            { MaintenanceWorkspace = () => Directory.CreateDirectory(Path.Combine(folder, "scratch-" + Wire.Id())).FullName };
            var backup = Path.Combine(folder, "must-not-create.db"); var changed = false;
            async Task Refused(Func<Task> action)
            {
                try { await action(); throw new Exception("Unsupported storage accepted"); }
                catch (InvalidDataException) { }
                if (hashes.Any(p => Wire.FileHash(p.Key) != p.Value) || File.Exists(backup) || changed)
                    throw new InvalidOperationException("Refusal changed original DB/WAL/configuration or invoked deployment");
                Console.WriteLine("PASS STORAGE unsupported version " + version + " refused with DB/WAL/configuration unchanged and no deployment");
            }
            await Refused(() => { using var denied = new ParcelRepository(directory, "sportmaster"); return Task.CompletedTask; });
            await Refused(() => service.PlanChangeAsync("Upgrade", backup));
            await Refused(() => service.ExecuteChangeAsync("synthetic-unapproved-plan", "Upgrade", backup, () => { changed = true; return Task.CompletedTask; }));
        }
    }
}
