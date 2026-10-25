using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow;

public sealed partial class SoftwareLogReader
{
    // Dense snapshots exceed the metadata cache, not the original reading/export
    // contract. At most one 256KiB line plus the requested page/context is retained.
    async IAsyncEnumerable<SoftwareLogEntry> StreamLines(Selection s, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        ValidateLengths(s); byte[] buffer = new byte[16384]; var check = AuthorityCheck(token);
        foreach (var source in s.Sources)
        {
            BeforeSourceRead?.Invoke(); check(); long offset = 0, number = 0; using var line = new MemoryStream();
            while (offset < source.Length)
            {
                check();
                var count = await RandomAccess.ReadAsync(source.Stream.SafeFileHandle, buffer.AsMemory(0, (int)Math.Min(buffer.Length, source.Length - offset)), offset, token).ConfigureAwait(false);
                if (count == 0) throw new IOException("原日志范围已缺失，请重新查询。");
                offset += count;
                for (var i = 0; i < count; i++)
                {
                    if (buffer[i] != 10)
                    {
                        line.WriteByte(buffer[i]);
                        if (line.Length > 262144) throw new IOException("单条日志超过256KiB，未静默截断；请检查写入器。");
                        continue;
                    }
                    check(); var bytes = line.ToArray(); line.SetLength(0);
                    yield return Decode(source, ++number, bytes);
                }
            }
        }
    }
    async Task<SoftwareLogPage> StreamQuery(Selection s, string id, int skip, CancellationToken token)
    {
        List<SoftwareLogEntry> page = []; var matcher = Matcher(s.Query); int position = 0; bool more = false; DateTimeOffset? first = null, last = null;
        await foreach (var e in StreamLines(s, token))
        {
            if (e.At is { } at) { first = first is null || at < first ? at : first; last = last is null || at > last ? at : last; }
            if (!more && Match(e, s.Query, matcher) && position++ >= skip) { if (page.Count == 200) more = true; else page.Add(e); }
        }
        s.Earliest = first; s.Latest = last; management.Require(); token.ThrowIfCancellationRequested();
        return new(id, page.ToArray(), more, Notices(s));
    }
    async Task<SoftwareLogContext> StreamContext(Selection s, SoftwareLogLocation location, int before, int after, CancellationToken token)
    {
        var prior = new Queue<SoftwareLogEntry>(); List<SoftwareLogEntry> result = []; bool found = false; string? group = null;
        await foreach (var e in StreamLines(s, token))
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
        management.Require(); token.ThrowIfCancellationRequested();
        return new(result.ToArray(), [.. Notices(s), .. (prior.Count < before || after > 0 ? new[] { "已到保留文件边界，部分上下文不可取得。" } : Array.Empty<string>())]);
    }
}
