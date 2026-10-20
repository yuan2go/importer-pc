using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    internal bool InitialRepairResponsibilitiesClear(FlowDefinition flow)
    {
        ValidateRawInventory();
        if (PendingReceipts().Length != 0 || LoadConflicts().Length != 0) return false;
        // Include immutable archives and image/attached/unsent responsibility, not
        // just the resident working set or the absence of an outbound request.
        long cursor = long.MaxValue;
        do
        {
            var page = History(cursor, 200);
            foreach (var item in page.Items)
            {
                var op = item.Operation;
                if (!(flow.MainSettled(op) || op.State is ParcelState.Abandoned or ParcelState.ImageRejected) || op.RecoveryRequired ||
                    op.FrozenConfiguration.Images is not null && !op.ImageReleased ||
                    new[] { op.Wcs, op.Plc, op.Capture, op.Upload }.OfType<StageFact>().Any(x => x.State is not (StageState.Accepted or StageState.Rejected or StageState.Cancelled))) return false;
            }
            cursor = page.NextBeforeSequence ?? 0;
        } while (cursor != 0);
        return true;
    }
}

public sealed partial class ParcelStartupMaintenance
{
    public Task<BarcodeFilterResult> TestBarcodeFilterAsync(BarcodeFilterPolicy policy, string[] candidates) => Task.FromResult(BarcodeFiltering.Select(policy, candidates.ToArray()));
    public async Task<StartupCheckReport> CheckAsync(ParcelConfiguration candidate)
    {
        var state = ReadState(); RequireIdentity(state.Envelope?.Effective ?? state.Seed, candidate);
        var errors = definition.Validate(candidate); List<StartupCheck> checks = [new("结构与业务字段", errors.Length == 0 ? "PASS" : "FAIL", string.Join("; ", errors))];
        await using var ports = factory(); candidate = ports.Normalize(candidate);
        if (errors.Length == 0)
        {
            try { ports.ValidateStructure(candidate); checks.Add(new("精确包与参数闭包", "PASS", "静态检查；未加载模块/SDK")); }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException) { checks.Add(new("精确包与参数闭包", "FAIL", error.Message)); }
            try { ports.Validate(candidate); checks.Add(new("部署资格与提交条件", "PASS", "保存Pending和激活时仍会重新检查")); }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException) { checks.Add(new("部署资格与提交条件", "BLOCKED", error.Message)); }
        }
        checks.Add(new("真实连接与实体许可", "NOT_RUN", "本入口不启动设备或发送业务请求；未提供批准的独立探测合同"));
        return new(Wire.Hash(Wire.Encode(candidate)), DateTimeOffset.UtcNow, checks.ToArray());
    }
    void RequireInitialRepairClear(string directory, string database)
    {
        foreach (var marker in new[] { "overload.json", "RESTORE-INCOMPLETE.json", "RESTORED-ISOLATED.json" })
            if (File.Exists(Path.Combine(directory, marker))) throw new InvalidOperationException("原隔离/接收责任仍存在：" + marker);
        if (!File.Exists(database))
        {
            if (new[] { "receipts", "images" }.Any(p => Directory.Exists(Path.Combine(directory, p)) && Directory.EnumerateFileSystemEntries(Path.Combine(directory, p)).Any()))
                throw new InvalidDataException("缺少业务库但存在原件；不能以首配覆盖原责任");
            return;
        }
        var scratch = MaintenanceWorkspace();
        try
        {
            var sources = new[] { database, database + "-wal" }.Where(File.Exists).ToArray();
            if (!ParcelStorageVolume.Plan(scratch, sources.Sum(p => new FileInfo(p).Length), 67108864).Fits) throw new IOException("原责任检查副本空间不足");
            foreach (var source in sources) { RecoveryFiles.RequireOrdinaryAncestors(source); File.Copy(source, Path.Combine(scratch, Path.GetFileName(source)), false); }
            if (Directory.Exists(Path.Combine(directory, "receipts")))
                foreach (var file in RecoveryFiles.Enumerate(directory, "receipts"))
                { var target = RecoveryFiles.Resolve(scratch, file); Directory.CreateDirectory(Path.GetDirectoryName(target)!); RecoveryFiles.CopyVerified(RecoveryFiles.Resolve(directory, file), target, Wire.FileHash(RecoveryFiles.Resolve(directory, file))); }
            using var repository = new ParcelRepository(scratch, definition.Id, 8);
            if (!repository.InitialRepairResponsibilitiesClear(definition)) throw new InvalidOperationException("原作业、回执、图像或身份责任未清；须使用原发布核对，不允许初始配置替换");
        }
        finally { Directory.Delete(scratch, true); }
    }
    string DraftFile(string directory) => Path.Combine(directory, "startup-maintenance", "editor-draft.bin");
    public Task<StartupSavedDraft?> ReadDraftAsync()
    {
        var state = ReadState(); var file = DraftFile(state.Directory);
        if (!File.Exists(file)) return Task.FromResult<StartupSavedDraft?>(null);
        var draft = Wire.Decode<StartupSavedDraft>(ProtectedDraftPayload.Read(file));
        return Task.FromResult<StartupSavedDraft?>(draft with { DraftHash = Wire.FileHash(file), BaseChanged = draft.BaseConfigurationHash != state.Hash || draft.SourceIdentity != Wire.FileHash(typeof(ParcelStartupMaintenance).Assembly.Location) });
    }
    public async Task<StartupSavedDraft> SaveDraftAsync(string expectedConfigurationHash, string? expectedDraftHash, StartupEditorState editor)
    {
        Authorize(); var state = ReadState();
        if (state.Hash != expectedConfigurationHash) throw new InvalidOperationException("原配置已变化；保留当前输入，重新读取比较后再保存草稿");
        if (editor.Values is null || editor.Values.Count > 4096 || editor.Values.Any(x => x.Key.Length > 256 || x.Value is null || x.Value.Length > 65536))
            throw new InvalidDataException("草稿字段超出有界配额");
        var file = DraftFile(state.Directory); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var lease = new ResourceLease(Path.GetDirectoryName(file)!, ["configuration-editor"]);
        if ((File.Exists(file) ? Wire.FileHash(file) : null) != expectedDraftHash) throw new InvalidOperationException("另一个窗口已保存草稿；当前输入保留，不能覆盖");
        var draft = new StartupSavedDraft(state.Hash, "", DateTimeOffset.UtcNow, editor, false, Wire.FileHash(typeof(ParcelStartupMaintenance).Assembly.Location));
        string? sid = null;
        if (OperatingSystem.IsWindows() && File.Exists(Path.Combine(WindowsDeploymentSetup.MachineRoot, "runtime-policy.json")))
        { var policy = Path.Combine(WindowsDeploymentSetup.MachineRoot, "runtime-policy.json"); MachineRuntimePolicy.RequireProtectedEvidence(policy); sid = Wire.Decode<MachineRuntimePolicy>(File.ReadAllText(policy)).RuntimeAccountSid; }
        if (File.Exists(file))
        {
            var history = Path.Combine(Path.GetDirectoryName(file)!, "editor-history"); Directory.CreateDirectory(history);
            if (Directory.EnumerateFiles(history).Take(128).Count() == 128) throw new IOException("草稿历史达到128份预算，先在获授权范围保全并维护草稿历史；不自动删除秘密或旧输入");
            // Re-encrypt via the same confidential writer rather than widening an old ACL.
            ProtectedDraftPayload.Write(Path.Combine(history, Wire.Id() + ".bin"), ProtectedDraftPayload.Read(file), state.Seed.Mode == RunMode.Simulation, sid);
        }
        ProtectedDraftPayload.Write(file, Wire.Encode(draft), state.Seed.Mode == RunMode.Simulation, sid);
        return (await ReadDraftAsync())!;
    }
}
