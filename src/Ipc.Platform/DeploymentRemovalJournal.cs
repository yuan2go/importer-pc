using System.Runtime.Versioning;
using System.Security.Principal;

namespace Ipc.Platform;

public static partial class WindowsDeploymentSetup
{
    public static string RemovalOperationFile(string application, string operationId)
    {
        if (!ExtensionSchema.IsId(application) || !Guid.TryParseExact(operationId, "N", out var id) || id == Guid.Empty || id.ToString("N") != operationId)
            throw new InvalidDataException("卸载缺少固定且有效的原操作身份。");
        return Path.Combine(MachineRoot, "deployments", application + ".remove-" + operationId + ".json");
    }

    static (string File, RemovalState State)[] RemovalRecords(string application)
    {
        if (!ExtensionSchema.IsId(application)) throw new InvalidDataException("无效的应用身份。");
        var files = Directory.EnumerateFiles(Path.Combine(MachineRoot, "deployments"), application + ".remove-*.json").Take(129).ToArray();
        if (files.Length > 128) throw new InvalidDataException("原卸载记录超过核对预算；保留原件。");
        long bytes = 0;
        return files.Select(file =>
        {
            MachineRuntimePolicy.RequireProtectedEvidence(file);
            var length = new FileInfo(file).Length; bytes = checked(bytes + length);
            if (length > 4 * 1024 * 1024 || bytes > 16 * 1024 * 1024) throw new InvalidDataException("原卸载记录超出读取预算。");
            var record = Wire.Decode<RemovalState>(File.ReadAllText(file));
            ValidateRemovalJournal(application, file, record);
            return (file, record);
        }).ToArray();
    }

    internal static void ValidateRemovalJournal(string application, string file, RemovalState record)
    {
        if (record.Location is null) throw new InvalidDataException("原卸载缺少部署身份。");
        ValidateRemovalState(record, record.Location);
        if (record.Location.ApplicationId != application || record.Location.LifecycleState != "Installed" ||
            record.Status is not ("Prepared" or "NotCommitted" or "RemovedDataRetained" or "RemovedWithRetainedFiles" or "RestartRequired") ||
            record.OperationId is { } operation && !string.Equals(Path.GetFileName(file), application + ".remove-" + operation + ".json", StringComparison.Ordinal) ||
            record.ExpectedRegistrationHash is { } hash && (hash.Length != 64 || !hash.All(char.IsAsciiHexDigit)))
            throw new InvalidDataException("原卸载记录身份或状态无效；保留原件。");
        if (record.OperationId is not null) _ = RemovalOperationFile(application, record.OperationId);
    }

    static void RequireNoUnresolvedRemoval(string application)
    {
        if (RemovalRecords(application).Any(item => item.State.Status == "Prepared"))
            throw new InstallationOutcomeUnknownException("存在尚未核对结束的原卸载，请核对原操作，不创建第二次卸载或切换版本。");
    }

    internal static WindowsRemovalReceipt ProjectRemoval(RemovalState record, string file, DeploymentLocation current, string currentHash)
    {
        ValidateRemovalJournal(current.ApplicationId, file, record);
        var retired = record.Location with { LifecycleState = "RemovedDataRetained", MaintenanceOperation = file };
        var sameRetired = Wire.Encode(retired) == Wire.Encode(current);
        string status;
        if (record.Status == "NotCommitted") status = "NotCommitted";
        else if (sameRetired) status = record.Status == "Prepared" ? "RetiredCleanupPending" : record.Status;
        else if (record.Status == "Prepared" && record.OperationId is not null && record.ExpectedRegistrationHash == currentHash &&
            Wire.Encode(current) == Wire.Encode(record.Location)) status = "NotCommittedNeedsClosure";
        else status = "Unknown";
        return new(status, file, [record.Location.DataRoot, record.Location.ConfigurationDirectory], record.RestartRequired,
            record.Remaining ?? [], record.Location.ReleaseRoot)
        { OperationId = record.OperationId, WithoutBusinessInspection = record.WithoutBusinessInspection };
    }

    // Called only with the deployment lease held exclusively. It is an explicit
    // maintenance action, separate from read-only inspection and from retrying.
    internal static RemovalState CloseRemovalBeforeCommit(RemovalState record, string file, DeploymentLocation current,
        string currentHash, Action removeMatchingRetirementMarker)
    {
        if (ProjectRemoval(record, file, current, currentHash).Status != "NotCommittedNeedsClosure")
            throw new InvalidOperationException("无法证明原卸载未提交，保持Unknown和原记录。");
        removeMatchingRetirementMarker();
        return record with { Status = "NotCommitted" };
    }

    [SupportedOSPlatform("windows")]
    public static WindowsRemovalReceipt CloseUncommittedRemoval(string application, string operationId, string expectedHash)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var file = RemovalOperationFile(application, operationId);
        using var guard = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var record = RemovalRecords(application).Single(item => item.File == file).State;
        var current = Current(application) ?? throw new InvalidDataException("原登记不可读，保留原件。");
        var hash = Wire.FileHash(Registration(application));
        if (hash != expectedHash) throw new InvalidOperationException("核对后登记发生变化，请重新核对。");
        var closed = CloseRemovalBeforeCommit(record, file, current, hash, () =>
        {
            var marker = Path.Combine(current.ReleaseRoot, "deployment-retired.json");
            DeploymentPaths.RequireOrdinaryAncestors(marker);
            if (!File.Exists(marker)) return;
            MachineRuntimePolicy.RequireProtectedEvidence(marker);
            if (new FileInfo(marker).Length > 1024 * 1024 ||
                File.ReadAllText(marker) != Wire.Encode(record.Location with { LifecycleState = "RemovedDataRetained", MaintenanceOperation = file }))
                throw new InvalidDataException("退役标记不属于这次原卸载，保留标记和原记录。");
            File.Delete(marker);
        });
        var sid = new SecurityIdentifier(current.RuntimeAccountSid);
        var archive = file + ".pre-close-" + Wire.FileHash(file)[..12] + ".archive";
        if (!File.Exists(archive)) WriteProtectedBytes(archive, File.ReadAllBytes(file), sid, overwrite: false);
        else { MachineRuntimePolicy.RequireProtectedEvidence(archive); if (Wire.FileHash(archive) != Wire.FileHash(file)) throw new InvalidDataException("原记录归档不一致。"); }
        WriteProtected(file, closed, sid);
        return ProjectRemoval(closed, file, current, hash);
    }
}
