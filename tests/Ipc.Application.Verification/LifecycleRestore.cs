using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static class LifecycleRestore
{
    public static async Task Run(string workspace, string root)
    {
        Directory.CreateDirectory(root); var checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception("FAIL RESTORE " + reason); checks++; Console.WriteLine("PASS RESTORE " + reason); }
        async Task Reject<T>(Func<Task> action, string reason) where T : Exception
        { try { await action(); } catch (T) { Check(true, reason); return; } throw new Exception("Expected " + typeof(T).Name + ": " + reason); }
        using var h = new RestoreHarness(workspace, root);
        var c = h.Configuration;
        // This factory has one capability monitor (WCS). Manual observation
        // sources publish their own evidence and are not ICapabilityPort probes.
        string[] expectedPorts = [c.Wcs!.InstanceId];
        var backup = Path.Combine(root, "complete.db");
        await using (var app = new ParcelRuntime(h.Launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(h.Launch)) { RuntimeIdentityPreflight = () => { } })
        {
            await app.StartAsync(default);
            await app.ConfigureManagementPasswordAsync("Synthetic-original-password-8374");
            Check(await app.EnterManagementModeAsync("Synthetic-original-password-8374"), "online backup uses actual temporary management authorization");
            _ = await app.CommandAsync(new(Wire.Id(), (await app.SnapshotAsync()).Revision, "Pause"));
            var receipt = await LifecycleOptimization.AfterMonitorDrain(app, () => app.BackupAsync(backup), expectedPorts);
            Check(receipt.Status == "BackupCreated", "real running Application produces the complete fenced backup before normal shutdown");
        }
        var source = Path.Combine(h.Launch.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId);
        await using var service = h.Service();
        var originalHash = ParcelRepository.HandoffDatabaseIdentity(backup);
        var targetRoot = Path.Combine(root, "isolated"); var preview = await service.PlanRestoreAsync(backup, targetRoot);
        var deployed = new DeploymentLocation(c.ApplicationId, new string('a', 40), Path.Combine(root, "program"), h.Launch.ConfigurationDirectory, h.Launch.DataRoot, "synthetic");
        var intent = WindowsDeploymentSetup.NewRestoreIntent(deployed, backup, preview.BackupSha256, targetRoot, preview.Destination);
        WindowsDeploymentSetup.ValidateRestoreIntent(intent);
        service.RestoreIntentReader = () => [intent];
        var leaseRoot = Path.Combine(root, "leases"); Directory.CreateDirectory(leaseRoot);
        service.RestoreIntentWriter = (_, _, _, _) => new(intent, new ResourceLease(leaseRoot, ["restore-writer"]), new StoppedApplicationOwnership(leaseRoot, c.ApplicationId, h.Launch.DataRoot, default));
        var evidence = new RestoreMachineEvidence(() => Wire.Hash("test-machine"), _ => deployed, _ => throw new Exception("No purge expected"), (_, _, _, _) => throw new Exception("No purge expected")) { RequireRegistration = false };
        service.RestoreEvidence = evidence;
        Check((await service.ReadRestoreOperationsAsync()).Single().State == "Planned", "protected original intent reopens before any target file exists");
        var corruptOnce = true;
        RecoveryFiles.CopyClosedForVerification = (_, to) => { if (corruptOnce) { corruptOnce = false; File.WriteAllText(to, "preserved interrupted bytes"); } };
        try { await Reject<RecoveryFileFailure>(() => service.RestoreAsync(backup, preview.Destination, preview.BackupSha256), "real copy rejects damaged target without publishing complete marker"); }
        finally { RecoveryFiles.CopyClosedForVerification = null; }
        Check((await service.ReadRestoreOperationsAsync()).Single().State == "CopyIncomplete", "fresh service can discover original incomplete copy");
        var movedBackup = Path.Combine(root, "moved-source", Path.GetFileName(backup)); Directory.CreateDirectory(Path.GetDirectoryName(movedBackup)!);
        File.Move(backup, movedBackup); Directory.Move(backup + ".recovery", movedBackup + ".recovery");
        var movedPlan = await service.PlanRestoreAsync(movedBackup, targetRoot);
        Check(movedPlan.OperationId == intent.OperationId && movedPlan.State == "CopyIncomplete", "reselecting the same complete identity at a new path retains original copy ownership");
        await service.RestoreAsync(movedBackup, preview.Destination, preview.BackupSha256);
        Check((await service.ReadRestoreOperationsAsync()).Single().State == "Isolated", "completed original copy remains inspectable when its historical backup path is offline");
        File.Move(movedBackup, backup); Directory.Move(movedBackup + ".recovery", backup + ".recovery");
        var retained = Directory.EnumerateDirectories(Path.GetDirectoryName(preview.Destination)!, "sportmaster.restore-interrupted-*").Single();
        Check(File.ReadAllText(Path.Combine(retained, "sportmaster.db")) == "preserved interrupted bytes" && Wire.FileHash(Path.Combine(preview.Destination, "sportmaster.db")) == Wire.FileHash(backup), "original damaged bytes retained while same operation completes an exact verified copy");
        Check((await service.ReadRestoreOperationsAsync()).Single() is { State: "Isolated", OperationId: var id } && id == intent.OperationId, "complete isolated result reconstructs without minting another operation");
        using (var migration = h.Own(deployed, targetRoot, leaseRoot))
            await Reject<InstallationOutcomeUnknownException>(() => service.AdoptRestoreOwnedAsync(backup, targetRoot, preview.BackupSha256, _ => throw new IOException("lost commit reply"), migration), "handoff failure retains the original fence and identity for inspection");
        var proofPath = Path.Combine(preview.Destination, "restore-handoff.json"); var proof = Wire.Decode<StartupRestoreHandoff>(File.ReadAllText(proofPath));
        Check(proof.Id == intent.OperationId && (await service.ReadRestoreOperationsAsync()).Single().State == "HandoffPrepared", "unconfirmed handoff is rediscovered from original intent, target and source fence");
        var originalProof = File.ReadAllBytes(proofPath);
        File.Delete(Path.Combine(source, "MIGRATION-SOURCE-FENCED.json")); // process loss between the two durable handoff writes
        var calls = 0;
        using (var migration = h.Own(deployed, targetRoot, leaseRoot))
            await service.AdoptRestoreOwnedAsync(backup, targetRoot, preview.BackupSha256, p => { calls++; Check(p.Id == intent.OperationId, "missing original fence resumes the same handoff"); return Task.CompletedTask; }, migration);
        Check(calls == 1 && File.ReadAllBytes(proofPath).SequenceEqual(originalProof), "continuation repairs missing fence without replacing original proof");
        deployed = deployed with { DataRoot = targetRoot, RestoreHandoffHash = Wire.Hash(Wire.Encode(proof)) };
        service.RestoreEvidence = evidence with { RequireRegistration = true };
        using (var migration = h.Own(deployed with { DataRoot = h.Launch.DataRoot }, targetRoot, leaseRoot))
            await service.AdoptRestoreOwnedAsync(backup, targetRoot, preview.BackupSha256, _ => throw new Exception("must not commit twice"), migration);
        Check((await service.ReadRestoreOperationsAsync()).Single().State == "AdoptedIsolated" && calls == 1, "positive committed original returns its result without another handoff");
        Check(ParcelRepository.HandoffDatabaseIdentity(backup) == originalHash, "all copy and handoff attempts preserve original backup database facts");
        var restoredLaunch = h.Launch with { DataRoot = targetRoot };
        var restoredFactory = new CommercialProductization.SyntheticFactory(restoredLaunch);
        await using (var restored = new ParcelRuntime(restoredLaunch, new SportmasterFlow(), restoredFactory)
        { RuntimeIdentityPreflight = () => { }, RestoreEvidence = evidence with { RequireRegistration = true } })
        {
            await restored.StartAsync(default);
            Check((await restored.SnapshotAsync()).Paused && restoredFactory.Sends == 0, "real restored runtime opens isolated and paused without resending any request");
            await restored.ConfigureManagementPasswordAsync("Synthetic-restore-password-8374");
            Check(await restored.EnterManagementModeAsync("Synthetic-restore-password-8374"), "explicit management authorization remains required for enablement verification");
            await LifecycleOptimization.AfterMonitorDrain(restored, async () =>
            {
                var state = await restored.SnapshotAsync();
                await Reject<InvalidDataException>(() => restored.CommandAsync(new(Wire.Id(), state.Revision, "ConfirmRestore", proof.Id, "{\"Reason\":\"no actual floor\"}")), "missing external-number evidence is rejected by the real runtime, never defaulted to zero");
                var afterMissing = await restored.SnapshotAsync();
                await Reject<InvalidOperationException>(() => restored.CommandAsync(new(Wire.Id(), afterMissing.Revision, "ConfirmRestore", proof.Id, Wire.Encode(new RestoreVerificationInput("exhausted protocol", long.MaxValue)))),
                    "exhausted Int64 request space cannot be confirmed as usable after restore");
                var command = new BusinessCommand(Wire.Id(), (await restored.SnapshotAsync()).Revision, "ConfirmRestore", proof.Id, Wire.Encode(new RestoreVerificationInput("controlled external verification", 900000000000000000)));
                var confirmed = await restored.CommandAsync(command);
                var repeated = await restored.CommandAsync(command);
                Check(confirmed.Status == "Persisted" && confirmed == repeated && (await restored.SnapshotAsync()).Paused && restoredFactory.Sends == 0, "one confirmation command is durable and replay-safe, retains pause, and never calls RCS");
                var latest = await restored.SnapshotAsync();
                await Reject<InvalidOperationException>(() => restored.CommandAsync(command with { Id = Wire.Id(), ExpectedRevision = latest.Revision,
                    Value = Wire.Encode(new RestoreVerificationInput("cannot lower", 1)) }), "actual runtime refuses a lower externally claimed number floor after confirmation");
                return true;
            }, expectedPorts);
        }

        var machine = Path.Combine(root, "controlled-machine"); Directory.CreateDirectory(Path.Combine(machine, "deployments"));
        var retired = intent.Source with { LifecycleState = "RemovedDataRetained" };
        var purgePlan = WindowsDeploymentSetup.PlanDataPurgeCore(retired, Wire.Hash("old registration"), Wire.Id(), machine);
        Check(purgePlan.Allowed, "real purge planner inventories exact original station evidence for the controlled OS boundary");
        var purgeFile = Path.Combine(WindowsDeploymentSetup.MachineRoot, "deployments", "sportmaster.purge-" + purgePlan.OperationId + ".json");
        retired = retired with { LifecycleState = "Purged", MaintenanceOperation = purgeFile };
        var purgeRecord = new DeploymentPurgeRecord(1, DateTimeOffset.UtcNow, purgePlan, "Purged", [], "synthetic-admin");
        var purgeJson = Wire.Encode(purgeRecord);
        var purge = WindowsDeploymentSetup.ProjectPurgedRestoreSource(retired, purgeFile, purgeJson, Wire.Hash(purgeJson), source,
            Wire.FileHash(Path.Combine(source, "configuration.json")), Wire.FileHash(Path.Combine(source, "machine-origin.bin")));
        await Reject<InvalidDataException>(() =>
        {
            _ = WindowsDeploymentSetup.ProjectPurgedRestoreSource(retired, purgeFile, Wire.Encode(purgeRecord with { Status = "PartiallyPurged" }), Wire.Hash(purgeJson), source, purge.ConfigurationSha256, purge.MachineOriginSha256);
            return Task.CompletedTask;
        }, "pending deletion cannot masquerade as source retirement");
        await Reject<InvalidDataException>(() =>
        {
            _ = WindowsDeploymentSetup.ProjectPurgedRestoreSource(retired, purgeFile, purgeJson, Wire.Hash(purgeJson), source, Wire.Hash("different station"), purge.MachineOriginSha256);
            return Task.CompletedTask;
        }, "different backup identity cannot borrow a valid purge receipt");
        // Preserve rather than delete even these test originals. No machine
        // registry, queue, service or real uninstall action is executed.
        Directory.Move(h.Launch.DataRoot, Path.Combine(root, "retired-original-custody"));
        var reinstalled = h.Launch with { DataRoot = Path.Combine(root, "reinstalled-empty-root") };
        Directory.CreateDirectory(reinstalled.DataRoot);
        var newDeployment = intent.Source with { DataRoot = reinstalled.DataRoot, DeploymentPlanIdentity = Wire.Hash("new install"), RestoreHandoffHash = null };
        var purgedTarget = Path.Combine(root, "after-purge-restored");
        await using var afterPurge = new ParcelStartupMaintenance(reinstalled, new SportmasterFlow(), () => new CommercialProductization.SyntheticFactory(reinstalled), () => { });
        afterPurge.MaintenanceWorkspace = () => Directory.CreateDirectory(Path.Combine(root, "purge-snapshot-" + Wire.Id())).FullName;
        var purgePreview = await afterPurge.PlanRestoreAsync(backup, purgedTarget);
        var purgeIntent = WindowsDeploymentSetup.NewRestoreIntent(newDeployment, backup, purgePreview.BackupSha256, purgedTarget, purgePreview.Destination);
        afterPurge.RestoreIntentReader = () => [purgeIntent];
        afterPurge.RestoreIntentWriter = (_, _, _, _) => new(purgeIntent, new ResourceLease(leaseRoot, ["purged-copy"]), new StoppedApplicationOwnership(leaseRoot, c.ApplicationId, reinstalled.DataRoot, default));
        var purgeEvidence = new RestoreMachineEvidence(() => Wire.Hash("test-machine"), _ => newDeployment,
            p => { Check(p == purge, "same protected purge proof remains bound to the restored original"); }, (_, _, _, _) => purge) { RequireRegistration = false };
        afterPurge.RestoreEvidence = purgeEvidence;
        await afterPurge.RestoreAsync(backup, purgePreview.Destination, purgePreview.BackupSha256);
        using (var migration = h.Own(newDeployment, purgedTarget, leaseRoot))
            await afterPurge.AdoptRestoreOwnedAsync(backup, purgedTarget, purgePreview.BackupSha256, _ => Task.CompletedTask, migration);
        var purgedProof = Wire.Decode<StartupRestoreHandoff>(File.ReadAllText(Path.Combine(purgePreview.Destination, "restore-handoff.json")));
        Check(purgedProof.PurgedSource == purge && !Directory.Exists(h.Launch.DataRoot) && !Directory.Exists(Path.Combine(reinstalled.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId)),
            "clean-uninstall restore uses the original purge proof without recreating old roots or an empty business database");

        // Real database lifecycle: allow only the exact normal close, never a
        // blanket omission of audit, requests or new business facts.
        var life = Path.Combine(root, "shift-source"); Directory.CreateDirectory(life);
        var openBackup = Path.Combine(root, "shift-open.db"); var stopped = Path.Combine(root, "shift-closed.db");
        using (var repo = new ParcelRepository(life, c.ApplicationId))
        {
            repo.OpenStationShift(Guid.NewGuid(), TimeProvider.System, DateTimeOffset.UtcNow, TimeProvider.System.GetTimestamp());
            repo.Backup(openBackup); repo.CloseStationShift(true); repo.Backup(stopped);
        }
        ParcelRepository.RequireHandoffSourceEquivalent(stopped, openBackup); Check(true, "online snapshot followed by actual normal station close is a legal restore source");
        using (var repo = new ParcelRepository(life, c.ApplicationId)) { repo.Commit(null, "ExternalFactAfterBackup", "must not disappear"); repo.Backup(stopped + ".new"); }
        await Reject<InvalidDataException>(() => { ParcelRepository.RequireHandoffSourceEquivalent(stopped + ".new", openBackup); return Task.CompletedTask; }, "new facts after online backup remain a hard source preservation guard");

        using (var repo = new ParcelRepository(Path.Combine(root, "number-floor"), c.ApplicationId))
        {
            var confirm = new BusinessCommand(Wire.Id(), repo.Revision, "ConfirmRestore", proof.Id, "synthetic confirmed external floor");
            repo.Commit(null, "RestoreHandoffConfirmed", "synthetic external verification", paused: true, command: confirm, restoreFloor: new(proof.Id, 900000000000000000));
            Check(repo.IntegrationRequestIdFloor() == 900000000000000000 && repo.NextAutomaticIntegrationRequestId() > 900000000000000000 && repo.Paused, "confirmed external floor is durable and next request cannot reuse deleted-source numbers; pause retained");
            repo.Commit(null, "RestoreHandoffConfirmed", "lower repeat", command: confirm with { Id = Wire.Id(), ExpectedRevision = repo.Revision }, restoreFloor: new(proof.Id, 1));
            Check(repo.IntegrationRequestIdFloor() == 900000000000000000, "repeated lower proof never lowers permanent numbering");
        }
        CommitInterruptions(root, Check);
        Console.WriteLine("RESTORE checks=" + checks + "; real SQLite/copy/handoff and controlled machine boundary; real registry/SCM/UAC/reboot NOT_RUN");
    }
    static void CommitInterruptions(string root, Action<bool, string> check)
    {
        foreach (var cut in new[] { "seed", "descriptor", "registration", "projection", "receipt" })
        {
            var id = Wire.Id(); var before = new DeploymentLocation("sportmaster", new string('a', 40), Path.Combine(root, "release"), Path.Combine(root, "config"), Path.Combine(root, "old"), "synthetic");
            var file = Path.Combine(WindowsDeploymentSetup.MachineRoot, "deployments", "sportmaster.migration-" + id + ".json");
            var next = before with { DataRoot = Path.Combine(root, "restored"), MaintenanceOperation = file, RestoreHandoffHash = Wire.Hash("proof") };
            var record = new DeploymentRestoreCommit("Prepared", id, before, next, next.RestoreHandoffHash!, null, Convert.ToBase64String("old seed"u8), "restored seed");
            WindowsDeploymentSetup.ValidateRestoreCommit(record, "sportmaster", file);
            if (cut == "seed")
            {
                check(WindowsDeploymentSetup.RestoreBlocksAnotherOperation(record, before, Wire.Id()), "an unresolved original handoff blocks a new restore operation before source commit");
                check(!WindowsDeploymentSetup.RestoreBlocksAnotherOperation(record, before, id), "the same original handoff can resume its prepared commit");
                check(!WindowsDeploymentSetup.RestoreBlocksAnotherOperation(record, before with { LifecycleState = "Purged" }, Wire.Id()), "explicit later uninstall supersedes an old handoff without reviving it");
            }
            DeploymentLocation? current = before; var seed = "old seed"u8.ToArray(); var interrupt = true;
            void Boundary(string phase) { if (interrupt && cut == phase) throw new IOException("synthetic process boundary " + phase); }
            void Execute() => WindowsDeploymentSetup.ExecuteRestoreCommit(record, () => current, () => seed,
                bytes => { seed = bytes; Boundary("seed"); }, _ => Boundary("descriptor"), location => { current = location; Boundary("registration"); },
                () => Boundary("projection"), completed => { Boundary("receipt"); record = completed; });
            try { Execute(); throw new Exception("missing interruption"); } catch (IOException) { }
            check(record.Status == "Prepared" && WindowsDeploymentSetup.RestoreCommitState(record, current) is "HandoffPrepared" or "ProjectionPending", "interrupted original is inspectable at " + cut);
            interrupt = false; Execute();
            check(current == next && System.Text.Encoding.UTF8.GetString(seed) == "restored seed" && record.Status == "CompletedIsolated", "same original completes seed and sole registration at " + cut);
            current = next with { SourceCommit = new string('b', 40) };
            try { Execute(); throw new Exception("superseded accepted"); } catch (InvalidOperationException) { check(true, "later deployment cannot be overwritten by old restore at " + cut); }
        }
    }
    sealed class RestoreHarness : IDisposable
    {
        public ParcelConfiguration Configuration { get; }
        public BusinessLaunch Launch { get; }
        readonly string root;
        public RestoreHarness(string workspace, string root)
        {
            this.root = root;
            Configuration = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
            var config = Path.Combine(root, "config"); Directory.CreateDirectory(config); ExtensionStore.AtomicWrite(Path.Combine(config, "business.json"), Configuration);
            Launch = new(config, Path.Combine(root, "data"), null, null);
        }
        public ParcelStartupMaintenance Service() => new(Launch, new SportmasterFlow(), () => new CommercialProductization.SyntheticFactory(Launch), () => { }, () => "controlled-test")
        { MaintenanceWorkspace = () => Directory.CreateDirectory(Path.Combine(root, "snapshot-" + Wire.Id())).FullName };
        public DesktopRecoveryMigration Own(DeploymentLocation source, string targetRoot, string leaseRoot)
        {
            var migration = new DesktopRecoveryMigration(source, targetRoot, null, () => { });
            migration.HoldStoppedSource(new StoppedApplicationOwnership(leaseRoot, source.ApplicationId, source.DataRoot, default)); return migration;
        }
        public void Dispose() { }
    }
}
