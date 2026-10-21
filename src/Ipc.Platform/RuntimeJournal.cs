using System.Threading.Channels;

namespace Ipc.Platform;

// Diagnostic metadata only; authoritative business facts remain in the transactional store.
public sealed class RuntimeJournal : IAsyncDisposable
{
    readonly Channel<object> queue = Channel.CreateBounded<object>(new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    readonly Task worker;
    readonly Action<string> failure;
    sealed record RetentionChange(int? Days);
    RetentionChange retention;
    public RuntimeJournal(string directory, Action<string> failure) : this(directory, failure, null) { }
    public RuntimeJournal(string directory, Action<string> failure, int? retentionDays)
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
                Maintain();
                using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
                var available = queue.Reader.WaitToReadAsync().AsTask();
                var tick = timer.WaitForNextTickAsync().AsTask();
                while (true)
                {
                    await Task.WhenAny(available, tick);
                    if (tick.IsCompleted) { if (!await tick) break; Maintain(); tick = timer.WaitForNextTickAsync().AsTask(); }
                    if (!available.IsCompleted) continue;
                    if (!await available) break;
                    while (queue.Reader.TryRead(out var entry))
                    {
                        if (entry is RetentionChange) { Maintain(); continue; }
                        Maintain();
                        var line = System.Text.Json.JsonSerializer.Serialize(entry, new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
                        await File.AppendAllTextAsync(path, DiagnosticText.Protect(line) + Environment.NewLine);
                    }
                    available = queue.Reader.WaitToReadAsync().AsTask();
                }
            }
            catch (Exception ex) { failure("Diagnostic journal failure: " + ex.GetType().Name); }
        });
    }
    public static void AppendMetadata(string directory, object metadata)
    {
        DeploymentPaths.RequireOrdinaryAncestors(directory); Directory.CreateDirectory(directory); MaintainFiles(directory, null);
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
    public void Write(object metadata) { if (!queue.Writer.TryWrite(metadata)) failure("Diagnostic journal queue full; business audit remains in SQLite"); }
    public async ValueTask DisposeAsync() { queue.Writer.TryComplete(); await worker; }
}
