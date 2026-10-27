using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

public sealed record PositionPresentation(string Id, string DeviceId, string Availability, string Code, string Admission, string State, string Slot, string Reason, string MainResult, string Background, string StatusForeground, string StatusBackground, string StatusBorder, string StatusIcon, string InputSummary, string OriginalState)
{
    public bool CurrentSlotAvailable { get; init; }
    public bool HasCurrentInput { get; init; }
    public bool NeedsAttention { get; init; }
    public bool ShowStatus => !CurrentSlotAvailable || NeedsAttention;
    public double SlotFontSize => CurrentSlotAvailable ? UiTypography.SlotSize(Slot, false) : UiTypography.Placeholder;
    public double CompactSlotFontSize => CurrentSlotAvailable ? UiTypography.SlotSize(Slot, true) : UiTypography.Placeholder;
}
public partial class ParcelViewModel
{
    string selectedPositionId = "main";
    public string[] PositionIds => Snapshot?.Positions?.Select(x => x.Id).ToArray() ?? [];
    public string SelectedPositionId { get => selectedPositionId; set { selectedPositionId = value; Changed(); NotifyTargets(); } }
    public PositionPresentation[] PositionRows => Snapshot?.Positions?.Select(p =>
    {
        var op = CurrentAt(p.Id); var input = InputAt(p.Id); var failed = ParcelPresentation.IsUnadmitted(input);
        var currentResult = Fresh && !failed && op?.Wcs?.State == StageState.Accepted &&
            (input is null || input.OperationId == op.Id && (op.Acquisition is null ||
                input.Observation.DeviceId == op.Acquisition.DeviceId && input.Observation.Epoch == op.Acquisition.Epoch &&
                input.Observation.SourceEventId == op.Acquisition.SourceEventId && input.Observation.AdmissionId == op.Acquisition.AdmissionId));
        var style = !Fresh ? ParcelPresentation.Stale() : failed ? ParcelPresentation.InputPresentation(input!) : ParcelPresentation.Operation(op, true);
        var reason = failed ? input!.Reason + (op is null ? "" : "\n当前保留原作业：" + op.ParcelCode + " · " + op.Id + " · " + ParcelPresentation.Operation(op, Fresh).Text + "；原观察 " + op.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") + "；不属于本次观察") : ParcelPresentation.RcsException(op) ?? op?.Reason ?? "";
        return new PositionPresentation(p.Id, p.ReaderDeviceId, !Fresh ? "快照失效" : p.Admit ? "允许新入场" : string.Join(" · ", p.BlockingReasons.Select(ChineseInterfaceText.Notice)), failed ? ParcelPresentation.InputCode(input!) : op?.ParcelCode ?? "—", failed ? input!.Observation.AdmissionId : op?.AdmissionId ?? "—", style.Text,
            failed ? "本次未取得" : !Fresh ? "旧格口已失效 · " + (op?.Slot ?? "未取得") : op?.Wcs is { State: not StageState.Accepted } ? "未取得" : op?.Slot ?? "未取得", reason,
            failed ? "本次观察未创建新作业" : p.MainResultComplete ? "主结果已确认" : "主结果未完成", p.BackgroundComplete ? "后台责任已清偿" : "后台责任待清偿 · " + op?.Upload?.State + " · " + op?.Upload?.Failure,
            style.Foreground, style.Background, style.Border, style.Icon, ParcelPresentation.InputSummary(input), failed && op is not null ? "原件仍保留 · " + ParcelPresentation.Operation(op, Fresh).Text  : "")
        { CurrentSlotAvailable = currentResult && !string.IsNullOrWhiteSpace(op?.Slot),
          HasCurrentInput = op is not null || input is not null, NeedsAttention = !Fresh || failed || style.Foreground is "#B52F38" or "#895509" };
    }).ToArray() ?? [];
    public string CurrentCommandTarget => "当前位命令目标：" + SelectedPositionId + " · " + (Current?.ParcelCode ?? "无作业") + " · " + (Current?.Id ?? "—");
    public string HistoryCommandTarget => "记录命令目标：" + (Selected?.PositionId ?? "—") + " · " + (Selected?.ParcelCode ?? "未选择") + " · " + (Selected?.Id ?? "—");
    public bool CanHistoryContinue => !Busy && Fresh && Selected is not null && Snapshot?.Availability?.ContinuePrepared.Contains(Selected.Id) == true;
    public bool CanHistoryQuery => !Busy && Fresh && !SelectedRcsQueryUnavailable && Selected is not null && Snapshot?.Operations.Any(x => x.Id == Selected.Id && (x.Wcs?.State == StageState.Unknown || x.Plc?.State is StageState.Unknown or StageState.Accepted || x.Capture?.State == StageState.Unknown || x.Upload?.State == StageState.Unknown)) == true;
    public string CapacityState => Snapshot?.Capacity is { } c ? $"未结 {c.Unresolved}/{c.MaximumUnresolved} · 工作集 {c.Resident} · 归档 {c.Archived}（原件和去重保留）" : "容量未取得";
    public string AuxiliaryState => string.Join("\n", Snapshot?.AuxiliaryReasons ?? []);
    public string InstalledProviderParameters { get; private set; } = "";
    public async Task ValidateConfigurationDraftAsync() { var candidate = Wire.Decode<ParcelConfiguration>(ConfigurationDraft); var r = await ServiceAsync(() => application.ValidateConfigurationAsync(candidate)); Message = r.Status + " · " + r.Reason; }
    public string ConfigurationReason { get; set; } = "";
    public string ExtensionAction { get; set; } = "extension-recover";
    public string ExtensionIdentity { get; set; } = "";
    public string IdentityConflictId { get; set; } = "";
    public ParcelHistoryItem[] HistoryItems { get; private set; } = [];
    ParcelHistoryItem? selectedHistory;
    bool rebindingHistory;
    public ParcelHistoryItem? SelectedHistory
    {
        get => selectedHistory;
        set
        {
            // WPF clears SelectedItem synchronously when ItemsSource changes. This
            // is collection reconciliation, not an operator request to clear Owner.
            if (rebindingHistory || ReferenceEquals(value, selectedHistory)) return;
            SetSelection(value?.Operation, true);
        }
    }
    long? nextHistory;
    readonly List<long> historyCursors = [];
    int historyPageIndex;
    long historyGeneration;
    bool historyLoading;
    string? historyFailure;
    bool historyLoaded;
    DateTimeOffset? historyReadAt;
    public string HistorySelectionState => Selected is null ? "未选择原作业" : selectedHistory is null
        ? "当前页外的原作业详情：" + Selected.Id + "；命令仅针对该原作业"
        : "所选行 / 详情 / 命令目标：" + Selected.Id + "；列表为查询时快照，详情可由应用快照更新";
    public bool HistoryLoading => historyLoading;
    public bool HasNextHistory => !historyLoading && !HistoryFiltersChanged && nextHistory is not null;
    public bool HasPreviousHistory => !historyLoading && !HistoryFiltersChanged && historyPageIndex > 0;
    public string HistoryStatus => (HistoryFiltersChanged ? "条件已改变，尚未应用；当前记录仍属于上方已应用范围，翻页和导出已暂停。\n" : "") +
        (historyLoading ? "正在读取作业记录，原结果仍属于上次范围…" : historyFailure is { } failure ? "查询未完成 · " + failure + "；之前读取的记录仅供参考" : !historyLoaded ? "尚未读取作业记录" : HistoryItems.Length == 0 ? "此页没有作业记录" : "已读取 " + HistoryItems.Length + " 条作业 · 查询完成 " + historyReadAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
    public Task PreviousHistoryAsync() => LoadHistoryPageAsync(historyPageIndex - 1, false);
    public Task FirstHistoryAsync() => LoadHistoryPageAsync(0, false);
    public async Task LoadHistoryAsync(bool next = false)
        => await LoadHistoryPageAsync(next ? historyPageIndex + 1 : 0, !next);
    async Task LoadHistoryPageAsync(int targetPage, bool newSelection)
    {
        if (historyLoading) throw new InvalidOperationException("已有历史查询进行中");
        if (!newSelection && (HistoryFiltersChanged || historySelection is null || targetPage < 0 || targetPage > historyPageIndex && nextHistory is null))
            throw new InvalidOperationException("请先查询并固定当前条件；不能翻阅已改变的筛选");
        var generation = Interlocked.Increment(ref historyGeneration);
        var before = newSelection ? long.MaxValue : targetPage > historyPageIndex ? nextHistory!.Value : historyCursors[targetPage];
        historyLoading = true; historyFailure = null; NotifyHistory();
        using var cancellation = new CancellationTokenSource(); historyQueryCancellation = cancellation;
        try
        {
            var signature = HistoryFilterSignature();
            ParcelHistoryPage page;
            if (application is IParcelHistorySessionApplication query)
            {
                var selection = newSelection ? null : historySelection;
                selection ??= await ServiceAsync(() => query.SelectHistoryAsync(HistoryFilter()));
                if (!newSelection && appliedHistorySignature != signature) throw new InvalidOperationException("筛选已改变，先重新查询；不能续读旧筛选");
                page = await ServiceAsync(() => query.ReadHistoryPageAsync(selection, before, 50, cancellation.Token));
                var metrics = await ServiceAsync(() => query.ReadHistoryMetricsAsync(selection, cancellation.Token));
                var positionIds = await ServiceAsync(() => query.HistoryPositionIdsAsync(selection, cancellation.Token));
                cancellation.Token.ThrowIfCancellationRequested();
                if (generation != Volatile.Read(ref historyGeneration) || signature != HistoryFilterSignature()) return;
                HistoryMetricsSummary = $"固定序号 ≤ {selection.UpperSequence} · 实体作业 {metrics.Operations} · WCS请求 {metrics.WcsRequests} · WCS接受 {metrics.WcsAccepted} · 待核对 {metrics.Unknown} · 实体完成 {metrics.PhysicalCompleted} · 发送尝试 {metrics.SendAttempts}（取得格口不等于实体完成）";
                Changed(nameof(HistoryMetricsSummary)); appliedHistorySignature = signature; historySelection = selection;
                foreach (var id in positionIds) historicalPositions.Add(id);
            }
            else throw new NotSupportedException("当前组件缺少可取消的历史查询契约；请使用完整的同版组件。");
            // Keep ownership until drain; fence cancelled or superseded results before replacing any visible page.
            cancellation.Token.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref historyGeneration) || signature != HistoryFilterSignature()) return;
            var current = Selected;
            rebindingHistory = true;
            try
            {
                HistoryItems = page.Items; nextHistory = page.NextBeforeSequence; historyLoaded = true;
                if (newSelection) { historyCursors.Clear(); historyCursors.Add(long.MaxValue); }
                else if (targetPage == historyCursors.Count) historyCursors.Add(before);
                historyPageIndex = targetPage;
                foreach (var item in page.Items) historicalPositions.Add(item.Operation.PositionId ?? "main");
                Changed(nameof(HistoryPositionChoices));
                Changed(nameof(HistoryItems));
                SetSelection(current, false);
            }
            finally { rebindingHistory = false; }
            historyReadAt = DateTimeOffset.Now;
            Changed(nameof(HistorySelectionState));
        }
        catch (OperationCanceledException) { if (generation == Volatile.Read(ref historyGeneration)) historyFailure = "查询已取消或输入已改变，原结果及范围保留"; }
        catch (Exception error)
        { if (generation == Volatile.Read(ref historyGeneration)) historyFailure = error.Message; throw; }
        finally
        { if (ReferenceEquals(historyQueryCancellation, cancellation)) historyQueryCancellation = null; if (generation == Volatile.Read(ref historyGeneration)) { historyLoading = false; NotifyHistory(); } }
    }
    void ResetHistoryForRuntime()
    {
        Interlocked.Increment(ref historyGeneration); historyLoading = false; historyFailure = null; historyLoaded = false; historyReadAt = null;
        CancelHistoryRead();
        HistoryItems = []; nextHistory = null; selectedHistory = null;
        historySelection = null; appliedHistorySignature = null; CancelHistoryExport();
        historyCursors.Clear(); historyPageIndex = 0; historicalPositions.Clear();
        Changed(nameof(HistoryItems)); Changed(nameof(SelectedHistory)); NotifyHistory();
    }
    void NotifyHistory() { foreach (var name in new[] { nameof(HistoryLoading), nameof(HistoryStatus), nameof(HasNextHistory), nameof(HasPreviousHistory), nameof(HistoryFiltersChanged), nameof(AppliedHistoryRange), nameof(HistoryRangeSummary), nameof(CanExportHistory) }) Changed(name); }
    public async Task QuerySelectedAsync()
    {
        RequireFreshSnapshot(); var op = Selected ?? throw new InvalidOperationException("先选择详情中的原作业"); var generation = selectionGeneration;
        var r = await ServiceAsync(() => application.ReconcileAsync(op.Id)); Message = r.Status + " · " + r.Reason + " · 原作业 " + op.Id;
        var detail = await ServiceAsync(() => application.OperationAsync(op.Id));
        if (generation == selectionGeneration && Selected?.Id == op.Id) Selected = detail;
    }
    public async Task ContinueSelectedAsync()
    {
        RequireFreshSnapshot(); var op = Selected ?? throw new InvalidOperationException("先选择详情中的原作业"); var generation = selectionGeneration;
        var command = new BusinessCommand(Wire.Id(), Snapshot!.Revision, "ContinuePrepared", op.Id, InspectionReason);
        var r = await ServiceAsync(() => application.CommandAsync(command)); Message = r.Status + " · " + r.Reason + " · 原作业 " + op.Id;
        var detail = await ServiceAsync(() => application.OperationAsync(op.Id));
        if (generation == selectionGeneration && Selected?.Id == op.Id) Selected = detail;
    }
    public async Task ManageExtensionAsync() { RequireFreshSnapshot(); var action = ExtensionAction; var identity = ExtensionIdentity; var r = await ServiceAsync(() => application.ManageCapabilityAsync(action, identity)); Message = r.Status + " · " + r.Reason; }
    public async Task ArchiveHistoryAsync() { RequireFreshSnapshot(); var command = new BusinessCommand(Wire.Id(), Snapshot!.Revision, "ArchiveHistory", Value: InspectionReason); var r = await ServiceAsync(() => application.CommandAsync(command)); Message = r.Status + " · " + r.Reason; }
    public async Task AcknowledgeIdentityAsync() { RequireFreshSnapshot(); var command = new BusinessCommand(Wire.Id(), Snapshot!.Revision, "AcknowledgeIdentityConflict", IdentityConflictId, InspectionReason); var r = await ServiceAsync(() => application.CommandAsync(command)); Message = r.Status + " · " + r.Reason; }
    public string EffectiveConfiguration => Snapshot is null ? "未加载" : Wire.Encode(Snapshot.Configuration.Effective);
    public string EffectiveConfigurationHeading => Snapshot?.Configuration.Confirmation is { Required: false } ? "最后确认 Effective" : "当前运行输入 · 尚待确认";
    public string PendingConfiguration => Snapshot?.Configuration.Pending is { } pending ? Wire.Encode(pending) : "无候选";
    public string ReceiptOccurrenceId { get; set; } = "";
    string? inspectedRestoreId;
    public string RestoreInspection { get; private set; } = "完整恢复保持隔离。原机接管完成后读取交接证据；换机退役/权限/秘密/设备/资格/编号范围须真实交接。";
    public string RestoreInspectionReason { get; set; } = "";
    public async Task InspectRestoreAsync()
    {
        RequireFreshSnapshot(); var receipt = await ServiceAsync(() => application.CommandAsync(new(Wire.Id(), Snapshot!.Revision, "InspectRestore")));
        RestoreInspection = receipt.Reason; inspectedRestoreId = receipt.Status == "RestoreHandoff" ? Wire.Decode<StartupRestoreHandoff>(receipt.Reason).Id : null; Changed(nameof(RestoreInspection));
    }
    public async Task ConfirmRestoreAsync()
    {
        RequireFreshSnapshot(); if (inspectedRestoreId is null) throw new InvalidOperationException("先读取当前原机交接证据");
        var receipt = await ServiceAsync(() => application.CommandAsync(new(Wire.Id(), Snapshot!.Revision, "ConfirmRestore", inspectedRestoreId, RestoreInspectionReason)));
        Message = receipt.Status + " · " + receipt.Reason;
    }
    string? receiptInventoryOwner;
    string[] receiptInventoryIds = [];
    public string ReceiptInventory { get; private set; } = "先在记录页选择原作业，再读取原接收发生。";
    public async Task LoadReceiptResponsibilitiesAsync()
    { RequireFreshSnapshot();
        var op = Selected ?? throw new InvalidOperationException("先选择记录中的原作业");
        var generation = selectionGeneration;
        var command = new BusinessCommand(Wire.Id(), Snapshot!.Revision, "ReceiptResponsibilities", op.Id);
        var r = await ServiceAsync(() => application.CommandAsync(command));
        if (generation != selectionGeneration || Selected?.Id != op.Id) return;
        ReceiptInventory = r.Reason;
        receiptInventoryOwner = r.Status == "ReceiptResponsibilities" ? op.Id : null;
        if (receiptInventoryOwner is not null)
        {
            using var json = System.Text.Json.JsonDocument.Parse(r.Reason);
            receiptInventoryIds = json.RootElement.EnumerateArray().Select(x => x.GetProperty("Id").GetString()!).ToArray();
        }
        else receiptInventoryIds = [];
        Changed(nameof(ReceiptInventory));
    }
    public async Task ReconcileReceiptAsync()
    { RequireFreshSnapshot();
        if (Selected?.Id != receiptInventoryOwner || !receiptInventoryIds.Contains(ReceiptOccurrenceId))
            throw new InvalidOperationException("重新读取当前所选原作业的回执责任，并选择其中的确切发生ID；不回退到其他位或隐藏目标");
        var command = new BusinessCommand(Wire.Id(), Snapshot!.Revision, "ReconcileReceipt", ReceiptOccurrenceId, InspectionReason);
        var r = await ServiceAsync(() => application.CommandAsync(command));
        Message = r.Status + " · " + r.Reason;
        await LoadReceiptResponsibilitiesAsync();
    }
    readonly Dictionary<string, string> publishedText = [];
    public bool HistoryLoaded => historyLoaded;
    void NotifyTargets()
    {
        foreach (var name in new[] { nameof(ViewportFits), nameof(ViewportReady), nameof(CanUseWorkspace), nameof(CanResumeInViewport), nameof(CanAcknowledgeViewport), nameof(ViewportWarningVisibility), nameof(ViewportStatus), nameof(Current), nameof(CurrentInput), nameof(CurrentInputUnadmitted), nameof(CurrentInputSummary), nameof(CurrentBarcode), nameof(CurrentInputReason), nameof(CurrentResponsibility), nameof(CurrentPresentation), nameof(NextAction), nameof(StatusPresentation), nameof(HistorySelectionState), nameof(HistoryQueryGuidance), nameof(ConfigurationEditorState), nameof(CurrentSlot), nameof(CurrentStage), nameof(CurrentWeight), nameof(OperationOperator), nameof(CurrentImage), nameof(CaptureState), nameof(UploadState), nameof(PositionIds), nameof(PositionRows), nameof(CurrentCommandTarget), nameof(HistoryCommandTarget), nameof(CanQueryCurrent), nameof(CanHistoryContinue), nameof(CanHistoryQuery), nameof(CanContinue), nameof(CanContinueUpload), nameof(CanAbandon), nameof(CapacityState), nameof(AuxiliaryState), nameof(EffectiveConfigurationHeading) }) Changed(name);
        RefreshDetailsProjection();
        foreach (var (name, value) in new[] { (nameof(RequestHandover), RequestHandover), (nameof(EffectiveConfiguration), EffectiveConfiguration), (nameof(PendingConfiguration), PendingConfiguration) })
        {
            if (publishedText.TryGetValue(name, out var prior) && prior == value) continue;
            publishedText[name] = value; Changed(name);
        }
    }
}
