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
        if (!TrustedProgramRoots.Contains(Path.Combine(MaintenanceCacheRoot, location.ApplicationId), root) ||
            location.InventoryFile != Path.Combine(root, "installed-inventory.json") || !ExtensionSchema.IsHash(location.InventorySha256 ?? ""))
            throw new InvalidDataException("维护缓存身份或范围无效。");
        MachineRuntimePolicy.RequireProtectedEvidence(location.InventoryFile);
        if (Wire.FileHash(location.InventoryFile) != location.InventorySha256) throw new InvalidDataException("原安装库存不一致。");
        return root;
    }

    static string[] InstalledInventory(DeploymentLocation location)
    {
        RequireMaintenanceCache(location);
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(location.InventoryFile!));
        var value = document.RootElement;
        if (value.GetProperty("ApplicationId").GetString() != location.ApplicationId ||
            value.GetProperty("SourceCommit").GetString() != location.SourceCommit || value.GetProperty("ReleaseRoot").GetString() != location.ReleaseRoot)
            throw new InvalidDataException("原安装库存不属于本部署。");
        return value.GetProperty("Files").EnumerateObject().Select(p => p.Name).ToArray();
    }
}
