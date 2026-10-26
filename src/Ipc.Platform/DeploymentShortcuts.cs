using System.Runtime.Versioning;

namespace Ipc.Platform;

public sealed record DeploymentShortcutOptions(bool StartMenu, bool Desktop);
public sealed record DeploymentShortcut(string Path, string Target, string Arguments, string WorkingDirectory);

public static class DeploymentShortcuts
{
    public static DeploymentShortcut[] Plan(DeploymentLocation location, string programs, string desktop)
    {
        var options = location.Shortcuts ?? new(true, false); // Historical installation defaults.
        var id = location.ApplicationId + " · " + location.SourceCommit[..8] + " · " + System.IO.Path.GetFileName(location.ReleaseRoot)[^8..];
        var target = System.IO.Path.Combine(location.ReleaseRoot, "desktop", "Ipc.Desktop.exe");
        var result = new List<DeploymentShortcut>();
        if (options.StartMenu)
            foreach (var entry in new[] { (Name: "运行", Args: ""), (Name: "启动修复", Args: "--maintenance"), (Name: "安装维护", Args: "--setup") })
                result.Add(new(System.IO.Path.Combine(programs, "菜鸟供包台", id + " · " + entry.Name + ".lnk"), target, entry.Args, location.ReleaseRoot));
        if (options.Desktop) result.Add(new(System.IO.Path.Combine(desktop, "菜鸟供包台.lnk"), target, "", location.ReleaseRoot));
        return result.ToArray();
    }

    [SupportedOSPlatform("windows")]
    public static void Create(DeploymentLocation location, string programs, string desktop, DeploymentLocation? previous = null)
    {
        var plan = Plan(location, programs, desktop);
        if (plan.Length == 0) return;
        foreach (var entry in plan)
        {
            DeploymentPaths.RequireOrdinaryAncestors(entry.Path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(entry.Path)!);
            var exists = File.Exists(entry.Path);
            using var link = new ShellLinkFile(exists ? entry.Path : null);
            if (exists && (!string.Equals(link.TargetPath, entry.Target, StringComparison.OrdinalIgnoreCase) || link.Arguments != entry.Arguments) &&
                !(entry.Arguments == "" && System.IO.Path.GetFileName(entry.Path) == "菜鸟供包台.lnk" && link.Arguments == "" &&
                  PreviousOwnsTarget(location, previous, link.TargetPath)))
                throw new IOException("保留目标不同的既有快捷方式：" + entry.Path);
            link.TargetPath = entry.Target; link.Arguments = entry.Arguments; link.WorkingDirectory = entry.WorkingDirectory;
            link.Description = "菜鸟供包台 · " + location.ApplicationId; link.SetIcon(entry.Target, 0); link.Save(entry.Path);
        }
    }

    [SupportedOSPlatform("windows")]
    static bool PreviousOwnsTarget(DeploymentLocation current, DeploymentLocation? previous, string target)
    {
        if (previous is null)
        {
            // Repair after a committed projection failure may recover only a protected
            // original deployment descriptor, never an arbitrary same-named executable.
            var root = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(target));
            if (root is null) return false;
            var descriptor = System.IO.Path.Combine(root, "deployment-location.json");
            if (!File.Exists(descriptor)) return false;
            _ = TrustedProgramRoots.RequireBoundary(root); MachineRuntimePolicy.RequireProtectedEvidence(descriptor);
            if (new FileInfo(descriptor).Length > 65536) throw new InvalidDataException("Shortcut ownership descriptor quota exceeded");
            previous = Wire.Decode<DeploymentLocation>(File.ReadAllText(descriptor));
            if (!string.Equals(previous.ReleaseRoot, root, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return previous.ApplicationId == current.ApplicationId && previous.RuntimeAccountSid == current.RuntimeAccountSid &&
            string.Equals(System.IO.Path.GetFullPath(previous.DataRoot), System.IO.Path.GetFullPath(current.DataRoot), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(target, System.IO.Path.Combine(previous.ReleaseRoot, "desktop", "Ipc.Desktop.exe"), StringComparison.OrdinalIgnoreCase);
    }

    [SupportedOSPlatform("windows")]
    public static void Remove(DeploymentLocation location, string programs, string desktop)
    {
        var files = Plan(location with { Shortcuts = new(true, true) }, programs, desktop).ToList();
        var legacyId = location.ApplicationId + " · " + location.SourceCommit[..8] + " · " + System.IO.Path.GetFileName(location.ReleaseRoot)[^8..];
        files.Add(new(System.IO.Path.Combine(desktop, "菜鸟供包台 · " + legacyId + ".lnk"),
            System.IO.Path.Combine(location.ReleaseRoot, "desktop", "Ipc.Desktop.exe"), "", location.ReleaseRoot));
        foreach (var name in new[] { "运行", "启动修复", "安装维护" })
        {
            var entry = files.Single(link => System.IO.Path.GetFileName(link.Path).EndsWith(" · " + name + ".lnk", StringComparison.Ordinal));
            files.Add(entry with { Path = System.IO.Path.Combine(programs, "Importer PC", System.IO.Path.GetFileName(entry.Path)) });
        }
        foreach (var entry in files)
        {
            DeploymentPaths.RequireOrdinaryAncestors(entry.Path);
            if (!File.Exists(entry.Path)) continue;
            bool owned;
            using (var link = new ShellLinkFile(entry.Path))
                owned = string.Equals(link.TargetPath, entry.Target, StringComparison.OrdinalIgnoreCase) && link.Arguments == entry.Arguments;
            // A user-retargeted link no longer belongs to this installation.
            if (owned) File.Delete(entry.Path);
        }
    }
}
