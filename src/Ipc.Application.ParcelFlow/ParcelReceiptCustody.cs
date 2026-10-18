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
        c.CommandText = StorageVersion >= 5 ? "SELECT query_occurrence FROM parcel_receipt_custody WHERE occurrence=$id" :
            "SELECT json_extract(detail,'$.QueryOccurrence') FROM parcel_audit WHERE event IN ('ReceiptOutcomeKnown','ReceiptOutcomeReconciled') AND json_valid(detail) AND json_extract(detail,'$.OriginalOccurrence')=$id ORDER BY seq DESC LIMIT 1";
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
            if (StorageVersion<4) return false;
            using var archived=db.CreateCommand(); archived.CommandText="SELECT body FROM parcel_archived_receipts WHERE id=$id"; archived.Parameters.AddWithValue("$id",id);
            if (archived.ExecuteScalar() is not byte[] raw) return false; bytes=raw;
        }
        if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))!=hash) throw new InvalidDataException("Outcome Query original bytes changed");
        var reply=Wire.Decode<StageReply>(System.Text.Encoding.UTF8.GetString(bytes));
        var op=StorageVersion<4 ? Load().SingleOrDefault(x=>x.Id==operation) : ReadOperation(operation);
        var fact=stage switch { "image-upload"=>op?.Upload,"image-capture"=>op?.Capture,"wcs-slot"=>op?.Wcs,"plc-submit"=>op?.Plc,_=>null };
        return fact is not null && fact.Request.Id==request && reply.RequestId==request && reply.PayloadHash==fact.Request.PayloadHash &&
            reply.TransportSucceeded && reply.ProtocolAcknowledged==true && reply.BusinessAccepted is not null &&
            fact.State==(reply.BusinessAccepted==true?StageState.Accepted:StageState.Rejected) && fact.Replies.Any(r=>Wire.Encode(r)==Wire.Encode(reply));
    }
    internal bool CustodyOnly(ParcelOperation op, string application)
    {
        if (!ExternalWorkComplete(op,application)) return false;
        var pending=PendingReceipts().Where(x=>x.Operation==op.Id).ToArray();
        return pending.Length>0 && op.FrozenConfiguration.Images?.Strategy==UploadStrategy.Attached &&
            pending.All(x=>x.Stage=="image-upload" && x.Reply is null && HasReceiptOutcomeBasis(x.Id));
    }
    static bool ExternalWorkComplete(ParcelOperation op,string application) => !op.RecoveryRequired &&
        (op.State is ParcelState.Abandoned or ParcelState.ImageRejected or ParcelState.WcsRejected or ParcelState.PlcRejected or ParcelState.PhysicalCompleted || application=="sportmaster" && op.State==ParcelState.SlotResolved) &&
        new[]{op.Wcs,op.Plc,op.Capture,op.Upload}.OfType<StageFact>().All(s=>s.State is StageState.Accepted or StageState.Rejected or StageState.Cancelled);
    bool LegacyCustodyUpgradeEligible(string application)
    {
        using var states = db.CreateCommand();
        states.CommandText = "SELECT count(*) FROM parcel_receipts WHERE state NOT IN ('Awaiting','RawDurable','OutcomeReconciled','Committed','NoReply')";
        if ((long)states.ExecuteScalar()! != 0) return false;
        // Include DB4's historical false disposition before SQL5 restores Awaiting.
        using var c = db.CreateCommand();
        c.CommandText = "SELECT id,operation FROM parcel_receipts WHERE state IN ('Awaiting','RawDurable','OutcomeReconciled')";
        List<(string Id,string Owner)> pending = [];
        using (var r = c.ExecuteReader()) while (r.Read()) pending.Add((r.GetString(0),r.GetString(1)));
        foreach (var op in UpgradeOperations())
        {
            if (!ExternalWorkComplete(op,application)) return false;
            var own = pending.Where(x => x.Owner == op.Id).ToArray();
            if (own.Length == 0)
            {
                if (op.FrozenConfiguration.Images is not null && !op.ImageReleased) return false;
            }
            else if (op.FrozenConfiguration.Images?.Strategy != UploadStrategy.Attached || !HasMainReceiptCustody(op) ||
                own.Any(x => ReceiptById(x.Id) is not { Stage: "image-upload", RawHash: null } ||
                    File.Exists(Path.Combine(rawRoot,x.Id+".json")) || !HasReceiptOutcomeBasis(x.Id))) return false;
        }
        return pending.All(x => StorageVersion >= 4 ? ReadOperation(x.Owner) is not null : Load().Any(op => op.Id == x.Owner));
    }
    public static void UpgradeCustodyOnly(string directory,string application)
    {
        // Caller holds the application owner and has created a full backup. No sources
        // are constructed here. Ordinary startup does not opt into this old-reader transition.
        var file=Path.Combine(directory,application+".db");var version=ReadStorageVersion(file,application);
        using(var original=new ParcelRepository(directory,application,version))
        {
            original.ValidateRawInventory();
            if(!original.LegacyCustodyUpgradeEligible(application))throw new InvalidDataException("Legacy external/custody proof incomplete; original reader marker retained");
        }
        using var upgraded=new ParcelRepository(directory,application,5,allowCustodyUpgrade:true);
        upgraded.ValidateRawInventory();
        foreach(var pending in upgraded.PendingReceipts())
            if (!upgraded.HasReceiptOutcomeBasis(pending.Id)) throw new InvalidDataException("Unproved legacy original; preserve matching old reader and custody");
        upgraded.Commit(null,"LegacyCustodyUpgraded","External work confirmed by original implementation; original missing raw/image/package custody remains pending",paused:true);
    }
}
public sealed partial class ParcelRuntime
{
    bool ConfigurationResponsibilitiesClear() => identityConflicts.Count == 0 && operations.Values.All(op=>IsSettled(op)||repository!.CustodyOnly(op,Id)) &&
        repository!.PendingReceipts().All(x=>repository.HasReceiptOutcomeBasis(x.Id));
    bool RequiresOriginalExecution(ParcelOperation op) => !IsSettled(op) && !repository!.CustodyOnly(op,Id);
}
