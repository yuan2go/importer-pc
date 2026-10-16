using System.Threading.Channels;

namespace Ipc.Platform;

// Diagnostic metadata only; authoritative business facts remain in the transactional store.
public sealed class RuntimeJournal : IAsyncDisposable
{
    readonly Channel<object> queue = Channel.CreateBounded<object>(new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    readonly Task worker;
    readonly Action<string> failure;
    public RuntimeJournal(string directory, Action<string> failure)
    {
        this.failure = failure;
        Directory.CreateDirectory(directory);
        worker = Task.Run(async () =>
        {
            try
            {
                await foreach (var entry in queue.Reader.ReadAllAsync())
                {
                    var path = Path.Combine(directory, "runtime.jsonl");
                    if (File.Exists(path) && new FileInfo(path).Length > 5_000_000)
                    {
                        for (int i = 2; i >= 1; i--) { var old = path + "." + i; if (File.Exists(old)) File.Move(old, path + "." + (i + 1), overwrite: true); }
                        File.Move(path, path + ".1", overwrite: true);
                    }
                    var line = System.Text.Json.JsonSerializer.Serialize(entry, new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
                    await File.AppendAllTextAsync(path, line + Environment.NewLine);
                }
            }
            catch (Exception ex) { failure("Diagnostic journal failure: " + ex.GetType().Name); }
        });
    }
    public void Write(object metadata) { if (!queue.Writer.TryWrite(metadata)) failure("Diagnostic journal queue full; business audit remains in SQLite"); }
    public async ValueTask DisposeAsync() { queue.Writer.TryComplete(); await worker; }
}
