namespace Ipc.Platform;

public sealed record JournalTailEntry(long Sequence, DateTimeOffset WrittenAt, string Text);
public sealed record JournalTail(Guid Stream, long Cursor, bool HasMore, long Missed, long DebugDropped, JournalTailEntry[] Entries);

public sealed partial class RuntimeJournal
{
    readonly object tailSync = new();
    readonly Queue<JournalTailEntry> tail = new();
    readonly Guid tailStream = Guid.NewGuid();
    long tailSequence, debugDropped;
    int tailCharacters;
    const int TailCharacterBudget = 1_048_576, TailEntryBudget = 512;
    void PublishTail(string line, DateTimeOffset writtenAt)
    {
        // This is only a bounded view of successfully appended diagnostics.
        // The file remains the export/history authority, never this cache.
        if (line.Length > 65536)
        {
            var length = 65000;
            if (char.IsHighSurrogate(line[length - 1])) length--;
            line = line[..length] + "\n【实时显示已省略后续内容，完整原文请按时间范围查询并导出。】";
        }
        lock (tailSync)
        {
            tail.Enqueue(new(++tailSequence, writtenAt, line)); tailCharacters += line.Length;
            while (tail.Count > TailEntryBudget || tailCharacters > TailCharacterBudget) tailCharacters -= tail.Dequeue().Text.Length;
        }
    }
    public JournalTail ReadTail(Guid? stream, long? afterSequence)
    {
        if (afterSequence is < 0) throw new ArgumentOutOfRangeException(nameof(afterSequence));
        lock (tailSync)
        {
            var after = stream == tailStream ? afterSequence : null;
            if (after > tailSequence) throw new ArgumentOutOfRangeException(nameof(afterSequence));
            var oldest = tail.Count == 0 ? tailSequence + 1 : tail.Peek().Sequence;
            var missed = after is { } cursor ? Math.Max(0, oldest - cursor - 1) : 0;
            var entries = after is { } position ? tail.Where(e => e.Sequence > position).Take(200).ToArray() : tail.TakeLast(200).ToArray();
            var last = entries.LastOrDefault()?.Sequence ?? tailSequence;
            return new(tailStream, last, last < tailSequence, missed, Interlocked.Read(ref debugDropped), entries);
        }
    }
    public void WriteDebug(object metadata)
    {
        // Reserve ordinary diagnostic capacity. Optional traces never wait on I/O
        // and never convert debug pressure into a business success/failure.
        if (queue.Reader.Count >= 128 || !queue.Writer.TryWrite(metadata)) Interlocked.Increment(ref debugDropped);
    }
}
