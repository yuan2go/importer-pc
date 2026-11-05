using System.Security.AccessControl;
using System.Security.Principal;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Assembly;

// Rebuild only a missing launch seed. The configuration ledger and business
// database stay authoritative and are never changed by installation preparation.
public static class InstallationConfiguration
{
    internal static ParcelConfiguration ReadSaved(string ledger)
    {
        DeploymentPaths.RequireOrdinaryAncestors(ledger);
        if (new FileInfo(ledger).Length > 4 * 1024 * 1024) throw new InvalidDataException("原配置文件过大，原件已保留，请核对本站配置。");
        return ConfigurationRevisions<ParcelConfiguration>.ReadCurrentEnvelope(File.ReadAllText(ledger)).Effective;
    }
    internal static bool MissingSeed(string directory)
    {
        var path = Path.Combine(directory, "business.json"); DeploymentPaths.RequireOrdinaryAncestors(path);
        try { _ = File.GetAttributes(path); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
    }

    public static string Prepare(ParcelConfiguration initial, DeploymentLocation? installed, string destination, string? dataRoot = null)
    {
        if (installed is not null)
        {
            if (installed.ApplicationId != initial.ApplicationId) throw new InvalidDataException("安装记录属于其他软件，原配置已保留。");
            if (!MissingSeed(installed.ConfigurationDirectory)) return installed.ConfigurationDirectory;
        }
        var seed = initial;
        dataRoot = installed?.DataRoot ?? dataRoot;
        if (dataRoot is not null)
        {
            var candidates = new List<(string Directory, RunMode Mode, string Station)>();
            foreach (var mode in Enum.GetValues<RunMode>())
            {
                var modeRoot = Path.Combine(dataRoot, mode.ToString());
                DeploymentPaths.RequireOrdinaryAncestors(modeRoot);
                if (!Directory.Exists(modeRoot)) continue;
                foreach (var station in Directory.EnumerateDirectories(modeRoot))
                {
                    var directory = Path.Combine(station, initial.ApplicationId);
                    DeploymentPaths.RequireOrdinaryAncestors(directory);
                    if (Directory.Exists(directory)) candidates.Add((directory, mode, Path.GetFileName(station)));
                }
            }
            if (candidates.Count > 1) throw new InvalidDataException("启动配置缺失，数据目录中有多个供包台。请由维护人员核对本站配置；原数据已保留。");
            if (candidates.SingleOrDefault() is { Directory: not null } candidate)
            {
                if (!ExtensionSchema.IsId(candidate.Station)) throw new InvalidDataException("原供包台目录名称无效，原数据已保留，请核对本站配置。");
                seed = initial with { Mode = candidate.Mode, StationId = candidate.Station };
                var ledger = Path.Combine(candidate.Directory, "configuration.json");
                if (File.Exists(ledger))
                {
                    var saved = ReadSaved(ledger);
                    if (saved.SchemaVersion != 3 || saved.ApplicationId != seed.ApplicationId || saved.Mode != seed.Mode || saved.StationId != seed.StationId)
                        throw new InvalidDataException("原配置与供包台目录不一致，原件已保留，请核对本站配置。");
                    seed = saved;
                }
            }
        }
        DeploymentPaths.RequireOrdinaryAncestors(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("安装配置暂存位置已占用。");
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { identity.User!, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
                acl.AddAccessRule(new(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(destination).Create(acl);
        }
        else Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "business.json"), Wire.Encode(seed));
        return destination;
    }
}
