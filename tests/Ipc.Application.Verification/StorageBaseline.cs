using System.Reflection;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
using Ipc.Supply;
using Microsoft.Data.Sqlite;

static class StorageBaseline
{
    static void Check(bool ok, string reason)
    { if (!ok) throw new InvalidDataException(reason); Console.WriteLine("PASS BASELINE " + reason); }
    public static async Task Run(string workspace, string root, bool smoke)
    {
        await CurrentStorageChecks.Run(workspace, root);
        var parcelRoot = Path.Combine(root, "baseline-parcel");
        var supplyFile = Path.Combine(root, "baseline-supply", "station.db");
        using (var parcel = new ParcelRepository(parcelRoot, "sportmaster"))
        using (var supply = new SupplyStore(supplyFile))
        {
            foreach (var owner in new object[] { parcel, supply })
            {
                var db = (SqliteConnection)owner.GetType().GetField("db", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
                using var c = db.CreateCommand(); c.CommandText = "PRAGMA journal_mode";
                Check((string?)c.ExecuteScalar() == "wal", owner.GetType().Name + " uses WAL");
                c.CommandText = "PRAGMA synchronous";
                Check(Convert.ToInt32(c.ExecuteScalar()) == 2, owner.GetType().Name + " uses synchronous=FULL");
            }
        }
        foreach (var (name, file) in new[] { ("parcel", Path.Combine(parcelRoot, "sportmaster.db")), ("supply", supplyFile) })
        {
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
            using var c = db.CreateCommand();
            c.CommandText = name == "parcel" ? "SELECT count(*) FROM platform_migrations WHERE version=$current" : "SELECT count(*) FROM schema_history WHERE id=6";
            if (name == "parcel") c.Parameters.AddWithValue("$current", ParcelRepository.CurrentStorageVersion);
            Check(Convert.ToInt32(c.ExecuteScalar()) == 1, name + " has exactly one current baseline identity");
            c.CommandText = name == "parcel" ? "SELECT count(*) FROM platform_migrations" : "SELECT count(*) FROM schema_history";
            Check(Convert.ToInt32(c.ExecuteScalar()) == 1, name + " contains no historical migration entries");
        }
        Check(ParcelRepository.ReadStorageVersion(Path.Combine(parcelRoot, "sportmaster.db"), "sportmaster") == ParcelRepository.CurrentStorageVersion && SupplyStore.InspectReadOnly(supplyFile).FormatVersion == 6, "current readers accept fresh Parcel14/Supply6");
        var analyzed = Path.Combine(root, "analyzed-current.db"); File.Copy(Path.Combine(parcelRoot, "sportmaster.db"), analyzed);
        using (var db = new SqliteConnection("Data Source=" + analyzed + ";Pooling=False"))
        { db.Open(); using var c = db.CreateCommand(); c.CommandText = "ANALYZE;"; c.ExecuteNonQuery(); }
        Check(ParcelRepository.ReadStorageVersion(analyzed, "sportmaster") == ParcelRepository.CurrentStorageVersion, "SQLite-owned optimizer tables remain valid in the current schema");
        foreach (var unexpected in new[] { "unexpected_payload", "sqliteX_payload" })
        {
            var file = Path.Combine(root, unexpected + ".db"); File.Copy(analyzed, file);
            using (var db = new SqliteConnection("Data Source=" + file + ";Pooling=False"))
            { db.Open(); using var c = db.CreateCommand(); c.CommandText = "CREATE TABLE " + unexpected + "(id TEXT);"; c.ExecuteNonQuery(); }
            var hash = Wire.FileHash(file);
            try { ParcelRepository.ReadStorageVersion(file, "sportmaster"); throw new Exception("Unexpected schema accepted"); }
            catch (InvalidDataException) { Check(Wire.FileHash(file) == hash, "current schema refuses unknown and SQLite-lookalike objects without changing bytes: " + unexpected); }
        }
        foreach (var parcel in new[] { true, false })
        {
            var directory = Path.Combine(root, parcel ? "malformed-parcel-identity" : "malformed-supply-identity"); Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, parcel ? "sportmaster.db" : "station.db");
            File.Copy(parcel ? Path.Combine(parcelRoot, "sportmaster.db") : supplyFile, file);
            using (var db = new SqliteConnection("Data Source=" + file + ";Pooling=False"))
            {
                db.Open(); using var c = db.CreateCommand();
                c.CommandText = parcel ? "DROP TABLE platform_migrations; CREATE TABLE platform_migrations(unrecognized TEXT);" : "DROP TABLE schema_history; CREATE TABLE schema_history(unrecognized TEXT);";
                c.ExecuteNonQuery();
            }
            var hash = Wire.FileHash(file);
            try
            {
                if (parcel) { using var denied = new ParcelRepository(directory, "sportmaster"); }
                else { using var denied = new SupplyStore(file); }
                throw new Exception("Malformed format identity accepted");
            }
            catch (InvalidDataException ex) when (ex.Message.Contains(parcel ? "原文件已保留" : "请使用全新数据目录"))
            { Check(Wire.FileHash(file) == hash, "malformed " + (parcel ? "Parcel" : "Supply") + " identity refuses with fresh-directory guidance and unchanged database bytes"); }
        }
        var brokenRoot = Path.Combine(root, "missing-index"); Directory.CreateDirectory(brokenRoot);
        var broken = Path.Combine(brokenRoot, "sportmaster.db"); File.Copy(Path.Combine(parcelRoot, "sportmaster.db"), broken);
        using (var db = new SqliteConnection("Data Source=" + broken + ";Pooling=False"))
        { db.Open(); using var c = db.CreateCommand(); c.CommandText = "DROP TABLE parcel_reply_identities"; c.ExecuteNonQuery(); }
        var before = Wire.FileHash(broken);
        try { using var denied = new ParcelRepository(brokenRoot, "sportmaster"); throw new Exception("Missing identity table accepted"); }
        // Current DB14 verifies the complete schema before the old, narrower
        // reply-identity check. Keep typed rejection and byte/no-directory guards;
        // a historical diagnostic phrase is not the storage safety contract.
        catch (InvalidDataException)
        { Check(Wire.FileHash(broken) == before && !Directory.Exists(Path.Combine(brokenRoot, "receipts")), "missing identity table refuses without database writes or receipt directories"); }
        if (!smoke) return;
        var syntheticConfiguration = Path.Combine(root, "fresh-config"); Directory.CreateDirectory(syntheticConfiguration);
        File.Copy(Path.Combine(workspace, "tests", "Ipc.Application.Verification", "sportmaster.synthetic.json"), Path.Combine(syntheticConfiguration, "business.json"));
        var launch = new BusinessLaunch(syntheticConfiguration, Path.Combine(root, "fresh-runtime"), null, null);
        await using (var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new ParcelCapabilityFactory(launch)) { RuntimeIdentityPreflight = () => { } })
        {
            await runtime.StartAsync(default);
            var snapshot = await runtime.SnapshotAsync();
            Check(snapshot.Operations.Length == 0, "fresh Simulation runtime starts with no operations");
            await runtime.StopAsync(default);
        }
        Check(Directory.EnumerateFiles(launch.DataRoot, "sportmaster.db", SearchOption.AllDirectories).Count() == 1, "fresh Simulation runtime creates one database and stops cleanly");
    }
}
