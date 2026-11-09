using System.Runtime.Versioning;
using System.Security.Principal;

namespace Ipc.Platform;

public sealed record QuietDeploymentIntent(string ApplicationId, string OperationId, string Action, string? RequestedDataRoot,
    string LogFile, string? MediaSourceCommit, string? MediaInventorySha256)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Identity => Wire.Hash(Wire.Encode(this));
    public void Validate()
    {
        _ = WindowsDeploymentSetup.RemovalOperationFile(ApplicationId, OperationId);
        if (Action is not ("Install" or "Repair" or "Uninstall") || !Path.IsPathFullyQualified(LogFile) ||
            RequestedDataRoot is not null && !Path.IsPathFullyQualified(RequestedDataRoot))
            throw new ArgumentException("静默部署的命令或路径身份不完整。");
        if (Action == "Uninstall")
        {
            if (RequestedDataRoot is not null || MediaSourceCommit is not null || MediaInventorySha256 is not null)
                throw new ArgumentException("卸载不接受替代介质或数据根。");
        }
        else if (MediaSourceCommit is not { Length: 40 } || !MediaSourceCommit.All(char.IsAsciiHexDigit) ||
            MediaInventorySha256 is not { Length: 64 } || !MediaInventorySha256.All(char.IsAsciiHexDigit))
            throw new ArgumentException("静默部署的命令、介质或路径身份不完整。");
    }
}

public sealed record QuietDeploymentReplay(string OperationId, string Status, int ExitCode, string Detail, string LogStatus);
internal sealed record QuietDeploymentRecord(int Schema, QuietDeploymentIntent Intent, string Status,
    WindowsDeploymentRequest? Installation = null, string? RemovalFile = null, string? ExpectedRegistrationHash = null,
    int? ExitCode = null, string? LogSha256 = null);

// This is an acknowledgement/index of the original setup/removal, never a
// second deployment authority. Every replay derives facts from original records.
public sealed class QuietDeploymentOperation : IDisposable
{
    readonly IDisposable guard;
    readonly Action<QuietDeploymentRecord> save;
    readonly Func<QuietDeploymentRecord, QuietDeploymentReplay> inspect;
    QuietDeploymentRecord record;
    bool disposed;
    public bool IsReplay { get; }
    public string? OriginalOperationFile => record.Installation is { } request ? WindowsDeploymentSetup.InstallationOperationFile(request) : record.RemovalFile;
    internal QuietDeploymentOperation(QuietDeploymentRecord record, bool replay, IDisposable guard,
        Action<QuietDeploymentRecord> save, Func<QuietDeploymentRecord, QuietDeploymentReplay> inspect)
    { this.record = record; IsReplay = replay; this.guard = guard; this.save = save; this.inspect = inspect; }

    [SupportedOSPlatform("windows")]
    public QuietDeploymentOutput CreateOutput()
    {
        RequireNew(); MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        WindowsStoragePaths.PrepareProtectedDirectory(Path.GetDirectoryName(record.Intent.LogFile)!, new SecurityIdentifier(MachineRuntimePolicy.SharedUsersSid));
        return new(record.Intent.LogFile, record.Intent.ApplicationId, record.Intent.OperationId, record.Intent.Action);
    }

    public void FreezeInstallation(WindowsDeploymentRequest request)
    {
        RequireNew();
        if (request.ApplicationId != record.Intent.ApplicationId || request.OperationId != record.Intent.OperationId ||
            request.SourceCommit != record.Intent.MediaSourceCommit || record.Intent.Action == "Uninstall")
            throw new InvalidDataException("静默安装计划与原命令身份不一致。");
        Update(record with { Status = "Executing", Installation = request, ExpectedRegistrationHash = request.ExpectedRegistrationHash });
    }

    public void FreezeRemoval(DeploymentLocation original, string expectedHash)
    {
        RequireNew();
        if (record.Intent.Action != "Uninstall" || original.ApplicationId != record.Intent.ApplicationId ||
            expectedHash.Length != 64 || !expectedHash.All(char.IsAsciiHexDigit)) throw new InvalidDataException("静默卸载身份不完整。");
        var file = original.LifecycleState == "Installed" ? WindowsDeploymentSetup.RemovalOperationFile(original.ApplicationId, record.Intent.OperationId) :
            original.MaintenanceOperation ?? throw new InvalidDataException("原卸载操作缺失。");
        Update(record with { Status = "Executing", RemovalFile = file, ExpectedRegistrationHash = expectedHash });
    }

    public QuietDeploymentReplay Inspect()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return inspect(record);
    }

    public QuietDeploymentReplay Finish(int proposedExitCode, string? logHash = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var outcome = record.Status == "Reserved" && !IsReplay ? new QuietDeploymentReplay(record.Intent.OperationId, "Failed", proposedExitCode == 0 ? 1603 : proposedExitCode,
            "尚未提交程序事务；原暂存/备份按原件保留。", "Unverified") : inspect(record);
        var terminal = outcome.Status is "Completed" or "Failed";
        Update(record with { Status = terminal ? outcome.Status : "Unknown", ExitCode = outcome.ExitCode, LogSha256 = logHash ?? record.LogSha256 });
        return outcome;
    }

    void RequireNew()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsReplay || record.Status != "Reserved") throw new InstallationOutcomeUnknownException("只允许核对原静默操作，不再次执行。");
    }
    void Update(QuietDeploymentRecord next) { WindowsDeploymentSetup.ValidateQuietRecord(next, record.Intent); save(next); record = next; }
    public void Dispose() { if (disposed) return; guard.Dispose(); disposed = true; }
}

public static partial class WindowsDeploymentSetup
{
    static string QuietFile(QuietDeploymentIntent intent) => Path.Combine(MachineRoot, "deployments", intent.ApplicationId + ".quiet-" + intent.OperationId + ".json");
    internal static void ValidateQuietRecord(QuietDeploymentRecord record, QuietDeploymentIntent intent)
    {
        record.Intent.Validate(); intent.Validate();
        if (record.Schema != 1 || record.Intent.Identity != intent.Identity || record.Status is not ("Reserved" or "Executing" or "Unknown" or "Completed" or "Failed"))
            throw new InvalidDataException("同一静默操作身份的命令或介质已变化；保留原记录，不重新执行。");
        if (record.Installation is { } install)
        {
            if (intent.Action == "Uninstall" || install.Mode != RunMode.Production || install.ApplicationId != intent.ApplicationId ||
                install.OperationId != intent.OperationId || install.SourceCommit != intent.MediaSourceCommit || install.ExpectedRegistrationHash != record.ExpectedRegistrationHash ||
                intent.RequestedDataRoot is not null && !Path.GetFullPath(install.DataRoot).Equals(intent.RequestedDataRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("冻结安装计划与原命令不一致。");
        }
        if (record.RemovalFile is not null)
        {
            if (intent.Action != "Uninstall" || record.ExpectedRegistrationHash is null) throw new InvalidDataException("原卸载缺少固定前置身份。");
            _ = QuietOriginalRemovalFile(record);
        }
        if (record.Installation is not null && record.RemovalFile is not null ||
            record.Status == "Reserved" && (record.Installation is not null || record.RemovalFile is not null) ||
            (record.Status is "Executing" or "Completed") && record.Installation is null && record.RemovalFile is null ||
            record.ExpectedRegistrationHash is { } registration && (registration.Length != 64 || !registration.All(char.IsAsciiHexDigit)) ||
            record.LogSha256 is { } hash && (hash.Length != 64 || !hash.All(char.IsAsciiHexDigit)))
            throw new InvalidDataException("原静默记录状态或摘要无效。");
    }

    static string QuietOriginalRemovalFile(QuietDeploymentRecord record)
    {
        var file = record.RemovalFile!;
        var name = Path.GetFileNameWithoutExtension(file); var prefix = record.Intent.ApplicationId + ".remove-";
        if (!name.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("原静默卸载文件名无效。");
        var expected = RemovalOperationFile(record.Intent.ApplicationId, name[prefix.Length..]);
        if (file != expected) throw new InvalidDataException("原静默卸载路径不一致。");
        return expected;
    }

    [SupportedOSPlatform("windows")]
    public static QuietDeploymentOperation BeginQuietOperation(QuietDeploymentIntent intent)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority(); intent.Validate();
        if (intent.ApplicationId != "sportmaster") throw new NotSupportedException("当前静默幂等账仅用于SM。");
        ApplicationRootOwnership.RequireWindowsStorage(intent.RequestedDataRoot ?? Current(intent.ApplicationId)?.DataRoot ?? @"D:\ProgramData\ImporterPc\data", @"D:\CainiaoSupplyStation");
        var sid = new SecurityIdentifier(MachineRuntimePolicy.SharedUsersSid);
        var directory = Path.Combine(MachineRoot, "deployments");
        // A new quiet install must establish machine policy before creating its journal.
        // The policy bootstrap deliberately refuses a machine root with existing deployment material.
        if (intent.Action == "Install" && !Directory.Exists(directory))
        {
            WindowsStoragePaths.RequireExistingProtectedAncestors(Path.GetDirectoryName(intent.LogFile)!);
            QuietDeploymentOutput.RequireNewPaths(intent.LogFile);
            MachineRuntimeSetup.InstallForAccount(sid.Value);
        }
        WindowsStoragePaths.PrepareProtectedDirectory(directory, sid);
        var guardFile = Path.Combine(directory, intent.ApplicationId + ".quiet.lease");
        if (!File.Exists(guardFile)) WriteProtected(guardFile, new { Purpose = "quiet-command-serialization" }, sid, overwrite: false);
        MachineRuntimePolicy.RequireProtectedEvidence(guardFile);
        var guard = new FileStream(guardFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            QuietDeploymentRecord Read(string file)
            {
                MachineRuntimePolicy.RequireProtectedEvidence(file);
                if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("原静默记录超出读取预算。");
                return Wire.Decode<QuietDeploymentRecord>(File.ReadAllText(file));
            }
            var file = QuietFile(intent); var replay = File.Exists(file); QuietDeploymentRecord record;
            if (replay) { record = Read(file); ValidateQuietRecord(record, intent); }
            else
            {
                var records = Directory.EnumerateFiles(directory, intent.ApplicationId + ".quiet-*.json").Take(129).ToArray();
                if (records.Length > 128) throw new InvalidDataException("原静默操作超过核对预算。");
                long total = 0;
                foreach (var previousFile in records)
                {
                    MachineRuntimePolicy.RequireProtectedEvidence(previousFile);
                    total = checked(total + new FileInfo(previousFile).Length);
                    if (total > 16 * 1024 * 1024) throw new InvalidDataException("原静默操作超出总读取预算。");
                    var previous = Read(previousFile); ValidateQuietRecord(previous, previous.Intent);
                    if (previousFile != QuietFile(previous.Intent)) throw new InvalidDataException("原静默记录与文件身份不一致。");
                    if (previous.Status is "Reserved" or "Executing" or "Unknown")
                    {
                        var observation = InspectQuietRecord(previous);
                        if (observation.Status is not ("Completed" or "Failed")) throw new InstallationOutcomeUnknownException("存在尚未核对的原静默部署；不能更换ID再次提交。");
                        WriteProtected(previousFile, previous with { Status = observation.Status, ExitCode = observation.ExitCode }, sid);
                    }
                }
                DeploymentPaths.RequireOrdinaryAncestors(intent.LogFile);
                WindowsStoragePaths.RequireExistingProtectedAncestors(Path.GetDirectoryName(intent.LogFile)!);
                QuietDeploymentOutput.RequireNewPaths(intent.LogFile);
                using (var deployment = new FileStream(Path.Combine(directory, intent.ApplicationId + ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    if (File.Exists(RemovalOperationFile(intent.ApplicationId, intent.OperationId)) ||
                        File.Exists(Path.Combine(directory, intent.ApplicationId + ".setup-" + intent.OperationId + ".json")))
                        throw new InstallationOutcomeUnknownException("原程序事务身份已经存在，须核对该次，不能添加另一份静默命令。");
                    var current = Current(intent.ApplicationId);
                    RequireQuietLogOutsideManagedRoots(intent.LogFile, [MachineRoot, WindowsStoragePaths.AdministratorRoot,
                        current?.DataRoot ?? intent.RequestedDataRoot ?? @"D:\ProgramData\ImporterPc\data",
                        current is null ? @"D:\CainiaoSupplyStation" : Path.GetDirectoryName(Path.GetDirectoryName(current.ReleaseRoot))!]);
                    record = new(1, intent, "Reserved", ExpectedRegistrationHash: current is null ? null : Wire.FileHash(Registration(intent.ApplicationId)));
                    WriteProtected(file, record, sid, overwrite: false);
                }
            }
            return new(record, replay, guard, state => WriteProtected(file, state, sid), InspectQuietRecord);
        }
        catch (Exception original)
        {
            try { guard.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("原静默检查失败且命令租约释放未完成。", original, cleanup); }
            throw;
        }
    }

    static QuietDeploymentReplay InspectQuietRecord(QuietDeploymentRecord record)
    {
        ValidateQuietRecord(record, record.Intent);
        if (record.Status == "Failed" && record.Installation is null && record.RemovalFile is null)
            return new(record.Intent.OperationId, "Failed", record.ExitCode ?? 1603, "原命令在提交程序事务前失败；不重新执行。", QuietLogStatus(record));
        using var lease = new FileStream(Path.Combine(MachineRoot, "deployments", record.Intent.ApplicationId + ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var current = Current(record.Intent.ApplicationId);
        var hash = current is null ? null : Wire.FileHash(Registration(record.Intent.ApplicationId));
        if (record.Installation is null && record.RemovalFile is null)
        {
            var noOriginal = !File.Exists(RemovalOperationFile(record.Intent.ApplicationId, record.Intent.OperationId)) &&
                !File.Exists(Path.Combine(MachineRoot, "deployments", record.Intent.ApplicationId + ".setup-" + record.Intent.OperationId + ".json"));
            return ProjectQuietOutcome(record, noOriginal && hash == record.ExpectedRegistrationHash ? "NotStarted" : "Unknown");
        }
        if (record.Installation is { } request)
        {
            var file = InstallationOperationFile(request);
            if (!File.Exists(file)) return ProjectQuietOutcome(record, hash == record.ExpectedRegistrationHash ? "NotStarted" : "Unknown");
            MachineRuntimePolicy.RequireProtectedEvidence(file);
            if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("原安装回执超出预算。");
            return ProjectQuietOutcome(record, ProjectInstallation(request, File.ReadAllText(file), current).Status);
        }
        if (record.RemovalFile is { } removal && current is not null)
        {
            if (!File.Exists(removal)) return ProjectQuietOutcome(record, hash == record.ExpectedRegistrationHash ? "NotStarted" : "Unknown");
            var original = RemovalRecords(record.Intent.ApplicationId).Single(item => item.File == removal).State;
            return ProjectQuietOutcome(record, ProjectRemoval(original, removal, current, hash!).Status);
        }
        return ProjectQuietOutcome(record, "Unknown");
    }

    internal static QuietDeploymentReplay ProjectQuietOutcome(QuietDeploymentRecord record, string originalStatus)
    {
        var (status, code) = originalStatus switch
        {
            "Completed" when record.Installation is not null => ("Completed", 0),
            "RemovedDataRetained" when record.RemovalFile is not null => ("Completed", 0),
            "RestartRequired" when record.RemovalFile is not null => ("Completed", 3010),
            "RemovedWithRetainedFiles" when record.RemovalFile is not null => ("Completed", 1603),
            "Failed" or "NotStarted" => ("Failed", 1603),
            "NotCommitted" when record.RemovalFile is not null => ("Failed", 1603),
            _ => ("Unknown", 1627)
        };
        return new(record.Intent.OperationId, status, code, "原程序事务核对：" + originalStatus + "；未重复执行或启动业务。", QuietLogStatus(record));
    }

    internal static void RequireQuietLogOutsideManagedRoots(string log, IEnumerable<string> roots)
    {
        DeploymentPaths.RequireOrdinaryAncestors(log);
        if (roots.Any(root => TrustedProgramRoots.Contains(root, log)))
            throw new ArgumentException("部署日志须位于程序、配置、数据和机器政策根之外；不把日志写入受管库存。");
    }

    static string QuietLogStatus(QuietDeploymentRecord record)
    {
        DeploymentPaths.RequireOrdinaryAncestors(record.Intent.LogFile);
        if (!File.Exists(record.Intent.LogFile)) return "Missing";
        if (new FileInfo(record.Intent.LogFile).Length > 4 * 1024 * 1024) return "TooLarge";
        return record.LogSha256 is null ? "Unverified" : Wire.FileHash(record.Intent.LogFile) == record.LogSha256 ? "Unchanged" : "Changed";
    }
}
