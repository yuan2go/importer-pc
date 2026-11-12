using System.Runtime.Versioning;
using System.Security.Principal;

namespace Ipc.Platform;

// The immutable intent owns only a new isolated copy. Commit/enablement are
// derived separately from the existing handoff and deployment registration.
public sealed record DeploymentRestoreIntent(int SchemaVersion, string ApplicationId, string OperationId,
    DeploymentLocation Source, string Backup, string BackupIdentity, string TargetRoot, string TargetDirectory,
    DateTimeOffset At, string Identity);

public sealed class DeploymentRestoreOperation : IDisposable
{
    readonly IDisposable registration;
    readonly IDisposable stopped;
    public DeploymentRestoreIntent Intent { get; }
    internal DeploymentRestoreOperation(DeploymentRestoreIntent intent, IDisposable registration, IDisposable stopped)
    { Intent = intent; this.registration = registration; this.stopped = stopped; }
    public void Dispose()
    {
        // Keep the deployment writer blocked until the actual application owner
        // has released; a failed release remains an error for the helper owner.
        stopped.Dispose(); registration.Dispose();
    }
}

public static partial class WindowsDeploymentSetup
{
    static string RestoreIntentFile(string application, string operation)
    {
        _ = RollbackFile(application, operation);
        return Path.Combine(MachineRoot, "deployments", application + ".restore-" + operation + ".json");
    }

    internal static DeploymentRestoreIntent NewRestoreIntent(DeploymentLocation source, string backup, string backupIdentity, string targetRoot, string targetDirectory)
    {
        var intent = new DeploymentRestoreIntent(1, source.ApplicationId, Wire.Id(), source, Path.GetFullPath(backup), backupIdentity,
            Path.GetFullPath(targetRoot), Path.GetFullPath(targetDirectory), DateTimeOffset.UtcNow, "");
        return intent with { Identity = Wire.Hash(Wire.Encode(intent)) };
    }

    public static void ValidateRestoreIntent(DeploymentRestoreIntent intent)
    {
        if (intent.SchemaVersion != 1 || intent.ApplicationId != "sportmaster" || intent.Source is null || intent.Source.ApplicationId != intent.ApplicationId ||
            intent.Source.LifecycleState != "Installed" || !Guid.TryParseExact(intent.OperationId, "N", out var operation) || operation == Guid.Empty ||
            !ExtensionSchema.IsHash(intent.BackupIdentity) || intent.Identity != Wire.Hash(Wire.Encode(intent with { Identity = "" })) ||
            !Path.IsPathFullyQualified(intent.Backup) || !Path.IsPathFullyQualified(intent.TargetRoot) || !Path.IsPathFullyQualified(intent.TargetDirectory) ||
            !TrustedProgramRoots.Contains(intent.TargetRoot, intent.TargetDirectory) || intent.TargetRoot == intent.TargetDirectory || Path.GetFileName(intent.TargetDirectory) != intent.ApplicationId)
            throw new InvalidDataException("原恢复意图身份或隔离范围无法核验，原目录保留。");
    }

    public static DeploymentRestoreIntent[] ReadRestoreIntents(string application)
    {
        if (application != "sportmaster") throw new NotSupportedException("此恢复续办仅用于 SM。");
        var directory = Path.Combine(MachineRoot, "deployments");
        if (!Directory.Exists(directory)) return [];
        var files = Directory.EnumerateFiles(directory, application + ".restore-*.json").Take(129).ToArray();
        if (files.Length > 128) throw new InvalidDataException("恢复意图超过核对预算，原件保留。");
        var result = new List<DeploymentRestoreIntent>(); long bytes = 0;
        foreach (var file in files)
        {
            MachineRuntimePolicy.RequireProtectedEvidence(file);
            var length = new FileInfo(file).Length; bytes = checked(bytes + length);
            if (length > 4 * 1024 * 1024 || bytes > 32 * 1024 * 1024) throw new InvalidDataException("恢复意图超过读取预算。");
            var intent = Wire.Decode<DeploymentRestoreIntent>(File.ReadAllText(file)); ValidateRestoreIntent(intent);
            if (file != RestoreIntentFile(application, intent.OperationId)) throw new InvalidDataException("原恢复意图文件身份不一致。");
            result.Add(intent);
        }
        return result.OrderByDescending(i => i.At).ToArray();
    }

    [SupportedOSPlatform("windows")]
    public static DeploymentRestoreOperation PrepareRestoreIntent(string application, string backup, string backupIdentity, string targetRoot, string targetDirectory)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        if (application != "sportmaster") throw new NotSupportedException("此恢复续办仅用于 SM。");
        var observed = Current(application) ?? throw new InvalidDataException("缺少当前部署登记。");
        var stopped = new StoppedApplicationOwnership(MachineRuntimePolicy.InstallerLeaseRoot(""), application, observed.DataRoot, default);
        FileStream? lease = null;
        try
        {
            lease = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            if (Wire.Encode(Current(application)) != Wire.Encode(observed)) throw new InvalidOperationException("部署在取得恢复所有权时变化，原意图保留。");
            var intent = PrepareRestoreIntentCore(application, backup, backupIdentity, targetRoot, targetDirectory);
            return new(intent, lease, stopped);
        }
        catch { lease?.Dispose(); stopped.Dispose(); throw; }
    }

    public static IDisposable HoldRestoreInspection(string application)
    {
        if (application != "sportmaster") throw new NotSupportedException("此恢复核查仅用于 SM。");
        return new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.Read, FileShare.None);
    }

    [SupportedOSPlatform("windows")]
    static DeploymentRestoreIntent PrepareRestoreIntentCore(string application, string backup, string backupIdentity, string targetRoot, string targetDirectory)
    {
        var current = Current(application) ?? throw new InvalidDataException("缺少当前部署登记，不能准备本站恢复。");
        if (current.LifecycleState != "Installed") throw new InvalidOperationException("先完成当前安装，再恢复本站。");
        targetRoot = Path.GetFullPath(targetRoot); targetDirectory = Path.GetFullPath(targetDirectory);
        RequireDataStorage(targetRoot);
        var protectedRoots = new[] { current.DataRoot, current.ConfigurationDirectory, MachineRoot, MaintenanceCacheRoot,
            Path.GetDirectoryName(Path.GetDirectoryName(current.ReleaseRoot)!)!, Path.Combine(WindowsStoragePaths.ProtectedAnchor!, "Administrators") }
            .Concat(ReadVersionReferences(Path.Combine(MachineRoot, "deployments")).Where(d => d.ApplicationId != application)
                .SelectMany(d => new[] { d.DataRoot, d.ConfigurationDirectory, d.ReleaseRoot, d.MaintenanceRoot }).OfType<string>());
        if (protectedRoots.Any(root => TrustedProgramRoots.Contains(root, targetRoot) || TrustedProgramRoots.Contains(targetRoot, root)))
            throw new InvalidDataException("隔离恢复目录与原数据、程序、安全信息或其他安装重叠，未创建恢复意图。");
        var existing = ReadRestoreIntents(application).Where(i => string.Equals(i.TargetDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (existing.Length > 1) throw new InstallationOutcomeUnknownException("同一目标对应多个原恢复意图，须先核对。");
        if (existing.SingleOrDefault() is { } original)
        {
            if (original.BackupIdentity != backupIdentity || original.Backup != Path.GetFullPath(backup) || original.Source.ApplicationId != current.ApplicationId ||
                original.Source.RuntimeAccountSid != current.RuntimeAccountSid || Path.GetFullPath(original.Source.DataRoot) != Path.GetFullPath(current.DataRoot))
                throw new InvalidOperationException("原恢复的来源、目标或当前登记已改变；保留原意图，不创建新范围覆盖。");
            return original;
        }
        if (Directory.Exists(targetDirectory) || File.Exists(targetDirectory)) throw new InvalidOperationException("已有目标缺少受保护原恢复意图，不能覆盖或猜测续办。");
        var intent = NewRestoreIntent(current, backup, backupIdentity, targetRoot, targetDirectory); ValidateRestoreIntent(intent);
        WriteProtected(RestoreIntentFile(application, intent.OperationId), intent, new SecurityIdentifier(current.RuntimeAccountSid));
        return intent;
    }
}
