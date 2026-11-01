using Ipc.Application.ParcelFlow.Desktop;
using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

public sealed partial class SupplyStationPositionViewModel
{
    ParcelReadFeedback? readFeedback;
    DeviceState? fastReaderState;
    bool unreadFeedback => readFeedback is { } read && owner.Snapshot?.RuntimeId == read.RuntimeId &&
        read.ObservationId != Input?.Observation.Id && (Input is null ||
            (Input.Observation.Epoch == read.Epoch && read.SourceSequence > 0 && Input.Observation.Sequence > 0
                ? read.SourceSequence > Input.Observation.Sequence : Input.Observation.ObservedAt <= read.ObservedAt));
    static string SingleDisplayCode(string? code) => code is { Length: > 0 and <= 4096 } && !code.Any(char.IsControl) ? code : "";
    string ObservedBarcode => unreadFeedback ? SingleDisplayCode(readFeedback!.SingleCode) : Input is { } input
        ? SingleDisplayCode(input.BarcodeSelection?.SelectedCode ?? (input.Observation.Codes is [var single] ? single : null))
        : Operation?.ParcelCode ?? "等待包裹";
    public string BarcodePlaceholder => (unreadFeedback ? readFeedback!.CandidateCount : Input?.Observation.Codes.Length ?? 0) > 1
        ? "本帧读到多个条码，请检查包裹" : HasCurrentInput ? "本次条码无效，请检查包裹" : "等待相机扫码或输入条码";
    public ParcelPictureQuery? CurrentPictureQuery => !owner.Fresh || unreadFeedback || InputUnadmitted ? null :
        Input is { } input && Operation is { } current && input.OperationId == current.Id &&
            (current.Acquisition is null || input.Observation.DeviceId == current.Acquisition.DeviceId &&
                input.Observation.Epoch == current.Acquisition.Epoch && input.Observation.SourceEventId == current.Acquisition.SourceEventId &&
                input.Observation.AdmissionId == current.Acquisition.AdmissionId) ? new(ObservationId: input.Observation.Id) :
        Input is null && Operation is { } operation ? new(OperationId: operation.Id) : null;
    public bool CanViewCurrentPicture => CurrentPictureQuery is not null;
    bool FastReaderBlocked => fastReaderState is { } state && (
        owner.Snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == ReaderId) is not { } published ||
        published.Epoch != state.Epoch || published.UpdatedAt < state.UpdatedAt);
    bool ReaderUnavailableForDisplay => owner.Fresh && (Input is not null || owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId)?.BarcodeInput?.Manual != true) && Input?.Observation.OperatorInput is null && !(unreadFeedback && readFeedback?.OperatorInput == true) &&
        (FastReaderBlocked || owner.Snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == ReaderId)?.IsValid(DateTimeOffset.UtcNow) != true);
    // Retained property names are display contracts only; no previous destination is cached.
    public bool SlotProvisional => false;
    public bool IsProcessing => owner.Fresh && !DisplayPaused && !ReaderUnavailableForDisplay && !HasUncertainOriginal &&
        (unreadFeedback || !InputUnadmitted && Operation?.State == ParcelState.ResolvingWcs);
    bool DisplayPaused => owner.Snapshot?.Paused == true;
    public bool ShowConfirmedTask => CurrentTaskAvailable && SlotAvailable && !unreadFeedback && !ReaderUnavailableForDisplay && !DisplayPaused && owner.ViewportReady;
    public string? SuccessIdentity => ShowConfirmedTask ? $"{owner.Snapshot?.RuntimeId}/{ReaderId}/{Input?.Observation.Id ?? Operation?.Id}/{Operation?.Wcs?.Request.Id}" : null;
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
    public ParcelStatusPresentation DisplayPresentation => !owner.Fresh ? ParcelPresentation.Stale() : DisplayPaused ? ParcelPresentation.Neutral("供包已暂停") : ReaderUnavailableForDisplay ? ParcelPresentation.Rejected("相机：离线或恢复中") :
        IsProcessing ? ParcelPresentation.Pending("处理中") : CurrentTaskAvailable && !SlotAvailable ? ParcelPresentation.Unknown("未取得格口") : Presentation;
    public string DisplaySlotLabel => IsProcessing ? "本次处理中，尚未取得有效格口" : "本次有效格口，可选择复制";
    public string ContinuityNotice => "";
    public string DisplayStatus => ShowConfirmedTask && IsTight ? "按本次格口投递" : !owner.Fresh ? "状态已失效" : DisplayPaused ? "供包已暂停" : ReaderUnavailableForDisplay ? "相机离线 · 本位暂停供包" : HasUncertainOriginal ? Status :
        IsProcessing ? "本次处理中" : Status;
    public string DisplayAttentionReason => DisplayPaused ? "" : ReaderUnavailableForDisplay ? "相机恢复连接后自动继续；另一投递位不受影响。" :
        unreadFeedback ? HasUncertainOriginal ? "本次条码已读到；上一件 " + Operation?.ParcelCode + " 的结果仍待核对。" :
            "已读到条码，正在保存，尚未确认接收。" : AttentionReason;
    public string DisplayNextStep => DisplayPaused ? "本件暂勿投递，等待恢复供包。" : ShowConfirmedTask ? IsTight ? "" : "按本次格口投递" : ReaderUnavailableForDisplay ? "本位暂停供包，请通知维护人员检查相机。" :
        !HasUncertainOriginal && IsProcessing ? "等待本件格口，勿按上一件格口投递。" : NextStep;
    public double DisplaySlotFontSize => IsProcessing ? UiTypography.Placeholder : IsCompact ? OperatorTokens.SlotCompactSize : OperatorTokens.SlotSize;
    public string CameraReadFeedback => readFeedback is { } read && Stopwatch.GetElapsedTime(read.DetectedTicks) < TimeSpan.FromSeconds(1)
        ? "已读到条码，等待处理结果。" : "";
    public string ReadCueLabel => CameraReadFeedback.Length == 0 ? "" : "已读到" +
        (readFeedback?.SingleCode is { Length: > 0 } code && !code.Any(char.IsControl) ? " · …" + code[^Math.Min(4, code.Length)..] : "");
    public string? ReadFeedbackIdentity => readFeedback?.ObservationId;
    public string? ReadFeedbackKey => readFeedback is { } read ? $"{read.RuntimeId}/{read.DeviceId}/{read.Epoch}/{read.ObservationId}" : null;
    public string? RejectionIdentity => !unreadFeedback && (InputFailed || Operation?.State is ParcelState.WcsRejected or ParcelState.WcsUnknown or ParcelState.UnpackRequired || Operation?.Wcs?.State == StageState.Conflict)
        ? $"{owner.Snapshot?.RuntimeId}/{ReaderId}/{Input?.Observation.Epoch}/{Input?.Observation.Id ?? Operation?.Id}" : null;
    internal void ApplyReadFeedback(ParcelReadFeedback value)
    {
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
    void UpdateSlotMemory(ParcelSnapshot? snapshot, DateTimeOffset now, bool fresh, bool viewportReady)
    {
        if (readFeedback?.RuntimeId != snapshot?.RuntimeId || readFeedback?.DeviceId != ReaderId) readFeedback = null;
        if (fastReaderState?.DeviceId != ReaderId) fastReaderState = null;
        if (fastReaderState is { } state && snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == ReaderId) is { } acknowledged &&
            acknowledged.Epoch == state.Epoch && acknowledged.UpdatedAt >= state.UpdatedAt) fastReaderState = null;
    }
    void NotifySlotDisplay()
    {
        foreach (var (name, value) in new (string, object?)[] {
            (nameof(Barcode), Barcode), (nameof(HasCurrentInput), HasCurrentInput), (nameof(BarcodePlaceholder), BarcodePlaceholder),
            (nameof(CanViewCurrentPicture), CanViewCurrentPicture), (nameof(CurrentTaskAvailable), CurrentTaskAvailable), (nameof(SlotAvailable), SlotAvailable), (nameof(Slot), Slot),
            (nameof(WaveNo), WaveNo), (nameof(TaskId), TaskId), (nameof(SkuId), SkuId), (nameof(ResponseQuantity), ResponseQuantity),
            (nameof(IsProcessing), IsProcessing), (nameof(SlotProvisional), SlotProvisional), (nameof(DisplaySlotVisible), DisplaySlotVisible), (nameof(DisplaySlot), DisplaySlot),
            (nameof(DisplayPresentation), DisplayPresentation), (nameof(ResultBackground), ResultBackground), (nameof(ResultForeground), ResultForeground), (nameof(SuccessIdentity), SuccessIdentity), (nameof(DisplaySlotForeground), DisplaySlotForeground), (nameof(DisplaySlotLabel), DisplaySlotLabel), (nameof(ContinuityNotice), ContinuityNotice),
            (nameof(DisplayStatus), DisplayStatus), (nameof(DisplayAttentionReason), DisplayAttentionReason), (nameof(DisplayNextStep), DisplayNextStep), (nameof(ShowConfirmedTask), ShowConfirmedTask),
            (nameof(DisplaySlotFontSize), DisplaySlotFontSize), (nameof(CameraReadFeedback), CameraReadFeedback), (nameof(ReadCueLabel), ReadCueLabel), (nameof(ReadFeedbackIdentity), ReadFeedbackIdentity),
            (nameof(ReadFeedbackKey), ReadFeedbackKey), (nameof(RejectionIdentity), RejectionIdentity) })
        {
            if (published.TryGetValue(name, out var prior) && Equals(prior, value)) continue;
            published[name] = value; PropertyChanged?.Invoke(this, new(name));
        }
    }
}
