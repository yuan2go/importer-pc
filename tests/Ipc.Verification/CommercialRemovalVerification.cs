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
        Console.WriteLine("NOT_RUN protected uninstall entry, ARP/UAC and actual reboot deletion; this fixture only calls the internal inventory kernel.");
    }
}
