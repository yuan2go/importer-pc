using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace Ipc.Platform;

public sealed record WindowsRemovalReceipt(string Status, string OperationFile, string[] Retained, bool RestartRequired, string[]? RemainingFiles = null, string? ReleaseRoot = null);
public static partial class WindowsDeploymentSetup
{
    sealed record RemovalState(string Status, DeploymentLocation Location, string[] Files, string[]? Remaining = null, bool RestartRequired = false)
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string[]? SystemEntries { get; init; }
    }
    static string SystemKey(string application) => @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ImporterPc." + application;
    [SupportedOSPlatform("windows")]
    static void ProjectInstalledApplication(DeploymentLocation location)
    {
        DesktopRecoveryInstallation.Project(location);
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        if (location.LifecycleState != "Installed") { machine.DeleteSubKeyTree(SystemKey(location.ApplicationId), false); return; }
        using var key = machine.CreateSubKey(SystemKey(location.ApplicationId), true);
        var executable = Path.Combine(location.ReleaseRoot, "desktop", "Ipc.Desktop.exe");
        var release = Wire.Decode<ApplicationRelease>(File.ReadAllText(Path.Combine(location.ReleaseRoot, "app", "application-release.json")));
        key.SetValue("DisplayName", "菜鸟供包台 · " + location.ApplicationId);
        key.SetValue("DisplayVersion", (release.BusinessVersion ?? "reference") + " · " + location.SourceCommit[..12]);
        key.SetValue("Publisher", "Importer PC reference project"); key.SetValue("InstallLocation", location.ReleaseRoot);
        key.SetValue("DisplayIcon", executable); key.SetValue("UninstallString", "\"" + executable + "\" --product-maintenance");
        key.SetValue("ModifyPath", "\"" + executable + "\" --product-maintenance");
        key.SetValue("URLInfoAbout", "https://github.com/yuan2go/importer-pc"); key.SetValue("NoModify", 0, RegistryValueKind.DWord);
        key.SetValue("Comments", "程序维护沿唯一受保护部署登记；卸载保留业务数据、身份、原件及扩展。未取得现场投产资格。");
        key.Flush();
    }
    [SupportedOSPlatform("windows")]
    public static void RepairSystemRegistration(string application, string expectedHash, Action<DeploymentLocation>? repairShortcuts = null)
        => RepairSystemRegistrationCore(application, expectedHash, (location, _) => repairShortcuts?.Invoke(location), null);
    [SupportedOSPlatform("windows")]
    public static void RepairCommittedInstallation(WindowsDeploymentRequest original, string expectedHash, Action<DeploymentLocation, DeploymentLocation?> repairShortcuts)
        => RepairSystemRegistrationCore(original.ApplicationId, expectedHash, repairShortcuts, original);
    [SupportedOSPlatform("windows")]
    static void RepairSystemRegistrationCore(string application, string expectedHash, Action<DeploymentLocation, DeploymentLocation?> repairShortcuts, WindowsDeploymentRequest? original)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        using var guard = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Wire.FileHash(Registration(application)) != expectedHash) throw new InvalidOperationException("部署登记变化；重新检查维护计划");
        var location = Current(application) ?? throw new InvalidDataException("No authoritative deployment");
        InstallationInspection? inspection = null; string? operation = null, archive = null;
        if (original is not null)
        {
            operation = InstallationOperationFile(original);
            MachineRuntimePolicy.RequireProtectedEvidence(operation);
            if (new FileInfo(operation).Length > 4 * 1024 * 1024) throw new InvalidDataException("原安装回执超出读取预算");
            inspection = ProjectInstallation(original, File.ReadAllText(operation), location);
            if (inspection.Status is not ("Completed" or "CommittedNeedsSystemRepair") || inspection.Location is null)
                throw new InvalidOperationException("原安装尚无可核验的已提交登记；保留原操作，不修复其他安装");
            if (inspection.Previous is { } previous) _ = TrustedProgramRoots.RequireBoundary(previous.ReleaseRoot);
            if (inspection.Status != "Completed")
            {
                var hash = Wire.FileHash(operation); archive = operation + ".pre-system-repair-" + hash[..12] + ".json";
                if (File.Exists(archive))
                { MachineRuntimePolicy.RequireProtectedEvidence(archive); if (Wire.FileHash(archive) != hash) throw new InvalidDataException("原失败回执归档不一致；保留原件"); }
                else WriteProtectedBytes(archive, File.ReadAllBytes(operation), new SecurityIdentifier(location.RuntimeAccountSid), overwrite: false);
            }
        }
        if (location.LifecycleState == "Installed") _ = DeploymentMedia.Inspect(location.ReleaseRoot, application);
        if (location.LifecycleState == "Installed") repairShortcuts(location, inspection?.Previous);
        ProjectInstalledApplication(location);
        if (location is { LifecycleState: "Installed", Recovery.Enabled: true }) DesktopRecoverySupervisor.ResetAfterAuthorizedRepair(location);
        if (original is not null && inspection is { Status: "CommittedNeedsSystemRepair" })
            WriteProtected(operation!, new { Status = "Completed", RequestIdentity = InstallationRequestIdentity(original), inspection.ChangePlanIdentity,
                Location = location, inspection.Previous, BusinessStarted = false, RepairedFromStatus = inspection.Status, SystemRepairArchive = archive }, new SecurityIdentifier(location.RuntimeAccountSid));
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool MoveFileEx(string path, string? target, int flags);
    [SupportedOSPlatform("windows")]
    public static WindowsRemovalReceipt Remove(string application, string expectedHash, Action<DeploymentLocation> requireResponsibilitiesAndBackup,
        Action<DeploymentLocation> removeShortcuts, string[]? originalMediaFiles = null)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        using var guard = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Wire.FileHash(Registration(application)) != expectedHash) throw new InvalidOperationException("部署登记变化；重新检查卸载计划");
        var location = Current(application) ?? throw new InvalidDataException("没有可核验的原部署");
        var sid = new SecurityIdentifier(location.RuntimeAccountSid);
        var operation = location.LifecycleState != "Installed" && location.MaintenanceOperation is not null ? location.MaintenanceOperation : Path.Combine(MachineRoot, "deployments", application + ".remove-" + Wire.Id() + ".json");
        _ = TrustedProgramRoots.RequireBoundary(location.ReleaseRoot);
        RemovalState plan;
        if (location.LifecycleState == "Installed")
        {
            var media = DeploymentMedia.Inspect(location.ReleaseRoot, application);
            requireResponsibilitiesAndBackup(location); // Caller holds application-owner until return.
            plan = new("Prepared", location, media.Files.Keys.Append("deployment-location.json").Append("deployment-retired.json").ToArray())
                { SystemEntries = location.Recovery is not null ? DesktopRecoveryInstallation.Plan(location).SystemEntries : null };
            WriteProtected(operation, plan, sid);
        }
        else
        {
            MachineRuntimePolicy.RequireProtectedEvidence(operation);
            if (!TrustedProgramRoots.Contains(Path.Combine(MachineRoot, "deployments"), operation) || new FileInfo(operation).Length > 4 * 1024 * 1024) throw new InvalidDataException("Unsafe removal journal");
            plan = Wire.Decode<RemovalState>(File.ReadAllText(operation));
            ValidateRemovalState(plan, location);
            if (originalMediaFiles is null || !plan.Files.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(originalMediaFiles.Append("deployment-location.json").Append("deployment-retired.json").Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("原卸载清单与同源完整介质不一致，拒绝清理；原件保留");
        }
        var retired = location with { LifecycleState = "RemovedDataRetained", MaintenanceOperation = operation };
        if (Directory.Exists(location.ReleaseRoot)) WriteProtected(Path.Combine(location.ReleaseRoot, "deployment-retired.json"), retired, sid);
        WriteProtected(Registration(application), retired, sid); // one irreversible detach; reentry reads this state
        if (application is "sportmaster" or "direct-plc" or "wcs-to-plc") ManagementModeBroker.RemoveIfUnused(location.DataRoot);
        ProjectInstalledApplication(retired); removeShortcuts(location);
        var cleanup = CleanRemovalInventory(location.ReleaseRoot, plan.Files, path => MoveFileEx(path, null, 4));
        var status = cleanup.RestartRequired ? "RestartRequired" : cleanup.Remaining.Length == 0 ? "RemovedDataRetained" : "RemovedWithRetainedFiles";
        WriteProtected(operation, plan with { Status = status, Remaining = cleanup.Remaining, RestartRequired = cleanup.RestartRequired }, sid);
        var dumpDirectory = location.Recovery is not null ? DesktopRecoveryInstallation.Plan(location).DumpFolder : null;
        return new(status, operation, [location.DataRoot, location.ConfigurationDirectory,
            .. dumpDirectory is not null && Directory.Exists(dumpDirectory) ? new[] { dumpDirectory } : Array.Empty<string>()], cleanup.RestartRequired, cleanup.Remaining, location.ReleaseRoot);
    }
    internal static (bool RestartRequired, string[] Remaining) CleanRemovalInventory(string root, string[] files, Func<string, bool> scheduleDelete)
    {
        root = Path.GetFullPath(root); DeploymentPaths.RequireOrdinaryAncestors(root);
        if (files.Length > 16384 || files.Any(n => !ExtensionPackages.SafePath(n)) || files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length)
            throw new InvalidDataException("Unsafe removal inventory");
        if (!Directory.Exists(root))
        {
            if (File.Exists(root)) throw new InvalidDataException("Removal directory was replaced by a file");
            return (false, []); // Completed deletion/reboot, possibly before the journal's final write.
        }
        // Validate the entire fixed inventory before deleting the first file.
        foreach (var name in files)
        {
            if (!ProductValidation.IsSafeRelativeFile(root, name, out var path)) throw new InvalidDataException("Unsafe removal inventory");
            DeploymentPaths.RequireOrdinaryAncestors(path);
        }
        var restart = false; var remaining = new List<string>();
        foreach (var name in files)
        {
            if (!ProductValidation.IsSafeRelativeFile(root, name, out var path)) throw new InvalidDataException("Unsafe removal inventory");
            DeploymentPaths.RequireOrdinaryAncestors(path);
            if (!File.Exists(path)) continue;
            try { File.Delete(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                if (!scheduleDelete(path)) throw new Win32Exception(Marshal.GetLastWin32Error(), "卸载文件被占用且无法登记重启删除；保留操作回执");
                restart = true; remaining.Add(Path.GetRelativePath(root, path));
            }
        }
        // Only empty, inventoried directories; no recursive delete, no shared drivers,
        // no other releases, policies, data, package consumers or arbitrary extra files.
        var directories = files.Select(n => Path.GetDirectoryName(Path.Combine(root, n))!).SelectMany(p => Parents(p, root)).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(p => p.Length);
        foreach (var directory in directories)
        {
            DeploymentPaths.RequireOrdinaryAncestors(directory);
            if (!Directory.Exists(directory)) continue;
            try { Directory.Delete(directory, false); }
            catch (IOException) { if (restart && scheduleDelete(directory)) continue; remaining.Add(directory); }
        }
        return (restart, remaining.ToArray());
    }
    static IEnumerable<string> Parents(string directory, string root)
    {
        while (TrustedProgramRoots.Contains(root, directory))
        { yield return directory; if (string.Equals(Path.GetFullPath(directory), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) break; directory = Path.GetDirectoryName(directory)!; }
    }
    [SupportedOSPlatform("windows")]
    public static void SwitchDataRoot(string application, string expectedRegistrationHash, string newDataRoot, string handoffHash)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        if (!ExtensionSchema.IsHash(handoffHash)) throw new InvalidDataException("Missing restore handoff identity");
        DeploymentPaths.RequireOrdinaryAncestors(newDataRoot);
        using var guard = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Wire.FileHash(Registration(application)) != expectedRegistrationHash) throw new InvalidOperationException("原部署在接管前变化；原根围栏保留，重新核对后继续");
        var current = Current(application) ?? throw new InvalidDataException("原机部署登记缺失");
        if (current.LifecycleState != "Installed") throw new InvalidOperationException("只有已安装原机部署可接管恢复根");
        PreviewDataAccess(newDataRoot, current.RuntimeAccountSid);
        var sid = new SecurityIdentifier(current.RuntimeAccountSid);
        var operation = Path.Combine(MachineRoot, "deployments", application + ".migration-" + Wire.Id() + ".json");
        var next = current with { DataRoot = Path.GetFullPath(newDataRoot), MaintenanceOperation = operation, RestoreHandoffHash = handoffHash };
        WriteProtected(operation, new { Status = "Prepared", Previous = current, Next = next, HandoffSha256 = handoffHash }, sid);
        if (application is "sportmaster" or "direct-plc" or "wcs-to-plc")
        {
            ManagementModeBroker.CopyCredentialsForMigration(current.DataRoot, newDataRoot);
            ManagementModeBroker.Install(newDataRoot, Path.Combine(current.ReleaseRoot, "app"));
        }
        WriteProtected(Path.Combine(current.ReleaseRoot, "deployment-location.json"), next, sid);
        WriteProtected(Registration(application), next, sid);
        ProjectInstalledApplication(next);
        if (application is "sportmaster" or "direct-plc" or "wcs-to-plc") ManagementModeBroker.RemoveIfUnused(current.DataRoot);
        WriteProtected(operation, new { Status = "CompletedIsolated", Previous = current, Next = next, HandoffSha256 = handoffHash }, sid);
    }
}
