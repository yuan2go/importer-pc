namespace Ipc.Platform;

public static class DeploymentPaths
{
    public static void RequireOrdinaryAncestors(string path)
    {
        var full = Path.GetFullPath(path); var root = Path.GetPathRoot(full)!; var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked deployment path refused: " + current);
        }
    }
}
