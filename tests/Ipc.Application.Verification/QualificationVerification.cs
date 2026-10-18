using System.Security.Cryptography;
using System.Text;
using Ipc.Application.Contracts;
using Ipc.Platform;

static class QualificationVerification
{
    public static void Run(string root)
    {
        if (OperatingSystem.IsMacOS() && root.StartsWith("/var/", StringComparison.Ordinal)) root = "/private" + root;
        void Check(bool ok, string detail) { if (!ok) throw new Exception(detail); Console.WriteLine("PASS AC20 qualification: " + detail); }
        void Reject(Action run, string detail) { try { run(); throw new Exception("Accepted: " + detail); } catch (InvalidDataException) { Check(true, detail); } }
        using var rsa = RSA.Create(3072);
        var trust = new ExtensionTrust(1, [new("qualification-test", rsa.ExportSubjectPublicKeyInfoPem(), false)]);
        var evidence = Path.Combine(root, "synthetic-approval-evidence.txt"); File.WriteAllText(evidence, "SYNTHETIC qualification verifier test; no actual production approval");
        var package = new PackageReference("test.provider", "1.0.0", new('A', 64)); var prerequisite = new ExtensionPrerequisite("vendor-sdk", "test-managed-bridge", "1.0.0", "synthetic-evidence");
        var file = new QualificationFile(evidence, Wire.FileHash(evidence));
        var q = new DeploymentQualification(1, RunMode.Production, new('B', 64), Wire.FileHash(typeof(ExtensionStore).Assembly.Location), Wire.FileHash(typeof(IParcelApplication).Assembly.Location), DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10), [file], [new(package, prerequisite, [file])]);
        var path = Path.Combine(root, "signed-qualification.json");
        void Write(DeploymentQualification value)
        { var document = Wire.Encode(value); ExtensionStore.AtomicWrite(path, new SignedDeploymentQualification("qualification-test", document, Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(document), HashAlgorithmName.SHA256, RSASignaturePadding.Pss)))); }
        DeploymentQualification Read() => DeploymentQualifications.Read(path, trust, RunMode.Production, q.ConfigurationHash, q.ApplicationContractSha256);
        Write(q); Check(Read().ConfigurationHash == q.ConfigurationHash, "independently signed exact evidence accepted by verifier (synthetic test only)");
        Reject(() => DeploymentQualifications.Read(path, trust with { Publishers = [trust.Publishers[0] with { TestOnly = true }] }, RunMode.Production, q.ConfigurationHash, q.ApplicationContractSha256), "test signer cannot qualify Production");
        Write(q with { ConfigurationHash = new('C', 64) }); Reject(() => Read(), "changed configuration cannot reuse qualification");
        Write(q with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) }); Reject(() => Read(), "expired qualification rejected");
        Write(q); File.AppendAllText(evidence, "changed"); Reject(() => Read(), "changed prerequisite/evidence bytes rejected");
        File.WriteAllText(evidence, "SYNTHETIC qualification verifier test; no actual production approval");
        var manifest = new ExtensionManifest(1, package.Id, package.Version, "qualification-test", "net10.0", "any", 1, RunMode.Production, new('a', 40), [], [], [], [prerequisite]);
        var verified = new VerifiedExtension(manifest, package.ManifestSha256, new('D', 64), new('E', 64), DateTimeOffset.UtcNow, false);
        DeploymentQualifications.RequirePrerequisites(q, verified); Check(true, "installed prerequisite proof binds exact package and declaration");
        Reject(() => DeploymentQualifications.RequirePrerequisites(q with { Prerequisites = [] }, verified), "missing SDK proof rejected without permanent blanket SDK denial");
    }
}
