using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;
using Microsoft.Extensions.DependencyInjection;

SupplyContractRegistration.Register();
var map = args.Chunk(2).ToDictionary(x => x[0], x => x.Length == 2 ? x[1] : "");
string Get(string key, string fallback) => map.GetValueOrDefault(key, fallback);
var root = Get("--config", Path.Combine(AppContext.BaseDirectory, "config"));
var data = Get("--data-root", Path.Combine(WindowsStoragePaths.UserRoot, "data"));
var action = Get("--action", "snapshot");
if (action == "extension-check")
{
    var allowed = new[] { "--action", "--mode", "--file", "--extension-trust", "--dependency-files" };
    if (map.Keys.Any(x => !allowed.Contains(x))) throw new ArgumentException("Unsupported static checker argument");
    var dependencies = map.TryGetValue("--dependency-files", out var dependencyFile)
        ? Wire.Decode<string[]>(File.ReadAllText(dependencyFile)) : [];
    Console.WriteLine(Wire.Encode(ExtensionBuildCheck.Inspect(Get("--file", ""), Get("--extension-trust", ""),
        Enum.Parse<RunMode>(Get("--mode", "Simulation"), ignoreCase: false), dependencies)));
    return;
}
if (action.StartsWith("extension-", StringComparison.Ordinal))
{
    // This branch never constructs ProductRuntime, loads a plugin, or starts a source/sender.
    var allowed = new[] { "--action", "--mode", "--file", "--sha256", "--operation", "--manifest-hash", "--config", "--data-root", "--extension-root", "--extension-trust" };
    if (map.Keys.Any(x => !allowed.Contains(x))) throw new ArgumentException("Unsupported installer argument");
    var mode = Enum.Parse<RunMode>(Get("--mode", "Simulation"), ignoreCase: false);
    if (!Enum.IsDefined(mode)) throw new InvalidDataException("Invalid mode");
    var paths = MachineRuntimePolicy.ExtensionPaths(mode, map.GetValueOrDefault("--extension-root"), map.GetValueOrDefault("--extension-trust"));
    var trust = Wire.Decode<ExtensionTrust>(File.ReadAllText(paths.TrustFile));
    // Inspection is read-only; mutation authority and the protected lease policy are
    // rechecked in the elevated branch before any filesystem transaction.
    var leaseRoot = action is "extension-inspect" or "extension-list" && OperatingSystem.IsWindows()
        ? Path.Combine(WindowsStoragePaths.MachineRoot, "leases")
        : MachineRuntimePolicy.InstallerLeaseRoot(Path.Combine(paths.Root, "leases"));
    var extensions = new ExtensionStore(paths.Root, trust, mode, leaseRoot);
    if (action == "extension-list") Console.WriteLine(Wire.Encode(new { Packages = extensions.Inventory(), Operations = extensions.Operations() }));
    else if (action == "extension-inspect") Console.WriteLine(Wire.Encode(extensions.Plan(Get("--file", ""))));
    else
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        MachineRuntimePolicy.RequireProtectedInstaller(AppContext.BaseDirectory);
        if (action == "extension-install") Console.WriteLine(Wire.Encode(extensions.Install(Get("--file", ""), Get("--sha256", ""))));
        else if (action == "extension-recover")
        {
            var originalId = Get("--operation", "");
            var original = extensions.Operations().Single(x => x.Id == originalId);
            Func<PackageReference[]>? references = null;
            if (original.Kind == "Uninstall")
            {
                var station = Wire.Decode<StationConfig>(File.ReadAllText(Path.Combine(root, "station.json")));
                if (station.Mode != mode) throw new InvalidDataException("Station/extension mode mismatch");
                references = () => extensions.InspectConsumers(Ipc.Assembly.ExtensionConsumerReaders.Inspect).Concat(SupplyStore.InspectRequiredExtensionPackages(Path.Combine(data, mode.ToString(), station.StationId, "station.db"))).Distinct().ToArray();
            }
            Console.WriteLine(Wire.Encode(extensions.Recover(originalId, refreshReferences: references)));
        }
        else if (action == "extension-uninstall")
        {
            var station = Wire.Decode<StationConfig>(File.ReadAllText(Path.Combine(root, "station.json")));
            if (station.Mode != mode) throw new InvalidDataException("Station/extension mode mismatch");
            var db = Path.Combine(data, mode.ToString(), station.StationId, "station.db");
            var package = extensions.Inventory().Single(x => x.Package.ManifestSha256 == Get("--manifest-hash", "") && x.Status == "Installed").Package;
            extensions.Uninstall(package, () => extensions.InspectConsumers(Ipc.Assembly.ExtensionConsumerReaders.Inspect).Concat(SupplyStore.InspectRequiredExtensionPackages(db)).Distinct().ToArray());
            Console.WriteLine(Wire.Encode(new { Status = "Uninstalled", Package = package }));
        }
        else throw new ArgumentException("Unknown extension action");
    }
    return;
}
if (action == "upgrade") throw new InvalidDataException("旧格式升级不受支持；原数据库与发行保留。");
if (action is "backup" or "preflight" or "restore")
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
        RegisterRecoveryCopy(station.Mode, source, dbPath);
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
    else if (action == "backup")
    {
        var destination = Get("--file", "backup.db"); RegisterRecoveryCopy(station.Mode, dbPath, destination);
        Console.WriteLine(Wire.Encode(SupplyStore.BackupReadOnly(dbPath, destination)));
    }
    return;
}
void RegisterRecoveryCopy(RunMode mode, string source, string destination)
{
    var required = SupplyStore.InspectRequiredExtensionPackages(source);
    var paths = MachineRuntimePolicy.ExtensionPaths(mode, map.GetValueOrDefault("--extension-root"), map.GetValueOrDefault("--extension-trust"));
    if (!File.Exists(paths.TrustFile))
    {
        if (required.Length > 0) throw new InvalidDataException("Recovery copy references exact packages but consumer registry/trust is unavailable; retain source");
        return;
    }
    var manager = new ExtensionStore(paths.Root, Wire.Decode<ExtensionTrust>(File.ReadAllText(paths.TrustFile)), mode, MachineRuntimePolicy.ResolveLeaseRoot(null));
    manager.RegisterConsumer(destination);
}
var host = ProductHost.Build(new(Path.Combine(root, "product.json"), Path.Combine(root, "station.json"), Path.Combine(root, "presentation.json"), data, View: map.GetValueOrDefault("--view"), Layout: map.GetValueOrDefault("--layout"), ExtensionRoot: map.GetValueOrDefault("--extension-root"), ExtensionTrustFile: map.GetValueOrDefault("--extension-trust")));
var runtime = host.Services.GetRequiredService<ProductRuntime>();
Exception? operationFailure = null;
try
{
    await host.StartAsync();
    if (action == "serve")
    {
        Console.WriteLine("JSON command console. One command per line; snapshot / configs / extensions / emit / replay / reconcile / export / quit supported. SIMULATION only.");
        if (runtime.Application is not null) runtime.StartHeadlessNotifications((cue, token) =>
        { Console.WriteLine(Wire.Encode(new { Notification = cue, Delivery = "AwaitingExplicitAcknowledgement" })); Console.Out.Flush(); return Task.CompletedTask; });
        string? line;
        while ((line = Console.ReadLine()) is not null && line != "quit")
        {
            try
            {
                if (line == "configs") { Console.WriteLine(Wire.Encode(await runtime.Application!.ConfigsAsync())); continue; }
                if (line == "integration-diagnostics") { Console.WriteLine(Wire.Encode(runtime.IntegrationDiagnostics)); continue; }
                if (line == "extensions") { Console.WriteLine(Wire.Encode(await runtime.ExtensionSnapshotAsync())); continue; }
                if (line == "snapshot") { Console.WriteLine(Wire.Encode(await runtime.Application!.SnapshotAsync())); continue; }
                var input = Wire.Decode<ConsoleInput>(line);
                if (input.Action == "notification-ack") { await runtime.AcknowledgeHeadlessNotificationAsync(input.Sequence ?? throw new InvalidDataException("Notification sequence required")); Console.WriteLine(Wire.Encode(new { Status = "NotificationAcknowledged", input.Sequence })); }
                else if (input.Action == "emit") await runtime.EmitAsync(input.Lane!, input.Kind!, input.Codes, input.Quantity);
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
    string? RequestId = null, string? File = null, string? OwnerId = null, string? Evidence = null, long? Sequence = null);
