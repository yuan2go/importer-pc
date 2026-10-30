using System.ComponentModel;

namespace Ipc.Platform;

// Diagnostic copies only. Never serialize maintenance request/password payloads.
public sealed record WindowsInstallationError(string Type, string Message, int HResult, int? NativeErrorCode,
    string? Stage, string? Api, string? Stack, WindowsInstallationError? Cause, WindowsInstallationError? RecoveryFailure, string? DiagnosticWriteFailure = null)
{
    public static WindowsInstallationError Capture(Exception error) => Capture(error, 0);
    static WindowsInstallationError Capture(Exception error, int depth)
    {
        var service = error as ManagementServiceInstallationException;
        return new(error.GetType().FullName ?? error.GetType().Name, Safe(error.Message), error.HResult,
            NativeCode(error), service?.Stage, service?.Api ?? error.Data["WindowsApi"] as string,
            Safe(error.StackTrace ?? ""), depth < 4 && error.InnerException is { } inner ? Capture(inner, depth + 1) : null,
            depth < 4 && service?.RecoveryFailure is { } recovery ? Capture(recovery, depth + 1) : null,
            error.Data["DiagnosticWriteFailure"] is string diagnostic ? Safe(diagnostic) : null);
    }
    internal static int? NativeCode(Exception error) => error is Win32Exception native ? native.NativeErrorCode :
        error.InnerException is { } inner ? NativeCode(inner) : null;
    internal static string Safe(string value)
    {
        try { return DiagnosticText.Protect(value[..Math.Min(value.Length, 16000)]); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return "诊断脱敏超时；原操作结果需核对。"; }
    }
}

public sealed class ManagementServiceInstallationException : IOException
{
    public string Stage { get; }
    public string Api { get; }
    public string Service { get; }
    public string DataRoot { get; }
    public Exception? RecoveryFailure { get; }
    internal ManagementServiceInstallationException(string stage, string api, string service, string dataRoot, Exception cause, Exception? recovery = null)
        : base(Describe(stage, api, service, dataRoot, cause, recovery), cause)
    { Stage = stage; Api = api; Service = service; DataRoot = dataRoot; RecoveryFailure = recovery; }
    static string Describe(string stage, string api, string service, string root, Exception cause, Exception? recovery)
    {
        var code = WindowsInstallationError.NativeCode(cause);
        return WindowsInstallationError.Safe($"管理授权服务安装失败；阶段={stage}；API={api}；" +
            (code is { } n ? $"Win32={n} (0x{n:X})；" : "") + $"服务={service}；数据目录={root}；原因：{cause.Message}" +
            (recovery is null ? "" : $"；恢复原服务也失败：{recovery.Message}。原失败与恢复失败均已保留，需核对原服务状态。"));
    }
}

public sealed record ManagementServiceInstallationDiagnostic(DateTimeOffset At, string Stage, string Api, string State,
    string Service, string DataRoot, WindowsInstallationError? Error = null, string? SourceApp = null, string? Executable = null);
