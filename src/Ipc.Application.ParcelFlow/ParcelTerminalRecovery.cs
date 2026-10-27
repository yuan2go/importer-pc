using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    // A terminal projection alone is insufficient to release a historical entity check.
    // Validate its frozen identity and original committed replies (including archived raw).
    bool HasTerminalRecoveryBasis(ParcelOperation op, string application)
    {
        if (op.FrozenConfiguration.ApplicationId != application || op.ConfigurationHash != Wire.Hash(Wire.Encode(op.FrozenConfiguration))) return false;
        using (var pending=db.CreateCommand())
        {
            pending.CommandText="SELECT count(*) FROM parcel_receipts WHERE operation=$op AND stage!='image-upload' AND state NOT IN ('Committed','NoReply')";
            pending.Parameters.AddWithValue("$op",op.Id); if((long)pending.ExecuteScalar()! != 0)return false;
        }
        foreach(var stage in new[]{op.Wcs,op.Plc,op.Capture}.OfType<StageFact>())
            if(!SettledStageHasCustody(op,stage))return false;
        return op.State switch
        {
            ParcelState.Abandoned => (op.Plc is null or {State:StageState.Cancelled,Attempts:0}) && HasInspectedAbandonment(op),
            ParcelState.SlotResolved => application=="sportmaster" && op.Wcs is {State:StageState.Accepted} wcs &&
                !string.IsNullOrWhiteSpace(op.Slot) && wcs.Replies.Any(r=>r.BusinessAccepted==true && r.Slot==op.Slot),
            ParcelState.WcsRejected => application!="direct-plc" && op.Wcs is {State:StageState.Rejected} rejected &&
                rejected.Replies.All(r=>r.Destination?.Outcome!=WcsDestinationOutcome.UnpackRequired),
            ParcelState.PlcRejected => application!="sportmaster" && op.Plc is {State:StageState.Rejected},
            ParcelState.PhysicalCompleted => application!="sportmaster" && op.Plc is {State:StageState.Accepted} plc &&
                plc.Replies.Any(r=>r.BusinessAccepted==true && r.PhysicalCompleted==true),
            ParcelState.ImageRejected => op.Capture?.State==StageState.Rejected || op.FrozenConfiguration.Images?.Strategy==UploadStrategy.BeforeMain &&
                op.Upload is {State:StageState.Rejected} upload && SettledStageHasCustody(op,upload),
            _ => false
        };
    }
    bool HasInspectedAbandonment(ParcelOperation op)
    {
        const string prefix="Unsubmitted parcel inspected and removed: ";
        if(op.Reason is not { } reason || !reason.StartsWith(prefix,StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(reason[prefix.Length..]))return false;
        using var c=db.CreateCommand(); c.CommandText="SELECT count(*) FROM parcel_audit WHERE event='Abandoned' AND owner=$op";
        c.Parameters.AddWithValue("$op",op.Id); return (long)c.ExecuteScalar()!>0;
    }
    bool SettledStageHasCustody(ParcelOperation op, StageFact stage)
    {
        var request=stage.Request;
        if(request.ApplicationId!=op.FrozenConfiguration.ApplicationId || request.OperationId!=op.Id || request.AdmissionId!=op.AdmissionId ||
            request.ConfigurationHash!=op.ConfigurationHash || Wire.Hash(request.Payload)!=request.PayloadHash || stage.Attempts is <0 or >1 ||
            !ParcelBindings.All(op.FrozenConfiguration).Any(b=>Wire.Encode(b)==Wire.Encode(request.Binding)))return false;
        if(stage.State==StageState.Cancelled)return stage.Attempts==0 && stage.Replies.Length==0;
        if(stage.State is not (StageState.Accepted or StageState.Rejected))return false;
        var replies=stage.Replies.Where(r=>r.BusinessAccepted is not null).ToArray();
        if(replies.Length==0)return false;
        foreach(var reply in replies)
        {
            if(reply.RequestId!=request.Id || reply.PayloadHash!=request.PayloadHash || !reply.TransportSucceeded || reply.ProtocolAcknowledged!=true ||
                (reply.BusinessAccepted==true)!=(stage.State==StageState.Accepted) || !Guid.TryParseExact(reply.Id,"N",out _) ||
                string.IsNullOrWhiteSpace(reply.Evidence) || !ParcelReceiptChronology.Valid(request,reply,DateTimeOffset.UtcNow))return false;
            if(!HasCommittedReplyBytes(op,stage,reply))return false;
        }
        return true;
    }
    bool HasCommittedReplyBytes(ParcelOperation op, StageFact stage, StageReply reply)
    {
        using var c=db.CreateCommand();
        c.CommandText="SELECT id,raw_hash FROM parcel_receipts WHERE operation=$op AND request=$request AND stage=$stage AND state='Committed'";
        c.Parameters.AddWithValue("$op",op.Id); c.Parameters.AddWithValue("$request",stage.Request.Id); c.Parameters.AddWithValue("$stage",stage.Request.Stage);
        var hash=Wire.Hash(Wire.Encode(reply));
        using var r=c.ExecuteReader();
        while(r.Read())
        {
            if(r.IsDBNull(1) || r.GetString(1)!=hash)continue;
            var file=Path.Combine(rawRoot,r.GetString(0)+".json");
            if(File.Exists(file)?Wire.FileHash(file)==hash:HasArchivedRaw(r.GetString(0),hash))return true;
        }
        return false;
    }
    void RepairTerminalRecoveryFlags(string application)
    {
        var inventoryValidated=false;
        // Keyset iteration keeps history bounded and leaves archived owners archived.
        foreach(var table in new[]{"parcel_operations","parcel_archives"})
        {
            string after="";
            while(true)
            {
                maintenanceCancellation.ThrowIfCancellationRequested();
                string id,body;
                using(var read=db.CreateCommand())
                {
                    read.CommandText=$"SELECT id,body"+(table=="parcel_archives"?",body_hash":"")+$" FROM {table} WHERE id>$after AND json_extract(body,'$.RecoveryRequired')=1 ORDER BY id LIMIT 1";
                    read.Parameters.AddWithValue("$after",after);
                    using var r=read.ExecuteReader(); if(!r.Read())break; id=r.GetString(0); body=r.GetString(1);
                    if(table=="parcel_archives" && Wire.Hash(body)!=r.GetString(2))throw new InvalidDataException("Archived original checksum differs");
                }
                after=id; var original=Wire.Decode<ParcelOperation>(body);
                if(!HasTerminalRecoveryBasis(original,application))continue;
                if(!inventoryValidated){ValidateRawInventory(); inventoryValidated=true;}
                var repaired=Wire.Encode(original with{RecoveryRequired=false});
                using var tx=db.BeginTransaction();
                if(table=="parcel_archives")
                {
                    using var retain=db.CreateCommand(); retain.Transaction=tx;
                    retain.CommandText="INSERT OR IGNORE INTO parcel_archive_versions SELECT id,body_hash,body,archived_at FROM parcel_archives WHERE id=$id";
                    retain.Parameters.AddWithValue("$id",id); retain.ExecuteNonQuery();
                }
                using(var update=db.CreateCommand())
                {
                    update.Transaction=tx;
                    update.CommandText=$"UPDATE {table} SET body=$body"+(table=="parcel_archives"?",body_hash=$hash":"")+" WHERE id=$id AND body=$original";
                    update.Parameters.AddWithValue("$body",repaired); if(table=="parcel_archives")update.Parameters.AddWithValue("$hash",Wire.Hash(repaired));
                    update.Parameters.AddWithValue("$id",id); update.Parameters.AddWithValue("$original",body);
                    if(update.ExecuteNonQuery()!=1)throw new InvalidDataException("Terminal recovery owner changed");
                }
                using(var audit=db.CreateCommand())
                {
                    audit.Transaction=tx;
                    audit.CommandText="INSERT INTO parcel_audit(at,event,owner,detail) VALUES($at,'TerminalRecoveryConverged',$id,$detail); UPDATE parcel_meta SET revision=revision+1 WHERE id=1";
                    audit.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O")); audit.Parameters.AddWithValue("$id",id);
                    audit.Parameters.AddWithValue("$detail",Wire.Encode(new{OriginalBody=body,OriginalSha256=Wire.Hash(body),Basis="Original committed terminal replies or inspected never-sent cancellation; independent receipt/image/package custody unchanged"})); audit.ExecuteNonQuery();
                }
                tx.Commit();
            }
        }
    }
}
