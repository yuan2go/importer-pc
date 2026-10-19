using System.IO.Compression;
using Ipc.Platform;

namespace Ipc.Assembly;

public sealed record DeploymentComponentReview(string Package, string Version, string Sha256, string[] Capabilities, string[] Dependencies, string[] Prerequisites, long ExpandedBytes);
public static class DeploymentComponentReviewing
{
    public static DeploymentComponentReview[] Inspect(WindowsDeploymentRequest request)
    {
        if (request.Packages.Count == 0) return [];
        if (request.Packages.Count > 128) throw new InvalidDataException("组件选择超出128包预算");
        ParcelCapabilityFactory.RegisterContracts();
        var trustPath = request.TrustFile;
        if (trustPath is null)
        { if (!OperatingSystem.IsWindows()) throw new InvalidDataException("缺少独立信任文件"); trustPath = MachineRuntimePolicy.ExtensionPaths(request.Mode).TrustFile; }
        var trust = WindowsDeploymentSetup.ReadTrust(trustPath, request.Mode);
        var candidates = new Dictionary<PackageReference, string>();
        foreach (var (path, hash) in request.Packages)
        {
            DeploymentPaths.RequireOrdinaryAncestors(path);
            if (new FileInfo(path).Length > ExtensionPackages.MaximumExpandedBytes || Wire.FileHash(path) != hash) throw new InvalidDataException("审阅的组件文件已变化");
            using var zip = ZipFile.OpenRead(path); var entry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("组件缺少清单");
            if (entry.Length > ExtensionPackages.MaximumManifestBytes) throw new InvalidDataException("组件清单超出预算");
            using var reader = new StreamReader(entry.Open()); var text = reader.ReadToEnd(); var manifest = Wire.Decode<ExtensionManifest>(text);
            using var bytes = entry.Open(); var identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            if (!candidates.TryAdd(new(manifest.Id, manifest.Version, identity), path)) throw new InvalidDataException("重复精确组件选择");
        }
        if (candidates.Keys.GroupBy(p => p.Id).Any(g => g.Count() > 1)) throw new InvalidDataException("同一组件ID选择了冲突版本/清单；按当前Application的精确闭包重新选择");
        var scratch = Path.Combine(Path.GetTempPath(), "ipc-component-review-" + Wire.Id()); Directory.CreateDirectory(scratch);
        long expanded = 0;
        Dictionary<PackageReference, (VerifiedExtension Package, string Directory)> verified = []; HashSet<PackageReference> visiting = [];
        ExtensionStore? installed = null;
        (VerifiedExtension Package, string Directory) Resolve(PackageReference reference)
        {
            if (verified.TryGetValue(reference, out var ready)) return ready;
            if (!visiting.Add(reference)) throw new InvalidDataException("组件依赖循环");
            try
            {
                if (!candidates.TryGetValue(reference, out var archive))
                {
                    if (!OperatingSystem.IsWindows()) throw new InvalidDataException("缺少选定的精确依赖：" + reference.Id);
                    var paths = MachineRuntimePolicy.ExtensionPaths(request.Mode);
                    installed ??= new(paths.Root, trust, request.Mode, scratch);
                    return (installed.VerifyInstalled(reference), installed.PackageDirectory(reference));
                }
                var package = ExtensionPackages.Inspect(archive, trust, request.Mode, Resolve);
                if (package.Reference != reference || request.Packages[archive] != package.PackageSha256) throw new InvalidDataException("组件来源身份变化");
                expanded = checked(expanded + package.Manifest.Files.Sum(f => f.Length));
                if (expanded > 2L * 1024 * 1024 * 1024 || new DriveInfo(Path.GetPathRoot(scratch)!).AvailableFreeSpace < package.Manifest.Files.Sum(f => f.Length) + 67108864)
                    throw new IOException("组件展开超过2GiB总预算或临时卷空间不足；未执行组件");
                var target = Path.Combine(scratch, package.ManifestSha256);
                ExtensionPackages.ExtractVerified(archive, target, trust, request.Mode, package.PackageSha256, Resolve);
                return verified[reference] = (package, target);
            }
            finally { visiting.Remove(reference); }
        }
        try
        {
            var selected = candidates.Keys.Select(Resolve).Select(x => x.Package).ToArray();
            if (selected.Sum(p => p.Manifest.Files.Sum(f => f.Length)) > 2L * 1024 * 1024 * 1024) throw new IOException("组件展开总量超出2GiB预算");
            return selected.Select(p => new DeploymentComponentReview(p.Manifest.Id, p.Manifest.Version, p.ManifestSha256,
                p.Manifest.Modules.Select(m => m.Kind + ":" + m.Id).ToArray(), p.Manifest.Dependencies.Select(d => d.Id + "@" + d.Version + "/" + d.ManifestSha256).ToArray(),
                p.Manifest.Prerequisites.Select(d => d.Kind + ":" + d.Identity + "@" + d.Version + " · 需独立安装与真实资格证明").ToArray(), p.Manifest.Files.Sum(f => f.Length))).ToArray();
        }
        finally { DeploymentPaths.RequireOrdinaryAncestors(scratch); Directory.Delete(scratch, true); }
    }
}
