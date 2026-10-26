using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Ipc.Platform;

// Last-chance diagnostics only. No business repair, replay or resource release.
public sealed class CrashDiagnostics : IDisposable
{
    public const int FatalExitCode = unchecked((int)0xE0000014);
    readonly string directory, version, sourceCommit;
    sealed record Context(string Instance, string Epochs, string RecentState);
    Context context = new("not-started", "not-started", "startup");
    string? progressFile;
    long unobserved, lastUnobservedWrite;
    int fatal, disposed;
    public long UnobservedCount => Interlocked.Read(ref unobserved);
    public string? LastWriteFailure { get; private set; }
    public CrashDiagnostics(string directory, string version, string sourceCommit)
    {
        this.directory = Path.GetFullPath(directory); this.version = version; this.sourceCommit = sourceCommit;
        AppDomain.CurrentDomain.UnhandledException += DomainUnhandled;
        TaskScheduler.UnobservedTaskException += TaskUnobserved;
    }
    public void SetContext(string instance, string epochs, string recentState)
        => Volatile.Write(ref context, new(Clip(instance, 256), Clip(epochs, 8192), Clip(recentState, 8192)));
    public void SetProgressFile(string path) => Volatile.Write(ref progressFile, Path.GetFullPath(path));
    static string Clip(string value, int length) => value.Length <= length ? value : value[..length] + " [truncated diagnostic preview]";
    string? LatestProgress()
    {
        var path = Volatile.Read(ref progressFile); if (path is null) return null;
        try
        {
            DeploymentPaths.RequireOrdinaryAncestors(path);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length > 131072) return "Progress preview exceeds diagnostic budget";
            using var reader = new StreamReader(file); return Clip(reader.ReadToEnd(), 32768);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "Progress unavailable: " + ex.GetType().Name; }
    }
    bool Record(string kind, Exception error, bool isFatal)
    {
        try
        {
            var current = Volatile.Read(ref context);
            RuntimeJournal.AppendMetadata(directory, new { At = DateTimeOffset.UtcNow, Source = "进程", Level = isFatal ? "致命" : "错误", Event = kind,
                Version = version, SourceCommit = sourceCommit, ProcessId = Environment.ProcessId, current.Instance, current.Epochs, current.RecentState,
                LatestProgress = LatestProgress(), UnobservedTaskCount = UnobservedCount, FatalExitCode,
                Exception = Clip(error.ToString(), 16384), Policy = "Diagnostics only; no replay or in-process recovery" });
            LastWriteFailure = null; return true; // AppendMetadata performs Flush(true).
        }
        catch (Exception ex) { LastWriteFailure = ex.GetType().Name; return false; }
    }
    void DomainUnhandled(object sender, UnhandledExceptionEventArgs e)
        => Fatal("AppDomain.UnhandledException", e.ExceptionObject as Exception ?? new Exception("Non-Exception unhandled object"));
    void TaskUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Interlocked.Increment(ref unobserved);
        // A burst cannot make diagnostics an unbounded write workload. Counts are
        // retained; one sampled record per second includes the cumulative count.
        var now = System.Diagnostics.Stopwatch.GetTimestamp(); var prior = Interlocked.Read(ref lastUnobservedWrite);
        if (prior != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(prior, now) < TimeSpan.FromSeconds(1)) return;
        if (Interlocked.CompareExchange(ref lastUnobservedWrite, now, prior) == prior) Record("TaskScheduler.UnobservedTaskException", e.Exception, false);
    }
    [DoesNotReturn]
    public void Fatal(string kind, Exception error)
    {
        if (Interlocked.Exchange(ref fatal, 1) != 0) Environment.FailFast("Recursive fatal exception", error);
        var flushed = Record(kind, error, true);
        if (OperatingSystem.IsWindows()) FailFastWindows(flushed);
        Environment.FailFast("Fatal desktop exception; diagnostics flushed=" + flushed, error);
    }
    [SupportedOSPlatform("windows"), DoesNotReturn]
    static void FailFastWindows(bool flushed)
    {
        var record = new ExceptionRecord { Code = unchecked((uint)FatalExitCode), Flags = 1, Count = 1, Parameters = new UIntPtr[15] };
        record.Parameters[0] = flushed ? (UIntPtr)1 : UIntPtr.Zero;
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ExceptionRecord>());
        Marshal.StructureToPtr(record, memory, false);
        RaiseFailFastException(memory, IntPtr.Zero, 1); // Explicit code; WER gets the native crash.
        Environment.Exit(FatalExitCode); // Defensive only: the API never returns.
        throw new InvalidOperationException("Fail-fast unexpectedly returned");
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ExceptionRecord
    {
        public uint Code, Flags; public IntPtr Nested, Address; public uint Count;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 15)] public UIntPtr[] Parameters;
    }
    [DllImport("kernel32.dll")]
    static extern void RaiseFailFastException(IntPtr exceptionRecord, IntPtr context, uint flags);
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        AppDomain.CurrentDomain.UnhandledException -= DomainUnhandled;
        TaskScheduler.UnobservedTaskException -= TaskUnobserved;
    }
}
