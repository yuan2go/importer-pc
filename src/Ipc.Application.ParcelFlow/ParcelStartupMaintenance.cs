using Ipc.Application.Contracts;
using Ipc.Platform;
using Ipc.Platform.Storage;

namespace Ipc.Application.ParcelFlow;

// Application-owned offline operations. Never constructs a runtime, port or SDK.
public sealed partial class ParcelStartupMaintenance(BusinessLaunch launch, FlowDefinition definition,
    Func<IParcelCapabilityFactory> factory, Action? requireAuthority = null, Func<string?>? authorizedActor = null) : IStartupMaintenance
{
    internal Func<string> MaintenanceWorkspace { get; set; } = MachineRuntimeSetup.CreateMaintenanceWorkspace;
    string Seed => Path.Combine(Path.GetFullPath(launch.ConfigurationDirectory), "business.json");
    void Authorize() { if (requireAuthority is not null) requireAuthority(); else LocalManagementMode.ForRoot(launch.DataRoot).Require(); }
    string Actor => authorizedActor?.Invoke() ?? LocalManagementMode.ForRoot(launch.DataRoot).Evidence?.AuditActor ?? Environment.UserName;
    static byte[] ReadBytes(string path)
    {
        RecoveryFiles.RequireOrdinaryAncestors(path);
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Configuration exceeds maintenance quota");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Configuration exceeds maintenance quota");
        return bytes;
    }
    static string Text(byte[] bytes) { using var reader = new StreamReader(new MemoryStream(bytes)); return reader.ReadToEnd(); }
    static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    (ParcelConfiguration Seed, ConfigurationRevision<ParcelConfiguration>? Envelope, string Directory, string File, string Hash) ReadState(bool authorize = true)
    {
        if (authorize) Authorize();
        var seedMissing = !File.Exists(Seed);
        var seed = seedMissing ? definition.InitialConfiguration() : Wire.Decode<ParcelConfiguration>(Text(ReadBytes(Seed)));
        var seedHash = seedMissing ? "MissingSeed:" + Wire.Hash(Wire.Encode(seed)) : Hash(ReadBytes(Seed));
        // Reading an editor cannot require all runtime parameters to be valid.
        // Only the identity needed to locate the original authority is required here.
        if (seed.ApplicationId != definition.Id || !ExtensionSchema.IsId(seed.StationId) || !Enum.IsDefined(seed.Mode))
            throw new InvalidDataException("配置身份无法定位；可选择原配置/完整备份，不能猜测原业务目录");
        var directory = Path.GetFullPath(Path.Combine(launch.DataRoot, seed.Mode.ToString(), seed.StationId, definition.Id));
        RecoveryFiles.RequireOrdinaryAncestors(directory);
        var file = Path.Combine(directory, "configuration.json");
        if (!File.Exists(file))
        {
            if (File.Exists(Path.Combine(directory, definition.Id + ".db")))
                throw new InvalidDataException("原数据库存在但配置账缺失；保留原目录，通过完整备份恢复，不创建新账");
            return (seed, null, directory, file, seedHash);
        }
        var bytes = ReadBytes(file);
        var envelope = ConfigurationRevisions<ParcelConfiguration>.ReadCurrentEnvelope(Text(bytes));
        RequireIdentity(seed, envelope.Effective);
        if (envelope.Pending is { } pending) RequireIdentity(seed, pending);
        return (seed, envelope, directory, file, Hash(bytes));
    }
    static void RequireIdentity(ParcelConfiguration original, ParcelConfiguration candidate)
    {
        if (original.ApplicationId != candidate.ApplicationId || original.StationId != candidate.StationId || original.Mode != candidate.Mode)
            throw new InvalidDataException("原Application、站和模式不能由启动修复改变");
        if (candidate.SchemaVersion != 3) throw new InvalidDataException("不得降级或摊平原拓扑");
    }
    public Task<StartupClassification> ClassifyAsync()
    {
        // Launch routing discloses no configuration values and creates no data/accounts.
        try { var state = ReadState(false); return Task.FromResult(new StartupClassification(state.Envelope is not null, true, [])); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { return Task.FromResult(new StartupClassification(false, false, ["原配置不可读取，请由维护人员进入管理模式打开启动修复。", error.GetType().Name])); }
    }
    public async Task<StartupMaintenanceDocument> ReadAsync()
    {
        Authorize();
        await using var ports = factory();
        try
        {
            var state = ReadState();
            var candidate = state.Envelope?.Pending ?? state.Envelope?.Effective ?? state.Seed;
            _ = ports.Normalize(candidate); // Select the original mode's inventory without constructing ports.
            return new(candidate, ports.Catalog(), state.Hash,
                state.Envelope?.Revision ?? 0, state.Envelope is not null, state.Directory,
                ["离线维护：未启动设备或业务；保存只生成待生效修订。", .. (state.Envelope?.Failure is { } failure ? new[] { failure } : Array.Empty<string>())], state.Envelope?.Status ?? (File.Exists(Seed) ? "Seed" : "MissingConfiguration"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        { return new(null, ports.Catalog(), "", 0, false, null, [ex.Message, "原文件保留；可安装修复程序、选择正确配置目录或恢复完整备份，不能覆盖损坏原账。"]); }
    }
    public async Task<BusinessReceipt> ValidateAsync(ParcelConfiguration candidate)
    {
        var state = ReadState();
        RequireIdentity(state.Envelope?.Effective ?? state.Seed, candidate);
        var errors = definition.Validate(candidate);
        if (errors.Length != 0) return new("Rejected", string.Join("; ", errors), state.Envelope?.Revision ?? 0);
        await using var ports = factory();
        candidate = ports.Normalize(candidate); ports.ValidateStructure(candidate);
        return new("Validated", "完整候选已静态校验；未启动设备、保存配置或改写数据库", state.Envelope?.Revision ?? 0);
    }
    public Task<StartupMaintenanceDocument> StageAsync(string expectedHash, ParcelConfiguration candidate, string reason) => StageCore(expectedHash, candidate, reason, false);
    public Task<StartupMaintenanceDocument> RepairInitialAsync(string expectedHash, ParcelConfiguration candidate, string reason) => StageCore(expectedHash, candidate, reason, true);
    async Task<StartupMaintenanceDocument> StageCore(string expectedHash, ParcelConfiguration candidate, string reason, bool initialRepair)
    {
        Authorize();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1024) throw new InvalidDataException("填写1～1024字符的维护变更原因");
        var state = ReadState();
        if (state.Hash != expectedHash) throw new InvalidOperationException("配置已变化；重新读取并比较，不覆盖其他维护修改");
        RequireIdentity(state.Envelope?.Effective ?? state.Seed, candidate);
        // Bootstrap is explicit maintenance over a missing seed, never a repair over old data.
        if (state.Envelope is null && (state.Hash.StartsWith("MissingSeed:", StringComparison.Ordinal) || definition.Validate(state.Seed).Length > 0))
        {
            using var missingOwner = new ResourceLease(Path.GetFullPath(launch.ConfigurationDirectory), ["initial-configuration"]);
            if (ReadState().Hash != expectedHash) throw new InvalidOperationException("配置已由其他窗口改变；输入保留，重新读取比较");
            if (Directory.Exists(launch.DataRoot) && Directory.EnumerateFiles(launch.DataRoot, "*.db", SearchOption.AllDirectories).Any())
                throw new InvalidOperationException("数据根已有原业务库，缺少原种子不能创建新身份；选择原配置或恢复完整备份，当前参数输入保留");
            var bootstrapErrors = definition.Validate(candidate);
            if (bootstrapErrors.Length != 0) throw new InvalidDataException(string.Join("; ", bootstrapErrors));
            await using var bootstrapFactory = factory(); candidate = bootstrapFactory.Normalize(candidate);
            bootstrapFactory.ValidateStructure(candidate);
            // Saving initial parameters is allowed without a live SDK/connection; real
            // deployment qualification still gates normal runtime startup.
            var bootstrapOperation = Path.Combine(Path.GetFullPath(launch.ConfigurationDirectory), "startup-maintenance", Wire.Id());
            Directory.CreateDirectory(bootstrapOperation);
            if (File.Exists(Seed)) File.Copy(Seed, Path.Combine(bootstrapOperation, "original-business.json"), false);
            ExtensionStore.AtomicWrite(Path.Combine(bootstrapOperation, "operation.json"), new { Actor = Actor, Reason = reason, BeforeHash = expectedHash, CandidateHash = Wire.Hash(Wire.Encode(candidate)), Status = "InitialParametersPrepared", OriginalMissing = !File.Exists(Seed) });
            Authorize(); ExtensionStore.AtomicWrite(Seed, candidate);
            return await ReadAsync();
        }
        // The actual application-owner lease excludes live/starting instances.
        using var owner = new ResourceLease(state.Directory, ["application-owner"]);
        var locked = ReadState();
        if (locked.Hash != expectedHash) throw new InvalidOperationException("配置在获取所有权时变化；保留当前草稿并重新读取");
        var database = Path.Combine(state.Directory, definition.Id + ".db");
        if (File.Exists(database) && ReadVersionFromCopy(database) != ParcelRepository.CurrentStorageVersion)
            throw new InvalidDataException("原库不是当前格式；原文件保留，请使用全新数据目录");
        if (initialRepair) RequireInitialRepairClear(state.Directory, database);
        var errors = definition.Validate(candidate);
        if (errors.Length != 0) throw new InvalidDataException(string.Join("; ", errors));
        await using var ports = factory();
        candidate = ports.Normalize(candidate);
        using var references = ports.ProtectConfiguration(candidate);
        ports.ValidateStructure(candidate);
        ParcelMachineOrigin.EnsureNew(state.Directory, state.Seed);
        var operation = Path.Combine(state.Directory, "startup-maintenance", Wire.Id()); Directory.CreateDirectory(operation);
        var source = state.Envelope is null ? Seed : state.File;
        using (var before = new FileStream(Path.Combine(operation, "original-configuration.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { before.Write(File.ReadAllBytes(source)); before.Flush(true); }
        var audit = new { At = DateTimeOffset.UtcNow, Reason = reason, BeforeHash = expectedHash, CandidateHash = Wire.Hash(Wire.Encode(candidate)), Actor = Actor, Status = "Prepared" };
        ExtensionStore.AtomicWrite(Path.Combine(operation, "operation.json"), audit);
        var revisions = new ConfigurationRevisions<ParcelConfiguration>(state.File, state.Seed, definition.Validate);
        if (initialRepair)
        {
            revisions.PrepareEffectiveConfirmation();
            revisions.RecoverInterruptedActivation();
            Authorize(); revisions.StageInitialRepair(revisions.Current.Revision, candidate, Actor, reason);
        }
        else { Authorize(); revisions.Stage(state.Envelope?.Revision ?? 0, candidate); }
        ExtensionStore.AtomicWrite(Path.Combine(operation, "operation.json"), new { audit.At, audit.Reason, audit.BeforeHash, audit.CandidateHash, audit.Actor, Status = "PendingRestart", AfterHash = Wire.FileHash(state.File) });
        return await ReadAsync();
    }
    int ReadVersionFromCopy(string database)
    {
        // SQLite read-only WAL connections may create SHM/WAL sidecars. Inspect a private
        // DB+WAL snapshot instead, while holding application-owner; never open the original DB.
        var sources = new[] { database, database + "-wal" }.Where(File.Exists).ToArray();
        foreach (var source in sources) RecoveryFiles.RequireOrdinaryAncestors(source);
        var scratch = MaintenanceWorkspace();
        try
        {
            if (!ParcelStorageVolume.Plan(scratch, sources.Sum(p => new FileInfo(p).Length), 67108864).Fits)
                throw new IOException("维护临时卷空间不足以无写入检查原库；保留原库，可先选择完整备份恢复到容量充足的新卷");
            foreach (var source in sources) File.Copy(source, Path.Combine(scratch, Path.GetFileName(source)), false);
            return ParcelRepository.ReadStorageVersion(Path.Combine(scratch, Path.GetFileName(database)), definition.Id);
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }
    public Task<StartupRecoveryPreview> PlanRestoreAsync(string backup, string destination)
    {
        Authorize();
        var original = BackupConfiguration(backup);
        var target = Path.Combine(Path.GetFullPath(destination), original.Mode.ToString(), original.StationId, definition.Id);
        var plan = ParcelRecovery.PlanRestore(backup, target);
        return Task.FromResult(new StartupRecoveryPreview(BackupIdentity(backup), target, Wire.Encode(plan), Path.GetFullPath(destination)));
    }
    public Task RestoreAsync(string backup, string destination, string expectedBackupHash)
    {
        Authorize();
        if (BackupIdentity(backup) != expectedBackupHash) throw new InvalidDataException("所选备份集已变化；重新读取恢复计划");
        Authorize();
        var original = BackupConfiguration(backup);
        var originalDirectory = Path.Combine(launch.DataRoot, original.Mode.ToString(), original.StationId, definition.Id);
        RecoveryFiles.RequireOrdinaryAncestors(originalDirectory);
        using var originalOwner = Directory.Exists(originalDirectory) ? new ResourceLease(originalDirectory, ["application-owner"]) : null;
        _ = ParcelRecovery.PlanRestore(backup, destination);
        if (OperatingSystem.IsWindows() && File.Exists(Path.Combine(WindowsDeploymentSetup.MachineRoot, "runtime-policy.json")))
        {
            var suffix = Path.Combine(original.Mode.ToString(), original.StationId, definition.Id);
            var full = Path.GetFullPath(destination);
            if (!full.EndsWith(Path.DirectorySeparatorChar + suffix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("恢复目标须为已预览的模式/站/Application完整路径");
            WindowsDeploymentSetup.PrepareRestoreDataRoot(full[..^(suffix.Length + 1)]);
        }
        ParcelRecovery.RestoreAuthorized(backup, destination, Authorize);
        return Task.CompletedTask;
    }
    string BackupIdentity(string backup) => Wire.Hash(Wire.FileHash(backup) + Wire.FileHash(Path.Combine(backup + ".recovery", "COMPLETED.json")));
    ParcelConfiguration BackupConfiguration(string backup)
    {
        var c = ConfigurationRevisions<ParcelConfiguration>.ReadCurrentEnvelope(Text(ReadBytes(Path.Combine(backup + ".recovery", "configuration.json")))).Effective;
        if (c.ApplicationId != definition.Id) throw new InvalidDataException("完整备份不属于当前Application");
        var seed = Wire.Decode<ParcelConfiguration>(Text(ReadBytes(Seed))); RequireIdentity(seed, c);
        var errors = definition.Validate(c); if (errors.Length != 0) throw new InvalidDataException(string.Join("; ", errors));
        return c;
    }
}
