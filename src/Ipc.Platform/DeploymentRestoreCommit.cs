using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;

namespace Ipc.Platform;

public sealed record DeploymentRestoreCommit(string Status, string OperationId, DeploymentLocation Previous,
    DeploymentLocation Next, string HandoffSha256, string? SupervisionLedgerSha256, string OriginalSeedBase64, string RestoredSeed);

public static partial class WindowsDeploymentSetup
{
    static string RestoreCommitFile(string app, string id)
    {
        _ = RollbackFile(app, id);
        return Path.Combine(MachineRoot, "deployments", app + ".migration-" + id + ".json");
    }
    static DeploymentRestoreCommit ReadRestoreCommit(string app, string id)
    {
        var file = RestoreCommitFile(app, id); MachineRuntimePolicy.RequireProtectedEvidence(file);
        if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("恢复接管记录超过预算。");
        var record = Wire.Decode<DeploymentRestoreCommit>(File.ReadAllText(file));
        ValidateRestoreCommit(record, app, file); return record;
    }
    internal static void ValidateRestoreCommit(DeploymentRestoreCommit record, string app, string file)
    {
        if (record.Status is not ("Prepared" or "CompletedIsolated") || record.OperationId is null || record.Previous is null || record.Next is null ||
            file != RestoreCommitFile(app, record.OperationId) || record.Previous.ApplicationId != app || record.Next.ApplicationId != app ||
            record.Next.MaintenanceOperation != file || record.Next.RestoreHandoffHash != record.HandoffSha256 || !ExtensionSchema.IsHash(record.HandoffSha256) ||
            record.Next.ReleaseRoot != record.Previous.ReleaseRoot || record.Next.ConfigurationDirectory != record.Previous.ConfigurationDirectory ||
            record.Next.RuntimeAccountSid != record.Previous.RuntimeAccountSid || record.Next.DataRoot == record.Previous.DataRoot ||
            string.IsNullOrWhiteSpace(record.RestoredSeed))
            throw new InvalidDataException("原恢复接管记录身份冲突，保留原件。");
        _ = Convert.FromBase64String(record.OriginalSeedBase64);
    }
    internal static string RestoreCommitState(DeploymentRestoreCommit record, DeploymentLocation? current)
        => Wire.Encode(current) == Wire.Encode(record.Next) ? record.Status == "CompletedIsolated" ? "AdoptedIsolated" : "ProjectionPending"
            : Wire.Encode(current) == Wire.Encode(record.Previous) ? "HandoffPrepared" : "Superseded";

    public static string? RestoreCommitStatus(string app, string id)
        => File.Exists(RestoreCommitFile(app, id)) ? RestoreCommitState(ReadRestoreCommit(app, id), Current(app)) : null;

    static void RequireNoUnresolvedRestore(string app)
    {
        var directory = Path.Combine(MachineRoot, "deployments");
        if (!Directory.Exists(directory)) return;
        var files = Directory.EnumerateFiles(directory, app + ".migration-*.json").Take(129).ToArray();
        if (files.Length > 128) throw new InvalidDataException("恢复接管记录超过核对预算。");
        var current = Current(app);
        foreach (var file in files)
        {
            var id = Path.GetFileNameWithoutExtension(file)[(app.Length + ".migration-".Length)..];
            var record = ReadRestoreCommit(app, id);
            if (record.Status == "Prepared" && RestoreCommitState(record, current) != "Superseded")
                throw new InstallationOutcomeUnknownException("原恢复接管尚未收口，请在离线维护读取原恢复并修复入口；不重新安装或启动。");
        }
    }

    [SupportedOSPlatform("windows")]
    public static void CommitRestore(string app, string expectedRegistrationHash, string newRoot, string handoffHash,
        string operation, string restoredSeed, DesktopRecoveryMigration recovery)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var file = RestoreCommitFile(app, operation);
        var current = Current(app) ?? throw new InvalidDataException("缺少当前登记。");
        recovery.RequireStoppedSource(app, current.DataRoot);
        recovery.RequireRegistration(Registration(app), expectedRegistrationHash);
        recovery.Require(current, newRoot);
        if (Wire.FileHash(Registration(app)) != expectedRegistrationHash || current.LifecycleState != "Installed" || !ExtensionSchema.IsHash(handoffHash))
            throw new InvalidOperationException("接管登记已改变，核对原结果。");
        DeploymentRestoreCommit record;
        if (File.Exists(file))
        {
            record = ReadRestoreCommit(app, operation);
            if (RestoreCommitState(record, current) != "HandoffPrepared" || record.HandoffSha256 != handoffHash || record.RestoredSeed != restoredSeed ||
                record.Next.DataRoot != Path.GetFullPath(newRoot) || record.SupervisionLedgerSha256 != recovery.LedgerSha256)
                throw new InvalidDataException("原恢复提交与本次证据冲突，不覆盖原操作。");
        }
        else
        {
            var seed = Path.Combine(current.ConfigurationDirectory, "business.json"); MachineRuntimePolicy.RequireProtectedEvidence(seed);
            if (new FileInfo(seed).Length > 1024 * 1024) throw new InvalidDataException("启动配置超过预算。");
            var next = current with { DataRoot = Path.GetFullPath(newRoot), MaintenanceOperation = file, RestoreHandoffHash = handoffHash,
                PreviousDeployment = current.PreviousDeployment is { } previous ? previous with { DataRoot = Path.GetFullPath(newRoot), RestoreHandoffHash = handoffHash } : null };
            record = new("Prepared", operation, current, next, handoffHash, recovery.LedgerSha256, Convert.ToBase64String(File.ReadAllBytes(seed)), restoredSeed);
            WriteProtected(file, record, new SecurityIdentifier(current.RuntimeAccountSid));
        }
        CompleteRestoreCommit(record);
    }

    [SupportedOSPlatform("windows")]
    public static void RepairRestoreCommit(string app, string operation, string expectedBackupIdentity)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var current = Current(app) ?? throw new InvalidDataException("缺少当前登记。");
        using var stopped = new StoppedApplicationOwnership(MachineRuntimePolicy.InstallerLeaseRoot(""), app, current.DataRoot, default);
        using var owner = new FileStream(Path.Combine(MachineRoot, "deployments", app + ".lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var record = ReadRestoreCommit(app, operation);
        var intent = ReadRestoreIntents(app).Single(i => i.OperationId == operation);
        if (intent.BackupIdentity != expectedBackupIdentity || RestoreCommitState(record, Current(app)) is not ("ProjectionPending" or "AdoptedIsolated"))
            throw new InvalidOperationException("原恢复尚未提交或已被后续部署取代，不能修复为当前入口。");
        CompleteRestoreCommit(record);
    }

    [SupportedOSPlatform("windows")]
    static void CompleteRestoreCommit(DeploymentRestoreCommit record)
    {
        var sid = new SecurityIdentifier(record.Next.RuntimeAccountSid);
        var seed = Path.Combine(record.Next.ConfigurationDirectory, "business.json"); MachineRuntimePolicy.RequireProtectedEvidence(seed);
        PreviewDataAccess(record.Next.DataRoot, record.Next.RuntimeAccountSid);
        ManagementModeBroker.CopyCredentialsForMigration(record.Previous.DataRoot, record.Next.DataRoot);
        ManagementModeBroker.Install(record.Next.DataRoot, Path.Combine(record.Next.ReleaseRoot, "app"));
        ExecuteRestoreCommit(record, () => Current(record.Next.ApplicationId), () => File.ReadAllBytes(seed),
            bytes => WriteProtectedBytes(seed, bytes, sid),
            location => WriteProtected(Path.Combine(location.ReleaseRoot, "deployment-location.json"), location, sid),
            location => WriteProtected(Registration(location.ApplicationId), location, sid),
            () => { ProjectInstalledApplication(record.Next); ManagementModeBroker.RemoveIfUnused(record.Previous.DataRoot); },
            completed => WriteProtected(RestoreCommitFile(record.Next.ApplicationId, record.OperationId), completed, sid));
    }

    internal static void ExecuteRestoreCommit(DeploymentRestoreCommit record, Func<DeploymentLocation?> current, Func<byte[]> seed,
        Action<byte[]> writeSeed, Action<DeploymentLocation> descriptor, Action<DeploymentLocation> registration,
        Action projections, Action<DeploymentRestoreCommit> save)
    {
        if (RestoreCommitState(record, current()) == "Superseded") throw new InvalidOperationException("后续部署已取代原接管，不覆盖当前登记。");
        var original = Convert.FromBase64String(record.OriginalSeedBase64); var candidate = Encoding.UTF8.GetBytes(record.RestoredSeed);
        var actual = seed();
        if (!actual.SequenceEqual(original) && !actual.SequenceEqual(candidate)) throw new InvalidDataException("启动种子在原恢复之外发生变化，保留原件并核对。");
        // Prepared is durable before either projection changes. The launch guard
        // rejects this boundary after a process loss; re-entry finishes this ID.
        writeSeed(candidate); descriptor(record.Next); registration(record.Next); projections(); save(record with { Status = "CompletedIsolated" });
    }
}
