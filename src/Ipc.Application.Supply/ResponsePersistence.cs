using Ipc.Platform;

namespace Ipc.Supply;

public sealed partial class StationApplication
{
    string ReceiptFence => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(store.Path)!, "receipt-owner-fence.json");
    // Reserve before transport/query. A failed reservation suppresses the external call.
    // Each invocation owns one row; another invocation can never discharge it.
    public Task<string> BeginReceiptAsync(BusinessRequest request, SendAttempt? attempt, string origin) => owner.Invoke(async () =>
    {
        await gate.WaitAsync();
        try { return BeginReceipt(request, attempt, origin).Id; }
        catch (Exception ex) when (IsStorage(ex)) { Latch(SupplyReasonCodes.StorageFailure, "Receipt reservation failed; external call forbidden: " + ex.Message); throw; }
        finally { gate.Release(); }
    }, priority: false, waitForCapacity: true);
    ReceiptResponsibility BeginReceipt(BusinessRequest request, SendAttempt? attempt, string origin)
    {
        var original = store.Request(request.Id) ?? throw new InvalidDataException("Receipt requires original request");
        if (original with { Status = request.Status } != request || attempt is not null && attempt.RequestId != request.Id)
            throw new InvalidDataException("Receipt reservation original identity mismatch");
        var occurrence = new ReceiptResponsibility(Wire.Id(), original.Id, attempt?.Id, origin, Now, ReceiptResponsibilityRules.AwaitingResponse);
        store.Commit(state, "ReceiptReserved", original.Id, "receiver", occurrence.Id, occurrence: occurrence);
        return occurrence;
    }
    // Only the caller which observed no response may close its own reservation. Unknown
    // business outcomes remain in the request/outbox; this is not a request reconciliation.
    public Task NoReceiptAsync(string id) => owner.Invoke(async () =>
    {
        await gate.WaitAsync();
        try
        {
            var occurrence = store.ReceiptOccurrence(id) ?? throw new InvalidDataException("Receipt reservation missing");
            if (occurrence.Status != ReceiptResponsibilityRules.AwaitingResponse || File.Exists(ReceiptFile(id))) throw new InvalidDataException("Received custody cannot be cancelled");
            store.Commit(state, "NoResponseReceived", occurrence.RequestId, "receiver", id, occurrence: occurrence with { Status = ReceiptResponsibilityRules.NoResponse });
        }
        catch (Exception ex) when (IsStorage(ex)) { Latch(SupplyReasonCodes.StorageFailure, ex.Message); throw; }
        finally { gate.Release(); }
    }, priority: false, waitForCapacity: true);
    void RecoverReceiptFence()
    {
        if (File.Exists(ReceiptFence))
            throw new InvalidDataException("Unsupported receipt fence; original file and request responsibility retained");
    }
    sealed record ReceiptIngress(string Id, string RequestId, SendAttempt? Attempt, IntegrationResponse Response, DateTimeOffset ReceivedAt);
    string ReceiptDirectory => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(store.Path)!, "receipt-ingress");
    string ReceiptFile(string id) => System.IO.Path.Combine(ReceiptDirectory, id + ".json");
    void ValidateReceiptInputs()
    {
        RecoverReceiptFence();
        if (Directory.Exists(ReceiptDirectory))
            foreach (var file in Directory.GetFiles(ReceiptDirectory)) _ = ReadReceiptIngress(file);
    }
    (ReceiptIngress Ingress, BusinessRequest Original, ReceiptResponsibility Occurrence) ReadReceiptIngress(string file)
    {
        var ingress = Wire.Decode<ReceiptIngress>(File.ReadAllText(file));
        if (System.IO.Path.GetFileName(file) != ingress.Id + ".json") throw new InvalidDataException("Interrupted receipt custody requires inspection; original retained");
        var original = store.Request(ingress.RequestId) ?? throw new InvalidDataException("Receipt custody has no original request");
        contract.ValidateResponse(original, ingress.Response);
        var occurrence = store.ReceiptOccurrence(ingress.Id) ?? throw new InvalidDataException("Receipt occurrence reservation missing; raw original retained without replay");
        if (occurrence.RequestId != original.Id || occurrence.AttemptId != ingress.Attempt?.Id ||
            occurrence.Status is not (ReceiptResponsibilityRules.AwaitingResponse or ReceiptResponsibilityRules.RawUnavailable or ReceiptResponsibilityRules.Committed) ||
            occurrence.Status == ReceiptResponsibilityRules.Committed && occurrence.Response != ingress.Response)
            throw new InvalidDataException("Receipt reservation or committed raw identity differs; originals retained");
        if (ingress.Attempt is { } attempt)
        {
            var stored = store.Attempts(original.Id).SingleOrDefault(x => x.Id == attempt.Id);
            if (stored is null || stored.RequestId != attempt.RequestId || stored.Ordinal != attempt.Ordinal || stored.StartedAt != attempt.StartedAt)
                throw new InvalidDataException("Receipt original send attempt identity differs; originals retained");
        }
        return (ingress, original, occurrence);
    }
    ReceiptIngress CaptureReceipt(BusinessRequest request, SendAttempt? attempt, IntegrationResponse response, ReceiptResponsibility occurrence)
    {
        var ingress = new ReceiptIngress(occurrence.Id, request.Id, attempt, response, Now);
        faults.Hit("ReceiptCreate");
        ExtensionStore.AtomicWrite(ReceiptFile(occurrence.Id), ingress);
        faults.Hit("ReceiptFlushed");
        return ingress;
    }
    void RecoverReceiptIngress()
    {
        if (Directory.Exists(ReceiptDirectory))
            foreach (var file in Directory.GetFiles(ReceiptDirectory))
            {
                var (ingress, original, occurrence) = ReadReceiptIngress(file);
                if (occurrence.Status != ReceiptResponsibilityRules.Committed) PersistResponse(original, ingress.Attempt, ingress.Response, ingress);
                else if (occurrence.Response != ingress.Response) throw new InvalidDataException("Committed receipt and raw custody differ; originals retained");
                File.Delete(file);
            }
        foreach (var occurrence in store.IncompleteReceipts())
        {
            // No raw was acquired, or the process died between reservation and Flush.
            // Later queries cannot certify this missing original or clear this row.
            var next = Clone(state);
            SetReceiptBlock(next, occurrence);
            store.Commit(next, "ReceiptRawUnavailable", occurrence.RequestId, "recovery", occurrence.Id + ": raw not acquired; original content unrecoverable",
                occurrence: occurrence with { Status = ReceiptResponsibilityRules.RawUnavailable });
            Adopt(next);
        }
    }
    static void SetReceiptBlock(StationState next, ReceiptResponsibility occurrence)
    {
        if (!next.Blocks.Any(x => x.Code == SupplyReasonCodes.ReceiptPersistenceUncertain && x.SourceEpoch == occurrence.Id))
            next.Blocks.Add(new(SupplyReasonCodes.ReceiptPersistenceUncertain, "request", occurrence.RequestId, occurrence.Id, 1, "approved-authoritative-reconciliation"));
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
        var custody = store.ReceiptOccurrence(ingress.Id) ?? throw new InvalidDataException("Receipt reservation missing before T3");
        if (custody.RequestId != original.Id || custody.AttemptId != attempt?.Id || ReceiptResponsibilityRules.IsFinal(custody.Status))
            throw new InvalidDataException("Receipt occurrence does not own this T3");
        next.Blocks.RemoveAll(x => x.Code == SupplyReasonCodes.ReceiptPersistenceUncertain && x.SourceEpoch == custody.Id);
        ResponseConflict? occurrence = null;
        if (conflict)
        {
            occurrence = new(Wire.Hash(Wire.Encode(new { original.Id, Response = response })), original.Id, attempt?.Id, "integration:" + (original.Implementation?.ConfigurationSha256 ?? original.Binding.Id),
                ingress.ReceivedAt, response.EvidenceId, Wire.Hash(Wire.Encode(response)), response);
            SetBlock(next, SupplyReasonCodes.ConflictingEvidence, "request", original.Id, original.PayloadHash, "approved-authoritative-reconciliation");
        }
        if (!unresolved && original.Status is not (RequestStatus.Accepted or RequestStatus.Rejected or RequestStatus.CancelledBeforeDispatch)) ApplyOwner(next, request, response, false);
        else if (unresolved && original.Status is not (RequestStatus.Accepted or RequestStatus.Rejected or RequestStatus.CancelledBeforeDispatch)) MarkOwner(next, request, "Contradictory evidence; original identity requires reconciliation");
        next.Revision++; faults.Hit("T3");
        store.Commit(next, conflict ? "ConflictingResponse" : "T3Response", request.OwnerId, "integration", response.Reason,
            request: request, response: conflict ? null : response, conflict: occurrence,
            count: !unresolved && request.Kind == RequestKind.Submit && status == RequestStatus.Accepted,
            occurrence: custody with { Status = ReceiptResponsibilityRules.Committed, ContentSha256 = Wire.Hash(Wire.Encode(response)), Response = response },
            attempt: attempt is null || original.Status is RequestStatus.Accepted or RequestStatus.Rejected or RequestStatus.CancelledBeforeDispatch ? null : attempt with { EndedAt = Now, Disposition = unresolved ? SupplyReasonCodes.ConflictingEvidence : status.ToString() });
        Adopt(next);
    }
}
