using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;

namespace Ipc.Assembly;

public sealed record StartupMaintenanceRequest(string Action, BusinessLaunch? Launch = null, string? ExpectedHash = null,
    ParcelConfiguration? Candidate = null, string? Reason = null, string? Backup = null, string? Destination = null,
    WindowsDeploymentRequest? Deployment = null, string? Pipe = null);

// Composition entry points for installation and offline maintenance; never creates IBusinessRuntime.
public static class ParcelStartupServices
{
    public static string ApplicationId => ParcelAssembly.Definition().Id;
    public static string DefaultRuntimeAccount()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var policy = Path.Combine(WindowsDeploymentSetup.MachineRoot, "runtime-policy.json");
        if (File.Exists(policy))
        {
            MachineRuntimePolicy.RequireProtectedEvidence(policy);
            return ((NTAccount)new SecurityIdentifier(Wire.Decode<MachineRuntimePolicy>(File.ReadAllText(policy)).RuntimeAccountSid).Translate(typeof(NTAccount))).Value;
        }
        using var identity = WindowsIdentity.GetCurrent(); return identity.Name;
    }
    public static WindowsDeploymentRequest Review(DeploymentMedia media, string account, string dataRoot, string configurationSource,
        string? trustFile, string[] packages, bool repairSeed)
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
        if (caller.User?.Value != sid.Value && data.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(data)) throw new InvalidDataException("其他运行账户无法保证访问当前用户的原数据；保留原根，选择原运行账户或先完整恢复到共享卷。");
            data = Path.Combine(WindowsDeploymentSetup.MachineRoot, "data", ApplicationId);
        }
        var request = new WindowsDeploymentRequest(media.Root, ApplicationId, media.SourceCommit, sid.Value, data, source, seed.Mode, trust,
            trust is null ? null : Wire.FileHash(trust), packages.ToDictionary(p => p, Wire.FileHash), repairSeed,
            files.ToDictionary(p => Path.GetFileName(p), Wire.FileHash));
        ValidateSeed(request);
        return request;
    }
    public static bool IsRuntimeAccount(DeploymentLocation location)
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = WindowsIdentity.GetCurrent(); return identity.User?.Value == location.RuntimeAccountSid;
    }
    public static IStartupMaintenance Maintenance(BusinessLaunch launch) => new ElevatedMaintenance(launch);
    static ParcelStartupMaintenance Core(BusinessLaunch launch, bool installer = false) => new(launch, ParcelAssembly.Definition(), () => new ParcelCapabilityFactory(launch,
        BusinessCompatibility.Read(ParcelAssembly.Definition().GetType().Assembly), ParcelAssembly.Definition().GetType().Assembly, installer));
    public static async Task<DeploymentLocation> InstallAsync(WindowsDeploymentRequest request)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows setup requires Windows");
        await ElevateAsync(new("Install", Deployment: request));
        var location = WindowsDeploymentSetup.Current(request.ApplicationId) ?? throw new InvalidDataException("No installed deployment receipt");
        if (location.SourceCommit != request.SourceCommit || location.RuntimeAccountSid != request.RuntimeAccountSid ||
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
        var request = Wire.Decode<StartupMaintenanceRequest>(System.Text.Encoding.UTF8.GetString(bytes));
        if (request.Pipe is not { Length: 64 } || request.Pipe.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException("Invalid maintenance receipt channel");
        string? failure = null;
        try { if (request.Action != "Install") RequireProtectedHelper(AppContext.BaseDirectory); ExecuteAsync(request).GetAwaiter().GetResult(); }
        catch (Exception ex) { failure = ex.Message; Environment.ExitCode = 2; }
        using var pipe = new NamedPipeClientStream(".", request.Pipe, PipeDirection.Out);
        pipe.Connect(10000);
        using var writer = new StreamWriter(pipe); writer.WriteLine(Wire.Encode(new { Status = failure is null ? "Completed" : "Failed", Error = failure is null ? null : failure[..Math.Min(failure.Length, 4000)] }));
        return true;
    }
    static async Task ExecuteAsync(StartupMaintenanceRequest request)
    {
        switch (request.Action)
        {
            case "Stage": await Core(request.Launch ?? throw new InvalidDataException("Missing launch"), installer: true).StageAsync(request.ExpectedHash!, request.Candidate!, request.Reason!); break;
            case "Restore": await Core(request.Launch ?? throw new InvalidDataException("Missing launch"), installer: true).RestoreAsync(request.Backup!, request.Destination!, request.ExpectedHash!); break;
            case "Install":
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                var install = request.Deployment ?? throw new InvalidDataException("Missing deployment");
                if (install.ApplicationId != ParcelAssembly.Definition().Id) throw new InvalidDataException("Helper belongs to another Application");
                ValidateSeed(install); // Validate bounded source/identity and ordinary paths before creating even an owner lease.
                var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(install.ConfigurationSource, "business.json")));
                var ownerDirectory = Path.Combine(install.DataRoot, seed.Mode.ToString(), seed.StationId, seed.ApplicationId);
                var previous = WindowsDeploymentSetup.Current(install.ApplicationId);
                var previousDirectory = previous is null ? ownerDirectory : Path.Combine(previous.DataRoot, seed.Mode.ToString(), seed.StationId, seed.ApplicationId);
                DeploymentPaths.RequireOrdinaryAncestors(ownerDirectory); DeploymentPaths.RequireOrdinaryAncestors(previousDirectory);
                using (var previousOwner = Directory.Exists(previousDirectory) && !string.Equals(Path.GetFullPath(previousDirectory), Path.GetFullPath(ownerDirectory), StringComparison.OrdinalIgnoreCase)
                    ? new ResourceLease(previousDirectory, ["application-owner"]) : null)
                using (var owner = Directory.Exists(ownerDirectory) ? new ResourceLease(ownerDirectory, ["application-owner"]) : null)
                {
                    _ = WindowsDeploymentSetup.Install(install, ValidateSeed, InstallPackages, CreateShortcuts);
                }
                break;
            default: throw new InvalidDataException("Unknown maintenance action");
        }
    }
    static void ValidateSeed(WindowsDeploymentRequest request)
    {
        _ = WindowsDeploymentSetup.ConfigurationFiles(request.ConfigurationSource);
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(request.ConfigurationSource, "business.json")));
        var errors = ParcelAssembly.Definition().Validate(c);
        if (errors.Length != 0 || c.Mode != request.Mode || c.ApplicationId != request.ApplicationId)
            throw new InvalidDataException("Invalid Application seed or mode: " + string.Join("; ", errors));
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
    static void CreateShortcuts(DeploymentLocation location)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Importer PC");
        DeploymentPaths.RequireOrdinaryAncestors(folder); Directory.CreateDirectory(folder);
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows shortcut service unavailable");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            foreach (var entry in new[] { (Name: "运行", Args: ""), (Name: "启动修复", Args: "--maintenance"), (Name: "安装维护", Args: "--setup") })
            {
                var path = Path.Combine(folder, location.ApplicationId + " · " + location.SourceCommit[..8] + " · " + Path.GetFileName(location.ReleaseRoot)[^8..] + " · " + entry.Name + ".lnk");
                DeploymentPaths.RequireOrdinaryAncestors(path);
                dynamic shortcut = shell.CreateShortcut(path);
                try { shortcut.TargetPath = Path.Combine(location.ReleaseRoot, "desktop", "Ipc.Desktop.exe"); shortcut.Arguments = entry.Args; shortcut.WorkingDirectory = location.ReleaseRoot; shortcut.Description = "Importer PC " + location.ApplicationId; shortcut.Save(); }
                finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); }
            }
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }
    static async Task ElevateAsync(StartupMaintenanceRequest request)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var directory = Path.GetDirectoryName(typeof(ParcelStartupServices).Assembly.Location)!;
        var helper = Path.Combine(directory, "Ipc.Assembly.exe");
        if (!File.Exists(helper)) helper = Path.GetFullPath(Path.Combine(directory, "..", "app", "Ipc.Assembly.exe"));
        if (request.Action != "Install" && WindowsDeploymentSetup.Current(ApplicationId) is { } installed)
            helper = Path.Combine(installed.ReleaseRoot, "app", "Ipc.Assembly.exe");
        foreach (var assembly in new[] { typeof(ParcelStartupServices).Assembly, typeof(MachineRuntimeSetup).Assembly })
            if (Wire.FileHash(Path.Combine(Path.GetDirectoryName(helper)!, Path.GetFileName(assembly.Location))) != Wire.FileHash(assembly.Location))
                throw new InvalidDataException("维护 helper 与当前客户端版本不匹配；请使用相同发布的开始菜单启动修复入口，原数据保留");
        if (request.Action != "Install") RequireProtectedHelper(Path.GetDirectoryName(helper)!);
        var pipeName = Wire.Id() + Wire.Id();
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var path = Path.Combine(Path.GetTempPath(), "ipc-maintenance-" + Wire.Id() + ".json");
        await File.WriteAllTextAsync(path, Wire.Encode(request with { Pipe = pipeName }));
        try
        {
            using var pinned = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var start = new ProcessStartInfo(helper) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(helper)! };
            foreach (var arg in new[] { "--startup-command", path, Wire.FileHash(path) }) start.ArgumentList.Add(arg);
            using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var connection = server.WaitForConnectionAsync(budget.Token);
            using var process = Process.Start(start) ?? throw new IOException("Windows did not start maintenance");
            async Task<string?> ReadResponse()
            {
                await connection;
                using var input = new StreamReader(server);
                return await input.ReadLineAsync(budget.Token);
            }
            var responseRead = ReadResponse(); // Drain while the child writes, before waiting for its exit.
            await process.WaitForExitAsync();
            try { await connection.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (TimeoutException) { budget.Cancel(); throw new IOException("Maintenance returned without a receipt (exit " + process.ExitCode + "); keep existing data and inspect installation diagnostics"); }
            var response = await responseRead;
            if (response is null || response.Length > 65536) throw new IOException("Invalid maintenance receipt");
            using var document = System.Text.Json.JsonDocument.Parse(response);
            if (process.ExitCode != 0 || document.RootElement.GetProperty("Status").GetString() != "Completed")
                throw new IOException(document.RootElement.GetProperty("Error").GetString() ?? "Maintenance failed");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new UnauthorizedAccessException("已取消 Windows 维护授权；已有配置和数据保留，可重新操作。", ex); }
        finally { File.Delete(path); }
    }
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
        public Task<StartupMaintenanceDocument> ReadAsync() => core.ReadAsync();
        public Task<BusinessReceipt> ValidateAsync(ParcelConfiguration candidate) => core.ValidateAsync(candidate);
        public Task ExportAsync(string destination) => core.ExportAsync(destination);
        public Task<StartupRecoveryPreview> PlanRestoreAsync(string backup, string destination) => core.PlanRestoreAsync(backup, destination);
        public async Task<StartupMaintenanceDocument> StageAsync(string expectedHash, ParcelConfiguration candidate, string reason)
        {
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
            if (OperatingSystem.IsWindows()) await ElevateAsync(new("Restore", launch, expectedBackupHash, Backup: backup, Destination: destination));
            else await core.RestoreAsync(backup, destination, expectedBackupHash);
        }
    }
}
