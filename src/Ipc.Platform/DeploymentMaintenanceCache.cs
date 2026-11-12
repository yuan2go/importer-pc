using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Ipc.Platform;

public static partial class WindowsDeploymentSetup
{
    static string MaintenanceCacheRoot => Path.Combine(WindowsStoragePaths.ProtectedAnchor ?? @"D:\ProgramData\ImporterPc", "Maintenance");

    [SupportedOSPlatform("windows")]
    static DeploymentLocation CacheMaintenance(DeploymentLocation location, DeploymentMedia media, SecurityIdentifier runtimeSid)
    {
        var parent = Path.Combine(MaintenanceCacheRoot, location.ApplicationId);
        if (!Path.GetPathRoot(parent)!.Equals(@"D:\", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("维护缓存须使用管理员批准的D盘存储根，请先完成路径配置。");
        ProtectedDirectory(parent, runtimeSid);
        var root = Path.Combine(parent, Wire.Id());
        ProtectedDirectory(root, runtimeSid);
        var size = media.Files.Keys.Sum(n => new FileInfo(Path.Combine(media.Root, n)).Length);
        if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < size + 67108864)
            throw new IOException("维护缓存空间不足，原部署保留。");
        foreach (var file in media.Files)
        {
            if (!ProductValidation.IsSafeRelativeFile(root, file.Key, out var target)) throw new InvalidDataException("维护缓存路径无效。");
            var source = Path.Combine(media.Root, file.Key);
            DeploymentPaths.RequireOrdinaryAncestors(source);
            if (Wire.FileHash(source) != file.Value) throw new InvalidDataException("维护缓存源文件已变化。");
            ProtectedDirectory(Path.GetDirectoryName(target)!, runtimeSid);
            WriteProtectedBytes(target, File.ReadAllBytes(source), runtimeSid, overwrite: false);
            new FileInfo(target).SetAccessControl(MachineRuntimeSetup.FileAcl(runtimeSid, FileSystemRights.ReadAndExecute));
            if (Wire.FileHash(target) != file.Value) throw new IOException("维护缓存复制不完整。");
        }
        var inventory = Path.Combine(root, "installed-inventory.json");
        WriteProtected(inventory, new { location.ApplicationId, location.SourceCommit, location.ReleaseRoot, Files = media.Files }, runtimeSid, overwrite: false);
        return location with { MaintenanceRoot = root, InventoryFile = inventory, InventorySha256 = Wire.FileHash(inventory) };
    }

    public static string RequireMaintenanceCache(DeploymentLocation location)
    {
        var root = location.MaintenanceRoot ?? throw new InvalidDataException("原版本没有独立维护缓存，请先使用已修复的同版安装维护工具。");
        DeploymentPaths.RequireOrdinaryAncestors(root);
        var parent = Path.Combine(MaintenanceCacheRoot, location.ApplicationId);
        if (!Path.GetDirectoryName(Path.GetFullPath(root))!.Equals(Path.GetFullPath(parent), StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(root), "N", out var cacheId) || cacheId == Guid.Empty)
            throw new InvalidDataException("维护缓存必须是本站独立版本目录，不能使用共享根。");
        if (!TrustedProgramRoots.Contains(Path.Combine(MaintenanceCacheRoot, location.ApplicationId), root) ||
            location.InventoryFile != Path.Combine(root, "installed-inventory.json") || !ExtensionSchema.IsHash(location.InventorySha256 ?? ""))
            throw new InvalidDataException("维护缓存身份或范围无效。");
        MachineRuntimePolicy.RequireProtectedEvidence(location.InventoryFile);
        if (new FileInfo(location.InventoryFile).Length > 4 * 1024 * 1024)
            throw new InvalidDataException("原安装库存超过读取预算。");
        if (Wire.FileHash(location.InventoryFile) != location.InventorySha256) throw new InvalidDataException("原安装库存不一致。");
        return root;
    }

    [SupportedOSPlatform("windows")]
    public static void RequireProtectedMaintenanceHelper(string application, string directory, IReadOnlyList<string> requiredFiles)
    {
        var location = Current(application);
        var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        var cache = location?.MaintenanceRoot;
        var area = cache is null ? null : new[] { "app", "desktop" }.FirstOrDefault(name =>
            full.Equals(Path.Combine(cache, name), StringComparison.OrdinalIgnoreCase));
        if (area is null)
        {
            MachineRuntimePolicy.RequireProtectedApplicationInstaller(directory, requiredFiles);
            return;
        }
        // The installer owns this separate cache; it is not an operator-approved
        // program root. Anchor its identity to the protected current registration.
        RequireMaintenanceCache(location!);
        var inventory = File.ReadAllText(location!.InventoryFile!);
        _ = ValidateInstalledInventory(location, inventory);
        using var document = System.Text.Json.JsonDocument.Parse(inventory);
        var files = document.RootElement.GetProperty("Files");
        foreach (var name in requiredFiles)
        {
            if (!ProductValidation.IsSafeRelativeFile(full, name, out var path) ||
                !files.TryGetProperty(area + "/" + name.Replace('\\', '/'), out var expected))
                throw new InvalidDataException("维护程序文件不属于本次安装清单。");
            MachineRuntimePolicy.RequireProtectedEvidence(path);
            if (!Wire.FileHash(path).Equals(expected.GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("维护程序文件与本次安装清单不一致。");
        }
    }

    static string[] InstalledInventory(DeploymentLocation location)
    {
        if (location.ApplicationId == "sportmaster" && location.LifecycleState != "Installed" && !File.Exists(location.InventoryFile) && ExtensionSchema.IsHash(location.InventorySha256 ?? ""))
        {
            var archive = AssetInventoryArchive(location.InventorySha256!);
            MachineRuntimePolicy.RequireProtectedEvidence(archive);
            if (new FileInfo(archive).Length > 4 * 1024 * 1024 || Wire.FileHash(archive) != location.InventorySha256)
                throw new InvalidDataException("原卸载的安全库存回执无法核验。");
            return ValidateInstalledInventory(location, File.ReadAllText(archive));
        }
        RequireMaintenanceCache(location);
        return ValidateInstalledInventory(location, File.ReadAllText(location.InventoryFile!));
    }

    internal static string[] ValidateInstalledInventory(DeploymentLocation location, string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 4 * 1024 * 1024) throw new InvalidDataException("原安装库存超过读取预算。");
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var value = document.RootElement;
        if (value.GetProperty("ApplicationId").GetString() != location.ApplicationId ||
            value.GetProperty("SourceCommit").GetString() != location.SourceCommit || value.GetProperty("ReleaseRoot").GetString() != location.ReleaseRoot)
            throw new InvalidDataException("原安装库存不属于本部署。");
        var files = value.GetProperty("Files").EnumerateObject().Take(16385).ToArray();
        // The release may already have been removed. Validate the recorded names
        // without probing that tree; the deleter checks ordinary paths before I/O.
        if (files.Length is 0 or > 16384 || files.Any(p => !ExtensionPackages.SafePath(p.Name) ||
                p.Value.ValueKind != System.Text.Json.JsonValueKind.String || !ExtensionSchema.IsHash(p.Value.GetString()!)) ||
            files.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length)
            throw new InvalidDataException("原安装库存路径、字节身份或数量无效。");
        return files.Select(p => p.Name).ToArray();
    }
}
