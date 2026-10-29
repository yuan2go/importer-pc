using System.Diagnostics;
using System.Runtime.Versioning;

namespace Ipc.Platform;

// A process owner, never a business sender. Persistent launch accounting prevents
// a restarted watchdog from resetting the client's crash-loop budget.
public static partial class DesktopRecoverySupervisor
{
    internal static Action<string>? BeforePersistForVerification { get; set; }
    public const int LoopBlockedExitCode = 77;
    sealed record State(int Launches, string Status, int? ChildPid = null, long? ChildStartedTicks = null, string? SourceCommit = null, string? ExecutableSha256 = null, bool LaunchUncertain = false);
    public static string StateDirectory(DeploymentLocation location) => Path.Combine(location.DataRoot, "runtime-recovery", location.ApplicationId);
    static string StateFile(DeploymentLocation location) => Path.Combine(StateDirectory(location), "state.json");
    static string AlertFile(DeploymentLocation location) => Path.Combine(StateDirectory(location), "alert.json");
    static void RequireDirectory(DeploymentLocation location)
    {
        var directory = StateDirectory(location); DeploymentPaths.RequireOrdinaryAncestors(directory);
        if ((File.GetAttributes(directory) & FileAttributes.Directory) == 0) throw new InvalidDataException("Original recovery directory is not a directory");
    }
    internal static void RequireExistingState(DeploymentLocation location) => _ = Read(location);
    // Called only by the installer after proving there is no prior registration.
    // An existing directory is not evidence of a fresh budget.
    internal static void InitializeNewDeployment(DeploymentLocation location)
    {
        var directory = StateDirectory(location); DeploymentPaths.RequireOrdinaryAncestors(directory);
        var exists = true;
        try { _ = File.GetAttributes(directory); }
        catch (FileNotFoundException) { exists = false; }
        catch (DirectoryNotFoundException) { exists = false; }
        if (exists) { RequireExistingState(location); return; }
        var temporary = directory + ".initial-" + Wire.Id();
        Directory.CreateDirectory(Path.Combine(temporary, "diagnostics"));
        ExtensionStore.AtomicWrite(Path.Combine(temporary, "state.json"), new State(0, "New"), createDirectory: false, overwrite: false);
        Directory.Move(temporary, directory); // Publish a complete initial directory without replacing prior evidence.
    }
    static bool LedgerError(Exception error) => error is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException;
    static int Unreadable(Exception error)
    {
        DiagnosticFailure("Recovery blocked: original launch ledger retained; " + error.GetType().Name);
        return LoopBlockedExitCode;
    }
    static void DiagnosticFailure(string message)
    { try { Console.Error.WriteLine(message); } catch (IOException) { /* State/return code remain authoritative even when stderr is closed. */ } }
    static State Read(DeploymentLocation location)
    {
        RequireDirectory(location);
        var path = StateFile(location); DeploymentPaths.RequireOrdinaryAncestors(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (input.Length > 16384) throw new InvalidDataException("Recovery state exceeds budget");
        using var reader = new StreamReader(input);
        var state = Wire.Decode<State>(reader.ReadToEnd());
        if (state.Launches is < 0 or > 11 || state.Status is not ("New" or "Launching" or "Running" or "Exited" or "Blocked")) throw new InvalidDataException("Recovery state invalid; inspect original launch");
        if (state.Status == "Running" && (state.Launches == 0 || state.ChildPid is not > 0 || state.ChildStartedTicks is not > 0 ||
            state.SourceCommit is not { Length: 40 } || !state.SourceCommit.All(Uri.IsHexDigit) ||
            state.ExecutableSha256 is not { Length: 64 } || !state.ExecutableSha256.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Running recovery identity incomplete; inspect original launch");
        if (state.Status == "New" && (state.Launches != 0 || state.ChildPid is not null || state.ChildStartedTicks is not null ||
            state.SourceCommit is not null || state.ExecutableSha256 is not null || state.LaunchUncertain))
            throw new InvalidDataException("New recovery state contains prior launch facts");
        if (!state.LaunchUncertain && state.Status is "Exited" or "Blocked")
        {
            var normal = state.Status == "Exited" && state.Launches == 0 && state.ChildPid is null && state.ChildStartedTicks is null && state.SourceCommit is null && state.ExecutableSha256 is null;
            var known = state.Launches > 0 && state.SourceCommit is { Length: 40 } && state.SourceCommit.All(Uri.IsHexDigit) &&
                state.ExecutableSha256 is { Length: 64 } && state.ExecutableSha256.All(Uri.IsHexDigit) &&
                (state.ChildPid is null && state.ChildStartedTicks is null || state.ChildPid is > 0 && state.ChildStartedTicks is > 0);
            if (!normal && !known) throw new InvalidDataException("Completed recovery state lacks original identity");
        }
        return state.Status == "Launching" ? state with { LaunchUncertain = true } : state;
    }
    static void Save(DeploymentLocation location, State state)
    { RequireDirectory(location); BeforePersistForVerification?.Invoke(state.Status); ExtensionStore.AtomicWrite(StateFile(location), state, createDirectory: false); }
    public static bool IsBlocked(DeploymentLocation location)
    { var state = Read(location); return state.LaunchUncertain || state.Status is "Blocked" or "Launching"; }
    static int Block(DeploymentLocation location, State state, string reason)
    {
        Save(location, state with { Status = "Blocked" });
        var alert = new { At = DateTimeOffset.UtcNow, Source = "进程监督", Level = "错误", Event = "DesktopCrashLoopBlocked", Reason = reason,
            location.ApplicationId, location.SourceCommit, state.Launches, state.ChildPid, ExitCode = LoopBlockedExitCode, Policy = "No more client launches; no replay or Unknown changes" };
        try { ExtensionStore.AtomicWrite(AlertFile(location), alert, createDirectory: false); }
        catch (Exception error) when (LedgerError(error)) { DiagnosticFailure("Recovery state is blocked; alert unavailable: " + error.GetType().Name); }
        try { RuntimeJournal.AppendMetadata(Path.Combine(StateDirectory(location), "diagnostics"), alert, createDirectory: false); }
        catch (Exception error) when (LedgerError(error)) { DiagnosticFailure("Recovery state is blocked; diagnostic unavailable: " + error.GetType().Name); }
        return LoopBlockedExitCode;
    }
    internal static void ResetAfterAuthorizedRepair(DeploymentLocation location) => RepairAndReset(location, () => { });
    internal static void RepairAndReset(DeploymentLocation location, Action repair)
    {
        RequireDirectory(location);
        using var owner = new ResourceLease(StateDirectory(location), ["supervisor"], createDirectory: false);
        var state = Read(location);
        if (state.LaunchUncertain || state.Status is "Running" or "Launching") throw new InvalidOperationException("原启动结果不明，先核对并保全原进程；常规修复不能清除此围栏。");
        repair();
        Save(location, new(0, "New"));
        try { File.Delete(AlertFile(location)); }
        catch (Exception error) when (LedgerError(error)) { DiagnosticFailure("Recovery reset committed; stale diagnostic retained: " + error.GetType().Name); }
    }
    [SupportedOSPlatform("windows")]
    public static bool StartForInstalledDesktop(DeploymentLocation location, string[] presentationArguments)
    {
        if (location.Recovery?.Enabled != true) return false;
        MachineRuntimePolicy.RequireRuntimeIdentity();
        if (IsBlocked(location)) throw new InvalidOperationException("连续启动失败已停止自动重启；请由安装维护修复系统入口。告警：" + AlertFile(location));
        ValidatePresentationArguments(presentationArguments);
        ExtensionStore.AtomicWrite(Path.Combine(StateDirectory(location), "presentation.json"), presentationArguments, createDirectory: false);
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
        if (state.LaunchUncertain || state.SourceCommit != location.SourceCommit) return false;
        if (state.Status == "Exited") return true; // SourceCommit is cleared on a normal close; otherwise bounded backoff.
        if (state.Status != "Running" || state.ChildPid is not { } pid) return false;
        try { using var child = Process.GetProcessById(pid); return MatchesLiveProcess(child, location, state); }
        catch (Exception error) when (ProcessInspectionError(error)) { return false; }
    }
    static bool ProcessInspectionError(Exception error) => error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException;
    static bool MatchesLiveProcess(Process child, DeploymentLocation location, State state)
    {
        _ = child.Handle; // Retain the observed process identity before checking exit/PID reuse.
        return !child.HasExited && child.StartTime.ToUniversalTime().Ticks == state.ChildStartedTicks && state.SourceCommit == location.SourceCommit &&
            child.MainModule?.FileName is { } executable && Wire.FileHash(executable) == state.ExecutableSha256;
    }
    internal static async Task<int> RunAsync(DeploymentLocation location, Func<DeploymentLocation?> current, string[]? presentation = null,
        Func<ProcessStartInfo, Process>? launch = null, TimeProvider? clock = null, CancellationToken cancellationToken = default)
    {
        var policy = location.Recovery ?? throw new InvalidDataException("No recovery policy"); policy.Validate();
        if (!policy.Enabled) return 0;
        try { RequireExistingState(location); }
        catch (Exception error) when (LedgerError(error)) { return Unreadable(error); }
        clock ??= TimeProvider.System;
        var directory = StateDirectory(location); DeploymentPaths.RequireOrdinaryAncestors(directory);
        ResourceLease owner;
        var ownershipStarted = clock.GetTimestamp();
        while (true)
        {
            try { owner = new(directory, ["supervisor"], createDirectory: false); break; }
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
            catch (Exception error) when (LedgerError(error))
            { return Unreadable(error); }
            bool Installed() => current() is { LifecycleState: "Installed" } now && now.ReleaseRoot == location.ReleaseRoot && now.SourceCommit == location.SourceCommit &&
                now.DataRoot == location.DataRoot && now.ConfigurationDirectory == location.ConfigurationDirectory && now.Recovery?.Enabled == true;
            while (Installed())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (state.Status == "Blocked") return LoopBlockedExitCode;
                if (state.LaunchUncertain || state.Status == "Launching") return Block(location, state, "Original launch outcome unavailable; inspect before relaunch");
                Process? child = null;
                if (state.Status == "Running" && state.ChildPid is { } priorPid)
                {
                    try
                    {
                        child = Process.GetProcessById(priorPid);
                        if (!MatchesLiveProcess(child, location, state))
                        { child.Dispose(); return Block(location, state with { LaunchUncertain = true }, "Saved process outcome unavailable or identity differs; no second client created"); }
                    }
                    catch (Exception error) when (ProcessInspectionError(error))
                    { child?.Dispose(); return Block(location, state with { LaunchUncertain = true }, "Saved process cannot be verified: " + error.GetType().Name); }
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
                        state = state with { ChildPid = child.Id };
                        state = state with { ChildStartedTicks = child.StartTime.ToUniversalTime().Ticks };
                        state = state with { ExecutableSha256 = Wire.FileHash(child.MainModule?.FileName ?? executable) };
                        state = state with { Status = "Running", LaunchUncertain = false }; Save(location, state);
                    }
                    catch (Exception error)
                    { child?.Dispose(); return Block(location, state with { LaunchUncertain = true }, "Client creation outcome requires inspection: " + error.GetType().Name); }
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
                            { state = state with { Launches = 1 }; Save(location, state); healthyReset = true; }
                        }
                        await exited.ConfigureAwait(false);
                        var exit = child.ExitCode;
                        if (exit == CrashDiagnostics.PermanentStorageExitCode)
                            return Block(location, state,
                                "Permanent storage fault; repair storage and inspect original responsibilities before authorized reset");
                        var exitFact = new { At = DateTimeOffset.UtcNow, Source = "进程监督", Event = "DesktopExited", location.SourceCommit, ChildPid = child.Id, ExitCode = exit, state.Launches };
                        state = exit == 0 ? new(0, "Exited") : state with { Status = "Exited", ChildPid = null, ChildStartedTicks = null }; Save(location, state);
                        try { RuntimeJournal.AppendMetadata(Path.Combine(directory, "diagnostics"), exitFact, createDirectory: false); }
                        catch (Exception error) when (LedgerError(error)) { DiagnosticFailure("Observed client exit persisted; diagnostic unavailable: " + error.GetType().Name); }
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
