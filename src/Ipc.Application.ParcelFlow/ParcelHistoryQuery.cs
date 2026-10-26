using System.Text;
using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    public ParcelHistorySelection SelectHistory(ParcelHistoryFilter filter)
    {
        ParcelHistoryReader.Validate(filter);
        using var command = db.CreateCommand(); command.CommandText = "SELECT coalesce(max(seq),0) FROM parcel_history_index";
        return new(filter, (long)command.ExecuteScalar()!);
    }
}

// Separate bounded read sessions keep export I/O outside the operation coordinator.
public sealed class ParcelHistoryReader : IDisposable
{
    readonly SqliteConnection db;
    readonly SqliteTransaction tx;
    readonly CancellationToken cancellation;
    public ParcelHistoryReader(string database, CancellationToken token = default)
    {
        RecoveryFiles.RequireOrdinaryAncestors(database);
        db = new(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
        cancellation = token;
        try
        {
            token.ThrowIfCancellationRequested();
            SQLitePCL.raw.sqlite3_progress_handler(db.Handle, 1000, _ => token.IsCancellationRequested ? 1 : 0, null);
            tx = db.BeginTransaction(deferred: true);
        }
        catch { db.Dispose(); throw; }
    }
    public static void Validate(ParcelHistoryFilter f)
    {
        if (f.Barcode?.Length > 256 || f.Position?.Length > 128 || f.RequestId?.Length > 256 || f.State is { } state && !Enum.IsDefined(state) ||
            f.From is { } start && f.ToExclusive is { } end && start >= end) throw new InvalidDataException("查询条件无效；时间为含开始、不含结束的明确时区区间");
    }
    const string Source = " FROM parcel_history_index h JOIN parcel_history_projection p ON p.id=h.id ";
    const string Originals = " LEFT JOIN parcel_operations o ON o.id=h.id LEFT JOIN parcel_archives a ON a.id=h.id ";
    static string Conditions(ParcelHistorySelection s)
    {
        var f = s.Filter;
        return " WHERE h.seq<=$upper" + (f.Barcode is null ? "" : " AND p.barcode=$code") +
            (f.Position is null ? "" : " AND h.position=$position") + (f.State is null ? "" : " AND p.state=$state") +
            (f.From is null ? "" : " AND p.observed>=julianday($from)") + (f.ToExclusive is null ? "" : " AND p.observed<julianday($to)") +
            (f.RequestId is null ? "" : " AND (p.wcs=$request OR p.plc=$request OR p.upload=$request OR p.capture=$request)");
    }
    SqliteCommand Command(ParcelHistorySelection s, string sql)
    {
        cancellation.ThrowIfCancellationRequested(); Validate(s.Filter); if (s.UpperSequence < 0) throw new InvalidDataException("Invalid history boundary");
        var c = db.CreateCommand(); c.Transaction = tx; c.CommandText = sql;
        void Add(string key, object? value) => c.Parameters.AddWithValue(key, value ?? DBNull.Value);
        Add("$upper", s.UpperSequence); Add("$code", s.Filter.Barcode); Add("$position", s.Filter.Position);
        Add("$state", s.Filter.State?.ToString()); Add("$from", s.Filter.From?.ToString("O")); Add("$to", s.Filter.ToExclusive?.ToString("O")); Add("$request", s.Filter.RequestId);
        return c;
    }
    public ParcelHistoryPage Page(ParcelHistorySelection s, long before, int limit)
    {
        if (limit is < 1 or > 200 || before < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        using var c = Command(s, "SELECT h.seq,a.id IS NOT NULL,coalesce(o.body,a.body),a.body_hash" + Source + Originals + Conditions(s) + " AND h.seq<$before ORDER BY h.seq DESC LIMIT $limit");
        c.Parameters.AddWithValue("$before", before); c.Parameters.AddWithValue("$limit", limit + 1);
        using var r = c.ExecuteReader(); List<ParcelHistoryItem> items = [];
        while (r.Read())
        {
            var body = r.GetString(2);
            if (!r.IsDBNull(3) && Wire.Hash(body) != r.GetString(3)) throw new InvalidDataException("Historical original checksum differs");
            items.Add(new(r.GetInt64(0), r.GetBoolean(1), Wire.Decode<ParcelOperation>(body)));
        }
        return new(items.Take(limit).ToArray(), items.Count > limit ? items[limit - 1].Sequence : null);
    }
    public ParcelHistoryMetrics Metrics(ParcelHistorySelection s)
    {
        using var c = Command(s, "SELECT count(*),coalesce(sum(p.wcs IS NOT NULL),0),coalesce(sum(p.accepted),0),coalesce(sum(p.unknown),0),coalesce(sum(p.physical),0),coalesce(sum(p.attempts),0)" + Source + Conditions(s));
        using var r = c.ExecuteReader(); r.Read(); return new(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4), r.GetInt64(5));
    }
    public string[] Positions(ParcelHistorySelection selection)
    {
        using var c = Command(selection, "SELECT DISTINCT coalesce(h.position,'main') FROM parcel_history_index h WHERE h.seq<=$upper ORDER BY 1 LIMIT 257");
        using var r = c.ExecuteReader(); List<string> result = [];
        while (r.Read()) { cancellation.ThrowIfCancellationRequested(); if (result.Count == 256) throw new IOException("历史投递位超过256项，请用精确ID查询"); result.Add(r.GetString(0)); }
        return result.ToArray();
    }
    public async Task<ParcelExportReceipt> Export(ParcelHistorySelection selection, string destination, CancellationToken token)
    {
        destination = Path.GetFullPath(destination); RecoveryFiles.RequireOrdinaryAncestors(destination);
        if (File.Exists(destination) || File.Exists(destination + ".result.json")) throw new IOException("选择新的导出文件；不覆盖原结果");
        using (var reserved = new FileStream(destination + ".result.json", FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { var initialReceipt = Encoding.UTF8.GetBytes(Wire.Encode(new ParcelExportReceipt("Prepared", 0, 0, null, selection, destination))); reserved.Write(initialReceipt); reserved.Flush(true); }
        var temporary = destination + "." + Wire.Id() + ".partial"; long rows = 0, bytes = 0;
        var status = "Failed";
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromMinutes(2));
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                await using var writer = new StreamWriter(output, new UTF8Encoding(true), 65536, leaveOpen: true);
                await writer.WriteLineAsync("sequence,observed_at_text,position_text,operation_text,barcode_text,barcode_utf8_base64,state_text,slot_text,wcs_request_text,wcs_attempts,wcs_fact_text,physical_completed");
                long cursor = long.MaxValue;
                do
                {
                    budget.Token.ThrowIfCancellationRequested(); var page = Page(selection, cursor, 100);
                    foreach (var item in page.Items)
                    {
                        budget.Token.ThrowIfCancellationRequested(); if (++rows > 100000) throw new IOException("导出超过10万作业预算；缩小筛选区间");
                        var op = item.Operation;
                        // Apostrophe is an explicit text marker. Exact UTF-8 is separately
                        // recoverable from base64; CSV quoting alone is not formula safety.
                        static string Text(string? value) => "\"'" + (value ?? "").Replace("\"", "\"\"") + "\"";
                        var line = string.Join(",", item.Sequence, Text(op.ObservedAt.ToString("O")), Text(op.PositionId ?? "main"), Text(op.Id), Text(op.ParcelCode),
                            Convert.ToBase64String(Encoding.UTF8.GetBytes(op.ParcelCode)), Text(op.State.ToString()), Text(op.Slot), Text(op.Wcs?.Request.Id), op.Wcs?.Attempts ?? 0,
                            Text(op.Wcs?.State.ToString()), op.State == ParcelState.PhysicalCompleted ? "1" : "0");
                        bytes += Encoding.UTF8.GetByteCount(line) + 2;
                        if (bytes > 64 * 1024 * 1024) throw new IOException("导出超过64MiB预算；缩小筛选区间");
                        await writer.WriteLineAsync(line.AsMemory(), budget.Token);
                    }
                    await writer.FlushAsync(budget.Token); cursor = page.NextBeforeSequence ?? 0;
                } while (cursor != 0);
                output.Flush(true);
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, destination, false); status = "CompletedReceiptPending";
            var receipt = new ParcelExportReceipt("Completed", rows, new FileInfo(destination).Length, Wire.FileHash(destination), selection, destination);
            ExtensionStore.AtomicWrite(destination + ".result.json", receipt); status = "Completed"; return receipt;
        }
        catch (OperationCanceledException) { status = "Cancelled"; throw; }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (status != "Completed") ExtensionStore.AtomicWrite(destination + ".result.json", new ParcelExportReceipt(status, rows, bytes, null, selection, destination));
        }
    }
    public void Dispose() { tx.Dispose(); SQLitePCL.raw.sqlite3_progress_handler(db.Handle, 0, null, null); db.Dispose(); }
}

public sealed partial class ParcelRuntime : IParcelHistorySessionApplication
{
    int historyExportInProgress;
    public async Task<ParcelHistorySelection> SelectHistoryAsync(ParcelHistoryFilter filter)
    { await gate.WaitAsync().ConfigureAwait(false); try { return repository!.SelectHistory(filter); } finally { gate.Release(); } }
    public async Task<ParcelHistoryPage> QueryHistoryAsync(ParcelHistorySelection selection, long beforeSequence = long.MaxValue, int limit = 50)
        => await ReadHistoryPageAsync(selection, beforeSequence, limit, default).ConfigureAwait(false);
    public async Task<ParcelHistoryPage> ReadHistoryPageAsync(ParcelHistorySelection selection, long beforeSequence, int limit, CancellationToken token)
        => OperatorHistory(await ReadHistoryAsync(reader => reader.Page(selection, beforeSequence, limit), token).ConfigureAwait(false));
    public async Task<ParcelHistoryMetrics> HistoryMetricsAsync(ParcelHistorySelection selection)
        => await ReadHistoryMetricsAsync(selection, default).ConfigureAwait(false);
    public Task<ParcelHistoryMetrics> ReadHistoryMetricsAsync(ParcelHistorySelection selection, CancellationToken token)
        => ReadHistoryAsync(reader => reader.Metrics(selection), token);
    public Task<string[]> HistoryPositionIdsAsync(ParcelHistorySelection selection, CancellationToken token)
        => ReadHistoryAsync(reader => reader.Positions(selection), token);
    Task<T> ReadHistoryAsync<T>(Func<ParcelHistoryReader, T> read, CancellationToken token = default)
        => OwnedRead(t =>
        {
            using var reader = new ParcelHistoryReader(Path.Combine(directory!, Id + ".db"), t);
            try { var result = read(reader); t.ThrowIfCancellationRequested(); return Task.FromResult(result); }
            catch (SqliteException) when (t.IsCancellationRequested) { throw new OperationCanceledException(t); }
        }, token);
    public async Task<ParcelExportReceipt> ExportHistoryAsync(ParcelHistorySelection selection, string destination, CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref historyExportInProgress, 1, 0) != 0)
            throw new InvalidOperationException("已有历史导出正在执行；等待原任务完成或取消后再导出");
        try
        {
            return await OwnedRead(async t =>
            {
                var relative = Path.GetRelativePath(directory!, Path.GetFullPath(destination));
                if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar)) throw new IOException("导出必须位于业务根以外");
                using var reader = new ParcelHistoryReader(Path.Combine(directory!, Id + ".db"), t);
                try { return await reader.Export(selection, destination, t).ConfigureAwait(false); }
                catch (SqliteException) when (t.IsCancellationRequested) { throw new OperationCanceledException(t); }
            }, token).ConfigureAwait(false);
        }
        catch (Exception ex) { Volatile.Write(ref lastExportFailure, ex.GetType().Name + "; original history retained"); throw; }
        finally { Interlocked.Exchange(ref historyExportInProgress, 0); }
    }
    string? lastExportFailure;
    async Task ObserveExport(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception error) { Volatile.Write(ref lastExportFailure, error.GetType().Name + "; export caller/receipt owns details; runtime facts unchanged"); }
    }
}
