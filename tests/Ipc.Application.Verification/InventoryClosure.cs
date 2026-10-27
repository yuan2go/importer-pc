using Ipc.Application.Contracts;
using Ipc.Application.DirectPlc;
using Ipc.Application.ParcelFlow;
using Ipc.Assembly;
using Ipc.Platform;

static partial class ArchitectureQuality
{
    static readonly string[] inventoryScopes = ["Effective", "Pending", "Frozen"];
    public static async Task InventoryClosureChecks(string source, string evidence)
    {
        workspace = source; root = evidence; failures = 0;
        var package = new PackageReference("inventory.reference", "1.0.0", new string('A', 64));
        foreach(var oldSchema in new[]{1,2})foreach(var scope in inventoryScopes)
        {var old=InventorySeed(oldSchema);InspectInventoryCase(oldSchema,scope,"unsupported-format",old,old,null);}
        foreach (var schema in new[] { 3 })
        {
            var seed = InventorySeed(schema);
            var plc = seed.Plc!; var unbound = new CapabilityBinding("login","reference-login","1.0.0","inventory.unbound-login","reference://inventory/login","PARCEL-REFERENCE-v1",new(){["subjects"]="synthetic"});
            var bound = plc with { Package = package, Module = "parcel-plc", Implementation = "extension:" + package.Id + "/parcel-plc" };
            var spare = plc with { InstanceId = "inventory.spare" };
            (string Name, ParcelConfiguration Candidate)[] invalid =
            [
                ("duplicate-identical", seed with { CapabilityInstances = [plc, unbound, plc] }),
                ("duplicate-hidden-package", seed with { CapabilityInstances = [plc, unbound, bound] }),
                ("role-mirror-conflict", seed with { CapabilityInstances = [plc with { Target = "reference://inventory/other" }, unbound] }),
                ("unbound-duplicate", seed with { CapabilityInstances = [plc, unbound, spare, spare with { Contract = "DIFFERENT" }] }),
                ("dangling-package", seed with { CapabilityInstances = [plc, unbound, bound with { InstanceId = "inventory.spare" }] }),
                ("disabled-dangling-package", seed with { CapabilityInstances = [plc, unbound, bound with { InstanceId = "inventory.spare", Enabled = false }] }),
                ("module-without-package", seed with { CapabilityInstances = [plc, unbound, spare with { Module = "parcel-plc" }] }),
                ("package-version-mismatch", seed with { Plc = bound with { Version = "2.0.0" }, Extensions = new([package], []) }),
                ("invalid-package-identity", seed with { Plc = bound with { Package = package with { Id = "INVALID" }, Implementation = "extension:INVALID/parcel-plc" }, Extensions = new([package with { Id = "INVALID" }], []) }),
                ("unknown-kind", seed with { CapabilityInstances = [plc, unbound, spare with { Kind = "future-kind" }] })
            ];
            foreach (var scope in inventoryScopes)
            {
                foreach (var test in invalid)
                    InspectInventoryCase(schema, scope, test.Name, seed, test.Candidate, null);
                InspectInventoryCase(schema, scope, "valid-no-inventory", seed, seed, []);
                InspectInventoryCase(schema, scope, "valid-role-mirrors", seed, seed with { CapabilityInstances = [plc, unbound] }, []);
                var exact = seed with { Plc = bound, CapabilityInstances = [bound, unbound], Extensions = new([package], []) };
                InspectInventoryCase(schema, scope, "valid-exact-package", seed, exact, [package]);
            }
            // Exercise real Application validation/CAS and a real accepted main chain.
            await using var app = await Open(seed with { CapabilityInstances = [plc, unbound] }, "inventory-live-" + schema);
            await Maintenance(app); var before = (await app.SnapshotAsync()).Configuration;
            var candidate = invalid.Single(x => x.Name == "duplicate-hidden-package").Candidate;
            Check(new DirectPlcFlow().Validate(candidate).Length != 0, "R03 schema" + schema + " Application format rejects raw inventory before deduplication");
            Check((await app.ValidateConfigurationAsync(candidate)).Status == "Rejected" &&
                (await app.StageConfigurationAsync(before.Revision, candidate, "Synthetic invalid raw inventory")).Status == "Rejected" &&
                Wire.Encode((await app.SnapshotAsync()).Configuration) == Wire.Encode(before), "R03 schema" + schema + " real validation/CAS rejection leaves original configuration unchanged");
            await Command(app, "Resume");
            app.EmitReference("inventory-valid-" + schema, "001", "inventory-valid-source-" + schema);
            var operation = (await Wait(app, s => s.Operations.FirstOrDefault()?.State == ParcelState.PlcAccepted)).Operations.Single();
            Check(operation.Plc!.Attempts == 1, "R03 schema" + schema + " legal complete mirrored inventory still runs actual DirectPlc once");
        }
        if (failures != 0) throw new Exception("R03 inventory closure failures: " + failures);
        Console.WriteLine("PASS R03 INVENTORY CLOSURE COMPLETE");
    }
    static ParcelConfiguration InventorySeed(int schema) => Config("direct-plc") with
    {
        SchemaVersion = schema, Topology = schema == 3 ? Config("direct-plc").Topology : null,
        WeightValidity = schema == 1 ? null : WeightValidity.UntilDispatch
    };
    static void WriteInventoryConsumer(string directory, ParcelConfiguration seed, ParcelConfiguration candidate, string scope)
    {
        Directory.CreateDirectory(directory);
        using (var store = new ParcelRepository(directory, seed.ApplicationId))
            if (scope == "Frozen")
            {
                // Valid pre-send unresolved state, with the exact candidate hash. No
                // external attempt or raw receipt is invented for this synthetic parcel.
                var operation = new ParcelOperation(Wire.Id(), "inventory-original", "001", Wire.Id(), DateTimeOffset.UtcNow,
                    Wire.Hash(Wire.Encode(candidate)), candidate, [], candidate.Weight, WeightRules.Configured(candidate.Weight),
                    ParcelState.Prepared, null, null, null, null);
                store.Commit(operation, "SyntheticOriginalPrepared", "Immutable offline consumer regression fixture; never sent");
            }
        ExtensionStore.AtomicWrite(Path.Combine(directory, "configuration.json"), CurrentConfigurationFixture.Create<ParcelConfiguration>(1,
            scope == "Effective" ? candidate : seed, scope == "Pending" ? candidate : null, scope == "Pending" ? "PendingRestart" : "Effective", null));
    }
    static void InspectInventoryCase(int schema, string scope, string name, ParcelConfiguration seed, ParcelConfiguration candidate, PackageReference[]? expected)
    {
        var directory = Path.Combine(root, "inventory-consumers", "schema" + schema, scope, name);
        WriteInventoryConsumer(directory, seed, candidate, scope);
        var database = Path.Combine(directory, seed.ApplicationId + ".db"); var configuration = Path.Combine(directory, "configuration.json");
        var dbBytes = File.ReadAllBytes(database); var configBytes = File.ReadAllBytes(configuration);
        PackageReference[]? references = null; var rejected = false;
        try { references = ExtensionConsumerReaders.Inspect(database); }
        catch (InvalidDataException) { rejected = true; }
        var correct = expected is null ? rejected : !rejected && references!.ToHashSet().SetEquals(expected);
        Check(correct && File.ReadAllBytes(database).SequenceEqual(dbBytes) && File.ReadAllBytes(configuration).SequenceEqual(configBytes),
            "R03 schema" + schema + "/" + scope + "/" + name + (expected is null ? " rejects ambiguity" : " retains exact known references") + " with original DB/config bytes unchanged");
    }
    static async Task LegacyInventoryUninstallChecks(string packageFile, string trustFile, ExtensionTrust trust)
    {
        foreach (var schema in new[] { 1, 2 }) foreach (var scope in inventoryScopes)
        {
            var path = Path.Combine(root, "inventory-uninstall-" + schema + "-" + scope); Directory.CreateDirectory(path);
            var store = new ExtensionStore(Path.Combine(path, "store"), trust, RunMode.Simulation, Path.Combine(path, "leases"));
            var seed = Config("direct-plc"); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), seed);
            ExtensionStore.AtomicWrite(Path.Combine(path, "extension-store.json"), new ParcelExtensionStoreConfiguration(store.Root, trustFile));
            var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null);
            await using var app = DirectPlcApplication.Create(launch, new ParcelCapabilityFactory(launch)); await app.StartAsync(default); await Maintenance(app);
            Check((await app.InstallCapabilityAsync(packageFile, Wire.FileHash(packageFile))).Status == "Installed", "R03 exact signed package installed for isolated legacy consumer guard");
            var package = store.Inventory().Single().Package; var legacy = InventorySeed(schema);
            var hidden = legacy.Plc! with { Package = package, Module = "parcel-plc", Version = package.Version, Implementation = "extension:" + package.Id + "/parcel-plc" };
            var ambiguous = legacy with { CapabilityInstances = [legacy.Plc!, hidden] };
            var consumer = Path.Combine(path, "offline-original"); WriteInventoryConsumer(consumer, seed, ambiguous, scope);
            var database = Path.Combine(consumer, seed.ApplicationId + ".db"); var configuration = Path.Combine(consumer, "configuration.json");
            store.RegisterConsumer(database); var dbBytes = File.ReadAllBytes(database); var configBytes = File.ReadAllBytes(configuration);
            var result = await app.ManageCapabilityAsync("extension-uninstall", Wire.Encode(package));
            Check(result.Status == "Rejected" && store.Inventory().Single().Status == "Installed" &&
                File.ReadAllBytes(database).SequenceEqual(dbBytes) && File.ReadAllBytes(configuration).SequenceEqual(configBytes),
                "R03 schema" + schema + "/" + scope + " real Application uninstall preserves exact installed package and ambiguous original bytes");
            // Deliberately retain the malformed consumer, registry and installed package.
            // A later test must never clear this responsibility to obtain Uninstalled.
        }
    }
}
