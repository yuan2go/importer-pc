using System.Reflection;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static class LatencyStorageChecks
{
    sealed class Clock : TimeProvider
    {
        long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref ticks);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref ticks, elapsed.Ticks);
    }
    public static async Task Run(string workspace, string root)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
        var config = Path.Combine(root, "config"); Directory.CreateDirectory(config);
        const long reserve = 16777216;
        var retention = new ParcelRetentionPolicy(200, reserve, HistoryDays: 7) { WarningFreeBytes = reserve * 4, CriticalFreeBytes = reserve * 2, WalWarningBytes = 1048576, WalCheckpointSeconds = 1 };
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json"))) with { Retention = retention };
        Check(ConfigurationPolicyDraft.From(c).ApplyParameters(c).Retention == retention, "ordinary parameter save retains advanced capacity/WAL policy in the same candidate");
        Check(new SportmasterFlow().Validate(c with { Retention = retention with { WarningFreeBytes = reserve } }).Length != 0, "misordered warning and admission thresholds are rejected");
        Check(!Wire.Encode(new ParcelRetentionPolicy()).Contains("WarningFreeBytes"), "absent monitoring options preserve old serialized policy bytes");
        File.WriteAllText(Path.Combine(config, "business.json"), Wire.Encode(c));
        var launch = new BusinessLaunch(config, Path.Combine(root, "data"), null, null); var clock = new Clock(); long free = reserve * 8;
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch))
        { RuntimeIdentityPreflight = () => { }, StorageTimeForVerification = clock, StorageAvailableBytesForVerification = () => Volatile.Read(ref free), WalIntervalForVerification = TimeSpan.FromMilliseconds(50) };
        await runtime.StartAsync(default);
        const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        var gate = (SemaphoreSlim)typeof(ParcelRuntime).GetField("gate", hidden)!.GetValue(runtime)!;
        var repo = (ParcelRepository)typeof(ParcelRuntime).GetField("repository", hidden)!.GetValue(runtime)!;
        var matrix = new List<object>();
        foreach (var item in new[] { (reserve * 5, "Normal", true), (reserve * 3, "Warning", true), (reserve * 3 / 2, "Critical", true), (reserve - 1, "Blocked", false) })
        {
            Volatile.Write(ref free, item.Item1); clock.Advance(TimeSpan.FromSeconds(2));
            await gate.WaitAsync(); try { typeof(ParcelRuntime).GetMethod("RefreshStorageCapacity", hidden)!.Invoke(runtime, null); } finally { gate.Release(); }
            var state = await runtime.SnapshotAsync(); var notice = state.AuxiliaryReasons!.FirstOrDefault(x => x.StartsWith("StorageGrowthWarning:"));
            Check((item.Item2 == "Normal" ? notice is null : notice?.Contains("Level=" + item.Item2) == true) && state.Capacity!.Available == item.Item3, "capacity phase " + item.Item2 + " precedes or enforces the unchanged reserve fence");
            matrix.Add(new { Free = item.Item1, Level = item.Item2, Available = state.Capacity!.Available });
        }
        Volatile.Write(ref free, reserve * 8);
        ParcelOperation Make(int n) => new("storage-synthetic-" + n, "storage-admission-" + n, "SYNTHETIC", "storage-observation-" + n,
            DateTimeOffset.UtcNow, Wire.Hash(Wire.Encode(c)), c, [], c.Weight, null, ParcelState.Abandoned, null, null, null, new string('R', 8192), PositionId: n % 2 == 0 ? "left" : "right");
        await gate.WaitAsync(); try { for (var i = 0; i < 1000; i++) repo.Commit(Make(i), "SyntheticUnsent", "No external facts"); } finally { gate.Release(); }
        clock.Advance(TimeSpan.FromMinutes(2));
        await gate.WaitAsync(); try { typeof(ParcelRuntime).GetMethod("RefreshStorageCapacity", hidden)!.Invoke(runtime, null); } finally { gate.Release(); }
        var forecast = (await runtime.SnapshotAsync()).AuxiliaryReasons!.Single(x => x.StartsWith("StorageForecast:"));
        using var predicted = JsonDocument.Parse(forecast["StorageForecast:".Length..]);
        Check(predicted.RootElement.GetProperty("BytesPerJob").GetDouble() > 0 && predicted.RootElement.GetProperty("RemainingDays").GetDouble() > 0, "recent job/physical SQLite growth yields an explicit remaining-days estimate");

        var database = Path.Combine(launch.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId, c.ApplicationId + ".db");
        var selection = await runtime.SelectHistoryAsync(new());
        var pinned = new ParcelHistoryReader(database); _ = pinned.Page(selection, long.MaxValue, 1);
        var samples = new List<JsonElement>();
        async Task Observe()
        {
            var data = (await runtime.SnapshotAsync()).AuxiliaryReasons!.FirstOrDefault(x => x.StartsWith("WalCheckpoint:{"));
            if (data is not null) { using var json = JsonDocument.Parse(data["WalCheckpoint:".Length..]); samples.Add(json.RootElement.Clone()); }
        }
        using var writerEnd = new CancellationTokenSource();
        var writer = Task.Run(async () =>
        {
            while (!writerEnd.IsCancellationRequested)
            {
                await gate.WaitAsync(); try { repo.Commit(null, "SyntheticWalWrite", new string('W', 65536)); } finally { gate.Release(); }
                try { await Task.Delay(10, writerEnd.Token); } catch (OperationCanceledException) { break; }
            }
        });
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var export = runtime.ExportHistoryAsync(selection, Path.Combine(root, "interleaved-" + i + ".csv"), default);
                while (!export.IsCompleted) { await Observe(); await Task.Delay(30); }
                Check((await export).Status == "Completed", "real bounded export interleaves with continuing WAL writes " + i);
            }
            for (var i = 0; i < 15; i++) { await Observe(); await Task.Delay(50); }
        }
        finally { writerEnd.Cancel(); await writer; pinned.Dispose(); }
        for (var i = 0; i < 100; i++)
        {
            await Observe();
            if (samples.Count != 0 && samples[^1].GetProperty("BacklogFrames").GetInt64() == 0) break;
            await Task.Delay(50);
        }
        Check(samples.Any(x => x.GetProperty("BacklogFrames").GetInt64() > 0 && x.GetProperty("Warning").GetBoolean()), "pinned reader plus real exports exposes WAL backlog and high-water warning");
        Check(samples[^1].GetProperty("BacklogFrames").GetInt64() == 0 && samples[^1].GetProperty("HighWaterBytes").GetInt64() > 1048576, "after reader/export release PASSIVE completion and retained high water are observed");
        ExtensionStore.AtomicWrite(Path.Combine(root, "STORAGE-OBSERVATIONS.json"), new { Capacity = matrix, Forecast = predicted.RootElement.Clone(), Wal = samples, Scope = "Synthetic capacity probe, monotonic time, pinned read plus actual bounded exports; no physical slow storage or field qualification" });
    }
}
