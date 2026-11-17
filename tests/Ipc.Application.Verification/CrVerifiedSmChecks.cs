using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

// Current SM contracts, real SQLite/configuration files and the built-in RCS over loopback.
// Faults are injected at owned persistence/publication boundaries, never by replaying a request.
internal static class CrVerifiedSmChecks
{
    static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS CR-SM " + message); }
    static T Field<T>(ParcelRuntime app, string name) => (T)typeof(ParcelRuntime)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
    static PositionSnapshot Left(ParcelSnapshot snapshot) => snapshot.Positions!.Single(p => p.Id == "left");
    static string DirectoryFor(BusinessLaunch launch, ParcelConfiguration config) =>
        Path.Combine(launch.DataRoot, config.Mode.ToString(), config.StationId, config.ApplicationId);
    static async Task<Exception> Failure(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) { return error; }
        throw new InvalidOperationException("Expected the injected failure to remain visible");
    }
    static async Task Pause(ParcelRuntime app)
    {
        await ManagementModeFixture.Authorize(app);
        var snapshot = await app.SnapshotAsync();
        Check((await app.CommandAsync(new(Wire.Id(), snapshot.Revision, "Pause"))).Status == "Persisted", "backup fixture pauses through the application contract");
    }

    public static async Task Removal(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, root, deferred: true, mode: RunMode.Simulation);
        await h.StartAsync();
        var first = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "CR-SM-REMOVAL"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(first.OperationId!))?.Wcs?.State == StageState.Accepted);
        await ManagementModeFixture.Authorize(h.App);
        var window = Wire.Id();
        await h.App.SetManagementWorkspaceAsync(window, true, default);
        try
        {
            var before = await h.App.SnapshotAsync();
            Check(Left(before).PlacementRemovalOperation is null && Left(before).CurrentRoundId is not null, "management workspace forbids physical confirmation for the existing round");
            foreach (var token in new string?[] { null, "", " " })
            {
                var receipt = await h.App.CommandAsync(new(Wire.Id(), before.Revision, "ConfirmPlacementRemoval", "left", token));
                Check(receipt.Status != "Persisted" && Left(await h.App.SnapshotAsync()).CurrentRoundId == Left(before).CurrentRoundId,
                    "missing/empty removal token cannot satisfy a closed confirmation guard");
            }
        }
        finally { await h.App.SetManagementWorkspaceAsync(window, false, default); }
        var current = await h.App.SnapshotAsync();
        var valid = new BusinessCommand(Wire.Id(), current.Revision, "ConfirmPlacementRemoval", "left", Left(current).PlacementRemovalOperation);
        Check(valid.Value is not null && (await h.App.CommandAsync(valid)).Status == "Persisted" && Left(await h.App.SnapshotAsync()).CurrentRoundId is null,
            "a current nonempty round/version token still confirms this parcel exactly once");
        await h.NextVehicleAsync();
        var next = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "CR-SM-NEXT"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(next.OperationId!))?.Wcs?.State == StageState.Accepted);
        var nextRound = Left(await h.App.SnapshotAsync()).CurrentRoundId;
        Check((await h.App.CommandAsync(valid with { Id = Wire.Id() })).Status != "Persisted" && Left(await h.App.SnapshotAsync()).CurrentRoundId == nextRound && h.Peer.Requests.Count == 2,
            "the old removal token cannot end or resubmit the next parcel");
    }

    public static async Task Dispatch(string workspace, string root)
    {
        foreach (var storageFailure in new[] { false, true })
        {
            await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "publication-" + storageFailure), deferred: true, mode: RunMode.Simulation);
            await h.StartAsync();
            var command = await h.Command("left", "CR-SM-PUBLICATION");
            h.App.Boundary = phase =>
            {
                if (phase != "BeforeSmInputPublication") return;
                if (storageFailure) throw new IOException("Synthetic business-storage publication failure");
                throw new InvalidOperationException("Synthetic auxiliary publication failure");
            };
            Exception error;
            try { error = await Failure(() => h.App.SubmitParcelBarcodeAsync(command, default)); }
            finally { h.App.Boundary = null; }
            Check(error is IOException == storageFailure, "the original publication failure remains visible to its caller");
            var operation = (await h.App.SnapshotAsync()).Operations.Single();
            if (storageFailure)
            {
                await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity!.ManagedExecutions == 0);
                Check(h.App.FatalFailure.IsCompleted && h.Peer.Requests.IsEmpty && (await h.App.OperationAsync(operation.Id))!.Wcs?.Attempts is null or 0,
                    "registered execution still respects the real storage-failure guard and sends nothing");
            }
            else
            {
                await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(operation.Id))?.Wcs?.State == StageState.Accepted);
                var replay = await h.App.SubmitParcelBarcodeAsync(command, default);
                Check(replay.OperationId == operation.Id && h.Peer.Requests.Count == 1 && (await h.App.OperationAsync(operation.Id))!.Wcs!.Attempts == 1,
                    "durably claimed work completes after auxiliary failure; original command reconciliation never resends");
            }
        }
    }

    public static async Task LiveDevices(string workspace, string root)
    {
        foreach (var phase in new[] { "start-audit", "configuration-write" })
        {
            await using var h = new CameraRoundHarness(workspace, Path.Combine(root, phase));
            await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
            var before = await h.App.SnapshotAsync();
            var originalPorts = Field<ParcelPorts>(h.App, "ports");
            var originalDecoder = h.Factory.Decoders[h.Left];
            var originalRight = h.Factory.Sources[h.Right];
            var directory = DirectoryFor(h.Launch, h.Config);
            var ledger = Path.Combine(directory, "configuration.json"); var originalHash = Wire.FileHash(ledger);
            var readers = before.Configuration.Effective.Topology!.Readers.Select(r => r.DeviceId == h.Left
                ? r with { Parameters = new(r.Parameters!) { ["device.ip"] = "192.0.2.99" } } : r).ToArray();
            var candidate = before.Configuration.Effective with { Reader = readers[0], Topology = before.Configuration.Effective.Topology with { Readers = readers } };
            h.Factory.Expect(candidate);
            var repository = Field<ParcelRepository>(h.App, "repository");
            FileStream? held = null;
            try
            {
                if (phase == "start-audit") repository.Boundary = name =>
                { if (name == "BeforeBusinessCommit:LiveDeviceApplyStarted") throw new InvalidOperationException("Synthetic live-apply audit failure"); };
                else held = new FileStream(ledger, FileMode.Open, FileAccess.Read, FileShare.Read);
                BusinessReceipt? receipt = null;
                try { receipt = await h.App.StageConfigurationAsync(before.Configuration.Revision, candidate, "CR-SM live replacement failure"); }
                catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException) { }
                Check(receipt?.Status != "Effective" && Wire.FileHash(ledger) == originalHash &&
                    (await h.App.SnapshotAsync()).Configuration.Revision == before.Configuration.Revision,
                    phase + " retains the exact original authoritative configuration");
                Check(ReferenceEquals(Field<ParcelPorts>(h.App, "ports").Reader, originalPorts.Reader) &&
                    ReferenceEquals(h.Factory.Sources[h.Right], originalRight) && h.Factory.Decoders[h.Left].ReleaseCalls > 0,
                    phase + " never adopts the uncommitted candidate, releases it and preserves the other camera");
                var fences = Field<System.Collections.Concurrent.ConcurrentDictionary<string, byte>>(h.App, "maintainedResources");
                Check(phase == "start-audit" ? !fences.ContainsKey(h.Left) && originalDecoder.ReleaseCalls == 0 : fences.ContainsKey(h.Left),
                    "pre-stop failure releases the temporary fence; post-stop failure retains affected-device isolation");
            }
            finally { repository.Boundary = null; held?.Dispose(); }
            await h.App.DisposeAsync();
            Check(!h.App.HasUnreleasedResources, phase + " retains all failed owners through real shutdown cleanup");
        }
    }

    public static Task BackupOwnership(string workspace, string root)
    {
        Directory.CreateDirectory(root);
        var backup = Path.Combine(root, "foreign.db");
        using (var repository = new ParcelRepository(Path.Combine(root, "source"), "sportmaster")) repository.Backup(backup);
        // A foreign current-format database is a boundary fixture, not development of another business module.
        using (var database = new SqliteConnection("Data Source=" + backup + ";Pooling=False"))
        {
            database.Open(); using var command = database.CreateCommand();
            command.CommandText = "UPDATE platform_migrations SET application='direct-plc'"; command.ExecuteNonQuery();
        }
        var recovery = Directory.CreateDirectory(backup + ".recovery").FullName;
        var completion = Path.Combine(recovery, "COMPLETED.json");
        var manifest = new ParcelRecoveryManifest("sportmaster", Path.GetFileName(backup), Wire.FileHash(backup), [], default, "Synthetic incomplete foreign backup");
        ExtensionStore.AtomicWrite(completion, manifest);
        var databaseHash = Wire.FileHash(backup); var completionHash = Wire.FileHash(completion);
        try { _ = ParcelRecovery.PlanSetDeletion(backup, "sportmaster", () => { }, default); throw new Exception("Self-declared manifest granted foreign database deletion"); }
        catch (InvalidDataException) { }
        Check(Wire.FileHash(backup) == databaseHash && Wire.FileHash(completion) == completionHash && !File.Exists(backup + ".deleting.json"),
            "an incomplete manifest cannot turn another application's database into an SM deletion inventory");
        ExtensionStore.AtomicWrite(completion, manifest with { Application = "direct-plc" });
        var foreign = ParcelRecovery.PlanSetDeletion(backup, "direct-plc", () => { }, default);
        foreach (var status in new[] { "NotStarted", "Partial" })
        {
            ExtensionStore.AtomicWrite(backup + ".deleting.json", foreign with { Status = status });
            var originals = new[] { backup, completion, backup + ".deleting.json" }.ToDictionary(p => p, Wire.FileHash);
            using var execution = new MaintenanceExecution();
            try { _ = ParcelRecovery.DeleteSet(backup, foreign.Identity, "sportmaster", () => { }, _ => { }, execution); throw new Exception("Foreign deletion receipt bypassed current business ownership"); }
            catch (InvalidDataException) { }
            Check(originals.All(p => File.Exists(p.Key) && Wire.FileHash(p.Key) == p.Value),
                status + " deletion resume rejects foreign ownership before mutating any frozen byte");
        }
        using (var execution = new MaintenanceExecution())
            Check(ParcelRecovery.DeleteSet(backup, foreign.Identity, "direct-plc", () => { }, _ => { }, execution).Status == "Deleted",
                "the original owner can still finish the exact attributable incomplete deletion");
        return Task.CompletedTask;
    }

    public static async Task BackupFences(string workspace, string root)
    {
        foreach (var phase in new[] { "HeartbeatMaintenanceLease", "HeartbeatMaintenanceReleased", "both-storage" })
        {
            await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, phase), deferred: true, mode: RunMode.Simulation);
            await h.StartAsync(); await Pause(h.App);
            var repository = Field<ParcelRepository>(h.App, "repository");
            repository.Boundary = name =>
            {
                if (phase == "both-storage" && name is "BeforeBusinessCommit:HeartbeatMaintenanceLease" or "BeforeBusinessCommit:HeartbeatMaintenanceReleased")
                    throw new IOException("Synthetic source " + name);
                if (name == "BeforeBusinessCommit:" + phase) throw new InvalidOperationException("Synthetic " + phase);
            };
            Exception error;
            try { error = await LifecycleOptimization.AfterMonitorDrain(h.App, () => Failure(() => h.App.BackupAsync(Path.Combine(root, phase, "first.db")))); }
            finally { repository.Boundary = null; }
            Check(!Field<bool>(h.App, "backupInProgress"), phase + " releases only its owned temporary ingress fence on every failure path");
            if (phase == "both-storage")
            {
                Check(error is AggregateException aggregate && aggregate.Flatten().InnerExceptions.Count == 2 && h.App.FatalFailure.IsCompleted,
                    "preparation and cleanup errors both survive while the real source-storage guard remains active");
                Check((await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "CR-SM-STORAGE-BLOCKED"), default)).Status != "Admitted" && h.Peer.Requests.IsEmpty,
                    "temporary fence release cannot bypass damaged-source admission protection");
            }
            else
            {
                Check(error is InvalidOperationException, "heartbeat maintenance failure is not reported as backup success");
                await CameraRoundHarness.Until(async () => await h.App.CheckRcsHeartbeatAsync(default) == RcsHeartbeatResult.Received);
                var result = await LifecycleOptimization.AfterMonitorDrain(h.App, () => h.App.BackupAsync(Path.Combine(root, phase, "next.db")));
                Check(result.Status == "BackupCreated", "a later explicit backup can complete after the original failure without restarting");
            }
        }
    }

    public static async Task Templates(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, root, deferred: false, mode: RunMode.Simulation);
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        var vm = new ParcelViewModel(h.App, "参数", "合成 CR 回归");
        await vm.RefreshAsync(); await vm.LoadCapabilitiesAsync();
        var before = vm.BuildStructuredConfiguration();
        var originals = ParcelBindings.Readers(before).ToDictionary(r => r.DeviceId, r => Wire.Encode(r.Parameters));
        var template = vm.ExportSiteTemplate();
        template.Defaults.Values["policy.ReadTimeoutMs"] = "1700";
        vm.ApplySiteTemplate(vm.PreviewSiteTemplate(template));
        var after = vm.BuildStructuredConfiguration();
        Check(after.EffectiveReadTimeoutMs == 1700 && ParcelBindings.Readers(after).All(r => originals[r.DeviceId] == Wire.Encode(r.Parameters)),
            "template policy applies while each camera retains its own IP, serial and recognition parameters despite different editor row IDs");
        Check((await h.App.SnapshotAsync()).Configuration.Revision == vm.Snapshot!.Configuration.Revision && h.Factory.CreateCalls == 1 && h.Peer.Requests.IsEmpty,
            "template application changes only the complete draft, with no device reconnect, save or business request");
    }

    public static async Task Preferences(string workspace, string root)
    {
        var originalRoot = PresentationPreferences.UserRootForVerification;
        var userRoot = Path.Combine(root, "preferences");
        PresentationPreferences.UserRootForVerification = () => userRoot;
        try
        {
            var directory = Directory.CreateDirectory(Path.Combine(userRoot, "presentation")).FullName;
            foreach (var (surface, property, text) in new[]
            {
                ("window", "WindowOpeningMode", "{\"Schema\":2,\"Mode\":\"Window\"}"),
                ("pace", "ShowPace", "{\"Schema\":2,\"Compact\":true}"),
                ("audio", "CueVolume", "{\"Schema\":1,\"Volume\":101}")
            })
            {
                var file = Path.Combine(directory, "sportmaster." + surface + ".json"); File.WriteAllText(file, text);
                var draft = new SmDisplayPreferenceDraft();
                Check(!draft.CanEdit(property) && draft.ReadFailure(property).Length > 0 && File.ReadAllText(file) == text,
                    "invalid " + surface + " preference remains readable as a protected draft failure without replacing its original bytes");
                File.Delete(file);
            }
            var preferences = new SmDisplayPreferenceDraft { CueVolume = 40, ShowPace = false };
            var audio = Path.Combine(directory, "sportmaster.audio.json"); File.WriteAllText(audio, "{\"Schema\":1,\"Volume\":101}");
            var saved = preferences.Save();
            Check(saved.Single(r => r.Path == "CueVolume").State == "Unknown" && saved.Single(r => r.Path == "ShowPace").State == "Saved" &&
                preferences.HasChanges && File.ReadAllText(audio).Contains("101", StringComparison.Ordinal),
                "invalid stored audio reports a per-item failure while another preference saves independently");
            File.WriteAllText(audio, "{\"Schema\":1,\"Volume\":0}");
            Check(preferences.Save() is [{ Path: "CueVolume", State: "Saved" }] && !preferences.HasChanges && PresentationPreferences.ReadCueVolume("sportmaster") == 40,
                "preference retry saves only the failed item and retains the earlier successful item");
            await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "station"), deferred: true, mode: RunMode.Simulation);
            await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
            var before = await h.App.SnapshotAsync();
            var ledger = Path.Combine(DirectoryFor(h.Launch, h.Config), "configuration.json"); var original = File.ReadAllBytes(ledger);
            try
            {
                File.WriteAllText(ledger, new string(' ', 4 * 1024 * 1024 + 1));
                var inspection = await h.App.InspectConfigurationSubmissionAsync(before.RuntimeId, before.Configuration.Revision, before.Configuration.Effective);
                Check(inspection.Status == "Unknown" && new FileInfo(ledger).Length == 4 * 1024 * 1024 + 1,
                    "oversized authoritative envelope returns unknown submission status and preserves the original evidence");
            }
            finally { File.WriteAllBytes(ledger, original); }
            var vm = new ParcelViewModel(h.App, "参数", "合成 CR 回归");
            await vm.RefreshAsync(); await vm.LoadCapabilitiesAsync(); vm.LoadWindowPreference("sportmaster");
            vm.StructuredPolicy!.ReadTimeoutMs = "1700"; vm.SmPreferences.CueVolume = 101;
            Check(!await vm.PrepareConfigurationReviewAsync() && !vm.CanSubmitReviewedConfiguration &&
                vm.SmPreferences.CueVolume == 101 && (await h.App.SnapshotAsync()).Configuration.Revision == before.Configuration.Revision,
                "combined business/preference review rejects an invalid volume before any business commit and preserves both drafts");
        }
        finally { PresentationPreferences.UserRootForVerification = originalRoot; }
    }
}
