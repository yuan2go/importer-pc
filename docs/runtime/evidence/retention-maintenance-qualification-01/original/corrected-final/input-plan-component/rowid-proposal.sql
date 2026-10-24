SELECT rowid,id FROM parcel_ingress i NOT INDEXED WHERE rowid>$cursor AND state IN ('Processed','Inspected') AND julianday(completed_at)<julianday($cutoff)
                    AND NOT EXISTS(SELECT 1 FROM parcel_history_index h WHERE h.admission=coalesce(json_extract(i.body,'$.Observation.AdmissionId'),json_extract(i.body,'$.Measurement.AdmissionId'))
                        AND NOT EXISTS(SELECT 1 FROM parcel_retired_operations t WHERE t.id=h.id)) ORDER BY rowid LIMIT 256
