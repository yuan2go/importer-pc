using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Ipc.Platform;

public static partial class ManagementModeBroker
{
    internal sealed class InstallationTrace(string service, string root, Action<ManagementServiceInstallationDiagnostic>? observer, string? sourceApp = null)
    {
        Exception? pendingDiagnostic;
        public string? Executable { get; set; }
        public T Run<T>(string stage, string api, Func<T> action)
        {
            try
            {
                RequireDiagnostics(); Emit(stage, api, "Started");
                var result = action();
                // Return ownership of a newly created native handle before reporting
                // a journal failure; the enclosing registration can then recover it.
                try { Emit(stage, api, "Completed"); }
                catch (Exception diagnosticError) { pendingDiagnostic = diagnosticError; }
                return result;
            }
            catch (Exception cause)
            {
                var failure = cause as ManagementServiceInstallationException ?? new ManagementServiceInstallationException(stage, cause.Data["WindowsApi"] as string ?? api, service, root, cause);
                Failed(failure.Stage, failure.Api, failure); throw failure;
            }
        }
        internal void RequireDiagnostics()
        {
            if (pendingDiagnostic is { } error)
                throw new ManagementServiceInstallationException("WriteServiceDiagnostic", "AppendServiceDiagnostic", service, root, error);
        }
        public void Run(string stage, string api, Action action) => Run(stage, api, () => { action(); return true; });
        void Emit(string stage, string api, string state, Exception? error = null) => observer?.Invoke(new(DateTimeOffset.UtcNow,
            stage, api, state, service, root, error is null ? null : WindowsInstallationError.Capture(error), sourceApp, Executable));
        void Failed(string stage, string api, Exception error)
        {
            try { Emit(stage, api, "Failed", error); }
            catch (Exception diagnosticError) { error.Data["DiagnosticWriteFailure"] = WindowsInstallationError.Safe(diagnosticError.ToString()); }
        }
        internal void Recover(Action action, ManagementServiceInstallationException original)
        {
            // Diagnostic storage must not prevent restoring the previous service.
            try
            {
                try { Emit("RestorePreviousService", "RestoreServiceRegistration", "Started"); }
                catch (Exception diagnosticError) { original.Data["DiagnosticWriteFailure"] = WindowsInstallationError.Safe(diagnosticError.ToString()); }
                action();
                try { Emit("RestorePreviousService", "RestoreServiceRegistration", "Completed"); }
                catch (Exception diagnosticError) { original.Data["DiagnosticWriteFailure"] = WindowsInstallationError.Safe(diagnosticError.ToString()); }
            }
            catch (Exception recovery)
            {
                var combined = new ManagementServiceInstallationException(original.Stage, original.Api, service, root, original, recovery);
                Failed(original.Stage, original.Api, combined); throw combined;
            }
        }
    }
    static Win32Exception NativeFailure(string api, int? captured = null)
    {
        var code = captured ?? Marshal.GetLastWin32Error();
        var error = new Win32Exception(code); error.Data["WindowsApi"] = api; return error;
    }
}
