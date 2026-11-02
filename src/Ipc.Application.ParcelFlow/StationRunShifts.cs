using System.Globalization;
using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    internal TimeProvider StationTime = TimeProvider.System;
    bool stationStartupCompleted;
    DateTimeOffset stationOpeningAt;
    long stationOpeningTicks;
}
public sealed partial class ParcelRepository
{
    string? stationShiftId;
    TimeProvider stationTime = TimeProvider.System;
    long stationOpenedTicks;
    DateTimeOffset stationOpenedAt;
    internal string? CurrentStationShiftId => stationShiftId;
    internal double CurrentStationElapsed => stationShiftId is null ? 0 : Math.Max(0, stationTime.GetElapsedTime(stationOpenedTicks).TotalMilliseconds);
    internal void OpenStationShift(Guid runtime, TimeProvider clock, DateTimeOffset openedAt, long openedTicks)
    {
        if (stationShiftId is not null) throw new InvalidOperationException("同一业务实例不能重复开班");
        var id = runtime.ToString("N"); var ticks = openedTicks; var now = clock.GetUtcNow().ToUniversalTime();
        stationTime = clock;
        using var tx = db.BeginTransaction(); using var c = db.CreateCommand(); c.Transaction = tx;
        // The application-owner lease precedes this transaction. An old Open row
        // proves an unclosed run, never the time it physically stopped.
        c.CommandText = "UPDATE station_run_shifts SET status='Interrupted',detected_at=$at WHERE status='Open'; INSERT INTO station_run_shifts(id,opened_at,last_seen_at,status,elapsed_ms) VALUES($id,$opened,$at,'Open',$elapsed)";
        c.Parameters.AddWithValue("$id", id); c.Parameters.AddWithValue("$at", now.ToString("O")); c.Parameters.AddWithValue("$opened", openedAt.ToUniversalTime().ToString("O")); c.Parameters.AddWithValue("$elapsed", Math.Max(0, clock.GetElapsedTime(openedTicks).TotalMilliseconds)); c.ExecuteNonQuery();
        WriteStationLifecycle(tx, id, "StationShiftOpened", new { Runtime = runtime, OpenedAt = openedAt, ConfirmedAt = now, Policy = "One business runtime, not one page or window" });
        tx.Commit(); Revision++;
        stationTime = clock; stationOpenedTicks = ticks; stationOpenedAt = openedAt; stationShiftId = id; ProjectionChanged?.Invoke();
    }
    void TouchStationShift(SqliteTransaction tx)
    {
        if (stationShiftId is null) return;
        using var c = db.CreateCommand(); c.Transaction = tx;
        var now = stationTime.GetUtcNow(); var elapsed = CurrentStationElapsed;
        c.CommandText = "UPDATE station_run_shifts SET last_seen_at=$at,elapsed_ms=$elapsed,clock_adjusted=max(clock_adjusted,$adjusted) WHERE id=$id AND status='Open'";
        c.Parameters.AddWithValue("$id", stationShiftId); c.Parameters.AddWithValue("$at", now.ToString("O")); c.Parameters.AddWithValue("$elapsed", elapsed);
        c.Parameters.AddWithValue("$adjusted", Math.Abs((now - stationOpenedAt).TotalMilliseconds - elapsed) > 5000 ? 1 : 0);
        if (c.ExecuteNonQuery() != 1) throw new InvalidDataException("当前班次身份丢失，拒绝提交业务");
    }
    internal void CloseStationShift(bool completed)
    {
        if (stationShiftId is null) return;
        using var tx = db.BeginTransaction(); TouchStationShift(tx);
        using var c = db.CreateCommand(); c.Transaction = tx;
        c.CommandText = "UPDATE station_run_shifts SET status=$status,closed_at=$at,detected_at=$detected WHERE id=$id AND status='Open'";
        c.Parameters.AddWithValue("$id", stationShiftId); c.Parameters.AddWithValue("$status", completed ? "Closed" : "Interrupted");
        c.Parameters.AddWithValue("$at", completed ? stationTime.GetUtcNow().ToString("O") : DBNull.Value);
        c.Parameters.AddWithValue("$detected", stationTime.GetUtcNow().ToString("O"));
        if (c.ExecuteNonQuery() != 1) throw new InvalidDataException("班次关闭身份变化");
        WriteStationLifecycle(tx, stationShiftId, completed ? "StationShiftClosed" : "StationShiftInterrupted", new { Completed = completed, ElapsedMilliseconds = CurrentStationElapsed });
        tx.Commit(); Revision++; stationShiftId = null; ProjectionChanged?.Invoke();
    }
    void WriteStationLifecycle(SqliteTransaction tx, string id, string name, object detail)
    {
        using var c = db.CreateCommand(); c.Transaction = tx;
        c.CommandText = "INSERT INTO parcel_audit(at,event,owner,detail) VALUES($at,$event,$id,$detail); UPDATE parcel_meta SET revision=revision+1 WHERE id=1";
        c.Parameters.AddWithValue("$at", stationTime.GetUtcNow().ToString("O")); c.Parameters.AddWithValue("$event", name);
        c.Parameters.AddWithValue("$id", id); c.Parameters.AddWithValue("$detail", Wire.Encode(detail)); c.ExecuteNonQuery();
    }
    internal void ValidateStationProjection()
    {
        using var c = db.CreateCommand();
        c.CommandText = "SELECT count(*) FROM station_metric_facts f WHERE f.shift<>'' AND NOT EXISTS(SELECT 1 FROM station_run_shifts s WHERE s.id=f.shift)";
        if (Convert.ToInt64(c.ExecuteScalar()) != 0) throw new InvalidDataException("统计事实班次归属不完整；保留原件修复");
        // Retired detail is intentionally absent. Validate the durable aggregates
        // against one another, then require each remaining live fact to be covered.
        // Do not rebuild retained history from incomplete originals.
        c.CommandText = """
            WITH b AS (SELECT shift,position,sum(recognitions) r,sum(requests) q,sum(assigned) a,sum(rejected) j,sum(unknown_count) u,sum(manual_requests) m FROM station_metric_buckets GROUP BY shift,position)
            SELECT 1 FROM b LEFT JOIN station_shift_totals t USING(shift,position) WHERE t.shift IS NULL OR b.r<>t.recognitions OR b.q<>t.requests OR b.a<>t.assigned OR b.j<>t.rejected OR b.u<>t.unknown_count OR b.m<>t.manual_requests
            UNION ALL SELECT 1 FROM station_shift_totals t WHERE NOT EXISTS(SELECT 1 FROM b WHERE b.shift=t.shift AND b.position=t.position) LIMIT 1
            """;
        if (c.ExecuteScalar() is not null) throw new InvalidDataException("班次汇总与分钟统计不一致；保留原件，拒绝重建或启动业务");
        c.CommandText = """
            WITH f AS (SELECT shift,minute,position,sum(recognitions) r,sum(requests) q,sum(assigned) a,sum(rejected) j,sum(unknown_count) u,sum(manual_requests) m FROM station_metric_facts GROUP BY shift,minute,position)
            SELECT 1 FROM f LEFT JOIN station_metric_buckets b USING(shift,minute,position) WHERE b.shift IS NULL OR f.r>b.recognitions OR f.q>b.requests OR f.a>b.assigned OR f.j>b.rejected OR f.u>b.unknown_count OR f.m>b.manual_requests LIMIT 1
            """;
        if (c.ExecuteScalar() is not null) throw new InvalidDataException("活动统计事实未被完整汇总；保留原件，拒绝启动业务");
    }
}
