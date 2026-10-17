namespace Ipc.Platform;

// Protected-store usage files are created by the installer, read-only to runtime.
// Multiple stations may share immutable package bytes. A writer requires zero readers.
// Unlike a device lease this never writes/truncates a runtime-writable pathname during elevation.
public sealed class PackageUseLease : IDisposable
{
    readonly List<FileStream> streams = [];
    public PackageUseLease(IEnumerable<string> paths, bool exclusive)
    {
        try
        {
            foreach (var path in paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Linked package usage file refused");
                streams.Add(new(path, FileMode.Open, FileAccess.Read, exclusive ? FileShare.None : FileShare.Read));
            }
        }
        catch (Exception acquisition)
        {
            try { Dispose(); } catch (Exception cleanup) { throw new AggregateException("Package usage acquisition/cleanup failed", acquisition, cleanup); }
            throw;
        }
    }
    public void Dispose()
    {
        List<Exception> failures = [];
        foreach (var stream in streams.ToArray())
            try { stream.Dispose(); streams.Remove(stream); } catch (Exception ex) { failures.Add(ex); }
        if (failures.Count > 0) throw new AggregateException("Package usage release failed; independent handles attempted", failures);
    }
}
