using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    const string Metric11Sql = "CREATE TABLE station_metric_buckets(minute TEXT NOT NULL,position TEXT NOT NULL,recognitions INTEGER NOT NULL,requests INTEGER NOT NULL,assigned INTEGER NOT NULL,rejected INTEGER NOT NULL,unknown_count INTEGER NOT NULL,manual_requests INTEGER NOT NULL,PRIMARY KEY(minute,position));";
    const string Metric12Sql = "CREATE TABLE station_metric_buckets(shift TEXT NOT NULL,minute TEXT NOT NULL,position TEXT NOT NULL,recognitions INTEGER NOT NULL CHECK(recognitions>=0),requests INTEGER NOT NULL CHECK(requests>=0),assigned INTEGER NOT NULL CHECK(assigned>=0),rejected INTEGER NOT NULL CHECK(rejected>=0),unknown_count INTEGER NOT NULL CHECK(unknown_count>=0),manual_requests INTEGER NOT NULL CHECK(manual_requests>=0),PRIMARY KEY(shift,minute,position));";
    const string StationSessionSchema = """
        CREATE TABLE station_run_shifts(seq INTEGER PRIMARY KEY AUTOINCREMENT,id TEXT NOT NULL UNIQUE,opened_at TEXT NOT NULL,last_seen_at TEXT NOT NULL,detected_at TEXT,closed_at TEXT,status TEXT NOT NULL CHECK(status IN ('Open','Closed','Interrupted')),elapsed_ms REAL NOT NULL CHECK(elapsed_ms>=0),clock_adjusted INTEGER NOT NULL DEFAULT 0);
        CREATE UNIQUE INDEX station_one_open_shift ON station_run_shifts(status) WHERE status='Open';
        CREATE TABLE station_shift_totals(shift TEXT NOT NULL,position TEXT NOT NULL,recognitions INTEGER NOT NULL CHECK(recognitions>=0),requests INTEGER NOT NULL CHECK(requests>=0),assigned INTEGER NOT NULL CHECK(assigned>=0),rejected INTEGER NOT NULL CHECK(rejected>=0),unknown_count INTEGER NOT NULL CHECK(unknown_count>=0),manual_requests INTEGER NOT NULL CHECK(manual_requests>=0),PRIMARY KEY(shift,position));
        CREATE TABLE station_metric_facts(id TEXT PRIMARY KEY,kind TEXT NOT NULL CHECK(kind IN ('observation','request')),owner TEXT NOT NULL,source_seq INTEGER,shift TEXT NOT NULL,fact_at TEXT NOT NULL,minute TEXT NOT NULL,position TEXT NOT NULL,recognitions INTEGER NOT NULL CHECK(recognitions IN (0,1)),requests INTEGER NOT NULL CHECK(requests IN (0,1)),assigned INTEGER NOT NULL CHECK(assigned IN (0,1)),rejected INTEGER NOT NULL CHECK(rejected IN (0,1)),unknown_count INTEGER NOT NULL CHECK(unknown_count IN (0,1)),manual_requests INTEGER NOT NULL CHECK(manual_requests IN (0,1)),reason_scope TEXT NOT NULL,reason_code TEXT NOT NULL,issues INTEGER NOT NULL CHECK(issues IN (0,1)));
        CREATE UNIQUE INDEX station_observation_fact ON station_metric_facts(source_seq) WHERE source_seq IS NOT NULL;
        CREATE INDEX station_fact_owner ON station_metric_facts(owner,kind);
        CREATE INDEX station_fact_shift ON station_metric_facts(shift,kind);
        CREATE INDEX station_exception_unknown ON station_metric_facts(owner) WHERE kind='request' AND unknown_count=1;
        CREATE INDEX station_exception_rejected ON station_metric_facts(owner) WHERE kind='request' AND rejected=1;
        CREATE INDEX station_exception_observation ON station_metric_facts(source_seq DESC) WHERE kind='observation' AND issues=1;
        CREATE INDEX station_metrics_time ON station_metric_buckets(minute,position);
        CREATE TABLE station_reason_buckets(shift TEXT NOT NULL,minute TEXT NOT NULL,position TEXT NOT NULL,scope TEXT NOT NULL,code TEXT NOT NULL,count INTEGER NOT NULL CHECK(count>=0),PRIMARY KEY(shift,minute,position,scope,code));
        CREATE INDEX station_reasons_time ON station_reason_buckets(minute,scope,code);
        CREATE TRIGGER station_fact_immutable BEFORE UPDATE ON station_metric_facts WHEN NEW.id<>OLD.id OR NEW.kind<>OLD.kind OR NEW.owner<>OLD.owner OR NEW.shift<>OLD.shift OR NEW.fact_at<>OLD.fact_at OR NEW.minute<>OLD.minute OR NEW.position<>OLD.position OR coalesce(NEW.source_seq,-1)<>coalesce(OLD.source_seq,-1) BEGIN SELECT RAISE(ABORT,'Original statistic attribution cannot change'); END;
        CREATE TRIGGER station_fact_insert AFTER INSERT ON station_metric_facts BEGIN
          INSERT INTO station_metric_buckets VALUES(NEW.shift,NEW.minute,NEW.position,NEW.recognitions,NEW.requests,NEW.assigned,NEW.rejected,NEW.unknown_count,NEW.manual_requests) ON CONFLICT(shift,minute,position) DO UPDATE SET recognitions=recognitions+excluded.recognitions,requests=requests+excluded.requests,assigned=assigned+excluded.assigned,rejected=rejected+excluded.rejected,unknown_count=unknown_count+excluded.unknown_count,manual_requests=manual_requests+excluded.manual_requests;
          INSERT INTO station_shift_totals VALUES(NEW.shift,NEW.position,NEW.recognitions,NEW.requests,NEW.assigned,NEW.rejected,NEW.unknown_count,NEW.manual_requests) ON CONFLICT(shift,position) DO UPDATE SET recognitions=recognitions+excluded.recognitions,requests=requests+excluded.requests,assigned=assigned+excluded.assigned,rejected=rejected+excluded.rejected,unknown_count=unknown_count+excluded.unknown_count,manual_requests=manual_requests+excluded.manual_requests;
          INSERT INTO station_reason_buckets SELECT NEW.shift,NEW.minute,NEW.position,NEW.reason_scope,NEW.reason_code,NEW.issues WHERE NEW.issues>0 ON CONFLICT(shift,minute,position,scope,code) DO UPDATE SET count=count+excluded.count;
        END;
        CREATE TRIGGER station_fact_update AFTER UPDATE ON station_metric_facts BEGIN
          UPDATE station_metric_buckets SET recognitions=recognitions-OLD.recognitions+NEW.recognitions,requests=requests-OLD.requests+NEW.requests,assigned=assigned-OLD.assigned+NEW.assigned,rejected=rejected-OLD.rejected+NEW.rejected,unknown_count=unknown_count-OLD.unknown_count+NEW.unknown_count,manual_requests=manual_requests-OLD.manual_requests+NEW.manual_requests WHERE shift=OLD.shift AND minute=OLD.minute AND position=OLD.position;
          UPDATE station_shift_totals SET recognitions=recognitions-OLD.recognitions+NEW.recognitions,requests=requests-OLD.requests+NEW.requests,assigned=assigned-OLD.assigned+NEW.assigned,rejected=rejected-OLD.rejected+NEW.rejected,unknown_count=unknown_count-OLD.unknown_count+NEW.unknown_count,manual_requests=manual_requests-OLD.manual_requests+NEW.manual_requests WHERE shift=OLD.shift AND position=OLD.position;
          UPDATE station_reason_buckets SET count=count-OLD.issues WHERE OLD.issues>0 AND shift=OLD.shift AND minute=OLD.minute AND position=OLD.position AND scope=OLD.reason_scope AND code=OLD.reason_code;
          INSERT INTO station_reason_buckets SELECT NEW.shift,NEW.minute,NEW.position,NEW.reason_scope,NEW.reason_code,NEW.issues WHERE NEW.issues>0 ON CONFLICT(shift,minute,position,scope,code) DO UPDATE SET count=count+excluded.count;
        END;
        """;
    static long StationScalar(SqliteTransaction tx, string sql)
    { using var c = tx.Connection!.CreateCommand(); c.Transaction = tx; c.CommandText = sql; return Convert.ToInt64(c.ExecuteScalar()); }
}
