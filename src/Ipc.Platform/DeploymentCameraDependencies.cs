using System.Runtime.Versioning;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Text.Json;

namespace Ipc.Platform;

public sealed record CameraDependencyInspection(string[] Files, string[] Runtimes)
{
    public bool NeedsRepair => Files.Length != 0 || Runtimes.Length != 0;
}

public static partial class WindowsDeploymentSetup
{
    internal static bool IsCameraDependency(string name) => name is "MVIDCodeReaderConfig.ini" or "app/MVIDCodeReaderConfig.ini" or "desktop/MVIDCodeReaderConfig.ini" or
        "app/Ipc.Drivers.Hikrobot.Mvs.dll" or "desktop/Ipc.Drivers.Hikrobot.Mvs.dll" || new[] { "app/", "desktop/" }.Any(area =>
        new[] { "vendor/hikrobot-mvs/", "vendor/hikrobot-mvid/", "vendor/zxing-cpp/" }.Any(folder =>
            name.StartsWith(area + folder, StringComparison.Ordinal)));

    static Dictionary<string, string> CameraInventory(DeploymentLocation location)
    {
        RequireMaintenanceCache(location);
        var json = File.ReadAllText(location.InventoryFile!);
        _ = ValidateInstalledInventory(location, json);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("Files").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
    }

    [SupportedOSPlatform("windows")]
    public static CameraDependencyInspection InspectCameraDependencies(string directory)
    {
        var location = Current("sportmaster");
        if (location is not { LifecycleState: "Installed", MaintenanceRoot: not null } || !Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.Combine(location.ReleaseRoot, "desktop"), StringComparison.OrdinalIgnoreCase)) return new([], []);
        var files = CameraInventory(location);
        var missing = CameraDependencyDifferences(location.ReleaseRoot, files);
        var media = new DeploymentMedia(location.MaintenanceRoot!, location.ApplicationId, location.SourceCommit, files);
        return new(missing, SmVcRuntimeSetup.Missing(media));
    }

    internal static string[] CameraDependencyDifferences(string release, IReadOnlyDictionary<string, string> files) => files
        .Where(p => IsCameraDependency(p.Key)).Where(p =>
        {
            if (!ProductValidation.IsSafeRelativeFile(release, p.Key, out var target)) throw new InvalidDataException("相机依赖路径无效。");
            DeploymentPaths.RequireOrdinaryAncestors(target);
            return !File.Exists(target) || Wire.FileHash(target) != p.Value;
        }).Select(p => p.Key).ToArray();

    // Idempotent exact-byte recovery. Validate and pin every source before the
    // first write; interruption leaves only individually verifiable files.
    internal static string[] RestoreCameraDependencyFiles(DeploymentLocation location, IReadOnlyDictionary<string, string> files)
    {
        var missing = CameraDependencyDifferences(location.ReleaseRoot, files);
        var held = new List<FileStream>();
        try
        {
            foreach (var name in missing)
            {
                if (!ProductValidation.IsSafeRelativeFile(location.MaintenanceRoot!, name, out var source)) throw new InvalidDataException("相机依赖缓存路径无效。");
                DeploymentPaths.RequireOrdinaryAncestors(source);
                var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read); held.Add(input);
                if (input.Length > 256L * 1024 * 1024 || Wire.FileHash(source) != files[name]) throw new InvalidDataException("相机依赖恢复源缺失或校验失败；请使用同版完整安装包修复。");
            }
            for (var i = 0; i < missing.Length; i++)
            {
                var target = Path.Combine(location.ReleaseRoot, missing[i]);
                DeploymentPaths.RequireOrdinaryAncestors(target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temporary = target + "." + Wire.Id() + ".repair";
                try
                {
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { held[i].CopyTo(output); output.Flush(true); }
                    if (Wire.FileHash(temporary) != files[missing[i]]) throw new IOException("相机依赖复制校验失败。");
                    File.Move(temporary, target, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return missing;
        }
        finally { foreach (var hold in held) hold.Dispose(); }
    }

    [SupportedOSPlatform("windows")]
    public static void RepairCameraDependencies(string expectedRegistrationHash)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var location = Current("sportmaster") ?? throw new InvalidDataException("未找到SM安装登记。");
        using var stopped = new StoppedApplicationOwnership(MachineRuntimePolicy.InstallerLeaseRoot(""), location.ApplicationId, location.DataRoot, default);
        using var setup = new FileStream(Path.Combine(MachineRoot, "deployments", location.ApplicationId + ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Wire.FileHash(Registration(location.ApplicationId)) != expectedRegistrationHash || location.LifecycleState != "Installed")
            throw new InvalidOperationException("安装登记已变化；请重新启动核对。");
        RequireNoUnresolvedInstallation(location.ApplicationId, null);
        RequireNoUnresolvedLifecycle(location.ApplicationId);
        _ = TrustedProgramRoots.RequireBoundary(location.ReleaseRoot);
        var files = CameraInventory(location);
        // Inspect the entire independent cache before it is trusted as repair media.
        var media = DeploymentMedia.Inspect(location.MaintenanceRoot!, location.ApplicationId);
        if (media.SourceCommit != location.SourceCommit) throw new InvalidDataException("相机依赖缓存与安装来源不符。");
        var sid = new SecurityIdentifier(location.RuntimeAccountSid);
        foreach (var name in files.Keys.Where(IsCameraDependency))
        {
            var parent = Path.GetDirectoryName(Path.Combine(location.ReleaseRoot, name))!;
            DeploymentPaths.RequireOrdinaryAncestors(parent);
            ProtectedDirectory(parent, sid);
        }
        foreach (var name in RestoreCameraDependencyFiles(location, files))
            new FileInfo(Path.Combine(location.ReleaseRoot, name)).SetAccessControl(MachineRuntimeSetup.FileAcl(sid, FileSystemRights.ReadAndExecute));
        _ = SmVcRuntimeSetup.Ensure(media, media.Root, null);
        var result = InspectCameraDependencies(Path.Combine(location.ReleaseRoot, "desktop"));
        if (result.NeedsRepair) throw new IOException("相机依赖仍未就绪，请使用同版完整安装包修复。");
    }
}
