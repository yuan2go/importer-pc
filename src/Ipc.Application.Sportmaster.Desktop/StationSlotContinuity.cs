using Ipc.Application.ParcelFlow.Desktop;
using System.Diagnostics;
using Ipc.Application.Contracts;

namespace Ipc.Application.Sportmaster.Desktop;

public sealed partial class SupplyStationPositionViewModel
{
    sealed record PriorSlot(Guid Runtime, long Configuration, string Reader, string Epoch, string Code, string Slot, string Operation, long At);
    PriorSlot? priorSlot;
    string? lastRememberedOperation;
    ParcelReadFeedback? readFeedback;
    public bool SlotProvisional { get; private set; }
    bool unreadFeedback => readFeedback is { } read && owner.Snapshot?.RuntimeId == read.RuntimeId && read.ObservationId != Input?.Observation.Id && (Input is null || Input.Observation.ObservedAt <= read.ObservedAt);
    public bool DisplaySlotVisible => SlotProvisional || CurrentTaskAvailable && !unreadFeedback;
    public string DisplaySlot => SlotProvisional ? priorSlot!.Slot : Slot;
    public string DisplaySlotForeground => SlotProvisional ? "#68788E" : Presentation.Foreground;
    public ParcelStatusPresentation DisplayPresentation => !HasUncertainOriginal && (SlotProvisional || unreadFeedback) ? ParcelPresentation.Pending(DisplayStatus) : Presentation;
    public string DisplaySlotLabel => SlotProvisional ? "上次格口，正在确认本次请求，不能据此投递" : "本次有效格口，可选择复制";
    public string ContinuityNotice => SlotProvisional ? "上次格口 · 确认中；请等待本次回应后投递。" : "";
    public string DisplayStatus => HasUncertainOriginal ? Status : SlotProvisional ? "本次格口确认中" : unreadFeedback ? "已读到 · 等待本次处理" : Status;
    public string DisplayNextStep => !HasUncertainOriginal && (SlotProvisional || unreadFeedback) ? "已读到条码，等待本次处理；上次格口不能作为本次投递依据。" : NextStep;
    public bool ShowConfirmedTask => CurrentTaskAvailable && !unreadFeedback;
    public double DisplaySlotFontSize => SlotProvisional ? UiTypography.SlotSize(priorSlot!.Slot, IsCompact) : SlotFontSize;
    public string CameraReadFeedback => readFeedback is { } read && Stopwatch.GetElapsedTime(read.DetectedTicks) < TimeSpan.FromSeconds(1)
        ? "已读到 · " + (CurrentTaskAvailable && !unreadFeedback ? "本次格口已取得" : "等待本次处理") : "";
    public string ReadCueLabel => CameraReadFeedback.Length == 0 ? "" : "已读到";
    public string? ReadFeedbackIdentity => readFeedback?.ObservationId;
    internal void ApplyReadFeedback(ParcelReadFeedback value)
    {
        if (value.RuntimeId != owner.Snapshot?.RuntimeId || value.DeviceId != ReaderId || value.PositionId != PositionId ||
            value.DetectedTicks > Stopwatch.GetTimestamp() || Stopwatch.GetElapsedTime(value.DetectedTicks) > TimeSpan.FromSeconds(3)) return;
        if (readFeedback?.ObservationId == value.ObservationId) { UpdateSlotMemory(owner.Snapshot, DateTimeOffset.UtcNow, owner.Fresh, owner.ViewportReady); NotifySlotDisplay(); return; }
        readFeedback = value;
        UpdateSlotMemory(owner.Snapshot, DateTimeOffset.UtcNow, owner.Fresh, owner.ViewportReady);
        NotifySlotDisplay();
    }
    void UpdateSlotMemory(ParcelSnapshot? snapshot, DateTimeOffset now, bool fresh, bool viewportReady)
    {
        SlotProvisional = false;
        if (readFeedback?.RuntimeId != snapshot?.RuntimeId || readFeedback?.DeviceId != ReaderId) readFeedback = null;
        var epoch = unreadFeedback ? readFeedback!.Epoch : Input?.Observation.Epoch ?? Operation?.Acquisition?.Epoch;
        var code = unreadFeedback ? readFeedback!.SingleCode : Input?.BarcodeSelection?.SelectedCode ?? Operation?.ParcelCode;
        var usable = fresh && viewportReady && snapshot is { Paused: false } &&
            snapshot.Devices?.Any(d => d.DeviceId == ReaderId && d.IsValid(now)) == true &&
            snapshot.Health.Any(h => h.InstanceId == snapshot.Configuration.Effective.Wcs?.InstanceId && h.Ready);
        if (!usable || priorSlot is { } old && (snapshot!.RuntimeId != old.Runtime || snapshot.Configuration.Revision != old.Configuration ||
            ReaderId != old.Reader || epoch != old.Epoch || code != old.Code)) priorSlot = null;
        if (CurrentTaskAvailable && !string.IsNullOrWhiteSpace(Operation!.Slot))
        {
            // A faster read cue may already refer to the next same-code observation
            // while this consistent snapshot publishes the previous accepted reply.
            // It remains a valid *prior* result, never a confirmed value for that cue.
            if (usable && Operation.Acquisition is { } acquisition && acquisition.Epoch == epoch && code == Operation.ParcelCode && lastRememberedOperation != Operation.Id)
            {
                priorSlot = new(snapshot!.RuntimeId, snapshot.Configuration.Revision, ReaderId, epoch ?? "", Operation.ParcelCode, Operation.Slot!, Operation.Id, Stopwatch.GetTimestamp());
                lastRememberedOperation = Operation.Id;
            }
            if (!unreadFeedback) return;
        }
        // Only a pending new intent may borrow a visibly unconfirmed value. Rejection,
        // Unknown, blocked input and stale status always discard it, without changing business facts.
        var pending = unreadFeedback || !InputUnadmitted && Operation?.State == ParcelState.ResolvingWcs;
        if (!pending || !usable || priorSlot is null || Stopwatch.GetElapsedTime(priorSlot.At) >= TimeSpan.FromMilliseconds(1500))
        { priorSlot = null; return; }
        SlotProvisional = true;
    }
    void NotifySlotDisplay()
    {
        foreach (var (name, value) in new (string, object?)[] {
            (nameof(SlotProvisional), SlotProvisional), (nameof(DisplaySlotVisible), DisplaySlotVisible), (nameof(DisplaySlot), DisplaySlot),
            (nameof(DisplayPresentation), DisplayPresentation), (nameof(DisplaySlotForeground), DisplaySlotForeground), (nameof(DisplaySlotLabel), DisplaySlotLabel), (nameof(ContinuityNotice), ContinuityNotice),
            (nameof(DisplayStatus), DisplayStatus), (nameof(DisplayNextStep), DisplayNextStep), (nameof(ShowConfirmedTask), ShowConfirmedTask),
            (nameof(DisplaySlotFontSize), DisplaySlotFontSize), (nameof(CameraReadFeedback), CameraReadFeedback), (nameof(ReadCueLabel), ReadCueLabel), (nameof(ReadFeedbackIdentity), ReadFeedbackIdentity) })
        {
            if (published.TryGetValue(name, out var prior) && Equals(prior, value)) continue;
            published[name] = value; PropertyChanged?.Invoke(this, new(name));
        }
    }
}
