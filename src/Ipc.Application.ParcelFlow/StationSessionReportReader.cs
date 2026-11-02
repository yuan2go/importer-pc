using System.Globalization;
using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

internal static class StationSessionReportReader
{
    internal static DateTimeOffset Minute(DateTimeOffset at) => new(at.UtcDateTime.AddTicks(-(at.UtcTicks % TimeSpan.TicksPerMinute)), TimeSpan.Zero);
    static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    static string Key(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:00Z", CultureInfo.InvariantCulture);
    static DateTimeOffset Day(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        for (var i = 0; zone.IsInvalidTime(local); i++) { if (i == 180) throw new InvalidDataException("时区边界无效"); local = local.AddMinutes(1); }
        return new DateTimeOffset(local, zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local)).ToUniversalTime();
    }
    internal static StationSessionReport Read(string database, string currentId, StationReportQuery query, StationOperationsPolicy policy,
        DateTimeOffset now, double elapsed, CancellationToken token)
    {
        if (!Enum.IsDefined(query.Range)) throw new ArgumentException("统计范围无效");
        RecoveryFiles.RequireOrdinaryAncestors(database);
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        db.Open(); token.ThrowIfCancellationRequested(); SQLitePCL.raw.sqlite3_progress_handler(db.Handle, 1000, _ => token.IsCancellationRequested ? 1 : 0, null);
        using var tx = db.BeginTransaction(deferred: true);
        SqliteCommand Command(string sql)
        {
            var c = db.CreateCommand(); c.Transaction = tx; c.CommandTimeout = 10; c.CommandText = sql; return c;
        }
        StationRunShift? Shift(string predicate, object value)
        {
            using var c = Command("SELECT id,seq,status,opened_at,last_seen_at,closed_at,elapsed_ms,clock_adjusted FROM station_run_shifts WHERE " + predicate + " ORDER BY seq DESC LIMIT 1"); c.Parameters.AddWithValue("$value", value);
            using var r = c.ExecuteReader(); if (!r.Read()) return null;
            var id = r.GetString(0); var open = r.GetString(2) == "Open" && id == currentId; var start = Parse(r.GetString(3));
            var duration = open ? elapsed : r.GetDouble(6);
            return new(id, r.GetInt64(1), r.GetString(2), start, Parse(r.GetString(4)), r.IsDBNull(5) ? null : Parse(r.GetString(5)), duration,
                open || r.GetString(2) == "Closed", r.GetInt32(7) != 0 || open && Math.Abs((now - start).TotalMilliseconds - duration) > 5000);
        }
        var current = Shift("id=$value", currentId) ?? throw new InvalidOperationException("当前运行班次尚未建立");
        var selected = query.Range == StationReportRange.CurrentShift ? current : query.Range == StationReportRange.PreviousShift ? Shift("seq<$value", current.Sequence) : null;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(policy.TimeZoneId);
        var from = selected?.OpenedAt ?? Day(TimeZoneInfo.ConvertTime(now, zone).Date.AddDays(query.Range == StationReportRange.LastSevenDays ? -6 : 0), zone);
        var to = selected?.ClosedAt ?? now;
        var calendar = query.Range is StationReportRange.Today or StationReportRange.LastSevenDays;
        if (calendar) to = Minute(now).AddMinutes(1);
        var scope = calendar ? "minute >= $from AND minute < $to" : "shift=$shift";
        if (!calendar && selected is null) scope = "0";
        var detailsFrom = selected?.ClockAdjusted == true || from < now.AddDays(-31) ? Minute(now.AddDays(-31)) : Minute(from);
        SqliteCommand Scoped(string sql)
        {
            var c = Command(sql); c.Parameters.AddWithValue("$from", Key(Minute(from))); c.Parameters.AddWithValue("$to", Key(Minute(to).AddMinutes(to.UtcTicks % TimeSpan.TicksPerMinute == 0 ? 0 : 1)));
            c.Parameters.AddWithValue("$shift", selected?.Id ?? ""); c.Parameters.AddWithValue("$details", Key(detailsFrom)); return c;
        }
        List<StationPositionTotals> totals = []; List<StationMetricBucket> buckets = []; List<StationReasonCount> reasons = [];
        try
        {
            using (var c = Scoped("SELECT position,sum(recognitions),sum(requests),sum(assigned),sum(rejected),sum(unknown_count),sum(manual_requests) FROM " +
                (calendar ? "station_metric_buckets" : "station_shift_totals") + " WHERE " + scope + " GROUP BY position"))
            using (var r = c.ExecuteReader()) while (r.Read()) totals.Add(new(r.GetString(0),r.GetInt64(1),r.GetInt64(2),r.GetInt64(3),r.GetInt64(4),r.GetInt64(5),r.GetInt64(6)));
            if (query.IncludeDetails)
            {
                using (var c = Scoped("SELECT minute,position,sum(recognitions),sum(requests),sum(assigned),sum(rejected),sum(unknown_count),sum(manual_requests) FROM station_metric_buckets WHERE " + scope + " AND minute >= $details GROUP BY minute,position ORDER BY minute,position LIMIT 100001"))
                using (var r = c.ExecuteReader()) while (r.Read())
                { token.ThrowIfCancellationRequested(); if (buckets.Count == 100000) throw new IOException("统计明细超过10万行，请缩小范围"); buckets.Add(new(Parse(r.GetString(0)),r.GetString(1),r.GetInt64(2),r.GetInt64(3),r.GetInt64(4),r.GetInt64(5),r.GetInt64(6),r.GetInt64(7))); }
                using (var c = Scoped("SELECT scope,code,sum(count) FROM station_reason_buckets WHERE " + scope + " GROUP BY scope,code HAVING sum(count)>0 LIMIT 65"))
                using (var r = c.ExecuteReader()) while (r.Read()) { if (reasons.Count == 64) throw new InvalidDataException("统计原因类别超出预算"); reasons.Add(new(r.GetString(0),r.GetString(1),r.GetInt64(2))); }
            }
            double? duration = selected?.ElapsedReliable == true ? selected.ElapsedMilliseconds : null;
            List<string> notices = ["按软件观察与唯一原请求计数，同码独立观察分别记录；不代表实物完成件数。", "班次随业务实例成功打开、排空关闭而形成；切页和管理模式不切班。调试/试扫不计生产。", "识别按观察归属，请求/结果固定首次准备时的班次；迟到核对仍更新原班。连续空帧不计漏读包裹。"];
            if (calendar)
            {
                duration = 0;
                using var c = Scoped("SELECT id,status,opened_at,closed_at,detected_at,elapsed_ms,clock_adjusted FROM station_run_shifts WHERE opened_at < $end AND coalesce(closed_at,detected_at,$end)>$start");
                c.Parameters.AddWithValue("$start", from.ToUniversalTime().ToString("O")); c.Parameters.AddWithValue("$end", now.ToUniversalTime().ToString("O"));
                using var r = c.ExecuteReader();
                while (r.Read())
                {
                    var start = Parse(r.GetString(2)); var end = r.IsDBNull(3) ? now : Parse(r.GetString(3));
                    var valid = r.GetString(0) == currentId && r.GetString(1) == "Open" || r.GetString(1) == "Closed";
                    var measured = r.GetString(0) == currentId ? elapsed : r.GetDouble(5);
                    if (!valid || r.GetInt32(6) != 0 || Math.Abs((end-start).TotalMilliseconds-measured)>5000) { duration = null; break; }
                    var a = start > from ? start : from; var b = end < now ? end : now; if (b>a) duration += (b-a).TotalMilliseconds;
                }
                if (current.ClockAdjusted) duration = null;
                using var legacy = Scoped("SELECT coalesce(sum(requests+recognitions),0) FROM station_metric_buckets WHERE shift='' AND " + scope);
                if (Convert.ToInt64(legacy.ExecuteScalar()) > 0) { duration = null; notices.Add("范围含升级前未归属班次的历史事实；原记录/统计保留，未推算班次或运行时长。"); }
            }
            if (query.Range == StationReportRange.PreviousShift && selected is null) notices.Add("还没有上一班。");
            if (selected?.Status == "Interrupted") notices.Add("上一班未正常关闭，真实结束时刻未确认；时长只是最后持久记录的下限，不计算平均速度。");
            if (selected?.ClockAdjusted == true) notices.Add("此班系统时钟发生调整；班次顺序及总数按持久身份，运行时长按单调时钟，近10分钟速度暂不显示。");
            if (detailsFrom > Minute(from)) notices.Add("明细与CSV只覆盖最近31天；指标卡仍包含整班总数。");
            if (calendar) notices.Add("按完整分钟汇总，当前分钟包含已提交事实；均速只采用可证明的运行时长，包含软件暂停，不含关机间隙。");
            double? recent = selected?.ClockAdjusted == true ? null : 0;
            if (selected is not null && recent is not null)
            { using var c = Scoped("SELECT coalesce(sum(assigned),0) FROM station_metric_buckets WHERE shift=$shift AND minute >= $recent AND minute < $recentEnd"); c.Parameters.AddWithValue("$recent",Key(Minute(now).AddMinutes(-9))); c.Parameters.AddWithValue("$recentEnd",Key(Minute(now).AddMinutes(1))); recent = Convert.ToInt64(c.ExecuteScalar())/10d; }
            return new(null,null,query.Range,selected,from,to,now,policy.TimeZoneId,totals.ToArray(),buckets.ToArray(),reasons.ToArray(),recent,duration,detailsFrom,notices.ToArray());
        }
        catch (SqliteException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
    }
}
