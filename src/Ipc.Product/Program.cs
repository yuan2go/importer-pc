using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;
using Microsoft.Extensions.DependencyInjection;

var map = args.Chunk(2).ToDictionary(x => x[0], x => x.Length == 2 ? x[1] : "");
string Get(string key, string fallback) => map.GetValueOrDefault(key, fallback);
var root = Get("--config", Path.Combine(AppContext.BaseDirectory, "config"));
var data = Get("--data-root", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImporterPc", "data"));
var action = Get("--action", "snapshot");
if (action is "backup" or "preflight" or "restore")
{
    // Offline operations acquire the same data ownership lock. Restore always requires a new
    // data directory, preserves the old database and enters external reconciliation quarantine.
    var station = Wire.Decode<StationConfig>(File.ReadAllText(Path.Combine(root, "station.json")));
    var directory = Path.Combine(data, station.Mode.ToString(), station.StationId); Directory.CreateDirectory(directory);
    using var lease = new ResourceLease(directory, ["database"]);
    var dbPath = Path.Combine(directory, "station.db");
    if (action == "restore")
    {
        if (File.Exists(dbPath) || File.Exists(dbPath + "-wal")) throw new IOException("Restore requires a fresh station data directory; original retained");
        SupplyStore.RestoreVerified(Get("--file", "backup.db"), dbPath);
        Console.WriteLine("RESTORED / QUARANTINED"); return;
    }
    if (!File.Exists(dbPath)) throw new FileNotFoundException("Initialize station before maintenance", dbPath);
    using var store = new SupplyStore(dbPath);
    if (action == "preflight")
    {
        var state = store.Load(); var pending = store.PendingCount();
        if (pending != 0 || state.Session is { Status: not (SessionStatus.Closed or SessionStatus.Rejected) } || state.Quarantined) throw new InvalidOperationException("Upgrade blocked: close context, reconcile pending effects/quarantine");
        Console.WriteLine("PREFLIGHT PASS / schema 1 / no pending effects");
    }
    else { store.Backup(Get("--file", "backup.db")); Console.WriteLine("CONSISTENT BACKUP SAVED"); }
    return;
}
using var host = ProductHost.Build(new(Path.Combine(root, "product.json"), Path.Combine(root, "station.json"), Path.Combine(root, "presentation.json"), data));
await host.StartAsync();
var runtime = host.Services.GetRequiredService<ProductRuntime>();
try
{
    if (action == "serve")
    {
        Console.WriteLine("JSON command console. One command per line; snapshot / emit / replay / reconcile / export / quit supported. SIMULATION only.");
        string? line;
        while ((line = Console.ReadLine()) is not null && line != "quit")
        {
            try
            {
                if (line == "snapshot") { Console.WriteLine(Wire.Encode(await runtime.Application!.SnapshotAsync())); continue; }
                var input = Wire.Decode<ConsoleInput>(line);
                if (input.Action == "emit") await runtime.EmitAsync(input.Lane!, input.Kind!, input.Codes, input.Quantity);
                else if (input.Action == "replay") await runtime.ReplayAsync(input.Lane!);
                else if (input.Action == "reconcile") await runtime.ReconcileAsync(input.RequestId!);
                else if (input.Action == "export") await runtime.ExportAsync(input.File!, input.OwnerId);
                else if (input.Action == "review-observations") await runtime.Application!.ReviewObservationsAsync(input.Evidence!);
                else if (input.Action == "prune-history") Console.WriteLine(await runtime.Application!.PruneHistoryAsync(input.Quantity ?? throw new InvalidDataException("Retention days required"), input.Evidence!));
                else if (input.Action == "clear-overload") await runtime.ClearOverloadAsync(input.Evidence!);
                else Console.WriteLine(Wire.Encode(await runtime.CommandAsync(input.Command ?? throw new InvalidDataException("Command required"))));
            }
            catch (Exception ex) { Console.WriteLine(Wire.Encode(new { Error = ex.GetType().Name, ex.Message })); }
        }
    }
    else if (action == "export") await runtime.ExportAsync(Get("--file", "diagnostics.json"));
    else Console.WriteLine(runtime.Application is null ? Wire.Encode(runtime.Diagnostics) : Wire.Encode(await runtime.Application.SnapshotAsync()));
    if (runtime.Diagnostics.Count != 0) Environment.ExitCode = 2;
}
finally { await host.StopAsync(); }
public sealed record ConsoleInput(string Action, Command? Command = null, string? Lane = null, string? Kind = null, string[]? Codes = null, int? Quantity = null,
    string? RequestId = null, string? File = null, string? OwnerId = null, string? Evidence = null);
