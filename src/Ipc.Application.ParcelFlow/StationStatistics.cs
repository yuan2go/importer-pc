using System.Globalization;
using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IStationOperations
{
    public async Task<StationOperationsPolicy> ReadOperationsPolicyAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { return Wire.Decode<StationOperationsPolicy>(Wire.Encode(Effective.OperationsPolicy ?? StationShiftSchedule.Default)); }
        finally { gate.Release(); }
    }
    public Task<StationStatistics> ReadStatisticsAsync(DateTimeOffset from, DateTimeOffset toExclusive, CancellationToken token)
        => OwnedRead(async owned =>
        {
            if (Id != "sportmaster") throw new NotSupportedException("本统计用于SM业务");
            StationOperationsPolicy policy;
            await gate.WaitAsync(owned).ConfigureAwait(false);
            try { policy = Wire.Decode<StationOperationsPolicy>(Wire.Encode(Effective.OperationsPolicy ?? StationShiftSchedule.Default)); }
            finally { gate.Release(); }
            var rows = StationStatisticsReader.Read(Path.Combine(directory!, Id + ".db"), from, toExclusive, owned);
            return new StationStatistics(from, toExclusive, policy.TimeZoneId, rows, policy,
                ["按独立识别和原请求统计，不按条码去重；格口取得次数不代表实物完成件数。", "调试请求不计入生产统计；时间范围采用整分钟、左闭右开边界。"]);
        }, token);
    public async Task<BusinessReceipt> SaveOperationsPolicyAsync(long expectedRevision, StationOperationsPolicy policy)
    {
        ParcelConfiguration candidate;
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Management.Require();
            if (Id != "sportmaster" || configuration is null) throw new InvalidOperationException("SM尚未就绪");
            var errors = StationShiftSchedule.Validate(policy); if (errors.Length != 0) return new("Rejected", string.Join("；", errors), repository!.Revision);
            candidate = (configuration.Current.Pending ?? Effective) with { OperationsPolicy = Wire.Decode<StationOperationsPolicy>(Wire.Encode(policy)) };
        }
        finally { gate.Release(); }
        return await StageConfigurationAsync(expectedRevision, candidate, "调整本站班次与自动维护策略").ConfigureAwait(false);
    }
}

public static class StationStatisticsReader
{
    // Both current source facts and transactionally retained buckets are read in
    // one SQLite snapshot. A retention commit therefore cannot double count.
    public static StationMetricBucket[] Read(string database, DateTimeOffset from, DateTimeOffset to, CancellationToken token)
    {
        if (from >= to || to - from > TimeSpan.FromDays(31) || from.UtcTicks % TimeSpan.TicksPerMinute != 0 || to.UtcTicks % TimeSpan.TicksPerMinute != 0)
            throw new ArgumentException("统计范围须为整分钟且不超过31天");
        RecoveryFiles.RequireOrdinaryAncestors(database);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open(); token.ThrowIfCancellationRequested();
        SQLitePCL.raw.sqlite3_progress_handler(connection.Handle, 1000, _ => token.IsCancellationRequested ? 1 : 0, null);
        using var tx = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand(); command.Transaction = tx; command.CommandTimeout = 30;
        command.CommandText = """
            WITH operation_facts AS (
                SELECT body FROM parcel_operations WHERE julianday(json_extract(body,'$.Wcs.Request.PreparedAt'))>=julianday($from) AND julianday(json_extract(body,'$.Wcs.Request.PreparedAt'))<julianday($to)
                UNION ALL SELECT body FROM parcel_archives WHERE julianday(json_extract(body,'$.Wcs.Request.PreparedAt'))>=julianday($from) AND julianday(json_extract(body,'$.Wcs.Request.PreparedAt'))<julianday($to)
            ), live AS (
                SELECT strftime('%Y-%m-%dT%H:%M:00Z',json_extract(body,'$.Wcs.Request.PreparedAt')) minute,
                  coalesce(json_extract(body,'$.PositionId'),'main') position,0 recognitions,
                  1 requests,json_extract(body,'$.Wcs.State')='Accepted' AND coalesce(json_extract(body,'$.Slot'),'')<>'' assigned,
                  json_extract(body,'$.Wcs.State')='Rejected' rejected,
                  json_extract(body,'$.Wcs.State') IN ('Unknown','Conflict') unknown_count,
                  json_extract(body,'$.OperatorInput') IS NOT NULL manual_requests
                FROM operation_facts WHERE coalesce(json_extract(body,'$.Wcs.Attempts'),0)>0
                  AND json_extract(body,'$.DebugRequest') IS NULL AND json_extract(body,'$.OperatorDebugRequest') IS NULL
                UNION ALL
                SELECT strftime('%Y-%m-%dT%H:%M:00Z',json_extract(detail,'$.Observation.ObservedAt')),
                  json_extract(detail,'$.PositionId'),1,0,0,0,0,0 FROM parcel_audit
                  WHERE event='InputObserved' AND json_valid(detail) AND json_extract(detail,'$.Outcome')<>'Duplicate' AND julianday(json_extract(detail,'$.Observation.ObservedAt'))>=julianday($from) AND julianday(json_extract(detail,'$.Observation.ObservedAt'))<julianday($to) AND json_array_length(detail,'$.Observation.Codes')>0
                  AND json_extract(detail,'$.Observation.OperatorInput') IS NULL AND json_extract(detail,'$.Observation.InspectionSessionId') IS NULL
                UNION ALL SELECT * FROM station_metric_buckets
            ) SELECT minute,position,sum(recognitions),sum(requests),sum(assigned),sum(rejected),sum(unknown_count),sum(manual_requests)
              FROM live WHERE julianday(minute)>=julianday($from) AND julianday(minute)<julianday($to)
              GROUP BY minute,position ORDER BY minute,position LIMIT 100001
            """;
        command.Parameters.AddWithValue("$from", from.ToUniversalTime().ToString("O")); command.Parameters.AddWithValue("$to", to.ToUniversalTime().ToString("O"));
        List<StationMetricBucket> rows = [];
        try
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested(); if (rows.Count == 100000) throw new InvalidOperationException("统计超出单次预算，请缩小时间范围");
                rows.Add(new(DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7)));
            }
        }
        catch (SqliteException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        return rows.ToArray();
    }
}

public sealed partial class ParcelRepository
{
    void AddRetainedMetric(StationMetricBucket bucket, SqliteTransaction transaction)
    {
        using var c = db.CreateCommand(); c.Transaction = transaction;
        c.CommandText = """
            INSERT INTO station_metric_buckets VALUES($minute,$position,$recognitions,$requests,$assigned,$rejected,$unknown,$manual)
            ON CONFLICT(minute,position) DO UPDATE SET recognitions=recognitions+excluded.recognitions,requests=requests+excluded.requests,
              assigned=assigned+excluded.assigned,rejected=rejected+excluded.rejected,unknown_count=unknown_count+excluded.unknown_count,manual_requests=manual_requests+excluded.manual_requests
            """;
        c.Parameters.AddWithValue("$minute", bucket.Minute.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:00Z", CultureInfo.InvariantCulture));
        c.Parameters.AddWithValue("$position", bucket.Position); c.Parameters.AddWithValue("$recognitions", bucket.Recognitions);
        c.Parameters.AddWithValue("$requests", bucket.Requests); c.Parameters.AddWithValue("$assigned", bucket.Assigned); c.Parameters.AddWithValue("$rejected", bucket.Rejected);
        c.Parameters.AddWithValue("$unknown", bucket.Unknown); c.Parameters.AddWithValue("$manual", bucket.ManualRequests); c.ExecuteNonQuery();
    }
    void RetainOperationStatistics(ParcelOperation op, SqliteTransaction tx)
    {
        if (op.DebugRequest is null && op.OperatorDebugRequest is null && op.Wcs is { Attempts: > 0 } stage)
            AddRetainedMetric(new(stage.Request.PreparedAt, op.PositionId ?? "main", 0, 1,
                stage.State == StageState.Accepted && !string.IsNullOrWhiteSpace(op.Slot) ? 1 : 0,
                stage.State == StageState.Rejected ? 1 : 0, stage.State is StageState.Unknown or StageState.Conflict or StageState.Dispatching ? 1 : 0,
                op.OperatorInput is not null ? 1 : 0), tx);
        RetainRecognitions("json_extract(detail,'$.OperationId')=$owner", op.Id, tx);
    }
    void RetainInputStatistics(string ingress, SqliteTransaction tx)
        => RetainRecognitions("seq IN (SELECT seq FROM parcel_ingress_audits WHERE input=$owner)", ingress, tx);
    void RetainRecognitions(string predicate, string owner, SqliteTransaction tx)
    {
        using var c = db.CreateCommand(); c.Transaction = tx;
        c.CommandText = "SELECT detail FROM parcel_audit WHERE event='InputObserved' AND json_valid(detail) AND json_extract(detail,'$.Outcome')<>'Duplicate' AND json_array_length(detail,'$.Observation.Codes')>0 AND " + predicate;
        c.Parameters.AddWithValue("$owner", owner); List<ParcelInputSnapshot> inputs = [];
        using (var r = c.ExecuteReader()) while (r.Read()) inputs.Add(Wire.Decode<ParcelInputSnapshot>(r.GetString(0)));
        foreach (var input in inputs.Where(x => x.Observation.Codes.Length > 0 && x.Observation.OperatorInput is null && x.Observation.InspectionSessionId is null))
            AddRetainedMetric(new(input.Observation.ObservedAt, input.PositionId, 1, 0, 0, 0, 0, 0), tx);
    }
}
