using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public enum RecoveryFailureOrigin { Source, Destination, Undetermined }
public sealed class RecoveryFileFailure(RecoveryFailureOrigin origin, string phase, Exception error, bool sourceBytesVerified = false)
    : IOException(origin switch
    {
        RecoveryFailureOrigin.Source => "源文件读取或校验失败；原件保留，按存储保护处理。",
        RecoveryFailureOrigin.Destination => "目标文件写入或校验失败；残件保留，不能作为完整恢复集。",
        _ => "文件操作未完成，故障归属尚未核实；原件和残件保留。"
    }, error)
{
    public RecoveryFailureOrigin Origin { get; } = origin;
    public string Phase { get; } = phase;
    public bool SourceBytesVerified { get; } = sourceBytesVerified;
    public override string ToString() => base.ToString() + Environment.NewLine +
        $"RecoveryPhase={Phase}; Origin={Origin}; SourceBytesVerified={SourceBytesVerified}";
}

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
    // Frozen deletion evidence remains readable after its physical roots vanish.
    // Execution separately checks every existing ancestor and the deletion handle.
    internal static string ResolveRecorded(string root, string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 1024 || name.Split('/').Any(InvalidComponent))
            throw new InvalidDataException("Nonportable recorded recovery path: " + name);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(fullRoot, name.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Recorded recovery path leaves its original range");
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
    public static void ValidateNames(string root, IEnumerable<string> names) => ValidateNames(root, names, CancellationToken.None);
    public static void ValidateNames(string root, IEnumerable<string> names, CancellationToken token, bool recordedOnly = false)
    {
        token.ThrowIfCancellationRequested();
        Dictionary<string, (string Original, bool Directory)> identities = new(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (var name in names)
        {
            token.ThrowIfCancellationRequested();
            if (++count > MaximumFiles) throw new InvalidDataException("Recovery file inventory quota exceeded");
            _ = recordedOnly ? ResolveRecorded(root, name) : Resolve(root, name);
            if (name is not ("configuration.json" or "overload.json" or ParcelMachineOrigin.FileName) && !name.StartsWith("receipts/", StringComparison.Ordinal) && !name.StartsWith("images/", StringComparison.Ordinal) && !IsMaintenanceEvidence(name))
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
    public static IEnumerable<string> Enumerate(string root, string? subtree = null, bool skipImageLeases = false) => Enumerate(root, subtree, skipImageLeases, CancellationToken.None);
    public static IEnumerable<string> Enumerate(string root, string? subtree, bool skipImageLeases, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = subtree is null ? root : Resolve(root, subtree);
        if (!Directory.Exists(start)) yield break;
        Stack<string> pending = new(); pending.Push(start); int count = 0;
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Linked recovery directory");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                if (++count > MaximumFiles * 2) throw new InvalidDataException("Recovery inventory quota exceeded");
                var name = FromNative(root, entry); _ = Resolve(root, name);
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.Directory)) { pending.Push(entry); continue; }
                if (skipImageLeases && name.StartsWith("images/", StringComparison.Ordinal) && Path.GetExtension(name) == ".lease" && ExtensionSchema.IsHash(Path.GetFileNameWithoutExtension(name))) continue;
                yield return name;
            }
        }
    }
    public static void CopyVerified(string source, string destination, string expectedHash, CancellationToken cancellationToken = default)
        => CopyVerified(source, destination, expectedHash, cancellationToken, null);
    public static void CopyVerified(string source, string destination, string expectedHash, CancellationToken cancellationToken, Action<long>? copied)
    {
        var origin = RecoveryFailureOrigin.Source; var phase = "SourceOpen"; var verified = false;
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                origin = RecoveryFailureOrigin.Destination; phase = "DestinationOpen";
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var digest = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256))
                {
                    var buffer = new byte[65536];
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested(); origin = RecoveryFailureOrigin.Source; phase = "SourceRead";
                        var count = input.Read(buffer); if (count == 0) break;
                        digest.AppendData(buffer, 0, count);
                        origin = RecoveryFailureOrigin.Destination; phase = "DestinationWrite"; output.Write(buffer, 0, count);
                        origin = RecoveryFailureOrigin.Undetermined; phase = "CopyProgress"; copied?.Invoke(count);
                    }
                    phase = "SourceChecksum";
                    if (Convert.ToHexString(digest.GetHashAndReset()) != expectedHash)
                        throw new InvalidDataException("Original source bytes differ from verified custody");
                    verified = true;
                    cancellationToken.ThrowIfCancellationRequested(); origin = RecoveryFailureOrigin.Destination; phase = "DestinationFlush";
                    output.Flush(flushToDisk: true); phase = "DestinationClose";
                }
                origin = RecoveryFailureOrigin.Source; phase = "SourceClose";
            }
            origin = RecoveryFailureOrigin.Undetermined; phase = "CopyClosed"; CopyClosedForVerification?.Invoke(source, destination);
            origin = RecoveryFailureOrigin.Destination; phase = "DestinationChecksum";
            if (Wire.FileHash(destination, cancellationToken) != expectedHash)
                throw new InvalidDataException("Destination bytes differ from the verified source stream");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { throw new RecoveryFileFailure(origin, phase, error, verified); }
    }
    internal static Action<string, string>? CopyClosedForVerification { get; set; }
    static bool IsMaintenanceEvidence(string name)
    {
        var p = name.Split('/');
        return p.Length >= 3 && p[0] == "startup-maintenance" && Guid.TryParseExact(p[1], "N", out _) ||
            p.Length == 2 && p[0] == "startup-maintenance" && (p[1] == "editor-draft.bin" || p[1].StartsWith("editor-draft.bin.", StringComparison.Ordinal) && p[1].EndsWith(".new", StringComparison.Ordinal)) ||
            p.Length == 3 && p[0] == "startup-maintenance" && p[1] == "editor-history" && (p[2].EndsWith(".bin", StringComparison.Ordinal) || p[2].EndsWith(".new", StringComparison.Ordinal));
    }
    public static IEnumerable<string> MaintenanceEvidence(string root) => MaintenanceEvidence(root, CancellationToken.None);
    public static IEnumerable<string> MaintenanceEvidence(string root, CancellationToken token)
    {
        foreach (var name in Enumerate(root, "startup-maintenance", false, token))
        {
            if (Path.GetExtension(name) == ".lease" && ExtensionSchema.IsHash(Path.GetFileNameWithoutExtension(name))) continue;
            if (!IsMaintenanceEvidence(name)) throw new InvalidDataException("Unrecognized private maintenance evidence; preserve and inspect: " + name);
            yield return name;
        }
    }
    public static Dictionary<string, string> Inventory(string root, CancellationToken cancellationToken = default)
    {
        var names = Enumerate(root, null, false, cancellationToken).Where(x => x != "COMPLETED.json").ToArray();
        ValidateNames(root, names, cancellationToken);
        return names.ToDictionary(x => x, x => Wire.FileHash(Resolve(root, x), cancellationToken), StringComparer.Ordinal);
    }
}
