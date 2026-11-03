using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

// IE-P02 permanent deletion: real files in temporary roots, the actual SQLite business check,
// and the platform plan/fence/executor. Windows registration, ACLs and the elevated helper are
// exercised only on Windows (NOT_RUN here).
static class PermanentDeletion02
{
    public static async Task Run(string workspace, string root)
    {
        var checks = 0;
        void Check(bool value, string detail) { if (!value) throw new Exception("FAIL IE01-PURGE " + detail); checks++; Console.WriteLine("PASS IE01-PURGE " + detail); }
        void Reject<T>(Action action, string detail) where T : Exception
        { try { action(); } catch (T) { Check(true, detail); return; } throw new Exception("Expected " + typeof(T).Name + ": " + detail); }

        var machine = Path.Combine(root, "purge-machine"); Directory.CreateDirectory(Path.Combine(machine, "deployments"));
        var settled = new DeploymentPurgeBusinessCheck("business-plan", true, null);
        DeploymentLocation Station(string name)
        {
            var data = Path.Combine(root, name, "data"); var config = Path.Combine(root, name, "config");
            Directory.CreateDirectory(Path.Combine(data, "Production", "S1", "sportmaster", "images"));
            File.WriteAllText(Path.Combine(data, "Production", "S1", "sportmaster", "sportmaster.db"), "synthetic db " + name);
            File.WriteAllText(Path.Combine(data, "Production", "S1", "sportmaster", "images", "a.jpg"), "synthetic image");
            File.WriteAllText(Path.Combine(data, "Production", "S1", "sportmaster", Wire.Hash("application-owner") + ".lease"), "pid=1");
            Directory.CreateDirectory(config); File.WriteAllText(Path.Combine(config, "business.json"), "{}");
            return new("sportmaster", new string('a', 40), Path.Combine(root, name, "program"), config, data, "synthetic-sid", "RemovedDataRetained");
        }
        DeploymentPurgePlan Plan(DeploymentLocation location, DeploymentPurgeBusinessCheck? business = null) =>
            WindowsDeploymentSetup.PlanDataPurgeCore(location, "registration", Wire.Id(), business ?? settled, machine);

        var installed = Station("installed");
        Check(!Plan(installed with { LifecycleState = "Installed" }).Allowed, "an installed station cannot be purged; uninstall is a separate first task");
        var unsettled = Plan(installed, new("plan", false, "仍有未完成或结果未确认的请求。"));
        Check(!unsettled.Allowed && unsettled.Reason.Contains("结果未确认"),
            "unsettled or Unknown requests block deletion and say why");
        Check(!WindowsDeploymentSetup.PlanDataPurgeCore(installed, "registration", Wire.Id(), null, machine).Allowed, "no business check means no deletion");
        Check(!Plan(installed with { RestoreHandoffHash = "handoff" }).Allowed, "a restored station with an open handoff is not deleted");
        Check(!Plan(installed with { LifecycleState = "PartiallyPurged" }).Allowed, "a partial purge only continues its original list");
        Check(!Plan(installed with { DataRoot = machine }).Allowed && !Plan(installed with { DataRoot = Path.GetPathRoot(root)! }).Allowed,
            "the machine root and a disk root never enter the deletion scope");
        Check(!Plan(installed with { ConfigurationDirectory = Path.Combine(installed.DataRoot, "Production") }).Allowed, "nested data and configuration roots are refused");

        var plan = Plan(installed);
        Check(plan.Allowed && plan.Delete.Sum(d => d.Files.Count) == 4 && plan.Retained.Any(r => r.Contains("登记")), "the plan lists every station file and discloses what stays");
        Check(plan.Delete.SelectMany(d => d.Files).Single(f => f.Key.EndsWith(".lease", StringComparison.Ordinal)).Value == WindowsDeploymentSetup.LeaseHash,
            "owner lease files are listed without a byte fence");

        var registration = new List<string>(); DeploymentPurgeRecord? saved = null;
        DeploymentPurgeRecord Record(DeploymentPurgePlan p) => new(1, DateTimeOffset.UtcNow, p, "Prepared", [], "synthetic-admin");

        var changed = Station("changed"); var changedPlan = Plan(changed);
        File.WriteAllText(Path.Combine(changed.DataRoot, "Production", "S1", "sportmaster", "sportmaster.db"), "a request was written after review");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ApplyDataPurge(Record(changedPlan), "receipt", () => "registration", s => saved = s, registration.Add), "changed bytes refuse the whole plan");
        var added = Station("added"); var addedPlan = Plan(added);
        File.WriteAllText(Path.Combine(added.DataRoot, "late-receipt.json"), "{}");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ApplyDataPurge(Record(addedPlan), "receipt", () => "registration", s => saved = s, registration.Add), "a file added after review refuses the whole plan");
        Reject<InvalidOperationException>(() => WindowsDeploymentSetup.ApplyDataPurge(Record(plan), "receipt", () => "other-registration", s => saved = s, registration.Add), "a changed registration refuses before any write");
        Check(saved is null && registration.Count == 0 && File.Exists(Path.Combine(changed.DataRoot, "Production", "S1", "sportmaster", "images", "a.jpg")),
            "refusals write nothing and delete nothing");

        var cancelled = Station("cancelled"); var cancelledPlan = Plan(cancelled);
        using (var execution = new MaintenanceExecution())
        {
            Reject<OperationCanceledException>(() => WindowsDeploymentSetup.ApplyDataPurge(Record(cancelledPlan), "receipt", () => "registration",
                s => { saved = s; if (s.Status == "Prepared") execution.TryCancel(); }, registration.Add, execution), "cancel before the first delete is accepted");
            Check(saved?.Status == "NotStarted" && registration.Count == 0 && Directory.Exists(cancelled.DataRoot), "a cancelled purge keeps every file and the registration");
        }

        var done = WindowsDeploymentSetup.ApplyDataPurge(Record(plan), "receipt", () => "registration", s => saved = s, registration.Add);
        Check(done.Status == "Purged" && saved?.Status == "Purged" && registration.SequenceEqual(["Purging", "Purged"]), "registration reads Purging before the first delete and Purged after");
        Check(!Directory.Exists(installed.DataRoot) && !Directory.Exists(installed.ConfigurationDirectory), "listed files and their emptied directories are gone");
        Check(Directory.Exists(Path.Combine(machine, "deployments")) && Directory.Exists(Path.Combine(root, "changed")), "deployment records and other stations are untouched");

        var extra = Station("extra"); var extraPlan = Plan(extra); registration.Clear();
        var partial = WindowsDeploymentSetup.ApplyDataPurge(Record(extraPlan) with { Status = "Purging" }, "receipt", () => "registration", s => saved = s, registration.Add);
        Check(partial.Status == "Purged", "an interrupted purge resumes with the original list");
        var leftover = Station("leftover"); var leftoverPlan = Plan(leftover); registration.Clear();
        File.WriteAllText(Path.Combine(leftover.DataRoot, "Production", "S1", "sportmaster", "images", "b.jpg"), "unlisted");
        var kept = WindowsDeploymentSetup.ApplyDataPurge(Record(leftoverPlan) with { Status = "Purging" }, "receipt", () => "registration", s => saved = s, registration.Add);
        Check(kept.Status == "PartiallyPurged" && registration[^1] == "PartiallyPurged" && File.Exists(Path.Combine(leftover.DataRoot, "Production", "S1", "sportmaster", "images", "b.jpg")) &&
            !File.Exists(Path.Combine(leftover.DataRoot, "Production", "S1", "sportmaster", "sportmaster.db")), "an unlisted file is never deleted and the station stays PartiallyPurged");
        var lost = Station("lost"); var lostPlan = Plan(lost);
        Reject<InstallationOutcomeUnknownException>(() => WindowsDeploymentSetup.ApplyDataPurge(Record(lostPlan), "receipt", () => "registration",
            s => { if (s.Status == "Purged") throw new IOException("synthetic receipt loss"); saved = s; }, registration.Add), "a lost final receipt reads as Unknown");
        Check(saved?.Status == "Purging", "the last confirmed state is Purging, never inferred as done");

        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "purge-sqlite"), deferred: true);
        await h.StartAsync(); await h.App.DisposeAsync();
        var directory = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, "sportmaster");
        var database = Path.Combine(directory, "sportmaster.db"); var hash = Wire.FileHash(database);
        var core = new ParcelStartupMaintenance(h.Launch, new SportmasterFlow(), () => new ManualSupplyHarness.ManualFactory(h.Launch), () => { }, () => "synthetic-admin")
        {
            MaintenanceWorkspace = () => { var path = Path.Combine(root, "purge-snapshot-" + Wire.Id()); Directory.CreateDirectory(path); return path; }
        };
        var preview = await core.PlanChangeAsync("Purge", null);
        Check(preview.Allowed && preview.OperatorSummary!.Contains("结清"), "a settled SQLite station passes the business check");
        var backup = Path.Combine(root, "before-purge.db");
        var withBackup = await core.PlanChangeAsync("Purge", backup);
        await core.ExecuteChangeAsync(withBackup.Identity, "Purge", backup, () => Task.CompletedTask);
        Check(File.Exists(backup) && Wire.FileHash(database) == hash, "the optional full backup is taken before deletion and the original is unchanged");
        File.WriteAllText(Path.Combine(directory, "RESTORE-INCOMPLETE.json"), "{}");
        Check(!(await core.PlanChangeAsync("Purge", null)).Allowed, "a restore marker blocks permanent deletion");
        File.Delete(Path.Combine(directory, "RESTORE-INCOMPLETE.json"));
        var historical = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), "synthetic-old-station", "sportmaster"); Directory.CreateDirectory(historical);
        File.WriteAllText(Path.Combine(historical, "unknown-original.db"), "synthetic historical root");
        Check(!(await core.PlanChangeAsync("Purge", null)).Allowed, "another station's root blocks deletion of the shared data root");
        Console.WriteLine("IE01-PURGE checks=" + checks + "; real files and SQLite; Windows registration/ACL/elevated helper/VM NOT_RUN");
    }
}
