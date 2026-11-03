using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static class IndustrialVersionPruning
{
    public static async Task Run(string workspace, string root)
    {
        var checks = 0;
        void Check(bool value, string detail) { if (!value) throw new Exception("FAIL IE01-PRUNE " + detail); checks++; Console.WriteLine("PASS IE01-PRUNE " + detail); }
        void Reject<T>(Action action, string detail) where T : Exception
        { try { action(); } catch (T) { Check(true, detail); return; } throw new Exception("Expected " + typeof(T).Name + ": " + detail); }
        DeploymentPruneRecord Record(string name, out string area)
        {
            area = Path.Combine(root, name); Directory.CreateDirectory(area);
            var owned = Path.Combine(area, "app", "Ipc.Synthetic.dll"); Directory.CreateDirectory(Path.GetDirectoryName(owned)!);
            File.WriteAllText(owned, "synthetic old version only");
            var plan = new DeploymentPrunePlan("synthetic-reviewed", Wire.Id(), "sportmaster", "registration", true, "synthetic plan",
                [new(area, new() { ["app/Ipc.Synthetic.dll"] = Wire.FileHash(owned) })], [new(Path.Combine(root, "current"), "current"), new(Path.Combine(root, "previous"), "fallback")], true);
            return new(1, DateTimeOffset.UtcNow, plan, "Prepared", [], "synthetic-Windows-admin");
        }
        var normal = Record("complete", out var ordinary);
        var current = Path.Combine(root, "current"); var previous = Path.Combine(root, "previous");
        Directory.CreateDirectory(current); Directory.CreateDirectory(previous);
        File.WriteAllText(Path.Combine(current, "keep.txt"), "current"); File.WriteAllText(Path.Combine(previous, "keep.txt"), "previous");
        DeploymentPruneRecord? saved = null;
        var completed = WindowsDeploymentSetup.ApplyVersionPruning(normal, "synthetic-receipt", () => "registration", state => saved = state);
        Check(completed.Status == "Completed" && saved?.Status == "Completed" && !Directory.Exists(ordinary), "actual inventoried file and empty parents removed");
        Check(File.Exists(Path.Combine(current, "keep.txt")) && File.Exists(Path.Combine(previous, "keep.txt")), "current and previous program directories untouched");
        var again = WindowsDeploymentSetup.ApplyVersionPruning(normal, "synthetic-receipt", () => "registration", state => saved = state);
        Check(again.Status == "Completed", "same fixed deletion list is idempotent after missing-root completion");

        var changed = Record("changed", out var changedArea);
        File.WriteAllText(Path.Combine(changedArea, "app", "Ipc.Synthetic.dll"), "changed bytes");
        var writes = 0;
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ApplyVersionPruning(changed, "receipt", () => "registration", _ => writes++), "changed bytes reject entire list before receipt/deletion");
        Check(writes == 0 && File.ReadAllText(Path.Combine(changedArea, "app", "Ipc.Synthetic.dll")) == "changed bytes", "changed original retained");
        var stale = Record("stale", out var staleArea);
        Reject<InvalidOperationException>(() => WindowsDeploymentSetup.ApplyVersionPruning(stale, "receipt", () => "other-registration", _ => writes++), "registration CAS protects scope before first deletion");
        Check(writes == 0 && File.Exists(Path.Combine(staleArea, "app", "Ipc.Synthetic.dll")), "stale plan has no write side effects");
        Reject<InvalidOperationException>(() => WindowsDeploymentSetup.ApplyVersionPruning(stale with { Plan = stale.Plan with { OrdinaryStartupConfirmed = false } }, "receipt", () => "registration", _ => writes++), "ordinary startup confirmation cannot be inferred by maintenance");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ApplyVersionPruning(stale with { Plan = stale.Plan with { Retain = [new(staleArea, "must retain")] } }, "receipt", () => "registration", _ => writes++), "retained fallback cannot enter deletion scope even with matching registration");
        var cancelled = Record("cancel-before-first-delete", out var cancelledArea);
        using (var cancellation = new MaintenanceExecution())
        {
            Reject<OperationCanceledException>(() => WindowsDeploymentSetup.ApplyVersionPruning(cancelled, "receipt", () => "registration", state =>
            {
                saved = state; if (state.Status == "Prepared") cancellation.TryCancel();
            }, cancellation), "actual cancellation after prepared intent is accepted before first deletion");
            Check(saved?.Status == "NotStarted" && File.Exists(Path.Combine(cancelledArea, "app", "Ipc.Synthetic.dll")), "known pre-delete cancellation finalizes original without deleting files or pinning future maintenance");
        }

        var extra = Record("extra", out var extraArea);
        File.WriteAllText(Path.Combine(extraArea, "operator-note.txt"), "unlisted original retained");
        var extras = WindowsDeploymentSetup.ApplyVersionPruning(extra, "receipt", () => "registration", _ => { });
        Check(extras.Status == "CompletedWithRetainedFiles" && File.Exists(Path.Combine(extraArea, "operator-note.txt")), "unlisted extras remain with explicit terminal receipt");
        Check(!File.Exists(Path.Combine(extraArea, "app", "Ipc.Synthetic.dll")), "extras never cause recursive deletion");
        var locked = Record("locked", out var lockedArea);
        var lockedFile = Path.Combine(lockedArea, "app", "Ipc.Synthetic.dll");
        using (var holding = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var partial = WindowsDeploymentSetup.ApplyVersionPruning(locked, "receipt", () => "registration", state => saved = state);
            Check(partial.Status == "Partial" && File.Exists(lockedFile), "actual locked inventoried file stays Partial without reboot scheduling");
        }
        var resumed = WindowsDeploymentSetup.ApplyVersionPruning(locked, "receipt", () => "registration", state => saved = state);
        Check(resumed.Status == "Completed" && !Directory.Exists(lockedArea), "closing owner allows original fixed list continuation");
        var lost = Record("receipt-loss", out var lostArea);
        Reject<InstallationOutcomeUnknownException>(() => WindowsDeploymentSetup.ApplyVersionPruning(lost, "receipt", () => "registration", state =>
        {
            if (state.Status != "Prepared") throw new IOException("synthetic final receipt loss"); saved = state;
        }), "lost final receipt stays Unknown with prepared original");
        Check(saved?.Status == "Prepared" && !Directory.Exists(lostArea), "deletion facts are not inferred from a lost receipt");
        Check(WindowsDeploymentSetup.ApplyVersionPruning(lost, "receipt", () => "registration", _ => { }).Status == "Completed", "original intent resolves lost final receipt without new scope");
        var retained = WindowsDeploymentSetup.RetainPruneRemainder(stale, "synthetic-disposition-admin");
        Check(retained.Status == "RetainedUnverified" && retained.Plan == stale.Plan && retained.Remaining.Single().Contains(staleArea, StringComparison.Ordinal), "explicit keep disposition preserves complete original scope without asserting deletion");
        Check(retained.WindowsAdministratorSid == stale.WindowsAdministratorSid && retained.DispositionAdministratorSid == "synthetic-disposition-admin", "original administrator and later keep decision remain separate evidence");
        Check(File.Exists(Path.Combine(staleArea, "app", "Ipc.Synthetic.dll")), "keeping remainder has no filesystem side effect");
        Check(WindowsDeploymentSetup.RetainPruneRemainder(retained, "another-admin") == retained, "terminal keep disposition cannot silently replace original decision");

        var unsafeArea = Record("unsafe", out var unsafeRoot).Plan.Delete[0];
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ValidatePruneFiles([unsafeArea with { Files = new() { ["../outside"] = new string('a', 64) } }]), "path traversal denied before deleting any file");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ValidatePruneFiles([unsafeArea, unsafeArea with { Root = Path.Combine(unsafeRoot, "app") }]), "overlapping roots denied");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ValidatePruneFiles([unsafeArea with { Files = new() { ["app/Ipc.Synthetic.dll"] = "not-a-hash" } }]), "missing byte identity denied");
        var location = new DeploymentLocation("sportmaster", new string('a', 40), Path.Combine(root, "programs", "releases", "sportmaster-" + new string('a', 12) + "-" + Wire.Id()), "config", "data", "synthetic");
        WindowsDeploymentSetup.RequireVersionDirectory(Path.Combine(root, "programs"), location);
        Check(true, "actual installer version-directory grammar accepted");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.RequireVersionDirectory(Path.Combine(root, "programs"), location with { ReleaseRoot = Path.Combine(root, "programs") }), "program root cannot be used as a version deletion root");

        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "sqlite"), deferred: true);
        await h.StartAsync(); await h.App.DisposeAsync();
        var directory = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, "sportmaster");
        var database = Path.Combine(directory, "sportmaster.db"); var hash = Wire.FileHash(database);
        var core = new ParcelStartupMaintenance(h.Launch, new SportmasterFlow(), () => new ManualSupplyHarness.ManualFactory(h.Launch), () => { }, () => "synthetic-admin")
        {
            MaintenanceWorkspace = () => { var path = Path.Combine(root, "snapshot-" + Wire.Id()); Directory.CreateDirectory(path); return path; }
        };
        var backup = Path.Combine(root, "before-prune.db");
        var preview = await core.PlanChangeAsync("PruneVersions", backup);
        Check(preview.Allowed, "actual confirmed current SQLite root can be reviewed for version cleanup");
        var called = false;
        await core.ExecuteChangeAsync(preview.Identity, "PruneVersions", backup, () =>
        {
            _ = ParcelRecovery.VerifyBackup(backup, () => { }); called = true;
            Reject<IOException>(() => { using var competitor = new ResourceLease(directory, ["application-owner"]); }, "backup and program cleanup share actual offline application owner");
            return Task.CompletedTask;
        });
        Check(called && Wire.FileHash(database) == hash, "complete backup precedes callback and current business DB remains unchanged");
        var configurationFile = Path.Combine(directory, "configuration.json");
        var originalConfiguration = File.ReadAllBytes(configurationFile);
        var configuration = ConfigurationRevisions<ParcelConfiguration>.ReadCurrentEnvelope(File.ReadAllText(configurationFile));
        ExtensionStore.AtomicWrite(configurationFile, configuration with { Pending = configuration.Effective, Status = "PendingRestart" });
        Check(!(await core.PlanChangeAsync("PruneVersions", Path.Combine(root, "pending-blocked.db"))).Allowed, "pending configuration pins old program versions until ordinary activation confirmation");
        File.WriteAllBytes(configurationFile, originalConfiguration);
        var historical = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), "synthetic-old-station", "sportmaster"); Directory.CreateDirectory(historical);
        File.WriteAllText(Path.Combine(historical, "unknown-original.db"), "synthetic unresolved historical root");
        Check(!(await core.PlanChangeAsync("PruneVersions", Path.Combine(root, "blocked.db"))).Allowed, "another historical station root conservatively pins all versions");
        Check(Wire.FileHash(database) == hash, "historical-root refusal does not mutate original DB");
        Console.WriteLine("IE01-PRUNE checks=" + checks + "; actual file/SQLite and locked files; protected catalog/Windows administrator/VM NOT_RUN");
    }
}
