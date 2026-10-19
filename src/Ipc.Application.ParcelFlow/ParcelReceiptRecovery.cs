using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed record ParcelReceiptResponsibility(string Id, string Operation, string Stage, string Request, string Source, string State, string? RawHash, string? QueryOccurrence = null, string? Custody = null);
public sealed partial class ParcelRepository
{
    public ParcelReceiptResponsibility[] ReceiptResponsibilities(string operation)
    {
        using var c = db.CreateCommand();
        c.CommandText = "SELECT id,operation,stage,request,source,state,raw_hash FROM parcel_receipts WHERE operation=$op AND (state IN ('Awaiting','RawDurable') OR rowid IN (SELECT rowid FROM parcel_receipts WHERE operation=$op ORDER BY rowid DESC LIMIT 200)) ORDER BY rowid DESC";
        c.Parameters.AddWithValue("$op", operation); using var r = c.ExecuteReader(); List<ParcelReceiptResponsibility> rows = [];
        while (r.Read()) rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.IsDBNull(6) ? null : r.GetString(6)));
        return rows.Select(x => x with { QueryOccurrence = CustodyQuery(x.Id), Custody = x.State == "Awaiting" && x.RawHash is null ? "OriginalRawMissing" : null }).ToArray();
    }
    public ParcelReceiptResponsibility? ReceiptById(string id)
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT id,operation,stage,request,source,state,raw_hash FROM parcel_receipts WHERE id=$id";
        c.Parameters.AddWithValue("$id",id); using var r = c.ExecuteReader();
        ParcelReceiptResponsibility? row = !r.Read() ? null : new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.IsDBNull(6) ? null : r.GetString(6));
        r.Close(); return row is null ? null : row with { QueryOccurrence = CustodyQuery(row.Id), Custody = row.State == "Awaiting" && row.RawHash is null ? "OriginalRawMissing" : null };
    }
    public bool MissingAwaitingRaw(string id) => !File.Exists(Path.Combine(rawRoot, id + ".json")) &&
        PendingReceipts().Any(x => x.Id == id && x.Reply is null) && MissingAwaitingRow(id);
    bool MissingAwaitingRow(string id)
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT count(*) FROM parcel_receipts WHERE id=$id AND state='Awaiting' AND raw_hash IS NULL";
        c.Parameters.AddWithValue("$id", id); return (long)c.ExecuteScalar()! == 1;
    }
    public bool HasMainReceiptCustody(ParcelOperation op)
    {
        foreach (var stage in new[] { op.Wcs, op.Plc }.OfType<StageFact>())
        {
            if (stage.State is not (StageState.Accepted or StageState.Rejected)) return false;
            var terminal = stage.Replies.Where(r => r.BusinessAccepted is not null).ToArray();
            if (terminal.Length == 0) return false;
            foreach (var reply in terminal)
            {
                if (reply.RequestId != stage.Request.Id || reply.PayloadHash != stage.Request.PayloadHash ||
                    !reply.TransportSucceeded || reply.ProtocolAcknowledged != true || (reply.BusinessAccepted == true) != (stage.State == StageState.Accepted) ||
                    stage.Request.Stage == "wcs-slot" && reply.BusinessAccepted == true && string.IsNullOrWhiteSpace(reply.Slot)) return false;
                if (!HasCommittedReplyBytes(op, stage, reply)) return false;
            }
        }
        return op.Wcs is not null || op.Plc is not null;
    }
}
public sealed partial class ParcelRuntime
{
    void RestoreMainProjections()
    {
        foreach (var before in operations.Values.Where(x => x.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached &&
                     x.State is ParcelState.Blocked or ParcelState.ImageUnknown).ToArray())
        {
            if (!repository!.HasMainReceiptCustody(before) || repository.PendingReceipts().Any(x => x.Operation == before.Id && x.Stage != "image-upload")) continue;
            var restored = definition.RestoreMainProjection(before);
            if (restored != before)
                Save(restored with { RecoveryRequired = false }, "MainProjectionRecovered", Wire.Encode(new { Original = before, Basis = "Existing committed original main-stage raw; attached receipt responsibility unchanged" }));
        }
    }
    async Task<BusinessReceipt> ReconcileReceiptAsync(BusinessCommand command)
    {
        if (!CanConfigure || command.Owner is null || string.IsNullOrWhiteSpace(command.Value) || command.Value.Length > 1024)
            return new("Rejected", "Pause, authenticate maintenance, select the original receipt occurrence and provide an inspection reason", repository!.Revision);
        var pending = repository!.PendingReceipts().SingleOrDefault(x => x.Id == command.Owner);
        if (pending.Id is null || !repository.MissingAwaitingRaw(pending.Id) || !operations.TryGetValue(pending.Operation, out var op))
            return new("Rejected", "Only original Awaiting with no recorded raw can be queried; durable/missing/changed originals retain custody isolation", repository.Revision);
        if (repository.HasReceiptOutcomeBasis(pending.Id)) return new("OutcomeKnownRawMissing", "Business outcome is known; original raw custody, image and package responsibility remain", repository.Revision);
        var kind = ParseStage(pending.Stage); var stage = GetStage(op, kind);
        if (stage is null || stage.Request.Id != pending.Request || stage.State == StageState.Conflict || activeStages.Contains(op.Id + ":" + kind))
            return new("Rejected", "Original stage unavailable, conflicting or in use", repository.Revision);
        var auditReason = Wire.Encode(new { OriginalOccurrence = pending.Id, OriginalSource = repository.ReceiptById(pending.Id)!.Source,
            Actor = maintenanceSession.Identity, Reason = command.Value, MissingRaw = true, Policy = "Authoritative original-query outcome only; absent old raw remains absent and original occurrence retained" });
        await ExecuteStageAsync(op, kind, true, pending.Id, auditReason, command).ConfigureAwait(false);
        ReleaseCompletedImages();
        return new(repository.HasReceiptOutcomeBasis(pending.Id) ? "OutcomeKnownRawMissing" : "Unresolved",
            "Original occurrence retained; inspect ReceiptResponsibilities and ReceiptOutcomeKnown audit. A query never supplies the old raw.", repository.Revision);
    }
}
