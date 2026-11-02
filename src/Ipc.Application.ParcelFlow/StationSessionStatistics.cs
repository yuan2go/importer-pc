using System.Globalization;
using System.Reflection;
using System.Text;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IStationSessionStatistics
{
    sealed record ReportSelection(StationSessionReport Report, long Created, int Bytes);
    readonly object stationReportSync = new();
    readonly Dictionary<string, ReportSelection> stationReports = new(StringComparer.Ordinal);
    int stationExporting;
    internal TimeSpan StationReportLifetime = TimeSpan.FromMinutes(10);
    public Task<StationSessionReport> ReadSessionStatisticsAsync(StationReportQuery query, CancellationToken token)
        => OwnedRead(async owned =>
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(owned); budget.CancelAfter(TimeSpan.FromSeconds(10));
            StationOperationsPolicy policy; string shift; DateTimeOffset now; double elapsed;
            await gate.WaitAsync(budget.Token).ConfigureAwait(false);
            try
            {
                if (Id != "sportmaster") throw new NotSupportedException("当前业务不提供SM运行班次");
                shift = repository?.CurrentStationShiftId ?? throw new InvalidOperationException("业务运行班次尚未建立");
                now = StationTime.GetUtcNow(); elapsed = repository.CurrentStationElapsed;
                policy = Wire.Decode<StationOperationsPolicy>(Wire.Encode(Effective.OperationsPolicy ?? StationShiftSchedule.Default));
            }
            finally { gate.Release(); }
            var value = StationSessionReportReader.Read(Path.Combine(directory!, Id + ".db"), shift, query, policy, now, elapsed, budget.Token);
            budget.Token.ThrowIfCancellationRequested();
            if (!query.IncludeDetails) return value;
            var id = Wire.Id(); var report = value with { SelectionId = id, ExpiresAt = DateTimeOffset.UtcNow + StationReportLifetime };
            var json = Wire.Encode(report); var size = Encoding.UTF8.GetByteCount(json);
            if (size > 16 * 1024 * 1024) return value with { Notices = [.. value.Notices, "明细超过16MiB快照预算，导出未启用，请缩小范围。"] };
            lock (stationReportSync)
            {
                PruneStationReports();
                while (stationReports.Count >= 8 || stationReports.Values.Sum(s => s.Bytes) + size > 16 * 1024 * 1024)
                    stationReports.Remove(stationReports.MinBy(p => p.Value.Created).Key);
                stationReports.Add(id, new(Wire.Decode<StationSessionReport>(json), System.Diagnostics.Stopwatch.GetTimestamp(), size));
            }
            return report;
        }, token);
    void ReleaseStationReports() { lock (stationReportSync) stationReports.Clear(); }
    void PruneStationReports()
    {
        foreach (var row in stationReports.Where(p => System.Diagnostics.Stopwatch.GetElapsedTime(p.Value.Created) >= StationReportLifetime).ToArray()) stationReports.Remove(row.Key);
    }
    public async Task<string> ExportSessionStatisticsAsync(string selectionId, string destination, CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref stationExporting, 1, 0) != 0) throw new InvalidOperationException("已有统计导出正在执行");
        try
        {
            return await OwnedRead(async owned =>
            {
                StationSessionReport report;
                lock (stationReportSync) { PruneStationReports(); report = stationReports.TryGetValue(selectionId, out var saved) ? saved.Report : throw new InvalidOperationException("统计快照已过期，请刷新后再导出"); }
                destination = Path.GetFullPath(destination); RecoveryFiles.RequireOrdinaryAncestors(destination);
                foreach (var root in new[] { directory!, launch.DataRoot, launch.ConfigurationDirectory })
                {
                    var relative = Path.GetRelativePath(root, destination);
                    if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar)) throw new IOException("统计导出须位于配置与业务数据根目录外");
                }
                if (File.Exists(destination) || File.Exists(destination + ".result.json")) throw new IOException("请选择新的导出文件，不覆盖原记录");
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(owned); budget.CancelAfter(TimeSpan.FromMinutes(2));
                var initial = new { Status = "Prepared", report.SelectionId, report.Range, Shift = report.Shift?.Id, report.From, report.ToExclusive, report.ReadAt, report.DetailsFrom, report.TimeZoneId };
                using (var reserved = new FileStream(destination + ".result.json", FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { var bytes = Encoding.UTF8.GetBytes(Wire.Encode(initial)); reserved.Write(bytes); reserved.Flush(true); }
                var temporary = destination + ".partial-" + Wire.Id(); var status = "Failed";
                try
                {
                    using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                    {
                        using var writer = new StreamWriter(file, new UTF8Encoding(true), 65536, leaveOpen: true);
                        // The original eight CSV headings remain byte-identical.
                        await writer.WriteAsync("UTC时间,投递位,识别次数,请求次数,格口取得次数,拒绝次数,未确认次数,人工请求次数\r\n".AsMemory(), budget.Token);
                        foreach (var row in report.Buckets)
                        {
                            budget.Token.ThrowIfCancellationRequested();
                            var position = row.Position is "left" or "right" or "main" ? row.Position : "'" + row.Position.Replace("\"", "\"\"", StringComparison.Ordinal);
                            await writer.WriteAsync(FormattableString.Invariant($"{row.Minute:O},\"{position}\",{row.Recognitions},{row.Requests},{row.Assigned},{row.Rejected},{row.Unknown},{row.ManualRequests}\r\n").AsMemory(), budget.Token);
                        }
                        await writer.FlushAsync(budget.Token); file.Flush(true);
                    }
                    owned.ThrowIfCancellationRequested(); File.Move(temporary, destination, false); status = "CompletedReceiptPending";
                    ExtensionStore.AtomicWrite(destination + ".result.json", new { Status = "Completed", Source = typeof(ParcelRuntime).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion, Snapshot = report, CsvSha256 = Wire.FileHash(destination), Rows = report.Buckets.Length });
                    status = "Completed"; return destination;
                }
                catch (OperationCanceledException) { status = "Cancelled"; throw; }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                    if (status != "Completed") ExtensionStore.AtomicWrite(destination + ".result.json", new { Status = status, Initial = initial });
                }
            }, token).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref stationExporting, 0); }
    }
}
