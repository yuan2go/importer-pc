using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

// Read-only snapshots pin the actual file objects across rename/delete rotation. No business lock.
public sealed partial class SoftwareLogReader(string diagnostics, LocalManagementMode management, string? securityDiagnostics = null, string? protectedRoot = null) : IDisposable
{
    internal long ScannedLines;
    internal Action? BeforeSourceRead { get; set; }
    sealed record Source(string Id, string Name, FileStream Stream, long Length);
    sealed class Selection(SoftwareLogQuery query, Source[] sources, CancellationToken authority)
    {
        public SoftwareLogQuery Query { get; } = query;
        public Source[] Sources { get; } = sources;
        public CancellationToken Authority { get; } = authority;
        public CancellationTokenSource Lifetime { get; } = new(TimeSpan.FromMinutes(10));
        public SemaphoreSlim IndexGate { get; } = new(1, 1);
        public LogIndex? Index;
        public int Users;
        public bool Retired;
        public DateTimeOffset? Earliest, Latest;
    }
    sealed class ReadLease(SoftwareLogReader owner, Selection value) : IDisposable
    {
        public Selection Value => value;
        public void Dispose()
        {
            lock (owner.sync)
            {
                value.Users--;
                if (!value.Retired && (value.Authority.IsCancellationRequested || value.Lifetime.IsCancellationRequested))
                {
                    foreach (var id in owner.selections.Where(p => ReferenceEquals(p.Value, value)).Select(p => p.Key).ToArray()) owner.selections.Remove(id);
                    owner.Retire(value);
                }
                else if (value.Retired && value.Users == 0) owner.Close(value);
            }
        }
    }
    readonly Dictionary<string, Selection> selections = [];
    readonly object sync = new();
    Timer? cleanup;
    bool disposed;
    internal int OpenSources;
    void Close(Selection s)
    {
        foreach (var source in s.Sources) source.Stream.Dispose();
        Interlocked.Add(ref OpenSources, -s.Sources.Length);
        s.Index = null; s.IndexGate.Dispose(); s.Lifetime.Dispose();
    }
    void Retire(Selection s)
    {
        s.Retired = true; s.Lifetime.Cancel();
        if (s.Users == 0) Close(s);
    }
    void PruneLocked()
    {
        foreach (var key in selections.Where(x => x.Value.Authority.IsCancellationRequested || x.Value.Lifetime.IsCancellationRequested).Select(x => x.Key).ToArray())
        { var s = selections[key]; selections.Remove(key); Retire(s); }
    }
    void Prune() { lock (sync) PruneLocked(); }
    static string Redact(string text) => DiagnosticText.Protect(text);
    ReadLease Get(string id)
    {
        management.Require();
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this); PruneLocked();
            if (!selections.TryGetValue(id, out var s)) throw new InvalidOperationException("查询已过期或权限已撤销，请重新查询。");
            s.Users++; return new(this, s);
        }
    }
    string Select(SoftwareLogQuery q)
    {
        var authority = management.Require();
        if (q.From >= q.ToExclusive || q.ToExclusive - q.From > TimeSpan.FromDays(3660) || q.Keyword.Length > 2048 || q.Level.Length > 32 || q.Source.Length > 128) throw new InvalidDataException("请检查时间范围和检索条件长度。");
        _ = Matcher(q);
        List<Source> sources = []; Selection? created = null;
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
            var id = Guid.NewGuid().ToString("N");
            lock (sync)
            {
                ObjectDisposedException.ThrowIf(disposed, this); PruneLocked();
                if (selections.Count >= 4) { var old = selections.First(); selections.Remove(old.Key); Retire(old.Value); }
                var s = created = new Selection(q, sources.ToArray(), authority);
                selections.Add(id, s); Interlocked.Add(ref OpenSources, sources.Count);
                cleanup ??= new Timer(_ => Prune(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            }
            return id;
        }
        catch { if (created is null) foreach (var s in sources) s.Stream.Dispose(); throw; }
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
    static string[] Notices(Selection s) => ["现存日志记录时间：" + (s.Earliest?.ToString("O") ?? "未取得") + " ～ " + (s.Latest?.ToString("O") ?? "未取得") + "。早于首条/晚于末条的请求范围无法证明完整；无记录不等于无异常。", "仅查询开始时已保留的日志；历史已清理部分不可补回。轮转文件按原文件对象固定，查询10分钟有效。", "不完整的末行不进入本次快照；无有效时间的记录仅能通过相邻上下文查看。敏感凭据已隐藏。", .. (s.Index?.Streaming == true ? new[] { "记录量超过定位缓存预算，本次采用有界流式读取；翻页需重新扫描，原记录和完整导出均保留。" } : Array.Empty<string>()), .. (s.Sources.Length == 0 ? new[] { "所选软件尚无保留日志。" } : Array.Empty<string>())];
    public async Task<SoftwareLogPage> QueryAsync(SoftwareLogQuery q, string? selection, int skip, CancellationToken token)
    {
        if (skip is < 0 or > 1_000_000) throw new InvalidDataException("分页位置超出限制。");
        var id = selection ?? Select(q); using var lease = Get(id); var s = lease.Value;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token, s.Authority, s.Lifetime.Token); budget.CancelAfter(TimeSpan.FromSeconds(20));
        var index = await IndexAsync(s, budget.Token).ConfigureAwait(false); ValidateLengths(s);
        if (index.Streaming) return await StreamQuery(s, id, skip, budget.Token).ConfigureAwait(false);
        List<SoftwareLogEntry> page = []; var check = AuthorityCheck(budget.Token);
        foreach (var row in index.Matches.Skip(skip).Take(200)) { check(); page.Add(await ReadRow(s, index.Rows[row], budget.Token).ConfigureAwait(false)); }
        management.Require(); budget.Token.ThrowIfCancellationRequested();
        return new(id, page.ToArray(), index.Matches.Length > (long)skip + page.Count, Notices(s));
    }
    public async Task<SoftwareLogContext> ContextAsync(string id, SoftwareLogLocation location, int before, int after, CancellationToken token)
    {
        if (before is < 0 or > 200 || after is < 0 or > 200) throw new InvalidDataException("上下文前后各支持0～200条原始记录。");
        using var lease = Get(id); var s = lease.Value;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token, s.Authority, s.Lifetime.Token); budget.CancelAfter(TimeSpan.FromSeconds(20));
        var index = await IndexAsync(s, budget.Token).ConfigureAwait(false); ValidateLengths(s);
        if (index.Streaming) return await StreamContext(s, location, before, after, budget.Token).ConfigureAwait(false);
        var found = Array.FindIndex(index.Rows, r => s.Sources[r.Source].Id == location.FileId && r.Number == location.Line);
        if (found < 0) throw new IOException("所选原日志不在保留快照中，请重新查询。");
        if (index.Rows[found].Hash != location.Hash) throw new IOException("原日志位置内容已变化，不能显示错位上下文。");
        var group = s.Sources[index.Rows[found].Source].Name.Split('/')[0];
        var start = found; var end = found;
        while (start > 0 && found - start < before && s.Sources[index.Rows[start - 1].Source].Name.Split('/')[0] == group) start--;
        while (end + 1 < index.Rows.Length && end - found < after && s.Sources[index.Rows[end + 1].Source].Name.Split('/')[0] == group) end++;
        List<SoftwareLogEntry> result = []; var check = AuthorityCheck(budget.Token);
        for (var i = start; i <= end; i++) { check(); result.Add(await ReadRow(s, index.Rows[i], budget.Token).ConfigureAwait(false)); }
        management.Require(); budget.Token.ThrowIfCancellationRequested();
        return new(result.ToArray(), [.. Notices(s), .. (found - start < before || end - found < after ? new[] { "已到保留文件边界，部分上下文不可取得。" } : Array.Empty<string>())]);
    }
    public async Task<long> ExportAsync(string id, string destination, bool range, int context, CancellationToken token)
    {
        if (context is < 0 or > 200) throw new InvalidDataException("导出上下文范围为0～200条。");
        using var lease = Get(id); var s = lease.Value; using var budget = CancellationTokenSource.CreateLinkedTokenSource(token, s.Authority, s.Lifetime.Token); budget.CancelAfter(TimeSpan.FromMinutes(5));
        _ = await IndexAsync(s, budget.Token).ConfigureAwait(false); ValidateLengths(s);
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
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return; disposed = true; cleanup?.Dispose();
            foreach (var s in selections.Values) Retire(s); selections.Clear();
        }
    }
}
