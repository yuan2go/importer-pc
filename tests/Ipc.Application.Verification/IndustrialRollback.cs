using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static class IndustrialRollback
{
    public static async Task Run(string workspace, string root)
    {
        var checks = 0;
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidDataException("FAIL IE01-ROLLBACK " + message); checks++; Console.WriteLine("PASS IE01-ROLLBACK " + message); }
        void Reject<T>(Action action, string message) where T : Exception
        { try { action(); } catch (T) { Check(true, message); return; } throw new Exception("Expected " + typeof(T).Name + ": " + message); }
        var files = new Dictionary<string, string> { ["Ipc.Own.dll"] = new string('a', 64), ["native/vendor.dll"] = new string('b', 64) };
        var release = new ApplicationRelease(2, "sportmaster", new string('a', 40), [], [], files, "1.2.0", CompatibilitySha256: new string('c', 64));
        var compatibility = new DeploymentRuntimeCompatibility(1, 12, 3, 2, 3, 1, 4, "1.0.0", release.CompatibilitySha256!, DeploymentRuntimeCompatibility.DependencyHash(files));
        compatibility.Validate(release);
        var currentSmBuild = release with { BusinessVersion = "1.0.0" };
        Check(WindowsDeploymentSetup.RollbackVersionAllowed("sportmaster", currentSmBuild, currentSmBuild with { SourceCommit = new string('b', 40) }), "two fixed 1.0.0 SM builds may switch back to the registered previous source");
        Check(!WindowsDeploymentSetup.RollbackVersionAllowed("sportmaster", currentSmBuild, currentSmBuild), "the same source is not a second program rollback");
        Check(DeploymentRuntimeCompatibility.DependencyHash(files) == Wire.Hash("native/vendor.dll\t" + new string('B', 64)), "dependency identity excludes own source-varying binaries but binds actual SDK bytes");
        Check(DeploymentRuntimeCompatibility.Incompatibility(compatibility, compatibility) is null, "identical current readers/protocols allow program-only rollback");
        foreach (var incompatible in new[] { compatibility with { DataFormat = 11 }, compatibility with { ConfigurationSchema = 2 },
            compatibility with { CredentialSchema = 1 }, compatibility with { ManagementProtocol = 2 }, compatibility with { RecoverySecurityEpoch = 0 },
            compatibility with { LifecycleProtocol = 3 }, compatibility with { ApplicationContractVersion = "2.0.0" },
            compatibility with { ProviderPolicySha256 = new string('d', 64) }, compatibility with { DependencyInventorySha256 = new string('e', 64) } })
            Check(DeploymentRuntimeCompatibility.Incompatibility(compatibility, incompatible) is not null, "different reader/security/provider/SDK dimension rejected: " + Wire.Hash(Wire.Encode(incompatible))[..8]);
        Reject<InvalidDataException>(() => (compatibility with { DependencyInventorySha256 = new string('e', 64) }).Validate(release), "compatibility metadata cannot contradict inventory");
        var oversized = Path.Combine(root, "oversized-compatibility-release"); Directory.CreateDirectory(oversized);
        using (var file = File.Create(Path.Combine(oversized, "application-release.json"))) file.SetLength(4 * 1024 * 1024 + 1);
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.RuntimeCompatibility(oversized), "oversized cached release refused before decoding or hashing further metadata");

        foreach (var cut in new[] { "before-commit", "projection", "receipt", "success" })
        {
            var area = Path.Combine(root, cut); Directory.CreateDirectory(area);
            var database = Path.Combine(area, "business-facts.db"); File.WriteAllText(database, "synthetic accepted and unknown facts - never roll back");
            var dataHash = Wire.FileHash(database);
            var registration = Path.Combine(area, "registration.json"); var journal = Path.Combine(area, "rollback.json");
            var before = new DeploymentLocation("sportmaster", new string('a', 40), Path.Combine(area, "current"), Path.Combine(area, "config"), Path.Combine(area, "data"), "synthetic");
            ExtensionStore.AtomicWrite(registration, before);
            var plan = new DeploymentRollbackPlan(new string('f', 64), Wire.Id(), Wire.FileHash(registration), before,
                before with { ReleaseRoot = Path.Combine(area, "previous"), SourceCommit = new string('b', 40) }, true, "synthetic compatibility");
            var target = plan.Target! with { DeploymentPlanIdentity = plan.Identity, LifecycleOperation = journal, PreviousDeployment = before };
            var record = new DeploymentRollbackRecord(1, "Prepared", DateTimeOffset.UtcNow, plan, target);
            (DeploymentLocation, string) Observe() => (Wire.Decode<DeploymentLocation>(File.ReadAllText(registration)), Wire.FileHash(registration));
            void Apply() => WindowsDeploymentSetup.ApplyRollback(record, Observe, next =>
            {
                if (cut == "before-commit") throw new IOException("synthetic precommit interruption");
                ExtensionStore.AtomicWrite(registration, next);
            }, state =>
            {
                if (state.Status == "Completed" && cut == "receipt") throw new IOException("synthetic final receipt loss");
                ExtensionStore.AtomicWrite(journal, state);
            }, _ => { if (cut == "projection") throw new IOException("synthetic projection interruption"); });
            if (cut == "success") Apply();
            else if (cut == "before-commit") Reject<IOException>(Apply, "precommit failure preserved: " + cut);
            else Reject<InstallationOutcomeUnknownException>(Apply, "committed result requires original inspection: " + cut);
            var observed = Wire.Decode<DeploymentRollbackRecord>(File.ReadAllText(journal));
            Check(Wire.FileHash(database) == dataHash, "program switch never restores/deletes business facts: " + cut);
            if (cut == "before-commit")
                Check(observed.Status == "NotCommitted" && Observe().Item1.ReleaseRoot == before.ReleaseRoot, "precommit retains old sole authority");
            else
            {
                Check(Observe().Item1.ReleaseRoot == target.ReleaseRoot && Observe().Item1.DeploymentPlanIdentity == plan.Identity, "exactly one target registration after commit: " + cut);
                if (cut == "success") { WindowsDeploymentSetup.RequireRollbackReady(target, observed); Check(observed.Status == "Completed", "completed original permits ordinary startup"); }
                else Reject<InvalidOperationException>(() => WindowsDeploymentSetup.RequireRollbackReady(target, observed), "pending system projections block ordinary startup: " + cut);
            }
            Reject<InvalidOperationException>(() => WindowsDeploymentSetup.ApplyRollback(record with { Plan = plan with { ExpectedRegistrationHash = new string('0', 64) } },
                Observe, _ => throw new Exception("must not commit"), _ => throw new Exception("must not write"), _ => throw new Exception("must not project")),
                "changed registration refused before first side effect: " + cut);
            Reject<InvalidOperationException>(() => DeploymentMedia.AuthoritativeLocation(before, target), "stale release descriptor cannot authorize its own startup: " + cut);
        }

        var unreadableLocation = new DeploymentLocation("sportmaster", new string('a', 40), "synthetic", "synthetic", "synthetic", "synthetic");
        var unreadablePlan = new DeploymentRollbackPlan("synthetic", Wire.Id(), "before", unreadableLocation, unreadableLocation, true, "synthetic");
        var observations = 0;
        Reject<InstallationOutcomeUnknownException>(() => WindowsDeploymentSetup.ApplyRollback(
            new(1, "Prepared", DateTimeOffset.UtcNow, unreadablePlan, unreadableLocation),
            () => ++observations == 1 ? (unreadableLocation, "before") : throw new IOException("synthetic unreadable registration"),
            _ => throw new IOException("synthetic interrupted commit"), _ => { }, _ => { }),
            "unreadable postcommit registration stays Unknown and preserves both failures");
        var removedTarget = unreadableLocation with { LifecycleOperation = "synthetic-original-rollback", SourceCommit = new string('a', 40) };
        string Removed(string status, DeploymentLocation location) => Wire.Encode(new { Status = status, Location = location, Files = Array.Empty<string>() });
        Check(WindowsDeploymentSetup.RemovalSupersedesRollback("sportmaster", "synthetic-original-rollback", Removed("RemovedDataRetained", removedTarget)), "completed program removal ends old projection obligation without claiming business settlement");
        Check(!WindowsDeploymentSetup.RemovalSupersedesRollback("sportmaster", "synthetic-original-rollback", Removed("Prepared", removedTarget)), "prepared removal does not prove rollback is superseded");
        Check(!WindowsDeploymentSetup.RemovalSupersedesRollback("other", "synthetic-original-rollback", Removed("RemovedDataRetained", removedTarget)), "another application removal cannot resolve original rollback");
        Check(!WindowsDeploymentSetup.RemovalSupersedesRollback("sportmaster", "other-operation", Removed("RemovedDataRetained", removedTarget)), "another operation removal cannot resolve original rollback");
        Check(!WindowsDeploymentSetup.RemovalSupersedesRollback("sportmaster", "synthetic-original-rollback", Removed("RemovedDataRetained", removedTarget with { SourceCommit = "" })), "unidentified removal cannot resolve original rollback");

        foreach (var scenario in new[] { "settled", "resident-unknown", "historical-unknown" })
        {
            var unknown = scenario != "settled";
            var retainedUnknown = scenario == "historical-unknown";
            await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "sqlite-" + scenario), deferred: true);
            await h.StartAsync();
            if (unknown) h.Peer.Mode = "malformed";
            var admitted = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTHETIC-ROLLBACK-" + unknown), default);
            await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(admitted.OperationId!))?.Wcs?.State == (unknown ? StageState.Unknown : StageState.Accepted));
            if (!unknown || retainedUnknown) await SmRoundFixture.ConfirmRemovedAsync(h.App, "left");
            if (retainedUnknown) await CameraRoundHarness.Until(async () => (await h.App.HistoryAsync()).Items.Any(i => i.Operation.Id == admitted.OperationId && i.Archived));
            await h.App.DisposeAsync();
            var directory = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, "sportmaster");
            var database = Path.Combine(directory, "sportmaster.db"); var originalHash = Wire.FileHash(database);
            var core = new ParcelStartupMaintenance(h.Launch, new SportmasterFlow(), () => new ManualSupplyHarness.ManualFactory(h.Launch), () => { }, () => "synthetic-admin")
            {
                MaintenanceWorkspace = () => { var path = Path.Combine(root, "snapshot-" + Wire.Id()); Directory.CreateDirectory(path); return path; }
            };
            var backup = Path.Combine(root, "before-rollback-" + scenario + ".db");
            var preview = await core.PlanChangeAsync("Rollback", backup);
            Check(preview.Allowed == (!unknown || retainedUnknown), "actual SQLite distinguishes current execution from historical Unknown custody: " + scenario);
            if (!unknown || retainedUnknown)
            {
                var called = false;
                await core.ExecuteChangeAsync(preview.Identity, "Rollback", backup, () =>
                {
                    _ = ParcelRecovery.VerifyBackup(backup, () => { });
                    Reject<IOException>(() => { using var competitor = new ResourceLease(directory, ["application-owner"]); }, "same application owner spans backup and switch");
                    called = true; return Task.CompletedTask;
                });
                Check(called, "complete verified recovery set published before program switch callback");
            }
            else
            {
                try { await core.ExecuteChangeAsync(preview.Identity, "Rollback", backup, () => throw new Exception("must not switch unknown")); throw new Exception("unknown accepted"); }
                catch (InvalidOperationException) { Check(true, "Unknown cannot enter backup/switch commit"); }
                Check(!File.Exists(backup), "rejected Unknown creates no success backup");
            }
            Check(Wire.FileHash(database) == originalHash, "offline maintenance does not mutate original SQLite: unknown=" + unknown);
        }
        Console.WriteLine("IE01-ROLLBACK checks=" + checks + "; actual files/SQLite, synthetic registry callbacks; UAC/SCM/VM NOT_RUN");
    }
}
