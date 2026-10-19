namespace Ipc.Platform;

public sealed record DeploymentLocation(string ApplicationId, string SourceCommit, string ReleaseRoot,
    string ConfigurationDirectory, string DataRoot, string RuntimeAccountSid);
public sealed record DeploymentMedia(string Root, string ApplicationId, string SourceCommit, Dictionary<string, string> Files)
{
    // A byte inventory is integrity evidence, not publisher trust. The maintainer selects trusted media.
    public static DeploymentMedia Inspect(string root, string application)
    {
        root = Path.GetFullPath(root); DeploymentPaths.RequireOrdinaryAncestors(root);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? source = null;
        foreach (var area in new[] { "app", "desktop" })
        {
            var directory = Path.Combine(root, area);
            var registration = Path.Combine(directory, "application-release.json");
            DeploymentPaths.RequireOrdinaryAncestors(registration);
            if (new FileInfo(registration).Length > 4 * 1024 * 1024) throw new InvalidDataException("Release registration quota exceeded");
            var release = Wire.Decode<ApplicationRelease>(File.ReadAllText(registration));
            if (release.SchemaVersion != 2 || release.ApplicationId != application || release.SourceCommit.Length != 40 ||
                !release.SourceCommit.All(char.IsAsciiHexDigit) || release.Files.Count is < 1 or > 4096 ||
                source is not null && source != release.SourceCommit) throw new InvalidDataException("Media application/source differs");
            source = release.SourceCommit;
            foreach (var entry in release.Files.Append(new("application-release.json", Wire.FileHash(registration))))
            {
                if (!ExtensionPackages.SafePath(entry.Key) || !ExtensionSchema.IsHash(entry.Value)) throw new InvalidDataException("Unsafe media entry");
                var path = Path.Combine(directory, entry.Key); DeploymentPaths.RequireOrdinaryAncestors(path);
                if (new FileInfo(path).Length > 256L * 1024 * 1024 || Wire.FileHash(path) != entry.Value) throw new InvalidDataException("Media integrity failed: " + entry.Key);
                files.Add(area + "/" + entry.Key, entry.Value);
            }
            // Reject extra files (including DLL search-path injection), rather than copying them.
            foreach (var path in Enumerate(directory))
            {
                DeploymentPaths.RequireOrdinaryAncestors(path);
                if (File.Exists(path) && !files.ContainsKey(area + "/" + Path.GetRelativePath(directory, path).Replace('\\', '/')))
                    throw new InvalidDataException("Unregistered media file: " + path);
            }
        }
        foreach (var required in new[] { "app/Ipc.Assembly.exe", "desktop/Ipc.Desktop.exe", "app/config/business.json", "desktop/config/business.json" })
            if (!files.ContainsKey(required)) throw new InvalidDataException("Incomplete Windows media: " + required);
        // Shipped operating guides, launchers and optional offline bundles stay with the installed release.
        var inventory = Path.Combine(root, "SHA256SUMS");
        if (File.Exists(inventory))
        {
            DeploymentPaths.RequireOrdinaryAncestors(inventory);
            if (new FileInfo(inventory).Length > 4 * 1024 * 1024) throw new InvalidDataException("Media inventory quota exceeded");
            var entries = File.ReadAllLines(inventory); if (entries.Length > 16384) throw new InvalidDataException("Media file quota exceeded");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in entries)
            {
                if (line.Length < 67 || line[64..66] != "  " || !ExtensionSchema.IsHash(line[..64]) || !ExtensionPackages.SafePath(line[66..]) || !names.Add(line[66..]))
                    throw new InvalidDataException("Invalid or duplicate media inventory entry");
                var name = line[66..]; var path = Path.Combine(root, name); DeploymentPaths.RequireOrdinaryAncestors(path);
                var hash = line[..64].ToUpperInvariant();
                if (new FileInfo(path).Length > 256L * 1024 * 1024 || Wire.FileHash(path) != hash) throw new InvalidDataException("Outer media integrity failed: " + name);
                if (files.TryGetValue(name, out var registered) && registered != hash) throw new InvalidDataException("Media inventories disagree");
                files[name] = hash;
            }
            if (files.Keys.Any(name => !names.Contains(name))) throw new InvalidDataException("Outer media inventory is incomplete");
            files.Add("SHA256SUMS", Wire.FileHash(inventory));
        }
        return new(root, application, source!, files);
    }
    static IEnumerable<string> Enumerate(string root)
    {
        var directories = new Stack<string>(); directories.Push(root); int count = 0;
        while (directories.TryPop(out var directory))
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++count > 16384) throw new InvalidDataException("Media directory quota exceeded");
                DeploymentPaths.RequireOrdinaryAncestors(entry); // Check before recursion, including dangling links.
                if (Directory.Exists(entry)) directories.Push(entry); else yield return entry;
            }
    }
    public void CopyTo(string destination, Action<string>? createRoot = null, Action<string>? protectFile = null)
    {
        DeploymentPaths.RequireOrdinaryAncestors(destination);
        if (Directory.Exists(destination)) throw new IOException("Side-by-side deployment destination already exists");
        if (createRoot is null) Directory.CreateDirectory(destination); else createRoot(destination);
        foreach (var entry in Files)
        {
            var source = Path.Combine(Root, entry.Key); DeploymentPaths.RequireOrdinaryAncestors(source);
            var target = Path.Combine(destination, entry.Key);
            if (createRoot is null) Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            else
            {
                var parent = destination;
                foreach (var part in entry.Key.Split('/')[..^1]) { parent = Path.Combine(parent, part); createRoot(parent); }
            }
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { input.CopyTo(output); output.Flush(true); }
            protectFile?.Invoke(target);
            if (Wire.FileHash(target) != entry.Value) throw new InvalidDataException("Media changed while copying: " + entry.Key);
        }
        _ = Inspect(destination, ApplicationId);
    }
    public static DeploymentLocation? ReadInstalled(string directory, string application)
    {
        var file = Path.GetFullPath(Path.Combine(directory, "..", "deployment-location.json"));
        if (!File.Exists(file)) return null;
        MachineRuntimePolicy.RequireProtectedEvidence(file);
        var location = Wire.Decode<DeploymentLocation>(File.ReadAllText(file));
        if (location.ApplicationId != application || !string.Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), Path.Combine(location.ReleaseRoot, "desktop"), StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), Path.Combine(location.ReleaseRoot, "app"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Deployment location differs from the selected executable");
        return location;
    }
}
