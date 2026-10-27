using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static class P0Cleanup
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS ST02 " + message); }
    static int Accepting(ParcelRuntime app) => (int)typeof(ParcelRuntime).GetField("accepting", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
    static async Task Until(Func<Task<bool>> predicate)
    { var watch = Stopwatch.StartNew(); while (!await predicate()) { if (watch.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException(); await Task.Delay(20); } }
    public static async Task Run(string workspace, string root)
    {
        var failures = new List<string>();
        foreach (var scenario in new[] { "locked", "spool-corrupt", "archive-missing", "archive-corrupt" })
        {
            var result = new Dictionary<string, object>();
            try { await Case(workspace, Path.Combine(root, scenario), scenario, result); result["Result"] = "PASS"; }
            catch (Exception error) { result["Result"] = error.GetType().Name + ": " + error.Message; failures.Add(scenario + ": " + error.Message); }
            File.WriteAllText(Path.Combine(root, "cleanup-" + scenario + ".json"), Wire.Encode(result));
            Console.WriteLine("ST02 " + scenario + " " + Wire.Encode(result));
        }
        Check(failures.Count == 0, "all cleanup cases: " + string.Join(" | ", failures));
        Backoff(workspace, Path.Combine(root, "locked"));
    }
    sealed class Clock : TimeProvider
    {
        long ticks;
        DateTimeOffset utc = DateTimeOffset.UtcNow;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public override DateTimeOffset GetUtcNow() => utc;
        public void Move(TimeSpan wall, TimeSpan elapsed) { utc += wall; ticks += elapsed.Ticks; }
    }
    static void Backoff(string workspace, string root)
    {
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "business.json")));
        var directory = Path.Combine(root, "data", "Simulation", c.StationId, c.ApplicationId);
        string id; byte[] bytes;
        using (var db = new SqliteConnection("Data Source=" + Path.Combine(directory, "sportmaster.db") + ";Pooling=False"))
        {
            db.Open(); using var query = db.CreateCommand(); query.CommandText = "SELECT id,body FROM parcel_archived_receipts LIMIT 1";
            using var row = query.ExecuteReader(); Check(row.Read(), "backoff uses an actual archived receipt"); id = row.GetString(0); bytes = (byte[])row[1];
        }
        var path = Path.Combine(directory, "receipts", id + ".json"); File.WriteAllBytes(path, bytes);
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        using var repository = new ParcelRepository(directory, c.ApplicationId);
        var clock = new Clock(); repository.ArchiveCleanupClock = clock;
        repository.ValidateRawInventory();
        Check(repository.ArchiveCleanupAttempts == 1 && repository.ArchiveCleanupDelay == TimeSpan.FromMilliseconds(500), "startup tolerates only verified redundant lock with initial 500ms delay");
        clock.Move(TimeSpan.FromDays(-1), TimeSpan.FromMilliseconds(499)); repository.RetryArchiveCleanup();
        Check(repository.ArchiveCleanupAttempts == 1, "wall-clock rollback neither bypasses nor freezes monotonic backoff");
        clock.Move(TimeSpan.FromDays(2), TimeSpan.FromMilliseconds(1)); repository.RetryArchiveCleanup();
        Check(repository.ArchiveCleanupAttempts == 2 && repository.ArchiveCleanupDelay == TimeSpan.FromSeconds(1), "retry occurs exactly at elapsed deadline");
        for (var i = 0; i < 40; i++) { clock.Move(TimeSpan.FromHours(-1), repository.ArchiveCleanupDelay); repository.RetryArchiveCleanup(); }
        Check(repository.ArchiveCleanupAttempts == 32 && repository.ArchiveCleanupDelay == TimeSpan.FromSeconds(30) && repository.ArchiveCleanupFailure!.StartsWith("ArchiveCleanupWarning", StringComparison.Ordinal), "counter and exponential delay capped; persistent lock produces visible warning");
        held.Dispose(); clock.Move(TimeSpan.Zero, repository.ArchiveCleanupDelay); repository.RetryArchiveCleanup();
        Check(!File.Exists(path) && repository.ArchiveCleanupAttempts == 0 && repository.ArchiveCleanupFailure is null, "successful hash-verified cleanup resets warning/backoff");
    }
    static async Task Case(string workspace, string root, string scenario, Dictionary<string, object> result)
    {
        Directory.CreateDirectory(root);
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json"))) with { Retention = new(1, 67108864), TimeoutMs = 3000 };
        ExtensionStore.AtomicWrite(Path.Combine(root, "business.json"), c);
        var launch = new BusinessLaunch(root, Path.Combine(root, "data"), null, null);
        var directory = Path.Combine(launch.DataRoot, "Simulation", c.StationId, c.ApplicationId);
        var archived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FileStream? held = null; string? path = null; var injected = false;
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), new P0SimulationFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        app.Boundary = point =>
        {
            if (point != "AfterArchiveCommit" || injected) return; injected = true;
            path = Directory.EnumerateFiles(Path.Combine(directory, "receipts"), "*.json").Single();
            var id = Path.GetFileNameWithoutExtension(path);
            using var db = new SqliteConnection("Data Source=" + Path.Combine(directory, "sportmaster.db") + ";Pooling=False"); db.Open();
            using var command = db.CreateCommand(); command.CommandText = "SELECT count(*) FROM parcel_archived_receipts WHERE id=$id"; command.Parameters.AddWithValue("$id", id);
            Check((long)command.ExecuteScalar()! == 1, "independent SQL confirms committed archive before injection");
            if (scenario == "locked") held = new(path, FileMode.Open, FileAccess.Read, FileShare.None);
            else if (scenario == "spool-corrupt") File.WriteAllText(path, "synthetic corrupted redundant spool");
            else
            {
                command.CommandText = scenario == "archive-missing" ? "DELETE FROM parcel_archived_receipts WHERE id=$id" : "UPDATE parcel_archived_receipts SET body=X'01' WHERE id=$id";
                command.ExecuteNonQuery();
            }
            archived.TrySetResult();
        };
        try
        {
            await app.StartAsync(default);
            Check(app.EmitReferenceAt("left", "synthetic-first", "0001", "synthetic-first-event"), "first durable input accepted");
            await archived.Task.WaitAsync(TimeSpan.FromSeconds(8)); await Task.Delay(300);
            result["AcceptingAfterInjection"] = Accepting(app);
            result["Progress"] = JsonSerializer.Deserialize<JsonElement>(Wire.Encode(app.RuntimeProgress()));
            if (scenario == "locked")
            {
                Check(Accepting(app) == 1, "temporary locked redundant spool does not fence inputs or terminate maintenance");
                Check(app.EmitReferenceAt("right", "synthetic-second", "0002", "synthetic-second-event"), "input continues during cleanup lock");
                await Until(async () => (await app.SnapshotAsync()).Capacity!.Archived >= 2);
                var snapshot = await app.SnapshotAsync();
                Check(snapshot.AuxiliaryReasons!.Any(x => x.Contains("cleanup", StringComparison.OrdinalIgnoreCase)), "pending cleanup is visible through existing snapshot");
                held!.Dispose(); held = null;
                await Until(() => Task.FromResult(!File.Exists(path)));
                Check(Accepting(app) == 1, "cleanup retries after unlock without another business command");
            }
            else
            {
                await Until(() => Task.FromResult(Accepting(app) == 0));
                Check(Wire.Encode(app.RuntimeProgress()).Contains("Maintenance worker failed", StringComparison.Ordinal), "integrity damage stops maintenance and fences inputs");
                Check(File.Exists(path), "unverified spool is never deleted");
            }
        }
        finally { held?.Dispose(); }
    }
}
