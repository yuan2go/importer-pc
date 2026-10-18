using Ipc.Application.Contracts;
using Ipc.Application.WcsToPlc;
using Ipc.Application.ParcelFlow;
using Ipc.Assembly;
using Ipc.Platform;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

static class SignedCapabilities
{
    public static async Task Run(string workspace, string root, string packageFile, string trustFile, bool qualifyPrerequisite = false)
    {
        void Pass(bool condition, string reason) { if (!condition) throw new Exception("FAIL AC19 signed: " + reason); Console.WriteLine("PASS AC19 signed: " + reason); }
        using var signer = qualifyPrerequisite ? RSA.Create(3072) : null;
        if (signer is not null)
        {
            // A new synthetic signed identity, never a rewrite of the installed package.
            var trust = Wire.Decode<ExtensionTrust>(File.ReadAllText(trustFile));
            trustFile = Path.Combine(root, "qualified-synthetic-trust.json");
            ExtensionStore.AtomicWrite(trustFile, trust with { Publishers = [.. trust.Publishers, new("qualification-synthetic", signer.ExportSubjectPublicKeyInfoPem(), true)] });
            Dictionary<string, byte[]> entries;
            using (var source = ZipFile.OpenRead(packageFile)) entries = source.Entries.ToDictionary(x => x.FullName, x => { using var stream = x.Open(); using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray(); });
            var original = Wire.Decode<ExtensionManifest>(Encoding.UTF8.GetString(entries["manifest.json"]));
            var manifest = original with { Id = original.Id + ".qualified", Publisher = "qualification-synthetic", Prerequisites = [new("vendor-sdk", "synthetic-prerequisite", "1.0.0", "SYNTHETIC verifier fixture only")] };
            entries["manifest.json"] = Encoding.UTF8.GetBytes(Wire.Encode(manifest));
            entries["manifest.sig"] = signer.SignData(entries["manifest.json"], HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            packageFile = Path.Combine(root, "qualified-synthetic.ipcx");
            using var output = ZipFile.Open(packageFile, ZipArchiveMode.Create);
            foreach (var entry in entries) { using var stream = output.CreateEntry(entry.Key).Open(); stream.Write(entry.Value); }
        }
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
        void Qualify(ParcelConfiguration selected)
        {
            if (signer is null) return;
            var evidence = Path.Combine(root, "synthetic-installed-prerequisite.txt"); File.WriteAllText(evidence, "SYNTHETIC installed prerequisite; no SDK or production approval");
            var file = new QualificationFile(evidence, Wire.FileHash(evidence).ToLowerInvariant());
            var hash = Wire.Hash(Wire.Encode(selected));
            var q = new DeploymentQualification(1, selected.Mode, hash, Wire.FileHash(typeof(ExtensionStore).Assembly.Location), Wire.FileHash(typeof(IParcelApplication).Assembly.Location), DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10), [file], [new(package.Package, verified.Manifest.Prerequisites.Single(), [file])]);
            var document = Wire.Encode(q);
            ExtensionStore.AtomicWrite(Path.Combine(root, "deployment-qualification." + hash + ".json"), new SignedDeploymentQualification("qualification-synthetic", document, Convert.ToBase64String(signer.SignData(Encoding.UTF8.GetBytes(document), HashAlgorithmName.SHA256, RSASignaturePadding.Pss))));
        }
        void Validate(ParcelConfiguration selected) { Qualify(selected); factory.Validate(selected); }
        Validate(c);
        Validate(c with { Wcs = c.Wcs! with { Target = "site-wcs://line/one" } });
        Pass(true, "signed component owns its target identity (no external call)");
        if (verified.Manifest.Modules.Single(x => x.Id == "parcel-integration").Parameters.Any(x => x.Key == "endpoint"))
        {
            Validate(c with { Wcs = c.Wcs! with { Parameters = new(c.Wcs.Parameters) { ["endpoint"] = "https://service.example.invalid/site/v7/resolve" } } });
            Pass(true, "actual signed component endpoint schema is validated without imposing reference loopback routing (no external call)");
        }
        var strict = c with { Wcs = B("integration", "strict-wcs") };
        foreach (var bad in new[] { strict with { Weight = strict.Weight with { Source = WeightSource.Disabled }, Scale = null }, strict with { Images = strict.Images! with { Strategy = UploadStrategy.Attached } } })
        {
            try { Validate(bad); throw new Exception("Conflicting requirement was accepted"); }
            catch (InvalidDataException) { Pass(true, "signed protocol measured-weight/image-address requirements reject incompatible configuration"); }
        }
        try { Validate(c with { Login = c.Login! with { Parameters = new() { ["subjects"] = "alice", ["unknown-field"] = "value" } } }); throw new Exception("Unknown schema field accepted"); }
        catch (InvalidDataException) { Pass(true, "provider-owned parameter schema rejects unknown protocol fields"); }
        var catalog = factory.Catalog(); Pass(catalog.Providers.Count(x => x.Package == package.Package) == 7 && catalog.InstalledPackages.Length == 1, "installed providers discoverable before any enable or binding");
        Qualify(strict);
        ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), strict);
        await using (var app = WcsToPlcApplication.Create(launch, factory))
        {
            await app.StartAsync(CancellationToken.None);
            if (signer is not null) Pass(true, "actual Create acquires lowercase independently signed approval/prerequisite evidence and activates original providers");
            Pass((await app.LoginAsync("alice", "SIMULATION_ONLY")).Status == "Authenticated", "signed external login factory creates the common authenticator");
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
