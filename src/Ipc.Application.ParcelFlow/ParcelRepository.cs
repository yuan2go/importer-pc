using Ipc.Application.Contracts;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

// Application owns SQL, aggregate semantics and the T1/T2/T3 transaction boundaries.
public sealed partial class ParcelRepository : IDisposable
{
    internal Action<string>? Boundary { get; set; }
    readonly SqliteConnection db;
    readonly string rawRoot;
    readonly CancellationToken maintenanceCancellation;
    public int StorageVersion { get; private set; }
    static readonly DatabaseMigration[] migrations = [new(1, """
        CREATE TABLE parcel_meta(id INTEGER PRIMARY KEY CHECK(id=1),revision INTEGER NOT NULL,paused INTEGER NOT NULL);
        INSERT INTO parcel_meta VALUES(1,0,0);
        CREATE TABLE parcel_operations(id TEXT PRIMARY KEY,admission TEXT NOT NULL UNIQUE,observation TEXT NOT NULL UNIQUE,body TEXT NOT NULL);
        CREATE TABLE parcel_audit(seq INTEGER PRIMARY KEY AUTOINCREMENT,at TEXT NOT NULL,event TEXT NOT NULL,owner TEXT NOT NULL,detail TEXT NOT NULL);
        CREATE TABLE parcel_commands(id TEXT PRIMARY KEY,hash TEXT NOT NULL,receipt TEXT NOT NULL);
        CREATE TABLE parcel_receipts(id TEXT PRIMARY KEY,operation TEXT NOT NULL,stage TEXT NOT NULL,request TEXT NOT NULL,source TEXT NOT NULL,state TEXT NOT NULL,raw_hash TEXT);
        CREATE TABLE parcel_measurements(id TEXT PRIMARY KEY,admission TEXT NOT NULL,body TEXT NOT NULL);
        """), new(2, """
        CREATE TABLE parcel_semantics(version INTEGER PRIMARY KEY CHECK(version=2),description TEXT NOT NULL);
        INSERT INTO parcel_semantics VALUES(2,'Independent weight/capture/upload guards; schema2 weight validity; local unsent disposition');
        """), new(3, """
        CREATE TABLE parcel_topology_semantics(version INTEGER PRIMARY KEY CHECK(version=3),description TEXT NOT NULL);
        INSERT INTO parcel_topology_semantics VALUES(3,'Position-scoped admission and frozen device topology; independent bounded execution');
        """), new(4, """
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
        """), new(5, """
        CREATE TABLE parcel_receipt_custody(occurrence TEXT PRIMARY KEY,query_occurrence TEXT,observed_at TEXT NOT NULL,evidence TEXT NOT NULL);
        CREATE TABLE parcel_archive_versions(operation TEXT NOT NULL,body_hash TEXT NOT NULL,body TEXT NOT NULL,recorded_at TEXT NOT NULL,PRIMARY KEY(operation,body_hash));
        INSERT INTO parcel_receipt_custody SELECT r.id,
            (SELECT json_extract(a.detail,'$.QueryOccurrence') FROM parcel_audit a WHERE a.event IN ('ReceiptOutcomeReconciled','ReceiptOutcomeKnown') AND json_valid(a.detail) AND json_extract(a.detail,'$.OriginalOccurrence')=r.id ORDER BY a.seq DESC LIMIT 1),
            strftime('%Y-%m-%dT%H:%M:%fZ','now'),'Prior outcome resolution does not restore the missing original raw'
            FROM parcel_receipts r WHERE r.state='OutcomeReconciled' OR EXISTS(SELECT 1 FROM parcel_audit a WHERE a.event='ReceiptOutcomeKnown' AND json_valid(a.detail) AND json_extract(a.detail,'$.OriginalOccurrence')=r.id);
        UPDATE parcel_receipts SET state='Awaiting' WHERE state='OutcomeReconciled';
        """), new(6, """
        CREATE TABLE parcel_integration_semantics(version INTEGER PRIMARY KEY CHECK(version=6),description TEXT NOT NULL);
        INSERT INTO parcel_integration_semantics VALUES(6,'Frozen external wire intents and original HTTP receipts; UnpackRequired holds the original position');
        """), new(7, """
        CREATE TABLE parcel_integration_id_owners(id INTEGER PRIMARY KEY CHECK(id>0),operation TEXT NOT NULL UNIQUE,request TEXT NOT NULL UNIQUE,request_hash TEXT NOT NULL);
        CREATE TABLE parcel_integration_id_ranges(binding_hash TEXT PRIMARY KEY,first_id INTEGER NOT NULL CHECK(first_id>0),last_id INTEGER NOT NULL CHECK(last_id>=first_id));
        CREATE TABLE parcel_integration_id_restores(identity TEXT PRIMARY KEY,ceiling INTEGER NOT NULL CHECK(ceiling>=0));
        """), new(8, """
        CREATE TABLE parcel_commercial_semantics(version INTEGER PRIMARY KEY CHECK(version=8),description TEXT NOT NULL);
        INSERT INTO parcel_commercial_semantics VALUES(8,'Explicit initial repair, protected restore handoff and configuration-bound backup schedule; prior readers must refuse');
        """), new(9, """
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
        """)];
    public long Revision { get; private set; }
    public bool Paused { get; private set; }
    public ParcelRepository(string directory, string application, int storageVersion = 9, bool allowCustodyUpgrade = false)
        : this(directory, application, storageVersion, allowCustodyUpgrade, CancellationToken.None) { }
    internal ParcelRepository(string directory, string application, int storageVersion, bool allowCustodyUpgrade, CancellationToken maintenanceCancellation)
    {
        this.maintenanceCancellation = maintenanceCancellation;
        maintenanceCancellation.ThrowIfCancellationRequested();
        if (storageVersion is not (2 or 3 or 4 or 5 or 6 or 7 or 8 or 9)) throw new ArgumentOutOfRangeException(nameof(storageVersion));
        Directory.CreateDirectory(directory); rawRoot = Path.Combine(directory, "receipts"); Directory.CreateDirectory(rawRoot);
        var database = Path.Combine(directory, application + ".db");
        if (File.Exists(database) && new FileInfo(database).Length != 0)
        {
            // A refused upgrade must not checkpoint a live historical WAL, change
            // journal settings, or strand configuration behind a new reader marker.
            db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            try
            {
                db.Open();
                if (maintenanceCancellation.CanBeCanceled)
                    SQLitePCL.raw.sqlite3_progress_handler(db.Handle, 1000, _ => maintenanceCancellation.IsCancellationRequested ? 1 : 0, null);
                StorageVersion = ReadStorageVersion(database, application);
                if (StorageVersion > storageVersion) throw new InvalidDataException("Future reader required; retain original database and use its matching release");
                RequireLegacySettlement(application, storageVersion, allowCustodyUpgrade);
                if (StorageVersion >= 6 && StorageVersion < storageVersion) ValidateIntegrationRequestHistory();
            }
            finally { db.Dispose(); }
        }
        db = Database.Open(database, true, maintenanceCancellation);
        try
        {
            using (var version = db.CreateCommand())
            {
                version.CommandText="SELECT count(*) FROM sqlite_master WHERE name='platform_migrations'";
                if ((long)version.ExecuteScalar()! != 0) { version.CommandText="SELECT coalesce(max(version),0) FROM platform_migrations"; StorageVersion=Convert.ToInt32(version.ExecuteScalar()); }
            }
            RequireLegacySettlement(application, storageVersion, allowCustodyUpgrade);
            if (StorageVersion >= 4 && StorageVersion < storageVersion) ValidateRawInventory();
            Database.Migrate(db, application, migrations.Take(storageVersion).ToArray());
            StorageVersion=storageVersion;
            using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT revision,paused FROM parcel_meta WHERE id=1";
            using (var r = cmd.ExecuteReader())
            { if (!r.Read()) throw new InvalidDataException("Missing parcel metadata"); Revision = r.GetInt64(0); Paused = r.GetBoolean(1); }
            RepairTerminalRecoveryFlags(application);
            InitializeIntegrationRequestHistory();
            maintenanceCancellation.ThrowIfCancellationRequested();
        }
        catch { db.Dispose(); throw; }
    }
    void RequireLegacySettlement(string application, int storageVersion, bool allowCustodyUpgrade)
    {
        using var exists = db.CreateCommand();
        exists.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='platform_migrations'";
        if ((long)exists.ExecuteScalar()! == 0) return;
        using var version = db.CreateCommand(); version.CommandText = "SELECT max(version) FROM platform_migrations WHERE application=$app"; version.Parameters.AddWithValue("$app", application);
        if (version.ExecuteScalar() is not long legacy || legacy >= storageVersion) return;
        if (allowCustodyUpgrade && LegacyCustodyUpgradeEligible(application)) return;
        // Do not strand old unresolved originals behind a future-reader marker. They need
        // their matching old host/implementation; refuse BEFORE adding any future-reader migration.
        using var pending = db.CreateCommand();
        pending.CommandText = "SELECT count(*) FROM parcel_receipts WHERE state NOT IN ('Committed','NoReply')";
        var unsettled = (long)pending.ExecuteScalar()! > 0 || UpgradeOperations().Any(op =>
            !ExternalWorkComplete(op, application) ||
            op.FrozenConfiguration.Images is not null && !op.ImageReleased && !(op.Image is null && op.Capture?.State is StageState.Rejected or StageState.Cancelled && op.Upload is null));
        if (unsettled) throw new InvalidDataException("LegacyUnsettled: preserve original schema; reconcile originals with the matching old release and create a consistent backup before upgrade");
    }
    // Upgrade reads history as well as the resident working set. Stream the originals:
    // an archived owner or an old falsely-cleared receipt cannot hide responsibility.
    IEnumerable<ParcelOperation> UpgradeOperations()
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = StorageVersion >= 4 ? "SELECT body,NULL,id,admission,observation FROM parcel_operations UNION ALL SELECT body,body_hash,id,admission,observation FROM parcel_archives" : "SELECT body,NULL,id,admission,observation FROM parcel_operations";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            maintenanceCancellation.ThrowIfCancellationRequested();
            var body = r.GetString(0);
            if (!r.IsDBNull(1) && Wire.Hash(body) != r.GetString(1)) throw new InvalidDataException("Archived original checksum differs");
            var operation = Wire.Decode<ParcelOperation>(body);
            if (operation.Id != r.GetString(2) || operation.AdmissionId != r.GetString(3) || operation.ObservationId != r.GetString(4))
                throw new InvalidDataException("Original operation identity differs from its stored owner; preserve original database");
            yield return operation;
        }
    }
    public ParcelOperation[] Load()
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = StorageVersion >= 5 ? "SELECT body,NULL FROM parcel_operations UNION ALL SELECT body,body_hash FROM parcel_archives a WHERE EXISTS(SELECT 1 FROM parcel_receipts r WHERE r.operation=a.id AND r.state IN ('Awaiting','RawDurable'))" : "SELECT body,NULL FROM parcel_operations ORDER BY rowid";
        using var r = cmd.ExecuteReader(); List<ParcelOperation> result = [];
        while (r.Read())
        {
            var body=r.GetString(0); if(!r.IsDBNull(1) && Wire.Hash(body)!=r.GetString(1)) throw new InvalidDataException("Archived original checksum differs");
            result.Add(Wire.Decode<ParcelOperation>(body));
        }
        return result.ToArray();
    }
    public void Commit(ParcelOperation? operation, string eventName, string detail, bool? paused = null, string? occurrence = null, BusinessCommand? command = null, WeightMeasurement? measurement = null, string? acknowledgedOverload = null, IdentityConflict? identityConflict = null, string? clearConflict = null, ConfigurationChangeAudit? configurationAudit = null, string? reconciledOccurrence = null, string? outcomeQuery = null, string? priorConflictBody = null, ParcelInputSnapshot? input = null, BusinessReceipt? commandResult = null, string? interruptedCommand = null, string? inspectedIngress = null, string? ingressIdentity = null)
    {
        if (StorageVersion >= 9 && (operation is not null && RetiredIdentity(operation.AdmissionId, operation.ObservationId) is not null || measurement is not null && RetiredMeasurement(measurement) is not null))
            throw new InvalidDataException("Expired identity cannot be recreated; permanent retention evidence remains authoritative");
        using var tx = db.BeginTransaction();
        if (inspectedIngress is not null)
        {
            using var inspected = db.CreateCommand(); inspected.Transaction = tx;
            inspected.CommandText = "UPDATE parcel_ingress SET state='Inspected',detail=$detail,completed_at=$at WHERE id=$id AND state='Interrupted'";
            inspected.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            inspected.Parameters.AddWithValue("$id", inspectedIngress); inspected.Parameters.AddWithValue("$detail", detail);
            if (inspected.ExecuteNonQuery() != 1) throw new InvalidOperationException("Interrupted ingress changed; inspect again");
        }
        if (interruptedCommand is not null)
        {
            using var close = db.CreateCommand(); close.Transaction = tx;
            close.CommandText = "UPDATE parcel_commands SET receipt=$receipt WHERE id=$id AND json_extract(receipt,'$.Status')='Pending' AND EXISTS(SELECT 1 FROM parcel_audit WHERE event='CommandAdmitted' AND json_extract(detail,'$.Id')=$id)";
            close.Parameters.AddWithValue("$id", interruptedCommand);
            close.Parameters.AddWithValue("$receipt", Wire.Encode(new BusinessReceipt("Interrupted", "Inspected under command " + command!.Id + "; original business/configuration/receipt facts retained; no automatic execution", Revision + 1)));
            if (close.ExecuteNonQuery() != 1) throw new InvalidOperationException("Original pending command changed or admission evidence is missing");
        }
        if (measurement is not null)
        {
            using var sample = db.CreateCommand(); sample.Transaction = tx;
            sample.CommandText = "INSERT INTO parcel_measurements VALUES($id,$admission,$body)";
            sample.Parameters.AddWithValue("$id", measurement.Id); sample.Parameters.AddWithValue("$admission", measurement.AdmissionId);
            sample.Parameters.AddWithValue("$body", Wire.Encode(measurement)); sample.ExecuteNonQuery();
            Boundary?.Invoke("AfterMeasurementInsert");
        }
        if (configurationAudit is not null)
        {
            using var change = db.CreateCommand(); change.Transaction = tx;
            change.CommandText = "INSERT INTO parcel_configuration_audit VALUES($id,$body) ON CONFLICT(id) DO UPDATE SET body=excluded.body";
            change.Parameters.AddWithValue("$id", configurationAudit.Id); change.Parameters.AddWithValue("$body", Wire.Encode(configurationAudit)); change.ExecuteNonQuery();
        }
        if (identityConflict is not null)
        {
            using var conflict = db.CreateCommand(); conflict.Transaction = tx; conflict.CommandText = priorConflictBody is null ? "INSERT INTO parcel_identity_conflicts(id,body) VALUES($id,$body)" :
                "UPDATE parcel_identity_conflicts SET body=$body WHERE id=$id AND body=$prior AND acknowledged=0";
            if (priorConflictBody is not null) conflict.Parameters.AddWithValue("$prior", priorConflictBody);
            conflict.Parameters.AddWithValue("$id", identityConflict.Id); conflict.Parameters.AddWithValue("$body", Wire.Encode(identityConflict));
            if (conflict.ExecuteNonQuery() != 1) throw new InvalidDataException("Conflict scope changed during recovery");
        }
        if (clearConflict is not null)
        {
            using var clear = db.CreateCommand(); clear.Transaction = tx; clear.CommandText = "UPDATE parcel_identity_conflicts SET acknowledged=1 WHERE id=$id AND acknowledged=0";
            clear.Parameters.AddWithValue("$id", clearConflict); if (clear.ExecuteNonQuery() != 1) throw new InvalidOperationException("Conflict acknowledgement identity changed");
        }
        if (acknowledgedOverload is not null)
        {
            using var ack = db.CreateCommand(); ack.Transaction = tx;
            ack.CommandText = "INSERT INTO parcel_overload_ack VALUES($identity,$command)";
            ack.Parameters.AddWithValue("$identity", acknowledgedOverload); ack.Parameters.AddWithValue("$command", command!.Id); ack.ExecuteNonQuery();
        }
        if (operation is not null)
        {
            ValidateIntegrationIntentCommit(operation,tx);
            if (StorageVersion >= 5)
            {
                using var prior = db.CreateCommand(); prior.Transaction = tx;
                prior.CommandText = "INSERT OR IGNORE INTO parcel_archive_versions SELECT id,body_hash,body,archived_at FROM parcel_archives WHERE id=$id; DELETE FROM parcel_archives WHERE id=$id";
                prior.Parameters.AddWithValue("$id",operation.Id); prior.ExecuteNonQuery();
            }
            using var save = db.CreateCommand(); save.Transaction = tx;
            using var indexed = db.CreateCommand(); indexed.Transaction = tx;
            indexed.CommandText = "INSERT INTO parcel_history_index(id,admission,observation,position) VALUES($id,$admission,$observation,$position) ON CONFLICT(id) DO NOTHING";
            // Old-version repositories exist only for explicit migration verification.
            using var versionCheck = db.CreateCommand(); versionCheck.Transaction = tx; versionCheck.CommandText = "SELECT count(*) FROM sqlite_master WHERE name='parcel_history_index'";
            if ((long)versionCheck.ExecuteScalar()! != 0)
            {
                indexed.Parameters.AddWithValue("$id", operation.Id); indexed.Parameters.AddWithValue("$admission", operation.AdmissionId);
                indexed.Parameters.AddWithValue("$observation", operation.ObservationId); indexed.Parameters.AddWithValue("$position", operation.PositionId ?? "main"); indexed.ExecuteNonQuery();
            }
            save.CommandText = "INSERT INTO parcel_operations VALUES($id,$admission,$observation,$body) ON CONFLICT(id) DO UPDATE SET body=excluded.body";
            save.Parameters.AddWithValue("$id", operation.Id); save.Parameters.AddWithValue("$admission", operation.AdmissionId);
            save.Parameters.AddWithValue("$observation", operation.ObservationId); save.Parameters.AddWithValue("$body", Wire.Encode(operation)); save.ExecuteNonQuery();
        }
        using (var meta = db.CreateCommand())
        {
            meta.Transaction = tx; meta.CommandText = "UPDATE parcel_meta SET revision=revision+1,paused=$paused WHERE id=1";
            meta.Parameters.AddWithValue("$paused", paused ?? Paused); meta.ExecuteNonQuery();
        }
        using (var audit = db.CreateCommand())
        {
            audit.Transaction = tx; audit.CommandText = "INSERT INTO parcel_audit(at,event,owner,detail) VALUES($at,$event,$owner,$detail)";
            audit.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); audit.Parameters.AddWithValue("$event", eventName);
            audit.Parameters.AddWithValue("$owner", operation?.Id ?? "runtime"); audit.Parameters.AddWithValue("$detail", detail); audit.ExecuteNonQuery();
        }
        if (input is not null)
        {
            using var audit = db.CreateCommand(); audit.Transaction = tx;
            audit.CommandText = "INSERT INTO parcel_audit(at,event,owner,detail) VALUES($at,'InputObserved',$owner,$detail)";
            audit.Parameters.AddWithValue("$at", input.RecordedAt.ToString("O"));
            audit.Parameters.AddWithValue("$owner", input.OperationId ?? input.PositionId);
            audit.Parameters.AddWithValue("$detail", Wire.Encode(input)); audit.ExecuteNonQuery();
            if (StorageVersion >= 9 && ingressIdentity is not null)
            {
                using var association = db.CreateCommand(); association.Transaction = tx;
                association.CommandText = "INSERT INTO parcel_ingress_audits VALUES($input,last_insert_rowid())";
                association.Parameters.AddWithValue("$input", ingressIdentity); association.ExecuteNonQuery();
            }
            Boundary?.Invoke("AfterInputObservedInsert");
        }
        if (occurrence is not null)
        {
            using var receipt = db.CreateCommand(); receipt.Transaction = tx; receipt.CommandText = "UPDATE parcel_receipts SET state='Committed' WHERE id=$id AND state='RawDurable'";
            receipt.Parameters.AddWithValue("$id", occurrence); if (receipt.ExecuteNonQuery() != 1) throw new InvalidDataException("Receipt occurrence is not raw-durable");
        }
        if (reconciledOccurrence is not null)
        {
            // A different Query proves only the business outcome. The old occurrence
            // remains Awaiting and all custody/image/package/archive guards keep it.
            using var resolve = db.CreateCommand(); resolve.Transaction = tx;
            resolve.CommandText = "SELECT count(*) FROM parcel_receipts r JOIN parcel_receipts q ON q.id=$query WHERE r.id=$old AND r.state='Awaiting' AND r.raw_hash IS NULL AND q.state='Committed' AND q.source='Query' AND q.operation=r.operation AND q.request=r.request AND q.stage=r.stage";
            resolve.Parameters.AddWithValue("$old", reconciledOccurrence);
            resolve.Parameters.AddWithValue("$query", occurrence ?? outcomeQuery ?? throw new InvalidOperationException("Resolution requires its own committed Query"));
            if ((long)resolve.ExecuteScalar()! != 1) throw new InvalidDataException("Original missing-raw occurrence or authoritative Query changed");
            if (StorageVersion >= 5)
            {
                using var basis = db.CreateCommand(); basis.Transaction = tx;
                basis.CommandText = "INSERT INTO parcel_receipt_custody VALUES($old,$query,$at,$evidence) ON CONFLICT(occurrence) DO UPDATE SET query_occurrence=excluded.query_occurrence,observed_at=excluded.observed_at,evidence=excluded.evidence";
                basis.Parameters.AddWithValue("$old", reconciledOccurrence); basis.Parameters.AddWithValue("$query", occurrence ?? outcomeQuery!);
                basis.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); basis.Parameters.AddWithValue("$evidence", detail); basis.ExecuteNonQuery();
            }
        }
        if (command is not null)
        {
            WriteCommand(tx, command, commandResult ?? new("Persisted", "Application command committed", Revision + 1));
        }
        Boundary?.Invoke("BeforeBusinessCommit:" + eventName);
        tx.Commit();
        Boundary?.Invoke("AfterBusinessCommit:" + eventName);
        if(operation?.Wcs?.Request.IntegrationRequest is {} allocated)lastIntegrationRequestId=Math.Max(lastIntegrationRequestId,allocated.RequestId);
        Revision++; if (paused is not null) Paused = paused.Value;
    }
    public string BeginReceipt(ParcelOperation op, StageFact stage, string source)
    {
        var id = Wire.Id(); using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO parcel_receipts VALUES($id,$op,$stage,$request,$source,'Awaiting',NULL)";
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$op", op.Id); cmd.Parameters.AddWithValue("$stage", stage.Request.Stage);
        cmd.Parameters.AddWithValue("$request", stage.Request.Id); cmd.Parameters.AddWithValue("$source", source); cmd.ExecuteNonQuery(); return id;
    }
    public void StoreRaw(string occurrence, StageReply reply)
    {
        ExtensionStore.AtomicWrite(Path.Combine(rawRoot, occurrence + ".json"), reply);
        if (Boundary is not null)
        {
            using var hook=db.CreateCommand(); hook.CommandText="SELECT stage FROM parcel_receipts WHERE id=$id";hook.Parameters.AddWithValue("$id",occurrence);
            Boundary("AfterRawFileDurable:"+(string)hook.ExecuteScalar()!);
        }
        using var tx = db.BeginTransaction();
        using var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "UPDATE parcel_receipts SET state='RawDurable',raw_hash=$hash WHERE id=$id AND state='Awaiting'";
        cmd.Parameters.AddWithValue("$id", occurrence); cmd.Parameters.AddWithValue("$hash", Wire.Hash(Wire.Encode(reply)));
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidDataException("Receipt occurrence cannot be reused");
        RegisterReplyIdentity(OccurrenceRequest(occurrence, tx), reply, tx);
        tx.Commit();
    }
    public void NoReply(string occurrence)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE parcel_receipts SET state='NoReply' WHERE id=$id AND state='Awaiting'";
        cmd.Parameters.AddWithValue("$id", occurrence); cmd.ExecuteNonQuery();
    }
    public void ValidateRawInventory()
    {
        // Streaming inventory: history growth cannot allocate a dictionary of every receipt.
        foreach (var file in Directory.EnumerateFiles(rawRoot))
        {
            maintenanceCancellation.ThrowIfCancellationRequested();
            var id = Path.GetFileNameWithoutExtension(file);
            if (Path.GetExtension(file) != ".json" || !Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Unknown/interrupted original raw custody file");
            using var c = db.CreateCommand(); c.CommandText = "SELECT raw_hash FROM parcel_receipts WHERE id=$id"; c.Parameters.AddWithValue("$id", id);
            using var r = c.ExecuteReader(); if (!r.Read()) throw new InvalidDataException("Unknown original receipt file");
            if (ArchivedReceiptExpectedHash(id) is { } archivedHash)
                RequireArchivedReceipt(id, archivedHash); // Optional redundant spool is checked by cleanup below.
            else if (!r.IsDBNull(0) && Wire.FileHash(file, maintenanceCancellation) != r.GetString(0)) throw new InvalidDataException("Original raw receipt hash differs");
        }
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT id,raw_hash FROM parcel_receipts WHERE state IN ('Committed','RawDurable')";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            maintenanceCancellation.ThrowIfCancellationRequested();
            var id = reader.GetString(0); if (File.Exists(Path.Combine(rawRoot, id + ".json"))) continue;
            if (!HasArchivedRaw(id, reader.GetString(1))) throw new InvalidDataException("Durable original raw receipt is missing");
        }
        ValidateDurableReplyIdentities();
        CleanupArchivedRawFiles();
    }
    (string Id, string Operation, string Stage, string Request, StageReply? Reply)[]? projectedReceipts;
    internal void BeginReceiptProjection() => projectedReceipts = PendingReceipts();
    internal void EndReceiptProjection() => projectedReceipts = null;
    public (string Id, string Operation, string Stage, string Request, StageReply? Reply)[] PendingReceipts()
    {
        if (projectedReceipts is not null) return projectedReceipts;
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT id,operation,stage,request,raw_hash FROM parcel_receipts WHERE state IN ('Awaiting','RawDurable')";
        using var r = cmd.ExecuteReader(); List<(string, string, string, string, StageReply?)> result = [];
        while (r.Read())
        {
            var id = r.GetString(0); var file = Path.Combine(rawRoot, id + ".json"); StageReply? reply = null;
            if (File.Exists(file))
            {
                reply = Wire.Decode<StageReply>(File.ReadAllText(file));
                if (!r.IsDBNull(4) && Wire.Hash(Wire.Encode(reply)) != r.GetString(4)) throw new InvalidDataException("Original receipt bytes changed");
            }
            result.Add((id, r.GetString(1), r.GetString(2), r.GetString(3), reply));
        }
        return result.ToArray();
    }
    public void RecoverRaw(string id, StageReply reply)
    {
        using var tx = db.BeginTransaction();
        using var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "UPDATE parcel_receipts SET state='RawDurable',raw_hash=$hash WHERE id=$id AND state IN ('Awaiting','RawDurable')";
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$hash", Wire.Hash(Wire.Encode(reply)));
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidDataException("Original recovery occurrence changed");
        RegisterReplyIdentity(OccurrenceRequest(id, tx), reply, tx); Boundary?.Invoke("BeforeRecoveredReplyIdentityCommit"); tx.Commit();
    }
    public WeightMeasurement? ReadMeasurement(string id)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT body FROM parcel_measurements WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar() is string body ? Wire.Decode<WeightMeasurement>(body) : null;
    }
    public bool HasMeasurement(WeightMeasurement measurement)
    {
        var prior = ReadMeasurement(measurement.Id);
        if (prior is not null && Wire.Encode(prior) != Wire.Encode(measurement)) throw new InvalidDataException("Measurement identity reused with different content");
        return prior is not null;
    }
    internal ParcelOperation FindMeasurementOwner(string admission)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT id FROM parcel_history_index WHERE admission=$admission"; cmd.Parameters.AddWithValue("$admission", admission);
        return cmd.ExecuteScalar() is string id && ReadOperation(id) is { } owner ? owner : throw new InvalidDataException("Original measurement owner is missing; preserve database custody");
    }
    public BusinessReceipt? PriorCommand(BusinessCommand c)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT hash,receipt FROM parcel_commands WHERE id=$id"; cmd.Parameters.AddWithValue("$id", c.Id);
        using var r = cmd.ExecuteReader();
        return !r.Read() ? null : r.GetString(0) != Wire.Hash(Wire.Encode(c)) ? new("Rejected", "Command ID reused with changed content", Revision) : Wire.Decode<BusinessReceipt>(r.GetString(1));
    }
    void WriteCommand(Microsoft.Data.Sqlite.SqliteTransaction tx, BusinessCommand c, BusinessReceipt result)
    {
        using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        // Only an admitted, unfinished command can acquire its final receipt. Existing
        // completed receipts (including historical ones) are immutable.
        cmd.CommandText = "INSERT INTO parcel_commands VALUES($id,$hash,$receipt) ON CONFLICT(id) DO UPDATE SET receipt=excluded.receipt WHERE hash=excluded.hash AND json_extract(receipt,'$.Status')='Pending'";
        cmd.Parameters.AddWithValue("$id", c.Id); cmd.Parameters.AddWithValue("$hash", Wire.Hash(Wire.Encode(c))); cmd.Parameters.AddWithValue("$receipt", Wire.Encode(result));
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidDataException("Command identity already completed or content changed");
    }
    internal void AdmitCommand(BusinessCommand c)
    {
        Commit(null, "CommandAdmitted", Wire.Encode(new { c.Id, c.Kind, c.Owner, Policy = "Interrupted commands are inspected, never automatically replayed" }), command: c,
            commandResult: new("Pending", "Command identity persisted; inspect business/configuration/receipt facts before a new command; this ID never re-executes", Revision + 1));
        Boundary?.Invoke("AfterCommandAdmitted");
    }
    internal BusinessReceipt CompleteCommand(BusinessCommand c, BusinessReceipt result)
    {
        result = result with { Revision = Revision + 1 };
        Commit(null, "CommandCompleted", Wire.Encode(new { c.Id, Result = result.Status }), command: c, commandResult: result);
        return result;
    }
    internal string[] PendingCommands()
    {
        using var c = db.CreateCommand();
        c.CommandText = "SELECT id FROM parcel_commands WHERE json_extract(receipt,'$.Status')='Pending' ORDER BY rowid DESC LIMIT 32";
        using var r = c.ExecuteReader(); List<string> ids = []; while (r.Read()) ids.Add(r.GetString(0)); return ids.ToArray();
    }
    internal bool IsPendingCommand(string? id)
    {
        if (id is null) return false;
        using var c = db.CreateCommand(); c.CommandText = "SELECT 1 FROM parcel_commands WHERE id=$id AND json_extract(receipt,'$.Status')='Pending'";
        c.Parameters.AddWithValue("$id", id); return c.ExecuteScalar() is not null;
    }
    public long StorageBytes
    {
        get
        {
            using var c = db.CreateCommand(); c.CommandText = "PRAGMA page_count"; var pages = (long)c.ExecuteScalar()!;
            c.CommandText = "PRAGMA page_size"; return checked(pages * (long)c.ExecuteScalar()!);
        }
    }
    public void Backup(string destination) => Database.Backup(db, destination);
    public void Backup(string destination, CancellationToken token)
    {
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = db.DataSource, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 5 }.ToString());
        token.ThrowIfCancellationRequested(); source.Open();
        using var snapshot = source.BeginTransaction(deferred: true);
        using (var establish = source.CreateCommand())
        { establish.Transaction = snapshot; establish.CommandText = "SELECT revision FROM parcel_meta WHERE id=1"; _ = establish.ExecuteScalar(); }
        Database.Backup(source, destination, token);
    }
    public void Dispose() => db.Dispose();
}
