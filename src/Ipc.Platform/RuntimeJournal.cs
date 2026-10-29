using System.Threading.Channels;

namespace Ipc.Platform;

// Diagnostic metadata only; authoritative business facts remain in the transactional store.
public sealed partial class RuntimeJournal : IAsyncDisposable
{
    long dropped, written;
    string? lastFailure, currentFailure;
    long progressTicks;
    public object Status => new { State = worker.IsCompleted ? "Stopped" : Volatile.Read(ref currentFailure) is null ? "Running" : "Degraded",
        QueueDepth = queue.Reader.Count, Dropped = Interlocked.Read(ref dropped), DebugDropped = Interlocked.Read(ref debugDropped), Written = Interlocked.Read(ref written),
        LastFailure = Volatile.Read(ref lastFailure), CurrentFailure = Volatile.Read(ref currentFailure), LastProgressUtcTicks = Interlocked.Read(ref progressTicks) };
    void Report(string detail)
    {
        Volatile.Write(ref lastFailure, detail);
        if (Interlocked.Exchange(ref currentFailure, detail) is null) failure(detail);
    }
    readonly Channel<object> queue = Channel.CreateBounded<object>(new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    readonly Task worker;
    readonly Action<string> failure;
    internal Action? BeforeAppend { get; set; }
    sealed record RetentionChange(int? Days);
    RetentionChange retention;
    public RuntimeJournal(string directory, Action<string> failure) : this(directory, failure, null) { }
    public RuntimeJournal(string directory, Action<string> failure, int? retentionDays) : this(directory, failure, retentionDays, TimeProvider.System) { }
    internal RuntimeJournal(string directory, Action<string> failure, int? retentionDays, TimeProvider clock)
    {
        ValidateDays(retentionDays);
        retention = new(retentionDays);
        this.failure = failure;
        directory = Path.GetFullPath(directory);
        DeploymentPaths.RequireOrdinaryAncestors(directory);
        Directory.CreateDirectory(directory);
        worker = Task.Run(async () =>
        {
            try
            {
                var path = Path.Combine(directory, "runtime.jsonl");
                void Maintain() => MaintainFiles(directory, Volatile.Read(ref retention).Days);
                void TryMaintain() { try { Maintain(); } catch (Exception ex) { Report("Diagnostic maintenance failure: " + ex.GetType().Name); } }
                TryMaintain();
                long? failedAt = null; long reportedDebugDrops = 0;
                bool ProbeDue() => failedAt is not { } stamp || clock.GetElapsedTime(stamp) >= TimeSpan.FromSeconds(1);
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                var available = queue.Reader.WaitToReadAsync().AsTask();
                var tick = timer.WaitForNextTickAsync().AsTask();
                while (true)
                {
                    await Task.WhenAny(available, tick);
                    if (tick.IsCompleted)
                    {
                        if (!await tick) break;
                        // Retention/daily rotation also advance while healthy and idle.
                        // Maintenance alone cannot clear an append failure; a real probe must write.
                        TryMaintain();
                        var lostDebug = Interlocked.Read(ref debugDropped);
                        if (lostDebug != reportedDebugDrops && queue.Writer.TryWrite(new { At = clock.GetUtcNow(), Source = "诊断日志", Level = "警告", Event = "详细日志写入繁忙", DroppedDebugRecords = lostDebug })) reportedDebugDrops = lostDebug;
                        if (Volatile.Read(ref currentFailure) is not null && ProbeDue())
                            queue.Writer.TryWrite(new { At = clock.GetUtcNow(), Event = "DiagnosticRecoveryProbe" });
                        tick = timer.WaitForNextTickAsync().AsTask();
                    }
                    if (!available.IsCompleted) continue;
                    if (!await available) break;
                    while (queue.Reader.TryRead(out var entry))
                    {
                        if (!ProbeDue()) { Interlocked.Increment(ref dropped); continue; }
                        try
                        {
                            BeforeAppend?.Invoke();
                            Maintain();
                            var line = System.Text.Json.JsonSerializer.Serialize(entry, new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
                            var protectedLine = DiagnosticText.Protect(line);
                            await File.AppendAllTextAsync(path, protectedLine + Environment.NewLine);
                            PublishTail(protectedLine, clock.GetUtcNow());
                            Interlocked.Increment(ref written); Interlocked.Exchange(ref progressTicks, DateTimeOffset.UtcNow.UtcTicks);
                            Volatile.Write(ref currentFailure, null);
                            failedAt = null;
                        }
                        catch (Exception ex)
                        {
                            failedAt = clock.GetTimestamp();
                            Interlocked.Increment(ref dropped); Report("Diagnostic append failure: " + ex.GetType().Name);
                            // Drain disposable metadata promptly; one subsequent probe per second.
                            // Never delay disposal once per dropped entry, or retry an old entry.
                        }
                    }
                    available = queue.Reader.WaitToReadAsync().AsTask();
                }
            }
            catch (Exception ex) { Report("Diagnostic journal worker failure: " + ex.GetType().Name); queue.Writer.TryComplete(ex); }
        });
    }
    public static void AppendMetadata(string directory, object metadata) => AppendMetadata(directory, metadata, createDirectory: true);
    internal static void AppendMetadata(string directory, object metadata, bool createDirectory)
    {
        DeploymentPaths.RequireOrdinaryAncestors(directory);
        if (createDirectory) Directory.CreateDirectory(directory);
        else if ((File.GetAttributes(directory) & FileAttributes.Directory) == 0) throw new IOException("Diagnostic directory unavailable");
        MaintainFiles(directory, null);
        var bytes = System.Text.Encoding.UTF8.GetBytes(DiagnosticText.Protect(System.Text.Json.JsonSerializer.Serialize(metadata)) + "\n");
        using var stream = new FileStream(Path.Combine(directory, "runtime.jsonl"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        stream.Write(bytes); stream.Flush(true);
    }
    static void MaintainFiles(string directory, int? days)
        => Capture(directory, () => { MaintainFilesUnlocked(directory, days); return true; });
    // Cooperating writers and readers share only the brief file-open/rotation boundary.
    // Scanning, regex, context and exports never hold this lock or a business lock.
    public static T Capture<T>(string directory, Func<T> capture)
    {
        if (!Directory.Exists(directory)) return capture();
        var path = Path.Combine(directory, "rotation.lease"); DeploymentPaths.RequireOrdinaryAncestors(path);
        FileStream? held = null;
        for (var attempt = 0; held is null; attempt++)
        {
            try { held = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 && attempt < 50) { Thread.Sleep(2); }
        }
        using (held) return capture();
    }
    static void MaintainFilesUnlocked(string directory, int? days)
    {
        var path = Path.Combine(directory, "runtime.jsonl");
                    // These four diagnostic files are disposable metadata. Never
                    // enumerate arbitrary files or delete business/custody evidence.
                    for (var i = 0; i <= 3; i++) DeploymentPaths.RequireOrdinaryAncestors(i == 0 ? path : path + "." + i);
                    var now = DateTime.UtcNow;
                    if (File.Exists(path) && (new FileInfo(path).Length > 5_000_000 || days is not null && File.GetLastWriteTimeUtc(path).Date < now.Date))
                    {
                        for (int i = 2; i >= 1; i--) { var old = path + "." + i; if (File.Exists(old)) File.Move(old, path + "." + (i + 1), overwrite: true); }
                        File.Move(path, path + ".1", overwrite: true);
                    }
                    if (days is { } keep)
                        for (var i = 1; i <= 3; i++)
                        {
                            var file = path + "." + i;
                            if (File.Exists(file) && File.GetLastWriteTimeUtc(file) <= now.AddDays(-keep)) File.Delete(file);
                        }
    }
    static void ValidateDays(int? days) { if (days is < 1 or > 3650) throw new ArgumentOutOfRangeException(nameof(days), "诊断日志保留天数须为1～3650"); }
    public void ConfigureRetention(int? days)
    {
        ValidateDays(days);
        var next = new RetentionChange(days);
        // A full diagnostic queue may drop metadata, never an activated policy.
        // The worker observes this value on its next entry or maintenance tick.
        Volatile.Write(ref retention, next); Write(next);
    }
    public void Write(object metadata)
    {
        if (!queue.Writer.TryWrite(metadata))
        { Interlocked.Increment(ref dropped); Report("Diagnostic queue full/closed; metadata dropped; authoritative business audit remains in SQLite"); }
    }
    public async ValueTask DisposeAsync() { queue.Writer.TryComplete(); await worker; }
}
