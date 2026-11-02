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
            var existing = (configuration.Current.Pending ?? Effective).OperationsPolicy ?? StationShiftSchedule.Default;
            if (Wire.Encode(policy.Shifts) != Wire.Encode(existing.Shifts))
                return new("Rejected", "班次由打开和关闭供包台确定；不支持按固定时刻修改班次。原历史时段设置仅保留兼容。", repository!.Revision);
            candidate = (configuration.Current.Pending ?? Effective) with { OperationsPolicy = Wire.Decode<StationOperationsPolicy>(Wire.Encode(policy)) };
        }
        finally { gate.Release(); }
        return await StageConfigurationAsync(expectedRevision, candidate, "调整本站时区与自动维护策略").ConfigureAwait(false);
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
            SELECT minute,position,sum(recognitions),sum(requests),sum(assigned),sum(rejected),sum(unknown_count),sum(manual_requests)
              FROM station_metric_buckets WHERE julianday(minute)>=julianday($from) AND julianday(minute)<julianday($to)
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
