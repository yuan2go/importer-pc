using System.Runtime.Versioning;
using System.Text.Json;

namespace Ipc.Platform;

public sealed record DeploymentShortcutOptions(bool StartMenu, bool Desktop);
public sealed record DeploymentShortcut(string Path, string Target, string Arguments, string WorkingDirectory);

public static class DeploymentShortcuts
{
    public static DeploymentShortcut[] Plan(DeploymentLocation location, string programs, string desktop)
    {
        var options = location.Shortcuts ?? throw new InvalidDataException("部署缺少明确的快捷方式选项；保留原登记并使用当前安装计划。");
        var business = location.ApplicationId switch { "sportmaster" => "SM供包业务", "direct-plc" => "设备直连业务", "wcs-to-plc" => "调度转发业务", _ => location.ApplicationId };
        var id = InstallationIdentity.DisplayName + " · " + business + " · " + location.ApplicationId + " · " + location.SourceCommit[..8] + " · " + System.IO.Path.GetFileName(location.ReleaseRoot)[^8..];
        var target = System.IO.Path.Combine(location.ReleaseRoot, "desktop", InstallationIdentity.DesktopExecutable);
        var result = new List<DeploymentShortcut>();
        if (options.StartMenu && location.ApplicationId == "sportmaster")
        {
            result.Add(new(System.IO.Path.Combine(programs, InstallationIdentity.DisplayName, InstallationIdentity.DisplayName + ".lnk"), target, "", location.ReleaseRoot));
            result.Add(new(System.IO.Path.Combine(programs, InstallationIdentity.DisplayName, InstallationIdentity.OperationsDisplayName + ".lnk"),
                target, InstallationIdentity.OperationsArgument, location.ReleaseRoot));
        }
        else if (options.StartMenu)
            foreach (var entry in new[] { (Name: "运行", Args: ""), (Name: "启动修复", Args: "--maintenance"), (Name: "安装维护", Args: "--setup") })
                result.Add(new(System.IO.Path.Combine(programs, InstallationIdentity.DisplayName, (location.ApplicationId == "sportmaster" ?
                    entry.Args == "" ? InstallationIdentity.DisplayName : InstallationIdentity.DisplayName + " · " + entry.Name : id + " · " + entry.Name) + ".lnk"), target, entry.Args, location.ReleaseRoot));
        if (options.Desktop) result.Add(new(System.IO.Path.Combine(desktop, InstallationIdentity.DisplayName + ".lnk"), target, "", location.ReleaseRoot));
        return result.ToArray();
    }

    [SupportedOSPlatform("windows")]
    public static void Create(DeploymentLocation location, string programs, string desktop, DeploymentLocation? previous = null)
    {
        previous ??= location.PreviousDeployment;
        var plan = Plan(location, programs, desktop);
        if (location.ApplicationId == "sportmaster" && location.InventoryFile is { } inventory)
        {
            // A retained deployment may predate the observer. Project its declared
            // entries, never borrow an EXE from another build or infer capability
            // from a missing file in an otherwise incomplete dual-program release.
            DeploymentPaths.RequireOrdinaryAncestors(inventory);
            if (new FileInfo(inventory).Length > 4 * 1024 * 1024 ||
                !string.Equals(Wire.FileHash(inventory), location.InventorySha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("安装库存无法核验，原快捷方式保留；请核对原安装记录。");
            using var declared = JsonDocument.Parse(File.ReadAllText(inventory));
            var files = declared.RootElement.GetProperty("Files");
            if (files.TryGetProperty("desktop/" + InstallationIdentity.LegacyOperationsExecutable, out _))
                plan = plan.Select(entry => entry.Arguments == InstallationIdentity.OperationsArgument ? LegacyTool(entry, location) : entry).ToArray();
            else if (!files.TryGetProperty("desktop/Ipc.Operations.Desktop.dll", out _))
                plan = plan.Where(entry => entry.Arguments != InstallationIdentity.OperationsArgument).ToArray();
        }
        foreach (var entry in plan)
        {
            DeploymentPaths.RequireOrdinaryAncestors(entry.Path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(entry.Path)!);
            var exists = ExistingShortcutFile(entry.Path);
            using var link = new ShellLinkFile(exists ? entry.Path : null);
            if (exists && (!string.Equals(link.TargetPath, entry.Target, StringComparison.OrdinalIgnoreCase) || link.Arguments != entry.Arguments) &&
                !((location.ApplicationId == "sportmaster" || entry.Arguments == "" && System.IO.Path.GetFileName(entry.Path) == InstallationIdentity.DisplayName + ".lnk") &&
                  PreviousOwnsTarget(location, previous, entry, link.TargetPath, link.Arguments)))
                throw new IOException("保留目标不同的既有快捷方式：" + entry.Path);
            link.TargetPath = entry.Target; link.Arguments = entry.Arguments; link.WorkingDirectory = entry.WorkingDirectory;
            link.Description = location.ApplicationId == "sportmaster" ? InstallationIdentity.DisplayName : InstallationIdentity.DisplayName + " · " + location.ApplicationId; link.SetIcon(entry.Target, 0); link.Save(entry.Path);
        }
        var candidates = OwnedCandidates(location, programs, desktop)
            .Concat(previous is null ? [] : OwnedCandidates(previous, programs, desktop));
        RemoveOwned(candidates.Where(c => !plan.Any(p => string.Equals(p.Path, c.Path, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Target, c.Target, StringComparison.OrdinalIgnoreCase) && p.Arguments == c.Arguments)));
    }

    [SupportedOSPlatform("windows")]
    static bool PreviousOwnsTarget(DeploymentLocation current, DeploymentLocation? previous, DeploymentShortcut entry, string target, string arguments)
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
            (string.Equals(target, Path.Combine(previous.ReleaseRoot, "desktop", InstallationIdentity.DesktopExecutable), StringComparison.OrdinalIgnoreCase) &&
                arguments == (Path.GetFileName(entry.Path) == InstallationIdentity.OperationsDisplayName + ".lnk" ? InstallationIdentity.OperationsArgument : entry.Arguments) ||
             Path.GetFileName(entry.Path) == InstallationIdentity.OperationsDisplayName + ".lnk" &&
                arguments == "" && string.Equals(target, Path.Combine(previous.ReleaseRoot, "desktop", InstallationIdentity.LegacyOperationsExecutable), StringComparison.OrdinalIgnoreCase));
    }

    [SupportedOSPlatform("windows")]
    public static void Remove(DeploymentLocation location, string programs, string desktop)
    {
        RemoveOwned(OwnedCandidates(location, programs, desktop));
    }

    static DeploymentShortcut LegacyTool(DeploymentShortcut entry, DeploymentLocation location) =>
        entry with { Target = Path.Combine(location.ReleaseRoot, "desktop", InstallationIdentity.LegacyOperationsExecutable), Arguments = "" };

    static IEnumerable<DeploymentShortcut> OwnedCandidates(DeploymentLocation location, string programs, string desktop)
    {
        foreach (var entry in Plan(location with { Shortcuts = new(true, true) }, programs, desktop).Concat(LegacySmMenu(location, programs)))
        {
            yield return entry;
            if (entry.Arguments == InstallationIdentity.OperationsArgument) yield return LegacyTool(entry, location);
        }
        if (location.ApplicationId == "sportmaster")
            foreach (var entry in new[] { (Name: "启动修复", Args: "--maintenance"), (Name: "安装维护", Args: "--setup") })
                yield return new(System.IO.Path.Combine(programs, InstallationIdentity.DisplayName, InstallationIdentity.DisplayName + " · " + entry.Name + ".lnk"),
                    System.IO.Path.Combine(location.ReleaseRoot, "desktop", InstallationIdentity.DesktopExecutable), entry.Args, location.ReleaseRoot);
    }

    // Exact names from the registered installation only. Never sweep menu folders
    // or remove a user-retargeted shortcut based on its display name.
    static IEnumerable<DeploymentShortcut> LegacySmMenu(DeploymentLocation location, string programs)
    {
        if (location.ApplicationId != "sportmaster") yield break;
        var id = InstallationIdentity.DisplayName + " · SM供包业务 · sportmaster · " + location.SourceCommit[..8] + " · " + System.IO.Path.GetFileName(location.ReleaseRoot)[^8..];
        foreach (var entry in new[] { (Name: "运行", Args: ""), (Name: "启动修复", Args: "--maintenance"), (Name: "安装维护", Args: "--setup") })
            yield return new(System.IO.Path.Combine(programs, InstallationIdentity.DisplayName, id + " · " + entry.Name + ".lnk"),
                System.IO.Path.Combine(location.ReleaseRoot, "desktop", InstallationIdentity.DesktopExecutable), entry.Args, location.ReleaseRoot);
    }

    [SupportedOSPlatform("windows")]
    static void RemoveOwned(IEnumerable<DeploymentShortcut> files)
    {
        foreach (var entry in files)
        {
            DeploymentPaths.RequireOrdinaryAncestors(entry.Path);
            if (!ExistingShortcutFile(entry.Path)) continue;
            bool owned;
            using (var link = new ShellLinkFile(entry.Path))
                owned = string.Equals(link.TargetPath, entry.Target, StringComparison.OrdinalIgnoreCase) && link.Arguments == entry.Arguments;
            // A user-retargeted link no longer belongs to this installation.
            if (owned) File.Delete(entry.Path);
        }
    }

    static bool ExistingShortcutFile(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        // File.Exists also returns false for a directory and some access failures.
        // Only a genuinely missing entry is an idempotent no-op during removal.
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("保留快捷方式位置的目录或链接冲突：" + path);
        return true;
    }
}
