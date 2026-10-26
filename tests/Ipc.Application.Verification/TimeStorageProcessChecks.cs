using System.Diagnostics;
using Ipc.Platform;

static class TimeStorageProcessChecks
{
    public static async Task Supervisor(string root)
    {
        var location = new DeploymentLocation("sportmaster", new string('a', 40), Path.Combine(root, "release"), Path.Combine(root, "config"), Path.Combine(root, "data"), "synthetic-account")
        { Recovery = new(RestartDelaySeconds: 1, MaximumRestarts: 2, HealthyResetSeconds: 60) };
        var launches = 0;
        Process Launch()
        {
            launches++; var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { typeof(TimeStorageProcessChecks).Assembly.Location, root, "--time-storage-exit-child", "78" }) start.ArgumentList.Add(arg);
            return Process.Start(start)!;
        }
        var result = await DesktopRecoverySupervisor.RunAsync(location, () => location, launch: _ => Launch());
        if (result != 77 || launches != 1) throw new Exception("FAIL ST08 permanent storage exit must block after one launch; actual=" + launches);
        Console.WriteLine("PASS ST08 permanent storage failure blocks supervision after one launch");
    }
}
