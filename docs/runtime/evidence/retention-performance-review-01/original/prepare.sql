
            CREATE TEMP TABLE IF NOT EXISTS parcel_retention_pending_owners(owner TEXT PRIMARY KEY) WITHOUT ROWID;
            DELETE FROM temp.parcel_retention_pending_owners;
            INSERT OR IGNORE INTO temp.parcel_retention_pending_owners
            SELECT json_extract(a.detail,'$.Owner') FROM parcel_audit a
            JOIN parcel_commands c ON c.id=json_extract(a.detail,'$.Id')
            WHERE a.event='CommandAdmitted' AND json_valid(a.detail)
                AND json_type(a.detail,'$.Owner')='text' AND json_extract(c.receipt,'$.Status')='Pending';
            