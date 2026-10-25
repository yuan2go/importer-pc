using System.Diagnostics;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;

static partial class ReliabilityHardening
{
    static async Task DenseLogs(string root)
    {
        var authority = new LocalManagementMode(root); authority.ConfigurePassword("12345"); authority.Enter("12345");
        var directory = Path.Combine(root, "diagnostics"); Directory.CreateDirectory(directory); var now = DateTimeOffset.UtcNow;
        const int count = 131073;
        File.WriteAllLines(Path.Combine(directory, "runtime.jsonl"), Enumerable.Repeat(JsonSerializer.Serialize(new { At = now, Message = "dense" }), count));
        using var reader = new SoftwareLogReader(directory, authority); var watch = Stopwatch.StartNew();
        var page = await reader.QueryAsync(new(now.AddMinutes(-1), now.AddMinutes(1)), null, count - 1, default);
        Check(page.Entries.Single().Location.Line == count && !page.HasMore && page.Notices.Any(x => x.Contains("流式")),
            "cache capacity preserves the final original record with explicit streaming notice");
        var context = await reader.ContextAsync(page.Selection, page.Entries[0].Location, 1, 1, default);
        Check(context.Entries.Select(x => x.Location.Line).SequenceEqual(new long[] { count - 1, count }), "dense streaming context retains original adjacency");
        Check(await reader.ExportAsync(page.Selection, Path.Combine(root, "..", "dense-export.jsonl"), true, 0, default) == count,
            "all dense snapshot originals remain exportable beyond metadata cache capacity");
        Console.WriteLine($"MEASURE H02 dense_rows={count}; query_context_export_ms={watch.Elapsed.TotalMilliseconds:F2}; scanned={reader.ScannedLines}");
    }
    static async Task Logs(string root)
    {
        var authority = new LocalManagementMode(root); authority.ConfigurePassword("12345"); authority.Enter("12345");
        var directory = Path.Combine(root, "diagnostics"); Directory.CreateDirectory(directory);
        var now = DateTimeOffset.UtcNow;
        File.WriteAllLines(Path.Combine(directory, "runtime.jsonl"), Enumerable.Range(0, 3000).Select(i =>
            JsonSerializer.Serialize(new { At = now, Message = "synthetic-" + i, password = "DO-NOT-EXPORT" })));
        using var reader = new SoftwareLogReader(directory, authority);
        var query = new SoftwareLogQuery(now.AddMinutes(-1), now.AddMinutes(1), "synthetic");
        var watch = Stopwatch.StartNew(); var first = await reader.QueryAsync(query, null, 0, default); var firstMs = watch.Elapsed.TotalMilliseconds;
        var baseline = reader.ScannedLines; watch.Restart();
        var second = await reader.QueryAsync(query, first.Selection, 200, default);
        var previous = await reader.QueryAsync(query, first.Selection, 0, default);
        Console.WriteLine($"MEASURE H02 logs first_ms={firstMs:F2}; two_pages_ms={watch.Elapsed.TotalMilliseconds:F2}; first_scanned={baseline}; additional_scanned={reader.ScannedLines - baseline}");
        Check(second.Entries.Length == 200 && first.Entries[0] == previous.Entries[0] && second.Entries[0].Location.Line == 201,
            "pagination preserves frozen order and original file/line/hash");
        Check(reader.ScannedLines - baseline <= 400, "two subsequent pages scan at most their 400 original rows, not all 6000 rows");
        var original = Path.Combine(directory, "runtime.jsonl"); var renamed = original + ".renamed";
        File.Move(original, renamed); File.WriteAllText(original, "new unrelated file\n");
        Check((await reader.QueryAsync(query, first.Selection, 0, default)).Entries[0] == first.Entries[0], "rotation pins the old file object instead of following the new path");
        var context = await reader.ContextAsync(first.Selection, second.Entries[0].Location, 2, 2, default);
        Check(context.Entries.Select(e => e.Location.Line).SequenceEqual(new long[] { 199, 200, 201, 202, 203 }), "indexed context preserves actual adjacency and original line numbers");
        var export = Path.Combine(root, "..", "complete-log-export.jsonl");
        Check(await reader.ExportAsync(first.Selection, export, false, 0, default) == 3000 && !File.ReadAllText(export).Contains("DO-NOT-EXPORT"), "complete export passes every original hash and redacts all retained records");
        using (var mutate = new FileStream(renamed, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { mutate.Position = 2; mutate.WriteByte((byte)'Z'); mutate.Flush(true); }
        try { await reader.QueryAsync(query, first.Selection, 0, default); throw new Exception("Changed original was accepted"); }
        catch (IOException) { Check(true, "same-length original mutation fails hash validation instead of serving cached text"); }
    }
    static async Task LogOwnership(string root)
    {
        foreach (var mode in new[] { "dispose", "evict", "revoke" })
        {
            var path = Path.Combine(root, mode); var authority = new LocalManagementMode(path); authority.ConfigurePassword("12345"); authority.Enter("12345");
            var directory = Path.Combine(path, "diagnostics"); Directory.CreateDirectory(directory); var file = Path.Combine(directory, "runtime.jsonl");
            var now = DateTimeOffset.UtcNow; File.WriteAllText(file, JsonSerializer.Serialize(new { At = now, Message = "synthetic" }) + "\n");
            using var reader = new SoftwareLogReader(directory, authority); var query = new SoftwareLogQuery(now.AddMinutes(-1), now.AddMinutes(1));
            var page = await reader.QueryAsync(query, null, 0, default);
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); int first = 0;
            reader.BeforeSourceRead = () => { if (Interlocked.Increment(ref first) == 1) { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); } };
            var pending = Task.Run(() => reader.ContextAsync(page.Selection, page.Entries[0].Location, 0, 0, default));
            Check(entered.Wait(TimeSpan.FromSeconds(3)), mode + " context owns a real pinned file before cancellation");
            try
            {
                if (mode == "dispose") reader.Dispose();
                else if (mode == "revoke") authority.Exit();
                else for (var i = 0; i < 4; i++) await reader.QueryAsync(query, null, 0, default);
                Check(!pending.IsCompleted && reader.OpenSources >= 1, mode + " retains active file ownership while cancellation is pending");
            }
            finally { release.Set(); }
            try { await pending; throw new Exception("Cancelled context published"); } catch (OperationCanceledException) { }
            reader.Dispose();
            Check(reader.OpenSources == 0, mode + " final read exit releases all pinned sources and selection CTS");
            using var exclusive = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
    }
    sealed class ControlledTime : TimeProvider
    {
        readonly object sync = new();
        DateTimeOffset utc = DateTimeOffset.UtcNow;
        long stamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() { lock (sync) return utc; }
        public override long GetTimestamp() { lock (sync) return stamp; }
        public void Move(TimeSpan wall, TimeSpan elapsed) { lock (sync) { utc += wall; stamp += elapsed.Ticks; } }
    }
    static long JournalCount(RuntimeJournal j, string key) { using var doc = JsonDocument.Parse(Wire.Encode(j.Status)); return doc.RootElement.GetProperty(key).GetInt64(); }
    static async Task JournalTime(string root)
    {
        var clock = new ControlledTime(); int attempts = 0;
        await using var journal = new RuntimeJournal(root, _ => { }, null, clock);
        journal.BeforeAppend = () => { if (Interlocked.Increment(ref attempts) == 1) throw new IOException("Synthetic write failure"); };
        journal.Write(new { Event = "failure" }); await Until(() => JournalCount(journal, "Dropped") == 1);
        clock.Move(TimeSpan.FromHours(-12), TimeSpan.FromSeconds(2));
        journal.Write(new { Event = "after-wall-clock-rollback" });
        await Until(() => JournalCount(journal, "Written") + JournalCount(journal, "Dropped") >= 2);
        Console.WriteLine($"MEASURE H02 time rollback_hours=12; monotonic_seconds=2; attempts={attempts}; written={JournalCount(journal, "Written")}");
        Check(JournalCount(journal, "Written") == 1, "wall-clock rollback cannot postpone the one-second diagnostic recovery interval");
        journal.BeforeAppend = () => throw new IOException("Synthetic second failure");
        journal.Write(new { Event = "failure-again" }); await Until(() => JournalCount(journal, "Dropped") == 2);
        clock.Move(TimeSpan.FromDays(2), TimeSpan.Zero);
        journal.BeforeAppend = () => Interlocked.Increment(ref attempts);
        journal.Write(new { Event = "wall-clock-jump" }); await Until(() => JournalCount(journal, "Written") + JournalCount(journal, "Dropped") >= 4);
        Check(JournalCount(journal, "Written") == 1, "wall-clock jump cannot bypass the monotonic recovery backoff");
        clock.Move(TimeSpan.Zero, TimeSpan.FromSeconds(2)); journal.Write(new { Event = "elapsed" });
        await Until(() => JournalCount(journal, "Written") == 2);
        var now = DateTimeOffset.UtcNow;
        var health = new DeviceState("synthetic", "epoch", 1, DeviceHealth.Ready, now, now, now.AddSeconds(1), "controlled");
        Check(health.IsValid(now) && !health.IsValid(now.AddHours(-1)) && !health.IsValid(now.AddHours(1)),
            "existing health validity rejects future confirmations after rollback and expiry after forward jump");
    }
}
