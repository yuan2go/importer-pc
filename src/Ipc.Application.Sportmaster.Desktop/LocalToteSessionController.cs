using System.ComponentModel;
using Ipc.Application.Contracts;

namespace Ipc.Application.Sportmaster.Desktop;

// The current open tote is window-local. Only production-lane facts already
// accepted by the Application contribute, and ending a tote sends no command.
public sealed class LocalToteSessionController(Action<ToteSessionSummary>? record = null, TimeProvider? clock = null) : IToteSessionController
{
    readonly HashSet<string> dispatched = new(StringComparer.Ordinal);
    readonly TimeProvider time = clock ?? TimeProvider.System;
    string containerCode = "";
    int? plannedCount;
    DateTimeOffset startedAt;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ContainerCode
    {
        get => containerCode;
        set
        {
            if (value.Length > 128 || value.Any(char.IsControl))
                throw new ArgumentException(UiText.Get("Tote.InvalidCode"), nameof(value));
            if (value == containerCode) return;
            if (LeftHistory.Count > 0 || RightHistory.Count > 0)
                throw new InvalidOperationException(UiText.Get("Tote.FinishBeforeChanging"));
            startedAt = time.GetUtcNow();
            containerCode = value;
            Changed(nameof(ContainerCode));
        }
    }

    public int DispatchedCount => dispatched.Count;
    public int? PlannedCount
    {
        get => plannedCount;
        set
        {
            if (value is <= 0) throw new ArgumentOutOfRangeException(nameof(value), UiText.Get("Tote.InvalidPlan"));
            if (plannedCount == value) return;
            plannedCount = value;
            Changed(nameof(PlannedCount), nameof(HasShortage), nameof(MissingCount));
        }
    }

    public bool HasShortage => PlannedCount is > 0 && DispatchedCount < PlannedCount.Value;
    public int MissingCount => HasShortage ? PlannedCount!.Value - DispatchedCount : 0;
    public IReadOnlyList<ToteScanRecord> LeftHistory { get; private set; } = [];
    public IReadOnlyList<ToteScanRecord> RightHistory { get; private set; } = [];

    public void Observe(IEnumerable<ParcelOperation> operations)
    {
        if (string.IsNullOrWhiteSpace(ContainerCode)) return;
        var count = DispatchedCount;
        var left = LeftHistory;
        var right = RightHistory;
        foreach (var operation in operations)
        {
            if (operation.ObservedAt < startedAt || operation.DebugRequest is not null ||
                operation.PositionId is not ("left" or "right") ||
                operation.State is not (ParcelState.SlotResolved or ParcelState.WcsRejected or ParcelState.WcsUnknown or ParcelState.UnpackRequired)) continue;
            if (operation.State == ParcelState.SlotResolved) dispatched.Add(operation.Id);
            var row = new ToteScanRecord(operation.Id, operation.ObservedAt, operation.ParcelCode, operation.Slot, operation.State);
            if (operation.PositionId == "left") left = Upsert(left, row);
            else right = Upsert(right, row);
        }
        if (!LeftHistory.SequenceEqual(left)) { LeftHistory = left; Changed(nameof(LeftHistory)); }
        if (!RightHistory.SequenceEqual(right)) { RightHistory = right; Changed(nameof(RightHistory)); }
        if (DispatchedCount != count) Changed(nameof(DispatchedCount), nameof(HasShortage), nameof(MissingCount));
    }

    static IReadOnlyList<ToteScanRecord> Upsert(IReadOnlyList<ToteScanRecord> rows, ToteScanRecord row)
        => rows.Where(previous => previous.OperationId != row.OperationId).Append(row)
            .OrderByDescending(item => item.At).ThenBy(item => item.OperationId, StringComparer.Ordinal).Take(8).ToArray();

    public void ResetSession()
    {
        record?.Invoke(new(DispatchedCount, PlannedCount));
        containerCode = "";
        plannedCount = null;
        dispatched.Clear();
        LeftHistory = [];
        RightHistory = [];
        Changed(nameof(ContainerCode), nameof(DispatchedCount), nameof(PlannedCount), nameof(HasShortage),
            nameof(MissingCount), nameof(LeftHistory), nameof(RightHistory));
    }

    void Changed(params string[] properties)
    {
        foreach (var property in properties) PropertyChanged?.Invoke(this, new(property));
    }
}
