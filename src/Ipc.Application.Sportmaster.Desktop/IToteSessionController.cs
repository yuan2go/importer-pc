using System.ComponentModel;
using Ipc.Application.Contracts;

namespace Ipc.Application.Sportmaster.Desktop;

public sealed record ToteSessionSummary(int DispatchedCount, int? PlannedCount);
public sealed record ToteScanRecord(string OperationId, DateTimeOffset At, string Barcode, string? Slot, ParcelState State);

public interface IToteSessionController : INotifyPropertyChanged
{
    string ContainerCode { get; set; }
    int DispatchedCount { get; }
    int? PlannedCount { get; set; }
    bool HasShortage { get; }
    int MissingCount { get; }
    IReadOnlyList<ToteScanRecord> LeftHistory { get; }
    IReadOnlyList<ToteScanRecord> RightHistory { get; }
    void Observe(IEnumerable<ParcelOperation> operations);
    void ResetSession();
}
