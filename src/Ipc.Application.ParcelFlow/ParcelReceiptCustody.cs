using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

// The occurrence is the custody authority. This index links separate business outcome
// evidence; it never changes Awaiting to Committed or supplies another occurrence's raw.
public sealed partial class ParcelRepository
{
    string? CustodyQuery(string occurrence)
    {
        using var c = db.CreateCommand();
        c.CommandText = "SELECT query_occurrence FROM parcel_receipt_custody WHERE occurrence=$id";
        c.Parameters.AddWithValue("$id",occurrence); return c.ExecuteScalar() as string;
    }
    public bool HasReceiptOutcomeBasis(string occurrence)
    {
        var query = CustodyQuery(occurrence); if (query is null) return false;
        using var c = db.CreateCommand();
        c.CommandText = "SELECT q.id,q.operation,q.stage,q.request,q.raw_hash FROM parcel_receipts r JOIN parcel_receipts q ON q.id=$query AND q.operation=r.operation AND q.stage=r.stage AND q.request=r.request WHERE r.id=$id AND q.source='Query' AND q.state='Committed' AND q.raw_hash IS NOT NULL";
        c.Parameters.AddWithValue("$id",occurrence); c.Parameters.AddWithValue("$query",query);
        string id, operation, stage, request, hash;
        using (var r=c.ExecuteReader()) { if (!r.Read()) return false; id=r.GetString(0); operation=r.GetString(1); stage=r.GetString(2); request=r.GetString(3); hash=r.GetString(4); }
        byte[] bytes; var file=Path.Combine(rawRoot,id+".json");
        if (File.Exists(file)) bytes=File.ReadAllBytes(file);
        else
        {
            using var archived=db.CreateCommand(); archived.CommandText="SELECT body FROM parcel_archived_receipts WHERE id=$id"; archived.Parameters.AddWithValue("$id",id);
            if (archived.ExecuteScalar() is not byte[] raw) return false; bytes=raw;
        }
        if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))!=hash) throw new InvalidDataException("Outcome Query original bytes changed");
        var reply=Wire.Decode<StageReply>(System.Text.Encoding.UTF8.GetString(bytes));
        var op=ReadOperation(operation);
        var fact=stage switch { "image-upload"=>op?.Upload,"image-capture"=>op?.Capture,"wcs-slot"=>op?.Wcs,"plc-submit"=>op?.Plc,_=>null };
        if (op is null || fact is null) return false;
        var frozen=fact.Request;
        // The committed Query raw is the evidence. Replies is a bounded UI/recovery
        // projection; eviction must not turn a proven outcome back into unknown.
        if (frozen.Id!=request || frozen.Stage!=stage || frozen.OperationId!=op.Id || frozen.AdmissionId!=op.AdmissionId ||
            frozen.ApplicationId!=op.FrozenConfiguration.ApplicationId || frozen.ConfigurationHash!=op.ConfigurationHash || op.ConfigurationHash!=Wire.Hash(Wire.Encode(op.FrozenConfiguration)) ||
            Wire.Hash(frozen.Payload)!=frozen.PayloadHash || reply.RequestId!=request || reply.PayloadHash!=frozen.PayloadHash ||
            !Guid.TryParseExact(reply.Id,"N",out _) || string.IsNullOrWhiteSpace(reply.Evidence) || !ParcelReceiptChronology.Valid(frozen,reply,DateTimeOffset.UtcNow) ||
            OriginalReplyHash(request,reply.Id)!=Wire.Hash(Wire.Encode(reply)) || !reply.TransportSucceeded || reply.ProtocolAcknowledged!=true || reply.BusinessAccepted is null ||
            fact.State!=(reply.BusinessAccepted==true?StageState.Accepted:StageState.Rejected)) return false;
        // Retained terminal facts remain a consistency guard, never an existence test.
        if (fact.Replies.Any(r=>r.BusinessAccepted is not null && (r.BusinessAccepted!=reply.BusinessAccepted || r.Slot!=reply.Slot || Wire.Encode(r.Image)!=Wire.Encode(reply.Image))) ||
            stage!="plc-submit" && reply.PhysicalCompleted==true) return false;
        if (stage=="wcs-slot" && reply.BusinessAccepted==true && (string.IsNullOrWhiteSpace(reply.Slot) || reply.Slot!=op.Slot)) return false;
        if (stage is "image-capture" or "image-upload" && reply.BusinessAccepted==true && (op.Image is null || reply.Image is null ||
            reply.Image with{UploadedUri=null}!=op.Image with{UploadedUri=null} || stage=="image-upload" &&
            (reply.Image.UploadedUri!=op.Image.UploadedUri || !Uri.TryCreate(reply.Image.UploadedUri,UriKind.Absolute,out var uri) || uri.Scheme is not ("reference" or "http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)))) return false;
        return true;
    }
    internal bool CustodyOnly(ParcelOperation op, string application)
    {
        if (!ExternalWorkComplete(op,application)) return false;
        var pending=PendingReceipts().Where(x=>x.Operation==op.Id).ToArray();
        return pending.Length>0 && op.FrozenConfiguration.Images?.Strategy==UploadStrategy.Attached &&
            pending.All(x=>x.Stage=="image-upload" && x.Reply is null && HasReceiptOutcomeBasis(x.Id));
    }
    bool ExternalWorkComplete(ParcelOperation op,string application) => (!op.RecoveryRequired || HasTerminalRecoveryBasis(op,application)) &&
        (op.State is ParcelState.Abandoned or ParcelState.ImageRejected or ParcelState.WcsRejected or ParcelState.PlcRejected or ParcelState.PhysicalCompleted || application=="sportmaster" && op.State==ParcelState.SlotResolved) &&
        new[]{op.Wcs,op.Plc,op.Capture,op.Upload}.OfType<StageFact>().All(s=>s.State is StageState.Accepted or StageState.Rejected or StageState.Cancelled);
}
public sealed partial class ParcelRuntime
{
    bool ConfigurationResponsibilitiesClear()
    {
        return !CameraBusinessFenced && interruptedPositions.IsEmpty && ingressLanes.Values.All(l => l.Waiting.IsEmpty) && identityConflicts.Count == 0 &&
            operations.Values.All(op=>IsSettled(op)||repository!.CustodyOnly(op,Id)) && repository!.PendingReceipts().All(x=>repository.HasReceiptOutcomeBasis(x.Id));
    }
    bool RequiresOriginalExecution(ParcelOperation op) => !IsSettled(op) && !repository!.CustodyOnly(op,Id);
}
