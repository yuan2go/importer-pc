using System.Security.Cryptography;
using System.Text;
namespace Ipc.Platform;

// An independently signed, expiring deployment decision. Package signatures prove origin;
// this decision binds the actual selected configuration and installed prerequisite bytes.
public sealed record QualificationFile(string Path, string Sha256);
public sealed record PrerequisiteQualification(PackageReference Package, ExtensionPrerequisite Declaration, QualificationFile[] Files);
public sealed record DeploymentQualification(int SchemaVersion, RunMode Mode, string ConfigurationHash,
    string HostContractSha256, string ApplicationContractSha256, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt,
    QualificationFile[] ApprovalEvidence, PrerequisiteQualification[] Prerequisites);
public sealed record SignedDeploymentQualification(string Publisher, string Document, string Signature);
public sealed record ProviderAssurance(int SchemaVersion, bool Simulation, string ProtocolEvidence);

public static class DeploymentQualifications
{
    public static DeploymentQualification Read(string file, ExtensionTrust trust, RunMode mode, string configurationHash, string applicationContractHash)
    {
        RequireFile(file);
        if (new FileInfo(file).Length > ExtensionPackages.MaximumManifestBytes) throw new InvalidDataException("Deployment qualification quota exceeded");
        var signed = Wire.Decode<SignedDeploymentQualification>(File.ReadAllText(file));
        var publisher = trust.Publishers.SingleOrDefault(x => x.Id == signed.Publisher) ?? throw new InvalidDataException("Untrusted qualification signer");
        if (mode == RunMode.Production && publisher.TestOnly) throw new InvalidDataException("Test qualification signer is forbidden in Production");
        using var rsa = RSA.Create(); rsa.ImportFromPem(publisher.PublicKeyPem);
        if (rsa.KeySize < 3072 || !rsa.VerifyData(Encoding.UTF8.GetBytes(signed.Document), Convert.FromBase64String(signed.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new InvalidDataException("Invalid independent deployment qualification signature");
        var q = Wire.Decode<DeploymentQualification>(signed.Document);
        if (q.SchemaVersion != 1 || q.Mode != mode || q.ConfigurationHash != configurationHash || q.HostContractSha256 != Wire.FileHash(typeof(ExtensionStore).Assembly.Location) ||
            q.ApplicationContractSha256 != applicationContractHash || q.IssuedAt > DateTimeOffset.UtcNow || q.ExpiresAt <= DateTimeOffset.UtcNow || q.ExpiresAt <= q.IssuedAt ||
            q.ApprovalEvidence is not { Length: > 0 and <= 64 } || q.Prerequisites is null || q.Prerequisites.Length > 64)
            throw new InvalidDataException("Missing/expired/mismatched deployment qualification for exact configuration and contracts");
        foreach (var evidence in q.ApprovalEvidence) VerifyFile(evidence);
        foreach (var prerequisite in q.Prerequisites)
        {
            if (prerequisite.Files is not { Length: > 0 and <= 64 }) throw new InvalidDataException("Prerequisite must bind actual installed files");
            foreach (var evidence in prerequisite.Files) VerifyFile(evidence);
        }
        return q;
    }
    public static void RequirePrerequisites(DeploymentQualification? q, VerifiedExtension package)
    {
        foreach (var declaration in package.Manifest.Prerequisites)
            if (q?.Prerequisites.Any(x => x.Package == package.Reference && x.Declaration == declaration && x.Files.Length > 0) != true)
                throw new InvalidDataException("SDK/system-driver prerequisite lacks exact installed evidence: " + package.Manifest.Id + "/" + declaration.Identity);
    }
    public static string FileHash(string path)
    { using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); return Convert.ToHexString(SHA256.HashData(input)); }
    static void VerifyFile(QualificationFile file)
    {
        if (!ExtensionSchema.IsHash(file.Sha256)) throw new InvalidDataException("Invalid qualification evidence hash");
        RequireFile(file.Path);
        if (!string.Equals(FileHash(file.Path), file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Qualification evidence changed: " + System.IO.Path.GetFileName(file.Path));
    }
    static void RequireFile(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new InvalidDataException("Qualification evidence must be an existing absolute file");
        for (FileSystemInfo? current = new FileInfo(path); current is not null; current = current is FileInfo f ? f.Directory : ((DirectoryInfo)current).Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked qualification evidence/ancestor refused");
        MachineRuntimePolicy.RequireProtectedEvidence(path);
    }
}
