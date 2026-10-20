using System.Runtime.InteropServices;
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
        if (options.Desktop) result.Add(new(System.IO.Path.Combine(desktop, "菜鸟供包台 · " + id + ".lnk"), target, "", location.ReleaseRoot));
        return result.ToArray();
    }

    [SupportedOSPlatform("windows")]
    public static void Create(DeploymentLocation location, string programs, string desktop)
    {
        var plan = Plan(location, programs, desktop);
        if (plan.Length == 0) return;
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows shortcut service unavailable");
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            foreach (var entry in plan)
            {
                DeploymentPaths.RequireOrdinaryAncestors(entry.Path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(entry.Path)!);
                var exists = File.Exists(entry.Path);
                dynamic link = shell.CreateShortcut(entry.Path);
                try
                {
                    if (exists && (!string.Equals((string)link.TargetPath, entry.Target, StringComparison.OrdinalIgnoreCase) || (string)link.Arguments != entry.Arguments))
                        throw new IOException("保留目标不同的既有快捷方式：" + entry.Path);
                    link.TargetPath = entry.Target; link.Arguments = entry.Arguments; link.WorkingDirectory = entry.WorkingDirectory;
                    link.Description = "菜鸟供包台 · " + location.ApplicationId; link.IconLocation = entry.Target + ",0"; link.Save();
                }
                finally { Marshal.FinalReleaseComObject(link); }
            }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    [SupportedOSPlatform("windows")]
    public static void Remove(DeploymentLocation location, string programs, string desktop)
    {
        var files = Plan(location with { Shortcuts = new(true, true) }, programs, desktop).ToList();
        foreach (var name in new[] { "运行", "启动修复", "安装维护" })
        {
            var entry = files.Single(link => System.IO.Path.GetFileName(link.Path).EndsWith(" · " + name + ".lnk", StringComparison.Ordinal));
            files.Add(entry with { Path = System.IO.Path.Combine(programs, "Importer PC", System.IO.Path.GetFileName(entry.Path)) });
        }
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows shortcut service unavailable");
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            foreach (var entry in files)
            {
                DeploymentPaths.RequireOrdinaryAncestors(entry.Path);
                if (!File.Exists(entry.Path)) continue;
                dynamic link = shell.CreateShortcut(entry.Path);
                bool owned;
                try { owned = string.Equals((string)link.TargetPath, entry.Target, StringComparison.OrdinalIgnoreCase) && (string)link.Arguments == entry.Arguments; }
                finally { Marshal.FinalReleaseComObject(link); }
                // A user-retargeted link no longer belongs to this installation.
                if (owned) File.Delete(entry.Path);
            }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }
}
