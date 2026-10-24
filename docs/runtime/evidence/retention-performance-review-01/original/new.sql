
            SELECT NOT EXISTS(SELECT 1 FROM parcel_receipts WHERE operation=$id AND state NOT IN ('Committed','NoReply'))
            AND NOT EXISTS(SELECT 1 FROM parcel_identity_conflicts WHERE acknowledged=0 AND json_extract(body,'$.OriginalOperation')=$id)
            AND NOT EXISTS(SELECT 1 FROM parcel_ingress i JOIN parcel_history_index h ON h.id=$id
                WHERE i.state IN ('Pending','Interrupted') AND coalesce(json_extract(i.body,'$.Observation.AdmissionId'),json_extract(i.body,'$.Measurement.AdmissionId'))=h.admission)
            AND NOT EXISTS(SELECT 1 FROM parcel_audit WHERE owner=$id AND julianday(at)>=julianday($cutoff))
            AND NOT EXISTS(SELECT 1 FROM temp.parcel_retention_pending_owners WHERE owner=$id)
            