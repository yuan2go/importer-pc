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
        read.ObservationId != Input?.Observation.Id && (Input is null || Input.Observation.ObservedAt <= read.ObservedAt);
    bool FastReaderBlocked => fastReaderState is { } state && (
        owner.Snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == ReaderId) is not { } published ||
        published.Epoch != state.Epoch || published.UpdatedAt < state.UpdatedAt);
    bool ReaderUnavailableForDisplay => owner.Fresh && Input?.Observation.OperatorInput is null && !(unreadFeedback && readFeedback?.OperatorInput == true) &&
        (FastReaderBlocked || owner.Snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == ReaderId)?.IsValid(DateTimeOffset.UtcNow) != true);
    // Retained property names are display contracts only; no previous destination is cached.
    public bool SlotProvisional => false;
    public bool IsProcessing => owner.Fresh && !ReaderUnavailableForDisplay && !HasUncertainOriginal &&
        (unreadFeedback || !InputUnadmitted && Operation?.State == ParcelState.ResolvingWcs);
    public bool ShowConfirmedTask => CurrentTaskAvailable && !unreadFeedback && !ReaderUnavailableForDisplay;
    public bool DisplaySlotVisible => ShowConfirmedTask || IsProcessing;
    public string DisplaySlot => IsProcessing ? "处理中…" : ShowConfirmedTask ? Slot : "";
    public string DisplaySlotForeground => IsProcessing ? "#536577" : Presentation.Foreground;
    public ParcelStatusPresentation DisplayPresentation => ReaderUnavailableForDisplay ? ParcelPresentation.Rejected("相机：离线或恢复待确认") :
        IsProcessing ? ParcelPresentation.Neutral("处理中") : Presentation;
    public string DisplaySlotLabel => IsProcessing ? "本次处理中，尚未取得有效格口" : "本次有效格口，可选择复制";
    public string ContinuityNotice => "";
    public string DisplayStatus => ReaderUnavailableForDisplay ? "相机不可用 · 本位停止供包" : HasUncertainOriginal ? Status :
        IsProcessing ? "本次处理中" : Status;
    public string DisplayAttentionReason => ReaderUnavailableForDisplay ? "等待本位相机连接及新状态确认；另一位独立运行。" :
        unreadFeedback && !HasUncertainOriginal ? "已读到，正在保存本次识别；尚未确认持久接收。" : AttentionReason;
    public string DisplayNextStep => ReaderUnavailableForDisplay ? "本位相机不可用，请检查连接。" :
        !HasUncertainOriginal && IsProcessing ? "等待本次结果；请勿依据上一件格口投递。" : NextStep;
    public double DisplaySlotFontSize => IsProcessing ? UiTypography.Placeholder : SlotFontSize;
    public string CameraReadFeedback => readFeedback is { } read && Stopwatch.GetElapsedTime(read.DetectedTicks) < TimeSpan.FromSeconds(1)
        ? "已读到；识别反馈不代表持久接收或准入成功。" : "";
    public string ReadCueLabel => CameraReadFeedback.Length == 0 ? "" : "已读到" +
        (readFeedback?.SingleCode is { Length: > 0 } code && !code.Any(char.IsControl) ? " · …" + code[^Math.Min(4, code.Length)..] : "");
    public string? ReadFeedbackIdentity => readFeedback?.ObservationId;
    public string? ReadFeedbackKey => readFeedback is { } read ? $"{read.RuntimeId}/{read.DeviceId}/{read.Epoch}/{read.ObservationId}" : null;
    public string? RejectionIdentity => !unreadFeedback && (InputFailed || Operation?.State == ParcelState.WcsRejected)
        ? $"{owner.Snapshot?.RuntimeId}/{ReaderId}/{Input?.Observation.Epoch}/{Input?.Observation.Id ?? Operation?.Id}" : null;
    internal void ApplyReadFeedback(ParcelReadFeedback value)
    {
        if (value.RuntimeId != owner.Snapshot?.RuntimeId || value.DeviceId != ReaderId || value.PositionId != PositionId ||
            value.DetectedTicks > Stopwatch.GetTimestamp() || Stopwatch.GetElapsedTime(value.DetectedTicks) > TimeSpan.FromSeconds(3) ||
            readFeedback is { } old && value.DetectedTicks < old.DetectedTicks) return;
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
            (nameof(IsProcessing), IsProcessing), (nameof(SlotProvisional), SlotProvisional), (nameof(DisplaySlotVisible), DisplaySlotVisible), (nameof(DisplaySlot), DisplaySlot),
            (nameof(DisplayPresentation), DisplayPresentation), (nameof(DisplaySlotForeground), DisplaySlotForeground), (nameof(DisplaySlotLabel), DisplaySlotLabel), (nameof(ContinuityNotice), ContinuityNotice),
            (nameof(DisplayStatus), DisplayStatus), (nameof(DisplayAttentionReason), DisplayAttentionReason), (nameof(DisplayNextStep), DisplayNextStep), (nameof(ShowConfirmedTask), ShowConfirmedTask),
            (nameof(DisplaySlotFontSize), DisplaySlotFontSize), (nameof(CameraReadFeedback), CameraReadFeedback), (nameof(ReadCueLabel), ReadCueLabel), (nameof(ReadFeedbackIdentity), ReadFeedbackIdentity),
            (nameof(ReadFeedbackKey), ReadFeedbackKey), (nameof(RejectionIdentity), RejectionIdentity) })
        {
            if (published.TryGetValue(name, out var prior) && Equals(prior, value)) continue;
            published[name] = value; PropertyChanged?.Invoke(this, new(name));
        }
    }
}
