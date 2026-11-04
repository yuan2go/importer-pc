using Ipc.Platform;

// Real files and the platform deletion fence. No business database or request
// inspection is required after program ownership has been released.
static class PermanentDeletion02
{
    public static Task Run(string workspace, string root)
    {
        var checks = 0;
        void Check(bool value, string detail) { if (!value) throw new Exception("FAIL IE01-PURGE " + detail); checks++; Console.WriteLine("PASS IE01-PURGE " + detail); }
        void Reject<T>(Action action, string detail) where T : Exception
        { try { action(); } catch (T) { Check(true, detail); return; } throw new Exception("Expected " + typeof(T).Name + ": " + detail); }

        var machine = Path.Combine(root, "purge-machine"); Directory.CreateDirectory(Path.Combine(machine, "deployments"));
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
        DeploymentPurgePlan Plan(DeploymentLocation location) =>
            WindowsDeploymentSetup.PlanDataPurgeCore(location, "registration", Wire.Id(), machine);

        var installed = Station("installed");
        Check(!Plan(installed with { LifecycleState = "Installed" }).Allowed, "data deletion requires the program retirement to commit first");
        Check(Plan(installed).Allowed, "closed station data can be deleted without parsing its synthetic invalid database");
        Check(Plan(installed with { RestoreHandoffHash = "handoff" }).Allowed, "restore handoff metadata does not block explicitly selected uninstall deletion");
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

        var combined = Station("combined-uninstall"); var combinedPlan = Plan(combined);
        using (var execution = new MaintenanceExecution())
        {
            execution.BeginCommit(MaintenanceStage.RetiringDeployment);
            var combinedResult = WindowsDeploymentSetup.ApplyDataPurge(Record(combinedPlan), "combined-receipt", () => "registration", s => saved = s, registration.Add, execution);
            Check(combinedResult.Status == "Purged" && !Directory.Exists(combined.DataRoot) && !Directory.Exists(combined.ConfigurationDirectory),
                "one uninstall execution deletes settled data after its program-retirement commit without starting a second transaction");
            Check(!execution.TryCancel(), "cancellation remains sealed between program removal and data deletion");
        }

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

        var unconfirmed = Station("unconfirmed");
        var directory = Path.Combine(unconfirmed.DataRoot, "Production", "S1", "sportmaster");
        File.WriteAllText(Path.Combine(directory, "request.json"), "{\"state\":\"Unknown\"}");
        File.WriteAllText(Path.Combine(directory, "RESTORE-INCOMPLETE.json"), "{}");
        File.WriteAllText(Path.Combine(unconfirmed.ConfigurationDirectory, "business.json"), "unreadable configuration");
        var unconfirmedPlan = Plan(unconfirmed);
        Check(unconfirmedPlan.Allowed, "Unknown request bytes, recovery markers and unreadable configuration are not uninstall gates");
        var unconfirmedResult = WindowsDeploymentSetup.ApplyDataPurge(Record(unconfirmedPlan), "receipt", () => "registration", s => saved = s, registration.Add);
        Check(unconfirmedResult.Status == "Purged" && !Directory.Exists(unconfirmed.DataRoot), "explicit deletion removes closed station data without completing or rewriting its requests");
        Check(!Directory.EnumerateFiles(Path.GetDirectoryName(unconfirmed.DataRoot)!, "*.db", SearchOption.AllDirectories).Any(), "uninstall deletion creates no database backup");
        var absent = unconfirmed with { DataRoot = Path.Combine(root, "absent-data"), ConfigurationDirectory = Path.Combine(root, "absent-config") };
        var absentResult = WindowsDeploymentSetup.ApplyDataPurge(Record(Plan(absent)), "receipt", () => "registration", s => saved = s, registration.Add);
        Check(absentResult.Status == "Purged" && !Directory.Exists(absent.DataRoot), "already absent data directories finish without being recreated");
        Directory.CreateDirectory(Path.Combine(absent.DataRoot, "empty", "nested")); Directory.CreateDirectory(absent.ConfigurationDirectory);
        var emptyResult = WindowsDeploymentSetup.ApplyDataPurge(Record(Plan(absent)), "receipt", () => "registration", s => saved = s, registration.Add);
        Check(emptyResult.Status == "Purged" && !Directory.Exists(absent.DataRoot) && !Directory.Exists(absent.ConfigurationDirectory), "empty station roots are removed without another cleanup workflow");
        Console.WriteLine("IE01-PURGE checks=" + checks + "; real files; Windows registration/ACL/elevated helper/VM NOT_RUN");
        return Task.CompletedTask;
    }
}
