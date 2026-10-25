using System.Diagnostics;
using System.Runtime.Versioning;

namespace Ipc.Platform;

// A process owner, never a business sender. Persistent launch accounting prevents
// a restarted watchdog from resetting the client's crash-loop budget.
public static class DesktopRecoverySupervisor
{
    public const int LoopBlockedExitCode = 77;
    sealed record State(int Launches, string Status, int? ChildPid = null, long? ChildStartedTicks = null, string? SourceCommit = null, string? ExecutableSha256 = null, bool LaunchUncertain = false);
    public static string StateDirectory(DeploymentLocation location) => Path.Combine(location.DataRoot, "runtime-recovery", location.ApplicationId);
    static string StateFile(DeploymentLocation location) => Path.Combine(StateDirectory(location), "state.json");
    static string AlertFile(DeploymentLocation location) => Path.Combine(StateDirectory(location), "alert.json");
    static State Read(DeploymentLocation location)
    {
        var path = StateFile(location); DeploymentPaths.RequireOrdinaryAncestors(path);
        if (!File.Exists(path)) return new(0, "New");
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Recovery state exceeds budget");
        var state = Wire.Decode<State>(File.ReadAllText(path));
        if (state.Launches is < 0 or > 11 || state.Status is not ("New" or "Launching" or "Running" or "Exited" or "Blocked")) throw new InvalidDataException("Recovery state invalid; inspect original launch");
        return state.Status == "Launching" ? state with { LaunchUncertain = true } : state;
    }
    static void Save(DeploymentLocation location, State state) => ExtensionStore.AtomicWrite(StateFile(location), state);
    public static bool IsBlocked(DeploymentLocation location)
    { var state = Read(location); return state.Status is "Blocked" or "Launching"; }
    static int Block(DeploymentLocation location, State state, string reason)
    {
        Save(location, state with { Status = "Blocked" });
        var alert = new { At = DateTimeOffset.UtcNow, Source = "进程监督", Level = "错误", Event = "DesktopCrashLoopBlocked", Reason = reason,
            location.ApplicationId, location.SourceCommit, state.Launches, state.ChildPid, ExitCode = LoopBlockedExitCode, Policy = "No more client launches; no replay or Unknown changes" };
        ExtensionStore.AtomicWrite(AlertFile(location), alert);
        RuntimeJournal.AppendMetadata(Path.Combine(StateDirectory(location), "diagnostics"), alert);
        return LoopBlockedExitCode;
    }
    internal static void ResetAfterAuthorizedRepair(DeploymentLocation location)
    {
        using var owner = new ResourceLease(StateDirectory(location), ["supervisor"]);
        var state = Read(location);
        if (state.LaunchUncertain) throw new InvalidOperationException("原启动结果不明，先核对并保全原进程；常规修复不能清除此围栏。");
        if (state.Status == "Running" && state.ChildPid is { } pid)
            try { using var process = Process.GetProcessById(pid); if (!process.HasExited) throw new InvalidOperationException("Original client is still active; recovery budget not reset"); }
            catch (ArgumentException) { }
        Save(location, new(0, "New"));
        if (File.Exists(AlertFile(location))) File.Delete(AlertFile(location));
    }
    [SupportedOSPlatform("windows")]
    public static bool StartForInstalledDesktop(DeploymentLocation location, string[] presentationArguments)
    {
        if (location.Recovery?.Enabled != true) return false;
        MachineRuntimePolicy.RequireRuntimeIdentity();
        if (IsBlocked(location)) throw new InvalidOperationException("连续启动失败已停止自动重启；请由安装维护修复系统入口。告警：" + AlertFile(location));
        ValidatePresentationArguments(presentationArguments);
        ExtensionStore.AtomicWrite(Path.Combine(StateDirectory(location), "presentation.json"), presentationArguments);
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(8))
        {
            DesktopRecoveryInstallation.RunRegistered(location);
            Thread.Sleep(100); // Caller uses Task.Run; never block the WPF dispatcher.
            var state = Read(location);
            if (state.Status is "Blocked" or "Launching" && Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(2))
                throw new InvalidOperationException("监督启动结果需核对；保留原进程责任。告警：" + AlertFile(location));
            if (OwnerWillRun(location, state)) return true;
        }
        throw new TimeoutException("计划任务未确认客户端启动；请检查原进程与监督告警，不能创建第二实例。");
    }
    static void ValidatePresentationArguments(string[] args)
    {
        if (args.Length > 6 || args.Length % 2 != 0) throw new ArgumentException("Invalid supervised presentation arguments");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
            if (args[i] is not ("--view" or "--layout" or "--ui") || !seen.Add(args[i]) || args[i + 1].Length is < 1 or > 128 || args[i + 1].Any(char.IsControl))
                throw new ArgumentException("Invalid supervised presentation argument");
    }
    public static async Task<bool> HandleAsync(string[] args)
    {
        if (args.FirstOrDefault() != "--supervise-desktop") return false;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (args.Length < 2 || args[1] is not ("sportmaster" or "direct-plc" or "wcs-to-plc")) throw new ArgumentException("Unknown supervised application");
        var presentation = args[2..]; ValidatePresentationArguments(presentation);
        MachineRuntimePolicy.RequireRuntimeIdentity();
        var location = WindowsDeploymentSetup.Current(args[1]) ?? throw new InvalidDataException("No installed recovery owner");
        if (location.LifecycleState != "Installed" || location.Recovery?.Enabled != true) { Environment.ExitCode = 0; return true; }
        if (!string.Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar),
            Path.Combine(location.ReleaseRoot, "desktop"), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Supervisor belongs to a different release");
        var presentationFile = Path.Combine(StateDirectory(location), "presentation.json"); DeploymentPaths.RequireOrdinaryAncestors(presentationFile);
        if (File.Exists(presentationFile))
        {
            if (new FileInfo(presentationFile).Length > 4096) throw new InvalidDataException("Presentation request exceeds budget");
            presentation = Wire.Decode<string[]>(File.ReadAllText(presentationFile)); ValidatePresentationArguments(presentation); File.Delete(presentationFile);
        }
        Environment.ExitCode = await RunAsync(location, () => WindowsDeploymentSetup.Current(location.ApplicationId), presentation).ConfigureAwait(false);
        return true;
    }
    static bool OwnerWillRun(DeploymentLocation location, State state)
    {
        if (state.SourceCommit != location.SourceCommit) return false;
        if (state.Status == "Exited") return true; // SourceCommit is cleared on a normal close; otherwise bounded backoff.
        if (state.Status != "Running" || state.ChildPid is not { } pid) return false;
        try { using var child = Process.GetProcessById(pid); return !child.HasExited && child.StartTime.ToUniversalTime().Ticks == state.ChildStartedTicks; }
        catch (ArgumentException) { return false; }
    }
    internal static async Task<int> RunAsync(DeploymentLocation location, Func<DeploymentLocation?> current, string[]? presentation = null,
        Func<ProcessStartInfo, Process>? launch = null, TimeProvider? clock = null, CancellationToken cancellationToken = default)
    {
        var policy = location.Recovery ?? throw new InvalidDataException("No recovery policy"); policy.Validate();
        clock ??= TimeProvider.System;
        var directory = StateDirectory(location); DeploymentPaths.RequireOrdinaryAncestors(directory);
        ResourceLease owner;
        var ownershipStarted = clock.GetTimestamp();
        while (true)
        {
            try { owner = new(directory, ["supervisor"]); break; }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
            {
                var existing = Read(location);
                if (OwnerWillRun(location, existing)) return 0;
                if (clock.GetElapsedTime(ownershipStarted) > TimeSpan.FromSeconds(5)) throw new IOException("Previous recovery owner has not drained", error);
                await Task.Delay(TimeSpan.FromMilliseconds(100), clock, cancellationToken).ConfigureAwait(false);
            }
        }
        using (owner)
        {
            State state;
            try { state = Read(location); }
            catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException)
            { return Block(location, new(0, "Blocked", LaunchUncertain: true), "Invalid existing launch ledger: " + error.GetType().Name); }
            bool Installed() => current() is { LifecycleState: "Installed" } now && now.ReleaseRoot == location.ReleaseRoot && now.SourceCommit == location.SourceCommit &&
                now.DataRoot == location.DataRoot && now.ConfigurationDirectory == location.ConfigurationDirectory && now.Recovery?.Enabled == true;
            while (Installed())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (state.Status is "Blocked" or "Launching") return Block(location, state, "Original launch outcome unavailable; inspect before relaunch");
                Process? child = null;
                if (state.Status == "Running" && state.ChildPid is { } priorPid)
                {
                    try
                    {
                        child = Process.GetProcessById(priorPid);
                        if (!child.HasExited && (child.StartTime.ToUniversalTime().Ticks != state.ChildStartedTicks || state.SourceCommit != location.SourceCommit ||
                            child.MainModule?.FileName is not { } executable || Wire.FileHash(executable) != state.ExecutableSha256))
                        { child.Dispose(); return Block(location, state, "Saved process identity differs; no second client created"); }
                        if (child.HasExited) { child.Dispose(); child = null; }
                    }
                    catch (ArgumentException) { child = null; }
                }
                if (child is null)
                {
                    if (state.Launches >= policy.MaximumRestarts + 1) return Block(location, state, "Configured restart budget exhausted");
                    state = new(state.Launches + 1, "Launching", SourceCommit: location.SourceCommit, LaunchUncertain: true); Save(location, state);
                    var executable = Path.Combine(location.ReleaseRoot, "desktop", "Ipc.Desktop.exe");
                    var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = location.ReleaseRoot };
                    foreach (var arg in new[] { "--recovery-child", "--config", location.ConfigurationDirectory, "--data-root", location.DataRoot }) start.ArgumentList.Add(arg);
                    foreach (var arg in presentation ?? []) start.ArgumentList.Add(arg);
                    try
                    {
                        child = launch is null ? Process.Start(start) : launch(start);
                        if (child is null) throw new IOException("No client process");
                        state = state with { Status = "Running", ChildPid = child.Id, ChildStartedTicks = child.StartTime.ToUniversalTime().Ticks,
                            ExecutableSha256 = Wire.FileHash(child.MainModule?.FileName ?? executable), LaunchUncertain = false }; Save(location, state);
                    }
                    catch (Exception error)
                    { child?.Dispose(); return Block(location, state, "Client creation outcome requires inspection: " + error.GetType().Name); }
                }
                using (child)
                {
                    var started = clock.GetTimestamp(); var healthyReset = false;
                    using var waitOwner = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var exited = child.WaitForExitAsync(waitOwner.Token);
                    try
                    {
                        while (!exited.IsCompleted)
                        {
                            await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(1), clock, waitOwner.Token)).ConfigureAwait(false);
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!Installed()) return 0; // Installer owns business-stop prerequisites; never kill/replay here.
                            if (!healthyReset && clock.GetElapsedTime(started) >= TimeSpan.FromSeconds(policy.HealthyResetSeconds))
                            { state = state with { Launches = 0 }; Save(location, state); healthyReset = true; }
                        }
                        await exited.ConfigureAwait(false);
                        var exit = child.ExitCode;
                        RuntimeJournal.AppendMetadata(Path.Combine(directory, "diagnostics"), new { At = DateTimeOffset.UtcNow, Source = "进程监督", Event = "DesktopExited", location.SourceCommit, ChildPid = child.Id, ExitCode = exit, state.Launches });
                        state = exit == 0 ? new(0, "Exited") : state with { Status = "Exited", ChildPid = null, ChildStartedTicks = null }; Save(location, state);
                        if (exit == 0) return 0; // Normal close is deliberate, never a restart request.
                    }
                    finally
                    {
                        waitOwner.Cancel(); // Detach the process wait/timer; cancellation never terminates the child.
                        try { await exited.ConfigureAwait(false); }
                        catch (OperationCanceledException) when (waitOwner.IsCancellationRequested) { }
                    }
                }
                if (state.Launches >= policy.MaximumRestarts + 1) return Block(location, state, "Configured restart budget exhausted");
                await Task.Delay(TimeSpan.FromSeconds(policy.RestartDelaySeconds), clock, cancellationToken).ConfigureAwait(false);
            }
            return 0;
        }
    }
}
