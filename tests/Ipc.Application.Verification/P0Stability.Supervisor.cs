using System.Diagnostics;
using System.Xml.Linq;
using Ipc.Platform;

static class P0Supervisor
{
    static void Check(bool yes, string what) { if (!yes) throw new Exception("FAIL ST14 " + what); Console.WriteLine("PASS ST14 " + what); }
    public static void ValidateTaskSchema(string root)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var location = new DeploymentLocation("sportmaster", new string('a', 40), root, root, root, identity.User!.Value) { Recovery = new() };
        var plan = DesktopRecoveryInstallation.Plan(location);
        object? service = null, folder = null, result = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!)!; ((dynamic)service).Connect();
            folder = ((dynamic)service).GetFolder("\\");
            // TASK_VALIDATE_ONLY=1: parses the real Windows schema; never registers a task.
            result = ((dynamic)folder).RegisterTask("ImporterPc-Validation-" + Wire.Id(), plan.TaskXml, 1, location.RuntimeAccountSid, null, 3, null);
            Check(true, "Windows Task Scheduler TASK_VALIDATE_ONLY accepted generated XML; no task registered");
        }
        finally { foreach (var item in new[] { result, folder, service }) if (item is not null && System.Runtime.InteropServices.Marshal.IsComObject(item)) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(item); }
    }
    public static async Task Run(string root)
    {
        DeploymentLocation Location(string name, DesktopRecoveryOptions? options = null) => new("sportmaster", new string('a', 40),
            Path.Combine(root, name, "release"), Path.Combine(root, name, "config"), Path.Combine(root, name, "data"), "S-1-5-21-123-456-789-1001")
            { Recovery = options ?? new(RestartDelaySeconds: 1, MaximumRestarts: 2, HealthyResetSeconds: 60) };
        var location = Location("plan"); var plan = DesktopRecoveryInstallation.Plan(location);
        var xml = XDocument.Parse(plan.TaskXml); XNamespace ns = xml.Root!.Name.Namespace;
        Check(xml.Descendants(ns + "LogonType").Single().Value == "InteractiveToken" && xml.Descendants(ns + "RunLevel").Single().Value == "LeastPrivilege" &&
            xml.Descendants(ns + "UserId").All(x => x.Value == location.RuntimeAccountSid), "task is session-bound, least privilege and password-free");
        Check(xml.Descendants(ns + "Interval").Single().Value == "PT1M" && xml.Descendants(ns + "Count").Single().Value == "2" &&
            xml.Descendants(ns + "ExecutionTimeLimit").Single().Value == "PT0S", "task wrapper retry obeys Windows minimum minute and finite count");
        Check(plan.TaskName == DesktopRecoveryInstallation.Plan(location with { DataRoot = Path.Combine(root, "moved") }).TaskName &&
            plan.SystemEntries.Any(x => x.StartsWith("ScheduledTask:")) && plan.SystemEntries.Any(x => x.StartsWith("HKLM64:")) &&
            location.Recovery!.DumpCount == 2 && location.Recovery.DumpType == 1, "migration keeps one task identity; mini-dump and uninstall targets bounded");
        Check(ManagementModeBroker.RecoveryResetSeconds == 86400 && ManagementModeBroker.RecoveryActions.SequenceEqual(new[] { (1u, 5000u), (1u, 30000u), (0u, 0u) }),
            "SCM has daily reset and final NONE instead of infinitely repeating restart");
        var legacy = location with { Recovery = null };
        Check(!Wire.Encode(legacy).Contains("Recovery"), "old deployment bytes omit absent recovery property");
        var dumps = Path.Combine(root, "owned-dumps"); Directory.CreateDirectory(dumps);
        File.WriteAllText(Path.Combine(dumps, "Ipc.Desktop.exe.123.dmp"), "synthetic dump");
        File.WriteAllText(Path.Combine(dumps, "foreign-evidence.dmp"), "retain synthetic foreign file");
        File.WriteAllText(Path.Combine(dumps, "Ipc.Desktop.exe.dmp"), "retain malformed foreign name");
        DesktopRecoveryInstallation.RemoveOwnedDumps(dumps);
        Check(!File.Exists(Path.Combine(dumps, "Ipc.Desktop.exe.123.dmp")) && File.Exists(Path.Combine(dumps, "foreign-evidence.dmp")) && File.Exists(Path.Combine(dumps, "Ipc.Desktop.exe.dmp")), "uninstall removes only owned WER names and preserves foreign evidence");
        Process Launch(string mode)
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var value in new[] { typeof(P0Supervisor).Assembly.Location, root, "--p0-supervised-child", mode }) start.ArgumentList.Add(value);
            var child = Process.Start(start)!;
            var ready = Stopwatch.GetTimestamp();
            while (child.MainModule?.FileName is null && !child.HasExited && Stopwatch.GetElapsedTime(ready) < TimeSpan.FromSeconds(2)) Thread.Sleep(5);
            return child;
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        location = Location("loop"); var launches = 0;
        var result = await DesktopRecoverySupervisor.RunAsync(location, () => location, launch: _ => { launches++; return Launch("--fail"); }, cancellationToken: deadline.Token);
        Check(result == 77 && launches == 3 && File.Exists(Path.Combine(DesktopRecoverySupervisor.StateDirectory(location), "alert.json")), "three abnormal exits exhaust two restarts and persist visible guard");
        result = await DesktopRecoverySupervisor.RunAsync(location, () => location, launch: _ => throw new Exception("unexpected replay"), cancellationToken: deadline.Token);
        Check(result == 77 && launches == 3, "new watchdog process cannot reset persisted crash-loop budget");
        DesktopRecoverySupervisor.ResetAfterAuthorizedRepair(location);
        Check(!DesktopRecoverySupervisor.IsBlocked(location) && !File.Exists(Path.Combine(DesktopRecoverySupervisor.StateDirectory(location), "alert.json")), "authorized repair can reset a known exhausted budget after client exit");
        location = Location("kill"); launches = 0; Process? killed = null;
        var watch = DesktopRecoverySupervisor.RunAsync(location, () => location, launch: _ => { launches++; var child = Launch(launches == 1 ? "--sleep" : "--normal"); if (launches == 1) killed = child; return child; }, cancellationToken: deadline.Token);
        while (killed is null) await Task.Delay(10);
        await Task.Delay(200); var killAt = Stopwatch.GetTimestamp(); killed.Kill();
        result = await watch;
        Check(result == 0 && launches == 2 && Stopwatch.GetElapsedTime(killAt) < TimeSpan.FromSeconds(5), "strong-killed synthetic client restarts within configured delay; normal close ends supervision");
        location = Location("adopt"); Process? adopted = null; using var interrupted = new CancellationTokenSource();
        watch = DesktopRecoverySupervisor.RunAsync(location, () => location, launch: _ => adopted = Launch("--sleep"), cancellationToken: interrupted.Token);
        while (adopted is null) await Task.Delay(10);
        var adoptedPid = adopted.Id;
        interrupted.Cancel(); try { await watch; } catch (OperationCanceledException) { }
        using (var original = Process.GetProcessById(adoptedPid))
        {
            watch = DesktopRecoverySupervisor.RunAsync(location, () => location, launch: _ => throw new Exception("second client"), cancellationToken: deadline.Token);
            await Task.Delay(100); Check(!original.HasExited && !watch.IsCompleted, "replacement watchdog adopts exact original PID/start/hash without duplicate client");
            // Stop only this verification-owned child. Let the adopted watcher finish its normal restart/exit path.
            original.Kill();
            // The current registration delegate is replaced by a separate retirement case below; cancellation leaves a durable original.
            deadline.Cancel(); try { await watch; } catch (OperationCanceledException) { }
        }
        location = Location("retired"); var attached = true; Process? retained = null;
        watch = DesktopRecoverySupervisor.RunAsync(location, () => attached ? location : null, launch: _ => retained = Launch("--sleep"));
        while (retained is null) await Task.Delay(10);
        var retainedPid = retained.Id;
        attached = false; result = await watch;
        using (var original = Process.GetProcessById(retainedPid)) { Check(result == 0 && !original.HasExited, "retirement detaches watchdog without killing business process"); original.Kill(); await original.WaitForExitAsync(); }
        location = Location("uncertain"); Directory.CreateDirectory(DesktopRecoverySupervisor.StateDirectory(location));
        ExtensionStore.AtomicWrite(Path.Combine(DesktopRecoverySupervisor.StateDirectory(location), "state.json"), new { Launches = 1, Status = "Launching" });
        result = await DesktopRecoverySupervisor.RunAsync(location, () => location, launch: _ => throw new Exception("uncertain launch repeated"));
        Check(result == 77, "unacknowledged process creation fails closed without a duplicate launch");
        var refused = false; try { DesktopRecoverySupervisor.ResetAfterAuthorizedRepair(location); } catch (InvalidOperationException) { refused = true; }
        Check(refused && DesktopRecoverySupervisor.IsBlocked(location), "ordinary system repair cannot clear an uncertain original launch");
        location = Location("healthy", new(RestartDelaySeconds: 1, MaximumRestarts: 0, HealthyResetSeconds: 60)); launches = 0;
        result = await DesktopRecoverySupervisor.RunAsync(location, () => location, launch: start => { launches++; var process = Launch(launches == 1 ? "--sleep" : "--normal");
            if (launches == 1) _ = Task.Run(async () => { await Task.Delay(1400); using var first = Process.GetProcessById(process.Id); first.Kill(); }); return process; }, clock: new AcceleratedClock());
        Check(result == 0 && launches == 2, "healthy interval resets restart budget using monotonic time despite wall-clock jumps");
    }
    sealed class AcceleratedClock : TimeProvider
    {
        readonly long origin = Stopwatch.GetTimestamp();
        public override long GetTimestamp() => (Stopwatch.GetTimestamp() - origin) * 120;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddYears(DateTime.UtcNow.Second % 2 == 0 ? 5 : -5);
    }
}
