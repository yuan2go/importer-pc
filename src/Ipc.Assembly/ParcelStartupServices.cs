using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Runtime.InteropServices;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;

namespace Ipc.Assembly;

public sealed record StartupMaintenanceRequest(string Action, BusinessLaunch? Launch = null, string? ExpectedHash = null,
    ParcelConfiguration? Candidate = null, string? Reason = null, string? Backup = null, string? Destination = null,
    WindowsDeploymentRequest? Deployment = null, string? Pipe = null, StartupEditorState? Editor = null, string? DraftHash = null, string? ChangePlanIdentity = null, ManagementModeDelegation? ManagementGrant = null,
    string? ManagementPassword = null);

// Composition entry points for installation and offline maintenance; never creates IBusinessRuntime.
public static class ParcelStartupServices
{
    public static IManagementMode ManagementMode(BusinessLaunch launch) => new ParcelManagementMode(launch.DataRoot,
        async password => { _ = await ElevateAsync(new("ConfigureManagementPassword", Launch: launch, ManagementPassword: password)); });
    static readonly AsyncLocal<Action?> delegatedAuthority = new();
    static readonly AsyncLocal<string?> delegatedPrincipal = new();
    static bool RequiresManagementMode(string action) => action is "Stage" or "RepairInitial" or "SaveDraft" or "PlanChange" or "PrepareChange" or "Backup" or "Restore" or "AdoptRestore";
    static string ManagementRequestHash(StartupMaintenanceRequest r) => Wire.Hash(Wire.Encode(r with { ManagementGrant = null }));
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
    public static void CreateSiteSeed(DeploymentMedia media, string destination, string station, RunMode mode)
    {
        if (!ExtensionSchema.IsId(station) || !Enum.IsDefined(mode)) throw new InvalidDataException("填写合法新工位标识并明确选择Simulation/Production");
        var verified = DeploymentMedia.Inspect(media.Root, ApplicationId);
        if (verified.SourceCommit != media.SourceCommit) throw new InvalidDataException("通用首配模板介质发生变化");
        var source = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(media.Root, "app", "config", "business.json")));
        var c = (mode == RunMode.Production ? ParcelAssembly.Definition().InitialConfiguration() : source) with { StationId = station, Mode = mode };
        var errors = ParcelAssembly.Definition().Validate(c); if (errors.Length != 0 && !IsInitialDraft(c)) throw new InvalidDataException(string.Join("; ", errors));
        DeploymentPaths.RequireOrdinaryAncestors(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("新站种子暂存目标须为新目录");
        Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "business.json"), Wire.Encode(c));
    }
    static bool IsInitialDraft(ParcelConfiguration candidate) => candidate.Mode == RunMode.Production &&
        Wire.Encode(candidate with { StationId = SystemParameterDefaults.InitialStationId }) == Wire.Encode(ParcelAssembly.Definition().InitialConfiguration());
    public static string ApplicationId => ParcelAssembly.Definition().Id;
    public static Task<UnusedSimulationRelocation> CompleteUnusedSimulationRelocationAsync(string original, string expectedOriginHash)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var current = WindowsDeploymentSetup.Current(ApplicationId) ?? throw new InvalidDataException("缺少原安装登记。");
        return Core(new(current.ConfigurationDirectory, current.DataRoot, null, null), installer: true).CompleteUnusedSimulationRelocationAsync(original, expectedOriginHash);
    }
    public static string DefaultRuntimeAccount(bool currentUserOnly = false)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var policy = Path.Combine(WindowsDeploymentSetup.MachineRoot, "runtime-policy.json");
        if (!currentUserOnly && File.Exists(policy))
        {
            MachineRuntimePolicy.RequireProtectedEvidence(policy);
            return ((NTAccount)new SecurityIdentifier(Wire.Decode<MachineRuntimePolicy>(File.ReadAllText(policy)).RuntimeAccountSid).Translate(typeof(NTAccount))).Value;
        }
        using var identity = WindowsIdentity.GetCurrent(); return identity.Name;
    }
    public static WindowsDeploymentRequest Review(DeploymentMedia media, string account, string dataRoot, string configurationSource,
        string? trustFile, string[] packages, bool repairSeed, string? programRoot = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        account = account.Trim();
        var sid = account.StartsWith("S-1-", StringComparison.Ordinal) ? new SecurityIdentifier(account) : (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));
        _ = sid.Translate(typeof(NTAccount));
        var source = Path.GetFullPath(configurationSource);
        var files = WindowsDeploymentSetup.ConfigurationFiles(source);
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(source, "business.json")));
        var trust = string.IsNullOrWhiteSpace(trustFile) ? null : Path.GetFullPath(trustFile);
        if (trust is not null) _ = WindowsDeploymentSetup.ReadTrust(trust, seed.Mode);
        var data = Path.GetFullPath(dataRoot);
        using var caller = WindowsIdentity.GetCurrent();
        if ((caller.User?.Value != sid.Value || sid.Value == MachineRuntimePolicy.SharedUsersSid) && data.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(data)) throw new InvalidDataException("其他运行账户无法保证访问当前用户的原数据；保留原根，选择原运行账户或先完整恢复到共享卷。");
            throw new InvalidDataException("所选目录属于当前维护账户的用户目录，不能保证指定运行账户访问；请明确选择受支持的共享数据目录。不会自动改道。");
        }
        var request = new WindowsDeploymentRequest(media.Root, ApplicationId, media.SourceCommit, sid.Value, data, source, seed.Mode, trust,
            trust is null ? null : Wire.FileHash(trust), packages.ToDictionary(p => p, Wire.FileHash), repairSeed,
            files.ToDictionary(p => Path.GetFileName(p), Wire.FileHash), TrustedProgramRoots.Normalize(programRoot ?? TrustedProgramRoots.Default, data),
            File.Exists(WindowsDeploymentSetup.Registration(ApplicationId)) ? Wire.FileHash(WindowsDeploymentSetup.Registration(ApplicationId)) : null);
        ValidateSeed(request);
        TrustedProgramRoots.Preview(request.ProgramRoot!, request.DataRoot);
        WindowsDeploymentSetup.PreviewDataAccess(request.DataRoot, request.RuntimeAccountSid);
        return request;
    }
    public static bool IsRuntimeAccount(DeploymentLocation location)
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = WindowsIdentity.GetCurrent(); return MachineRuntimePolicy.MatchesRuntimeAccount(identity.User?.Value, location.RuntimeAccountSid, identity.Claims);
    }
    public static IStartupMaintenance Maintenance(BusinessLaunch launch) => new ElevatedMaintenance(launch);
    internal static async Task ExecuteBatchInstall(WindowsDeploymentRequest request) => _ = await ExecuteAsync(new("Install", Deployment: request));
    public static async Task RepairRegistrationAsync(string expectedRegistrationHash) => _ = await ElevateAsync(new("RepairSystem", ExpectedHash: expectedRegistrationHash));
    public static async Task<StartupChangePreview> ReviewDeploymentAsync(WindowsDeploymentRequest request, string destination) =>
        Wire.Decode<StartupChangePreview>(await ElevateAsync(new("ReviewDeployment", Destination: destination, Deployment: request)) ?? throw new IOException("缺少原部署预检回执"));
    public static async Task<WindowsRemovalReceipt> RemoveAsync(string expectedRegistrationHash, string plan, string backup) =>
        Wire.Decode<WindowsRemovalReceipt>(await ElevateAsync(new("Uninstall", ExpectedHash: expectedRegistrationHash, Destination: backup, ChangePlanIdentity: plan)) ?? throw new IOException("缺少卸载回执"));
    public static async Task<WindowsRemovalReceipt> ResumeRemovalAsync(DeploymentMedia media, string expectedRegistrationHash)
    {
        var current = WindowsDeploymentSetup.Current(ApplicationId) ?? throw new InvalidDataException("缺少退役登记");
        if (media.SourceCommit != current.SourceCommit) throw new InvalidDataException("继续固定清理须使用卸载前同一完整介质；从关于/原回执核对源码身份");
        var request = new WindowsDeploymentRequest(media.Root, ApplicationId, media.SourceCommit, current.RuntimeAccountSid, current.DataRoot, current.ConfigurationDirectory, RunMode.Simulation, null, null, []);
        return Wire.Decode<WindowsRemovalReceipt>(await ElevateAsync(new("ResumeRemoval", ExpectedHash: expectedRegistrationHash, Deployment: request)) ?? throw new IOException("缺少清理回执"));
    }
    static ParcelStartupMaintenance Core(BusinessLaunch launch, bool installer = false) => new(launch, ParcelAssembly.Definition(), () => new ParcelCapabilityFactory(launch,
        BusinessCompatibility.Read(ParcelAssembly.Definition().GetType().Assembly), ParcelAssembly.Definition().GetType().Assembly, installer), installer ? () => { MachineRuntimePolicy.RequireExtensionInstallerAuthority(); delegatedAuthority.Value?.Invoke(); } : () => { LocalManagementMode.ForRoot(launch.DataRoot).Require(); }, () => delegatedPrincipal.Value ?? LocalManagementMode.ForRoot(launch.DataRoot).Evidence?.AuditActor);
    public static async Task<DeploymentLocation> InstallAsync(WindowsDeploymentRequest request)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows setup requires Windows");
        await ElevateAsync(new("Install", Deployment: request));
        return InstalledReceipt(request);
    }
    public static async Task<DeploymentLocation> InstallWithPreflightAsync(WindowsDeploymentRequest request, IProgress<WindowsInstallationStage>? progress = null)
    {
        if (request.ChangePlanIdentity is not null) throw new InvalidDataException("交互安装由同一次授权检查原责任，不能传入旧计划");
        var identity = await ElevateAsync(new("InstallWithPreflight", Deployment: request), progress);
        if (identity is not null && !ExtensionSchema.IsHash(identity)) throw new InstallationOutcomeUnknownException("原部署计划回执无效，请先核对安装记录。");
        progress?.Report(WindowsInstallationStage.VerifyingReceipt);
        try { return InstalledReceipt(request with { ChangePlanIdentity = identity }); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { throw new InstallationOutcomeUnknownException("安装进程已返回，但安装登记未核对成功。请检查安装记录，勿直接重复安装。", error); }
    }
    static DeploymentLocation InstalledReceipt(WindowsDeploymentRequest request)
    {
        var location = WindowsDeploymentSetup.Current(request.ApplicationId) ?? throw new InvalidDataException("No installed deployment receipt");
        if (location.LifecycleState != "Installed" || location.SourceCommit != request.SourceCommit || location.RuntimeAccountSid != request.RuntimeAccountSid ||
            location.Shortcuts != request.Shortcuts || location.Recovery != (request.Recovery ?? new DesktopRecoveryOptions()) || location.DeploymentPlanIdentity != Wire.Hash(Wire.Encode(request)) ||
            !string.Equals(Path.GetFullPath(location.DataRoot), Path.GetFullPath(request.DataRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installation receipt differs from the selected deployment");
        return location;
    }
    public static bool Handle(string[] args)
    {
        if (args.Length == 0 || args[0] != "--startup-command") return false;
        if (args.Length != 3 || !ExtensionSchema.IsHash(args[2])) throw new ArgumentException("Expected fixed maintenance request and SHA256");
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Privileged helper requires Windows");
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        DeploymentPaths.RequireOrdinaryAncestors(args[1]);
        using var stream = new FileStream(args[1], FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 8 * 1024 * 1024) throw new InvalidDataException("Maintenance request quota exceeded");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); var bytes = buffer.ToArray();
        if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) != args[2]) throw new InvalidDataException("Maintenance request changed");
        var request = Wire.Decode<StartupMaintenanceRequest>(ProtectedDraftPayload.Read(args[1]));
        if (request.Pipe is not { Length: 64 } || request.Pipe.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException("Invalid maintenance receipt channel");
        using var pipe = new NamedPipeClientStream(".", request.Pipe, PipeDirection.Out);
        pipe.Connect(10000);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };
        void Report(WindowsInstallationStage stage)
        {
            WriteInstallationProgress(writer, stage);
        }
        string? failure = null, result = null;
        try {
            if (RequiresManagementMode(request.Action))
            {
                var authority = LocalManagementMode.ForRoot(request.Launch?.DataRoot ?? throw new InvalidDataException("Missing administrator root"));
                var grant = request.ManagementGrant ?? throw new UnauthorizedAccessException("管理模式授权缺失，Windows资格不能代替管理密码。");
                var requestHash = ManagementRequestHash(request); authority.ValidateDelegation(grant, requestHash, true);
                delegatedPrincipal.Value = "management-mode." + grant.SessionId;
                delegatedAuthority.Value = () => authority.ValidateDelegation(grant, requestHash, false);
            }
            if (!MediaCommand(request.Action)) RequireProtectedHelper(AppContext.BaseDirectory); result = ExecuteAsync(request, Report).GetAwaiter().GetResult(); }
        catch (Exception ex) { failure = ex.Message; Environment.ExitCode = 2; }
        finally { delegatedAuthority.Value = null; delegatedPrincipal.Value = null; }
        writer.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Status = failure is null ? "Completed" : "Failed", Error = failure is null ? null : failure[..Math.Min(failure.Length, 4000)], Result = result }));
        return true;
    }
    static async Task<string?> ExecuteAsync(StartupMaintenanceRequest request, Action<WindowsInstallationStage>? progress = null)
    {
        switch (request.Action)
        {
            case "ConfigureManagementPassword":
                LocalManagementMode.ForRoot(request.Launch?.DataRoot ?? throw new InvalidDataException("管理模式数据目录缺失。")).ConfigurePassword(request.ManagementPassword ?? "");
                break;
            case "InstallWithPreflight":
                progress?.Invoke(WindowsInstallationStage.CheckingResponsibilities);
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                var candidate = request.Deployment ?? throw new InvalidDataException("Missing deployment");
                if (candidate.ChangePlanIdentity is not null) throw new InvalidDataException("Interactive installation refuses a stale supplied plan");
                RequireMatchingMedia(candidate); _ = DeploymentMedia.Inspect(candidate.MediaRoot, ApplicationId); ValidateSeed(candidate);
                var candidateSeed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(candidate.ConfigurationSource, "business.json")));
                if (WindowsDeploymentSetup.Current(ApplicationId) is not null || File.Exists(Path.Combine(candidate.DataRoot, candidateSeed.Mode.ToString(), candidateSeed.StationId, ApplicationId, ApplicationId + ".db")))
                {
                    if (candidate.BackupDestination is null) throw new InvalidDataException("升级必须指定新的完整备份目标");
                    var preview = Wire.Decode<StartupChangePreview>((await ExecuteAsync(new("ReviewDeployment", Deployment: candidate, Destination: candidate.BackupDestination)))!);
                    if (!preview.Allowed) throw new InvalidOperationException("原责任阻止升级：" + preview.Summary);
                    candidate = candidate with { ChangePlanIdentity = preview.Identity };
                }
                await ExecuteAsync(new("Install", Deployment: candidate), progress);
                return candidate.ChangePlanIdentity;
            case "ReviewDeployment":
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                var reviewedDeployment = request.Deployment ?? throw new InvalidDataException("缺少介质计划");
                RequireMatchingMedia(reviewedDeployment); _ = DeploymentMedia.Inspect(reviewedDeployment.MediaRoot, ApplicationId); ValidateSeed(reviewedDeployment);
                var original = WindowsDeploymentSetup.Current(ApplicationId);
                var reviewLaunch = original is null ? new BusinessLaunch(reviewedDeployment.ConfigurationSource, reviewedDeployment.DataRoot, null, null) : new(original.ConfigurationDirectory, original.DataRoot, null, null);
                return Wire.Encode(await Core(reviewLaunch, installer: true).PlanChangeAsync(original?.SourceCommit == reviewedDeployment.SourceCommit ? "Repair" : "Upgrade", request.Destination));
            case "Stage": await Core(request.Launch ?? throw new InvalidDataException("Missing launch"), installer: true).StageAsync(request.ExpectedHash!, request.Candidate!, request.Reason!); break;
            case "RepairInitial": await Core(request.Launch ?? throw new InvalidDataException("Missing launch"), installer: true).RepairInitialAsync(request.ExpectedHash!, request.Candidate!, request.Reason!); break;
            case "SaveDraft": await Core(request.Launch ?? throw new InvalidDataException("Missing launch"), installer: true).SaveDraftAsync(request.ExpectedHash!, request.DraftHash, request.Editor!); break;
            case "PlanChange": return Wire.Encode(await Core(request.Launch!, installer: true).PlanChangeAsync(request.Reason!, request.Destination));
            case "PrepareChange": return Wire.Encode(await Core(request.Launch!, installer: true).PrepareChangeAsync(request.ExpectedHash!, request.Reason!, request.Destination!));
            case "Backup": await Core(request.Launch!, installer: true).BackupAsync(request.Destination!); break;
            case "AdoptRestore":
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                var originalDeployment = WindowsDeploymentSetup.Current(ApplicationId) ?? throw new InvalidDataException("缺少原机部署登记，换机恢复保持隔离");
                if (Wire.FileHash(WindowsDeploymentSetup.Registration(ApplicationId)) != request.ChangePlanIdentity) throw new InvalidOperationException("原机登记变化，重新读取");
                if (string.Equals(Path.GetFullPath(originalDeployment.DataRoot), Path.GetFullPath(request.Destination!), StringComparison.OrdinalIgnoreCase))
                {
                    var restoredSeed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(originalDeployment.ConfigurationDirectory, "business.json")));
                    var proofFile = Path.Combine(originalDeployment.DataRoot, restoredSeed.Mode.ToString(), restoredSeed.StationId, ApplicationId, "restore-handoff.json");
                    DeploymentPaths.RequireOrdinaryAncestors(proofFile);
                    if (new FileInfo(proofFile).Length > 65536) throw new InvalidDataException("交接证据超出预算");
                    var committedProof = Wire.Decode<StartupRestoreHandoff>(File.ReadAllText(proofFile));
                    if (originalDeployment.RestoreHandoffHash != Wire.Hash(Wire.Encode(committedProof)) || committedProof.BackupIdentity != request.ExpectedHash ||
                        !File.Exists(Path.Combine(committedProof.SourceDirectory, "MIGRATION-SOURCE-FENCED.json")) || Wire.FileHash(proofFile) != Wire.FileHash(Path.Combine(committedProof.SourceDirectory, "MIGRATION-SOURCE-FENCED.json")))
                        throw new InvalidDataException("登记已指向新根但交接身份不符；保留隔离，不重复迁移");
                    break; // Previous commit succeeded; receipt loss must not create a second handoff.
                }
                await Core(new(originalDeployment.ConfigurationDirectory, originalDeployment.DataRoot, null, null), installer: true).AdoptRestoreOwnedAsync(request.Backup!, request.Destination!, request.ExpectedHash!, proof =>
                { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); WindowsDeploymentSetup.SwitchDataRoot(ApplicationId, request.ChangePlanIdentity!, request.Destination!, Wire.Hash(Wire.Encode(proof))); return Task.CompletedTask; });
                break;
            case "RepairSystem":
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                WindowsDeploymentSetup.RepairSystemRegistration(ApplicationId, request.ExpectedHash!);
                if (WindowsDeploymentSetup.Current(ApplicationId) is { LifecycleState: "Installed" } repaired) CreateShortcuts(repaired);
                break;
            case "Uninstall":
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                var removing = WindowsDeploymentSetup.Current(ApplicationId) ?? throw new InvalidDataException("没有原部署登记");
                WindowsRemovalReceipt? removed = null;
                await Core(new(removing.ConfigurationDirectory, removing.DataRoot, null, null), installer: true).ExecuteChangeAsync(request.ChangePlanIdentity!, "Uninstall", request.Destination!,
                    () => { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); removed = WindowsDeploymentSetup.Remove(ApplicationId, request.ExpectedHash!, _ => { }, RemoveShortcuts); return Task.CompletedTask; });
                return Wire.Encode(removed!);
            case "ResumeRemoval":
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                var cleanupMedia = request.Deployment ?? throw new InvalidDataException("需要同一完整介质");
                RequireMatchingMedia(cleanupMedia); _ = DeploymentMedia.Inspect(cleanupMedia.MediaRoot, ApplicationId);
                if (WindowsDeploymentSetup.Current(ApplicationId) is not { LifecycleState: "RemovedDataRetained" } cleaning || cleaning.SourceCommit != cleanupMedia.SourceCommit) throw new InvalidOperationException("只能用原介质继续已退役程序的固定清理清单");
                return Wire.Encode(WindowsDeploymentSetup.Remove(ApplicationId, request.ExpectedHash!, _ => throw new InvalidOperationException("退役清理不能重新执行卸载提交"), RemoveShortcuts));
            case "Restore": await Core(request.Launch ?? throw new InvalidDataException("Missing launch"), installer: true).RestoreAsync(request.Backup!, request.Destination!, request.ExpectedHash!); break;
            case "Install":
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                var install = request.Deployment ?? throw new InvalidDataException("Missing deployment");
                if (install.ApplicationId != ParcelAssembly.Definition().Id) throw new InvalidDataException("Helper belongs to another Application");
                RequireMatchingMedia(install);
                _ = DeploymentComponentReviewing.Inspect(install);
                ValidateSeed(install); // Validate bounded source/identity and ordinary paths before creating even an owner lease.
                var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(install.ConfigurationSource, "business.json")));
                var ownerDirectory = Path.Combine(install.DataRoot, seed.Mode.ToString(), seed.StationId, seed.ApplicationId);
                var previous = WindowsDeploymentSetup.Current(install.ApplicationId);
                var previousDirectory = previous is null ? ownerDirectory : Path.Combine(previous.DataRoot, seed.Mode.ToString(), seed.StationId, seed.ApplicationId);
                DeploymentPaths.RequireOrdinaryAncestors(ownerDirectory); DeploymentPaths.RequireOrdinaryAncestors(previousDirectory);
                if (previous is { LifecycleState: "Installed" } || File.Exists(Path.Combine(ownerDirectory, seed.ApplicationId + ".db")))
                {
                    if (install.ChangePlanIdentity is null || install.BackupDestination is null) throw new InvalidOperationException("升级/修复须先审阅原责任及完整备份计划");
                    var originalLaunch = previous is null ? new BusinessLaunch(install.ConfigurationSource, install.DataRoot, null, null) : new(previous.ConfigurationDirectory, previous.DataRoot, null, null);
                    progress?.Invoke(WindowsInstallationStage.PreparingBackup);
                    await Core(originalLaunch, installer: true).ExecuteChangeAsync(install.ChangePlanIdentity, previous?.SourceCommit == install.SourceCommit ? "Repair" : "Upgrade", install.BackupDestination,
                        () => { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); _ = WindowsDeploymentSetup.Install(install, ValidateSeed, InstallPackages, location => { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); CreateShortcuts(location, previous); }, progress); return Task.CompletedTask; });
                }
                else
                {
                    _ = DeploymentMedia.Inspect(install.MediaRoot, ApplicationId);
                    WindowsDeploymentSetup.PrepareInitialDataRoot(install.DataRoot, install.RuntimeAccountSid);
                    using var owner = new ResourceLease(ownerDirectory, ["application-owner"]);
                    _ = WindowsDeploymentSetup.Install(install, ValidateSeed, InstallPackages, location => { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); CreateShortcuts(location, previous); }, progress);
                }
                break;
            default: throw new InvalidDataException("Unknown maintenance action");
        }
        return null;
    }
    static void RequireMatchingMedia(WindowsDeploymentRequest request)
    {
        if (request.ApplicationId != ApplicationId) throw new InvalidDataException("介质不属于本Application");
        foreach (var assembly in new[] { typeof(ParcelStartupServices).Assembly, typeof(MachineRuntimePolicy).Assembly, typeof(IParcelApplication).Assembly, typeof(ParcelRuntime).Assembly, ParcelAssembly.Definition().GetType().Assembly })
            if (Wire.FileHash(Path.Combine(request.MediaRoot, "app", Path.GetFileName(assembly.Location))) != Wire.FileHash(assembly.Location))
                throw new InvalidDataException("安装helper/公共契约/业务与所选介质不匹配；使用同一完整介质");
    }
    static void ValidateSeed(WindowsDeploymentRequest request)
    {
        _ = WindowsDeploymentSetup.ConfigurationFiles(request.ConfigurationSource);
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(request.ConfigurationSource, "business.json")));
        var errors = ParcelAssembly.Definition().Validate(c);
        if ((errors.Length != 0 && !(WindowsDeploymentSetup.Current(request.ApplicationId) is null && IsInitialDraft(c))) || c.Mode != request.Mode || c.ApplicationId != request.ApplicationId)
            throw new InvalidDataException("Invalid Application seed or mode: " + string.Join("; ", errors));
        if (WindowsDeploymentSetup.Current(request.ApplicationId) is { } deployed)
        {
            var originalSeed = Path.Combine(deployed.ConfigurationDirectory, "business.json");
            ParcelConfiguration? originalIdentity = null;
            try { originalIdentity = Wire.Decode<ParcelConfiguration>(File.ReadAllText(originalSeed)); }
            catch (Exception error) when (request.RepairSeed && error is IOException or System.Text.Json.JsonException or InvalidDataException) { }
            if (originalIdentity is not null && (originalIdentity.ApplicationId != c.ApplicationId || originalIdentity.StationId != c.StationId || originalIdentity.Mode != c.Mode))
                throw new InvalidDataException("程序修复/升级保留原站/模式；新站不能复用原安装及数据身份");
        }
        // A seed must not redirect an existing data root to another station/mode and silently create an empty app.
        if (Directory.Exists(request.DataRoot))
        {
            var existing = new List<string>();
            foreach (var mode in new[] { "Simulation", "Production" })
            {
                var modeRoot = Path.Combine(request.DataRoot, mode); DeploymentPaths.RequireOrdinaryAncestors(modeRoot);
                if (!Directory.Exists(modeRoot)) continue;
                foreach (var station in Directory.EnumerateDirectories(modeRoot).Take(257))
                {
                    DeploymentPaths.RequireOrdinaryAncestors(station);
                    var directory = Path.Combine(station, request.ApplicationId); DeploymentPaths.RequireOrdinaryAncestors(directory);
                    if (Directory.Exists(directory)) existing.Add(Path.GetFullPath(directory));
                }
            }
            var selected = Path.GetFullPath(Path.Combine(request.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId));
            if (existing.Count > 0 && !existing.Contains(selected, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("所选数据根已有其他站/模式的原应用；选择匹配原身份的种子，不能用新种子创建空账绕过。");
        }
    }
    static void InstallPackages(WindowsDeploymentRequest request)
    {
        if (request.Packages.Count == 0) return;
        if (request.Packages.Count > 128) throw new InvalidDataException("Extension selection quota exceeded");
        ParcelCapabilityFactory.RegisterContracts();
        var paths = MachineRuntimePolicy.ExtensionPaths(request.Mode);
        var trust = WindowsDeploymentSetup.ReadTrust(paths.TrustFile, request.Mode);
        var store = new ExtensionStore(paths.Root, trust, request.Mode, MachineRuntimePolicy.InstallerLeaseRoot(""));
        // Dependencies must be selected before dependants. No module/SDK is loaded during installation.
        var pending = request.Packages.ToList();
        while (pending.Count > 0)
        {
            var progressed = false; Exception? last = null;
            foreach (var package in pending.ToArray())
                try { store.Install(package.Key, package.Value); pending.Remove(package); progressed = true; }
                catch (Exception ex) when (ex is InvalidDataException or IOException or System.Security.Cryptography.CryptographicException) { last = ex; }
            if (!progressed) throw new InvalidDataException("Extension installation incomplete; existing store/receipts retained. " + last?.Message, last);
        }
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static void CreateShortcuts(DeploymentLocation location) => DeploymentShortcuts.Create(location,
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static void CreateShortcuts(DeploymentLocation location, DeploymentLocation? previous)
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        DeploymentShortcuts.Create(location, programs, desktop, previous);
        if (previous is not null && previous.ReleaseRoot != location.ReleaseRoot)
            DeploymentShortcuts.Remove(previous, programs, desktop);
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static void RemoveShortcuts(DeploymentLocation location) => DeploymentShortcuts.Remove(location,
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
    static async Task<string?> ElevateAsync(StartupMaintenanceRequest request, IProgress<WindowsInstallationStage>? progress = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var directory = Path.GetDirectoryName(typeof(ParcelStartupServices).Assembly.Location)!;
        var helper = Path.Combine(directory, "Ipc.Assembly.exe");
        if (!File.Exists(helper)) helper = Path.GetFullPath(Path.Combine(directory, "..", "app", "Ipc.Assembly.exe"));
        if (request.Action == "InstallWithPreflight") helper = Path.Combine(request.Deployment!.MediaRoot, "desktop", "Ipc.Desktop.exe");
        if (!MediaCommand(request.Action) && WindowsDeploymentSetup.Current(ApplicationId) is { } installed)
            helper = Path.Combine(installed.ReleaseRoot, "app", "Ipc.Assembly.exe");
        foreach (var assembly in new[] { typeof(ParcelStartupServices).Assembly, typeof(MachineRuntimeSetup).Assembly })
            if (Wire.FileHash(Path.Combine(Path.GetDirectoryName(helper)!, Path.GetFileName(assembly.Location))) != Wire.FileHash(assembly.Location))
                throw new InvalidDataException("维护 helper 与当前客户端版本不匹配；请使用相同发布的开始菜单启动修复入口，原数据保留");
        if (!MediaCommand(request.Action)) RequireProtectedHelper(Path.GetDirectoryName(helper)!);
        var pipeName = Wire.Id() + Wire.Id();
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var path = Path.Combine(Path.GetTempPath(), "ipc-maintenance-" + Wire.Id() + ".bin");
        request = request with { Pipe = pipeName };
        if (RequiresManagementMode(request.Action)) request = request with { ManagementGrant = LocalManagementMode.ForRoot(request.Launch!.DataRoot).Delegate(ManagementRequestHash(request)) };
        ProtectedDraftPayload.Write(path, Wire.Encode(request), false);
        var retainRequest = false;
        try
        {
            using var pinned = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var start = new ProcessStartInfo(helper) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(helper)! };
            foreach (var arg in new[] { "--startup-command", path, Wire.FileHash(path) }) start.ArgumentList.Add(arg);
            using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var connection = server.WaitForConnectionAsync(budget.Token);
            progress?.Report(WindowsInstallationStage.WaitingForAuthorization);
            using var process = Process.Start(start) ?? throw new IOException("Windows did not start maintenance");
            async Task<string?> ReadResponse()
            {
                await connection;
                if (!GetNamedPipeClientProcessId(server.SafePipeHandle, out var client) || client != (uint)process.Id) throw new UnauthorizedAccessException("维护回执进程身份不匹配；重新核对持久操作记录");
                using var input = new StreamReader(server);
                return await ReadMaintenanceResponse(input, stage => progress?.Report(stage), budget.Token);
            }
            var responseRead = ReadResponse(); // Drain while the child writes, before waiting for its exit.
            try { await process.WaitForExitAsync(budget.Token); }
            catch (OperationCanceledException)
            { throw new InstallationOutcomeUnknownException("维护等待超时；授权进程可能仍在完成持久提交，未自动终止。保留窗口/原数据，核对受保护操作回执后再继续。"); }
            try { await connection.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (TimeoutException error) { budget.Cancel(); throw new InstallationOutcomeUnknownException("维护进程未返回回执（退出码 " + process.ExitCode + "），请核对安装记录后继续。", error); }
            catch (Exception error) when (error is IOException or OperationCanceledException)
            { throw new InstallationOutcomeUnknownException("维护回执通道中断，请先核对安装记录。", error); }
            try
            {
                return CompleteMaintenanceResponse(await responseRead, process.ExitCode);
            }
            catch (InstallationOutcomeUnknownException) { throw; }
            catch (IOException) when (responseRead.IsCompletedSuccessfully) { throw; } // An explicit Failed terminal is known.
            catch (Exception error) when (error is IOException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or OperationCanceledException or UnauthorizedAccessException)
            { throw new InstallationOutcomeUnknownException("未取得可信维护终态。安装可能已提交，请核对安装记录后再继续。", error); }
        }
        catch (InstallationOutcomeUnknownException) { retainRequest = true; throw; }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new UnauthorizedAccessException("已取消 Windows 维护授权；已有配置和数据保留，可重新操作。", ex); }
        finally { if (!retainRequest) File.Delete(path); }
    }
    internal static void WriteInstallationProgress(TextWriter writer, WindowsInstallationStage stage)
    {
        // One JSON value per line. Wire.Encode indents documents across lines.
        // Losing this observational channel must not interrupt a durable transaction.
        try { writer.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Status = "Progress", Stage = stage.ToString() })); }
        catch (IOException) { }
    }
    internal static async Task<string?> ReadMaintenanceResponse(TextReader input, Action<WindowsInstallationStage> progress, CancellationToken token)
    {
        var line = new System.Text.StringBuilder(); var character = new char[1]; var events = 0;
        while (await input.ReadAsync(character.AsMemory(), token) != 0)
        {
            if (character[0] == '\n')
            {
                var value = line.ToString(); line.Clear();
                using var message = System.Text.Json.JsonDocument.Parse(value);
                if (message.RootElement.GetProperty("Status").GetString() != "Progress") return value;
                if (++events > 128 || !Enum.TryParse<WindowsInstallationStage>(message.RootElement.GetProperty("Stage").GetString(), out var stage) || !Enum.IsDefined(stage))
                    throw new IOException("安装阶段回执无效；请核对安装记录");
                progress(stage); continue;
            }
            if (line.Length == 65536) throw new IOException("维护回执超出预算"); line.Append(character[0]);
        }
        return null; // A truncated final line never proves completion.
    }
    internal static string? CompleteMaintenanceResponse(string? response, int exitCode)
    {
        string? failure = null, result = null;
        try
        {
            if (response is null || response.Length > 65536) throw new IOException("维护终态回执缺失");
            using var document = System.Text.Json.JsonDocument.Parse(response);
            var terminal = document.RootElement.GetProperty("Status").GetString();
            if (terminal is not ("Completed" or "Failed") || terminal == "Completed" && exitCode != 0 || terminal == "Failed" && exitCode != 2) throw new IOException("维护终态与进程结果不一致");
            if (terminal == "Failed") failure = document.RootElement.GetProperty("Error").GetString() ?? "维护未完成";
            if (document.RootElement.TryGetProperty("Result", out var value))
            {
                if (value.ValueKind is not (System.Text.Json.JsonValueKind.String or System.Text.Json.JsonValueKind.Null)) throw new IOException("维护终态结果格式无效");
                result = value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
            }
        }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new InstallationOutcomeUnknownException("未取得可信维护终态，请先核对安装记录。", error); }
        if (failure is not null) throw new IOException(failure);
        return result;
    }
    static bool MediaCommand(string action) => action is "Install" or "InstallWithPreflight" or "ReviewDeployment" or "ResumeRemoval";
    static void RequireProtectedHelper(string directory)
    {
        var file = Path.Combine(directory, "application-release.json");
        if (!File.Exists(file)) throw new InvalidDataException("请先完成安装向导；离线写入使用受保护的匹配发布。原数据保留。");
        var release = Wire.Decode<ApplicationRelease>(File.ReadAllText(file));
        MachineRuntimePolicy.RequireProtectedApplicationInstaller(directory, release.Files.Keys.Append("application-release.json").ToArray());
        ApplicationReleaseValidation.ValidateIfPublished(directory, ParcelAssembly.Definition().Id, new(release.Views, release.Layouts), ParcelAssembly.Definition().GetType().Assembly);
    }
    sealed class ElevatedMaintenance(BusinessLaunch launch) : IStartupMaintenance
    {
        readonly ParcelStartupMaintenance core = Core(launch);
        void RequireManagementMode() => LocalManagementMode.ForRoot(launch.DataRoot).Require();
        public Task<StartupClassification> ClassifyAsync() => core.ClassifyAsync();
        public Task<StartupMaintenanceDocument> ReadAsync() => core.ReadAsync();
        public async Task AdoptRestoreAsync(string backup, string restoredDataRoot, string expectedBackupHash, string expectedRegistrationHash)
        {
            RequireManagementMode();
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("登记接管仅适用于原机Windows安装；portable恢复继续隔离");
            await ElevateAsync(new("AdoptRestore", launch, expectedBackupHash, Backup: backup, Destination: restoredDataRoot, ChangePlanIdentity: expectedRegistrationHash));
        }
        public Task<StartupBackupItem[]> ListBackupsAsync(string directory, CancellationToken token) => core.ListBackupsAsync(directory, token);
        public async Task<StartupChangePreview> PlanChangeAsync(string purpose, string? backupDestination)
        {
            RequireManagementMode();
            if (!OperatingSystem.IsWindows()) return await core.PlanChangeAsync(purpose, backupDestination);
            return Wire.Decode<StartupChangePreview>(await ElevateAsync(new("PlanChange", launch, Reason: purpose, Destination: backupDestination)) ?? throw new IOException("Missing maintenance plan"));
        }
        public async Task<StartupChangePreview> PrepareChangeAsync(string expectedPlan, string purpose, string backupDestination)
        {
            RequireManagementMode();
            if (!OperatingSystem.IsWindows()) return await core.PrepareChangeAsync(expectedPlan, purpose, backupDestination);
            return Wire.Decode<StartupChangePreview>(await ElevateAsync(new("PrepareChange", launch, expectedPlan, Reason: purpose, Destination: backupDestination)) ?? throw new IOException("Missing backup receipt"));
        }
        public async Task BackupAsync(string destination)
        { RequireManagementMode();
            if (OperatingSystem.IsWindows()) await ElevateAsync(new("Backup", launch, Destination: destination)); else await core.BackupAsync(destination); }
        public Task<StartupSavedDraft?> ReadDraftAsync() => core.ReadDraftAsync();
        public async Task<StartupSavedDraft> SaveDraftAsync(string expectedConfigurationHash, string? expectedDraftHash, StartupEditorState editor)
        {
            RequireManagementMode();
            if (OperatingSystem.IsWindows()) await ElevateAsync(new("SaveDraft", launch, expectedConfigurationHash, Editor: editor, DraftHash: expectedDraftHash));
            else return await core.SaveDraftAsync(expectedConfigurationHash, expectedDraftHash, editor);
            return await core.ReadDraftAsync() ?? throw new IOException("Draft receipt unavailable");
        }
        public async Task<StartupMaintenanceDocument> RepairInitialAsync(string expectedHash, ParcelConfiguration candidate, string reason)
        {
            RequireManagementMode();
            if (OperatingSystem.IsWindows()) await ElevateAsync(new("RepairInitial", launch, expectedHash, candidate, reason));
            else return await core.RepairInitialAsync(expectedHash, candidate, reason);
            return await core.ReadAsync();
        }
        public Task<BusinessReceipt> ValidateAsync(ParcelConfiguration candidate) => core.ValidateAsync(candidate);
        public Task<BarcodeFilterResult> TestBarcodeFilterAsync(BarcodeFilterPolicy policy, string[] candidates) => core.TestBarcodeFilterAsync(policy, candidates);
        public Task<StartupCheckReport> CheckAsync(ParcelConfiguration candidate) => core.CheckAsync(candidate);
        public Task<StartupRecoveryPreview> PlanRestoreAsync(string backup, string destination) => core.PlanRestoreAsync(backup, destination);
        public async Task<StartupMaintenanceDocument> StageAsync(string expectedHash, ParcelConfiguration candidate, string reason)
        {
            RequireManagementMode();
            if (OperatingSystem.IsWindows()) await ElevateAsync(new("Stage", launch, expectedHash, candidate, reason));
            else return await core.StageAsync(expectedHash, candidate, reason);
            var document = await core.ReadAsync();
            await using var ports = new ParcelCapabilityFactory(launch);
            if (document.Candidate is null || document.ConfigurationHash == expectedHash || Wire.Encode(document.Candidate) != Wire.Encode(ports.Normalize(candidate)))
                throw new IOException("保存后配置未匹配读回或已再次变化；重新读取比较，保留当前草稿");
            return document;
        }
        public async Task RestoreAsync(string backup, string destination, string expectedBackupHash)
        {
            RequireManagementMode();
            if (OperatingSystem.IsWindows()) await ElevateAsync(new("Restore", launch, expectedBackupHash, Backup: backup, Destination: destination));
            else await core.RestoreAsync(backup, destination, expectedBackupHash);
        }
    }
}
