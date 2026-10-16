using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;
using Microsoft.Extensions.DependencyInjection;

var map = args.Chunk(2).ToDictionary(x => x[0], x => x.Length == 2 ? x[1] : "");
string Get(string key, string fallback) => map.GetValueOrDefault(key, fallback);
var root = Get("--config", Path.Combine(AppContext.BaseDirectory, "config"));
var data = Get("--data-root", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImporterPc", "data"));
var action = Get("--action", "snapshot");
if (action is "backup" or "preflight" or "restore" or "upgrade")
{
    // Maintenance must obey the same fixed Windows account and protected machine policy
    // before creating a restore destination, opening a database or acquiring a data lease.
    _ = MachineRuntimePolicy.ResolveLeaseRoot(null);
    // The maintenance owner uses the same data lease as runtime. Read-only actions do
    // not construct SupplyStore, create a missing station, or change SQLite settings.
    var station = Wire.Decode<StationConfig>(File.ReadAllText(Path.Combine(root, "station.json")));
    var directory = Path.Combine(data, station.Mode.ToString(), station.StationId);
    if (action != "restore" && !Directory.Exists(directory)) throw new DirectoryNotFoundException("Initialize station before maintenance; missing source retained");
    if (action == "restore") Directory.CreateDirectory(directory);
    using var lease = new ResourceLease(directory, ["database"]);
    var dbPath = Path.Combine(directory, "station.db");
    if (action == "restore")
    {
        var source = Get("--file", "backup.db");
        var inspection = SupplyStore.InspectReadOnly(source);
        SupplyStore.RestoreVerified(source, dbPath);
        Console.WriteLine(Wire.Encode(new { Status = "RestoredQuarantined", SourceFormat = inspection.FormatVersion,
            DestinationFormat = SupplyStore.CurrentFormatVersion, SourceBackupSha256 = Wire.FileHash(source),
            Launch = "NOT_RUN", ExternalReconciliation = "REQUIRED" })); return;
    }
    if (action == "preflight")
    {
        var inspection = SupplyStore.InspectReadOnly(dbPath);
        Console.WriteLine(Wire.Encode(new { Status = inspection.UpgradeBlockers.Length == 0 ? "ReadOnlyInspectionCompleted" : "Blocked",
            Inspection = inspection, Launch = "NOT_RUN" }));
        if (inspection.UpgradeBlockers.Length != 0) Environment.ExitCode = 2;
    }
    else if (action == "backup") Console.WriteLine(Wire.Encode(SupplyStore.BackupReadOnly(dbPath, Get("--file", "backup.db"))));
    else
    {
        if (!map.TryGetValue("--previous-release", out var previous) || !map.TryGetValue("--target-release", out var selected) || !map.ContainsKey("--file"))
            throw new InvalidDataException("Explicit upgrade requires --previous-release, --target-release and new --file backup");
        var selectedIdentity = ReleaseIdentity(selected);
        if (selectedIdentity.ProgramAssemblySha256 != Wire.FileHash(typeof(ProductRuntime).Assembly.Location) ||
            !string.Equals(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(Path.Combine(selected, "app", "config")).TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Upgrade must execute the selected release program and configuration");
        var previousIdentity = ReleaseIdentity(previous);
        // Changing station identity/mode/root must be a separate reviewed migration, not
        // silently selecting a different database while backing up an unrelated station.
        var previousStation = Wire.Decode<StationConfig>(File.ReadAllText(Path.Combine(previous, "app", "config", "station.json")));
        if (previousStation.StationId != station.StationId || previousStation.Mode != station.Mode)
            throw new InvalidDataException("Previous and selected station identity/mode differ; controlled migration required");
        var file = map["--file"];
        Console.WriteLine(Wire.Encode(SupplyStore.UpgradeVerified(dbPath, file, Get("--receipt", file + ".upgrade.json"), previousIdentity, selectedIdentity)));
    }
    return;
}
MaintenanceReleaseIdentity ReleaseIdentity(string release)
{
    var app = Path.Combine(release, "app");
    var configuration = new Dictionary<string, string>();
    foreach (var name in new[] { "product.json", "station.json", "presentation.json" })
        configuration.Add(name, Wire.FileHash(Path.Combine(app, "config", name)));
    return new(Wire.FileHash(Path.Combine(app, "Ipc.Product.exe")), Wire.FileHash(Path.Combine(app, "Ipc.Product.dll")),
        Wire.FileHash(Path.Combine(release, "SHA256SUMS")), configuration);
}
var host = ProductHost.Build(new(Path.Combine(root, "product.json"), Path.Combine(root, "station.json"), Path.Combine(root, "presentation.json"), data, View: map.GetValueOrDefault("--view"), Layout: map.GetValueOrDefault("--layout")));
var runtime = host.Services.GetRequiredService<ProductRuntime>();
Exception? operationFailure = null;
try
{
    await host.StartAsync();
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
catch (Exception ex) { operationFailure = ex; }
finally
{
    var failures = new List<Exception>();
    if (operationFailure is not null) failures.Add(operationFailure);
    try { await host.StopAsync(); } catch (Exception ex) { failures.Add(ex); }
    var runtimeDisposed = false;
    try { await runtime.DisposeAsync(); runtimeDisposed = true; } catch (Exception ex) { failures.Add(ex); }
    if (runtimeDisposed) try { host.Dispose(); } catch (Exception ex) { failures.Add(ex); }
    if (failures.Count > 0) throw new AggregateException("Console operation and controlled cleanup retained failures", failures);
}
public sealed record ConsoleInput(string Action, Command? Command = null, string? Lane = null, string? Kind = null, string[]? Codes = null, int? Quantity = null,
    string? RequestId = null, string? File = null, string? OwnerId = null, string? Evidence = null);
