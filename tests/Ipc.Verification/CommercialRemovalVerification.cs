using Ipc.Platform;

static class CommercialRemovalVerification
{
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
        var release = Path.Combine(root, "release"); Directory.CreateDirectory(Path.Combine(release, "app"));
        var owned = Path.Combine(release, "app", "owned.txt"); File.WriteAllText(owned, "synthetic inventory");
        var foreign = Path.Combine(release, "foreign.txt"); File.WriteAllText(foreign, "retain");
        try { WindowsDeploymentSetup.CleanRemovalInventory(release, ["app/owned.txt", "../outside.txt"], _ => throw new Exception("No scheduling")); throw new Exception("Traversal accepted"); }
        catch (InvalidDataException) { Check(File.Exists(owned), "entire removal inventory is validated before the first deletion"); }
        var result = WindowsDeploymentSetup.CleanRemovalInventory(release, ["app/owned.txt"], _ => throw new Exception("No scheduling"));
        Check(!File.Exists(owned) && File.ReadAllText(foreign) == "retain" && result.Remaining.Length != 0, "unlisted files survive and are reported by nonrecursive removal");
        File.Delete(foreign);
        result = WindowsDeploymentSetup.CleanRemovalInventory(release, ["app/owned.txt"], _ => throw new Exception("No scheduling"));
        Check(!Directory.Exists(release) && result.Remaining.Length == 0, "reentry completes empty inventoried directories");
        result = WindowsDeploymentSetup.CleanRemovalInventory(release, ["app/owned.txt"], _ => throw new Exception("No scheduling"));
        Check(!result.RestartRequired && result.Remaining.Length == 0, "already deleted release root is a completed reentry");
        try { WindowsDeploymentSetup.CleanRemovalInventory(release, ["../outside"], _ => true); throw new Exception("Absent root bypassed validation"); }
        catch (InvalidDataException) { Check(true, "absent roots cannot bypass unsafe inventory rejection"); }
        File.WriteAllText(release, "replacement");
        try { WindowsDeploymentSetup.CleanRemovalInventory(release, ["app/owned.txt"], _ => true); throw new Exception("Replacement accepted"); }
        catch (InvalidDataException) { Check(File.ReadAllText(release) == "replacement", "root replaced by a file is retained and rejected"); }
        File.Delete(release);
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(Path.Combine(release, "app")); File.WriteAllText(owned, "locked");
            var scheduled = new List<string>();
            using (var locked = new FileStream(owned, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                result = WindowsDeploymentSetup.CleanRemovalInventory(release, ["app/owned.txt"], path => { scheduled.Add(path); return true; });
                Check(result.RestartRequired && File.Exists(owned) && scheduled.Contains(owned), "real Windows sharing violation records restart intent through a synthetic queue callback");
            }
            result = WindowsDeploymentSetup.CleanRemovalInventory(release, ["app/owned.txt"], _ => throw new Exception("Unlocked file should delete"));
            Check(!result.RestartRequired && result.Remaining.Length == 0, "post-unlock reentry completes the original inventory");
        }
        Directory.CreateDirectory(Path.Combine(release, "app")); File.WriteAllText(owned, "pending reboot");
        var original = new WindowsRemovalReceipt("RestartRequired", "original-journal.json", ["retained-data"], true, ["app/owned.txt"], release);
        var observed = WindowsDeploymentSetup.ObserveRemoval(original, ["app/owned.txt"]);
        Check(observed.Status == "RestartRequired" && observed.RestartRequired && File.ReadAllText(owned) == "pending reboot", "read-only inspection reports actual inventoried leftovers without deleting locked or unlocked files");
        File.Delete(owned); File.WriteAllText(foreign, "never delete");
        observed = WindowsDeploymentSetup.ObserveRemoval(original, ["app/owned.txt"]);
        Check(observed.Status == "RemovedWithRetainedFiles" && !observed.RestartRequired && observed.RemainingFiles!.Contains("foreign.txt") && !observed.RemainingFiles!.Any(p => p.EndsWith("owned.txt")) && File.ReadAllText(foreign) == "never delete", "inspection drops stale reboot leftovers and exposes current unlisted files without modifying them");
        File.Delete(foreign); Directory.Delete(Path.Combine(release, "app")); Directory.Delete(release);
        observed = WindowsDeploymentSetup.ObserveRemoval(original, ["app/owned.txt"]);
        Check(observed.Status == "RemovedDataRetained" && !observed.RestartRequired && observed.RemainingFiles!.Length == 0 && original.RestartRequired && original.RemainingFiles!.Length == 1 && observed.OperationFile == original.OperationFile, "post-reboot observation clears stale remaining files while preserving original receipt and recorded facts");
        observed = WindowsDeploymentSetup.ObserveRemoval(original with { Status = "RetiredCleanupPending" }, ["app/owned.txt"]);
        Check(observed.Status == "RetiredCleanupPending", "absent directory cannot turn an unconfirmed original operation into completion");
        Check(WindowsDeploymentSetup.ObserveRemoval(original with { Status = "Unknown" }, ["app/owned.txt"]).Status == "Unknown", "disk observation cannot manufacture a completed receipt from an unknown journal status");
        File.WriteAllText(release, "replacement");
        try { WindowsDeploymentSetup.ObserveRemoval(original, ["app/owned.txt"]); throw new Exception("Inspection accepted replacement"); }
        catch (InvalidDataException) { Check(File.ReadAllText(release) == "replacement", "read-only inspection rejects a replaced root without deleting it"); }
        File.Delete(release);
        try { WindowsDeploymentSetup.ObserveRemoval(original, ["../outside"]); throw new Exception("Inspection accepted unsafe inventory"); }
        catch (InvalidDataException) { Check(true, "inspection validates original inventory even after root deletion"); }
        var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "retain.txt"), "outside original tree");
        Directory.CreateDirectory(release);
        Directory.CreateSymbolicLink(Path.Combine(release, "linked"), outside);
        try { WindowsDeploymentSetup.ObserveRemoval(original, ["app/owned.txt"]); throw new Exception("Inspection followed a link"); }
        catch (IOException) { Check(File.ReadAllText(Path.Combine(outside, "retain.txt")) == "outside original tree", "inspection refuses linked descendants without following or modifying outside data"); }
        Directory.Delete(Path.Combine(release, "linked"));
        for (var i = 0; i < 32769; i++) File.WriteAllText(Path.Combine(release, i + ".txt"), "synthetic");
        try { WindowsDeploymentSetup.ObserveRemoval(original, ["app/owned.txt"]); throw new Exception("Inspection exceeded budget"); }
        catch (InvalidDataException) { Check(Directory.EnumerateFiles(release).Count() == 32769, "oversized read-only observation refuses completion and preserves every extra file"); }
        Console.WriteLine("NOT_RUN protected uninstall entry, ARP/UAC and actual reboot deletion; this fixture only calls the internal inventory kernel.");
    }
}
