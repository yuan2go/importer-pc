namespace Ipc.Application.Contracts;

// Optional additive capability; existing history/query contracts remain pinned reads.
public interface IRuntimeDiagnosticsControl
{
    Task<RuntimeDiagnosticSettings> DiagnosticSettingsAsync();
    Task<RuntimeDiagnosticSettings> SetDebugLoggingAsync(bool enabled);
    Task<RuntimeDiagnosticBatch> ReadDiagnosticTailAsync(Guid? stream, long? afterSequence, CancellationToken token);
}
public sealed record RuntimeDiagnosticSettings(bool DebugEnabled, long Revision, DateTimeOffset ChangedAt);
public sealed record RuntimeDiagnosticEntry(long Sequence, DateTimeOffset WrittenAt, string Text);
public sealed record RuntimeDiagnosticBatch(Guid Stream, long Cursor, bool HasMore, long Missed,
    long DebugDropped, RuntimeDiagnosticEntry[] Entries, RuntimeDiagnosticSettings Settings);
