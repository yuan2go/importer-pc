using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Adapters;
using Ipc.Assembly;
using Ipc.Components;
using Ipc.Integrations;
using Ipc.Platform;

static class CommercialProductization
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL " + message); Console.WriteLine("PASS " + message); }
    static async Task Reject<T>(Func<Task> action, string message) where T : Exception
    { try { await action(); throw new Exception("Expected rejection: " + message); } catch (T) { Check(true, message); } }
    internal sealed class SyntheticFactory(BusinessLaunch launch, bool failOriginal = false) : IParcelCapabilityFactory
    {
        readonly ParcelCapabilityFactory validation = new(launch);
        public int Sends;
        public ParcelConfiguration Normalize(ParcelConfiguration c) => validation.Normalize(c);
        public void Validate(ParcelConfiguration c) => validation.Validate(c);
        public void ValidateStructure(ParcelConfiguration c) => validation.ValidateStructure(c);
        public CapabilityCatalog Catalog() => validation.Catalog();
        public ParcelPorts Create(ParcelConfiguration c)
        {
            if (failOriginal && c.TimeoutMs == 1500) throw new InvalidOperationException("Synthetic original configuration cannot start");
            var readers = ParcelBindings.Readers(c).ToDictionary(r => r.DeviceId, r => (IControlledObservationSource)new ManualObservationSource(r.DeviceId));
            var wcs = new ReferenceWcs(c.Wcs!, Path.Combine(launch.DataRoot, "synthetic-peer"));
            return new(readers.Values.First(), null, new CountedWcs(wcs, this), null, new Dictionary<string, string> { [wcs.InstanceId] = "synthetic-implementation" },
                Login: c.Login is null ? null : new ReferenceOperatorAuthenticator(c.Login), Maintenance: c.MaintenanceLogin is null ? null : new ReferenceOperatorAuthenticator(c.MaintenanceLogin), Readers: readers);
        }
        public ValueTask DisposeAsync() => validation.DisposeAsync();
    }
    sealed class CountedWcs(IWcsPort inner, SyntheticFactory owner) : IWcsPort
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public Task<StageReply> ResolveSlotAsync(FrozenRequest r, CancellationToken t) { owner.Sends++; return inner.ResolveSlotAsync(r, t); }
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => inner.QueryAsync(r, t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    public static async Task Run(string workspace, string root)
    {
        var config = Path.Combine(root, "config"); Directory.CreateDirectory(config);
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        File.WriteAllText(Path.Combine(config, "business.json"), Wire.Encode(seed));
        var launch = new BusinessLaunch(config, Path.Combine(root, "data"), null, null);
        var directory = Path.Combine(launch.DataRoot, seed.Mode.ToString(), seed.StationId, seed.ApplicationId);
        // Empty DB7 is a real prior format; migration precedes the deliberately
        // broken first resource startup, so independent initial repair remains usable.
        using (var prior = new ParcelRepository(directory, seed.ApplicationId, 7)) { }
        ParcelRuntime Runtime(bool fail = true) => new(launch, new SportmasterFlow(), new SyntheticFactory(launch, fail)) { RuntimeIdentityPreflight = () => { } };
        Console.WriteLine("Isolated synthetic identity dependency; actual Windows UAC/runtime policy NOT_RUN by this fixture.");
        await using (var broken = Runtime()) await Reject<InvalidOperationException>(() => broken.StartAsync(default), "original unconfirmed configuration fails resource construction");
        var file = Path.Combine(directory, "configuration.json");
        Check(ParcelRepository.ReadStorageVersion(Path.Combine(directory, seed.ApplicationId + ".db"), seed.ApplicationId) == 8, "settled DB7 migrates to DB8 before failed initial resources and retains the repair route");
        var original = File.ReadAllBytes(file);
        var authority = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(file));
        Check(authority.Confirmation is { Required: true } && authority.Confirmation.Attempts.Last().Outcome == "Failed", "failed startup is durable and does not confirm Effective");
        var service = new ParcelStartupMaintenance(launch, new SportmasterFlow(), () => new SyntheticFactory(launch), () => { })
        { MaintenanceWorkspace = () => Directory.CreateDirectory(Path.Combine(root, "scratch-" + Wire.Id())).FullName };
        var document = await service.ReadAsync();
        var editor = new StartupConfigurationDraft(document.Candidate!, document.Catalog); editor.TimeoutMs = "unfinished-value";
        var captured = editor.Capture(); captured.Values["synthetic.secret"] = "SYNTHETIC-SECRET-COMM-01";
        var saved = await service.SaveDraftAsync(document.ConfigurationHash, null, captured);
        var reopened = (await service.ReadDraftAsync())!;
        var restoredEditor = new StartupConfigurationDraft(document.Candidate!, document.Catalog); restoredEditor.Restore(reopened.Editor);
        Check(restoredEditor.TimeoutMs == "unfinished-value" && Wire.FileHash(file) == document.ConfigurationHash, "incomplete draft resumes without becoming Pending or changing Effective");
        if (OperatingSystem.IsWindows()) Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "startup-maintenance/editor-draft.bin"))).Contains("SYNTHETIC-SECRET"), "actual Windows DPAPI output excludes plaintext secret marker");
        await Reject<InvalidOperationException>(() => service.SaveDraftAsync(document.ConfigurationHash, null, captured), "concurrent stale draft cannot overwrite newer input");
        var backupFolder = Path.Combine(root, "planned-backups"); Directory.CreateDirectory(backupFolder);
        var candidate = document.Candidate! with { TimeoutMs = 2000, BackupSchedule = new(backupFolder, 15, 1) };
        await service.StageAsync(document.ConfigurationHash, candidate, "ordinary pending cannot bypass original confirmation");
        await using (var stillBroken = Runtime()) await Reject<InvalidOperationException>(() => stillBroken.StartAsync(default), "ordinary Pending still cannot bypass unconfirmed original");
        document = await service.ReadAsync();
        await service.RepairInitialAsync(document.ConfigurationHash, document.Candidate! with { TimeoutMs = 1500 }, "explicit candidate failure retains original");
        await using (var failedRepair = Runtime()) await Reject<InvalidOperationException>(() => failedRepair.StartAsync(default), "failed repair candidate retains the original Effective");
        authority = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(file));
        Check(authority is { Effective.TimeoutMs: 1500, Confirmation.Required: true, Activation.Outcome: "Failed", Pending: not null }, "candidate failure does not confirm or erase pending repair evidence");
        document = await service.ReadAsync();
        await service.RepairInitialAsync(document.ConfigurationHash, candidate, "synthetic interruption before resource construction");
        var interrupted = new ConfigurationRevisions<ParcelConfiguration>(file, seed, new SportmasterFlow().Validate);
        _ = interrupted.BeginActivation(_ => true, _ => { });
        var restartLedger = new ConfigurationRevisions<ParcelConfiguration>(file, seed, new SportmasterFlow().Validate); restartLedger.PrepareEffectiveConfirmation(); restartLedger.RecoverInterruptedActivation();
        Check(restartLedger.Current is { Effective.TimeoutMs: 1500, Status: "ActivationFailed", Activation.Outcome: "Interrupted", Confirmation.Required: true }, "interrupted repair remains explicit-restage-only with original Effective");
        document = await service.ReadAsync();
        await service.RepairInitialAsync(document.ConfigurationHash, candidate, "explicit synthetic original repair");
        await Reject<InvalidOperationException>(() => service.RepairInitialAsync(document.ConfigurationHash, candidate, "stale CAS"), "stale initial repair cannot overwrite a newer intent");
        authority = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(file));
        Check(authority.Confirmation!.Required && authority.Effective.TimeoutMs == 1500 && authority.InitialRepair is not null &&
            authority.InitialRepair.CandidateHash == Wire.Hash(Wire.Encode(authority.Pending)), "repair intent keeps old Effective and confirmation until candidate resources succeed");
        var repairedEnvelope = authority.InitialRepair!.OriginalEnvelopeBase64;
        await using (var repaired = Runtime())
        {
            await repaired.StartAsync(default); var snapshot = await repaired.SnapshotAsync();
            Check(snapshot.Configuration is { Pending: null, Effective.TimeoutMs: 2000, Confirmation.Required: false, Activation.Outcome: "Activated" }, "correct candidate starts despite permanently broken original and confirms only after resource startup");
            Check(snapshot.Configuration.InitialRepair?.OriginalEnvelopeBase64 == repairedEnvelope && snapshot.Configuration.Confirmation!.Attempts.Any(a => a.Outcome == "Failed"), "original repair bytes and failed attempts remain in authoritative envelope");
            var starts = snapshot.RuntimeId;
            var vm = new ParcelViewModel(repaired, "synthetic", "sportmaster"); await vm.RefreshAsync(); await vm.LoadHistoryAsync(); await vm.LoadCapabilitiesAsync();
            Check((await repaired.SnapshotAsync()).RuntimeId == starts, "history/settings use the same runtime and cannot reconstruct readers");
            var now = DateTimeOffset.UtcNow;
            await repaired.CheckScheduledBackupAsync(now.AddMinutes(20));
            await repaired.CheckScheduledBackupAsync(now.AddMinutes(40));
            Check(Directory.GetFiles(backupFolder, "*.db").Length == 0 && (await repaired.SnapshotAsync()).AuxiliaryReasons!.Any(x => x.Contains("Pending")), "scheduled backup stays pending without authenticated pause and never pauses production itself");
            var s = await repaired.SnapshotAsync(); await repaired.CommandAsync(new(Wire.Id(), s.Revision, "Pause"));
            Check((await repaired.AuthorizeMaintenanceAsync("maintenance", "SIMULATION_ONLY")).Status == "Authorized" && (await repaired.SnapshotAsync()).Availability!.Configure, "synthetic maintenance authentication grants the existing backup permission");
            vm.StructuredPolicy!.Precision = "unfinished-precision";
            vm.NewCapabilityKind = "weight"; await vm.AddCapabilityInstanceAsync(); var extraInstance = vm.CapabilityDrafts.Last().InstanceId;
            await vm.SaveIncompleteEditorAsync();
            var resumedVm = new ParcelViewModel(repaired, "synthetic", "sportmaster"); await resumedVm.RefreshAsync(); await resumedVm.LoadCapabilitiesAsync(); await resumedVm.ContinueIncompleteEditorAsync();
            Check(resumedVm.StructuredPolicy!.Precision == "unfinished-precision" && resumedVm.CapabilityDrafts.Any(c => c.InstanceId == extraInstance) && (await repaired.SnapshotAsync()).Configuration.Effective.TimeoutMs == 2000,
                "running settings resume invalid fields and added inventory from the same encrypted CAS store without activating configuration");
            await repaired.CheckScheduledBackupAsync(now.AddMinutes(42));
            Check(Directory.GetFiles(backupFolder, "*.db").Length == 1 && (await repaired.SnapshotAsync()).AuxiliaryReasons!.Any(x => x.Contains("BackupCreated")), "scheduled path actually creates the original complete recovery set engine output");
            await repaired.CheckScheduledBackupAsync(now.AddMinutes(60));
            Check(Directory.GetFiles(backupFolder, "*.db").Length == 1 && (await repaired.SnapshotAsync()).AuxiliaryReasons!.Any(x => x.Contains("Failed")), "backup count budget leaves the prior complete set and reports failure without deletion");
        }
        var sets = await service.ListBackupsAsync(backupFolder, default);
        Check(sets is [{ Status: "Verified", Identity: not null }], "backup list verifies the actual scheduled complete set");
        var target = Path.Combine(root, "restored-from-schedule"); var restore = await service.PlanRestoreAsync(sets[0].Path, target);
        await service.RestoreAsync(sets[0].Path, restore.Destination, restore.BackupSha256);
        Check(File.Exists(Path.Combine(restore.Destination, "RESTORED-ISOLATED.json")) && File.Exists(Path.Combine(directory, "configuration.json")), "scheduled complete backup actually restores to an isolated new root and retains the original root");
        var dbFiles = Directory.GetFiles(directory).Where(p => Path.GetFileName(p).StartsWith("sportmaster.db")).ToDictionary(p => p, Wire.FileHash);
        var plan = await service.PlanChangeAsync("Upgrade", Path.Combine(root, "pre-upgrade.db"));
        Check(plan.Allowed && dbFiles.OrderBy(x => x.Key).SequenceEqual(Directory.GetFiles(directory).Where(p => Path.GetFileName(p).StartsWith("sportmaster.db")).ToDictionary(p => p, Wire.FileHash).OrderBy(x => x.Key)), "upgrade preflight inspects a private complete snapshot and preserves original DB/WAL file set and bytes");
        var template = StartupSiteTemplates.Export(seed.ApplicationId, editor.Capture());
        Check(template.Defaults.Values.Keys.All(k => !k.EndsWith(".target") && !k.Contains(".parameter.") && !k.StartsWith("policy.Backup")), "new-site template excludes stable devices, endpoints, secrets, numeric ranges and original backup path");
        // A foreign recovery marker must close the explicit repair route before any write.
        authority = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(file));
        File.WriteAllText(Path.Combine(directory, "RESTORED-ISOLATED.json"), "synthetic recovery marker");
        var blockedHash = Wire.FileHash(file);
        await Reject<InvalidOperationException>(() => service.RepairInitialAsync(blockedHash, candidate, "cannot bypass recovery"), "isolated recovery cannot be cleared through initial repair");
        Check(Wire.FileHash(file) == blockedHash, "refused repair preserves original envelope bytes"); File.Delete(Path.Combine(directory, "RESTORED-ISOLATED.json"));
        await History(seed, root);
        await Unknown(seed, root);
        var currentDatabase = Path.Combine(directory, seed.ApplicationId + ".db"); var beforeOldReader = Wire.FileHash(currentDatabase);
        await Reject<InvalidDataException>(() => { using var oldReader = new ParcelRepository(directory, seed.ApplicationId, 7); return Task.CompletedTask; }, "DB7 reader refuses the new DB8 semantic floor before writable open");
        Check(Wire.FileHash(currentDatabase) == beforeOldReader, "refused prior reader preserves original database bytes");
        // Handoff uses an explicitly synthetic machine proof here. Protected
        // deployment registration/Windows account readback remain native NOT_RUN.
        service.HandoffMachineIdentity = () => "SYNTHETIC-HANDOFF-MACHINE";
        using (var changedSource = new ParcelRepository(directory, seed.ApplicationId)) changedSource.Commit(null, "SyntheticAfterBackup", "original advanced after snapshot");
        await Reject<InvalidDataException>(() => service.AdoptRestoreOwnedAsync(sets[0].Path, target, restore.BackupSha256, _ => Task.CompletedTask), "historical backup cannot retire source with newer responsibility/evidence");
        var freshBackup = Path.Combine(root, "handoff-fresh.db"); await service.BackupAsync(freshBackup);
        var freshRoot = Path.Combine(root, "handoff-new-root"); var freshPlan = await service.PlanRestoreAsync(freshBackup, freshRoot); await service.RestoreAsync(freshBackup, freshPlan.Destination, freshPlan.BackupSha256);
        bool committedHandoff = false;
        await service.AdoptRestoreOwnedAsync(freshBackup, freshRoot, freshPlan.BackupSha256, proof => { committedHandoff = File.Exists(Path.Combine(directory, "MIGRATION-SOURCE-FENCED.json")); return Task.CompletedTask; });
        Check(committedHandoff && File.Exists(Path.Combine(freshPlan.Destination, "RESTORED-ISOLATED.json")), "matching full backup fences original before handoff commit and keeps restored root isolated");
        await using (var fenced = Runtime(false)) await Reject<InvalidOperationException>(() => fenced.StartAsync(default), "retired original root cannot start a second business owner");
        Check(Convert.FromBase64String(repairedEnvelope).Length > original.Length, "repair preserves the full later envelope including ordinary stage and failed retry history");
        Console.WriteLine("PASS COMM CORE COMPLETE");
    }
    static async Task Unknown(ParcelConfiguration seed, string root)
    {
        var config = Path.Combine(root, "unknown-config"); Directory.CreateDirectory(config);
        var c = seed with { TimeoutMs = 2000, Wcs = seed.Wcs! with { Parameters = new(seed.Wcs.Parameters) { ["reply-mode"] = "lose-after-commit" } } };
        File.WriteAllText(Path.Combine(config, "business.json"), Wire.Encode(c));
        var launch = new BusinessLaunch(config, Path.Combine(root, "unknown-data"), null, null);
        var firstFactory = new SyntheticFactory(launch); ParcelOperation original;
        await using (var app = new ParcelRuntime(launch, new SportmasterFlow(), firstFactory) { RuntimeIdentityPreflight = () => { } })
        {
            await app.StartAsync(default); app.EmitReferenceAt("left", "unknown-entity", "SYNTHETIC-SECRET-BARCODE", "unknown-observation");
            ParcelSnapshot snapshot = await app.SnapshotAsync();
            for (int i = 0; i < 100 && snapshot.Operations.FirstOrDefault()?.Wcs?.State != StageState.Unknown; i++) { await Task.Delay(20); snapshot = await app.SnapshotAsync(); }
            original = snapshot.Operations.Single(); Check(original.Wcs?.State == StageState.Unknown && firstFactory.Sends == 1, "lost-after-commit fixture creates one original Unknown request");
            var support = await app.PreviewSupportAsync(new(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddMinutes(1), original.Id));
            var destination = Path.Combine(root, "support.json"); await app.ExportSupportAsync(support.Id, destination, default);
            var bytes = File.ReadAllText(destination);
            Check(bytes == support.Json && !bytes.Contains("SYNTHETIC-SECRET") && !bytes.Contains("Payload\"") && !bytes.Contains("Parameters\"") && !bytes.Contains("reference://"), "previewed support bytes correlate original request without leaking barcode, payload, parameters or target");
        }
        var service = new ParcelStartupMaintenance(launch, new SportmasterFlow(), () => new SyntheticFactory(launch), () => { })
        { MaintenanceWorkspace = () => Directory.CreateDirectory(Path.Combine(root, "unknown-scratch-" + Wire.Id())).FullName };
        var read = await service.ReadAsync(); var changed = read.Candidate! with { Wcs = read.Candidate!.Wcs! with { Target = "reference://integration/new-target" } };
        await Reject<InvalidOperationException>(() => service.RepairInitialAsync(read.ConfigurationHash, changed, "blocked by original request"), "initial repair refuses existing Unknown responsibility");
        Check(!(await service.PlanChangeAsync("Upgrade", Path.Combine(root, "unknown-upgrade.db"))).Allowed, "upgrade/uninstall preflight cannot bypass original Unknown responsibility");
        read = await service.StageAsync(read.ConfigurationHash, changed, "new target for future intents only");
        // Reuse the actual lost-after-commit original as a DB7 upgrade fixture.
        var oldLaunch = launch with { DataRoot = Path.Combine(root, "db7-original-data") };
        var oldDirectory = Path.Combine(oldLaunch.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId); Directory.CreateDirectory(oldDirectory);
        File.WriteAllText(Path.Combine(oldDirectory, "configuration.json"), Wire.Encode(new ConfigurationRevision<ParcelConfiguration>(0, c, null, "Effective", null)));
        using (var old = new ParcelRepository(oldDirectory, c.ApplicationId, 7)) old.Commit(original, "OriginalUnknown", "original frozen request reused without execution");
        var oldDatabase = Path.Combine(oldDirectory, c.ApplicationId + ".db"); var oldHash = Wire.FileHash(oldDatabase);
        await using (var blockedUpgrade = new ParcelRuntime(oldLaunch, new SportmasterFlow(), new SyntheticFactory(oldLaunch)) { RuntimeIdentityPreflight = () => { } })
            await Reject<InvalidDataException>(() => blockedUpgrade.StartAsync(default), "DB7 Unknown remains on its original reader before the DB8 migration");
        Check(Wire.FileHash(oldDatabase) == oldHash && ParcelRepository.ReadStorageVersion(oldDatabase, c.ApplicationId) == 7, "refused DB7 upgrade preserves original request database and format");
        var reopenedFactory = new SyntheticFactory(launch);
        await using var reopened = new ParcelRuntime(launch, new SportmasterFlow(), reopenedFactory) { RuntimeIdentityPreflight = () => { } };
        await reopened.StartAsync(default); var recovered = (await reopened.SnapshotAsync()).Operations.Single();
        Check(Wire.Encode(recovered.Wcs!.Request) == Wire.Encode(original.Wcs!.Request) && reopenedFactory.Sends == 0 && recovered.Wcs.State == StageState.Unknown,
            "restart with changed candidate retains original request/payload/target/contract and never reexecutes Unknown");
    }
    static async Task History(ParcelConfiguration c, string root)
    {
        var directory = Path.Combine(root, "history"); Directory.CreateDirectory(directory);
        using var repository = new ParcelRepository(directory, c.ApplicationId);
        var at = DateTimeOffset.Parse("2026-10-19T08:00:00+08:00");
        ParcelOperation Op(string code, int n) => new(Wire.Id(), "entity-" + n, code, "observation-" + n, at.AddSeconds(n), Wire.Hash(Wire.Encode(c)), c, [], c.Weight, null, ParcelState.SlotResolved, "0007", null, null, null, PositionId: n % 2 == 0 ? "left" : "right");
        var first = Op("00000000000000000012345678901234567890", 1); repository.Commit(first, "synthetic", "bounded history");
        repository.Commit(Op(first.ParcelCode, 2), "synthetic", "same code different entity");
        var formula = Op("=HYPERLINK(\"https://invalid.example\")", 3); repository.Commit(formula, "synthetic", "formula input");
        var filter = new ParcelHistoryFilter(From: at, ToExclusive: at.AddSeconds(4)); var selection = repository.SelectHistory(filter);
        repository.Commit(Op("AFTER-SNAPSHOT", 4), "synthetic", "outside fixed boundary");
        using var reader = new ParcelHistoryReader(Path.Combine(directory, c.ApplicationId + ".db"));
        var page = reader.Page(selection, long.MaxValue, 2); var next = reader.Page(selection, page.NextBeforeSequence!.Value, 2);
        Check(page.Items.Length == 2 && next.Items.Length == 1 && page.Items.Concat(next.Items).Select(x => x.Operation.Id).Distinct().Count() == 3, "stable filtered cursor preserves separate same-code entities and excludes later admissions");
        Check(reader.Metrics(selection).Operations == 3 && reader.Metrics(selection).PhysicalCompleted == 0, "history metrics cannot count slot resolution as physical completion");
        var destination = Path.Combine(root, "history.csv"); var receipt = await reader.Export(selection, destination, default);
        var csv = await File.ReadAllTextAsync(destination);
        Check(receipt.Rows == 3 && csv.Contains("\"'=HYPERLINK") && csv.Contains(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(first.ParcelCode))) && !csv.Contains("AFTER-SNAPSHOT"), "CSV uses safe explicit text plus exact UTF-8 base64 and the same frozen filter");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); var cancelledFile = Path.Combine(root, "cancelled.csv");
        await Reject<OperationCanceledException>(() => reader.Export(selection, cancelledFile, cancelled.Token), "export observes cancellation");
        Check(!File.Exists(cancelledFile) && File.ReadAllText(cancelledFile + ".result.json").Contains("Cancelled"), "cancelled export publishes no partial CSV and keeps an accurate receipt");
        await Reject<IOException>(() => reader.Export(selection, destination, default), "export cannot overwrite an existing final result");
    }
}
