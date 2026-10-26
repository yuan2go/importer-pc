using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IRuntimeDiagnosticsControl
{
    readonly object diagnosticSettingsSync = new();
    RuntimeDiagnosticSettings diagnosticSettings = new(false, 0, DateTimeOffset.UtcNow);
    RuntimeJournal DiagnosticJournal => journal is { } current && !stopped && !stopping.IsCancellationRequested
        ? current : throw new InvalidOperationException("软件正在启动或停止，暂不能使用运行日志。");
    public Task<RuntimeDiagnosticSettings> DiagnosticSettingsAsync() => OwnedAdministration(token =>
    {
        Management.Require(); token.ThrowIfCancellationRequested(); _ = DiagnosticJournal;
        return Task.FromResult(Volatile.Read(ref diagnosticSettings));
    });
    public Task<RuntimeDiagnosticSettings> SetDebugLoggingAsync(bool enabled) => OwnedAdministration(token =>
        Task.FromResult(Management.Commit(() =>
        {
            token.ThrowIfCancellationRequested(); _ = DiagnosticJournal;
            lock (diagnosticSettingsSync)
            {
                var prior = diagnosticSettings;
                if (prior.DebugEnabled == enabled) return prior;
                var next = new RuntimeDiagnosticSettings(enabled, checked(prior.Revision + 1), DateTimeOffset.UtcNow);
                Volatile.Write(ref diagnosticSettings, next);
                Diagnostic("诊断日志", enabled ? "调试模式已开启" : "调试模式已关闭", detail: "立即生效；仅当前运行实例，重启后关闭。修订=" + next.Revision);
                return next;
            }
        })));
    public Task<RuntimeDiagnosticBatch> ReadDiagnosticTailAsync(Guid? stream, long? afterSequence, CancellationToken token)
        => OwnedRead(t =>
        {
            var authority = Management.Require(); t.ThrowIfCancellationRequested();
            var batch = DiagnosticJournal.ReadTail(stream, afterSequence);
            authority.ThrowIfCancellationRequested(); Management.Require();
            return Task.FromResult(new RuntimeDiagnosticBatch(batch.Stream, batch.Cursor, batch.HasMore, batch.Missed, batch.DebugDropped,
                batch.Entries.Select(e => new RuntimeDiagnosticEntry(e.Sequence, e.WrittenAt, e.Text)).ToArray(), Volatile.Read(ref diagnosticSettings)));
        }, token);
    void DebugDiagnostic(string source, string action, Func<object> detail)
    {
        if (!Volatile.Read(ref diagnosticSettings).DebugEnabled || journal is not { } current) return;
        current.WriteDebug(new { At = DateTimeOffset.UtcNow, Application = Id, Source = source, Level = "调试", Event = action, Detail = detail() });
    }
    static object? DiagnosticResponse(IntegrationHttpResponse? response)
    {
        if (response is null) return null;
        var length = Math.Min(response.BodyBase64.Length, 10920); length -= length % 4;
        string body;
        try { body = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(response.BodyBase64[..length])); }
        catch (FormatException) { body = "原响应编码无效，请查看原始回执。"; }
        return new { response.StatusCode, response.BodyHash, response.Complete, Body = DiagnosticFragment(body),
            EncodedCharacters = response.BodyBase64.Length, Truncated = length != response.BodyBase64.Length };
    }
    static object DiagnosticFragment(string? text, int maximum = 8192)
    {
        if (text is null) return new { Text = (string?)null, Characters = 0, Truncated = false };
        var length = Math.Min(text.Length, maximum);
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
        return new { Text = text[..length], Characters = text.Length, Truncated = length != text.Length };
    }
}
