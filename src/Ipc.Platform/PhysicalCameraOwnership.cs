namespace Ipc.Platform;

// Keep the original serial lease in each installed namespace. Taking both also
// coordinates with an already-installed client that predates test instances.
public sealed class PhysicalCameraOwnership : IDisposable
{
    readonly List<ResourceLease> leases = [];
    public static IDisposable Acquire(string serial)
    {
        var roots = new List<string> { MachineRuntimePolicy.ResolveLeaseRoot(null) };
        if (OperatingSystem.IsWindows() && InstallationResourceBoundary.OtherInstallation() is not null)
        {
            var anchor = WindowsStoragePaths.ProtectedAnchorFor(!InstallationIdentity.IsTest);
            var file = Path.Combine(anchor, "Machine", "runtime-policy.json");
            MachineRuntimePolicy.RequireProtectedEvidenceAt(file, anchor);
            var policy = Wire.Decode<MachineRuntimePolicy>(File.ReadAllText(file));
            var expected = Path.Combine(anchor, "Machine", "leases");
            if (policy.SchemaVersion != 1 || !Path.GetFullPath(policy.LeaseRoot).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("另一实例的相机占用目录无法核验。");
            DeploymentPaths.RequireOrdinaryAncestors(expected);
            MachineRuntimePolicy.RequireProtectedAcl(new DirectoryInfo(expected), policy.RuntimeAccountSid);
            roots.Add(expected);
        }
        return Acquire(serial, roots);
    }
    internal static IDisposable Acquire(string serial, IEnumerable<string> roots)
    {
        var owner = new PhysicalCameraOwnership();
        try
        {
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
                owner.leases.Add(new ResourceLease(root, ["device:hikrobot-gige:" + serial]));
            return owner;
        }
        catch (Exception original)
        {
            try { owner.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("相机占用核验失败且部分所有权未释放。", original, cleanup); }
            throw;
        }
    }
    public void Dispose()
    {
        var errors = new List<Exception>();
        foreach (var lease in leases.ToArray())
            try { lease.Dispose(); leases.Remove(lease); } catch (Exception error) { errors.Add(error); }
        if (errors.Count > 0) throw new AggregateException("相机跨实例所有权未完全释放。", errors);
    }
}
