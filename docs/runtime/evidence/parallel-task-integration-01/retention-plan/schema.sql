
        CREATE TABLE parcel_meta(id INTEGER PRIMARY KEY CHECK(id=1),revision INTEGER NOT NULL,paused INTEGER NOT NULL);
        INSERT INTO parcel_meta VALUES(1,0,0);
        CREATE TABLE parcel_operations(id TEXT PRIMARY KEY,admission TEXT NOT NULL UNIQUE,observation TEXT NOT NULL UNIQUE,body TEXT NOT NULL);
        CREATE TABLE parcel_audit(seq INTEGER PRIMARY KEY AUTOINCREMENT,at TEXT NOT NULL,event TEXT NOT NULL,owner TEXT NOT NULL,detail TEXT NOT NULL);
        CREATE TABLE parcel_commands(id TEXT PRIMARY KEY,hash TEXT NOT NULL,receipt TEXT NOT NULL);
        CREATE TABLE parcel_receipts(id TEXT PRIMARY KEY,operation TEXT NOT NULL,stage TEXT NOT NULL,request TEXT NOT NULL,source TEXT NOT NULL,state TEXT NOT NULL,raw_hash TEXT);
        CREATE TABLE parcel_measurements(id TEXT PRIMARY KEY,admission TEXT NOT NULL,body TEXT NOT NULL);
        

        CREATE TABLE parcel_semantics(version INTEGER PRIMARY KEY CHECK(version=2),description TEXT NOT NULL);
        INSERT INTO parcel_semantics VALUES(2,'Independent weight/capture/upload guards; schema2 weight validity; local unsent disposition');
        

        CREATE TABLE parcel_topology_semantics(version INTEGER PRIMARY KEY CHECK(version=3),description TEXT NOT NULL);
        INSERT INTO parcel_topology_semantics VALUES(3,'Position-scoped admission and frozen device topology; independent bounded execution');
        

        CREATE TABLE parcel_history_index(seq INTEGER PRIMARY KEY AUTOINCREMENT,id TEXT NOT NULL UNIQUE,admission TEXT NOT NULL UNIQUE,observation TEXT NOT NULL UNIQUE,position TEXT NOT NULL);
        INSERT INTO parcel_history_index(id,admission,observation,position) SELECT id,admission,observation,coalesce(json_extract(body,'$.PositionId'),'main') FROM parcel_operations ORDER BY rowid;
        CREATE TABLE parcel_archives(id TEXT PRIMARY KEY,admission TEXT NOT NULL UNIQUE,observation TEXT NOT NULL UNIQUE,body TEXT NOT NULL,body_hash TEXT NOT NULL,archived_at TEXT NOT NULL);
        CREATE TABLE parcel_reply_identities(request TEXT NOT NULL,id TEXT NOT NULL,hash TEXT NOT NULL,PRIMARY KEY(request,id));
        CREATE TABLE parcel_archived_receipts(id TEXT PRIMARY KEY,body BLOB NOT NULL,raw_hash TEXT NOT NULL);
        CREATE INDEX parcel_receipts_operation ON parcel_receipts(operation);
        CREATE INDEX parcel_history_position ON parcel_history_index(position,seq);
        CREATE TABLE parcel_overload_ack(identity TEXT PRIMARY KEY,command TEXT NOT NULL);
        CREATE TABLE parcel_identity_conflicts(id TEXT PRIMARY KEY,body TEXT NOT NULL,acknowledged INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE parcel_configuration_audit(id TEXT PRIMARY KEY,body TEXT NOT NULL);
        CREATE INDEX parcel_receipts_pending ON parcel_receipts(state,operation,stage);
        

        CREATE TABLE parcel_receipt_custody(occurrence TEXT PRIMARY KEY,query_occurrence TEXT,observed_at TEXT NOT NULL,evidence TEXT NOT NULL);
        CREATE TABLE parcel_archive_versions(operation TEXT NOT NULL,body_hash TEXT NOT NULL,body TEXT NOT NULL,recorded_at TEXT NOT NULL,PRIMARY KEY(operation,body_hash));
        INSERT INTO parcel_receipt_custody SELECT r.id,
            (SELECT json_extract(a.detail,'$.QueryOccurrence') FROM parcel_audit a WHERE a.event IN ('ReceiptOutcomeReconciled','ReceiptOutcomeKnown') AND json_valid(a.detail) AND json_extract(a.detail,'$.OriginalOccurrence')=r.id ORDER BY a.seq DESC LIMIT 1),
            strftime('%Y-%m-%dT%H:%M:%fZ','now'),'Prior outcome resolution does not restore the missing original raw'
            FROM parcel_receipts r WHERE r.state='OutcomeReconciled' OR EXISTS(SELECT 1 FROM parcel_audit a WHERE a.event='ReceiptOutcomeKnown' AND json_valid(a.detail) AND json_extract(a.detail,'$.OriginalOccurrence')=r.id);
        UPDATE parcel_receipts SET state='Awaiting' WHERE state='OutcomeReconciled';
        

        CREATE TABLE parcel_integration_semantics(version INTEGER PRIMARY KEY CHECK(version=6),description TEXT NOT NULL);
        INSERT INTO parcel_integration_semantics VALUES(6,'Frozen external wire intents and original HTTP receipts; UnpackRequired holds the original position');
        

        CREATE TABLE parcel_integration_id_owners(id INTEGER PRIMARY KEY CHECK(id>0),operation TEXT NOT NULL UNIQUE,request TEXT NOT NULL UNIQUE,request_hash TEXT NOT NULL);
        CREATE TABLE parcel_integration_id_ranges(binding_hash TEXT PRIMARY KEY,first_id INTEGER NOT NULL CHECK(first_id>0),last_id INTEGER NOT NULL CHECK(last_id>=first_id));
        CREATE TABLE parcel_integration_id_restores(identity TEXT PRIMARY KEY,ceiling INTEGER NOT NULL CHECK(ceiling>=0));
        

        CREATE TABLE parcel_commercial_semantics(version INTEGER PRIMARY KEY CHECK(version=8),description TEXT NOT NULL);
        INSERT INTO parcel_commercial_semantics VALUES(8,'Explicit initial repair, protected restore handoff and configuration-bound backup schedule; prior readers must refuse');
        

        CREATE TABLE parcel_ingress(id TEXT PRIMARY KEY,position TEXT NOT NULL,received_at TEXT NOT NULL,state TEXT NOT NULL,body TEXT NOT NULL,body_hash TEXT NOT NULL,detail TEXT,session TEXT NOT NULL,started_at TEXT,completed_at TEXT,queue_ms REAL,processing_ms REAL);
        CREATE INDEX parcel_ingress_pending ON parcel_ingress(state,position,received_at);
        CREATE INDEX parcel_ingress_session ON parcel_ingress(session,received_at,position);
        CREATE TABLE parcel_history_projection(id TEXT PRIMARY KEY,barcode TEXT,state TEXT,observed REAL,wcs TEXT,plc TEXT,upload TEXT,capture TEXT,accepted INTEGER,unknown INTEGER,physical INTEGER,attempts INTEGER);
        INSERT INTO parcel_history_projection SELECT id,json_extract(body,'$.ParcelCode'),json_extract(body,'$.State'),julianday(json_extract(body,'$.ObservedAt')),json_extract(body,'$.Wcs.Request.Id'),json_extract(body,'$.Plc.Request.Id'),json_extract(body,'$.Upload.Request.Id'),json_extract(body,'$.Capture.Request.Id'),coalesce(json_extract(body,'$.Wcs.State')='Accepted',0),coalesce(json_extract(body,'$.Wcs.State')='Unknown',0) OR coalesce(json_extract(body,'$.Plc.State')='Unknown',0) OR coalesce(json_extract(body,'$.Upload.State')='Unknown',0) OR coalesce(json_extract(body,'$.Capture.State')='Unknown',0),json_extract(body,'$.State')='PhysicalCompleted',coalesce(json_extract(body,'$.Wcs.Attempts'),0)+coalesce(json_extract(body,'$.Plc.Attempts'),0)+coalesce(json_extract(body,'$.Upload.Attempts'),0)+coalesce(json_extract(body,'$.Capture.Attempts'),0) FROM parcel_operations;
        CREATE TRIGGER parcel_operations_projection_insert AFTER INSERT ON parcel_operations BEGIN
            INSERT INTO parcel_history_projection(id,barcode,state,observed,wcs,plc,upload,capture,accepted,unknown,physical,attempts) VALUES(NEW.id,json_extract(NEW.body,'$.ParcelCode'),json_extract(NEW.body,'$.State'),julianday(json_extract(NEW.body,'$.ObservedAt')),json_extract(NEW.body,'$.Wcs.Request.Id'),json_extract(NEW.body,'$.Plc.Request.Id'),json_extract(NEW.body,'$.Upload.Request.Id'),json_extract(NEW.body,'$.Capture.Request.Id'),coalesce(json_extract(NEW.body,'$.Wcs.State')='Accepted',0),coalesce(json_extract(NEW.body,'$.Wcs.State')='Unknown',0) OR coalesce(json_extract(NEW.body,'$.Plc.State')='Unknown',0) OR coalesce(json_extract(NEW.body,'$.Upload.State')='Unknown',0) OR coalesce(json_extract(NEW.body,'$.Capture.State')='Unknown',0),json_extract(NEW.body,'$.State')='PhysicalCompleted',coalesce(json_extract(NEW.body,'$.Wcs.Attempts'),0)+coalesce(json_extract(NEW.body,'$.Plc.Attempts'),0)+coalesce(json_extract(NEW.body,'$.Upload.Attempts'),0)+coalesce(json_extract(NEW.body,'$.Capture.Attempts'),0)) ON CONFLICT(id) DO UPDATE SET barcode=excluded.barcode,state=excluded.state,observed=excluded.observed,wcs=excluded.wcs,plc=excluded.plc,upload=excluded.upload,capture=excluded.capture,accepted=excluded.accepted,unknown=excluded.unknown,physical=excluded.physical,attempts=excluded.attempts;
        END;
        CREATE TRIGGER parcel_operations_projection_update AFTER UPDATE ON parcel_operations BEGIN
            INSERT INTO parcel_history_projection(id,barcode,state,observed,wcs,plc,upload,capture,accepted,unknown,physical,attempts) VALUES(NEW.id,json_extract(NEW.body,'$.ParcelCode'),json_extract(NEW.body,'$.State'),julianday(json_extract(NEW.body,'$.ObservedAt')),json_extract(NEW.body,'$.Wcs.Request.Id'),json_extract(NEW.body,'$.Plc.Request.Id'),json_extract(NEW.body,'$.Upload.Request.Id'),json_extract(NEW.body,'$.Capture.Request.Id'),coalesce(json_extract(NEW.body,'$.Wcs.State')='Accepted',0),coalesce(json_extract(NEW.body,'$.Wcs.State')='Unknown',0) OR coalesce(json_extract(NEW.body,'$.Plc.State')='Unknown',0) OR coalesce(json_extract(NEW.body,'$.Upload.State')='Unknown',0) OR coalesce(json_extract(NEW.body,'$.Capture.State')='Unknown',0),json_extract(NEW.body,'$.State')='PhysicalCompleted',coalesce(json_extract(NEW.body,'$.Wcs.Attempts'),0)+coalesce(json_extract(NEW.body,'$.Plc.Attempts'),0)+coalesce(json_extract(NEW.body,'$.Upload.Attempts'),0)+coalesce(json_extract(NEW.body,'$.Capture.Attempts'),0)) ON CONFLICT(id) DO UPDATE SET barcode=excluded.barcode,state=excluded.state,observed=excluded.observed,wcs=excluded.wcs,plc=excluded.plc,upload=excluded.upload,capture=excluded.capture,accepted=excluded.accepted,unknown=excluded.unknown,physical=excluded.physical,attempts=excluded.attempts;
        END;
        INSERT INTO parcel_history_projection SELECT id,json_extract(body,'$.ParcelCode'),json_extract(body,'$.State'),julianday(json_extract(body,'$.ObservedAt')),json_extract(body,'$.Wcs.Request.Id'),json_extract(body,'$.Plc.Request.Id'),json_extract(body,'$.Upload.Request.Id'),json_extract(body,'$.Capture.Request.Id'),coalesce(json_extract(body,'$.Wcs.State')='Accepted',0),coalesce(json_extract(body,'$.Wcs.State')='Unknown',0) OR coalesce(json_extract(body,'$.Plc.State')='Unknown',0) OR coalesce(json_extract(body,'$.Upload.State')='Unknown',0) OR coalesce(json_extract(body,'$.Capture.State')='Unknown',0),json_extract(body,'$.State')='PhysicalCompleted',coalesce(json_extract(body,'$.Wcs.Attempts'),0)+coalesce(json_extract(body,'$.Plc.Attempts'),0)+coalesce(json_extract(body,'$.Upload.Attempts'),0)+coalesce(json_extract(body,'$.Capture.Attempts'),0) FROM parcel_archives;
        CREATE TRIGGER parcel_archives_projection_insert AFTER INSERT ON parcel_archives BEGIN
            INSERT INTO parcel_history_projection(id,barcode,state,observed,wcs,plc,upload,capture,accepted,unknown,physical,attempts) VALUES(NEW.id,json_extract(NEW.body,'$.ParcelCode'),json_extract(NEW.body,'$.State'),julianday(json_extract(NEW.body,'$.ObservedAt')),json_extract(NEW.body,'$.Wcs.Request.Id'),json_extract(NEW.body,'$.Plc.Request.Id'),json_extract(NEW.body,'$.Upload.Request.Id'),json_extract(NEW.body,'$.Capture.Request.Id'),coalesce(json_extract(NEW.body,'$.Wcs.State')='Accepted',0),coalesce(json_extract(NEW.body,'$.Wcs.State')='Unknown',0) OR coalesce(json_extract(NEW.body,'$.Plc.State')='Unknown',0) OR coalesce(json_extract(NEW.body,'$.Upload.State')='Unknown',0) OR coalesce(json_extract(NEW.body,'$.Capture.State')='Unknown',0),json_extract(NEW.body,'$.State')='PhysicalCompleted',coalesce(json_extract(NEW.body,'$.Wcs.Attempts'),0)+coalesce(json_extract(NEW.body,'$.Plc.Attempts'),0)+coalesce(json_extract(NEW.body,'$.Upload.Attempts'),0)+coalesce(json_extract(NEW.body,'$.Capture.Attempts'),0)) ON CONFLICT(id) DO UPDATE SET barcode=excluded.barcode,state=excluded.state,observed=excluded.observed,wcs=excluded.wcs,plc=excluded.plc,upload=excluded.upload,capture=excluded.capture,accepted=excluded.accepted,unknown=excluded.unknown,physical=excluded.physical,attempts=excluded.attempts;
        END;
        CREATE TRIGGER parcel_archives_projection_update AFTER UPDATE ON parcel_archives BEGIN
            INSERT INTO parcel_history_projection(id,barcode,state,observed,wcs,plc,upload,capture,accepted,unknown,physical,attempts) VALUES(NEW.id,json_extract(NEW.body,'$.ParcelCode'),json_extract(NEW.body,'$.State'),julianday(json_extract(NEW.body,'$.ObservedAt')),json_extract(NEW.body,'$.Wcs.Request.Id'),json_extract(NEW.body,'$.Plc.Request.Id'),json_extract(NEW.body,'$.Upload.Request.Id'),json_extract(NEW.body,'$.Capture.Request.Id'),coalesce(json_extract(NEW.body,'$.Wcs.State')='Accepted',0),coalesce(json_extract(NEW.body,'$.Wcs.State')='Unknown',0) OR coalesce(json_extract(NEW.body,'$.Plc.State')='Unknown',0) OR coalesce(json_extract(NEW.body,'$.Upload.State')='Unknown',0) OR coalesce(json_extract(NEW.body,'$.Capture.State')='Unknown',0),json_extract(NEW.body,'$.State')='PhysicalCompleted',coalesce(json_extract(NEW.body,'$.Wcs.Attempts'),0)+coalesce(json_extract(NEW.body,'$.Plc.Attempts'),0)+coalesce(json_extract(NEW.body,'$.Upload.Attempts'),0)+coalesce(json_extract(NEW.body,'$.Capture.Attempts'),0)) ON CONFLICT(id) DO UPDATE SET barcode=excluded.barcode,state=excluded.state,observed=excluded.observed,wcs=excluded.wcs,plc=excluded.plc,upload=excluded.upload,capture=excluded.capture,accepted=excluded.accepted,unknown=excluded.unknown,physical=excluded.physical,attempts=excluded.attempts;
        END;
        CREATE INDEX parcel_history_barcode ON parcel_history_projection(barcode,id);
        CREATE INDEX parcel_history_state ON parcel_history_projection(state,id);
        CREATE INDEX parcel_history_observed ON parcel_history_projection(observed,id);
        CREATE INDEX parcel_history_wcs ON parcel_history_projection(wcs,id);
        CREATE INDEX parcel_history_plc ON parcel_history_projection(plc,id);
        CREATE INDEX parcel_history_upload ON parcel_history_projection(upload,id);
        CREATE INDEX parcel_history_capture ON parcel_history_projection(capture,id);
        CREATE INDEX parcel_commands_pending ON parcel_commands(id) WHERE json_extract(receipt,'$.Status')='Pending';
        CREATE INDEX parcel_operations_recovery ON parcel_operations(id) WHERE json_extract(body,'$.RecoveryRequired')=1;
        CREATE INDEX parcel_archives_recovery ON parcel_archives(id) WHERE json_extract(body,'$.RecoveryRequired')=1;
        CREATE TABLE parcel_retired_operations(id TEXT PRIMARY KEY,body_hash TEXT NOT NULL,at TEXT NOT NULL,run TEXT NOT NULL);
        CREATE TABLE parcel_retired_measurements(id TEXT PRIMARY KEY,admission TEXT NOT NULL,body_hash TEXT NOT NULL);
        CREATE TABLE parcel_retired_inputs(id TEXT PRIMARY KEY,body_hash TEXT NOT NULL,state TEXT NOT NULL,at TEXT NOT NULL);
        CREATE TABLE parcel_ingress_audits(input TEXT NOT NULL,seq INTEGER NOT NULL,PRIMARY KEY(input,seq));
        CREATE TABLE parcel_retention_runs(id TEXT PRIMARY KEY,at TEXT NOT NULL,days INTEGER NOT NULL,cutoff TEXT NOT NULL,backup_hash TEXT NOT NULL,summary TEXT NOT NULL);
        CREATE INDEX parcel_audit_owner_at ON parcel_audit(owner,at);
        CREATE INDEX parcel_audit_input_operation ON parcel_audit(json_extract(detail,'$.OperationId')) WHERE event='InputObserved' AND json_valid(detail);
        