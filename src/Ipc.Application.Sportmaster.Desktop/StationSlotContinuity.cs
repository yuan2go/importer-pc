using Ipc.Application.ParcelFlow.Desktop;
using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

public sealed partial class SupplyStationPositionViewModel
{
    ParcelReadFeedback? readFeedback;
    ParcelCameraReadFeedback? cameraRead;
    Guid cameraReadRuntime;
    bool RetainedRejectedInput => Input is { Outcome: ParcelInputOutcome.Rejected } input &&
        readFeedback?.RetainedObservationId == input.Observation.Id;
    bool ManualPlacementResult => Operation?.OperatorInput is not null && Input?.OperationId == Operation.Id &&
        (Input.Observation.OperatorInput is not null || ParcelInputReasons.IsSamePlacement(Input)) &&
        (readFeedback is not { OperatorInput: false } read || read.ObservedAt <= Operation.ObservedAt || read.PlacementOperationId == Operation.Id) &&
        !(cameraRead is { Frame.Outcome: ObservationReadOutcome.Codes } camera && camera.Frame.ObservedAt > Operation.ObservedAt &&
            camera.Frame.CompletedTicks > (readFeedback?.DetectedTicks ?? 0));
    bool MatchedCameraCue => cameraRead is { } frame && readFeedback is { OperatorInput: false } read &&
        read.RuntimeId == cameraReadRuntime && read.DeviceId == frame.Frame.DeviceId && read.Epoch == frame.Frame.Epoch && read.DetectedTicks == frame.Frame.CompletedTicks;
    bool CameraObservationUnmatched => !ManualPlacementResult && cameraReadRuntime == owner.Snapshot?.RuntimeId && cameraRead is { } camera &&
        camera.Frame.Epoch == CurrentReaderState?.Epoch && camera.Frame.Outcome == ObservationReadOutcome.Codes &&
        !(readFeedback is { OperatorInput: true } manual && manual.DetectedTicks > camera.Frame.CompletedTicks) &&
        !(RetainedRejectedInput && readFeedback!.DeviceId == camera.Frame.DeviceId && readFeedback.Epoch == camera.Frame.Epoch &&
            readFeedback.DetectedTicks >= camera.Frame.CompletedTicks) &&
        !(Input is { } input && input.Observation.OperatorInput is null && input.Observation.DeviceId == camera.Frame.DeviceId &&
            input.Observation.Epoch == camera.Frame.Epoch && input.Observation.SourceEventId == camera.Frame.SourceEventId);
    bool CameraAwaitingRead => !unreadFeedback && Input?.Observation.OperatorInput is null &&
        Operation?.Acquisition is { } acquired && CurrentReaderState is { } current &&
        (acquired.DeviceId != current.DeviceId || acquired.Epoch != current.Epoch) &&
        // After a reconnect the new connection re-read the same parcel still resting on the tray: the runtime
        // kept it as the same placement (no new request), so its original result stands again.
        !(ParcelInputReasons.IsSamePlacement(Input) && Input!.OperationId == Operation.Id &&
            Input.Observation.DeviceId == current.DeviceId && Input.Observation.Epoch == current.Epoch);
    // No-code is a frame fact, not a declaration that a parcel exists or has left.
    // Manual input has its own source and remains usable while the camera sees no code.
    bool CameraNoCode => CameraAwaitingRead || cameraReadRuntime == owner.Snapshot?.RuntimeId && cameraRead is { } camera &&
        camera.Frame.Epoch == CurrentReaderState?.Epoch && camera.Frame.Outcome != ObservationReadOutcome.Codes &&
        !(Operation is { } placed && camera.RetainedOperationId == placed.Id) &&
        !(Input is { } rejected && camera.RetainedObservationId == rejected.Observation.Id) &&
        !(unreadFeedback ? readFeedback?.OperatorInput == true : Input?.Observation.OperatorInput is not null);
    DeviceState? fastReaderState;
    DeviceState? CurrentReaderState
    {
        get
        {
            var published = owner.Snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == ReaderId);
            return fastReaderState is { } fast && (published is null || fast.UpdatedAt >= published.UpdatedAt) ? fast : published;
        }
    }
    bool unreadFeedback => !(ManualPlacementResult && readFeedback?.OperatorInput == false) && !RetainedRejectedInput && readFeedback is { } read && owner.Snapshot?.RuntimeId == read.RuntimeId &&
        read.ObservationId != Input?.Observation.Id && (Input is null ||
            (Input.Observation.Epoch == read.Epoch && read.SourceSequence > 0 && Input.Observation.Sequence > 0
                ? read.SourceSequence > Input.Observation.Sequence : Input.Observation.ObservedAt <= read.ObservedAt));
    bool invalidReadFeedback
    {
        get
        {
            if (CameraObservationUnmatched && cameraRead is { } camera && Stopwatch.GetElapsedTime(camera.Frame.CompletedTicks) >=
                TimeSpan.FromMilliseconds(Math.Clamp(readFeedback?.PendingDisplayBudgetMs ?? 10000, 1000, 120000))) return true;
            if (!unreadFeedback || readFeedback is not { } read) return false;
            var current = CurrentReaderState;
            return !read.OperatorInput && current is not null && current.Epoch != read.Epoch ||
                Stopwatch.GetElapsedTime(read.DetectedTicks) >= TimeSpan.FromMilliseconds(Math.Clamp(read.PendingDisplayBudgetMs, 1000, 120000));
        }
    }
    static string SingleDisplayCode(string? code) => code is { Length: > 0 and <= 4096 } && !code.Any(char.IsControl) ? code : "";
    string ObservedBarcode => ManualPlacementResult ? Operation!.ParcelCode : CameraNoCode ? "" : CameraObservationUnmatched ? MatchedCameraCue ? SingleDisplayCode(readFeedback!.SingleCode) : "" : unreadFeedback ? SingleDisplayCode(readFeedback!.SingleCode) : Input is { } input
        ? SingleDisplayCode(input.BarcodeSelection?.SelectedCode ?? (input.Observation.Codes is [var single] ? single : null))
        : Operation?.ParcelCode ?? "等待包裹";
    // Manual entry is an independent source (AGENTS.md): an offline camera does not pause it.
    bool ManualInputAvailable => owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId)?.BarcodeInput?.Manual == true;
    public string BarcodePlaceholder => !SupplyEnabled ? SupplyDisabling ? "停用中，不再接收新条码" : "本位已停用，不接收条码" : ReaderUnavailableForDisplay ? ManualInputAvailable ? "相机离线，可扫码枪或键盘输入条码" : "相机离线" : CameraAwaitingRead ? "相机已重连，请重新扫码" : CameraNoCode ? "等待放件；已放包裹请将条码朝上" : CameraObservationUnmatched && !MatchedCameraCue ? "正在核对本次条码" : (unreadFeedback ? readFeedback!.CandidateCount : Input?.Observation.Codes.Length ?? 0) > 1
        ? "本帧读到多个条码，请检查包裹" : HasCurrentInput ? "本次条码无效，请检查包裹" : "等待相机扫码或输入条码";
    public ParcelPictureQuery? CurrentPictureQuery => !owner.Fresh || CameraNoCode || CameraObservationUnmatched || unreadFeedback || IsProcessing ? null :
        Input is { Outcome: ParcelInputOutcome.Rejected, Observation.OperatorInput: null } rejected ? new(ObservationId: rejected.Observation.Id) : InputUnadmitted ? null :
        Input is { } resting && Operation is { } placed && ParcelInputReasons.IsSamePlacement(resting) && resting.OperationId == placed.Id ? new(OperationId: placed.Id) :
        Input is { } input && Operation is { } current && input.OperationId == current.Id &&
            (current.Acquisition is null || input.Observation.DeviceId == current.Acquisition.DeviceId &&
                input.Observation.Epoch == current.Acquisition.Epoch && input.Observation.SourceEventId == current.Acquisition.SourceEventId &&
                input.Observation.AdmissionId == current.Acquisition.AdmissionId) ? new(ObservationId: input.Observation.Id) :
        Input is null && Operation is { } operation ? new(OperationId: operation.Id) : null;
    public bool CanViewCurrentPicture => CurrentPictureQuery is not null;
    public string? BarcodeObservationKey => unreadFeedback ? ReadFeedbackKey : Input is { } input
        ? $"{owner.Snapshot?.RuntimeId}/{input.Observation.DeviceId}/{input.Observation.Epoch}/{input.Observation.Id}"
        : Operation is { } operation ? $"{owner.Snapshot?.RuntimeId}/{operation.Id}" : null;
    bool FastReaderBlocked => fastReaderState is { } state && (
        owner.Snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == ReaderId) is not { } published ||
        published.Epoch != state.Epoch || published.UpdatedAt < state.UpdatedAt);
    bool ReaderUnavailableForDisplay => owner.Fresh && (Input is not null || owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId)?.BarcodeInput?.Manual != true) && Input?.Observation.OperatorInput is null && !(unreadFeedback && readFeedback?.OperatorInput == true) &&
        (FastReaderBlocked || owner.Snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == ReaderId)?.IsValid(DateTimeOffset.UtcNow) != true);
    bool OfflineCameraSurface => ReaderUnavailableForDisplay && !HasUncertainOriginal && Operation?.State != ParcelState.WcsRejected && Input?.Outcome != ParcelInputOutcome.Rejected;
    // Retained property names are display contracts only; no previous destination is cached.
    public bool SlotProvisional => false;
    public bool IsProcessing => !SupplyDisabled && owner.Fresh && !DisplayPaused && !ReaderUnavailableForDisplay && !HasUncertainOriginal &&
        !CameraNoCode && !invalidReadFeedback && (CameraObservationUnmatched || unreadFeedback || !InputUnadmitted && Operation?.State == ParcelState.ResolvingWcs);
    bool DisplayPaused => owner.Snapshot?.Paused == true;
    public bool ShowConfirmedTask => !SupplyDisabled && CurrentTaskAvailable && SlotAvailable && !unreadFeedback && !ReaderUnavailableForDisplay && !DisplayPaused && owner.ViewportReady;
    public string? SuccessIdentity => ShowConfirmedTask ? $"{owner.Snapshot?.RuntimeId}/{ReaderId}/{Operation?.Id}/{Operation?.Wcs?.Request.Id}" : null;
    public string ResultBackground => DisplayPresentation.Tone switch
    {
        ParcelPresentationTone.Accepted => OperatorTokens.Success,
        ParcelPresentationTone.Rejected => OperatorTokens.Danger,
        ParcelPresentationTone.Unknown => OperatorTokens.Warning,
        ParcelPresentationTone.Stale => OperatorTokens.Inactive,
        _ => OperatorTokens.Neutral
    };
    public string ResultForeground => DisplayPresentation.Tone is ParcelPresentationTone.Accepted or ParcelPresentationTone.Rejected or ParcelPresentationTone.Unknown or ParcelPresentationTone.Stale ? OperatorTokens.Surface : OperatorTokens.Ink;
    public bool DisplaySlotVisible => ShowConfirmedTask || IsProcessing;
    public string DisplaySlot => IsProcessing ? "处理中…" : ShowConfirmedTask ? Slot : "";
    public string DisplaySlotForeground => ResultForeground;
    public ParcelStatusPresentation DisplayPresentation => !owner.Fresh ? ParcelPresentation.Stale() : ShowCenteredState ? ParcelPresentation.Neutral(CenteredStateTitle) : DisplayPaused ? ParcelPresentation.Neutral("供包已暂停") : HasUncertainOriginal ? Presentation : OfflineCameraSurface ? ParcelPresentation.Neutral(ManualInputAvailable ? "等待输入条码" : "等待供包条件就绪") :
        CameraNoCode && !HasUncertainOriginal ? ParcelPresentation.Neutral(CameraAwaitingRead ? "等待重新扫码" : "未读到条码") : invalidReadFeedback ? ParcelPresentation.Unknown("本次读码已失效") : IsProcessing ? ParcelPresentation.Pending("处理中") : CurrentTaskAvailable && !SlotAvailable ? ParcelPresentation.Unknown("未取得格口") : Presentation;
    public string DisplaySlotLabel => IsProcessing ? "本次处理中，尚未取得有效格口" : "本次有效格口，可选择复制";
    public string ContinuityNotice => "";
    public string DisplayStatus => SupplyDisabled ? "已停用" : ShowConfirmedTask && IsTight ? "按本次格口投递" : !owner.Fresh ? "状态已失效" : DisplayPaused ? "供包已暂停" : OfflineCameraSurface ? ManualInputAvailable ? "等待输入条码" : "暂不能接收条码" : HasUncertainOriginal ? Operation?.State == ParcelState.WcsUnknown ? "格口结果未确认" : Status :
        CameraNoCode ? CameraAwaitingRead ? "等待重新扫码" : "未读到条码" : invalidReadFeedback ? "本次读码已失效" : IsProcessing ? "本次处理中" : Operation?.State == ParcelState.WcsRejected ? "未取得格口" : Status;
    public string DisplayAttentionReason => SupplyDisabled || DisplayPaused ? "" : OfflineCameraSurface ? ManualInputAvailable ? "可用扫码枪或键盘输入完整条码。" : AttentionReason :
        CameraNoCode ? (CameraAwaitingRead ? "相机连接周期已变化，上一周期的格口已失效。" : cameraRead!.Frame.Outcome == ObservationReadOutcome.RejectedFrame ? "当前图像无法识别；上一件格口不可用于本件。" : "托盘上未读到条码；上一件格口不可用于本件。") +
            (HasUncertainOriginal ? " " + AttentionReason : "") :
        invalidReadFeedback ? "相机连接周期已变化，或本次读码等待已超时；没有本次有效格口。" : unreadFeedback ? HasUncertainOriginal ? "本次条码已读到；上一件 " + Operation?.ParcelCode + " 的格口仍未确认。" :
            "已读到条码，正在保存，尚未确认接收。" : AttentionReason;
    public string DisplayNextStep => SupplyDisabled ? "" : DisplayPaused ? "本件暂勿投递，等待恢复供包。" : ShowConfirmedTask ? "" : OfflineCameraSurface ? ManualInputAvailable ? "输入完整条码后按回车或点击提交。" : NextStep :
        CameraNoCode ? HasUncertainOriginal ? NextStep : "托盘空时直接放上包裹，条码朝上；已放包裹仍未读到，请调整标签或用扫码枪/键盘补录，取得本件格口后再投递。" : invalidReadFeedback ? "本件暂勿投递；请重新扫描并等待有效格口，未确认的原请求仍需核对。" : !HasUncertainOriginal && IsProcessing ? "等待本件格口，勿按上一件格口投递。" :
        !HasUncertainOriginal && Input is { Outcome: ParcelInputOutcome.Rejected, BarcodeSelection.SelectedCode: null } ? "本件未取得格口，请查看图片核对条码；将包裹移出托盘后，再放下一件。" : NextStep;
    public double DisplaySlotFontSize => IsProcessing ? UiTypography.Placeholder : SlotFontSize;
    public string CameraReadFeedback => !CameraNoCode && (!CameraObservationUnmatched || MatchedCameraCue) && !invalidReadFeedback && readFeedback is { } read && Stopwatch.GetElapsedTime(read.DetectedTicks) < TimeSpan.FromSeconds(1)
        ? "已读到条码，等待处理结果。" : "";
    public string ReadCueLabel => ManualPlacementResult || CameraReadFeedback.Length == 0 ? "" : "已读到" +
        (readFeedback?.SingleCode is { Length: > 0 } code && !code.Any(char.IsControl) ? " · …" + code[^Math.Min(4, code.Length)..] : "");
    public string? ReadFeedbackIdentity => readFeedback?.ObservationId;
    public string? ReadFeedbackKey => readFeedback is { } read ? $"{read.RuntimeId}/{read.DeviceId}/{read.Epoch}/{read.ObservationId}" : null;
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
    internal void ApplyCameraReadFeedback(Guid runtime, ParcelCameraReadFeedback value)
    {
        if (!SupplyEnabled) return;
        if (runtime != owner.Snapshot?.RuntimeId || value.PositionId != PositionId || value.Frame.DeviceId != ReaderId ||
            value.Frame.CompletedTicks <= 0 || value.Frame.CompletedTicks > Stopwatch.GetTimestamp() ||
            value.Frame.Epoch != CurrentReaderState?.Epoch ||
            cameraReadRuntime == runtime && cameraRead is { } old && (old.Frame.CompletedTicks >= value.Frame.CompletedTicks ||
                old.Frame.Epoch == value.Frame.Epoch && old.Frame.FrameNumber >= value.Frame.FrameNumber)) return;
        cameraReadRuntime = runtime; cameraRead = value; NotifySlotDisplay();
    }
    void UpdateSlotMemory(ParcelSnapshot? snapshot, DateTimeOffset now, bool fresh, bool viewportReady)
    {
        if (readFeedback?.RuntimeId != snapshot?.RuntimeId || readFeedback?.DeviceId != ReaderId) readFeedback = null;
        if (cameraReadRuntime != snapshot?.RuntimeId || cameraRead?.Frame.DeviceId != ReaderId) cameraRead = null;
        if (fastReaderState?.DeviceId != ReaderId) fastReaderState = null;
        if (fastReaderState is { } state && snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == ReaderId) is { } acknowledged &&
            acknowledged.Epoch == state.Epoch && acknowledged.UpdatedAt >= state.UpdatedAt) fastReaderState = null;
        if (snapshot?.ReadFeedbacks.SingleOrDefault(r => r.PositionId == PositionId) is { } read)
            ApplyReadFeedback(read);
        if (snapshot?.CameraReads.SingleOrDefault(f => f.PositionId == PositionId) is { } frame)
            ApplyCameraReadFeedback(snapshot.RuntimeId, frame);
    }
    void NotifySlotDisplay()
    {
        foreach (var (name, value) in new (string, object?)[] {
            (nameof(SupplyEnabled), SupplyEnabled), (nameof(SupplyDisabling), SupplyDisabling), (nameof(SupplyDisabled), SupplyDisabled),
            (nameof(CanChangeSupply), CanChangeSupply), (nameof(DisableLabel), DisableLabel), (nameof(EnableLabel), EnableLabel), (nameof(SupplyNotice), SupplyNotice), (nameof(SupplyStateText), SupplyStateText),
            (nameof(EnableBackground), EnableBackground), (nameof(EnableForeground), EnableForeground), (nameof(DisableBackground), DisableBackground), (nameof(DisableForeground), DisableForeground),
            (nameof(Barcode), Barcode), (nameof(HasCurrentInput), HasCurrentInput), (nameof(BarcodePlaceholder), BarcodePlaceholder),
            (nameof(BarcodeObservationKey), BarcodeObservationKey),
            (nameof(CanViewCurrentPicture), CanViewCurrentPicture), (nameof(CurrentTaskAvailable), CurrentTaskAvailable), (nameof(SlotAvailable), SlotAvailable), (nameof(Slot), Slot),
            (nameof(WaveNo), WaveNo), (nameof(TaskId), TaskId), (nameof(SkuId), SkuId), (nameof(ResponseQuantity), ResponseQuantity),
            (nameof(IsProcessing), IsProcessing), (nameof(SlotProvisional), SlotProvisional), (nameof(DisplaySlotVisible), DisplaySlotVisible), (nameof(DisplaySlot), DisplaySlot),
            (nameof(DisplayPresentation), DisplayPresentation), (nameof(ResultBackground), ResultBackground), (nameof(ResultForeground), ResultForeground), (nameof(SuccessIdentity), SuccessIdentity), (nameof(DisplaySlotForeground), DisplaySlotForeground), (nameof(DisplaySlotLabel), DisplaySlotLabel), (nameof(ContinuityNotice), ContinuityNotice),
            (nameof(ShowCenteredState), ShowCenteredState), (nameof(CenteredStateTitle), CenteredStateTitle), (nameof(CenteredStateForeground), CenteredStateForeground),
            (nameof(CenteredStateReason), CenteredStateReason), (nameof(CenteredStateInstruction), CenteredStateInstruction),
            (nameof(DisplayStatus), DisplayStatus), (nameof(DisplayAttentionReason), DisplayAttentionReason), (nameof(DisplayNextStep), DisplayNextStep), (nameof(ShowConfirmedTask), ShowConfirmedTask),
            (nameof(DisplaySlotFontSize), DisplaySlotFontSize), (nameof(CameraReadFeedback), CameraReadFeedback), (nameof(ReadCueLabel), ReadCueLabel), (nameof(ReadFeedbackIdentity), ReadFeedbackIdentity),
            (nameof(ReadFeedbackKey), ReadFeedbackKey), (nameof(RejectionIdentity), RejectionIdentity) })
        {
            if (published.TryGetValue(name, out var prior) && Equals(prior, value)) continue;
            published[name] = value; PropertyChanged?.Invoke(this, new(name));
        }
    }
}
