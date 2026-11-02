using System.Globalization;
using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IStationExceptions
{
    public Task<StationExceptionPage> ReadExceptionsAsync(StationExceptionQuery query, CancellationToken token)
        => OwnedRead(owned =>
        {
            if (Id != "sportmaster") throw new NotSupportedException("当前业务不提供SM异常查询");
            return Task.FromResult(StationExceptionReader.Read(Path.Combine(directory!, Id + ".db"), query, owned));
        }, token);
}

internal static class StationExceptionReader
{
    internal static StationExceptionPage Read(string database, StationExceptionQuery query, CancellationToken token)
    {
            if (!Enum.IsDefined(query.Kind) || query.BeforeSequence is <= 0) throw new ArgumentException("异常查询范围无效");
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(5));
            RecoveryFiles.RequireOrdinaryAncestors(database);
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            db.Open(); SQLitePCL.raw.sqlite3_progress_handler(db.Handle, 1000, _ => budget.IsCancellationRequested ? 1 : 0, null);
            using var tx = db.BeginTransaction(deferred: true);
            SqliteCommand Command(string sql) { var c = db.CreateCommand(); c.Transaction = tx; c.CommandTimeout = 5; c.CommandText = sql; return c; }
            long Count(string predicate) { using var c = Command("SELECT count(*) FROM station_metric_facts WHERE " + predicate); return Convert.ToInt64(c.ExecuteScalar()); }
            try
            {
                var unknown = Count("kind='request' AND unknown_count=1");
                var rejected = Count("kind='request' AND rejected=1");
                var observations = Count("kind='observation' AND issues=1");
                var rows = new List<StationExceptionItem>(); var bytes = 0; long? next = null;
                if (query.Kind == StationExceptionKind.Observation)
                {
                    using var c = Command("SELECT f.source_seq,f.id,f.owner,f.fact_at,f.position,f.reason_code,a.detail FROM station_metric_facts f JOIN parcel_audit a ON a.seq=f.source_seq WHERE f.kind='observation' AND f.issues=1 ORDER BY f.source_seq DESC LIMIT 50");
                    using var r = c.ExecuteReader();
                    while (r.Read())
                    {
                        budget.Token.ThrowIfCancellationRequested(); var detail = r.GetString(6); bytes += detail.Length * 2;
                        if (bytes > 8 * 1024 * 1024) throw new InvalidDataException("异常观察超过读取预算，请到历史查询缩小范围");
                        var input = Wire.Decode<ParcelInputSnapshot>(detail);
                        rows.Add(new(r.GetInt64(0), r.GetString(1), string.IsNullOrEmpty(r.GetString(2)) ? null : r.GetString(2), null,
                            input.Observation.ObservedAt, r.GetString(4), input.BarcodeSelection?.SelectedCode ?? "", r.GetString(5), null, null, false));
                    }
                }
                else
                {
                    var unresolved = query.Kind == StationExceptionKind.Unconfirmed;
                    using var c = Command("SELECT h.seq,f.id,f.reason_code,o.body,a.body,a.body_hash FROM station_metric_facts f JOIN parcel_history_index h ON h.id=f.owner LEFT JOIN parcel_operations o ON o.id=h.id LEFT JOIN parcel_archives a ON a.id=h.id WHERE f.kind='request' AND " + (unresolved ? "f.unknown_count=1" : "f.rejected=1") + " AND h.seq<$before ORDER BY h.seq DESC LIMIT $limit");
                    c.Parameters.AddWithValue("$before", unresolved ? query.BeforeSequence ?? long.MaxValue : long.MaxValue); c.Parameters.AddWithValue("$limit", unresolved ? 101 : 50);
                    using var r = c.ExecuteReader();
                    while (r.Read())
                    {
                        budget.Token.ThrowIfCancellationRequested(); if (rows.Count == 100) { next = rows[^1].Sequence; break; }
                        var archived = r.IsDBNull(3); var body = r.GetString(archived ? 4 : 3); bytes += body.Length * 2;
                        if (bytes > 8 * 1024 * 1024) throw new InvalidDataException("异常原件超过读取预算，请到历史查询缩小范围");
                        if (archived && Wire.Hash(body) != r.GetString(5)) throw new InvalidDataException("异常历史原件校验失败；原记录保留");
                        var op = Wire.Decode<ParcelOperation>(body); var stage = op.Wcs ?? throw new InvalidDataException("异常原请求缺失");
                        var response = stage.Replies.LastOrDefault()?.Destination;
                        // Built-in RCS has no query contract. Never infer one from the response text.
                        rows.Add(new(r.GetInt64(0), r.GetString(1), op.Id, stage.Request.Id, op.ObservedAt, op.PositionId ?? "main", op.ParcelCode, r.GetString(2), response?.Code, response?.Message, false));
                    }
                }
                budget.Token.ThrowIfCancellationRequested();
                return new(query.Kind, unknown, rejected, observations, DateTimeOffset.UtcNow, rows.ToArray(), next);
            }
            catch (SqliteException) when (budget.IsCancellationRequested) { throw new OperationCanceledException(budget.Token); }
    }
}
