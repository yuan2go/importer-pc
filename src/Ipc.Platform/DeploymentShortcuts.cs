using System.Runtime.Versioning;
using System.Text.Json;

namespace Ipc.Platform;

public sealed record DeploymentShortcutOptions(bool StartMenu, bool Desktop,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool AutoStart = false);
public sealed record DeploymentShortcut(string Path, string Target, string Arguments, string WorkingDirectory);

public static class DeploymentShortcuts
{
    public static DeploymentShortcut[] Plan(DeploymentLocation location, string programs, string desktop, string? startup = null) =>
        Plan(location, RequireShortcutProfile(location), programs, desktop, startup);

    static ProductDeploymentProfile RequireShortcutProfile(DeploymentLocation location) =>
        ProductDeploymentCatalog.Find(location.ApplicationId) is { SupportsWindowsShortcuts: true } profile ? profile :
            throw new NotSupportedException("此产品未声明安装快捷方式：" + location.ApplicationId);

    static DeploymentShortcut[] Plan(DeploymentLocation location, ProductDeploymentProfile profile, string programs, string desktop, string? startup)
    {
        var options = location.Shortcuts ?? throw new InvalidDataException("部署缺少明确的快捷方式选项；保留原登记并使用当前安装计划。");
        var target = System.IO.Path.Combine(location.ReleaseRoot, "desktop", InstallationIdentity.DesktopExecutable);
        var result = new List<DeploymentShortcut>();
        if (options.StartMenu && profile.UseDualProgramShortcuts)
        {
            result.Add(new(System.IO.Path.Combine(programs, InstallationIdentity.DisplayName, InstallationIdentity.DisplayName + ".lnk"), target, "", location.ReleaseRoot));
            result.Add(new(System.IO.Path.Combine(programs, InstallationIdentity.DisplayName, InstallationIdentity.OperationsDisplayName + ".lnk"),
                target, InstallationIdentity.OperationsArgument, location.ReleaseRoot));
        }
        else if (options.StartMenu)
            result.AddRange(VersionedMenu(location, profile, programs));
        if (options.Desktop) result.Add(new(System.IO.Path.Combine(desktop, InstallationIdentity.DisplayName + ".lnk"), target, "", location.ReleaseRoot));
        if (options.AutoStart && profile.SupportsAutoStart && !string.IsNullOrWhiteSpace(startup))
            result.Add(AutoStartEntry(location, startup));
        return result.ToArray();
    }

    [SupportedOSPlatform("windows")]
    public static void Create(DeploymentLocation location, string programs, string desktop, DeploymentLocation? previous = null, string? startup = null)
    {
        previous ??= location.PreviousDeployment;
        var profile = RequireShortcutProfile(location);
        if (previous is not null) _ = RequireShortcutProfile(previous);
        var plan = Plan(location, profile, programs, desktop, startup);
        if (profile.UseDualProgramShortcuts && location.InventoryFile is { } inventory)
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
                !((profile.UseDualProgramShortcuts || entry.Arguments == "" && System.IO.Path.GetFileName(entry.Path) == InstallationIdentity.DisplayName + ".lnk") &&
                  PreviousOwnsTarget(location, previous, entry, link.TargetPath, link.Arguments)))
                throw new IOException("保留目标不同的既有快捷方式：" + entry.Path);
            link.TargetPath = entry.Target; link.Arguments = entry.Arguments; link.WorkingDirectory = entry.WorkingDirectory;
            link.Description = profile.ShortcutDescription; link.SetIcon(entry.Target, 0); link.Save(entry.Path);
        }
        var candidates = OwnedCandidates(location, programs, desktop, startup)
            .Concat(previous is null ? [] : OwnedCandidates(previous, programs, desktop, startup));
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
    public static void Remove(DeploymentLocation location, string programs, string desktop, string? startup = null)
    {
        RemoveOwned(OwnedCandidates(location, programs, desktop, startup));
    }

    static DeploymentShortcut AutoStartEntry(DeploymentLocation location, string startup) =>
        new(Path.Combine(startup, InstallationIdentity.DisplayName + ".lnk"),
            Path.Combine(location.ReleaseRoot, "desktop", InstallationIdentity.DesktopExecutable), "", location.ReleaseRoot);

    [SupportedOSPlatform("windows")]
    public static bool ReadAutoStart(DeploymentLocation location, string? startup)
    {
        if (ProductDeploymentCatalog.Find(location.ApplicationId)?.SupportsAutoStart != true || string.IsNullOrWhiteSpace(startup)) return false;
        var entry = AutoStartEntry(location, startup);
        DeploymentPaths.RequireOrdinaryAncestors(entry.Path);
        if (!ExistingShortcutFile(entry.Path)) return false;
        using var link = new ShellLinkFile(entry.Path);
        return link.TargetPath.Equals(entry.Target, StringComparison.OrdinalIgnoreCase) && link.Arguments == entry.Arguments;
    }

    [SupportedOSPlatform("windows")]
    public static bool ReadCurrentUserAutoStart() => ReadAutoStart(RequireCurrentWorkstation(), CurrentUserStartup());

    [SupportedOSPlatform("windows")]
    public static void SaveCurrentUserAutoStart(bool enabled)
    {
        var location = RequireCurrentWorkstation();
        var startup = CurrentUserStartup();
        var entry = AutoStartEntry(location, startup);
        DeploymentPaths.RequireOrdinaryAncestors(entry.Path);
        if (!enabled) { RemoveOwned([entry]); return; }
        if (ReadAutoStart(location, startup)) return;
        if (ExistingShortcutFile(entry.Path))
            throw new IOException("启动目录已有同名快捷方式，已保留。请先检查它的目标。");
        Directory.CreateDirectory(startup);
        var temporary = Path.Combine(startup, "." + Wire.Id() + ".tmp");
        try
        {
            using (var link = new ShellLinkFile())
            {
                link.TargetPath = entry.Target; link.Arguments = entry.Arguments; link.WorkingDirectory = entry.WorkingDirectory;
                link.Description = InstallationIdentity.DisplayName; link.SetIcon(entry.Target, 0); link.Save(temporary);
            }
            // Publish only to a free name. Never overwrite a user-edited shortcut.
            File.Move(temporary, entry.Path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    [SupportedOSPlatform("windows")]
    public static string CurrentUserStartupDirectory() =>
        Environment.GetFolderPath(Environment.SpecialFolder.Startup, Environment.SpecialFolderOption.DoNotVerify);

    [SupportedOSPlatform("windows")]
    static string CurrentUserStartup() =>
        CurrentUserStartupDirectory() is { Length: > 0 } path ? path :
            throw new IOException("当前 Windows 用户的启动目录不可用。");

    [SupportedOSPlatform("windows")]
    static DeploymentLocation RequireCurrentWorkstation()
    {
        if (!string.Equals(Path.GetFileName(Environment.ProcessPath), InstallationIdentity.DesktopExecutable, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请从已安装的供包台打开设置。");
        var profile = ProductDeploymentCatalog.IsolatedWorkstation;
        if (!profile.SupportsAutoStart) throw new InvalidOperationException("此产品未声明开机自启。");
        var location = DeploymentMedia.ReadInstalled(AppContext.BaseDirectory, profile.ApplicationId) ??
            throw new InvalidOperationException("请从已安装的供包台打开设置。");
        var executable = Path.Combine(location.ReleaseRoot, "desktop", InstallationIdentity.DesktopExecutable);
        if (!string.Equals(Environment.ProcessPath, executable, StringComparison.OrdinalIgnoreCase) ||
            Environment.GetCommandLineArgs().Contains(InstallationIdentity.OperationsArgument))
            throw new InvalidOperationException("请从当前安装的供包台打开设置。");
        DeploymentPaths.RequireOrdinaryAncestors(executable);
        MachineRuntimePolicy.RequireProtectedEvidence(executable);
        return location;
    }

    static DeploymentShortcut LegacyTool(DeploymentShortcut entry, DeploymentLocation location) =>
        entry with { Target = Path.Combine(location.ReleaseRoot, "desktop", InstallationIdentity.LegacyOperationsExecutable), Arguments = "" };

    static IEnumerable<DeploymentShortcut> OwnedCandidates(DeploymentLocation location, string programs, string desktop, string? startup = null)
    {
        var profile = RequireShortcutProfile(location);
        foreach (var entry in Plan(location with { Shortcuts = new(true, true, true) }, profile, programs, desktop, startup))
        {
            yield return entry;
            if (entry.Arguments == InstallationIdentity.OperationsArgument) yield return LegacyTool(entry, location);
        }
        if (profile.UseDualProgramShortcuts)
        {
            foreach (var entry in VersionedMenu(location, profile, programs)) yield return entry;
            foreach (var entry in new[] { (Name: "启动修复", Args: "--maintenance"), (Name: "安装维护", Args: "--setup") })
                yield return new(System.IO.Path.Combine(programs, InstallationIdentity.DisplayName, InstallationIdentity.DisplayName + " · " + entry.Name + ".lnk"),
                    System.IO.Path.Combine(location.ReleaseRoot, "desktop", InstallationIdentity.DesktopExecutable), entry.Args, location.ReleaseRoot);
        }
    }

    // Exact names from the registered installation only. Never sweep menu folders
    // or remove a user-retargeted shortcut based on its display name.
    static IEnumerable<DeploymentShortcut> VersionedMenu(DeploymentLocation location, ProductDeploymentProfile profile, string programs)
    {
        var id = InstallationIdentity.DisplayName + " · " + profile.ShortcutBusinessName + " · " + location.ApplicationId + " · " +
            location.SourceCommit[..8] + " · " + System.IO.Path.GetFileName(location.ReleaseRoot)[^8..];
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
