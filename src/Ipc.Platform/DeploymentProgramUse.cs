using System.Runtime.Versioning;

namespace Ipc.Platform;

// Readers protect installed program files only, never the business/data lease.
// Maintenance holds the same file exclusively until its mutation has ended.
public sealed class DeploymentProgramUse : IDisposable
{
    readonly List<FileStream> files = [];
    [SupportedOSPlatform("windows")]
    public static DeploymentProgramUse Observe(string releaseRoot) => AcquireAt(MachineRuntimePolicy.ResolveLeaseRoot(null), [releaseRoot], true);
    [SupportedOSPlatform("windows")]
    public static DeploymentProgramUse Maintain(DeploymentLocation? location)
    {
        var roots = new List<string>();
        for (var remaining = 16; location is not null; location = location.PreviousDeployment)
        {
            if (--remaining < 0) throw new InvalidDataException("程序部署关系超出核对范围，原文件保留。");
            roots.Add(location.ReleaseRoot);
            if (location.MaintenanceRoot is { } cache) roots.Add(cache);
        }
        return MaintainRoots(roots);
    }
    [SupportedOSPlatform("windows")]
    public static DeploymentProgramUse MaintainRoots(IEnumerable<string> roots)
    {
        var paths = roots.ToArray();
        return paths.Length == 0 ? new() : AcquireAt(MachineRuntimePolicy.InstallerLeaseRoot(""), paths, false);
    }
    internal static DeploymentProgramUse AcquireAt(string leaseRoot, IEnumerable<string> roots, bool observer)
    {
        var usage = new DeploymentProgramUse();
        Directory.CreateDirectory(leaseRoot);
        try
        {
            foreach (var path in roots.Select(p => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal))
            {
                DeploymentPaths.RequireOrdinaryAncestors(path);
                // Keep the pathname after close: deleting a shared lock file can
                // create two identities while another process still holds it.
                usage.files.Add(new(Path.Combine(leaseRoot, Wire.Hash("program-use:" + path) + ".lease"), FileMode.OpenOrCreate,
                    FileAccess.Read, observer ? FileShare.Read : FileShare.None));
            }
            return usage;
        }
        catch (Exception error)
        {
            try { usage.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("程序使用权取得与部分释放均未完成。", error, cleanup); }
            if (error is IOException && (error.HResult & 0xffff) is 32 or 33)
                throw new IOException("程序文件仍由供包运维工具使用，或正在进行安装维护。请关闭相关窗口后重试；程序和数据保留。", error) { HResult = error.HResult };
            throw;
        }
    }
    public void Dispose()
    {
        var errors = new List<Exception>();
        foreach (var file in files.ToArray())
            try { file.Dispose(); files.Remove(file); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("程序使用权释放未完成。", errors);
    }
}
