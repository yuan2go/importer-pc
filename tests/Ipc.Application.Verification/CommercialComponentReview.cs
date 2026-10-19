using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Ipc.Assembly;
using Ipc.Platform;

static class CommercialComponentReview
{
    // Input is an independently built reference barcode package, never a customer or Production package.
    public static void Run(string root, string package, string trustFile)
    {
        Directory.CreateDirectory(root);
        var trust = WindowsDeploymentSetup.ReadTrust(trustFile, RunMode.Simulation);
        if (trust.Publishers.Any(p => !p.TestOnly)) throw new InvalidDataException("Use exclusively synthetic TestOnly trust for this mutating fixture");
        var request = new WindowsDeploymentRequest(root, "sportmaster", new string('a', 40), "SYNTHETIC-UNUSED", Path.Combine(root, "never-business-root"), "unused", RunMode.Simulation, trustFile, Wire.FileHash(trustFile), new() { [package] = Wire.FileHash(package) });
        void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
        void Reject(Action action, string message)
        {
            try { action(); }
            catch (Exception e) when (e is InvalidDataException or IOException or CryptographicException) { Console.WriteLine("PASS " + message); return; }
            throw new Exception("Expected rejection: " + message);
        }
        var before = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.FullName).ToHashSet();
        var result = DeploymentComponentReviewing.Inspect(request);
        Check(result.Length == 1 && result[0].Capabilities.Contains("observation:barcode-source"), "frozen static review accepts an independently built, signed reference module");
        Check(!Directory.Exists(request.DataRoot) && !AppDomain.CurrentDomain.GetAssemblies().Any(a => !before.Contains(a.FullName) && a.GetName().Name == "Ipc.Reference.BarcodeExtension"), "static review does not load provider code or create business data");
        using var signer = RSA.Create(3072);
        var variantTrust = Path.Combine(root, "variant-trust.json");
        ExtensionStore.AtomicWrite(variantTrust, trust with { Publishers = [.. trust.Publishers, new("comm.review-fixture", signer.ExportSubjectPublicKeyInfoPem(), true)] });
        string Variant(string name, Func<ExtensionManifest, ExtensionManifest> transform, bool tamper = false)
        {
            var target = Path.Combine(root, name + ".zip");
            using var input = ZipFile.OpenRead(package); using var output = ZipFile.Open(target, ZipArchiveMode.Create);
            using var reader = new StreamReader(input.GetEntry("manifest.json")!.Open());
            var manifest = transform(Wire.Decode<ExtensionManifest>(reader.ReadToEnd()) with { Publisher = "comm.review-fixture" });
            var json = Encoding.UTF8.GetBytes(Wire.Encode(manifest));
            foreach (var entry in input.Entries.Where(e => e.FullName is not ("manifest.json" or "manifest.sig")))
            {
                using var bytes = entry.Open(); using var buffer = new MemoryStream(); bytes.CopyTo(buffer); var payload = buffer.ToArray();
                if (tamper && entry.FullName.EndsWith(".dll")) payload[0] ^= 0x20;
                using var destination = output.CreateEntry(entry.FullName).Open(); destination.Write(payload);
            }
            using (var destination = output.CreateEntry("manifest.json").Open()) destination.Write(json);
            using (var destination = output.CreateEntry("manifest.sig").Open()) destination.Write(signer.SignData(json, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
            return target;
        }
        WindowsDeploymentRequest Select(params string[] paths) => request with { TrustFile = variantTrust, TrustSha256 = Wire.FileHash(variantTrust), Packages = paths.ToDictionary(p => p, Wire.FileHash) };
        var changed = Variant("tampered-payload", m => m, true);
        Reject(() => DeploymentComponentReviewing.Inspect(Select(changed)), "changed payload cannot pass signed static review");
        var missing = Variant("missing-dependency", m => m with { Dependencies = [new("comm.missing", "1.0.0", new string('A', 64))] });
        Reject(() => DeploymentComponentReviewing.Inspect(Select(missing)), "missing exact dependency cannot be accepted or substituted");
        var alternate = Variant("conflicting-version", m => m with { Version = "999.0.0" });
        Reject(() => DeploymentComponentReviewing.Inspect(Select(package, alternate)), "conflicting signed versions cannot form an ambiguous selection");
        Reject(() => WindowsDeploymentSetup.ReadTrust(trustFile, RunMode.Production), "TestOnly trust cannot qualify Production");
        Check(!Directory.Exists(request.DataRoot), "failed static reviews leave business data absent");
    }
}
