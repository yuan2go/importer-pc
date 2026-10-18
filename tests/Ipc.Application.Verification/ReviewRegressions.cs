using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Ipc.Application.Contracts;
using Ipc.Platform;
using Ipc.Reference.Protocol;

static class ReviewRegressions
{
    public static void Run(string root)
    {
        List<string> failures = [];
        void Case(string name, Action run)
        { try { run(); Console.WriteLine("PASS review: " + name); } catch (Exception e) { failures.Add(name + ": " + e.Message); Console.WriteLine("FAIL review: " + failures[^1]); } }
        Case("signed qualification evidence casing", () => QualificationVerification.Run(root));
        Case("signed package protocol evidence", () => PackageEvidence(root));
        if (failures.Count != 0) throw new Exception(string.Join("; ", failures));
    }
    static void PackageEvidence(string root)
    {
        ExtensionPublicContracts.Register(typeof(IParcelApplication).Assembly);
        using var rsa = RSA.Create(3072);
        // Ephemeral synthetic trust tests static inspection only; never installed or activated.
        var trust = new ExtensionTrust(1, [new("synthetic-review", rsa.ExportSubjectPublicKeyInfoPem(), false)]);
        var assembly = typeof(ReferencePeer).Assembly;
        var bytes = File.ReadAllBytes(assembly.Location); var name = Path.GetFileName(assembly.Location);
        var module = new ExtensionModule("source", name, typeof(ReferencePeer).FullName!, "observation", 1, []);
        foreach (var mode in new[] { RunMode.Production, RunMode.Simulation })
        foreach (var evidence in new[] { "A".PadRight(64, 'A'), "a".PadRight(64, 'a'), string.Concat(Enumerable.Repeat("aB", 32)), "not-a-hash", new string('A', 63), new string('A', 65), new string('g', 64) })
        {
            var manifest = new ExtensionManifest(1, "synthetic.review", "1.0.0", "synthetic-review", "net10.0", "any", 1, mode, new('a', 40), [],
                [new(name, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)))], [module with { Assurance = new(1, false, evidence) }], [], Wire.FileHash(typeof(ExtensionStore).Assembly.Location), new() { ["Ipc.Application.Contracts"] = Wire.FileHash(typeof(IParcelApplication).Assembly.Location) });
            var json = Encoding.UTF8.GetBytes(Wire.Encode(manifest)); var path = Path.Combine(root, "synthetic-" + mode + "-" + Wire.Id() + ".ipcx");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                void Write(string file, byte[] content) { using var output = zip.CreateEntry(file).Open(); output.Write(content); }
                Write(name, bytes); Write("manifest.json", json); Write("manifest.sig", rsa.SignData(json, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
            }
            var valid = ExtensionSchema.IsHash(evidence);
            try { _ = ExtensionPackages.Inspect(path, trust, mode); if (!valid) throw new Exception("Non-hash real ProtocolEvidence accepted by signed package verifier"); }
            catch (InvalidDataException e) when (!valid && e.Message.Contains("Invalid extension inventory", StringComparison.Ordinal)) { }
            Console.WriteLine("PASS review package: " + mode + " real evidence length=" + evidence.Length + " valid=" + valid);
        }
    }
}
