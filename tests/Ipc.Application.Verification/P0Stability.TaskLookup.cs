using System.Reflection;
using System.Runtime.InteropServices;
using Ipc.Platform;

static class P0TaskLookup
{
    public static void Run(string root)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS TASK-LOOKUP " + message); }
        foreach (var missing in new Exception[] { new FileNotFoundException("Synthetic absent task"), new COMException("Synthetic Win32 absence", unchecked((int)0x80070002)), new COMException("Synthetic scheduler absence", unchecked((int)0x8004130F)) })
            Check(DesktopRecoveryInstallation.FindRegisteredTask(() => throw missing) is null, "known task absence recognized: " + missing.GetType().Name + "/" + missing.HResult.ToString("X8"));
        foreach (var denied in new Exception[] { new UnauthorizedAccessException("Synthetic denied lookup"), new COMException("Synthetic access denied", unchecked((int)0x80070005)), new COMException("Synthetic service unavailable", unchecked((int)0x80041315)), new COMException("Synthetic missing folder", unchecked((int)0x80070003)), new IOException("Synthetic I/O failure"), new FileNotFoundException("Synthetic required binary missing", "D:\\Synthetic\\required.dll") })
        {
            try { DesktopRecoveryInstallation.FindRegisteredTask(() => throw denied); throw new InvalidOperationException("Unexpected lookup error swallowed"); }
            catch (Exception actual) when (ReferenceEquals(actual, denied)) { Check(true, "non-absence lookup error propagates unchanged: " + denied.GetType().Name + "/" + denied.HResult.ToString("X8")); }
        }
        var existing = new object();
        Check(ReferenceEquals(DesktopRecoveryInstallation.FindRegisteredTask(() => existing), existing), "existing task identity is preserved");
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        object? service = null, folder = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!)!;
            ((dynamic)service).Connect(); folder = ((dynamic)service).GetFolder("\\");
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var location = new DeploymentLocation("lookup-" + Guid.NewGuid().ToString("N"), new string('a', 40), root, root, root, identity.User!.Value)
                { Recovery = new(Enabled: false) };
            var plan = DesktopRecoveryInstallation.Plan(location);
            Exception? observed = null;
            try { _ = ((dynamic)folder).GetTask(plan.TaskName); }
            catch (Exception error) when (error is COMException or FileNotFoundException) { observed = error; }
            Check(observed is FileNotFoundException && (uint)observed.HResult == 0x80070002,
                "real Task Scheduler maps an absent task to FileNotFoundException/0x80070002; no task created");
            Check(DesktopRecoveryInstallation.FindRegisteredTask(() => (object)((dynamic)folder).GetTask(plan.TaskName)) is null,
                "production lookup recognizes the real native missing task");
            var task = typeof(DesktopRecoveryInstallation).GetMethod("Task", BindingFlags.NonPublic | BindingFlags.Static)!;
            for (var i = 0; i < 2; i++)
            {
                try { task.Invoke(null, [plan, location, true]); }
                catch (TargetInvocationException error) when (error.InnerException is { } inner) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw(); }
            }
            Check(DesktopRecoveryInstallation.FindRegisteredTask(() => (object)((dynamic)folder).GetTask(plan.TaskName)) is null,
                "disabled recovery projection is idempotent when no task exists; no task registered or started");
        }
        finally { foreach (var item in new[] { folder, service }) if (item is not null && Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item); }
    }
}
