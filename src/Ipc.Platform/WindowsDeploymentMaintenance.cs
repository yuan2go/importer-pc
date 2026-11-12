using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace Ipc.Platform;

public sealed record WindowsRemovalReceipt(string Status, string OperationFile, string[] Retained, bool RestartRequired, string[]? RemainingFiles = null, string? ReleaseRoot = null)
{
    public bool WithoutBusinessInspection { get; init; }
    public string? OperationId { get; init; }
}
public static partial class WindowsDeploymentSetup
{
    internal sealed record RemovalState(string Status, DeploymentLocation Location, string[] Files, string[]? Remaining = null, bool RestartRequired = false)
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string[]? SystemEntries { get; init; }
        public bool WithoutBusinessInspection { get; init; }
        public string? OperationId { get; init; }
        public string? ExpectedRegistrationHash { get; init; }
    }
    static HashSet<string> RemovedRollbackOperations(string application)
    {
        var directory = Path.Combine(MachineRoot, "deployments");
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = Directory.EnumerateFiles(directory, application + ".remove-*.json").Take(129).ToArray();
        if (files.Length > 128) throw new InvalidDataException("原卸载记录超过核对预算，保留原件。");
        long bytes = 0;
        foreach (var file in files)
        {
            MachineRuntimePolicy.RequireProtectedEvidence(file);
            bytes = checked(bytes + new FileInfo(file).Length);
            if (bytes > 16 * 1024 * 1024) throw new InvalidDataException("原卸载记录超过读取预算，保留原件。");
            var json = File.ReadAllText(file); var state = Wire.Decode<RemovalState>(json);
            if (state.Location.LifecycleOperation is { } operation && RemovalSupersedesRollback(application, operation, json))
                result.Add(operation);
        }
        return result;
    }
    internal static bool RemovalSupersedesRollback(string application, string operation, string json)
    {
        var state = Wire.Decode<RemovalState>(json);
        return state.Status is "RemovedDataRetained" or "RestartRequired" or "RemovedWithRetainedFiles" &&
            state.Location.ApplicationId == application && state.Location.LifecycleOperation == operation &&
            state.Location.SourceCommit.Length == 40 && state.Location.SourceCommit.All(char.IsAsciiHexDigit);
    }
    static string SystemKey(string application) => @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ImporterPc." + application;
    [SupportedOSPlatform("windows")]
    static void ProjectInstalledApplication(DeploymentLocation location)
    {
        DesktopRecoveryInstallation.Project(location);
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        if (location.LifecycleState != "Installed") { machine.DeleteSubKeyTree(SystemKey(location.ApplicationId), false); return; }
        using var key = machine.CreateSubKey(SystemKey(location.ApplicationId), true);
        var executable = Path.Combine(location.MaintenanceRoot ?? location.ReleaseRoot, "desktop", "Ipc.Desktop.exe");
        var release = Wire.Decode<ApplicationRelease>(File.ReadAllText(Path.Combine(location.ReleaseRoot, "app", "application-release.json")));
        key.SetValue("DisplayName", location.ApplicationId == "sportmaster" ? "菜鸟供包台" : "菜鸟供包台 · " + location.ApplicationId);
        key.SetValue("DisplayVersion", release.BusinessVersion ?? "reference");
        key.SetValue("QuietUninstallString", "\"" + executable + "\" /quiet /uninstall /norestart");
        key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, new DirectoryInfo(location.ReleaseRoot).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024), RegistryValueKind.DWord);
        key.SetValue("Publisher", "Importer PC reference project"); key.SetValue("InstallLocation", location.ReleaseRoot);
        key.SetValue("DisplayIcon", executable); key.SetValue("UninstallString", "\"" + executable + "\" --product-maintenance");
        key.DeleteValue("ModifyPath", false);
        key.SetValue("URLInfoAbout", "https://github.com/yuan2go/importer-pc"); key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("Comments", "卸载不创建备份，默认保留本站数据；可选择同时删除本站数据。共享驱动保留。");
        key.Flush();
    }
    [SupportedOSPlatform("windows")]
    public static void RepairSystemRegistration(string application, string expectedHash, Action<DeploymentLocation>? repairShortcuts = null)
        => RepairSystemRegistrationCore(application, expectedHash, (location, _) => repairShortcuts?.Invoke(location), null);
    [SupportedOSPlatform("windows")]
    public static void RepairCommittedInstallation(WindowsDeploymentRequest original, string expectedHash, Action<DeploymentLocation, DeploymentLocation?> repairShortcuts)
        => RepairSystemRegistrationCore(original.ApplicationId, expectedHash, repairShortcuts, original);
    [SupportedOSPlatform("windows")]
    public static void RepairSystemRegistration(string application, string expectedHash, Action<DeploymentLocation>? repairShortcuts, MaintenanceExecution? execution)
        => RepairSystemRegistrationCore(application, expectedHash, (location, _) => repairShortcuts?.Invoke(location), null, execution);
    [SupportedOSPlatform("windows")]
    public static void RepairCommittedInstallation(WindowsDeploymentRequest original, string expectedHash, Action<DeploymentLocation, DeploymentLocation?> repairShortcuts, MaintenanceExecution? execution)
        => RepairSystemRegistrationCore(original.ApplicationId, expectedHash, repairShortcuts, original, execution);
    [SupportedOSPlatform("windows")]
    static void RepairSystemRegistrationCore(string application, string expectedHash, Action<DeploymentLocation, DeploymentLocation?> repairShortcuts, WindowsDeploymentRequest? original, MaintenanceExecution? execution = null)
    {
        execution?.Checkpoint(); execution?.Report(MaintenanceStage.Checking);
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
        void RepairProjections()
        {
            if (location.LifecycleState == "Installed") repairShortcuts(location, inspection?.Previous);
            ProjectInstalledApplication(location);
        }
        execution?.BeginCommit(MaintenanceStage.RepairingSystem);
        if (location is { LifecycleState: "Installed", Recovery.Enabled: true }) DesktopRecoverySupervisor.RepairAndReset(location, RepairProjections);
        else RepairProjections();
        if (original is not null && inspection is { Status: "CommittedNeedsSystemRepair" })
            WriteProtected(operation!, new
            {
                Status = "Completed",
                RequestIdentity = InstallationRequestIdentity(original),
                inspection.ChangePlanIdentity,
                Location = location,
                inspection.Previous,
                BusinessStarted = false,
                RepairedFromStatus = inspection.Status,
                SystemRepairArchive = archive
            }, new SecurityIdentifier(location.RuntimeAccountSid));
    }
    [SupportedOSPlatform("windows")]
    public static WindowsRemovalReceipt Remove(string application, string expectedHash, Action<DeploymentLocation> requireResponsibilitiesAndBackup,
        Action<DeploymentLocation> removeShortcuts, string[]? originalMediaFiles = null)
        => Remove(application, expectedHash, requireResponsibilitiesAndBackup, removeShortcuts, originalMediaFiles, null);
    [SupportedOSPlatform("windows")]
    public static WindowsRemovalReceipt Remove(string application, string expectedHash, Action<DeploymentLocation> requireResponsibilitiesAndBackup,
        Action<DeploymentLocation> removeShortcuts, string[]? originalMediaFiles, MaintenanceExecution? execution, bool withoutBusinessInspection = false, string? operationId = null)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        if (!ExtensionSchema.IsId(application)) throw new InvalidDataException("无效的应用身份。");
        using var guard = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Wire.FileHash(Registration(application)) != expectedHash) throw new InvalidOperationException("部署登记变化；重新检查卸载计划");
        var location = Current(application) ?? throw new InvalidDataException("没有可核验的原部署");
        using var rootOwnership = application == "sportmaster" && !withoutBusinessInspection ? ApplicationRootOwnership.AcquireMaintenance(application, location.DataRoot) : null;
        var sid = new SecurityIdentifier(location.RuntimeAccountSid);
        var operation = location.LifecycleState != "Installed" && location.MaintenanceOperation is not null ? location.MaintenanceOperation : RemovalOperationFile(application, operationId ?? Wire.Id());
        _ = TrustedProgramRoots.RequireBoundary(location.ReleaseRoot);
        RemovalState plan;
        if (location.LifecycleState == "Installed")
        {
            RequireNoUnresolvedPruning(application);
            RequireNoUnresolvedRemoval(application);
            if (File.Exists(operation)) throw new InvalidOperationException("卸载操作身份已使用，请核对原记录；不覆盖或重新执行。");
            // Removal uses the protected installation inventory. A missing or
            // damaged product executable must not prevent program-only removal.
            var files = location.MaintenanceRoot is not null ? InstalledInventory(location) :
                DeploymentMedia.Inspect(location.ReleaseRoot, application).Files.Keys.ToArray();
            requireResponsibilitiesAndBackup(location); // Caller holds application-owner until return.
            plan = new("Prepared", location, files.Append("deployment-location.json").Append("deployment-retired.json").ToArray())
            {
                SystemEntries = location.Recovery is not null ? DesktopRecoveryInstallation.Plan(location).SystemEntries : null,
                WithoutBusinessInspection = withoutBusinessInspection,
                OperationId = Path.GetFileNameWithoutExtension(operation)[(application.Length + ".remove-".Length)..],
                ExpectedRegistrationHash = expectedHash
            };
            try { WriteProtected(operation, plan, sid, overwrite: false); }
            catch (Exception error) { throw new InstallationOutcomeUnknownException("原卸载意图写入结果未确认，请先核对原记录。", error); }
        }
        else
        {
            MachineRuntimePolicy.RequireProtectedEvidence(operation);
            if (!TrustedProgramRoots.Contains(Path.Combine(MachineRoot, "deployments"), operation) || new FileInfo(operation).Length > 4 * 1024 * 1024) throw new InvalidDataException("Unsafe removal journal");
            plan = Wire.Decode<RemovalState>(File.ReadAllText(operation));
            ValidateRemovalState(plan, location);
            originalMediaFiles ??= InstalledInventory(location);
            if (!plan.Files.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(originalMediaFiles.Append("deployment-location.json").Append("deployment-retired.json").Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("原卸载清单与同源完整介质不一致，拒绝清理；原件保留");
        }
        try
        {
            execution?.Checkpoint(); execution?.BeginCommit(MaintenanceStage.RetiringDeployment);
            var retired = location with { LifecycleState = "RemovedDataRetained", MaintenanceOperation = operation };
            if (Directory.Exists(location.ReleaseRoot)) WriteProtected(Path.Combine(location.ReleaseRoot, "deployment-retired.json"), retired, sid);
            WriteProtected(Registration(application), retired, sid); // one irreversible detach; reentry reads this state
            if (application is "sportmaster" or "direct-plc" or "wcs-to-plc") ManagementModeBroker.RemoveIfUnused(location.DataRoot);
            ProjectInstalledApplication(retired); removeShortcuts(location);
            execution?.Report(MaintenanceStage.RemovingProgram);
            // New removals leave locked files as explicit leftovers. They never add a
            // reboot-delete obligation; old scheduled obligations remain observable.
            var cleanup = CleanRemovalInventory(location.ReleaseRoot, plan.Files, _ => false);
            var restart = plan.RestartRequired && plan.Files.Any(name => File.Exists(Path.Combine(location.ReleaseRoot, name)));
            var status = restart ? "RestartRequired" : cleanup.Remaining.Length == 0 ? "RemovedDataRetained" : "RemovedWithRetainedFiles";
            WriteProtected(operation, plan with { Status = status, Remaining = cleanup.Remaining, RestartRequired = restart }, sid);
            var dumpDirectory = location.Recovery is not null ? DesktopRecoveryInstallation.Plan(location).DumpFolder : null;
            return new(status, operation, [location.DataRoot, location.ConfigurationDirectory,
            .. dumpDirectory is not null && Directory.Exists(dumpDirectory) ? new[] { dumpDirectory } : Array.Empty<string>()], restart, cleanup.Remaining, location.ReleaseRoot)
            {
                WithoutBusinessInspection = plan.WithoutBusinessInspection,
                OperationId = plan.OperationId
            };
        }
        catch (Exception error) when (error is not InstallationOutcomeUnknownException)
        { throw new InstallationOutcomeUnknownException("原卸载记录已保存，结果须按原操作核对；不要再次提交。", error); }
    }
    internal static (bool RestartRequired, string[] Remaining) CleanRemovalInventory(string root, string[] files, Func<string, bool> scheduleDelete, int fileBudget = 16384,
        IReadOnlyDictionary<string, string>? reviewedHashes = null)
    {
        root = Path.GetFullPath(root); DeploymentPaths.RequireOrdinaryAncestors(root);
        if (files.Length > fileBudget || files.Any(n => !ExtensionPackages.SafePath(n)) || files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length)
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
            try
            {
                if (reviewedHashes is null) File.Delete(path);
                else DeleteReviewedPath(path, reviewedHashes[name]);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                var sharingViolation = error is IOException && (error.HResult & 0xFFFF) is 32 or 33;
                if (sharingViolation && scheduleDelete(path)) restart = true;
                remaining.Add(Path.GetRelativePath(root, path));
            }
        }
        // Only empty, inventoried directories; no recursive delete, no shared drivers,
        // no other releases, policies, data, package consumers or arbitrary extra files.
        var directories = files.Select(n => Path.GetDirectoryName(Path.Combine(root, n))!).SelectMany(p => Parents(p, root)).Append(root).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(p => p.Length);
        foreach (var directory in directories)
        {
            DeploymentPaths.RequireOrdinaryAncestors(directory);
            if (!Directory.Exists(directory)) continue;
            try
            {
                if (reviewedHashes is null) Directory.Delete(directory, false);
                else DeleteReviewedPath(directory, null);
            }
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
        var original = Current(application) ?? throw new InvalidDataException("Missing original deployment");
        using var recovery = PrepareRecoveryMigration(original, newDataRoot, expectedRegistrationHash);
        SwitchDataRoot(application, expectedRegistrationHash, newDataRoot, handoffHash, recovery);
    }
    [SupportedOSPlatform("windows")]
    public static void SwitchDataRoot(string application, string expectedRegistrationHash, string newDataRoot, string handoffHash, DesktopRecoveryMigration recovery)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        if (!ExtensionSchema.IsHash(handoffHash)) throw new InvalidDataException("Missing restore handoff identity");
        DeploymentPaths.RequireOrdinaryAncestors(newDataRoot);
        recovery.RequireRegistration(Registration(application), expectedRegistrationHash);
        if (Wire.FileHash(Registration(application)) != expectedRegistrationHash) throw new InvalidOperationException("原部署在接管前变化；原根围栏保留，重新核对后继续");
        var current = Current(application) ?? throw new InvalidDataException("原机部署登记缺失");
        using var rootOwnership = application == "sportmaster" ? ApplicationRootOwnership.AcquireMaintenance(application, current.DataRoot) : null;
        if (current.LifecycleState != "Installed") throw new InvalidOperationException("只有已安装原机部署可接管恢复根");
        recovery.Require(current, newDataRoot);
        PreviewDataAccess(newDataRoot, current.RuntimeAccountSid);
        var sid = new SecurityIdentifier(current.RuntimeAccountSid);
        var operation = Path.Combine(MachineRoot, "deployments", application + ".migration-" + Wire.Id() + ".json");
        var next = current with { DataRoot = Path.GetFullPath(newDataRoot), MaintenanceOperation = operation, RestoreHandoffHash = handoffHash };
        WriteProtected(operation, new { Status = "Prepared", Previous = current, Next = next, HandoffSha256 = handoffHash, SupervisionLedgerSha256 = recovery.LedgerSha256 }, sid);
        if (application is "sportmaster" or "direct-plc" or "wcs-to-plc")
        {
            ManagementModeBroker.CopyCredentialsForMigration(current.DataRoot, newDataRoot);
            ManagementModeBroker.Install(newDataRoot, Path.Combine(current.ReleaseRoot, "app"));
        }
        WriteProtected(Path.Combine(current.ReleaseRoot, "deployment-location.json"), next, sid);
        WriteProtected(Registration(application), next, sid);
        ProjectInstalledApplication(next);
        if (application is "sportmaster" or "direct-plc" or "wcs-to-plc") ManagementModeBroker.RemoveIfUnused(current.DataRoot);
        WriteProtected(operation, new { Status = "CompletedIsolated", Previous = current, Next = next, HandoffSha256 = handoffHash, SupervisionLedgerSha256 = recovery.LedgerSha256 }, sid);
    }
}
