using Ipc.Platform;

namespace Ipc.Supply;

public sealed partial class StationApplication
{
    string ReceiptFence => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(store.Path)!, "receipt-owner-fence.json");
    readonly HashSet<string> receiptOwners = [];
    readonly HashSet<string> incompleteReceipts = [];
    void CloseReceiptFence()
    {
        if (incompleteReceipts.Count == 0) File.Delete(ReceiptFence);
        else ExtensionStore.AtomicWrite(ReceiptFence, incompleteReceipts.Order(StringComparer.Ordinal).ToArray());
    }
    void RecoverReceiptFence()
    {
        if (File.Exists(ReceiptFence))
            foreach (var id in Wire.Decode<string[]>(File.ReadAllText(ReceiptFence)))
            {
                var request = store.Request(id);
                if (request?.Status is RequestStatus.Accepted or RequestStatus.Rejected or RequestStatus.CancelledBeforeDispatch)
                {
                    // If raw custody exists it can be replayed below. Otherwise a dirty
                    // receiver may have lost a late terminal receipt before it could write.
                    var hasRaw = Directory.Exists(ReceiptDirectory) && Directory.GetFiles(ReceiptDirectory, "*.json").Any(path => Wire.Decode<ReceiptIngress>(File.ReadAllText(path)).RequestId == id);
                    if (!hasRaw) SetBlock(state, "ReceiptPersistenceUncertain", "request", id, request.PayloadHash, "approved-authoritative-reconciliation");
                }
            }
        foreach (var request in store.Requests().Where(x => x.Status is RequestStatus.Accepted or RequestStatus.Rejected or RequestStatus.CancelledBeforeDispatch)) receiptOwners.Add(request.Id);
        ArmReceiptFence(); // startup cannot admit work without a durable intake fence
    }
    void ArmReceiptFence(string? requestId = null)
    {
        if (requestId is not null) receiptOwners.Add(requestId);
        ExtensionStore.AtomicWrite(ReceiptFence, receiptOwners.Order(StringComparer.Ordinal).ToArray());
    }
    sealed record ReceiptIngress(string Id, string RequestId, SendAttempt? Attempt, IntegrationResponse Response, DateTimeOffset ReceivedAt);
    string ReceiptDirectory => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(store.Path)!, "receipt-ingress");
    string ReceiptFile(string id) => System.IO.Path.Combine(ReceiptDirectory, id + ".json");
    ReceiptIngress CaptureReceipt(BusinessRequest request, SendAttempt? attempt, IntegrationResponse response)
    {
        // A deterministic custody key keeps repeat delivery idempotent; evidence identity
        // is deliberately NOT the storage identity of a conflicting occurrence.
        ArmReceiptFence(request.Id);
        var id = Wire.Hash(Wire.Encode(new { request.Id, Response = response }));
        var file = ReceiptFile(id);
        if (File.Exists(file)) return Wire.Decode<ReceiptIngress>(File.ReadAllText(file));
        var ingress = new ReceiptIngress(id, request.Id, attempt, response, Now);
        ExtensionStore.AtomicWrite(file, ingress); // flushed before T3; retained on any failure
        return ingress;
    }
    void RecoverReceiptIngress()
    {
        if (!Directory.Exists(ReceiptDirectory)) return;
        foreach (var file in Directory.GetFiles(ReceiptDirectory))
        {
            // An interrupted atomic publish is evidence too. Unknown/corrupt custody is
            // a startup failure, never silently skipped or treated as a successful T3.
            var ingress = Wire.Decode<ReceiptIngress>(File.ReadAllText(file));
            if (System.IO.Path.GetFileName(file) != ingress.Id + ".json") throw new InvalidDataException("Interrupted receipt custody requires inspection; original retained");
            var original = store.Request(ingress.RequestId) ?? throw new InvalidDataException("Receipt custody has no original request");
            contract.ValidateResponse(original, ingress.Response);
            PersistResponse(original, ingress.Attempt, ingress.Response, ingress);
            File.Delete(file);
        }
    }
    void PersistResponse(BusinessRequest original, SendAttempt? attempt, IntegrationResponse response, ReceiptIngress ingress)
    {
        var next = Clone(state);
        var matched = next.Lanes.Values.FirstOrDefault(x => x.Id == original.OwnerId && x.Revision == original.OwnerRevision);
        var semanticConflict = original.Kind == RequestKind.Submit && matched?.Task is { } task && response.Task is { } incoming &&
            (task.Sku != incoming.Sku || task.ItemWave != incoming.ItemWave || task.TaskId != incoming.TaskId || task.Destination != incoming.Destination || task.Unit != incoming.Unit);
        var previous = store.Responses(original.Id);
        // Global evidence-ID collisions must also be recorded, rather than rolling T3
        // back and losing the incoming raw document.
        var evidenceCollision = store.ResponseByEvidence(response.EvidenceId) is { } existing && existing != response;
        var conflict = semanticConflict || evidenceCollision || original.Status == RequestStatus.CancelledBeforeDispatch ||
            previous.Any(x => x.Result != response.Result || x.Quantity != response.Quantity || x.Remaining != response.Remaining || x.CheckToken != response.CheckToken || x.Context != response.Context || x.Task != response.Task);
        var unresolved = conflict || store.HasConflict(original.Id);
        // Retain the first authoritative terminal result and its unique acceptance ledger.
        var status = original.Status is RequestStatus.Accepted or RequestStatus.Rejected or RequestStatus.CancelledBeforeDispatch ? original.Status :
            unresolved ? RequestStatus.Unknown : response.Result == "Accepted" ? RequestStatus.Accepted : RequestStatus.Rejected;
        var request = original with { Status = status };
        ResponseConflict? occurrence = null;
        if (conflict)
        {
            occurrence = new(ingress.Id, original.Id, attempt?.Id, "integration:" + (original.Implementation?.ConfigurationSha256 ?? original.Binding.Id),
                ingress.ReceivedAt, response.EvidenceId, Wire.Hash(Wire.Encode(response)), response);
            SetBlock(next, "ConflictingEvidence", "request", original.Id, original.PayloadHash, "approved-authoritative-reconciliation");
        }
        if (!unresolved && original.Status is not (RequestStatus.Accepted or RequestStatus.Rejected or RequestStatus.CancelledBeforeDispatch)) ApplyOwner(next, request, response, false);
        else if (unresolved && original.Status is not (RequestStatus.Accepted or RequestStatus.Rejected or RequestStatus.CancelledBeforeDispatch)) MarkOwner(next, request, "Contradictory evidence; original identity requires reconciliation");
        next.Revision++; faults.Hit("T3");
        store.Commit(next, conflict ? "ConflictingResponse" : "T3Response", request.OwnerId, "integration", response.Reason,
            request: request, response: conflict ? null : response, conflict: occurrence,
            count: !unresolved && request.Kind == RequestKind.Submit && status == RequestStatus.Accepted,
            attempt: attempt is null || original.Status is RequestStatus.Accepted or RequestStatus.Rejected or RequestStatus.CancelledBeforeDispatch ? null : attempt with { EndedAt = Now, Disposition = unresolved ? "ConflictingEvidence" : status.ToString() });
        Adopt(next);
    }
}
