namespace Ipc.Platform;

// Program-only removal never parses, opens or repairs a business database.
// The global fence prevents a new current-protocol runtime from creating a new
// station between discovery and retirement; existing station owners also close.
internal sealed class OpaqueRemovalOwnership : IDisposable
{
    IDisposable? root;
    readonly List<ResourceLease> stations = [];
    public string DataRoot { get; }
    public string Application { get; }
    public string[] Directories { get; private set; } = [];

    public OpaqueRemovalOwnership(string leaseRoot, string application, string dataRoot, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Application = application; DataRoot = Path.GetFullPath(dataRoot);
        root = ApplicationRootOwnership.AcquireAt(leaseRoot, application, DataRoot);
        try
        {
            Directories = Discover(application, DataRoot, token);
            foreach (var directory in Directories)
            {
                token.ThrowIfCancellationRequested();
                stations.Add(new ResourceLease(directory, ["application-owner"], createDirectory: false));
            }
            if (!Discover(application, DataRoot, token).SequenceEqual(Directories, StringComparer.OrdinalIgnoreCase))
                throw new IOException("原站点目录在停机核对时变化；全部数据保留，请重新检查。");
        }
        catch (Exception acquisition)
        {
            try { Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("停机围栏取得及释放均未完成，原数据保留。", acquisition, cleanup); }
            throw;
        }
    }

    internal static string[] Discover(string application, string dataRoot, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _ = ApplicationRootOwnership.Resource(application, dataRoot);
        DeploymentPaths.RequireOrdinaryAncestors(dataRoot);
        if (File.Exists(dataRoot)) throw new InvalidDataException("原数据根被文件占用，不能推断站点目录。");
        if (!Directory.Exists(dataRoot)) return [];
        var result = new List<string>(); var count = 0;
        foreach (var mode in Directory.EnumerateDirectories(dataRoot))
        {
            token.ThrowIfCancellationRequested(); DeploymentPaths.RequireOrdinaryAncestors(mode);
            if (++count > 1024) throw new InvalidDataException("原目录数量超过停机核对预算，全部保留。");
            foreach (var station in Directory.EnumerateDirectories(mode))
            {
                token.ThrowIfCancellationRequested(); DeploymentPaths.RequireOrdinaryAncestors(station);
                if (++count > 1024) throw new InvalidDataException("原目录数量超过停机核对预算，全部保留。");
                var area = Path.Combine(station, application); DeploymentPaths.RequireOrdinaryAncestors(area);
                if (File.Exists(area)) throw new InvalidDataException("原站点目录被文件占用，不能推断停机范围。");
                if (Directory.Exists(area)) result.Add(Path.GetFullPath(area));
            }
        }
        return result.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        foreach (var station in stations.AsEnumerable().Reverse().ToArray())
            try { station.Dispose(); stations.Remove(station); } catch (Exception error) { failures.Add(error); }
        // Retain the global fence if an original local owner did not release.
        if (failures.Count == 0) { root?.Dispose(); root = null; }
        else throw new AggregateException("原站点所有权尚未释放，全应用停机围栏保留。", failures);
    }
}
