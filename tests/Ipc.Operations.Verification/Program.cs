extern alias Fixture;
using ManualSupplyHarness = Fixture::ManualSupplyHarness;
using CameraRoundHarness = Fixture::CameraRoundHarness;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ipc.Application.Contracts;
using Ipc.Diagnostics;
using Ipc.Operations;
using Ipc.Operations.Desktop;
using Ipc.Platform;

internal static partial class Program
{
    static string executable => Environment.ProcessPath!;
    static string workspace = null!, root = null!;
    const string OperationsPassword = "SYNTH-OPERATIONS-12345";
    static readonly Dictionary<int, Task<string>> childErrors = [];
    static Task<string> ErrorText(Process process) => childErrors[process.Id];
    static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS OPERATIONS " + message); }
    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            if (args is ["--p0-crash-hooks"]) return P0Dispatcher.Parent();
            if (args is ["--p0-dispatcher-child", var diagnostics]) return P0Dispatcher.Child(diagnostics);
            if (args.Length == 3 && args[2] == "--smoke-screenshots")
            {
                workspace = Path.GetFullPath(args[0]);
                root = Path.GetFullPath(args[1]);
                return CaptureOperations().GetAwaiter().GetResult();
            }
            if (args.Length == 2)
            {
                workspace = Path.GetFullPath(args[0]); root = Path.GetFullPath(args[1]);
                using var coordinator = Child("coordinator");
                Console.Write(ReadCoordinatorOutput(coordinator));
                return coordinator.ExitCode;
            }
            workspace = Environment.GetEnvironmentVariable("IPC_FIXTURE_WORKSPACE")!;
            root = Environment.GetEnvironmentVariable("IPC_FIXTURE_ROOT")!;
            Directory.CreateDirectory(root);
            // Match the existing Application verifier: source fixtures do not
            // provision or borrow the workstation's protected machine policy.
            typeof(Ipc.Application.ParcelFlow.ParcelRecovery).GetProperty("BackupSetLeaseRoot", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(null, (Func<string>)(() => Path.Combine(root, "backup-set-leases")));
            var role = Environment.GetEnvironmentVariable("IPC_FIXTURE_ROLE");
            if (role == "lease") { using var lease = new OperationsToolLease(Installation()); Console.WriteLine("READY"); Console.ReadLine(); return 0; }
            if (role == "ui-early-close") { EarlyCloseUi(); return 0; }
            if (role is "ui-control" or "ui-owner-loss" or "ui-smoke") { ControlUi(role == "ui-owner-loss", role == "ui-smoke"); return 0; }
            if (role is "ui" or "ui-crash" or "ui-offline" or "ui-cancel") { Ui(role == "ui-crash", role == "ui-offline", role == "ui-cancel"); return 0; }
            if (role is "server" or "server-restart") { Server(role != "server-restart").GetAwaiter().GetResult(); return 0; }
            if (role is "unapproved" or "wrong-role") { Unapproved().GetAwaiter().GetResult(); return 0; }
            if (role == "control-crash") { ControlCrash().GetAwaiter().GetResult(); return 0; }
            if (role == "control-inflight") { ControlInflight().GetAwaiter().GetResult(); return 0; }
            Run().GetAwaiter().GetResult(); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    static async Task<int> CaptureOperations()
    {
        Directory.CreateDirectory(root);
        using var server = Child("server");
        try
        {
            await Ready(server);
            using var capture = Child("ui-smoke");
            var output = capture.StandardOutput.ReadToEndAsync();
            try { await capture.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90)); }
            finally { if (!capture.HasExited) capture.Kill(entireProcessTree: true); }
            Console.Write(await output);
            Console.Error.Write(await ErrorText(capture));
            return capture.ExitCode;
        }
        finally
        {
            if (!server.HasExited)
            {
                await server.StandardInput.WriteLineAsync("stop");
                await server.StandardInput.FlushAsync();
                await server.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            }
        }
    }
    static string ReadCoordinatorOutput(Process process)
    {
        var output = process.StandardOutput.ReadToEnd(); process.WaitForExit();
        Console.Error.Write(ErrorText(process).GetAwaiter().GetResult()); return output;
    }
    static DeploymentLocation Location() => new("sportmaster", new string('A', 40), root, Path.Combine(root, "station", "config"),
        Path.Combine(root, "station", "data"), WindowsIdentity.GetCurrent().User!.Value) { InventorySha256 = new string('B', 64) };
    static OperationsInstallation Installation(string? peer = null)
    {
        var location = Location();
        // Only this friend test assembly can substitute registration. No CLI or
        // production API accepts these paths or bypasses protected installation proof.
        return new(location, peer ?? executable, () =>
        {
            if (File.Exists(Path.Combine(root, "registration-changed"))) throw new UnauthorizedAccessException("Synthetic replacement registration");
        }, () => DeploymentProgramUse.AcquireAt(Path.Combine(root, "program-leases"), [root], true));
    }
    static Process Child(string role, bool otherExecutable = false)
    {
        var path = executable;
        if (otherExecutable)
        {
            path = Path.Combine(AppContext.BaseDirectory, "Ipc.UnapprovedObserver.exe");
            File.Copy(executable, path, true);
        }
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["IPC_FIXTURE_WORKSPACE"] = workspace;
        start.Environment["IPC_FIXTURE_ROOT"] = root;
        start.Environment["IPC_FIXTURE_ROLE"] = role;
        if (role is not ("server" or "server-restart" or "wrong-role")) start.ArgumentList.Add(InstallationIdentity.OperationsArgument);
        var child = Process.Start(start)!; childErrors.Add(child.Id, child.StandardError.ReadToEndAsync()); return child;
    }
    static async Task Ready(Process process)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(40));
            if (line == "READY") return;
            if (line is null) throw new InvalidOperationException("Child exited: " + await ErrorText(process));
        }
    }
    static async Task<OperationsReply> Raw(OperationsRequest request, TokenImpersonationLevel level = TokenImpersonationLevel.Identification)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pipe = new NamedPipeClientStream(".", Installation().PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, level);
        await pipe.ConnectAsync(stop.Token); await OperationsPipe.Write(pipe, request, stop.Token);
        var reply = await OperationsPipe.Read<OperationsReply>(pipe, stop.Token);
        await pipe.WriteAsync(new byte[] { 1 }, stop.Token); return reply;
    }
    static async Task Reject(Func<Task> action, string message)
    {
        try { await action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { Check(true, message); return; }
        throw new InvalidOperationException("Expected rejection: " + message);
    }
    static async Task<ResourceLease> ReleasedBusinessLease()
    {
        // A signaled process is not proof that outstanding Windows file-object
        // references have drained. Assert actual exclusive acquisition, with a
        // finite wait only for observed sharing/lock violations; other failures fail now.
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            try { return new ResourceLease(Path.Combine(root, "leases"), ["operations-synthetic-business-owner"]); }
            catch (IOException error) when ((error.HResult & 0xFFFF) is 32 or 33 && Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(5))
            { await Task.Delay(50); }
        }
    }
    static async Task<DeploymentProgramUse> ReleasedProgramUse()
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            try { return DeploymentProgramUse.AcquireAt(Path.Combine(root, "program-leases"), [root], false); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 && Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(5))
            { await Task.Delay(50); }
        }
    }
    static async Task Server(bool ready)
    {
        Trace.Listeners.Add(new TextWriterTraceListener(Console.Error));
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "station"), deferred: true)
        {
            RootOwnership = () => new ResourceLease(Path.Combine(root, "leases"), ["operations-synthetic-business-owner"])
        };
        await h.StartAsync(ready); var initial = await h.App.SnapshotAsync();
        if (!(await h.App.ManagementModeStatusAsync()).Configured) await h.App.ConfigureManagementPasswordAsync(OperationsPassword);
        var endpoint = new OperationsServer(Installation(), (IParcelPresentationSource)h.App, initial, h.App, () => new Ipc.Application.ParcelFlow.ParcelStartupMaintenance(h.Launch, new Ipc.Application.Sportmaster.SportmasterFlow(), () => h.Factory));
        Task<bool>? backupPreparation = null; TaskCompletionSource? backupRelease = null;
        try
        {
            await Reject(() => Task.Run(async () =>
            {
                await using var duplicate = new OperationsServer(Installation(), (IParcelPresentationSource)h.App, initial);
            }), "second IPC owner cannot impersonate original runtime");
            Console.WriteLine("READY");
            while (await Console.In.ReadLineAsync() is { } command)
            {
                if (command == "stop") break;
                if (command is "stale" or "reconnect")
                {
                    await Task.WhenAll(endpoint.DisposeAsync().AsTask(), endpoint.DisposeAsync().AsTask());
                    var snapshot = await h.App.SnapshotAsync();
                    endpoint = command == "stale" ? new OperationsServer(Installation(), new SilentPresentation(),
                        snapshot with { PublishedTicks = Stopwatch.GetTimestamp() - Stopwatch.Frequency * 6 }) :
                        new OperationsServer(Installation(), (IParcelPresentationSource)h.App, snapshot, h.App, () => new Ipc.Application.ParcelFlow.ParcelStartupMaintenance(h.Launch, new Ipc.Application.Sportmaster.SportmasterFlow(), () => h.Factory));
                }
                if (command == "prepare-backup")
                {
                    if (backupPreparation is not null) throw new InvalidOperationException("Original backup fixture is still held");
                    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var release = backupRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    backupPreparation = h.AfterMonitorDrain(async () => { entered.TrySetResult(); await release.Task; return true; });
                    if (await Task.WhenAny(entered.Task, backupPreparation) != entered.Task) await backupPreparation;
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
                if (command == "release-backup")
                {
                    backupRelease!.TrySetResult(); await backupPreparation!; backupRelease = null; backupPreparation = null;
                }
                if (command == "hold-debug") h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
                if (command == "wait-debug-send") await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 1));
                if (command == "release-debug") { h.Peer.Hold?.TrySetResult(); h.Peer.Hold = null; }
                if (command == "verify-debug-crash")
                {
                    await h.App.EnterManagementSessionAsync(OperationsPassword);
                    var prepared = Wire.Decode<CameraReadRound>(File.ReadAllText(Path.Combine(root, "prepared-debug.json")));
                    await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(prepared.OperationId!))?.Wcs?.State is StageState.Accepted or StageState.Unknown);
                    var original = (await h.App.OperationAsync(prepared.OperationId!))!;
                    Check(original.Wcs is { Attempts: 1 } && original.Wcs.Request.IntegrationRequest!.Body == prepared.RequestBody && h.Peer.Requests.Count == 1,
                        "tool death during actual HTTP retains exactly one frozen original debug attempt and its real result");
                    var accepted = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "AFTER-DEBUG-CRASH"), default);
                    await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.PositionId == "left" && o.State == ParcelState.SlotResolved));
                    Check(accepted.Status == "Admitted" && h.Peer.Requests.Count == 2 && h.Peer.Requests.Count(body => body == prepared.RequestBody) == 1,
                        "other position continues legitimate supply without replaying the crashed tool request");
                }
                if (command == "submit")
                {
                    var receipt = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-OPS-0001"), default);
                    await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.State == ParcelState.SlotResolved));
                    Check(receipt.Status == "Admitted" && h.Peer.Requests.Count == 1 && !(await h.App.SnapshotAsync()).Paused,
                        "read-only observer leaves real Application admission and one RCS loopback request intact");
                }
                if (command == "owner")
                {
                    await using var factory = new ManualSupplyHarness.ManualFactory(h.Launch);
                    // The test exercises the same lease acquired by this runtime before any database access.
                    await Reject(() => Task.Run(() => { using var owner = h.RootOwnership!(); }), "business ownership remains exclusive while observer is connected");
                }
                if (command == "status") Console.WriteLine("STATUS " + JsonSerializer.Serialize(new { (await h.App.SnapshotAsync()).Paused, Requests = h.Peer.Requests.Count }));
                if (command == "field-save")
                {
                    var snapshot = await h.App.SnapshotAsync();
                    var before = snapshot.Configuration.Pending ?? snapshot.Configuration.Effective;
                    var result = await h.App.StageConfigurationAsync(snapshot.Configuration.Revision, before with { StationCode = "SYNTH-FIELD-CHANGED" }, "现场配置并发验证");
                    Check(result.Status is "Staged" or "Applied" or "PendingRestart" or "Saved", "the original owner accepts its field configuration candidate: " + result.Status);
                }
                Console.WriteLine("DONE");
            }
        }
        finally
        {
            backupRelease?.TrySetResult();
            try { if (backupPreparation is not null) await backupPreparation; }
            finally { await endpoint.DisposeAsync(); }
        }
    }
    sealed class SilentPresentation : IParcelPresentationSource, IParcelPresentationSubscription
    {
        public IParcelPresentationSubscription SubscribePresentation() => this;
        public async ValueTask<ParcelPresentationUpdate> ReadAsync(CancellationToken token)
        { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); }
        public void Dispose() { }
    }
    static OperationsRemoteApplication Remote(OperationsControlClient client) => new(client,
        new(Location().ConfigurationDirectory, Location().DataRoot));

    static async Task ControlCrash()
    {
        await using var control = new OperationsControlClient(Installation());
        await control.ConnectAsync(default); var remote = Remote(control);
        var grant = await ((IWorkstationAdministration)remote).EnterManagementSessionAsync(OperationsPassword);
        Check(grant?.Authorized == true, "crash scenario owns a real password session");
        var result = await ((IManagementWorkspace)remote).SetManagementWorkspaceAsync(Wire.Id(), true, default);
        Check(result.Status is "Paused" or "PausedWithCameraWarning", "explicit debug establishes the original management workspace");
        Console.WriteLine("READY"); await Task.Delay(Timeout.Infinite);
    }

    static async Task ControlInflight()
    {
        await using var control = new OperationsControlClient(Installation());
        await control.ConnectAsync(default); IParcelApplication remote = Remote(control);
        await ((IWorkstationAdministration)remote).EnterManagementSessionAsync(OperationsPassword);
        await ((IManagementWorkspace)remote).SetManagementWorkspaceAsync(Wire.Id(), true, default);
        var snapshot = await remote.SnapshotAsync();
        var round = await ((ICameraReadRounds)remote).PrepareManualRcsRequestAsync(new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, "right", "CRASH-DURING-HTTP", null)
        { ExpectedArrivalGeneration = snapshot.Positions!.Single(p => p.Id == "right").Arrival!.Generation }, default);
        File.WriteAllText(Path.Combine(root, "prepared-debug.json"), Wire.Encode(round));
        var sending = ((ICameraReadRounds)remote).SendCameraRoundAsync(new(Wire.Id(), snapshot.RuntimeId, round.Id!, round.RequestHash!), default);
        Console.WriteLine("READY"); await sending; await Task.Delay(Timeout.Infinite);
    }
    static async Task VerifyInflightCrash()
    {
        var previous = root; root = Path.Combine(root, "inflight-crash"); Directory.CreateDirectory(root);
        using var owner = Child("server"); Process? tool = null;
        try
        {
            await Ready(owner); await Command(owner, "hold-debug");
            tool = Child("control-inflight"); await Ready(tool); await Command(owner, "wait-debug-send");
            tool.Kill(); await tool.WaitForExitAsync(); await Command(owner, "release-debug");
            var observer = new OperationsClient(Installation());
            await CameraRoundHarness.Until(async () => (await observer.ReadAsync(default)).Positions.Single(p => p.Id == "left").Admit);
            await Command(owner, "verify-debug-crash");
            await owner.StandardInput.WriteLineAsync("stop"); await owner.StandardInput.FlushAsync();
            await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Check(owner.ExitCode == 0, "inflight crash owner shuts down with original responsibilities retained: " + await ErrorText(owner));
            using var released = await ReleasedBusinessLease();
        }
        finally
        {
            if (tool is not null) { if (!tool.HasExited) { tool.Kill(); await tool.WaitForExitAsync(); } tool.Dispose(); }
            if (!owner.HasExited) { owner.Kill(); await owner.WaitForExitAsync(); }
            var errors = await ErrorText(owner); File.WriteAllText(Path.Combine(root, "owner.log"), errors);
            root = previous;
        }
    }

    static async Task VerifyControl(Process owner, OperationsClient observer)
    {
        await using (var control = new OperationsControlClient(Installation()))
        {
            await control.ConnectAsync(default); IParcelApplication remote = Remote(control);
            var authority = (IWorkstationAdministration)remote;
            await Reject(() => remote.SnapshotAsync(), "authenticated installation alone cannot read privileged configuration");
            var grant = await authority.EnterManagementSessionAsync(OperationsPassword);
            Check(grant?.Authorized == true && (await observer.ReadAsync(default)).Positions.All(p => p.Admit),
                "login grants a scoped session without acquiring production pause");
            var snapshot = await remote.SnapshotAsync(); var before = snapshot.Configuration.Pending ?? snapshot.Configuration.Effective;
            await Reject(() => remote.StageConfigurationAsync(snapshot.Configuration.Revision, before with { StationCode = "ILLEGAL-TECHNICAL-IDENTITY" }),
                "technical control rejects changes to field-owned station identity before save");
            var candidate = before with { ReadTimeoutMs = before.EffectiveReadTimeoutMs + 100 };
            await remote.StageConfigurationAsync(snapshot.Configuration.Revision, candidate, "专业参数跨进程验证");
            var after = await remote.SnapshotAsync(); var actual = after.Configuration.Pending ?? after.Configuration.Effective;
            Check(actual.ReadTimeoutMs == candidate.ReadTimeoutMs && actual.StationCode == before.StationCode &&
                Wire.Encode(actual.Wcs) == Wire.Encode(before.Wcs) && Wire.Encode(actual.Topology) == Wire.Encode(before.Topology),
                "technical CAS save preserves every hidden RCS and camera field");
            var stale = after.Configuration.Revision;
            await Command(owner, "field-save");
            await Reject(() => remote.StageConfigurationAsync(stale, actual with { ReadTimeoutMs = 1800 }),
                "a concurrent field save prevents the old technical draft overwriting hidden fields");
            await Reject(() => ((ICameraReadRounds)remote).PrepareManualRcsRequestAsync(new(Wire.Id(), snapshot.RuntimeId,
                stale, "left", "SYNTH-NO-IMPLICIT-DEBUG", null), default), "manual RCS debug cannot implicitly enter production pause");
            var workspace = (IManagementWorkspace)remote;
            var debugWindow = Wire.Id();
            var opened = await workspace.SetManagementWorkspaceAsync(debugWindow, true, default);
            await CameraRoundHarness.Until(async () => (await observer.ReadAsync(default)).Positions.All(p => !p.Admit));
            Check(opened.Status is "Paused" or "PausedWithCameraWarning" && (await observer.ReadAsync(default)).Positions.All(p => !p.Admit),
                "only explicit debug acquires the original whole-station admission fence");
            var closed = await workspace.SetManagementWorkspaceAsync(debugWindow, false, default);
            Check(closed.Status == "Restored", "normal debug exit drains the same workspace and restores original state");
            var replacedWindow = Wire.Id();
            await workspace.SetManagementWorkspaceAsync(replacedWindow, true, default);
            var replacement = await authority.EnterManagementSessionAsync(OperationsPassword);
            await CameraRoundHarness.Until(async () => (await observer.ReadAsync(default)).Positions.All(p => p.Admit));
            Check(replacement?.Authorized == true && await workspace.ReadManagementWorkspaceOwnerAsync(default) is null &&
                (await observer.ReadAsync(default)).Positions.All(p => p.Admit),
                "replacing a grant releases the old explicit debug scope rather than adopting it under a new authorization");
            var current = await remote.SnapshotAsync();
            var queuedCandidate = (current.Configuration.Pending ?? current.Configuration.Effective) with { ReadTimeoutMs = 1900 };
            using (LocalManagementMode.BindCommandSession(grant!.SessionId!))
                await Reject(() => remote.StageConfigurationAsync(current.Configuration.Revision, queuedCandidate),
                    "a queued save with the original grant cannot borrow the replacement grant across IPC");
            Check((await remote.SnapshotAsync()).Configuration.Revision == current.Configuration.Revision,
                "the refused old-grant save leaves the configuration revision intact");
            var currentWindow = Wire.Id();
            await workspace.SetManagementWorkspaceAsync(currentWindow, true, default);
            await Reject(() => workspace.SetManagementWorkspaceAsync(replacedWindow, false, default),
                "a late close from the previous debug scope cannot release the next explicit scope");
            Check(await workspace.ReadManagementWorkspaceOwnerAsync(default) == currentWindow,
                "the current debug scope retains its exact owner after a stale close");
            await workspace.SetManagementWorkspaceAsync(currentWindow, false, default);
            var logPage = await authority.QueryLogsAsync(new(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddSeconds(1)), null, 0, default);
            Check(logPage.Entries.Length > 0, "full authorized log query reads actual original runtime records across the control pipe");
            var context = await authority.LogContextAsync(logPage.Selection, logPage.Entries[0].Location, 1, 1, default);
            var export = Path.Combine(root, "authorized-logs.txt");
            var exported = await authority.ExportLogsAsync(logPage.Selection, export, false, 0, default);
            Check(context.Entries.Length > 0 && exported > 0 && File.Exists(export), "original context and authorized export remain reachable with the selected log identity");
            ((ISoftwareLogSelectionLifetime)remote).ReleaseLogSelection(logPage.Selection);
            // Real original backup implementation, real pipe and original complete-set identity.
            var paused = await remote.SnapshotAsync();
            await remote.CommandAsync(new(Wire.Id(), paused.Revision, "Pause", Value: "Synthetic operations backup integration"));
            var catalog = Path.Combine(root, "backup-catalog"); Directory.CreateDirectory(catalog);
            var backup = Path.Combine(catalog, "station.db");
            // Use the existing pre-reservation seam, preserving the real busy
            // fence and sending exactly once after the original monitor drains.
            await Command(owner, "prepare-backup");
            BusinessReceipt backupReceipt;
            try { backupReceipt = await remote.BackupAsync(backup); }
            finally { await Command(owner, "release-backup"); }
            Check(backupReceipt.Status == "BackupCreated", "online backup is created only by the original production owner after explicit pause: " + backupReceipt.Status + "; " + backupReceipt.Reason);
            var sets = (ICompleteBackupSetMaintenance)remote;
            var progress = new CollectedProgress();
            var selected = (await sets.ListBackupsAsync(catalog, progress, default)).Single();
            Check(selected is { Status: "Verified", Identity: not null } && progress.Count > 0, "backup listing retains the whole-set identity and streams actual progress across processes");
            var originalHash = Wire.FileHash(backup);
            var delivered = await sets.ExportBackupSetAsync(backup, selected.Identity!, Path.Combine(root, "backup-delivery"), progress, default);
            Check(delivered.Status == "Exported" && Wire.FileHash(delivered.Path) == originalHash && Wire.FileHash(backup) == originalHash,
                "online complete-set delivery preserves original bytes and reports original result");
            var confirmed = await sets.InspectBackupDeliveryAsync(backup, selected.Identity!, Path.Combine(root, "backup-delivery"));
            Check(confirmed.Status == "Exported", "delivery reconciliation inspects the original destination without a duplicate copy");
            var copy = (await sets.ListBackupsAsync(Path.GetDirectoryName(delivered.Path)!, default)).Single();
            var deleted = await sets.DeleteBackupSetAsync(copy.Path, copy.Identity!, progress, default);
            Check(deleted.Status == "Deleted" && !File.Exists(copy.Path) && File.Exists(backup), "bounded backup deletion removes only the selected delivered set and preserves its source");
            var resume = await remote.SnapshotAsync();
            await remote.CommandAsync(new(Wire.Id(), resume.Revision, "Resume"));
            var newer = await authority.EnterManagementSessionAsync(OperationsPassword);
            await Reject(() => authority.ExitManagementModeAsync(grant!.SessionId!), "an old window cannot close a replacement session through control");
            Check((await authority.ManagementModeStatusAsync()).SessionId == newer!.SessionId, "replacement grant survives old-window close");
        }
        await CameraRoundHarness.Until(async () => (await observer.ReadAsync(default)).Positions.All(p => p.Admit));
        using var crashing = Child("control-crash"); await Ready(crashing);
        crashing.Kill(); await crashing.WaitForExitAsync();
        await CameraRoundHarness.Until(async () => (await observer.ReadAsync(default)).Positions.All(p => p.Admit));
        Check(true, "actual tool process crash releases only its debug workspace and production continues");
    }

    sealed class CollectedProgress : IProgress<MaintenanceProgress>
    {
        int count;
        public int Count => Volatile.Read(ref count);
        public void Report(MaintenanceProgress value) => Interlocked.Increment(ref count);
    }

    static async Task Unapproved()
    {
        await Reject(() => Raw(new(1, Installation().Identity, OperationsProtocol.Permission, Guid.NewGuid(), Guid.Empty)),
            "unregistered apphost is denied by actual Windows peer identity");
    }
    static async Task Command(Process process, string command)
    {
        await process.StandardInput.WriteLineAsync(command); await process.StandardInput.FlushAsync();
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
            if (line == "DONE") return;
            if (line is null) throw new InvalidOperationException(await ErrorText(process));
            Console.WriteLine(line);
        }
    }
    static async Task Run()
    {
        await VerifyInflightCrash();
        using (var early = Child("ui-early-close"))
        {
            await early.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Check(early.ExitCode == 0, "closing the native window during source initialization drains startup without reentrant Close: " + await ErrorText(early));
        }
        Check(!Directory.Exists(Location().DataRoot), "isolated verification starts without a business database");
        await Reject(() => Task.Run(() => OperationsInstallation.Load(AppContext.BaseDirectory)), "source/unregistered tool cannot claim installed authority");
        using var leaseChild = Child("lease"); await Ready(leaseChild);
        await Reject(() => Task.Run(() => { using var lease = new OperationsToolLease(Installation()); }), "second tool process cannot acquire the UI singleton");
        await Reject(() => Task.Run(() => { using var maintenance = DeploymentProgramUse.AcquireAt(Path.Combine(root, "program-leases"), [root], false); }),
            "an offline observer protects its program files without owning a business database");
        using (var otherRelease = DeploymentProgramUse.AcquireAt(Path.Combine(root, "program-leases"), [Path.Combine(root, "other-release")], false))
            Check(true, "an observer does not block maintenance of unrelated program roots");
        leaseChild.Kill(); await leaseChild.WaitForExitAsync();
        using (var maintenance = await ReleasedProgramUse())
        {
            await Reject(() => Task.Run(() => { using var observer = new OperationsToolLease(Installation()); }), "maintenance blocks a new observer until its program-file operation ends");
            Check(true, "abnormal observer exit releases the actual program-file guard");
        }
        using (var recovered = new OperationsToolLease(Installation())) Check(true, "Windows releases singleton after abnormal tool exit");
        using var server = Child("server");
        var completed = false;
        try
        {
            await Ready(server); var client = new OperationsClient(Installation());
            var value = await client.ReadAsync(default);
            Check(value.RuntimeId != Guid.Empty && value.Positions.Length == 2 && !value.Paused, "real cross-process handshake reads the existing Application runtime");
            await VerifyControl(server, client);
            using (var nativeControl = Child("ui-control"))
            {
                var outputRead = nativeControl.StandardOutput.ReadToEndAsync();
                await nativeControl.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
                var output = await outputRead;
                var errors = await ErrorText(nativeControl);
                File.WriteAllText(Path.Combine(root, "stage2-native-ui.log"), output + errors);
                Check(nativeControl.ExitCode == 0, "five native operations pages and professional field ownership: " + errors + output);
            }
            var request = new OperationsRequest(1, Installation().Identity, OperationsProtocol.Permission, Guid.NewGuid(), value.RuntimeId);
            foreach (var invalid in new[] { request with { Protocol = 2 }, request with { Installation = "other-installation" },
                request with { Permission = "Pause" }, request with { RuntimeId = Guid.NewGuid() } })
            {
                var reply = await Raw(invalid); Check(reply.Error is not null && reply.Overview is null, "invalid version, installation, permission or runtime discloses no snapshot");
            }
            await Reject(() => new OperationsClient(Installation(Path.Combine(root, "wrong.exe"))).ReadAsync(default), "client rejects unregistered server apphost");
            await Reject(() => Raw(request, TokenImpersonationLevel.Anonymous), "actual anonymous client identity cannot obtain runtime state");
            var denied = new OperationsInstallation(Location() with { RuntimeAccountSid = "S-1-5-21-1-2-3-99999" }, executable, () => { }, () => DeploymentProgramUse.AcquireAt(Path.Combine(root, "program-leases"), [root], true));
            await Reject(() => Task.Run(() => denied.RequireRead()), "unassigned Windows account has no online or offline read authority");
            using (var unapproved = Child("unapproved", otherExecutable: true))
            {
                await unapproved.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Check(unapproved.ExitCode == 0, "actual unapproved executable rejected: " + await ErrorText(unapproved));
            }
            using (var wrongRole = Child("wrong-role"))
            {
                await wrongRole.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Check(wrongRole.ExitCode == 0, "same registered apphost without the operations role is refused as client");
            }
            await Command(server, "stale");
            await Reject(() => client.ReadAsync(default), "draining or requesting an old Application snapshot cannot renew its freshness");
            await Command(server, "reconnect");
            Check((await client.ReadAsync(default)).RuntimeId == value.RuntimeId, "observer reconnect does not rebuild or replace the business instance");
            // Broken/partial frames occupy only one bounded worker and cannot stop production.
            using (var broken = new NamedPipeClientStream(".", Installation().PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification))
            {
                await broken.ConnectAsync(2000); await broken.WriteAsync(new byte[] { 0xff, 0xff });
                await Command(server, "submit");
                Check((await client.ReadAsync(default)).RuntimeId == value.RuntimeId, "half-frame client does not block other observer or business work");
            }
            await Command(server, "owner");
            // Real read-only file API, in parallel with the real runtime log writer.
            var directories = Installation().LogDirectories(); Check(directories.Length == 1, "only registered station runtime diagnostics are discovered");
            var diagnosticFiles = Directory.GetFiles(directories[0], "runtime.jsonl*");
            using (var logs = SoftwareLogReader.OperationalReadOnly(directories[0], Installation().RequireRead))
            {
                var page = await logs.QueryAsync(new(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddSeconds(1)), null, 0, default);
                Check(page.Entries.Length > 0, "same bounded log reader queries real runtime files without entering management mode");
                await Reject(() => logs.ExportAsync(page.Selection, Path.Combine(root, "forbidden.jsonl"), true, 0, default), "read-only authority cannot publish an export");
                logs.Release(page.Selection);
            }
            using (var ui = Child("ui"))
            {
                await ui.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Check(ui.ExitCode == 0, "native WPF online overview, log query and close: " + await ErrorText(ui));
            }
            using (var crashedTool = Child("ui-crash"))
            {
                await Ready(crashedTool); crashedTool.Kill(); await crashedTool.WaitForExitAsync();
                Check((await client.ReadAsync(default)).RuntimeId == value.RuntimeId, "killing an actively observing WPF tool leaves original runtime online");
            }
            using (var canceledTool = Child("ui-cancel"))
            {
                await canceledTool.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                Check(canceledTool.ExitCode == 0, "closing WPF while a real log query is in flight cancels and drains its owned read: " + await ErrorText(canceledTool));
            }
            await Command(server, "status");
            File.WriteAllText(Path.Combine(root, "registration-changed"), "synthetic replacement");
            await Reject(() => client.ReadAsync(default), "changed installation immediately revokes read authority");
            File.Delete(Path.Combine(root, "registration-changed"));
            using (var ownerLossTool = Child("ui-owner-loss"))
            {
                await Ready(ownerLossTool);
                server.Kill(); await server.WaitForExitAsync();
                await ownerLossTool.StandardInput.WriteLineAsync("close"); await ownerLossTool.StandardInput.FlushAsync();
                var output = ownerLossTool.StandardOutput.ReadToEndAsync();
                await ownerLossTool.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Check(ownerLossTool.ExitCode == 0, "an active debug tool can close after owner loss without fabricating a release receipt: " + await ErrorText(ownerLossTool) + await output);
            }
            await Reject(() => client.ReadAsync(default), "abnormal owner exit cannot yield a cached live result");
            using (var owner = await ReleasedBusinessLease())
                Check(true, "OS releases actual process-held business lease after abnormal exit");
            var before = Directory.GetFiles(Location().DataRoot, "*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".db") || p.EndsWith("-wal") || p.EndsWith("-shm")).ToDictionary(p => p, p => Wire.FileHash(p));
            using (var offline = SoftwareLogReader.OperationalReadOnly(directories[0], Installation().RequireRead))
            {
                var held = before.Keys.Select(p => new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.None)).ToArray();
                try
                {
                    var page = await offline.QueryAsync(new(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddSeconds(1)), null, 0, default);
                    Check(page.Entries.Length > 0, "offline logs work while original DB/WAL/SHM are exclusively locked against observer access");
                }
                finally { foreach (var file in held) file.Dispose(); }
            }
            Check(before.All(p => Wire.FileHash(p.Key) == p.Value), "offline observer leaves database, WAL and SHM bytes unchanged");
            foreach (var file in diagnosticFiles.Where(File.Exists))
                using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            Check(true, "all observer log handles are released after query and UI close");
            using (var offlineUi = Child("ui-offline"))
            { await offlineUi.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); Check(offlineUi.ExitCode == 0, "native WPF offline query and close: " + await ErrorText(offlineUi)); }
            using (var restarted = Child("server-restart"))
            {
                await Ready(restarted); Check((await client.ReadAsync(default)).RuntimeId != value.RuntimeId, "new real process establishes a new authenticated runtime identity");
                await restarted.StandardInput.WriteLineAsync("stop"); await restarted.StandardInput.FlushAsync();
                await restarted.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                Check(restarted.ExitCode == 0, "normal IPC and Application shutdown releases original resources: " + await ErrorText(restarted));
                using var owner = await ReleasedBusinessLease();
                Check(true, "business lease is available after successful normal stop");
            }
            completed = true;
        }
        finally
        {
            if (!server.HasExited) { server.Kill(); await server.WaitForExitAsync(); }
            var errors = await ErrorText(server);
            if (!string.IsNullOrWhiteSpace(errors))
            {
                File.WriteAllText(Path.Combine(root, "rejected-connections.txt"), errors);
                if (!completed) Console.Error.WriteLine("Original owner diagnostics:\n" + errors);
            }
        }
    }
    static void EarlyCloseUi()
    {
        using var programUse = new OperationsToolLease(Installation());
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Ipc.Presentation.Wpf;component/Themes/Workstation.xaml", UriKind.Relative) });
        var window = new OperationsWindow(Installation());
        window.SourceInitialized += (_, _) => window.Close();
        app.Startup += (_, _) => window.Show();
        app.Run();
    }
    static void Ui(bool waitForCrash, bool expectOffline, bool cancelOnQuery)
    {
        using var programUse = new OperationsToolLease(Installation());
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Ipc.Presentation.Wpf;component/Themes/Workstation.xaml", UriKind.Relative) });
        var window = new OperationsWindow(Installation());
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        var attempts = 0; bool queried = false, edited = false;
        timer.Tick += (_, _) =>
        {
            try
            {
                attempts++;
                if (attempts > 100) throw new TimeoutException("WPF read-only query did not complete");
                var tabs = (TabControl)window.FindName("Pages");
                var areas = (ComboBox)window.FindName("LogAreas");
                if (!queried && areas.SelectedIndex >= 0)
                {
                    tabs.SelectedIndex = 1; ((Button)window.FindName("QueryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); queried = true;
                    if (cancelOnQuery) { timer.Stop(); window.Close(); return; }
                }
                var grid = (DataGrid)window.FindName("LogGrid");
                if (!queried || grid.Items.Count == 0 || !((Button)window.FindName("QueryButton")).IsEnabled) return;
                if (!edited)
                {
                    ((CheckBox)window.FindName("KeyEvents")).IsChecked = false; edited = true;
                    Check(grid.Items.Count == 0 && !((Button)window.FindName("NextButton")).IsEnabled, "changing log filters invalidates old results and pinned pagination");
                    ((Button)window.FindName("QueryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return;
                }
                var connection = ((TextBlock)window.FindName("ConnectionText")).Text;
                if (expectOffline ? !connection.StartsWith("未连接") : !connection.StartsWith("已核验")) return;
                Check(expectOffline ? ((ItemsControl)window.FindName("PositionCards")).Items.Count == 0 :
                    ((ItemsControl)window.FindName("PositionCards")).Items.Count == 2, "online overview and offline invalidation reflect actual connection");
                Check(grid.Items.Count <= 200 && window.ActualWidth >= 880, "WPF presents bounded real log page using shared native resources");
                var dpi = VisualTreeHelper.GetDpi(window);
                var bitmap = new RenderTargetBitmap((int)(window.ActualWidth * dpi.DpiScaleX), (int)(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(root, "operations-native-" + Process.GetCurrentProcess().Id + ".png"))) encoder.Save(file);
                Console.WriteLine("NATIVE DPI=" + dpi.PixelsPerInchX + " WindowDIP=" + window.ActualWidth + "x" + window.ActualHeight);
                tabs.SelectedIndex = 0; window.Width = 880; window.Height = 560; window.UpdateLayout();
                var compact = new RenderTargetBitmap((int)(window.ActualWidth * dpi.DpiScaleX), (int)(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                compact.Render(window); var compactEncoder = new PngBitmapEncoder(); compactEncoder.Frames.Add(BitmapFrame.Create(compact));
                using (var file = File.Create(Path.Combine(root, "operations-compact-" + Process.GetCurrentProcess().Id + ".png"))) compactEncoder.Save(file);
                timer.Stop();
                if (waitForCrash) Console.WriteLine("READY"); else window.Close();
            }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; timer.Stop(); window.Close(); }
        };
        timer.Start(); app.Run(window);
        if (Environment.ExitCode != 0) throw new InvalidOperationException("Native UI verification failed");
    }
}
