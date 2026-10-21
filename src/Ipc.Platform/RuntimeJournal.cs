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
                void Maintain()
                {
                    var days = Volatile.Read(ref retention).Days;
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
                        await File.AppendAllTextAsync(path, line + Environment.NewLine);
                    }
                    available = queue.Reader.WaitToReadAsync().AsTask();
                }
            }
            catch (Exception ex) { failure("Diagnostic journal failure: " + ex.GetType().Name); }
        });
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
