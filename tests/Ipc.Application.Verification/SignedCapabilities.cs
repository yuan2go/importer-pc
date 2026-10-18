using Ipc.Application.Contracts;
using Ipc.Application.WcsToPlc;
using Ipc.Application.ParcelFlow;
using Ipc.Assembly;
using Ipc.Platform;

static class SignedCapabilities
{
    public static async Task Run(string workspace, string root, string packageFile, string trustFile)
    {
        void Pass(bool condition, string reason) { if (!condition) throw new Exception("FAIL AC19 signed: " + reason); Console.WriteLine("PASS AC19 signed: " + reason); }
        ParcelCapabilityFactory.RegisterContracts();
        var store = new ExtensionStore(Path.Combine(root, "store"), Wire.Decode<ExtensionTrust>(File.ReadAllText(trustFile)), RunMode.Simulation, Path.Combine(root, "leases"));
        var package = store.Install(packageFile, Wire.FileHash(packageFile));
        var verified = store.VerifyInstalled(package.Package);
        Pass(new[] { "login", "camera", "upload", "weight", "plc", "integration" }.All(k => verified.Manifest.Modules.Any(x => x.Kind == k)), "signature and exact contract policy admits all six registered provider kinds");
        var directory = Path.Combine(root, "signed-config"); Directory.CreateDirectory(directory);
        ExtensionStore.AtomicWrite(Path.Combine(directory, "extension-store.json"), new ParcelExtensionStoreConfiguration(store.Root, trustFile));
        var launch = new BusinessLaunch(directory, Path.Combine(root, "signed-data"), null, null);
        CapabilityBinding B(string kind, string? module = null)
        {
            var m = verified.Manifest.Modules.Single(x => x.Id == (module ?? "parcel-" + kind));
            Dictionary<string, string> parameters = kind == "login" ? new() { ["subjects"] = "alice,bob" } : kind is "plc" or "integration" or "upload" ? new() { ["peer-root"] = Path.Combine(root, "external-peers") } : [];
            if (kind == "plc") parameters["complete-on-query"] = "true";
            return new(kind, "extension:" + package.Package.Id + "/" + m.Id, package.Package.Version, kind + ".signed", "reference://signed/" + kind, "PARCEL-REFERENCE-v1", parameters, package.Package, m.Id);
        }
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/wcs-to-plc/business.json"))) with
        {
            Login = B("login"), Scale = B("weight"), Wcs = B("integration"), Plc = B("plc"), Weight = new(WeightSource.Measured, WeightUnit.Kg, 3), Images = new(B("camera"), B("upload")), Extensions = new([package.Package], [])
        };
        var factory = new ParcelCapabilityFactory(launch);
        factory.Validate(c);
        var strict = c with { Wcs = B("integration", "strict-wcs") };
        foreach (var bad in new[] { strict with { Weight = strict.Weight with { Source = WeightSource.Disabled }, Scale = null }, strict with { Images = strict.Images! with { Strategy = UploadStrategy.Attached } } })
        {
            try { factory.Validate(bad); throw new Exception("Conflicting requirement was accepted"); }
            catch (InvalidDataException) { Pass(true, "signed protocol measured-weight/image-address requirements reject incompatible configuration"); }
        }
        try { factory.Validate(c with { Login = c.Login! with { Parameters = new() { ["subjects"] = "alice", ["unknown-field"] = "value" } } }); throw new Exception("Unknown schema field accepted"); }
        catch (InvalidDataException) { Pass(true, "provider-owned parameter schema rejects unknown protocol fields"); }
        var catalog = factory.Catalog(); Pass(catalog.Providers.Count(x => x.Package == package.Package) == 7 && catalog.InstalledPackages.Length == 1, "installed providers discoverable before any enable or binding");
        ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), strict);
        await using (var app = WcsToPlcApplication.Create(launch, factory))
        {
            await app.StartAsync(CancellationToken.None); Pass((await app.LoginAsync("alice", "SIMULATION_ONLY")).Status == "Authenticated", "signed external login factory creates the common authenticator");
            app.EmitReference("signed-parcel", "000000019", "signed-source-event"); ParcelSnapshot s = await app.SnapshotAsync();
            for (int i = 0; i < 150 && s.Operations.FirstOrDefault()?.Capture?.State != StageState.Accepted; i++) { await Task.Delay(20); s = await app.SnapshotAsync(); }
            Pass(s.Operations.FirstOrDefault()?.Image is not null, "signed camera factory captures original acquisition through the installed module");
            app.EmitReferenceWeight(new(Wire.Id(), "signed-parcel", strict.Scale!.Target, 3.210m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow));
            for (int i = 0; i < 150 && s.Operations.First().State != ParcelState.PlcAccepted; i++) { await Task.Delay(20); s = await app.SnapshotAsync(); }
            var op = s.Operations.Single();
            Pass(op.State == ParcelState.PlcAccepted && op.Upload?.State == StageState.Accepted && op.Wcs?.State == StageState.Accepted, "installed/enabled/bound providers actually execute image upload, measured WCS and original PLC chain");
            await app.ReconcileAsync(op.Id);
            Pass((await app.SnapshotAsync()).Operations.Single().State == ParcelState.PhysicalCompleted, "signed declaration and original query prove only the reference peer physical fact");
        }
        Console.WriteLine("PASS AC19 SIGNED COMPLETE: one exact package, seven modules, parameter/requirement guards and real application activation");
    }
}
