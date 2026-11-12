using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

// The controlled boundary is machine evidence, not the recovery decision itself.
internal sealed record RestoreMachineEvidence(Func<string> PolicyIdentity, Func<string, DeploymentLocation?> Deployment,
    Action<DeploymentPurgedRestoreSource> VerifyPurge,
    Func<DeploymentLocation, string, string, string, DeploymentPurgedRestoreSource> PurgedSource)
{
    public bool RequireRegistration { get; init; } = OperatingSystem.IsWindows();
    public static RestoreMachineEvidence For(ParcelConfiguration c) => new(
        () => OperatingSystem.IsWindows() ? ProtectedPolicyIdentity()
            : c.Mode == RunMode.Simulation ? "PortableSimulation" : throw new InvalidOperationException("生产恢复须核验真实机器身份。"),
        app => OperatingSystem.IsWindows() ? WindowsDeploymentSetup.Current(app) : null,
        WindowsDeploymentSetup.VerifyPurgedRestoreSource, WindowsDeploymentSetup.RequirePurgedRestoreSource);
    static string ProtectedPolicyIdentity()
    {
        var file = Path.Combine(WindowsDeploymentSetup.MachineRoot, "runtime-policy.json");
        MachineRuntimePolicy.RequireProtectedEvidence(file);
        if (new FileInfo(file).Length > 65536) throw new InvalidDataException("机器政策超过核对预算。");
        return Wire.FileHash(file);
    }
}

internal static class ParcelRestoreEvidence
{
    internal sealed record Confirmation(string HandoffId, string Actor, string Reason, DateTimeOffset At, long VerifiedRequestIdHighWater);
    internal static bool ConfirmationValid(string directory, Func<StartupRestoreHandoff> readHandoff, Func<string, string, bool> committed)
    {
        var file = Path.Combine(directory, "RESTORE-CONFIRMED.json");
        if (!File.Exists(file)) return false;
        try
        {
            var proof = readHandoff(); var json = Read(file); var confirmation = Wire.Decode<Confirmation>(json);
            return confirmation.HandoffId == proof.Id && confirmation.VerifiedRequestIdHighWater >= proof.MinimumIntegrationId && committed(proof.Id, Wire.Hash(json));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException) { return false; }
    }
    internal static string Read(string file)
    {
        RecoveryFiles.RequireOrdinaryAncestors(file);
        if (new FileInfo(file).Length > 65536) throw new InvalidDataException("恢复交接证据超出预算。");
        return File.ReadAllText(file);
    }
    internal static void RequireDeploymentBinding(StartupRestoreHandoff proof, string directory, ParcelConfiguration c, RestoreMachineEvidence evidence)
    {
        if (!Guid.TryParseExact(proof.Id, "N", out var id) || id == Guid.Empty || proof.TargetDirectory != Path.GetFullPath(directory) ||
            proof.MachinePolicyHash != evidence.PolicyIdentity() || !ExtensionSchema.IsHash(proof.BackupIdentity) || proof.MinimumIntegrationId < 0)
            throw new InvalidDataException("恢复目标、原身份或机器证据不匹配，保持隔离。");
        if (!evidence.RequireRegistration) return;
        var deployment = evidence.Deployment(c.ApplicationId);
        if (deployment?.LifecycleState != "Installed" || deployment.RestoreHandoffHash != Wire.Hash(Wire.Encode(proof)) ||
            Path.GetFullPath(Path.Combine(deployment.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId)) != Path.GetFullPath(directory))
            throw new InvalidDataException("缺少本机登记的同一恢复接管身份，保持隔离。");
    }
    internal static StartupRestoreHandoff Require(string directory, ParcelConfiguration c, RestoreMachineEvidence evidence, bool requireRegistration = true, bool requireSourceFence = true)
    {
        var proof = Wire.Decode<StartupRestoreHandoff>(Read(Path.Combine(directory, "restore-handoff.json")));
        if (!Path.IsPathFullyQualified(proof.SourceDirectory) || proof.SourceDirectory.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException("原机退役路径必须是本地绝对目录。");
        if (proof.PurgedSource is null) RecoveryFiles.RequireOrdinaryAncestors(proof.SourceDirectory);
        var manifestFile = Path.Combine(directory, "RESTORED-ISOLATED.json");
        RecoveryFiles.RequireOrdinaryAncestors(manifestFile);
        if (new FileInfo(manifestFile).Length > RecoveryFiles.MaximumManifestBytes || Wire.FileHash(manifestFile) != proof.TargetManifestHash)
            throw new InvalidDataException("原恢复完成标记缺失或变化，保持隔离。");
        var manifest = Wire.Decode<ParcelRecoveryManifest>(File.ReadAllText(manifestFile));
        if (!manifest.Files.TryGetValue(ParcelMachineOrigin.FileName, out var originHash) || !manifest.Files.TryGetValue("configuration.json", out var configHash) ||
            configHash != proof.SourceConfigurationHash || Wire.FileHash(Path.Combine(directory, ParcelMachineOrigin.FileName)) != originHash ||
            Wire.Hash(manifest.DatabaseHash + proof.TargetManifestHash) != proof.BackupIdentity)
            throw new InvalidDataException("恢复集的原机器、配置或备份身份不一致，保持隔离。");
        if (proof.PurgedSource is { } purged)
        {
            evidence.VerifyPurge(purged);
            if (purged.SourceDirectory != proof.SourceDirectory || purged.ConfigurationSha256 != configHash || purged.MachineOriginSha256 != originHash)
                throw new InvalidDataException("原数据清理回执与恢复交接不一致，保持隔离。");
        }
        else
        {
            ParcelMachineOrigin.Require(proof.SourceDirectory, c, evidence, requireCurrentRegistration: false);
            if (Wire.FileHash(Path.Combine(proof.SourceDirectory, ParcelMachineOrigin.FileName)) != originHash ||
                Wire.FileHash(Path.Combine(proof.SourceDirectory, "configuration.json")) != proof.SourceConfigurationHash ||
                requireSourceFence && Wire.Encode(Wire.Decode<StartupRestoreHandoff>(Read(Path.Combine(proof.SourceDirectory, "MIGRATION-SOURCE-FENCED.json")))) != Wire.Encode(proof))
                throw new InvalidDataException("原机退役围栏或原件与恢复交接不一致，保持隔离。");
        }
        if (requireRegistration) RequireDeploymentBinding(proof, directory, c, evidence);
        else if (proof.TargetDirectory != Path.GetFullPath(directory) || proof.MachinePolicyHash != evidence.PolicyIdentity())
            throw new InvalidDataException("原待接管恢复的目标或机器身份不一致。");
        return proof;
    }
}
