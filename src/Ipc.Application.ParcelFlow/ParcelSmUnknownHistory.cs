using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    // Leave the operation in its current owner until the actual call and T3
    // finish. A deadline, a new display, and a pending raw receipt grant no custody.
    bool CanRetainSmUnknown(ParcelOperation op) =>
        ParcelRepository.IsSmUnknownHistoryCandidate(op) &&
        (releasedPlacements.Contains(op.Id) || IsDebugRequest(op)) &&
        !StageRunning(op) &&
        !identityConflicts.Values.Any(conflict => conflict.OriginalOperation == op.Id) &&
        !repository!.HasPendingReceipt(op.Id);

    void RetainReleasedSmUnknowns()
    {
        if (Id != "sportmaster" || backupInProgress || failure is not null) return;
        while (true)
        {
            var batch = operations.Values.Where(op => !obsoleteSmOriginals.Contains(op.Id) && CanRetainSmUnknown(op)).Take(128).ToArray();
            if (batch.Length == 0) return;
            repository!.ArchiveSmUnknowns(batch);
            RemoveArchivedFromRuntime(batch.Select(op => op.Id).ToArray(), unknowns: batch.Length);
            foreach (var op in batch)
                WriteRuntimeLog("供包状态", "未确认格口已转入历史记录", new
                {
                    OperationId = op.Id,
                    op.PositionId,
                    RequestId = op.Wcs!.Request.Id,
                    ProtocolRequestId = op.Wcs.Request.IntegrationRequest?.RequestId,
                    op.ConfigurationHash,
                    ConfigurationRevision = configuration!.Current.Revision,
                    op.Wcs.Failure,
                    Debug = IsDebugRequest(op)
                }, operationId: op.Id);
        }
    }

    bool StageRunning(ParcelOperation op) => activeStages.Any(key => key.StartsWith(op.Id + ":", StringComparison.Ordinal));
}

public sealed partial class ParcelRepository
{
    internal static bool IsSmUnknownHistoryCandidate(ParcelOperation op) =>
        op.FrozenConfiguration is { ApplicationId: "sportmaster", Images: null, Plc: null } &&
        op is { State: ParcelState.WcsUnknown, Capture: null, Upload: null, Plc: null, Image: null } &&
        op.OriginalFrame?.State != "Pending" &&
        op.Wcs is
        {
            State: StageState.Unknown, Attempts: 1,
            Request.Binding: { Package: null, Implementation: "sportmaster-rcs", Contract: RcsRequestPolicy.Contract }
        };

    internal bool HasPendingReceipt(string operation)
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM parcel_receipts WHERE operation=$id AND state NOT IN ('Committed','NoReply'))";
        command.Parameters.AddWithValue("$id", operation);
        return (long)command.ExecuteScalar()! != 0;
    }

    internal void ArchiveSmUnknowns(ParcelOperation[] originals)
    {
        // Same atomic original-byte move as other history. This separate entry point
        // does not confer a terminal result or permission to expire the record.
        if (originals.Any(op => !IsSmUnknownHistoryCandidate(op)))
            throw new InvalidOperationException("Only closed SM execution with an unknown RCS outcome can enter this custody path");
        MoveToHistory(originals.Select(op => op.Id).ToArray(), "SmUnknownHistoryRetained",
            "Placement ended or debug only; external execution ended and original receipt custody closed; outcome remains Unknown");
    }
}
