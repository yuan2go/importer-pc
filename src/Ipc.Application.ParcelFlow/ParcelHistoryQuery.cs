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
    public ParcelHistoryReader(string database)
    {
        RecoveryFiles.RequireOrdinaryAncestors(database);
        db = new(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
        tx = db.BeginTransaction(deferred: true);
    }
    public static void Validate(ParcelHistoryFilter f)
    {
        if (f.Barcode?.Length > 256 || f.Position?.Length > 128 || f.RequestId?.Length > 256 || f.State is { } state && !Enum.IsDefined(state) ||
            f.From is { } start && f.ToExclusive is { } end && start >= end) throw new InvalidDataException("查询条件无效；时间为含开始、不含结束的明确时区区间");
    }
    const string Source = " FROM parcel_history_index h JOIN (SELECT id,body,NULL AS hash,0 AS archived FROM parcel_operations UNION ALL SELECT id,body,body_hash,1 FROM parcel_archives) o ON o.id=h.id ";
    static string Conditions => " WHERE h.seq<=$upper AND ($code IS NULL OR json_extract(o.body,'$.ParcelCode')=$code) AND ($position IS NULL OR h.position=$position)" +
        " AND ($state IS NULL OR json_extract(o.body,'$.State')=$state) AND ($from IS NULL OR julianday(json_extract(o.body,'$.ObservedAt'))>=julianday($from))" +
        " AND ($to IS NULL OR julianday(json_extract(o.body,'$.ObservedAt'))<julianday($to))" +
        " AND ($request IS NULL OR json_extract(o.body,'$.Wcs.Request.Id')=$request OR json_extract(o.body,'$.Plc.Request.Id')=$request OR json_extract(o.body,'$.Upload.Request.Id')=$request OR json_extract(o.body,'$.Capture.Request.Id')=$request)";
    SqliteCommand Command(ParcelHistorySelection s, string sql)
    {
        Validate(s.Filter); if (s.UpperSequence < 0) throw new InvalidDataException("Invalid history boundary");
        var c = db.CreateCommand(); c.Transaction = tx; c.CommandText = sql;
        void Add(string key, object? value) => c.Parameters.AddWithValue(key, value ?? DBNull.Value);
        Add("$upper", s.UpperSequence); Add("$code", s.Filter.Barcode); Add("$position", s.Filter.Position);
        Add("$state", s.Filter.State?.ToString()); Add("$from", s.Filter.From?.ToString("O")); Add("$to", s.Filter.ToExclusive?.ToString("O")); Add("$request", s.Filter.RequestId);
        return c;
    }
    public ParcelHistoryPage Page(ParcelHistorySelection s, long before, int limit)
    {
        if (limit is < 1 or > 200 || before < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        using var c = Command(s, "SELECT h.seq,o.archived,o.body,o.hash" + Source + Conditions + " AND h.seq<$before ORDER BY h.seq DESC LIMIT $limit");
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
        using var c = Command(s, "SELECT count(*),coalesce(sum(json_extract(o.body,'$.Wcs.Request.Id') IS NOT NULL),0)," +
            "coalesce(sum(json_extract(o.body,'$.Wcs.State')='Accepted'),0)," +
            "coalesce(sum(json_extract(o.body,'$.Wcs.State')='Unknown' OR json_extract(o.body,'$.Plc.State')='Unknown' OR json_extract(o.body,'$.Upload.State')='Unknown' OR json_extract(o.body,'$.Capture.State')='Unknown'),0)," +
            "coalesce(sum(json_extract(o.body,'$.State')='PhysicalCompleted'),0)," +
            "coalesce(sum(coalesce(json_extract(o.body,'$.Wcs.Attempts'),0)+coalesce(json_extract(o.body,'$.Plc.Attempts'),0)+coalesce(json_extract(o.body,'$.Upload.Attempts'),0)+coalesce(json_extract(o.body,'$.Capture.Attempts'),0)),0)" + Source + Conditions);
        using var r = c.ExecuteReader(); r.Read(); return new(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4), r.GetInt64(5));
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
    public void Dispose() { tx.Dispose(); db.Dispose(); }
}

public sealed partial class ParcelRuntime : IParcelHistoryApplication
{
    public async Task<ParcelHistorySelection> SelectHistoryAsync(ParcelHistoryFilter filter)
    { await gate.WaitAsync().ConfigureAwait(false); try { return repository!.SelectHistory(filter); } finally { gate.Release(); } }
    public async Task<ParcelHistoryPage> QueryHistoryAsync(ParcelHistorySelection selection, long beforeSequence = long.MaxValue, int limit = 50)
        => OperatorHistory(await ReadHistoryAsync(reader => reader.Page(selection, beforeSequence, limit)).ConfigureAwait(false));
    public async Task<ParcelHistoryMetrics> HistoryMetricsAsync(ParcelHistorySelection selection)
        => await ReadHistoryAsync(reader => reader.Metrics(selection)).ConfigureAwait(false);
    async Task<T> ReadHistoryAsync<T>(Func<ParcelHistoryReader, T> read)
    {
        Task<T> task;
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!started || stopped || stopping.IsCancellationRequested) throw new InvalidOperationException("运行正在停止，不能开始历史读取");
            var database = Path.Combine(directory!, Id + ".db");
            task = Task.Run(() => { using var reader = new ParcelHistoryReader(database); return read(reader); });
            auxiliary.Add(ObserveExport(task)); // Stop drains independent readers before releasing the owner.
        }
        finally { gate.Release(); }
        return await task.ConfigureAwait(false);
    }
    public async Task<ParcelExportReceipt> ExportHistoryAsync(ParcelHistorySelection selection, string destination, CancellationToken token)
    {
        Task<ParcelExportReceipt> task;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!started || stopped || stopping.IsCancellationRequested) throw new InvalidOperationException("运行正在停止，不能开始导出");
            var relative = Path.GetRelativePath(directory!, Path.GetFullPath(destination));
            if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar)) throw new IOException("导出必须位于业务根以外");
            task = Task.Run(async () => { using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, stopping.Token); using var reader = new ParcelHistoryReader(Path.Combine(directory!, Id + ".db")); return await reader.Export(selection, destination, linked.Token); });
            auxiliary.Add(ObserveExport(task));
        }
        finally { gate.Release(); }
        return await task.ConfigureAwait(false);
    }
    string? lastExportFailure;
    async Task ObserveExport(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception error) { Volatile.Write(ref lastExportFailure, error.GetType().Name + "; export caller/receipt owns details; runtime facts unchanged"); }
    }
}
