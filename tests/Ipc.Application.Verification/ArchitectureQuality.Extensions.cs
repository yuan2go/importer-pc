using Ipc.Application.Contracts;
using Ipc.Application.DirectPlc;
using Ipc.Application.ParcelFlow;
using Ipc.Assembly;
using Ipc.Platform;
using System.Reflection;

static partial class ArchitectureQuality
{
    public static async Task ExtensionChecks(string source, string evidence, string packageFile, string trustFile)
    {
        workspace = source; root = evidence; failures = 0; ParcelCapabilityFactory.RegisterContracts();
        var trust = Wire.Decode<ExtensionTrust>(File.ReadAllText(trustFile));
        foreach (var keepBackup in new[] { false, true })
        {
            var name = keepBackup ? "extension-backup" : "extension-release";
            var path = Path.Combine(root, name); Directory.CreateDirectory(path);
            var store = new ExtensionStore(Path.Combine(path, "store"), trust, RunMode.Simulation, Path.Combine(path, "leases"));
            var seed = Config("direct-plc");
            ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), seed);
            ExtensionStore.AtomicWrite(Path.Combine(path, "extension-store.json"), new ParcelExtensionStoreConfiguration(store.Root, trustFile));
            var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null);
            ParcelRuntime Create() => DirectPlcApplication.Create(launch, new ParcelCapabilityFactory(launch));
            PackageReference package;
            await using (var app = Create())
            {
                await app.StartAsync(default); await Maintenance(app); await app.CapabilitiesAsync();
                if (!keepBackup)
                {
                    typeof(ExtensionStore).GetProperty("PhaseBoundary", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(store, (Action<string>)(phase => { if (phase == "Published") throw new IOException("Synthetic interruption after actual package publish"); }));
                    try { store.Install(packageFile, Wire.FileHash(packageFile)); Check(false, "install phase interruption should remain visible"); }
                    catch (IOException) { Check(store.Operations().Single().State == "RecoveryRequired", "actual signed installation interruption retains original operation journal"); }
                    var operation = store.Operations().Single();
                    Check((await app.ManageCapabilityAsync("extension-recover", operation.Id)).Status == "Installed", "actual Application management recovers original published package without new identity");
                }
                else Check((await app.InstallCapabilityAsync(packageFile, Wire.FileHash(packageFile))).Status == "Installed", "Application installs signed capability before binding");
                package = store.Inventory().Single().Package;
                var m = store.VerifyInstalled(package).Manifest.Modules.Single(x => x.Id == "parcel-plc");
                var binding = new CapabilityBinding("plc", "extension:" + package.Id + "/" + m.Id, package.Version, "quality.signed.plc", "reference://quality/signed-plc", m.ContractIdentity!, new() { ["peer-root"] = Path.Combine(path, "peers"), ["complete-on-query"] = "true" }, package, m.Id);
                var s = await app.SnapshotAsync();
                Check((await app.StageConfigurationAsync(s.Configuration.Revision, s.Configuration.Effective with { Plc = binding, Extensions = new([package], []) }, "Synthetic activation of exact signed PLC")).Status == "PendingRestart", "installed signed module binds only through complete CAS/Pending candidate");
                Check((await app.ManageCapabilityAsync("extension-uninstall", Wire.Encode(package))).Status == "Rejected", "Pending configuration consumer prevents retirement before activation");
            }
            await using (var app = Create())
            {
                await app.StartAsync(default); Check((await Command(app, "Resume")).Status == "Persisted", "controlled activation retains pause until explicit Resume"); app.EmitReference("signed-original", "00017", "signed-source");
                var s = await Wait(app, s => s.Operations.FirstOrDefault()?.State == ParcelState.PlcAccepted); var op = s.Operations.Single();
                Check(op.Plc!.Request.Binding.Package == package && op.Plc.Attempts == 1, "actual signed Application port executes and freezes package identity");
                await app.ReconcileAsync(op.Id); await Maintenance(app);
                if (keepBackup) Check((await app.BackupAsync(Path.Combine(path, "original.backup.db"))).Status == "BackupCreated", "package-bearing backup registers an offline consumer with complete custody");
                s = await app.SnapshotAsync();
                Check((await app.StageConfigurationAsync(s.Configuration.Revision, seed, "Synthetic settled signed module removal")).Status == "PendingRestart", "safe removal stages complete built-in deployment while retaining original history");
                Check((await app.ManageCapabilityAsync("extension-uninstall", Wire.Encode(package))).Status == "Rejected", "active resource/package use prevents uninstall before controlled restart");
            }
            // Same machine/mode registry contains a real stopped legacy Supply root.
            var supplyDb = Path.Combine(path, "offline-supply", "station.db");
            var supplyConfig = Wire.Decode<StationConfig>(File.ReadAllText(Path.Combine(workspace, "config/station.simulation.json")));
            using (var legacy = new Ipc.Supply.SupplyStore(supplyDb))
                legacy.Commit(legacy.Load(), "SyntheticOfflineSupply", "deployment", "synthetic", config: new(Wire.Id(), "seed", supplyConfig, "synthetic", "Effective", "", DateTimeOffset.UtcNow));
            store.RegisterConsumer(supplyDb);
            await using (var app = Create())
            {
                await app.StartAsync(default); await Maintenance(app);
                if (!keepBackup)
                {
                    using (var legacy = new Ipc.Supply.SupplyStore(supplyDb))
                        legacy.Commit(legacy.Load(), "SyntheticPendingSupply", "legacy-pending", "synthetic", config: new("legacy-pending", "seed", supplyConfig with {Extensions=new([package],[])}, "synthetic", "PendingActivation", "", DateTimeOffset.UtcNow));
                    Check((await app.ManageCapabilityAsync("extension-uninstall", Wire.Encode(package))).Status == "Rejected", "R04 actual Parcel management retains mixed offline Supply Pending package");
                    using (var legacy = new Ipc.Supply.SupplyStore(supplyDb))
                        legacy.Commit(legacy.Load(), "SyntheticRejectedSupply", "legacy-pending", "synthetic", config: new("legacy-pending", "seed", supplyConfig with {Extensions=new([package],[])}, "synthetic", "Rejected", "", DateTimeOffset.UtcNow));
                }
                var result = await app.ManageCapabilityAsync("extension-uninstall", Wire.Encode(package));
                Check(result.Status == (keepBackup ? "Rejected" : "Uninstalled"), keepBackup ? "offline original backup pins exact package after live binding and resources are removed" : "cleared live deployment and settled originals permit real safe uninstall through Application entry");
                var history = await app.HistoryAsync(); Check(history.Items.Single().Operation.Plc!.Request.Binding.Package == package && history.Items.Single().Operation.Plc!.Attempts == 1, "uninstall decision preserves historical package/request/acceptance facts");
            }
            if (!keepBackup)
            {
                Check(store.Inventory().Single().Status == "Uninstalled" && store.Operations().Any(x => x.State == "Uninstalled"), "retirement preserves permanent registration and original uninstall operation");
                File.WriteAllText(Path.Combine(path, "unavailable-consumer.db"), "Synthetic unreadable offline consumer"); store.RegisterConsumer(Path.Combine(path, "unavailable-consumer.db"));
                try { store.InspectConsumers(ExtensionConsumerReaders.Inspect); Check(false, "unreadable offline consumer must not become empty references"); }
                catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidDataException) { Check(true, "unreadable offline consumer explicitly blocks maintenance rather than clearing references"); }
            }
        }
        await PendingOnlyBackupCheck(packageFile, trustFile, trust);
        if (failures != 0) throw new Exception("AQ extension failures: " + failures);
        Console.WriteLine("PASS AQ EXTENSIONS COMPLETE");
    }
    static async Task PendingOnlyBackupCheck(string packageFile, string trustFile, ExtensionTrust trust)
    {
        var path = Path.Combine(root, "extension-pending-only-backup"); Directory.CreateDirectory(path);
        var store = new ExtensionStore(Path.Combine(path, "store"), trust, RunMode.Simulation, Path.Combine(path, "leases"));
        var seed = Config("direct-plc"); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), seed);
        ExtensionStore.AtomicWrite(Path.Combine(path, "extension-store.json"), new ParcelExtensionStoreConfiguration(store.Root, trustFile));
        var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null);
        ParcelRuntime Create() => DirectPlcApplication.Create(launch, new ParcelCapabilityFactory(launch));
        PackageReference package; string original;
        await using (var app = Create())
        {
            await app.StartAsync(default); app.EmitReference("pending-backup-original", "001", "pending-backup-source");
            original = (await Wait(app, s => s.Operations.FirstOrDefault()?.State == ParcelState.PlcAccepted)).Operations.Single().Id;
            await Maintenance(app); Check((await app.InstallCapabilityAsync(packageFile, Wire.FileHash(packageFile))).Status == "Installed", "Pending-only backup probe installs actual signed package");
            package = store.Inventory().Single().Package; var s = await app.SnapshotAsync();
            var module = store.VerifyInstalled(package).Manifest.Modules.Single(x => x.Id == "parcel-plc");
            var binding = new CapabilityBinding("plc", "extension:" + package.Id + "/parcel-plc", package.Version, "quality.pending.plc", "reference://quality/pending-plc", module.ContractIdentity!, new() { ["peer-root"] = Path.Combine(path, "peers"), ["complete-on-query"] = "true" }, package, module.Id);
            Check((await app.StageConfigurationAsync(s.Configuration.Revision, s.Configuration.Effective with { Plc = binding, Extensions = new([package], []) }, "Pending-only package before original settlement")).Status == "PendingRestart", "candidate-only package retained while original main is unresolved");
        }
        await using (var app = Create())
        {
            await app.StartAsync(default); var s = await app.SnapshotAsync();
            Check(s.Configuration.Status == "ActivationFailed" && s.Configuration.Effective.Extensions is null && s.Configuration.Pending?.Extensions?.Packages.Single() == package, "restart keeps package solely in rejected Pending without initializing active extension resources");
            await Maintenance(app); Check((await app.BackupAsync(Path.Combine(path, "pending-only.backup.db"))).Status == "BackupCreated", "actual backup preserves unactivated Pending package");
            await app.ReconcileAsync(original); s = await app.SnapshotAsync();
            Check(s.Operations.Single().State == ParcelState.PhysicalCompleted && (await app.StageConfigurationAsync(s.Configuration.Revision, seed, "Clear live Pending after original settlement")).Status == "PendingRestart", "live original and candidate can be cleared independently of immutable backup");
        }
        await using (var app = Create())
        {
            await app.StartAsync(default); await Maintenance(app);
            Check((await app.ManageCapabilityAsync("extension-uninstall", Wire.Encode(package))).Status == "Rejected" && store.Inventory().Single().Status == "Installed", "offline backup with package only in Pending prevents uninstall after all live references clear");
        }
    }

}
