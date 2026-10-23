using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Application.DirectPlc;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Assembly;
using Ipc.Platform;

// Real runtime, SQLite, management service and loopback TCP. Faults are confined to
// synthetic adapters or existing internal durability boundaries, never a DB substitute.
static partial class ArchitectureQualityRemediation
{
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception("FAIL AQA01: " + message); Console.WriteLine("PASS AQA01: " + message); }
    static ParcelConfiguration Config(string workspace, string id) => Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config", id, "business.json")));
    static BusinessLaunch Launch(string path) => new(path, Path.Combine(path, "data"), null, null);
    static string DirectoryFor(string path, ParcelConfiguration c) => Path.Combine(path, "data", c.Mode.ToString(), c.StationId, c.ApplicationId);
    static async Task<ParcelRuntime> Open(ParcelConfiguration c, string path, Func<ParcelCapabilityFactory, IParcelCapabilityFactory>? wrap = null)
    {
        Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), c);
        var launch = Launch(path); var inner = new ParcelCapabilityFactory(launch);
        var app = new ParcelRuntime(launch, c.ApplicationId == "sportmaster" ? new SportmasterFlow() : new DirectPlcFlow(), wrap?.Invoke(inner) ?? inner);
        try { await app.StartAsync(default); return app; } catch { await app.DisposeAsync(); throw; }
    }
    static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        while (DateTimeOffset.UtcNow < deadline)
        { var s = await app.SnapshotAsync().WaitAsync(TimeSpan.FromSeconds(2)); if (predicate(s)) return s; await Task.Delay(20); }
        throw new TimeoutException(Wire.Encode(await app.SnapshotAsync()));
    }
    static async Task<BusinessReceipt> Command(ParcelRuntime app, string kind, string? owner = null, string? value = null, bool drain = false)
    {
        for (int n = 0; n < 100; n++)
        {
            var s = await app.SnapshotAsync();
            var r = await app.CommandAsync(new(Wire.Id(), s.Revision, kind, owner, value));
            if (r.Status == "Conflict" || drain && r.Status == "Rejected" && r.Reason == "Unsupported command or an application guard is closed")
            { await Task.Delay(20); continue; } // Explicit pre-I/O rejection only.
            return r;
        }
        throw new TimeoutException("Command CAS did not settle");
    }
    static CapabilityBinding Binding(string kind) => new(kind, "reference-" + kind, "1.0.0", "aqa." + kind,
        "reference://aqa/" + kind, "PARCEL-REFERENCE-v1", []);
    public static async Task Run(string workspace, string root)
    {
        await QueueExpiry(workspace, Path.Combine(root, "queue"));
        await UntrustedSequenceBoundaries(workspace, Path.Combine(root, "sequence-boundaries"));
        await ArchiveRollback(workspace, Path.Combine(root, "archive-rollback"));
        await ReceiptCommandBoundaries(workspace, Path.Combine(root, "receipt-commands"));
        await ExpiredWeight(workspace, Path.Combine(root, "expired-weight"));
        await TcpConsumersAndProjection(workspace, Path.Combine(root, "tcp"));
        foreach (var boundary in new[] { "AfterAbandonedDurable", "AfterImageReleaseDurable", "AfterContinuationAdmitted", "AfterCommandAdmitted", "AfterConfigurationConfirmed", "AfterArchiveCommit" })
            await CommandCrash(workspace, Path.Combine(root, boundary), boundary);
        Console.WriteLine("PASS AQA01 COMPLETE");
    }
    static async Task ExpiredWeight(string workspace, string path)
    {
        var c = Config(workspace, "direct-plc") with { Weight = new(WeightSource.Measured, WeightUnit.Kg, 3, MaximumAgeMs: 500), Scale = Binding("weight"), WeightValidity = WeightValidity.UntilDispatch };
        await using var app = await Open(c, path);
        app.EmitReference("expired-weight", "123", "weight-source");
        await Wait(app, s => s.Operations.FirstOrDefault()?.State == ParcelState.WaitingWeight);
        Check((await Command(app, "Pause")).Status == "Persisted", "pause before associating original weight");
        app.EmitReferenceWeight(new(Wire.Id(), "expired-weight", c.Scale!.Target, 2.345m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow));
        var s = await Wait(app, x => x.Operations.Single().Weight is not null); var original = s.Operations.Single().Weight;
        await Task.Delay(c.Weight.MaximumAgeMs + 80);
        s = await app.SnapshotAsync();
        Check(s.Operations.Single().Weight == original && s.Operations.Single().Plc is null && !s.Availability!.Resume && (await Command(app, "Resume")).Status == "Rejected",
            "UntilDispatch expired same-parcel weight remains original evidence and prevents PLC/Resume; recovery fixture policy does not bypass this guard");
    }
    static async Task QueueExpiry(string workspace, string path)
    {
        var c = Config(workspace, "sportmaster") with { TimeoutMs = 1000 };
        SlowFactory? factory = null;
        await using (var app = await Open(c, path, inner => factory = new(inner)))
        {
            Check(app.EmitReferenceAt("left", "first", "111", "first-source"), "first fresh left input enqueued");
            await factory!.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(app.EmitReferenceAt("left", "second", "222", "second-source") && app.EmitReferenceAt("left", "expired", "333", "third-source"), "two fresh observations accepted during first external wait");
            app.EmitReferenceAt("right", "right", "444", "right-source");
            var s = await Wait(app, x => x.Inputs!.Any(i => i.Observation.AdmissionId == "expired" && i.Outcome == ParcelInputOutcome.Rejected));
            var input = s.Inputs!.Single(i => i.PositionId == "left");
            Check(input.Reason.StartsWith("ObservationExpired") && s.Operations.Count(o => o.PositionId == "left") == 2 &&
                s.Operations.All(o => o.AdmissionId != "expired") && s.Positions!.Single(p => p.Id == "left").CurrentOperation is null,
                "queue-expired current input is durable rejection without an operation/request or borrowed prior slot");
            var vm = new SupplyStationViewModel(app); await vm.RefreshAsync();
            Check(vm.Left.Barcode == "333" && !vm.Left.SlotAvailable && !vm.Left.NextStep.Contains("按本次格口投递") && vm.Right.SlotAvailable,
                "actual shared UI projection suppresses left delivery instruction while right stays independent");
            var reader = factory.Readers![c.Reader.DeviceId]; var revision = s.Revision;
            reader.Raw(input.Observation with { Id = Wire.Id(), SourceEventId = "old-sequence", Codes = ["OLD"] });
            s = await Wait(app, x => x.Revision > revision);
            Check(Wire.Encode(s.Inputs!.Single(i => i.PositionId == "left")) == Wire.Encode(input), "old sequence cannot overwrite current rejected input");
            revision = s.Revision;
            reader.Raw(input.Observation with { Id = Wire.Id(), SourceEventId = "already-stale-at-ingress", Sequence = 9000, ObservedAt = DateTimeOffset.UtcNow.AddHours(-1) });
            s = await Wait(app, x => x.Revision > revision);
            Check(Wire.Encode(s.Inputs!.Single(i => i.PositionId == "left")) == Wire.Encode(input), "source already stale at ingress remains audit-only and cannot poison sequence");
        }
        await using (var app = await Open(c, path, inner => factory = new(inner)))
        {
            var s = await app.SnapshotAsync(); var input = s.Inputs!.Single(i => i.PositionId == "left");
            Check(input.Outcome == ParcelInputOutcome.Rejected && input.Reason.StartsWith("ObservationExpired") && s.Positions!.Single(p => p.Id == "left").CurrentOperation is null,
                "direct runtime restart restores queue-expired rejection and does not resurrect previous terminal slot");
            var reader = factory!.Readers![c.Reader.DeviceId]; var vm = new SupplyStationViewModel(app);
            await Command(app, "Pause"); await ManagementModeFixture.Authorize(app);
            Check((await app.ReconnectDeviceAsync(c.Reader.DeviceId)).Status == "Reconnected" && !reader.Old(input.Observation), "old registration fenced after real reconnect");
            Check((await Command(app, "Resume")).Status == "Persisted", "resume only after controlled reconnect");
            app.EmitReferenceAt("left", "second", "222", "second-source");
            s = await Wait(app, x => x.Inputs!.Any(i => i.Outcome == ParcelInputOutcome.Duplicate));
            await vm.RefreshAsync();
            Check(s.Operations.Count(o => o.PositionId == "left") == 2 && !vm.Left.SlotAvailable, "duplicate old entity creates no request and cannot display a new-epoch slot");
            app.EmitReferenceAt("left", "last-expired", "555", "last-source");
            await Wait(app, x => x.Operations.Any(o => o.AdmissionId == "last-expired" && o.State == ParcelState.SlotResolved));
            // A valid current registration whose health expires must also close this input's display.
            reader.ExpireHealth();
            reader.Emit("health-expired", "666");
            await Wait(app, x => x.Inputs!.Any(i => i.Observation.AdmissionId == "health-expired" && i.Outcome == ParcelInputOutcome.Blocked));
        }
        await using var reopened = await Open(c, path);
        var restored = await reopened.SnapshotAsync();
        Check(restored.Inputs!.Single(i => i.PositionId == "left").Outcome == ParcelInputOutcome.Blocked && restored.Positions!.Single(p => p.Id == "left").CurrentOperation is null,
            "restart restores persisted failed input without resurrecting terminal slot; prior operations remain history");
    }
    static async Task TcpConsumersAndProjection(string workspace, string path)
    {
        var listeners = new[] { new TcpListener(IPAddress.Loopback, 0), new TcpListener(IPAddress.Loopback, 0) };
        foreach (var l in listeners) l.Start();
        try
        {
            var c = Config(workspace, "sportmaster");
            var readers = c.Topology!.Readers.Select((r, i) => r with { Driver = "tcp-line", Parameters = new()
            { ["device.ip"] = "127.0.0.1", ["device.port"] = ((IPEndPoint)listeners[i].LocalEndpoint).Port.ToString(), ["entity.mode"] = "line" } }).ToArray();
            c = c with { Reader = readers[0], Topology = c.Topology with { Readers = readers } };
            await using var app = await Open(c, path);
            using var left = await listeners[0].AcceptTcpClientAsync(); using var right = await listeners[1].AcceptTcpClientAsync();
            await left.GetStream().WriteAsync(Encoding.UTF8.GetBytes("000321\n"));
            var s = await Wait(app, x => x.Operations.Any(o => o.State == ParcelState.SlotResolved)); var id = s.Operations.Single().Id;
            async Task Redacted(string when)
            {
                var ordinary = await app.SnapshotAsync(); var op = await app.OperationAsync(id); var history = await app.HistoryAsync();
                Check(ordinary.Configuration.Effective.Reader.Parameters is null && ordinary.Inputs!.Single().ReaderBinding!.Parameters is null &&
                    op!.ReaderBinding!.Parameters is null && op.Wcs!.Request.Payload == "" && history.Items.Single().Operation.ReaderBinding!.Parameters is null,
                    "all ordinary DTO reader/configuration/request branches redacted: " + when);
            }
            await Redacted("before authorization");
            var password = "Aqa01-" + Wire.Id(); await app.ConfigureManagementPasswordAsync(password); Check(await app.EnterManagementModeAsync(password), "real management password authorizes synthetic TCP root");
            var original = await app.SnapshotAsync();
            Check(original.Inputs!.Single().ReaderBinding!.Parameters!["device.ip"] == "127.0.0.1" && original.Operations.Single().Wcs!.Request.Payload.Length > 0,
                "authorized projection retains original descriptor and raw T1 payload");
            await app.ExitManagementModeAsync(); await Redacted("explicit exit");
            Check(await app.EnterManagementModeAsync(password), "re-entry retains original management credential");
            var authority = LocalManagementMode.ForRoot(Launch(path).DataRoot); authority.Clock = () => DateTimeOffset.UtcNow.AddMinutes(16);
            try { await Redacted("real session expiry"); } finally { authority.Clock = () => DateTimeOffset.UtcNow; }
            Check(await app.EnterManagementModeAsync(password), "re-entry after expiry");
            var replacement = "Aqa01-New-" + Wire.Id(); await app.ChangeManagementPasswordAsync(replacement); await Redacted("password change revokes session");
            Check(await app.EnterManagementModeAsync(replacement), "new password re-entry");
            var after = await app.SnapshotAsync();
            Check(Wire.Encode(after.Inputs) == Wire.Encode(original.Inputs) && Wire.Encode(after.Operations) == Wire.Encode(original.Operations), "projection never mutates durable original inputs or requests");
            await Command(app, "Pause");
            var config = (await app.SnapshotAsync()).Configuration;
            BusinessReceipt staged;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
            do
            {
                await Wait(app, x => x.Availability?.Configure == true);
                staged = await app.StageConfigurationAsync(config.Revision, config.Effective with { TimeoutMs = 1800 }, "AQA01 synthetic TCP pending reference check");
            } while (DateTimeOffset.UtcNow < deadline && staged.Status == "Rejected" && staged.Reason == "Pause and authenticate maintenance before configuration");
            Check(staged.Status == "PendingRestart", "real CAS saves TCP Pending while original operation remains frozen");
            var db = Path.Combine(DirectoryFor(path, c), "sportmaster.db"); var backup = Path.Combine(path, "tcp.backup.db");
            BusinessReceipt backed;
            deadline = DateTimeOffset.UtcNow.AddSeconds(8);
            do
            {
                await Wait(app, x => x.Availability?.Configure == true); backed = await app.BackupAsync(backup);
            } while (DateTimeOffset.UtcNow < deadline && backed.Status == "Rejected" && backed.Reason == "Pause before reference maintenance backup");
            Check(backed.Status == "BackupCreated" && ParcelRepository.InspectRequiredPackages(db).Length == 0 &&
                ParcelRepository.InspectRequiredPackages(backup).Length == 0, "live Effective/Pending/Frozen and complete backup recognize built-in TCP without extension references");
            var saved = Path.Combine(backup + ".recovery", "configuration.json");
            var envelope = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(saved));
            var badReaders = readers.Select(r => r with { Driver = "future-reader" }).ToArray();
            var bad = envelope.Pending! with { Reader = badReaders[0], Topology = envelope.Pending.Topology! with { Readers = badReaders } };
            ExtensionStore.AtomicWrite(saved, envelope with { Pending = bad });
            var manifestPath = Path.Combine(backup + ".recovery", "COMPLETED.json"); var manifest = Wire.Decode<ParcelRecoveryManifest>(File.ReadAllText(manifestPath));
            manifest.Files["configuration.json"] = Wire.FileHash(saved); ExtensionStore.AtomicWrite(manifestPath, manifest);
            try { ParcelRepository.InspectRequiredPackages(backup); throw new Exception("Unknown driver accepted"); }
            catch (InvalidDataException ex) { Check(ex.Message.Contains("unknown reader"), "unknown future reader in a valid-custody backup still fails closed"); }
        }
        finally { foreach (var l in listeners) l.Stop(); }
    }
    public static async Task CrashChild(string workspace, string path, string boundary)
    {
        if (boundary.StartsWith("ReceiptQuery", StringComparison.Ordinal)) { await ReceiptQueryChild(workspace, path, boundary); return; }
        var c = Config(workspace, boundary is "AfterCommandAdmitted" or "AfterConfigurationConfirmed" or "AfterArchiveCommit" ? "sportmaster" : "direct-plc");
        if (c.ApplicationId == "direct-plc") c = c with { Weight = new(WeightSource.Measured, WeightUnit.Kg, 3), Scale = Binding("weight"), Images = new(Binding("camera"), null) };
        if (boundary is "AfterCommandAdmitted" or "AfterConfigurationConfirmed")
        {
            await using (var first = await Open(c, path)) { }
            ExtensionStore.AtomicWrite(Path.Combine(DirectoryFor(path, c), "configuration.json"), new ConfigurationRevision<ParcelConfiguration>(17, c, null, "Effective", null));
        }
        var app = await Open(c, path);
        var password = Environment.GetEnvironmentVariable("IPC_AQA01_FIXTURE_PASSWORD") ?? throw new InvalidOperationException("Missing isolated fixture credential");
        await app.ConfigureManagementPasswordAsync(password); Check(await app.EnterManagementModeAsync(password), "child uses real management service");
        string kind; string? owner;
        if (boundary is "AfterCommandAdmitted" or "AfterConfigurationConfirmed")
        {
            var s = await Wait(app, x => x.Availability?.Configure == true); kind = "ConfirmConfiguration";
            owner = s.RuntimeId.ToString("N") + ":" + s.Configuration.Revision + ":" + s.Configuration.Confirmation!.EffectiveHash;
        }
        else
        {
            app.EmitReference("crash-parcel", "777", "crash-source");
            var s = await Wait(app, x => boundary == "AfterArchiveCommit" ? x.Operations.FirstOrDefault()?.State == ParcelState.SlotResolved : x.Operations.FirstOrDefault()?.Capture?.State == StageState.Accepted);
            owner = s.Operations.Single().Id; await Command(app, "Pause");
            kind = boundary == "AfterArchiveCommit" ? "ArchiveHistory" : boundary == "AfterContinuationAdmitted" ? "ContinuePrepared" : "AbandonUnsubmitted";
            if (kind == "ContinuePrepared")
            {
                Check(app.EmitReferenceWeight(new(Wire.Id(), "crash-parcel", c.Scale!.Target, 2.345m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow)), "same-entity weight enters real measurement path");
                await Wait(app, x => x.Operations.Single().Weight is not null); await Command(app, "Resume");
            }
            if (kind == "ArchiveHistory") { owner = null; await Wait(app, x => x.Availability?.Configure == true); }
        }
        app.Boundary = point => { if (point == boundary) Environment.Exit(77); };
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var before = await app.SnapshotAsync(); var command = new BusinessCommand(Wire.Id(), before.Revision, kind, owner, "AQA01 synthetic inspected original");
            File.WriteAllText(Path.Combine(path, "command.json"), Wire.Encode(command));
            var receipt = await app.CommandAsync(command);
            if (receipt.Status == "Conflict" || receipt.Status == "Rejected" && (receipt.Reason == "Unsupported command or an application guard is closed" || receipt.Reason.StartsWith("Current resource health")))
            { await Task.Delay(20); continue; }
            throw new Exception("Durability boundary not reached: " + boundary + "; " + Wire.Encode(receipt));
        }
        throw new TimeoutException("Crash command pre-I/O guard did not settle");
    }
    static async Task CommandCrash(string workspace, string path, string boundary)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        var password = "Aqa01-" + Wire.Id(); psi.Environment["IPC_AQA01_FIXTURE_PASSWORD"] = password;
        foreach (var arg in new[] { typeof(ArchitectureQualityRemediation).Assembly.Location, workspace, "--aqa01-crash-child", path, boundary }) psi.ArgumentList.Add(arg);
        using var child = Process.Start(psi)!; var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
        catch { child.Kill(true); throw; }
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "child.stdout.log"), await stdout); File.WriteAllText(Path.Combine(path, "child.stderr.log"), await stderr);
        Check(child.ExitCode == 77, "actual process crash at " + boundary + ": " + await stderr);
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(path, "business.json"))); var command = Wire.Decode<BusinessCommand>(File.ReadAllText(Path.Combine(path, "command.json")));
        BusinessReceipt original;
        using (var repo = new ParcelRepository(DirectoryFor(path, c), c.ApplicationId))
        {
            original = repo.PriorCommand(command) ?? throw new Exception("Durable business state has no command identity");
            Check(original.Status == (boundary is "AfterCommandAdmitted" or "AfterConfigurationConfirmed" ? "Pending" : "Persisted"), "command identity survives atomic local commit or interrupted cross-resource action");
        }
        await using var app = await Open(c, path);
        Check(await app.CommandAsync(command) == original, "same-content replay returns exact stored receipt after restart: " + boundary);
        Check((await app.CommandAsync(command with { Value = "changed" })).Status == "Rejected", "same ID changed content rejected before revision or guard: " + boundary);
        var snapshot = await app.SnapshotAsync();
        if (boundary is "AfterCommandAdmitted" or "AfterConfigurationConfirmed")
        {
            Check(snapshot.AuxiliaryReasons!.Any(x => x.Contains("CommandPending:" + command.Id)) && snapshot.Configuration.Confirmation!.Required == (boundary == "AfterCommandAdmitted"), "interrupted identity visible alongside actual file confirmation state; replay does not re-confirm");
            Check((await Command(app, "CloseInterruptedCommand", command.Id, "unauthorized inspection")).Status == "Rejected", "ordinary mode cannot close an interrupted command");
            Check(await app.EnterManagementModeAsync(password), "real parent management re-entry after child crash");
            await Wait(app, x => x.Availability?.Configure == true);
            var beforeInspection = await app.SnapshotAsync();
            var inspected = await Command(app, "CloseInterruptedCommand", command.Id, "Synthetic inspection of original configuration; no business outcome asserted", drain: true);
            Check(inspected.Status == "Persisted" && (await app.CommandAsync(command)).Status == "Interrupted" &&
                Wire.Encode((await app.SnapshotAsync()).Configuration) == Wire.Encode(beforeInspection.Configuration) &&
                !(await app.SnapshotAsync()).AuxiliaryReasons!.Any(x => x.Contains("CommandPending:" + command.Id)), "explicit guarded inspection closes only command responsibility, retaining configuration and original guards");
        }
        else if (boundary == "AfterArchiveCommit") Check((await app.HistoryAsync()).Items.Single().Archived, "archive and command identity share one committed transaction");
        else
            Check(snapshot.Operations.Single().Plc is null && (boundary == "AfterContinuationAdmitted" ? snapshot.Operations.Single().RecoveryRequired : snapshot.Operations.Single().State == ParcelState.Abandoned && snapshot.Operations.Single().ImageReleased),
                "replay never creates PLC; recovery retains explicit inspection or completes durable image cleanup");
    }
    sealed class SlowFactory(ParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Dictionary<string, ReaderProbe>? Readers;
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public ParcelPorts Create(ParcelConfiguration c)
        {
            var p = inner.Create(c); Readers = p.Readers!.ToDictionary(x => x.Key, x => new ReaderProbe(x.Value));
            return p with { Reader = Readers[c.Reader.DeviceId], Readers = Readers.ToDictionary(x => x.Key, x => (IControlledObservationSource)x.Value), Wcs = new SlowWcs(p.Wcs!, Entered) };
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class SlowWcs(IWcsPort inner, TaskCompletionSource entered) : IWcsPort
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public async Task<StageReply> ResolveSlotAsync(FrozenRequest r, CancellationToken t)
        { if (r.AdmissionId is "first" or "second") { entered.TrySetResult(); await Task.Delay(650, t); } return await inner.ResolveSlotAsync(r, t); }
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => inner.QueryAsync(r, t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class ReaderProbe(IControlledObservationSource inner) : IControlledObservationSource, ISyntheticObservationInput
    {
        Func<Observation, bool>? callback, old; Action<DeviceHealthConfirmation>? health; bool healthExpired;
        public string DeviceId => inner.DeviceId; public string Epoch => inner.Epoch; public string ConfirmationSource => inner.ConfirmationSource;
        public bool Raw(Observation o) => callback?.Invoke(o) == true;
        public bool Old(Observation o) => old?.Invoke(o) == true;
        public void ExpireHealth() { healthExpired = true; health?.Invoke(new(DeviceId, Epoch, DeviceHealth.Faulted, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ConfirmationSource)); }
        public bool Emit(string admission, string code) => Emit("Read", admission, [code], null, Wire.Id());
        public bool Emit(string kind, string admission, string[] codes, int? quantity, string source) => ((ISyntheticObservationInput)inner).Emit(kind, admission, codes, quantity, source);
        public Task StartAsync(Func<Observation, bool> r, Action<string, string, DeviceHealth> h, CancellationToken t) => throw new NotSupportedException();
        public Task StartControlledAsync(string e, Func<Observation, bool> r, Action<DeviceHealthConfirmation> h, CancellationToken t)
        { callback = r; health = h; healthExpired = false; return inner.StartControlledAsync(e, r, value => { if (!healthExpired) h(value); }, t); }
        public Task StopAsync(CancellationToken t) { old = callback; return inner.StopAsync(t); }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
