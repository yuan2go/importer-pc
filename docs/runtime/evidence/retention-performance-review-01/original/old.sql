
            SELECT NOT EXISTS(SELECT 1 FROM parcel_receipts WHERE operation=$id AND state NOT IN ('Committed','NoReply'))
            AND NOT EXISTS(SELECT 1 FROM parcel_identity_conflicts WHERE acknowledged=0 AND json_extract(body,'$.OriginalOperation')=$id)
            AND NOT EXISTS(SELECT 1 FROM parcel_ingress i JOIN parcel_history_index h ON h.id=$id
                WHERE i.state IN ('Pending','Interrupted') AND coalesce(json_extract(i.body,'$.Observation.AdmissionId'),json_extract(i.body,'$.Measurement.AdmissionId'))=h.admission)
            AND NOT EXISTS(SELECT 1 FROM parcel_audit WHERE owner=$id AND julianday(at)>=julianday($cutoff))
            AND NOT EXISTS(SELECT 1 FROM parcel_audit a JOIN parcel_commands c ON c.id=json_extract(a.detail,'$.Id')
                WHERE a.event='CommandAdmitted' AND json_valid(a.detail) AND json_extract(a.detail,'$.Owner')=$id AND json_extract(c.receipt,'$.Status')='Pending')
            