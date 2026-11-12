using System.Text.Json;

namespace Ipc.Platform;

public sealed record DeploymentPurgedRestoreSource(DeploymentLocation Retired, string ReceiptFile, string ReceiptSha256,
    string SourceDirectory, string ConfigurationSha256, string MachineOriginSha256, string DatabaseSha256, string? WalSha256);

public static partial class WindowsDeploymentSetup
{
    // A deleted directory cannot furnish a new claim. Its exact protected purge
    // inventory and the installation's recorded predecessor establish custody.
    public static DeploymentPurgedRestoreSource RequirePurgedRestoreSource(DeploymentLocation current, string sourceDirectory,
        string configurationHash, string originHash)
    {
        if (current.ApplicationId != "sportmaster" || current.LifecycleState != "Installed") throw new InvalidOperationException("先完成 SM 安装，再核对清理后的恢复来源。");
        var directory = Path.Combine(MachineRoot, "deployments");
        var files = Directory.EnumerateFiles(directory, "sportmaster.setup-*.json").Take(1025).ToArray();
        if (files.Length > 1024) throw new InvalidDataException("安装来源记录超过核对预算。");
        var originals = new List<(WindowsDeploymentRequest Request, string Json)>(); long bytes = 0;
        foreach (var file in files)
        {
            if (Path.GetFileName(file).Contains(".json.pre-system-repair-", StringComparison.Ordinal)) continue;
            MachineRuntimePolicy.RequireProtectedEvidence(file);
            var size = new FileInfo(file).Length; bytes = checked(bytes + size);
            if (size > 4 * 1024 * 1024 || bytes > 64 * 1024 * 1024) throw new InvalidDataException("安装来源记录超过读取预算。");
            var json = File.ReadAllText(file); using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Request", out _) || doc.RootElement.GetProperty("Status").GetString() is not ("Completed" or "CommittedNeedsSystemRepair")) continue;
            var operation = Path.GetFileName(file)["sportmaster.setup-".Length..^5];
            var request = ReadInstallationRequest("sportmaster", operation, json);
            var changePlan = doc.RootElement.TryGetProperty("ChangePlanIdentity", out var plan) ? plan.GetString() : null;
            originals.Add((request with { ChangePlanIdentity = changePlan }, json));
        }
        var cursor = current; var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var count = 0; count < 16; count++)
        {
            if (cursor.DeploymentPlanIdentity is null || !visited.Add(cursor.DeploymentPlanIdentity)) break;
            var candidates = originals.Where(r => Wire.Hash(Wire.Encode(r.Request)) == cursor.DeploymentPlanIdentity).ToArray();
            if (candidates.Length != 1) break;
            using var doc = JsonDocument.Parse(candidates[0].Json);
            if (!doc.RootElement.TryGetProperty("Location", out var location) || !doc.RootElement.TryGetProperty("Previous", out var previous) || previous.ValueKind == JsonValueKind.Null) break;
            var recorded = Wire.Decode<DeploymentLocation>(location.GetRawText());
            if (recorded.ReleaseRoot != cursor.ReleaseRoot || recorded.SourceCommit != cursor.SourceCommit || recorded.DataRoot != cursor.DataRoot || recorded.RuntimeAccountSid != current.RuntimeAccountSid)
                throw new InvalidDataException("安装来源与当前登记冲突，不能接管已删除来源。");
            var retired = Wire.Decode<DeploymentLocation>(previous.GetRawText());
            if (retired.LifecycleState == "Purged")
            {
                var file = retired.MaintenanceOperation ?? throw new InvalidDataException("退役来源缺少原数据清理回执。");
                MachineRuntimePolicy.RequireProtectedEvidence(file);
                if (new FileInfo(file).Length > 8 * 1024 * 1024) throw new InvalidDataException("来源清理回执超过预算。");
                var station = Directory.GetParent(sourceDirectory) ?? throw new InvalidDataException("恢复站点路径缺失。");
                var mode = station.Parent ?? throw new InvalidDataException("恢复模式路径缺失。");
                if (!Enum.TryParse<RunMode>(mode.Name, out _) || !ExtensionSchema.IsId(station.Name)) throw new InvalidDataException("恢复站点路径不合法。");
                var retiredSource = Path.Combine(retired.DataRoot, mode.Name, station.Name, "sportmaster");
                return ProjectPurgedRestoreSource(retired, file, File.ReadAllText(file), Wire.FileHash(file), retiredSource, configurationHash, originHash);
            }
            if (retired.LifecycleState != "Installed") break;
            cursor = retired;
        }
        throw new InvalidDataException("没有可核验的本站清理后重装关系；原备份和隔离副本保留，不能猜测来源已退役。");
    }

    internal static DeploymentPurgedRestoreSource ProjectPurgedRestoreSource(DeploymentLocation retired, string path, string json, string receiptHash,
        string sourceDirectory, string configurationHash, string originHash)
    {
        var record = Wire.Decode<DeploymentPurgeRecord>(json);
        if (retired.ApplicationId != "sportmaster" || retired.LifecycleState != "Purged" || record.Status != "Purged" || record.Remaining.Length != 0 ||
            record.Plan.Identity != Wire.Hash(Wire.Encode(new { record.Plan.ApplicationId, operation = record.Plan.OperationId, registrationHash = record.Plan.ExpectedRegistrationHash, delete = record.Plan.Delete })) ||
            path != PurgeFile(retired.ApplicationId, record.Plan.OperationId) || !ExtensionSchema.IsHash(receiptHash))
            throw new InvalidDataException("来源数据删除没有明确终态，不能代替原机退役证据。");
        _ = ProjectDataPurge(record, path, retired);
        sourceDirectory = Path.GetFullPath(sourceDirectory);
        if (!TrustedProgramRoots.Contains(retired.DataRoot, sourceDirectory) || sourceDirectory == Path.GetFullPath(retired.DataRoot) || Path.GetFileName(sourceDirectory) != "sportmaster")
            throw new InvalidDataException("备份来源不属于原数据清理范围。");
        var area = record.Plan.Delete.Single(a => Path.GetFullPath(a.Root) == Path.GetFullPath(retired.DataRoot));
        var prefix = Path.GetRelativePath(retired.DataRoot, sourceDirectory).Replace(Path.DirectorySeparatorChar, '/') + "/";
        if (!area.Files.TryGetValue(prefix + "configuration.json", out var config) || config != configurationHash ||
            !area.Files.TryGetValue(prefix + "machine-origin.bin", out var origin) || origin != originHash ||
            !area.Files.TryGetValue(prefix + "sportmaster.db", out var database) || !ExtensionSchema.IsHash(database))
            throw new InvalidDataException("备份配置或原机器身份与受保护清理库存不一致，保持隔离。");
        area.Files.TryGetValue(prefix + "sportmaster.db-wal", out var wal);
        return new(retired, path, receiptHash, sourceDirectory, configurationHash, originHash, database, wal);
    }

    public static void VerifyPurgedRestoreSource(DeploymentPurgedRestoreSource proof)
    {
        var expectedDirectory = Path.Combine(MachineRoot, "deployments");
        if (Path.GetDirectoryName(proof.ReceiptFile) != expectedDirectory) throw new InvalidDataException("来源清理回执不在本机受保护范围。");
        MachineRuntimePolicy.RequireProtectedEvidence(proof.ReceiptFile);
        if (new FileInfo(proof.ReceiptFile).Length > 8 * 1024 * 1024 || Wire.FileHash(proof.ReceiptFile) != proof.ReceiptSha256)
            throw new InvalidDataException("原来源清理回执缺失或变化，保持隔离。");
        var actual = ProjectPurgedRestoreSource(proof.Retired, proof.ReceiptFile, File.ReadAllText(proof.ReceiptFile), proof.ReceiptSha256,
            proof.SourceDirectory, proof.ConfigurationSha256, proof.MachineOriginSha256);
        if (actual != proof) throw new InvalidDataException("恢复中的清理来源与原回执不一致。");
    }
}
