using System.Runtime.InteropServices;
using Ipc.Platform;

static class InstallerOptions
{
    public static void Run(string root)
    {
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
        var location = new DeploymentLocation("sportmaster", new string('a', 40), Path.Combine(root, "release-12345678"), Path.Combine(root, "config"), Path.Combine(root, "data"), "S-1-5-21-1");
        var programs = Path.Combine(root, "Programs"); var desktop = Path.Combine(root, "Desktop");
        var request = new WindowsDeploymentRequest(root, "sportmaster", location.SourceCommit, location.RuntimeAccountSid, location.DataRoot, location.ConfigurationDirectory, RunMode.Simulation, null, null, []);
        Check(!Wire.Encode(request).Contains("Shortcuts") && !Wire.Encode(location).Contains("Shortcuts"), "legacy requests and registrations omit absent option without changing stored plan hashes");
        Check(Wire.Encode(Wire.Decode<WindowsDeploymentRequest>(Wire.Encode(request))) == Wire.Encode(request), "legacy request wire bytes round trip unchanged");
        Check(DeploymentShortcuts.Plan(location, programs, desktop).Length == 3, "historical registration retains three Start Menu entries and no desktop entry");
        foreach (var menu in new[] { false, true }) foreach (var icon in new[] { false, true })
        {
            var selected = new DeploymentShortcutOptions(menu, icon);
            var serialized = Wire.Encode(request with { Shortcuts = selected });
            Check(Wire.Decode<WindowsDeploymentRequest>(serialized).Shortcuts == selected && Wire.Hash(serialized) != Wire.Hash(Wire.Encode(request)), $"request binds explicit menu={menu} desktop={icon} in reviewed identity");
            var registered = Wire.Decode<DeploymentLocation>(Wire.Encode(location with { Shortcuts = selected }));
            var plan = DeploymentShortcuts.Plan(registered, programs, desktop);
            Check(plan.Length == (menu ? 3 : 0) + (icon ? 1 : 0) && plan.All(p => p.Target == Path.Combine(location.ReleaseRoot, "desktop", "Ipc.Desktop.exe") && p.WorkingDirectory == location.ReleaseRoot), $"registered menu={menu} desktop={icon} targets installed release");
        }
        if (!OperatingSystem.IsWindows()) { Console.WriteLine("NOT_RUN Windows COM shortcuts"); return; }
        DeploymentShortcuts.Create(location with { Shortcuts = new(false, false) }, programs, desktop);
        Check(!Directory.Exists(programs) && !Directory.Exists(desktop), "unchecked choices create neither folder nor link");
        var chosen = location with { Shortcuts = new(true, true) };
        DeploymentShortcuts.Create(chosen, programs, desktop); DeploymentShortcuts.Create(chosen, programs, desktop);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        try
        {
            foreach (var entry in DeploymentShortcuts.Plan(chosen, programs, desktop))
            {
                dynamic link = shell.CreateShortcut(entry.Path);
                try { Check(File.Exists(entry.Path) && string.Equals((string)link.TargetPath, entry.Target, StringComparison.OrdinalIgnoreCase) && (string)link.Arguments == entry.Arguments && string.Equals((string)link.WorkingDirectory, entry.WorkingDirectory, StringComparison.OrdinalIgnoreCase) && ((string)link.IconLocation).Contains("Ipc.Desktop.exe"), "actual COM shortcut readback: " + Path.GetFileName(entry.Path)); }
                finally { Marshal.FinalReleaseComObject(link); }
            }
            var conflict = DeploymentShortcuts.Plan(chosen, programs, desktop).Last();
            dynamic other = shell.CreateShortcut(conflict.Path);
            try { other.TargetPath = Path.Combine(root, "other.exe"); other.Save(); } finally { Marshal.FinalReleaseComObject(other); }
            try { DeploymentShortcuts.Create(chosen, programs, desktop); throw new Exception("conflicting link unexpectedly overwritten"); }
            catch (IOException) { Check(true, "different-target shortcut is preserved and reported"); }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
        var unrelated = Path.Combine(desktop, "unrelated.txt"); File.WriteAllText(unrelated, "keep");
        DeploymentShortcuts.Remove(chosen, programs, desktop);
        Check(DeploymentShortcuts.Plan(chosen, programs, desktop).All(p => !File.Exists(p.Path)) && File.ReadAllText(unrelated) == "keep", "removal handles both selected locations and preserves unrelated files");
        Console.WriteLine("NOT_RUN machine-wide installation, UAC, devices and field acceptance; shortcut COM operations used isolated temporary roots only.");
    }
}
