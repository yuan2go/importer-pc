using Ipc.Platform;

// Only synthetic files under this fixture's new temporary root. Never calls the
// machine deployment, elevation, service, device or real uninstall entry points.
static class UninstallDataBoundary
{
    public static Task Run(string root)
    {
        var failures = new List<string>(); var assertions = 0;
        void Check(bool value, string detail)
        {
            assertions++; Console.WriteLine((value ? "PASS " : "FAIL ") + "UNINSTALL-BOUNDARY " + detail);
            if (!value) failures.Add(detail);
        }
        var machine = Path.Combine(root, "synthetic-machine"); Directory.CreateDirectory(Path.Combine(machine, "deployments"));
        DeploymentLocation Station(string name)
        {
            var data = Path.Combine(root, name, "data"); var config = Path.Combine(root, name, "config");
            Directory.CreateDirectory(data); Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(data, "owned.db"), "synthetic original database");
            File.WriteAllText(Path.Combine(config, "business.json"), "synthetic configuration");
            return new("sportmaster", new string('a', 40), Path.Combine(root, name, "program"), config, data, "synthetic", "RemovedDataRetained");
        }
        DeploymentPurgePlan Plan(DeploymentLocation location) => WindowsDeploymentSetup.PlanDataPurgeCore(location, "registration", Wire.Id(), machine);
        DeploymentPurgeRecord Record(DeploymentPurgePlan plan) => new(2, DateTimeOffset.UtcNow, plan, "Prepared", [], "synthetic");

        var shared = Station("shared");
        var foreign = Path.Combine(shared.DataRoot, "Production", "OTHER", "direct-plc", "direct-plc.db");
        Directory.CreateDirectory(Path.GetDirectoryName(foreign)!); File.WriteAllText(foreign, "other business must survive");
        var other = new DeploymentLocation("direct-plc", new string('b', 40), Path.Combine(root, "other-program"), Path.Combine(root, "other-config"), shared.DataRoot, "synthetic");
        File.WriteAllText(Path.Combine(machine, "deployments", "direct-plc.json"), Wire.Encode(other));
        var sharedPlan = Plan(shared);
        if (sharedPlan.Allowed) WindowsDeploymentSetup.ApplyDataPurge(Record(sharedPlan), "synthetic-receipt", () => "registration", _ => { }, _ => { });
        Check(!sharedPlan.Allowed && File.Exists(foreign), "another registered application's shared data survives station deletion");
        File.Delete(Path.Combine(machine, "deployments", "direct-plc.json"));

        var mutation = Station("mutation"); var mutationPlan = Plan(mutation);
        var target = Path.Combine(mutationPlan.Delete[0].Root, mutationPlan.Delete[0].Files.First().Key);
        DeploymentPurgeRecord? saved = null; DeploymentPurgeReceipt? outcome = null; Exception? error = null;
        try
        {
            outcome = WindowsDeploymentSetup.ApplyDataPurge(Record(mutationPlan), "synthetic-receipt", () => "registration", state =>
            {
                saved = state;
                if (state.Status == "Purging") File.WriteAllText(target, "new unreviewed data written during commit");
            }, _ => { });
        }
        catch (Exception failure) { error = failure; }
        Console.WriteLine("EVIDENCE commit-mutation receipt=" + outcome?.Status + "; saved=" + saved?.Status + "; error=" + error?.GetType().Name + "; fileExists=" + File.Exists(target));
        Check(File.Exists(target) && File.ReadAllText(target) == "new unreviewed data written during commit", "a reviewed filename cannot delete different bytes written during commit");
        Check(outcome?.Status != "Purged" && saved?.Status != "Purged", "a changed file cannot produce a completed data-deletion receipt");

        var original = Station("origin");
        var replacement = Station("replacement");
        Check(!WindowsDeploymentSetup.PlanDataPurgeCore(replacement, "new-registration", Wire.Id(), machine, original).Allowed,
            "an old program-removal result cannot authorize a replacement installation's data");
        Check(!WindowsDeploymentSetup.PlanDataPurgeCore(original with { MaintenanceOperation = "other-removal" }, "new-registration", Wire.Id(), machine, original).Allowed,
            "even identical roots cannot authorize a different retirement operation");
        Check(WindowsDeploymentSetup.PlanDataPurgeCore(original, "registration", Wire.Id(), machine, original).Allowed,
            "the exact retired installation remains eligible for explicitly selected deletion");
        Check(!Plan(original with { DataRoot = Path.Combine(machine, "deployments", "nested-data") }).Allowed,
            "deployment evidence descendants cannot be a deletion root");
        Check(!Plan(original with { DataRoot = Path.Combine(original.ReleaseRoot, "nested-data") }).Allowed,
            "program directory descendants cannot be a deletion root");
        using (WindowsDeploymentSetup.AcquireOtherPurgeDeployments("sportmaster", machine))
        {
            var blocked = false;
            try { using var concurrent = new FileStream(Path.Combine(machine, "deployments", "direct-plc.lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { blocked = true; }
            Check(blocked, "a previously absent supported installation cannot register during data deletion");
        }
        using (var released = new FileStream(Path.Combine(machine, "deployments", "direct-plc.lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(true, "other deployment leases are released after the deletion scope");

        // A partial operation cannot reinterpret a changed remaining file as its original bytes.
        if (saved is not null)
        {
            var refusedResume = false;
            try { WindowsDeploymentSetup.ApplyDataPurge(saved with { Status = "Purging" }, "synthetic-receipt", () => "changed-registration", _ => { }, _ => { }); }
            catch (InvalidDataException) { refusedResume = true; }
            Check(refusedResume && File.Exists(target), "partial deletion retry retains a changed remaining file and its original plan");
        }

        if (OperatingSystem.IsWindows())
        {
            var linked = Station("junction"); var destination = Station("junction-outside");
            var junction = Path.Combine(linked.DataRoot, "alias");
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "/c", "mklink", "/J", junction, destination.DataRoot }) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!; process.WaitForExit();
            if (process.ExitCode != 0) throw new Exception("Synthetic junction fixture failed: " + process.StandardError.ReadToEnd());
            try
            {
                var refusedLink = false;
                try { _ = Plan(linked); } catch (InvalidDataException) { refusedLink = true; }
                Check(refusedLink && File.Exists(Path.Combine(destination.DataRoot, "owned.db")), "a Windows junction never imports its external target into the deletion inventory");
                var rejectedHandle = false;
                try { WindowsDeploymentSetup.DeleteReviewedPath(Path.Combine(junction, "owned.db"), Wire.FileHash(Path.Combine(destination.DataRoot, "owned.db"))); }
                catch (IOException) { rejectedHandle = true; }
                Check(rejectedHandle && File.Exists(Path.Combine(destination.DataRoot, "owned.db")), "the Windows deletion handle also refuses a linked ancestor");
            }
            finally { Directory.Delete(junction); }
        }

        var outside = Station("outside"); var sentinel = Path.Combine(outside.DataRoot, "owned.db");
        var traversal = Station("traversal"); var traversalPlan = Plan(traversal);
        var invalid = traversalPlan with { Delete = [new(traversal.DataRoot, new() { ["../outside-sentinel"] = new string('a', 64) })] };
        var refused = false;
        try { WindowsDeploymentSetup.ApplyDataPurge(Record(invalid), "synthetic-receipt", () => "registration", _ => { }, _ => { }); }
        catch (InvalidDataException) { refused = true; }
        Check(refused && File.Exists(sentinel), "invalid relative names cannot reach another synthetic root");
        Console.WriteLine("UNINSTALL-BOUNDARY assertions=" + assertions + "; failures=" + failures.Count + "; protected Windows uninstall/UAC/deployment NOT_RUN");
        if (failures.Count != 0) throw new Exception("Boundary failures: " + string.Join("; ", failures));
        return Task.CompletedTask;
    }
}
