using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

// This is the custody format, independent of the host OS. Never normalize names
// supplied by a manifest: doing so can alias distinct originals or hide traversal.
internal static class RecoveryFiles
{
    public const int MaximumFiles = 100000;
    public const int MaximumManifestBytes = 32 * 1024 * 1024;
    // Custody destinations must be ordinary paths. Inspect ancestors even when
    // the leaf does not exist: File/Directory.Exists hide dangling links.
    public static void RequireOrdinaryAncestors(string path)
    {
        var full = Path.GetFullPath(path); var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked/reparse backup path or ancestor refused: " + current);
        }
    }
    public static string FromNative(string root, string file) => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
    public static string Resolve(string root, string name)
    {
        if (string.IsNullOrEmpty(name) || name.Split('/').Any(InvalidComponent) ||
            !ProductValidation.IsSafeRelativeFile(root, name, out var path)) throw new InvalidDataException("Nonportable or unsafe recovery custody path: " + name);
        return path;
    }
    static bool InvalidComponent(string component)
    {
        if (component.Length is 0 or > 255 || component.EndsWith('.') || component.EndsWith(' ') ||
            component.Any(c => c < 32 || "<>:\"\\|?*".Contains(c))) return true;
        var stem = component.Split('.')[0].TrimEnd(' ');
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3]);
    }
    public static void ValidateNames(string root, IEnumerable<string> names)
    {
        Dictionary<string, (string Original, bool Directory)> identities = new(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (var name in names)
        {
            if (++count > MaximumFiles) throw new InvalidDataException("Recovery file inventory quota exceeded");
            _ = Resolve(root, name);
            if (name is not ("configuration.json" or "overload.json") && !name.StartsWith("receipts/", StringComparison.Ordinal) && !name.StartsWith("images/", StringComparison.Ordinal))
                throw new InvalidDataException("Unexpected recovery custody file: " + name);
            var parts = name.Split('/'); var prefix = "";
            for (int i = 0; i < parts.Length; i++)
            {
                prefix = i == 0 ? parts[i] : prefix + "/" + parts[i]; var directory = i < parts.Length - 1;
                if (identities.TryGetValue(prefix, out var prior))
                {
                    if (prior.Original != prefix || !prior.Directory || !directory) throw new InvalidDataException("Aliased or duplicate recovery path: " + name);
                }
                else identities.Add(prefix, (prefix, directory));
            }
        }
    }
    // Walk one level at a time so a linked directory is rejected before recursion.
    public static IEnumerable<string> Enumerate(string root, string? subtree = null, bool skipImageLeases = false)
    {
        var start = subtree is null ? root : Resolve(root, subtree);
        if (!Directory.Exists(start)) yield break;
        Stack<string> pending = new(); pending.Push(start); int count = 0;
        while (pending.TryPop(out var directory))
        {
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Linked recovery directory");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++count > MaximumFiles * 2) throw new InvalidDataException("Recovery inventory quota exceeded");
                var name = FromNative(root, entry); _ = Resolve(root, name);
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.Directory)) { pending.Push(entry); continue; }
                if (skipImageLeases && name.StartsWith("images/", StringComparison.Ordinal) && Path.GetExtension(name) == ".lease" && ExtensionSchema.IsHash(Path.GetFileNameWithoutExtension(name))) continue;
                yield return name;
            }
        }
    }
    public static void CopyVerified(string source, string destination, string expectedHash)
    {
        // New file only. Flush original custody before a completion marker can be durable.
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { input.CopyTo(output); output.Flush(flushToDisk: true); }
        if (Wire.FileHash(destination) != expectedHash) throw new InvalidDataException("Recovery copy differs from verified custody; incomplete set retained");
    }
    public static Dictionary<string, string> Inventory(string root)
    {
        var names = Enumerate(root).Where(x => x != "COMPLETED.json").ToArray();
        ValidateNames(root, names);
        return names.ToDictionary(x => x, x => Wire.FileHash(Resolve(root, x)), StringComparer.Ordinal);
    }
}
