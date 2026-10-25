using System.IO;
using System.Windows;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

// Window owns selection/draft only; every command and fact belongs to the shared application.
public partial class ParcelViewModel(IParcelApplication application, string title, string flow, string presentationId = "parcel") : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public IParcelApplication Application => application;
    public string Title => title;
    public Visibility SimulationVisibility => Snapshot?.Configuration.Effective.Mode == RunMode.Simulation ? Visibility.Visible : Visibility.Collapsed;
    public string EnvironmentStatus => Snapshot?.Configuration.Effective.Mode == RunMode.Production ? "生产模式 · 软件暂停不是安全急停 · 资格不代替现场安全联锁" : "模拟模式 · 软件暂停不是安全急停 · 不操作真实设备";
    public string Flow => flow;
    public string PresentationId => presentationId;
    ParcelSnapshot? snapshot;
    ParcelOperation? selected;
    string message = "等待相机读码；模拟工程不操作真实设备。";
    public ParcelSnapshot? Snapshot { get => snapshot; private set { snapshot = value; Changed(nameof(AccountDisplay)); Changed(nameof(PauseVisibility)); Changed(nameof(ResumeVisibility)); LoadStationDisplay(); Changed(); Changed(nameof(Operations)); Changed(nameof(Current)); Changed(nameof(BlockingReasons)); Changed(nameof(Status)); Changed(nameof(CurrentWeight)); Changed(nameof(CurrentStage)); Changed(nameof(CurrentSlot)); Changed(nameof(Health)); Changed(nameof(ConfigurationState)); Changed(nameof(LoginVisibility)); Changed(nameof(Session)); Changed(nameof(SimulationVisibility)); Changed(nameof(EnvironmentStatus)); Changed(nameof(OperationOperator)); Changed(nameof(CurrentImage)); Changed(nameof(UploadState)); Changed(nameof(CaptureState)); Changed(nameof(CanAdmit)); Changed(nameof(CanResume)); Changed(nameof(CanContinue)); Changed(nameof(CanContinueUpload)); Changed(nameof(CanAbandon)); Changed(nameof(CanConfigure)); NotifyConfigurationEditorState(); } }
    public ParcelOperation[] Operations => Snapshot?.Operations ?? [];
    public ParcelOperation? Current => CurrentAt(SelectedPositionId);
    public ParcelOperation? CurrentAt(string position) => Snapshot?.Positions?.SingleOrDefault(p => p.Id == position)?.CurrentOperation is { } id
        ? Snapshot.Operations.SingleOrDefault(x => x.Id == id && (x.PositionId ?? "main") == position) : null;
    public string BlockingReasons => !Fresh ? ReadStatus : string.Join("\n", (Snapshot?.BlockingReasons ?? []).Select(ChineseInterfaceText.Notice));
    public string Status => !Fresh ? "快照已失效 · 旧状态仅供参考" : Snapshot?.State switch
    {
        "Ready" => "作业就绪", "PartiallyReady" => "部分投递位可作业", "Blocked" => "暂不可作业", "Paused" => "已暂停新入场", "NotReady" => "暂不可作业",
        "Starting" => "正在启动", "Stopping" => "正在停止", "Stopped" => "已停止",
        null => "正在初始化", var state => state
    };
    public string Health => !Fresh ? "设备旧状态已失效 · " + ReadStatus : string.Join("\n", Snapshot?.Health.Select(x => ParcelPresentation.Health(x, DateTimeOffset.UtcNow)) ?? []);
    public Visibility LoginVisibility => Snapshot?.Configuration.Effective.Login is null ? Visibility.Collapsed : Visibility.Visible;
    public string OperatorSubject { get; set; } = "";
    public string Session => Snapshot?.Configuration.Effective.Login is null ? "登录未启用" : Snapshot.CurrentOperator is { } user ? "当前操作员：" + user.DisplayName + (user.Simulation ? " · 模拟身份" : " · " + user.Authority) : "等待操作员认证";
    public string OperationOperator => Current?.Operator is { } user ? "原作业操作员：" + user.DisplayName + " · " + user.SessionId : "原作业没有登录身份";
    public bool CanAdmit => !Busy && Fresh && Snapshot?.Availability?.Admit == true;
    public bool CanResume => !Busy && Fresh && Snapshot?.Availability?.Resume == true;
    public bool CanConfigure => !Busy && Fresh && Snapshot?.Availability?.Configure == true;
    public bool CanContinue => !Busy && Fresh && Snapshot?.Availability?.ContinuePrepared.Contains(Current?.Id ?? "") == true;
    public bool CanContinueUpload => !Busy && Fresh && Snapshot?.Availability?.ContinueUpload.Contains(Selected?.Id ?? "") == true;
    public bool CanAbandon => !Busy && Fresh && Snapshot?.Availability?.AbandonUnsubmitted.Contains(Selected?.Id ?? "") == true;
    public string CaptureState => "采集：" + (Current?.Capture?.State.ToString() ?? "未准备") + " · " + Current?.Capture?.Failure;
    public string UploadState => Current?.Upload is { } upload ? "附加/前置上传：" + upload.State + " · 尝试 " + upload.Attempts + " · " + upload.Failure : "上传未准备";
    public string CurrentImage => Current?.Image is { } image ? (image.UploadedUri is null ? "图像已采集，上传未确认" : "图像已上传 · " + image.UploadedUri + (Current.ImageReleased ? " · 临时原件已释放" : "")) : Snapshot?.Configuration.Effective.Images is null ? "传图未启用" : "图像尚未取得";
    public async Task LoginAsync(string proof) { RequireFreshSnapshot(); var subject = OperatorSubject; var r = await ServiceAsync(() => application.LoginAsync(subject, proof)); Message = r.Status + " · " + r.Reason; }
    public async Task LogoutAsync() { RequireFreshSnapshot(); var r = await ServiceAsync(application.LogoutAsync); Message = r.Status + " · " + r.Reason; }
    public string CurrentSlot => CurrentInputUnadmitted ? "未取得" : !Fresh ? "旧格口已失效 · " + (Current?.Slot ?? "未取得") : Current?.Wcs is { State: not StageState.Accepted } ? "未取得" : Current?.Slot ?? "未取得";
    public string CurrentWeight => Current?.Weight is { } w ? w.Value.ToString("F" + w.Precision, CultureInfo.InvariantCulture) + " " + w.Unit + " · " + (w.Source == WeightSource.Measured ? "实测" : "配置补全") : "未参与或未取得；见当前重量策略";
    public string CurrentStage => CurrentPresentation.Text;
    long selectionGeneration;
    protected long SelectionGeneration => selectionGeneration;
    public ParcelOperation? Selected { get => selected; set => SetSelection(value, true); }
    void SetSelection(ParcelOperation? value, bool userIntent)
    {
        selected = value;
        if (userIntent) Interlocked.Increment(ref selectionGeneration);
        selectedHistory = value is null ? null : HistoryItems.FirstOrDefault(x => x.Operation.Id == value.Id);
        RefreshDetailsProjection();
        Changed(nameof(Selected)); Changed(nameof(SelectedHistory)); NotifyTargets();
    }
    public string Details
    {
        get
        {
            DetailMeasurement?.Invoke();
            return detailsText;
        }
    }
    internal Action? DetailMeasurement { get; set; }
    public static string DestinationSummary(ParcelOperation operation)
    {
        var reply = operation.Wcs?.Replies.LastOrDefault(r => r.Destination is not null);
        if (reply?.Destination is not { } result) return "";
        string Known(string? text) => text ?? "未取得";
        string Measure(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "未取得";
        var outcome = result.Outcome switch
        {
            WcsDestinationOutcome.Assigned => "目标格口已取得；物理完成未确认",
            WcsDestinationOutcome.UnpackRequired => "需拆包；本位等待检查处置",
            _ => "业务拒绝"
        };
        var lines = new List<string>
        {
            "原调度回执 · " + outcome + "（作业状态以原快照为准）",
            "任务 " + Known(result.TaskId) + " · 商品编码 " + Known(result.SkuId) + " · 格口 " + Known(reply.Slot),
            "波次 " + (result.WaveNo ?? "原回执未包含") + " · 返回数量 " + (result.Quantity ?? "原回执未包含") + "（原字符串；不等于请求数量）",
            "长度 " + Measure(result.Length) + " · 宽度 " + Measure(result.Width) + " · 高度 " + Measure(result.Height) + " · 重量 " + Measure(result.Weight) + "（接口未提供单位）"
        };
        if (result.Code is not null || result.Message is not null) lines.Add("业务码 " + Known(result.Code) + " · 原因 " + Known(result.Message));
        if (result.Unpack is { } unpack)
        {
            lines.Add("拆包条码 " + unpack.Barcode + " · 商品编码 " + unpack.SkuId + " · 扫描数量 " + unpack.ScanQty.ToString(CultureInfo.InvariantCulture));
            if (unpack.Chutes.Length == 0) lines.Add("上游未返回拆包格口明细");
            foreach (var chute in unpack.Chutes)
                lines.Add("格口 " + chute.ChuteNo + " · 订单 " + chute.OrderNo + " · 剩余 " + chute.Remaining.ToString(CultureInfo.InvariantCulture) + " · 阶段 " + chute.Phase);
            lines.Add("本接口未提供拆包提交；暂停并检查移除原实体后，使用已有受守卫的本地处置。");
        }
        return string.Join("\n", lines);
    }
    long messageRevision;
    public string Message { get => message; set { message = value; messageScope = runningFeedbackScope ?? feedbackScope; messageRevision++; Changed(); Changed(nameof(Feedback)); } }
    public string Feedback => Busy ? OperationStatus : ChineseInterfaceText.Message(Message);
    public string OperatorFeedback => (Busy ? OperationStatus : Message).Split(" · ", 2)[0] switch
    {
        "Persisted" => "操作已提交；请核对当前应用状态。",
        "PendingRestart" => "配置已保存；等待受控重启生效。",
        "Conflict" => "状态已变化；保留原对象，重新核对后再操作。",
        "Rejected" => "操作未被接受；通过来源诊断查看完整回执。",
        "NotReady" => "当前条件不允许操作；请核对阻断原因。",
        _ => Feedback
    };

    public string ConfigurationState => Snapshot is null ? "未加载" : ChineseInterfaceText.Status(Snapshot.Configuration.Status) + " · 修订 " + Snapshot.Configuration.Revision + (Snapshot.Configuration.Failure is { } f ? "\n" + f : "");
    string configurationDraft = "";
    public string ConfigurationDraft { get => configurationDraft; set { configurationDraft = value; Changed(); NotifyConfigurationEditorState(); } }
    public string ParcelCode { get; set; } = "000123456789";
    public string AdmissionId { get; set; } = "parcel-" + Wire.Id();
    public string MeasurementValue { get; set; } = "1.250";
    public string BackupPath { get; set; } = "";
    public bool Busy { get; private set; }
    long refreshGeneration;
    long snapshotReadId;
    protected virtual Task<ParcelSnapshot> ReadSnapshotAsync() => Own(SharedSnapshotRead.Read(application, ManagementStatus));
    protected virtual void ReportCommandFailure(Exception error,string prefix)
    { if (!ReferenceEquals(error, lastReadError)) Message=prefix+error.Message; }
    async Task<(long Id, ParcelSnapshot Snapshot)> ReadSnapshotWithIdAsync()
    {
        var id = Interlocked.Increment(ref snapshotReadId);
        try
        {
            var result = await ReadSnapshotAsync();
            return (id, result);
        }
        catch (Exception error) { SnapshotReadFailed(id, error); throw; }
    }
    public async Task RefreshAsync()
    {
        await RefreshManagementModeAsync();
        var generation = Interlocked.Increment(ref refreshGeneration);
        var (readId, next) = await ReadSnapshotWithIdAsync();
        if (generation != Volatile.Read(ref refreshGeneration) || !CanApplySnapshot(readId)) return;
        if (retiredRuntimeIds.Contains(next.RuntimeId) || Snapshot is { } previous && next.RuntimeId == previous.RuntimeId && next.Revision < previous.Revision)
        { MarkReadFailure("运行实例已退役或快照修订倒退；旧事实失效，请重新读取并核对当前运行实例"); return; }
        if (Snapshot is { } original && original.RuntimeId != next.RuntimeId)
        {
            globalOperationGeneration++; SetGlobalFeedback("");
            retiredRuntimeIds.Add(original.RuntimeId);
            ResetHistoryForRuntime();
            Selected = null;
        }
        Snapshot = next;
        SnapshotReadApplied(readId);
        if (!PositionIds.Contains(SelectedPositionId)) SelectedPositionId = PositionIds.FirstOrDefault() ?? "main";
        NotifyTargets();
        // Resolve the selection after the await. A slow read must never restore an
        // earlier user intent; refreshing facts for the same owner is not a new intent.
        if (Selected is { } current)
        {
            var replacement = Operations.FirstOrDefault(x => x.Id == current.Id);
            if (replacement is null && detailsAuthorityPending)
            {
                var intent = selectionGeneration;
                replacement = await ServiceAsync(() => application.OperationAsync(current.Id));
                if (generation != Volatile.Read(ref refreshGeneration) || intent != selectionGeneration || Selected?.Id != current.Id) return;
                SetSelection(replacement, false);
            }
            else SetSelection(replacement ?? current, false);
        }
        DetailsSnapshotApplied();
    }
    long? draftConfigurationRevision;
    Guid? draftRuntimeId;
    public string DraftRevisionState => draftConfigurationRevision is { } revision ? "本窗草稿基于配置修订 " + revision + "；自动刷新不改变草稿CAS" : "尚未读取草稿修订";
    void SetConfigurationDraft(ParcelConfiguration value, long revision, Guid runtime)
    { ConfigurationDraft = Wire.Encode(value); draftConfigurationRevision = revision; draftRuntimeId = runtime; Changed(nameof(ConfigurationDraft)); Changed(nameof(DraftRevisionState)); }
    public void LoadConfigurationDraft()
    { var loaded = Snapshot; if (loaded is not null) SetConfigurationDraft(loaded.Configuration.Pending ?? loaded.Configuration.Effective, loaded.Configuration.Revision, loaded.RuntimeId); }
    long RequireDraftRevision()
    {
        if (draftConfigurationRevision is not { } revision || Snapshot?.RuntimeId != draftRuntimeId) throw new InvalidOperationException("先读取本运行实例的完整修订，再编辑保存；自动刷新不会更新旧草稿的CAS");
        return revision;
    }
    void AdvanceOwnDraftRevision(long expected, BusinessReceipt receipt)
    { if (receipt.Status == "PendingRestart") { draftConfigurationRevision = expected + 1; Changed(nameof(DraftRevisionState)); } }
    public bool CanStartOperation => !Busy;
    public bool CanStartFreshOperation => !Busy && Fresh;
    public string OperationStatus { get; private set; } = "";
    void SetBusy(bool value)
    {
        Busy = value;
        foreach (var name in new[] { nameof(Busy), nameof(CanStartOperation), nameof(CanStartFreshOperation), nameof(OperationStatus), nameof(Feedback) }) Changed(name);
        RefreshFreshness();
    }
    public Task RunAsync(Func<Task> action, string operation = "操作") => Own(RunCoreAsync(action, operation));
    async Task RunCoreAsync(Func<Task> action, string operation)
    {
        if (Busy) return; // The active feedback already explains this lane; do not replace its result.
        runningFeedbackScope = feedbackScope;
        OperationStatus = "正在" + operation + "… 冲突操作暂不可用，软件暂停仍可独立执行。";
        SetBusy(true);
        var priorMessageRevision = messageRevision;
        try
        {
            await action();
            OperationStatus = operation + "已返回 · " + (messageRevision == priorMessageRevision ? "操作已返回，正在读取最新状态" : Message);
            Changed(nameof(OperationStatus)); Changed(nameof(Feedback));
            var ownsReadFailure = readFailureMessage is not null && Message == readFailureMessage;
            await RefreshAsync();
            // A read-only action may have no receipt. Publish its completion only if
            // neither it nor independent Pause wrote a message; a recovered read failure
            // may also release its own empty banner. Current read failures retain priority.
            // A new receipt with identical text still owns its message revision.
            if (messageRevision == priorMessageRevision || ownsReadFailure && Fresh && Message.Length == 0)
            {
                Message = operation + "已完成；已读取最新状态。";
                OperationStatus = Message;
            }
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(ex, globalOperationError)) ReportCommandFailure(ex, ex.GetType().Name + ": ");
            OperationStatus = operation + "未完成 · " + ex.Message + "；保留原对象与草稿，核对原因后重试。";
        }
        finally { SetBusy(false); runningFeedbackScope = null; }
    }
    public Task PauseAsync() => Own(PauseCoreAsync());
    async Task PauseCoreAsync()
    {
        var generation = BeginGlobalOperation("暂停供包");
        // Independent control lane: ordinary write mutual exclusion cannot hide Pause.
        try
        {
            for (var retry = 0; retry < 3; retry++)
            {
                var s = retry == 0 && Snapshot is { } displayed ? displayed : await ServiceAsync(application.SnapshotAsync).WaitAsync(TimeSpan.FromSeconds(2));
                var r = await ServiceAsync(() => application.CommandAsync(new(Wire.Id(), s.Revision, "Pause")));
                if (r.Status == "Conflict") continue;
                CompleteGlobalOperation(generation, "暂停供包", r.Status + " · " + r.Reason, r.Status == "Persisted");
                try { await RefreshAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (Exception ex) { MarkReadFailure(ex.Message); CompleteGlobalOperation(generation, "暂停供包", "软件暂停命令回执：" + r.Status + " · " + r.Reason + "；快照读取未恢复，请核对状态。"); }
                return;
            }
            CompleteGlobalOperation(generation, "暂停供包", "暂停遇到连续修订冲突，请重新读取并核对；状态仍由应用服务确认。");
        }
        catch (Exception ex) { CompleteGlobalOperation(generation, "暂停供包", "暂停结果待核对：" + ex.Message + "；请读取最新状态，勿将通信失败当作未执行。"); }
    }
    public async Task CommandAsync(string kind, string? owner = null)
    {
        var global = kind == "Resume";
        var generation = global ? BeginGlobalOperation("恢复供包") : 0;
        try
        {
            var s = RequireFreshSnapshot();
            var r = await ServiceAsync(() => application.CommandAsync(new(Wire.Id(), s.Revision, kind, owner)));
            if (global) CompleteGlobalOperation(generation, "恢复供包", r.Status + " · " + r.Reason, r.Status == "Persisted");
            else Message = r.Status + " · " + r.Reason;
        }
        catch (Exception ex)
        {
            if (global) { globalOperationError = ex; CompleteGlobalOperation(generation, "恢复供包", "操作未确认：" + ex.Message + "；请读取最新状态并核对。"); }
            throw;
        }
    }
    public void Emit()
    {
        if (!Fresh) { Message = ReadStatus; return; }
        Message = application.EmitReferenceAt(SelectedPositionId, AdmissionId, ParcelCode, Wire.Id()) ? "模拟观察已排队；状态由应用服务更新。" : "未接纳观察；检查暂停、过载或设备状态。";
        // The same admission is deliberately retained until the operator explicitly selects
        // a new parcel. Repeated clicking cannot invent a new physical entity.
    }
    public void NewParcel() { AdmissionId = "parcel-" + Wire.Id(); Changed(nameof(AdmissionId)); }
    public void EmitWeight()
    {
        RequireFreshSnapshot();
        var op = Current ?? throw new InvalidOperationException("尚无包裹");
        if (!decimal.TryParse(MeasurementValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) throw new InvalidDataException("重量数值无效");
        var scale = ParcelBindings.Scale(op.FrozenConfiguration, op.PositionId) ?? throw new InvalidOperationException("当前业务未启用实测重量");
        Message = application.EmitReferenceWeight(new(Wire.Id(), op.AdmissionId, scale.Target, value, op.WeightPolicy.Unit, op.WeightPolicy.Precision, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow))
            ? "模拟测量已排队，等待应用关联和校验。" : "测量未接纳；原作业仍保留。";
    }
    public async Task SaveConfigurationAsync()
    { RequireFreshSnapshot(); var expected = RequireDraftRevision(); var candidate = Wire.Decode<ParcelConfiguration>(ConfigurationDraft); _ = await SubmitConfigurationAsync(expected, candidate); }
    public async Task ReconcileAsync()
    { RequireFreshSnapshot(); var op = Current ?? throw new InvalidOperationException("当前投递位没有作业"); var r = await ServiceAsync(() => application.ReconcileAsync(op.Id)); Message = r.Status + " · " + r.Reason; }
    public async Task ContinueAsync() { RequireFreshSnapshot(); var op = Current ?? throw new InvalidOperationException("当前投递位没有作业"); var command = new BusinessCommand(Wire.Id(), Snapshot!.Revision, "ContinuePrepared", op.Id, InspectionReason); var r = await ServiceAsync(() => application.CommandAsync(command)); Message = r.Status + " · " + r.Reason; }
    public async Task ReconnectAsync() { RequireFreshSnapshot(); var r = await ServiceAsync(application.ReconnectAsync); Message = r.Status + " · " + r.Reason; }
    public async Task BackupAsync() { RequireFreshSnapshot(); var path = BackupPath; var r = await ServiceAsync(() => application.BackupAsync(path)); Message = r.Status + " · " + r.Reason; }
    protected void Changed([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new(name));
        if (name == nameof(Feedback)) PropertyChanged?.Invoke(this, new(nameof(OperatorFeedback)));
        if (name == nameof(Feedback)) PropertyChanged?.Invoke(this, new(nameof(TaskFeedback)));
        if (name is nameof(Snapshot) or nameof(Fresh)) PropertyChanged?.Invoke(this, new(nameof(HasOperationalAttention)));
        if (name == nameof(Current)) PropertyChanged?.Invoke(this, new(nameof(HasCurrentOperation)));
        if (name == nameof(ParameterConfigurationSummary)) PropertyChanged?.Invoke(this, new(nameof(ParameterEditStatus)));
        if (name is nameof(ParameterConfigurationSummary) or nameof(Busy) or nameof(CanConfigure))
            {
            PropertyChanged?.Invoke(this, new(nameof(ConfigurationSaveGuidance)));
            PropertyChanged?.Invoke(this, new(nameof(CanSubmitReviewedConfiguration)));
            PropertyChanged?.Invoke(this, new(nameof(CanReviewConfiguration)));
            PropertyChanged?.Invoke(this, new(nameof(ConfigurationSaveActionText)));
            PropertyChanged?.Invoke(this, new(nameof(ConfigurationReviewIsCurrent)));
            PropertyChanged?.Invoke(this, new(nameof(ConfigurationDraftStatusText)));
            PropertyChanged?.Invoke(this, new(nameof(ConfigurationSavedStatusText)));
        }
    }
}
