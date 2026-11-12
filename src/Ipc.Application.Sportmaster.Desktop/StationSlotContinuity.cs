using Ipc.Application.ParcelFlow.Desktop;
using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

public sealed partial class SupplyStationPositionViewModel
{
    ParcelReadFeedback? readFeedback;
    DateTimeOffset? PlacementClearedAt => owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId)?.PlacementClearedAt;
    ManualWorkCardExpiry? ExpiredManualResult => null;
    bool CameraObservationUnmatched => false;
    bool CameraAwaitingRead => false;
    // No-code is a frame fact, not a declaration that a parcel exists or has left.
    // Manual input has its own source and remains usable while the camera sees no code.
    bool CameraNoCode => false;
    DeviceState? fastReaderState;
    DeviceState? CurrentReaderState
    {
        get
        {
            var published = owner.Snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == ReaderId);
            return fastReaderState is { } fast && (published is null || fast.UpdatedAt >= published.UpdatedAt) ? fast : published;
        }
    }
    bool unreadFeedback => false;
    bool invalidReadFeedback => false;
    static string SingleDisplayCode(string? code) => code is { Length: > 0 and <= 4096 } && !code.Any(char.IsControl) ? code : "";
    string ObservedBarcode => CurrentRound?.SelectedCode ?? (CurrentRound is { } round ? string.Join(";", round.FormalCodes.Select(SingleDisplayCode)) : "");
    // Manual entry is an independent source (AGENTS.md): an offline camera does not pause it.
    bool ManualInputAvailable => owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId)?.BarcodeInput?.Manual == true;
    public string BarcodePlaceholder => !SupplyEnabled ? SupplyDisabling ? "停用中，不再接收新条码" : "本位已停用，不接收条码" : ReaderUnavailableForDisplay ? ManualInputAvailable ? "相机离线，可扫码枪或键盘输入条码" : "相机离线" : (CurrentRound?.FormalCodes.Length ?? 0) > 1
        ? "本次读到多个条码，请查看图片" : RoundReadActive ? CurrentRound!.AutoExpired ? "识读超时，可原位PDA补录" : "正在识读，可调整标签或PDA补录" : HasCurrentInput ? "本轮条码" : "等待相机扫码或输入条码";
    public ParcelPictureQuery? CurrentPictureQuery => !owner.Fresh || CameraNoCode || CameraObservationUnmatched || unreadFeedback || IsProcessing ? null :
        Input is { Outcome: ParcelInputOutcome.Rejected or ParcelInputOutcome.Blocked, Observation.OperatorInput: null } unadmitted ? new(ObservationId: unadmitted.Observation.Id) : InputUnadmitted ? null :
        Input is { } resting && Operation is { } placed && ParcelInputReasons.IsSamePlacement(resting) && resting.OperationId == placed.Id ? new(OperationId: placed.Id) :
        Input is { } input && Operation is { } current && input.OperationId == current.Id &&
            (current.Acquisition is null || input.Observation.DeviceId == current.Acquisition.DeviceId &&
                input.Observation.Epoch == current.Acquisition.Epoch && input.Observation.SourceEventId == current.Acquisition.SourceEventId &&
                input.Observation.AdmissionId == current.Acquisition.AdmissionId) ? new(ObservationId: input.Observation.Id) :
        Input is null && Operation is { } operation ? new(OperationId: operation.Id) : null;
    public bool CanViewCurrentPicture => CurrentPictureQuery is not null;
    public bool CanViewWorkDetails => CanReconcile && !ShowRecentChute && !(CameraNoCode && CenteredWaiting);
    public string PictureUnavailableReason => !owner.Fresh ? "SnapshotUnavailable" : CameraNoCode ? "NoCurrentCode" :
        CameraObservationUnmatched ? "CameraObservationAheadOfSnapshot" : unreadFeedback ? "ReadAheadOfSnapshot" :
        IsProcessing ? "Processing" : CurrentPictureQuery is null ? "NoPictureIdentity" : "Available";
    public string? BarcodeObservationKey => unreadFeedback ? ReadFeedbackKey : Input is { } input
        ? $"{owner.Snapshot?.RuntimeId}/{input.Observation.DeviceId}/{input.Observation.Epoch}/{input.Observation.Id}"
        : Operation is { } operation ? $"{owner.Snapshot?.RuntimeId}/{operation.Id}" : null;
    bool FastReaderBlocked => fastReaderState is { } state && (
        owner.Snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == ReaderId) is not { } published ||
        published.Epoch != state.Epoch || published.UpdatedAt < state.UpdatedAt);
    bool ReaderUnavailableForDisplay => CurrentRound is null && owner.Fresh && (Input is not null || owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId)?.BarcodeInput?.Manual != true) && Input?.Observation.OperatorInput is null && !(unreadFeedback && readFeedback?.OperatorInput == true) &&
        (FastReaderBlocked || owner.Snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == ReaderId)?.IsValid(DateTimeOffset.UtcNow) != true);
    bool OfflineCameraSurface => ReaderUnavailableForDisplay && !HasUncertainOriginal && Operation?.State != ParcelState.WcsRejected && Input?.Outcome != ParcelInputOutcome.Rejected;
    // Retained property names are display contracts only; no previous destination is cached.
    public bool SlotProvisional => false;
    public bool IsProcessing => CurrentRound?.State == SmRoundState.Submitted && !SupplyDisabled && owner.Fresh && !DisplayPaused && !ReaderUnavailableForDisplay && !HasUncertainOriginal &&
        !CameraNoCode && !invalidReadFeedback && (CurrentRound?.State == SmRoundState.Submitted);
    bool DisplayPaused => owner.Snapshot?.Paused == true;
    bool PausedAttention => DisplayPaused && (HasUncertainOriginal || !CameraNoCode && !CameraObservationUnmatched && !unreadFeedback &&
        (Input is { Outcome: ParcelInputOutcome.Rejected } rejected && !rejected.Reason.StartsWith("ObservationExpired;", StringComparison.Ordinal) || Operation?.State == ParcelState.WcsRejected));
    bool PausedReadBlocked => DisplayPaused && !CameraNoCode && !CameraObservationUnmatched && !unreadFeedback && Input?.Outcome == ParcelInputOutcome.Blocked;
    public bool ShowConfirmedTask => !SupplyDisabled && CurrentTaskAvailable && SlotAvailable && !unreadFeedback && !ReaderUnavailableForDisplay && !DisplayPaused && owner.ViewportReady;
    public bool ShowRecentChute => false;
    public string RecentChuteBarcode => "";
    public string? SuccessIdentity => ShowConfirmedTask ? $"{owner.Snapshot?.RuntimeId}/{ReaderId}/{Operation?.Id}/{Operation?.Wcs?.Request.Id}" : null;
    public string ResultBackground => CenteredAttention ? OperatorTokens.Neutral : DisplayPresentation.Tone switch
    {
        ParcelPresentationTone.Accepted => OperatorTokens.Success,
        ParcelPresentationTone.Rejected => OperatorTokens.Danger,
        ParcelPresentationTone.Unknown => OperatorTokens.Warning,
        ParcelPresentationTone.Stale => OperatorTokens.Inactive,
        _ => OperatorTokens.Neutral
    };
    public string ResultForeground => !CenteredAttention && DisplayPresentation.Tone is (ParcelPresentationTone.Accepted or ParcelPresentationTone.Rejected or ParcelPresentationTone.Unknown or ParcelPresentationTone.Stale) ? OperatorTokens.Surface : OperatorTokens.Ink;
    public bool DisplaySlotVisible => ShowConfirmedTask || IsProcessing || ShowRecentChute;
    public string DisplaySlot => IsProcessing ? "处理中…" : ShowConfirmedTask ? Slot : "";
    public string DisplaySlotForeground => ResultForeground;
    public ParcelStatusPresentation DisplayPresentation => !owner.Fresh ? ParcelPresentation.Stale() : ShowRecentChute ? ParcelPresentation.Neutral("上一件格口") : CenteredAttention
        ? CenteredAttentionTone == ParcelPresentationTone.Unknown ? ParcelPresentation.Unknown(CenteredStateTitle) : ParcelPresentation.Rejected(CenteredStateTitle) :
        ShowCenteredState ? ParcelPresentation.Neutral(CenteredStateTitle) : DisplayPaused && !PausedAttention ? ParcelPresentation.Neutral(PausedReadBlocked ? "已读到条码，供包已暂停" : "供包已暂停") : HasUncertainOriginal ? Presentation : OfflineCameraSurface ? ParcelPresentation.Neutral(ManualInputAvailable ? "等待输入条码" : "等待供包条件就绪") :
        CameraAwaitingRead && !HasUncertainOriginal ? ParcelPresentation.Neutral("等待重新扫码") : invalidReadFeedback ? ParcelPresentation.Unknown("本次读码已失效") : IsProcessing ? ParcelPresentation.Pending("处理中") : CurrentTaskAvailable && !SlotAvailable ? ParcelPresentation.Unknown("未取得格口") : Presentation;
    public string DisplaySlotLabel => ShowRecentChute ? "上一件格口，仅供查看" : IsProcessing ? "本次处理中，尚未取得有效格口" : "本次有效格口，可选择复制";
    public string ContinuityNotice => "";
    public string DisplayStatus => SupplyDisabled ? "已停用" : ShowRecentChute ? "上一件格口 · 仅供查看" : ShowConfirmedTask && IsTight ? "按本次格口投递" : !owner.Fresh ? "状态已失效" : DisplayPaused && !PausedAttention ? PausedReadBlocked ? "已读到条码，供包已暂停" : "供包已暂停" : OfflineCameraSurface ? ManualInputAvailable ? "等待输入条码" : "暂不能接收条码" : HasUncertainOriginal ? Operation?.State == ParcelState.WcsUnknown ? "格口结果未确认" : Status :
        CameraAwaitingRead ? "等待重新扫码" : CameraNoCode ? CenteredStateTitle : invalidReadFeedback ? "本次读码已失效" : IsProcessing ? "本次处理中" : Operation?.State == ParcelState.WcsRejected ? "未取得格口" : Status;
    public string DisplayAttentionReason => CenteredAttention && !CenteredRcsRejection ? CenteredStateReason :
        SupplyDisabled || DisplayPaused && !PausedAttention ? PausedReadBlocked ? "本次扫码未发送请求，可查看本次图片。" : "" : OfflineCameraSurface ? ManualInputAvailable ? "可用扫码枪或键盘输入完整条码。" : AttentionReason :
        CameraAwaitingRead ? "相机已重连，正在重新扫码。" :
        unreadFeedback ? "已读到条码，正在保存，尚未确认接收。" : AttentionReason;
    public string DisplayNextStep => ShowRecentChute ? "" : CenteredAttention ? CenteredStateInstruction : SupplyDisabled ? "" : DisplayPaused ? PausedAttention
        ? "供包已暂停；" + (HasUncertainOriginal ? NextStep : Input?.Outcome == ParcelInputOutcome.Rejected ? "查看图片核对条码；将包裹移出托盘后，再放下一件。" : NextStep)
        : PauseInstruction : ShowConfirmedTask ? "" : OfflineCameraSurface ? ManualInputAvailable ? "输入完整条码后按回车或点击提交。" : NextStep :
        invalidReadFeedback ? "本件暂勿投递；请重新扫描并等待有效格口，未确认的原请求仍需核对。" : !HasUncertainOriginal && IsProcessing ? "等待本件格口，勿按上一件格口投递。" :
        !HasUncertainOriginal && Input?.Reason.StartsWith("ObservationExpired;", StringComparison.Ordinal) == true ? "本件已跳过，请取下包裹后重新上件。" :
        !HasUncertainOriginal && Input is { Outcome: ParcelInputOutcome.Rejected, BarcodeSelection.SelectedCode: null } ? "本件未取得格口，请查看图片核对条码；将包裹移出托盘后，再放下一件。" : NextStep;
    public double DisplaySlotFontSize => IsProcessing ? UiTypography.Placeholder : SlotFontSize;
    public string CameraReadFeedback => readFeedback is { } read && CurrentRound?.Observation?.Id == read.ObservationId && Stopwatch.GetElapsedTime(read.DetectedTicks) < TimeSpan.FromSeconds(1)
        ? "已读到条码，等待处理结果。" : "";
    public string? ReadFeedbackIdentity => CurrentRound?.Observation?.Id == readFeedback?.ObservationId ? readFeedback?.ObservationId : null;
    public string? ReadFeedbackKey => ReadFeedbackIdentity is not null && readFeedback is { } read ? $"{read.RuntimeId}/{read.DeviceId}/{read.Epoch}/{read.ObservationId}" : null;
    public string? RejectionIdentity => !OfflineCameraSurface && (!CameraNoCode || HasUncertainOriginal) && !unreadFeedback && (InputFailed || Operation?.State is ParcelState.WcsRejected or ParcelState.WcsUnknown or ParcelState.UnpackRequired || Operation?.Wcs?.State == StageState.Conflict)
        ? $"{owner.Snapshot?.RuntimeId}/{ReaderId}/{Input?.Observation.Epoch}/{Input?.Observation.Id ?? Operation?.Id}" : null;
    internal void ApplyReadFeedback(ParcelReadFeedback value)
    {
        if (!SupplyEnabled) return;
        if (value.RuntimeId != owner.Snapshot?.RuntimeId || value.DeviceId != ReaderId || value.PositionId != PositionId ||
            value.DetectedTicks <= 0 || value.DetectedTicks > Stopwatch.GetTimestamp() ||
            readFeedback is { } old && value.DetectedTicks < old.DetectedTicks) return;
        // A short-lived cue may expire while its observed identity still leads the
        // business consumer. Keep that watermark for late snapshots/new windows.
        readFeedback = value;
        NotifySlotDisplay();
    }
    internal void ApplyReaderFeedback(Guid runtime, DeviceState value)
    {
        if (runtime != owner.Snapshot?.RuntimeId || value.DeviceId != ReaderId ||
            fastReaderState is { } old && old.UpdatedAt > value.UpdatedAt) return;
        fastReaderState = value;
        Update(owner.Snapshot, DateTimeOffset.UtcNow, owner.Fresh, owner.ViewportReady);
    }
    internal void ApplyCameraReadFeedback(Guid runtime, ParcelCameraReadFeedback value) { }
    void UpdateSlotMemory(ParcelSnapshot? snapshot, DateTimeOffset now, bool fresh, bool viewportReady)
    {
        if (readFeedback?.RuntimeId != snapshot?.RuntimeId || readFeedback?.DeviceId != ReaderId) readFeedback = null;
        if (fastReaderState?.DeviceId != ReaderId) fastReaderState = null;
        if (fastReaderState is { } state && snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == ReaderId) is { } acknowledged &&
            acknowledged.Epoch == state.Epoch && acknowledged.UpdatedAt >= state.UpdatedAt) fastReaderState = null;
        if (snapshot?.ReadFeedbacks.SingleOrDefault(r => r.PositionId == PositionId) is { } read) ApplyReadFeedback(read);
    }
    void NotifySlotDisplay()
    {
        foreach (var (name, value) in new (string, object?)[] {
            (nameof(Arrival), Arrival), (nameof(SupplyEnabled), SupplyEnabled), (nameof(SupplyDisabling), SupplyDisabling), (nameof(SupplyDisabled), SupplyDisabled),
            (nameof(SupplyConfigured), SupplyConfigured), (nameof(CanChangeSupply), CanChangeSupply), (nameof(CanConfirmPlacementRemoval), CanConfirmPlacementRemoval), (nameof(ShowConfirmedPlacementRemoval), ShowConfirmedPlacementRemoval), (nameof(DisableLabel), DisableLabel), (nameof(EnableLabel), EnableLabel), (nameof(SupplyNotice), SupplyNotice), (nameof(SupplyStateText), SupplyStateText),
            (nameof(EnableBackground), EnableBackground), (nameof(EnableForeground), EnableForeground), (nameof(DisableBackground), DisableBackground), (nameof(DisableForeground), DisableForeground),
            (nameof(Barcode), Barcode), (nameof(HasCurrentInput), HasCurrentInput), (nameof(BarcodePlaceholder), BarcodePlaceholder),
            (nameof(BarcodeObservationKey), BarcodeObservationKey),
            (nameof(CanViewCurrentPicture), CanViewCurrentPicture), (nameof(CanViewWorkDetails), CanViewWorkDetails), (nameof(PictureUnavailableReason), PictureUnavailableReason), (nameof(CurrentTaskAvailable), CurrentTaskAvailable), (nameof(SlotAvailable), SlotAvailable), (nameof(Slot), Slot),
            (nameof(WaveNo), WaveNo), (nameof(TaskId), TaskId), (nameof(SkuId), SkuId), (nameof(ResponseQuantity), ResponseQuantity),
            (nameof(IsProcessing), IsProcessing), (nameof(SlotProvisional), SlotProvisional), (nameof(DisplaySlotVisible), DisplaySlotVisible), (nameof(DisplaySlot), DisplaySlot),
            (nameof(ShowRecentChute), ShowRecentChute), (nameof(RecentChuteBarcode), RecentChuteBarcode),
            (nameof(DisplayPresentation), DisplayPresentation), (nameof(ResultBackground), ResultBackground), (nameof(ResultForeground), ResultForeground), (nameof(SuccessIdentity), SuccessIdentity), (nameof(DisplaySlotForeground), DisplaySlotForeground), (nameof(DisplaySlotLabel), DisplaySlotLabel), (nameof(ContinuityNotice), ContinuityNotice),
            (nameof(ShowCenteredState), ShowCenteredState), (nameof(CenteredAttention), CenteredAttention), (nameof(CenteredRcsRejection), CenteredRcsRejection), (nameof(CenteredStateTitle), CenteredStateTitle), (nameof(CenteredStateForeground), CenteredStateForeground),
            (nameof(CenteredStateReason), CenteredStateReason), (nameof(CenteredStateInstruction), CenteredStateInstruction), (nameof(CenteredStateFullInstruction), CenteredStateFullInstruction),
            (nameof(DisplayStatus), DisplayStatus), (nameof(DisplayAttentionReason), DisplayAttentionReason), (nameof(DisplayNextStep), DisplayNextStep), (nameof(ShowConfirmedTask), ShowConfirmedTask),
            (nameof(DisplaySlotFontSize), DisplaySlotFontSize), (nameof(CameraReadFeedback), CameraReadFeedback), (nameof(ReadFeedbackIdentity), ReadFeedbackIdentity),
            (nameof(ReadFeedbackKey), ReadFeedbackKey), (nameof(RejectionIdentity), RejectionIdentity) })
        {
            if (published.TryGetValue(name, out var prior) && Equals(prior, value)) continue;
            published[name] = value; PropertyChanged?.Invoke(this, new(name));
        }
    }


}
