using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace Ipc.Platform;

public sealed record WindowsDeploymentRequest(string MediaRoot, string ApplicationId, string SourceCommit,
    string RuntimeAccountSid, string DataRoot, string ConfigurationSource, RunMode Mode,
    string? TrustFile, string? TrustSha256, Dictionary<string, string> Packages, bool RepairSeed = false,
    Dictionary<string, string>? ConfigurationHashes = null, string? ProgramRoot = null, string? ExpectedRegistrationHash = null,
    string? ChangePlanIdentity = null, string? BackupDestination = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] DeploymentShortcutOptions? Shortcuts = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? OperationId = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DesktopRecoveryOptions? Recovery { get; init; }
}

public static partial class WindowsDeploymentSetup
{
    public static string MachineRoot => WindowsStoragePaths.MachineRoot;
    public static string Registration(string application) => Path.Combine(MachineRoot, "deployments", application + ".json");
    public static DeploymentLocation? Current(string application)
    {
        var file = Registration(application);
        try { _ = File.GetAttributes(file); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        MachineRuntimePolicy.RequireProtectedEvidence(file);
        var current = Wire.Decode<DeploymentLocation>(File.ReadAllText(file));
        if (current.ApplicationId != application) throw new InvalidDataException("Existing deployment identity differs");
        return current;
    }
    public static ExtensionTrust ReadTrust(string path, RunMode mode)
    {
        DeploymentPaths.RequireOrdinaryAncestors(path);
        if (new FileInfo(path).Length > ExtensionPackages.MaximumManifestBytes) throw new InvalidDataException("Trust policy quota exceeded");
        var text = File.ReadAllText(path);
        if (text.Contains("PRIVATE KEY", StringComparison.Ordinal)) throw new InvalidDataException("Private keys must not be deployed");
        var trust = Wire.Decode<ExtensionTrust>(text);
        if (trust.SchemaVersion != 1 || trust.Publishers is not { Length: > 0 } || trust.Publishers.Length > 128 ||
            trust.Publishers.Select(p => p.Id).Distinct().Count() != trust.Publishers.Length) throw new InvalidDataException("Invalid independent trust policy");
        foreach (var publisher in trust.Publishers)
        {
            if (!ExtensionSchema.IsId(publisher.Id) || mode == RunMode.Production && publisher.TestOnly || !publisher.PublicKeyPem.StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal))
                throw new InvalidDataException("Invalid publisher or Production test policy");
            using var rsa = RSA.Create(); rsa.ImportFromPem(publisher.PublicKeyPem);
            if (rsa.KeySize < 3072) throw new InvalidDataException("Publisher RSA key must be at least 3072 bits");
        }
        return trust;
    }
    [SupportedOSPlatform("windows")]
    public static void PreviewDataAccess(string dataRoot, string runtimeSid)
    {
        RequireDataStorage(dataRoot);
        var data = Path.GetFullPath(dataRoot);
        if (!Directory.Exists(data)) return; // Creation uses an explicit runtime-Modify ACL.
        var acl = new DirectoryInfo(data).GetAccessControl(); var granted = false;
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            var sid = ((SecurityIdentifier)rule.IdentityReference).Value;
            if ((rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;
            if (rule.AccessControlType == AccessControlType.Deny && (sid is "S-1-1-0" or "S-1-5-11" || sid == runtimeSid))
                throw new UnauthorizedAccessException("原数据目录含指定账户/公共主体的拒绝规则；保留原ACL，由现场维护核对实际令牌");
            if (rule.AccessControlType == AccessControlType.Allow && sid == runtimeSid && (rule.FileSystemRights & FileSystemRights.Modify) == FileSystemRights.Modify) granted = true;
        }
        if (!granted) throw new UnauthorizedAccessException("不能静态证明指定非提权账户可修改原数据目录；保留原数据/ACL，先核对账户权限或使用完整恢复迁移：" + data);
        var systemRights = (FileSystemRights)0;
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if ((rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;
            var subject = ((SecurityIdentifier)rule.IdentityReference).Value;
            if (subject is not ("S-1-5-18" or "S-1-5-32-544" or "S-1-1-0" or "S-1-5-11")) continue;
            if (rule.AccessControlType == AccessControlType.Deny && (rule.FileSystemRights & FileSystemRights.Modify) != 0)
                throw new UnauthorizedAccessException("数据目录拒绝 SYSTEM 服务所需访问；保留原 ACL，核对该拒绝规则后再安装：" + data);
            if (rule.AccessControlType == AccessControlType.Allow) systemRights |= rule.FileSystemRights;
        }
        if ((systemRights & FileSystemRights.Modify) != FileSystemRights.Modify)
            throw new UnauthorizedAccessException("不能静态证明 SYSTEM 授权服务可访问数据目录；保留原 ACL，先核对服务权限：" + data);
    }
    [SupportedOSPlatform("windows")]
    public static void PrepareRestoreDataRoot(string dataRoot)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var policyFile = Path.Combine(MachineRoot, "runtime-policy.json"); MachineRuntimePolicy.RequireProtectedEvidence(policyFile);
        var sid = new SecurityIdentifier(Wire.Decode<MachineRuntimePolicy>(File.ReadAllText(policyFile)).RuntimeAccountSid);
        PreviewDataAccess(dataRoot, sid.Value);
        if (!Directory.Exists(dataRoot)) new DirectoryInfo(dataRoot).Create(MachineRuntimeSetup.DirectoryAcl(sid, FileSystemRights.Modify));
        PreviewDataAccess(dataRoot, sid.Value);
    }
    [SupportedOSPlatform("windows")]
    public static void PrepareInitialDataRoot(string dataRoot, string runtimeSid)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority(); PreviewDataAccess(dataRoot, runtimeSid);
        MachineRuntimeSetup.InstallForAccount(runtimeSid);
        if (!Directory.Exists(dataRoot)) new DirectoryInfo(dataRoot).Create(MachineRuntimeSetup.DirectoryAcl(new SecurityIdentifier(runtimeSid), FileSystemRights.Modify));
        PreviewDataAccess(dataRoot, runtimeSid);
    }
    [SupportedOSPlatform("windows")]
    public static DeploymentLocation Install(WindowsDeploymentRequest request, Action<WindowsDeploymentRequest> validateConfiguration,
        Action<WindowsDeploymentRequest> installPackages, Action<DeploymentLocation> createShortcuts, Action<WindowsInstallationStage>? progress = null)
        => Install(request,validateConfiguration,installPackages,createShortcuts,progress,null);
    [SupportedOSPlatform("windows")]
    public static DeploymentLocation Install(WindowsDeploymentRequest request, Action<WindowsDeploymentRequest> validateConfiguration,
        Action<WindowsDeploymentRequest> installPackages, Action<DeploymentLocation> createShortcuts, Action<WindowsInstallationStage>? progress, MaintenanceExecution? execution)
    {
        execution?.Checkpoint();execution?.Report(MaintenanceStage.Checking);
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        RequireCurrentInstallationRequest(request);
        request.Recovery?.Validate();
        var media = DeploymentMedia.Inspect(request.MediaRoot,request.ApplicationId,execution?.Token??CancellationToken.None);
        if (media.SourceCommit != request.SourceCommit) throw new InvalidDataException("Selected media changed");
        validateConfiguration(request);
        var sourceFiles = ConfigurationFiles(request.ConfigurationSource);
        if (request.ConfigurationHashes is null || !sourceFiles.Select(Path.GetFileName).Order().SequenceEqual(request.ConfigurationHashes.Keys.Order()) ||
            sourceFiles.Any(path => !request.ConfigurationHashes.TryGetValue(Path.GetFileName(path), out var hash) || Wire.FileHash(path) != hash))
            throw new InvalidDataException("Selected seed configuration changed after review; read and review it again");
        var sid = new SecurityIdentifier(request.RuntimeAccountSid);
        if (!sid.IsAccountSid() && request.RuntimeAccountSid != MachineRuntimePolicy.SharedUsersSid) throw new InvalidDataException("Choose an existing Windows account");
        _ = sid.Translate(typeof(NTAccount));
        var data = Path.GetFullPath(request.DataRoot); DeploymentPaths.RequireOrdinaryAncestors(data);
        PreviewDataAccess(data, request.RuntimeAccountSid);
        var vendor = TrustedProgramRoots.Normalize(request.ProgramRoot ?? TrustedProgramRoots.Default, data);
        if (data.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Data must be outside Program Files");
        if (request.TrustFile is { } trust)
        {
            if (Wire.FileHash(trust) != request.TrustSha256) throw new InvalidDataException("Selected independent trust policy changed");
            _ = ReadTrust(trust, request.Mode);
        }
        if(execution is {Committing:false}) execution.BeginCommit(MaintenanceStage.InstallingMachine);
        else execution?.Report(MaintenanceStage.InstallingMachine);
        MachineRuntimeSetup.InstallForAccount(request.RuntimeAccountSid);
        TrustedProgramRoots.Approve(vendor, data, sid); ProtectedDirectory(Path.Combine(vendor, "releases"), sid);
        ProtectedDirectory(Path.Combine(MachineRoot, "deployments"), sid);
        using var setup = new FileStream(Path.Combine(MachineRoot, "deployments", request.ApplicationId + ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var before = Current(request.ApplicationId);
        if ((before is null ? null : Wire.FileHash(Registration(request.ApplicationId))) != request.ExpectedRegistrationHash)
            throw new InvalidOperationException("部署登记在计划审阅后变化；重新审阅，不覆盖并行安装");
        if (before is not null && (!string.Equals(Path.GetFullPath(before.DataRoot), data, StringComparison.OrdinalIgnoreCase) || before.RuntimeAccountSid != request.RuntimeAccountSid))
            throw new InvalidDataException("升级保留原账户和数据根；位置/账户迁移须走完整恢复流程");
        if (before is not null && (before.Recovery?.Enabled == true || request.Recovery?.Enabled == true))
            DesktopRecoverySupervisor.RequireExistingState(before);
        var requiredBytes = checked(media.Files.Keys.Sum(n => new FileInfo(Path.Combine(media.Root, n)).Length) + 67108864);
        if (new DriveInfo(Path.GetPathRoot(vendor)!).AvailableFreeSpace < requiredBytes) throw new IOException("程序目标卷空间不足");
        var operation = request.OperationId is null ? Path.Combine(MachineRoot, "deployments", request.ApplicationId + ".setup-" + Wire.Id() + ".json") : InstallationOperationFile(request);
        if (File.Exists(operation)) throw new InvalidOperationException("原安装操作已存在；只能核对原回执，不能重复提交");
        var releaseRoot = Path.Combine(vendor, "releases", request.ApplicationId + "-" + media.SourceCommit[..12] + "-" + Wire.Id());
        try
        {
            WriteProtected(operation, new { Status = "Prepared", RequestIdentity = InstallationRequestIdentity(request), request.ChangePlanIdentity, request.SourceCommit, Previous = before, ReleaseRoot = releaseRoot, DataRoot = data }, sid);
            progress?.Invoke(WindowsInstallationStage.CopyingFiles);
            media.CopyTo(releaseRoot, root => ProtectedDirectory(root, sid), file => new FileInfo(file).SetAccessControl(MachineRuntimeSetup.FileAcl(sid, FileSystemRights.ReadAndExecute)));
            progress?.Invoke(WindowsInstallationStage.PreparingConfiguration);
            ProtectedDirectory(Path.Combine(MachineRoot, "configuration"), sid);
            var config = request.RepairSeed ? Path.Combine(MachineRoot, "configuration", request.ApplicationId + "-" + Wire.Id()) :
                before?.ConfigurationDirectory ?? Path.Combine(MachineRoot, "configuration", request.ApplicationId);
            if (!Directory.Exists(config))
            {
                // Publish a complete seed directory once; upgrades retain it byte for byte.
                var temporary = config + "." + Wire.Id() + ".new"; ProtectedDirectory(temporary, sid);
                foreach (var source in ConfigurationFiles(request.ConfigurationSource))
                {
                    var target = Path.Combine(temporary, Path.GetFileName(source)); File.Copy(source, target, false);
                    new FileInfo(target).SetAccessControl(MachineRuntimeSetup.FileAcl(sid));
                    if (Wire.FileHash(target) != request.ConfigurationHashes[Path.GetFileName(source)]) throw new InvalidDataException("Configuration source changed while copying");
                }
                Directory.Move(temporary, config);
            }
            else { MachineRuntimePolicy.RequireProtectedEvidence(Path.Combine(config, "business.json")); validateConfiguration(request with { ConfigurationSource = config }); }

            if (!Directory.Exists(data)) new DirectoryInfo(data).Create(MachineRuntimeSetup.DirectoryAcl(sid, FileSystemRights.Modify));
            if (request.TrustFile is not null) ProvisionTrust(request, sid);
            progress?.Invoke(WindowsInstallationStage.InstallingComponents);
            installPackages(request);
            var location = new DeploymentLocation(request.ApplicationId, media.SourceCommit, releaseRoot, config, data, request.RuntimeAccountSid,
                RestoreHandoffHash: before?.RestoreHandoffHash, DeploymentPlanIdentity: Wire.Hash(Wire.Encode(request)), Shortcuts: request.Shortcuts)
                { Recovery = request.Recovery ?? (request.ApplicationId is "sportmaster" or "direct-plc" or "wcs-to-plc" ? new() : null) };
            if (before is null && request.ExpectedRegistrationHash is null && location.Recovery is not null)
                DesktopRecoverySupervisor.InitializeNewDeployment(location);
            WriteProtected(Path.Combine(releaseRoot, "deployment-location.json"), location, sid);
            MachineRuntimePolicy.RequireProtectedApplicationInstaller(Path.Combine(releaseRoot, "app"), ["Ipc.Assembly.exe", "Ipc.Assembly.dll", "Ipc.Platform.dll"]);
            progress?.Invoke(WindowsInstallationStage.RegisteringManagementService);
            if (request.ApplicationId is "sportmaster" or "direct-plc" or "wcs-to-plc") ManagementModeBroker.Install(data, Path.Combine(releaseRoot, "app"),
                entry => AppendServiceDiagnostic(operation + ".service.jsonl", entry, sid));
            WriteProtected(operation, new { Status = "ReadyToCommit", RequestIdentity = InstallationRequestIdentity(request), request.ChangePlanIdentity, Location = location, Previous = before, BusinessStarted = false }, sid);
            progress?.Invoke(WindowsInstallationStage.CommittingInstallation);
            WriteProtected(Registration(request.ApplicationId), location, sid); // Sole authoritative deployment commit.
            // Stable desktop entry is a repairable projection of the committed deployment.
            // No failed pre-commit installation can switch the user's existing entry.
            progress?.Invoke(WindowsInstallationStage.CreatingShortcuts);
            createShortcuts(location);
            // ARP is a repairable projection. A crash here is recovered from this
            // registration, never by choosing a registry value as a second authority.
            progress?.Invoke(WindowsInstallationStage.RegisteringApplication);
            ProjectInstalledApplication(location);
            WriteProtected(operation, new { Status = "Completed", RequestIdentity = InstallationRequestIdentity(request), request.ChangePlanIdentity, Location = location, BusinessStarted = false }, sid);
            return location;
        }
        catch (Exception error)
        {
            var committed = Current(request.ApplicationId)?.ReleaseRoot == releaseRoot;
            try { WriteProtected(operation, new { Status = committed ? "CommittedNeedsSystemRepair" : "Failed", RequestIdentity = InstallationRequestIdentity(request), request.ChangePlanIdentity, ErrorCode = error.GetType().Name,
                Error = WindowsInstallationError.Capture(error), ServiceDiagnosticFile = operation + ".service.jsonl", Previous = before, ReleaseRoot = releaseRoot, DataRoot = data,
                Retained = "Previous deployment, seed, database and leases retained; retry creates another side-by-side release" }, sid); }
            catch (Exception receiptFailure)
            {
                throw new InstallationOutcomeUnknownException("安装失败且原失败回执未能保存；保留原操作，先只读核对。原错误：" + WindowsInstallationError.Safe(error.Message) +
                    "；回执错误：" + WindowsInstallationError.Safe(receiptFailure.Message), new AggregateException(error, receiptFailure));
            }
            throw;
        }
    }
    [SupportedOSPlatform("windows")]
    static void AppendServiceDiagnostic(string path, ManagementServiceInstallationDiagnostic entry, SecurityIdentifier sid)
    {
        DeploymentPaths.RequireOrdinaryAncestors(path);
        if (File.Exists(path)) MachineRuntimePolicy.RequireProtectedEvidence(path);
        using var output = new FileInfo(path).Create(FileMode.Append, FileSystemRights.AppendData, FileShare.Read, 4096,
            FileOptions.WriteThrough, MachineRuntimeSetup.FileAcl(sid, FileSystemRights.ReadAndExecute));
        if (output.Length > 1024 * 1024) throw new IOException("本次服务安装诊断超过1MiB预算：" + path);
        output.Write(System.Text.Encoding.UTF8.GetBytes(DiagnosticText.Protect(Wire.Encode(entry)) + "\n")); output.Flush(true);
    }
    [SupportedOSPlatform("windows")]
    static void WriteProtected<T>(string path, T value, SecurityIdentifier sid)
        => WriteProtectedBytes(path, System.Text.Encoding.UTF8.GetBytes(Wire.Encode(value)), sid);
    [SupportedOSPlatform("windows")]
    static void WriteProtectedBytes(string path, byte[] bytes, SecurityIdentifier sid, bool overwrite = true)
    {
        DeploymentPaths.RequireOrdinaryAncestors(path);
        var temporary = path + "." + Wire.Id() + ".new";
        using (var output = new FileInfo(temporary).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.WriteThrough, MachineRuntimeSetup.FileAcl(sid)))
        { output.Write(bytes); output.Flush(true); }
        File.Move(temporary, path, overwrite);
    }
    public static string[] ConfigurationFiles(string root)
    {
        root = Path.GetFullPath(root); DeploymentPaths.RequireOrdinaryAncestors(root);
        var files = Directory.GetFiles(root);
        if (!files.Any(f => Path.GetFileName(f) == "business.json") || files.Length > 128 || Directory.GetDirectories(root).Length != 0)
            throw new InvalidDataException("Choose a flat Application configuration directory containing business.json");
        foreach (var path in files)
        {
            DeploymentPaths.RequireOrdinaryAncestors(path);
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || new FileInfo(path).Length > 4 * 1024 * 1024 || Path.GetFileName(path) == "extension-store.json")
                throw new InvalidDataException("Configuration contains unapproved file or portable extension overrides: " + path);
        }
        return files;
    }
    [SupportedOSPlatform("windows")]
    static void ProtectedDirectory(string path, SecurityIdentifier sid)
    {
        DeploymentPaths.RequireOrdinaryAncestors(path);
        if (Directory.Exists(path)) { MachineRuntimePolicy.RequireProtectedAcl(new DirectoryInfo(path), null); return; }
        new DirectoryInfo(path).Create(MachineRuntimeSetup.DirectoryAcl(sid, FileSystemRights.ReadAndExecute));
        MachineRuntimePolicy.RequireProtectedAcl(new DirectoryInfo(path), null);
    }
    [SupportedOSPlatform("windows")]
    static void ProvisionTrust(WindowsDeploymentRequest request, SecurityIdentifier sid)
    {
        var target = Path.Combine(MachineRoot, "extension-trust." + request.Mode + ".json");
        if (File.Exists(target))
        {
            MachineRuntimePolicy.RequireProtectedEvidence(target);
            if (Wire.FileHash(target) != request.TrustSha256) throw new InvalidDataException("Existing trust retained; trust rotation needs a separately reviewed policy");
            return;
        }
        ProtectedDirectory(Path.Combine(MachineRoot, "extensions"), sid);
        var store = Path.Combine(MachineRoot, "extensions", request.Mode.ToString()); ProtectedDirectory(store, sid);
        ProtectedDirectory(Path.Combine(store, "usage"), sid);
        var fence = Path.Combine(store, "usage", "consumers.lease");
        if (!File.Exists(fence))
        {
            var legacy = Directory.Exists(Path.Combine(store, "registrations")) && Directory.EnumerateFiles(Path.Combine(store, "registrations")).Any();
            File.WriteAllText(fence, legacy ? "LegacyConsumersUnverified: offline consumer scope predates registration; uninstall refused" : "Extension consumer registration and inspection fence v1");
        }
        var temporary = target + "." + Wire.Id() + ".new";
        File.Copy(request.TrustFile!, temporary, false);
        new FileInfo(temporary).SetAccessControl(MachineRuntimeSetup.FileAcl(sid));
        if (Wire.FileHash(temporary) != request.TrustSha256) throw new InvalidDataException("Trust bytes changed");
        File.Move(temporary, target, false);
    }
}
