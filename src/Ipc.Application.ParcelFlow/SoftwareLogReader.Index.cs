using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow;

public sealed partial class SoftwareLogReader
{
    // Bounded metadata only. Original text stays in pinned files; page/context/export
    // reread the bytes and verify the original hash, including CR before LF.
    const int MaximumIndexedLines = 131072;
    readonly record struct Row(int Source, long Offset, int Bytes, long Number, string Hash);
    sealed record LogIndex(Row[] Rows, int[] Matches, bool Streaming = false);
    static void ValidateLengths(Selection s)
    {
        foreach (var source in s.Sources)
            if (RandomAccess.GetLength(source.Stream.SafeFileHandle) < source.Length)
                throw new IOException("日志被原地截短，范围缺失；请重新查询，不能沿用旧定位。");
    }
    SoftwareLogEntry Decode(Source source, long number, byte[] bytes)
    {
        Interlocked.Increment(ref ScannedLines);
        var raw = new UTF8Encoding(false, true).GetString(bytes).TrimEnd('\r');
        DateTimeOffset? at = null; string level = "信息", origin = "运行";
        try
        {
            using var json = JsonDocument.Parse(raw); var root = json.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("At", out var time) && time.ValueKind == JsonValueKind.String && time.TryGetDateTimeOffset(out var timestamp)) at = timestamp;
                if (root.TryGetProperty("Level", out var l) && l.ValueKind == JsonValueKind.String) level = l.GetString() ?? level;
                if (root.TryGetProperty("Source", out var o) && o.ValueKind == JsonValueKind.String) origin = o.GetString() ?? origin;
                else if (root.TryGetProperty("Application", out var a) && a.ValueKind == JsonValueKind.String) origin = a.GetString() ?? origin;
            }
            else level = "无法解析";
        }
        catch (JsonException) { level = "无法解析"; }
        return new(new(source.Id, number, Convert.ToHexString(SHA256.HashData(bytes)), source.Name), origin, at, level, Redact(raw));
    }
    async Task<LogIndex> IndexAsync(Selection s, CancellationToken token)
    {
        if (Volatile.Read(ref s.Index) is { } ready) return ready;
        await s.IndexGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (s.Index is { } existing) return existing;
            List<Row> rows = []; List<int> matches = []; var matcher = Matcher(s.Query);
            DateTimeOffset? first = null, last = null; byte[] buffer = new byte[16384];
            ValidateLengths(s);
            for (var sourceIndex = 0; sourceIndex < s.Sources.Length; sourceIndex++)
            {
                var source = s.Sources[sourceIndex]; BeforeSourceRead?.Invoke(); token.ThrowIfCancellationRequested();
                long offset = 0, start = 0, number = 0; using var line = new MemoryStream();
                while (offset < source.Length)
                {
                    token.ThrowIfCancellationRequested(); management.Require();
                    var count = await RandomAccess.ReadAsync(source.Stream.SafeFileHandle, buffer.AsMemory(0, (int)Math.Min(buffer.Length, source.Length - offset)), offset, token).ConfigureAwait(false);
                    if (count == 0) throw new IOException("原日志范围已缺失，请重新查询。");
                    for (var i = 0; i < count; i++)
                    {
                        if (buffer[i] != 10)
                        {
                            line.WriteByte(buffer[i]);
                            if (line.Length > 262144) throw new IOException("单条日志超过256KiB，未静默截断；请检查写入器。");
                            continue;
                        }
                        token.ThrowIfCancellationRequested();
                        if (rows.Count == MaximumIndexedLines)
                        {
                            // Cache capacity must never become a new original-record limit.
                            // Retain the previous bounded streaming behavior for very dense files.
                            token.ThrowIfCancellationRequested(); management.Require();
                            var streaming = new LogIndex([], [], true); Volatile.Write(ref s.Index, streaming); return streaming;
                        }
                        var bytes = line.ToArray(); line.SetLength(0); var entry = Decode(source, ++number, bytes);
                        if (entry.At is { } at) { first = first is null || at < first ? at : first; last = last is null || at > last ? at : last; }
                        if (Match(entry, s.Query, matcher)) matches.Add(rows.Count);
                        rows.Add(new(sourceIndex, start, bytes.Length, number, entry.Location.Hash)); start = offset + i + 1;
                    }
                    offset += count;
                }
                // A concurrently-written incomplete tail is outside this snapshot.
            }
            token.ThrowIfCancellationRequested(); management.Require();
            var result = new LogIndex(rows.ToArray(), matches.ToArray());
            s.Earliest = first; s.Latest = last; Volatile.Write(ref s.Index, result); return result;
        }
        finally { s.IndexGate.Release(); }
    }
    Action AuthorityCheck(CancellationToken token)
    {
        var checkedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        return () =>
        {
            token.ThrowIfCancellationRequested();
            if (System.Diagnostics.Stopwatch.GetElapsedTime(checkedAt) < TimeSpan.FromMilliseconds(100)) return;
            management.Require(); checkedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        };
    }
    async Task<SoftwareLogEntry> ReadRow(Selection s, Row row, CancellationToken token)
    {
        BeforeSourceRead?.Invoke(); token.ThrowIfCancellationRequested();
        var source = s.Sources[row.Source]; var bytes = new byte[row.Bytes + 1]; int read = 0;
        while (read < bytes.Length)
        {
            var count = await RandomAccess.ReadAsync(source.Stream.SafeFileHandle, bytes.AsMemory(read), row.Offset + read, token).ConfigureAwait(false);
            if (count == 0) throw new IOException("原日志范围已缺失，请重新查询。");
            read += count;
        }
        token.ThrowIfCancellationRequested();
        if (bytes[^1] != 10 || Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, row.Bytes))) != row.Hash)
            throw new IOException("原日志位置内容已变化，不能显示错位内容。");
        return Decode(source, row.Number, bytes[..row.Bytes]);
    }
    async IAsyncEnumerable<SoftwareLogEntry> Lines(Selection s, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        var index = await IndexAsync(s, token).ConfigureAwait(false); ValidateLengths(s);
        if (index.Streaming) { await foreach (var entry in StreamLines(s, token)) yield return entry; yield break; }
        var check = AuthorityCheck(token);
        foreach (var row in index.Rows) { check(); yield return await ReadRow(s, row, token).ConfigureAwait(false); }
    }
}
