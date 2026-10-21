using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

// Read-only snapshots pin the actual file objects across rename/delete rotation. No business lock.
public sealed class SoftwareLogReader(string diagnostics, LocalManagementMode management, string? securityDiagnostics = null, string? protectedRoot = null) : IDisposable
{
    sealed record Source(string Id, string Name, FileStream Stream, long Length);
    sealed record Selection(SoftwareLogQuery Query, Source[] Sources, DateTimeOffset At, CancellationToken Authority) : IDisposable
    { public CancellationTokenSource Lifetime { get; } = new(TimeSpan.FromMinutes(10));
      public DateTimeOffset? Earliest { get; set; } public DateTimeOffset? Latest { get; set; }
      public void Dispose() { Lifetime.Cancel(); foreach (var source in Sources) source.Stream.Dispose(); } }
    readonly Dictionary<string, Selection> selections = [];
    readonly object sync = new();
    Timer? cleanup;
    void Prune() { lock (sync) { foreach (var key in selections.Where(x => x.Value.Authority.IsCancellationRequested || DateTimeOffset.UtcNow - x.Value.At > TimeSpan.FromMinutes(10)).Select(x => x.Key).ToArray()) { selections[key].Dispose(); selections.Remove(key); } } }
    static string Redact(string text) => DiagnosticText.Protect(text);
    Selection Get(string id)
    {
        management.Require();
        lock (sync)
        {
            if (!selections.TryGetValue(id, out var s) || s.Authority.IsCancellationRequested || DateTimeOffset.UtcNow - s.At > TimeSpan.FromMinutes(10))
                throw new InvalidOperationException("查询已过期或权限已撤销，请重新查询。");
            return s;
        }
    }
    string Select(SoftwareLogQuery q)
    {
        var authority = management.Require();
        if (q.From >= q.ToExclusive || q.ToExclusive - q.From > TimeSpan.FromDays(3660) || q.Keyword.Length > 2048 || q.Level.Length > 32 || q.Source.Length > 128) throw new InvalidDataException("请检查时间范围和检索条件长度。");
        _ = Matcher(q);
        List<Source> sources = [];
        try
        {
            // Only the four files owned by RuntimeJournal, never user-supplied paths.
            foreach (var logRoot in new[] { diagnostics, securityDiagnostics }.OfType<string>())
            RuntimeJournal.Capture(logRoot, () =>
            {
            for (int i = 3; i >= 0; i--)
            {
                var file = Path.Combine(logRoot, "runtime.jsonl" + (i == 0 ? "" : "." + i));
                DeploymentPaths.RequireOrdinaryAncestors(file);
                try
                {
                    var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16384, FileOptions.Asynchronous | FileOptions.RandomAccess);
                    if (stream.Length > 16 * 1024 * 1024) { stream.Dispose(); throw new IOException("单个日志超过16MiB读取预算，请检查日志写入器。"); }
                    sources.Add(new(Guid.NewGuid().ToString("N"), (logRoot == diagnostics ? "运行/" : "认证/") + Path.GetFileName(file), stream, stream.Length));
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            return true;
            });
            authority.ThrowIfCancellationRequested();
            var s = new Selection(q, sources.ToArray(), DateTimeOffset.UtcNow, authority); var id = Guid.NewGuid().ToString("N");
            lock (sync)
            {
                foreach (var key in selections.Where(x => x.Value.Authority.IsCancellationRequested || DateTimeOffset.UtcNow - x.Value.At > TimeSpan.FromMinutes(10)).Select(x => x.Key).ToArray()) { selections[key].Dispose(); selections.Remove(key); }
                if (selections.Count >= 4) { var old = selections.MinBy(x => x.Value.At); old.Value.Dispose(); selections.Remove(old.Key); }
                selections.Add(id, s); cleanup ??= new Timer(_ => Prune(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            }
            return id;
        }
        catch { foreach (var s in sources) s.Stream.Dispose(); throw; }
    }
    static Func<string, bool> Matcher(SoftwareLogQuery q)
    {
        if (!q.Regex) return text => text.Contains(q.Keyword, StringComparison.OrdinalIgnoreCase);
        var regex = new Regex(q.Keyword, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(25));
        return regex.IsMatch;
    }
    static bool InRange(SoftwareLogEntry entry, SoftwareLogQuery q) => entry.At >= q.From && entry.At < q.ToExclusive;
    static bool Match(SoftwareLogEntry e, SoftwareLogQuery q, Func<string, bool> match) => InRange(e, q) &&
        (q.Level.Length == 0 || q.Level.Equals(e.Level, StringComparison.OrdinalIgnoreCase)) &&
        (q.Source.Length == 0 || e.Source.Contains(q.Source, StringComparison.OrdinalIgnoreCase)) && match(e.Text);
    async IAsyncEnumerable<SoftwareLogEntry> Lines(Selection selection, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        byte[] buffer = new byte[16384];
        foreach (var source in selection.Sources)
        {
            token.ThrowIfCancellationRequested();
            if (RandomAccess.GetLength(source.Stream.SafeFileHandle) < source.Length) throw new IOException("日志被原地截短，范围缺失；请重新查询，不能沿用旧定位。");
            long offset = 0, number = 0; using var line = new MemoryStream();
            while (offset < source.Length)
            {
                token.ThrowIfCancellationRequested(); management.Require();
                var count = await RandomAccess.ReadAsync(source.Stream.SafeFileHandle, buffer.AsMemory(0, (int)Math.Min(buffer.Length, source.Length - offset)), offset, token).ConfigureAwait(false);
                if (count == 0) throw new IOException("原日志范围已缺失，请重新查询。");
                offset += count;
                for (var i = 0; i < count; i++)
                {
                    if (buffer[i] != 10) { line.WriteByte(buffer[i]); if (line.Length > 262144) throw new IOException("单条日志超过256KiB，未静默截断；请缩小范围或检查写入器。"); continue; }
                    token.ThrowIfCancellationRequested(); number++;
                    var bytes = line.ToArray(); line.SetLength(0);
                    var raw = new UTF8Encoding(false, true).GetString(bytes).TrimEnd('\r');
                    DateTimeOffset? at = null; string level = "信息", origin = "运行";
                    try
                    {
                        using var json = JsonDocument.Parse(raw); var root = json.RootElement;
                        if (root.TryGetProperty("At", out var time) && time.TryGetDateTimeOffset(out var timestamp)) at = timestamp;
                        if (root.TryGetProperty("Level", out var l)) level = l.GetString() ?? level;
                        if (root.TryGetProperty("Source", out var o)) origin = o.GetString() ?? origin;
                        else if (root.TryGetProperty("Application", out var a)) origin = a.GetString() ?? origin;
                    }
                    catch (JsonException) { level = "无法解析"; }
                    // Decode JSON escapes for full exception stacks in the viewer, after redaction.
                    var text = Redact(raw);
                    yield return new(new(source.Id, number, Convert.ToHexString(SHA256.HashData(bytes)), source.Name), origin, at, level, text);
                }
            }
            // An incomplete concurrently-written tail is excluded from the frozen query, explicitly stated in notices.
        }
    }
    static string[] Notices(Selection s) => ["现存日志记录时间：" + (s.Earliest?.ToString("O") ?? "未取得") + " ～ " + (s.Latest?.ToString("O") ?? "未取得") + "。早于首条/晚于末条的请求范围无法证明完整；无记录不等于无异常。", "仅查询开始时已保留的日志；历史已清理部分不可补回。轮转文件按原文件对象固定，查询10分钟有效。", "不完整的末行不进入本次快照；无有效时间的记录仅能通过相邻上下文查看。敏感凭据已隐藏。", .. (s.Sources.Length == 0 ? new[] { "所选软件尚无保留日志。" } : Array.Empty<string>())];
    public async Task<SoftwareLogPage> QueryAsync(SoftwareLogQuery q, string? selection, int skip, CancellationToken token)
    {
        if (skip is < 0 or > 1_000_000) throw new InvalidDataException("分页位置超出限制。");
        var id = selection ?? Select(q); var s = Get(id); using var budget = CancellationTokenSource.CreateLinkedTokenSource(token, s.Authority, s.Lifetime.Token); budget.CancelAfter(TimeSpan.FromSeconds(20));
        List<SoftwareLogEntry> page = []; var matcher = Matcher(s.Query); int index = 0; bool more = false; DateTimeOffset? first = null, last = null;
        await foreach (var e in Lines(s, budget.Token))
        {
            if (e.At is { } at) { first = first is null || at < first ? at : first; last = last is null || at > last ? at : last; }
            if (!more && Match(e, s.Query, matcher) && index++ >= skip) { if (page.Count == 200) more = true; else page.Add(e); }
        }
        s.Earliest = first; s.Latest = last;
        management.Require(); return new(id, page.ToArray(), more, Notices(s));
    }
    public async Task<SoftwareLogContext> ContextAsync(string id, SoftwareLogLocation location, int before, int after, CancellationToken token)
    {
        if (before is < 0 or > 200 || after is < 0 or > 200) throw new InvalidDataException("上下文前后各支持0～200条原始记录。");
        var s = Get(id); using var budget = CancellationTokenSource.CreateLinkedTokenSource(token, s.Authority, s.Lifetime.Token); budget.CancelAfter(TimeSpan.FromSeconds(20));
        var prior = new Queue<SoftwareLogEntry>(); List<SoftwareLogEntry> result = []; bool found = false; string? group = null;
        await foreach (var e in Lines(s, budget.Token))
        {
            var currentGroup = e.Location.File.Split('/')[0];
            if (group != currentGroup) { if (found) break; prior.Clear(); group = currentGroup; }
            if (found) { if (after-- <= 0) break; result.Add(e); continue; }
            if (e.Location.FileId == location.FileId && e.Location.Line == location.Line)
            {
                if (e.Location.Hash != location.Hash) throw new IOException("原日志位置内容已变化，不能显示错位上下文。");
                found = true; result.AddRange(prior); result.Add(e);
            }
            else { prior.Enqueue(e); if (prior.Count > before) prior.Dequeue(); }
        }
        if (!found) throw new IOException("所选原日志不在保留快照中，请重新查询。");
        management.Require(); return new(result.ToArray(), [.. Notices(s), .. (prior.Count < before || after > 0 ? new[] { "已到保留文件边界，部分上下文不可取得。" } : Array.Empty<string>())]);
    }
    public async Task<long> ExportAsync(string id, string destination, bool range, int context, CancellationToken token)
    {
        if (context is < 0 or > 200) throw new InvalidDataException("导出上下文范围为0～200条。");
        var s = Get(id); using var budget = CancellationTokenSource.CreateLinkedTokenSource(token, s.Authority, s.Lifetime.Token); budget.CancelAfter(TimeSpan.FromMinutes(5));
        destination = Path.GetFullPath(destination); DeploymentPaths.RequireOrdinaryAncestors(destination);
        // Exports must not overwrite any managed software/credential/business source.
        var relative = Path.GetRelativePath(protectedRoot ?? Path.GetDirectoryName(diagnostics)!, destination);
        if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar)) throw new IOException("请选择业务数据目录之外的导出位置。");
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial"; long count = 0;
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384, true))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteLineAsync(DiagnosticText.Protect(JsonSerializer.Serialize(new { Format = "ImporterPc.SoftwareLog.v1", s.Query, AllInTimeRange = range, Context = context, Sources = s.Sources.Select(f => new { f.Id, f.Name, f.Length }), Notices = Notices(s) })).AsMemory(), budget.Token);
                var prior = new Queue<SoftwareLogEntry>(); var matcher = Matcher(s.Query); int remaining = 0; string? group = null; long lastOrdinal = -1, ordinal = 0;
                async Task Write(SoftwareLogEntry entry, long position)
                { if (position <= lastOrdinal) return; budget.Token.ThrowIfCancellationRequested(); await writer.WriteLineAsync(JsonSerializer.Serialize(entry).AsMemory(), budget.Token); lastOrdinal = position; count++; }
                await foreach (var e in Lines(s, budget.Token))
                {
                    var currentGroup = e.Location.File.Split('/')[0];
                    if (currentGroup != group) { prior.Clear(); remaining = 0; group = currentGroup; }
                    bool hit = range ? InRange(e, s.Query) : Match(e, s.Query, matcher);
                    if (hit)
                    {
                        var start = ordinal - prior.Count; foreach (var p in prior) await Write(p, start++);
                        await Write(e, ordinal); remaining = context;
                    }
                    else if (remaining > 0) { await Write(e, ordinal); remaining--; }
                    prior.Enqueue(e); if (prior.Count > context) prior.Dequeue(); ordinal++;
                }
                await writer.FlushAsync(budget.Token); stream.Flush(true);
            }
            budget.Token.ThrowIfCancellationRequested(); management.Commit(() => { budget.Token.ThrowIfCancellationRequested(); File.Move(temporary, destination, false); return true; }); return count;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Dispose() { cleanup?.Dispose(); lock (sync) { foreach (var s in selections.Values) s.Dispose(); selections.Clear(); } }
}
