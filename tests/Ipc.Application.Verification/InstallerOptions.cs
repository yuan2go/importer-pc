using Ipc.Platform;

static class InstallerOptions
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern uint GetACP();
    public static void RequireCodePage(string expected)
    {
        var actual = GetACP();
        if (actual != uint.Parse(expected, System.Globalization.CultureInfo.InvariantCulture)) throw new Exception($"ACP mismatch: expected {expected}, actual {actual}");
        Console.WriteLine($"PASS actual process ACP={actual}");
    }
    public static void Run(string root)
    {
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
        var location = new DeploymentLocation("sportmaster", new string('a', 40), Path.Combine(root, "发布目录-12345678"), Path.Combine(root, "config"), Path.Combine(root, "data"), "S-1-5-21-1");
        var programs = Path.Combine(root, "开始菜单"); var desktop = Path.Combine(root, "桌面");
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
        // Real installation projects shortcuts only after the release exists.
        void ReleaseDirectory(DeploymentLocation deployment)
        {
            var directory = Path.Combine(deployment.ReleaseRoot, "desktop"); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "Ipc.Desktop.exe"), "synthetic shortcut target; never executed");
        }
        ReleaseDirectory(chosen);
        Check(Path.GetFileName(DeploymentShortcuts.Plan(chosen, programs, desktop).Last().Path) == "菜鸟供包台.lnk", "desktop name is exactly the product name without module, commit or deployment suffix");
        DeploymentShortcuts.Create(chosen, programs, desktop); DeploymentShortcuts.Create(chosen, programs, desktop);
        {
            foreach (var entry in DeploymentShortcuts.Plan(chosen, programs, desktop))
            {
                using var link = new ShellLinkFile(entry.Path);
                Check(File.Exists(entry.Path) && string.Equals(link.TargetPath, entry.Target, StringComparison.OrdinalIgnoreCase) && link.Arguments == entry.Arguments && string.Equals(link.WorkingDirectory, entry.WorkingDirectory, StringComparison.OrdinalIgnoreCase) && link.IconLocation == entry.Target + ",0" && link.Description == "菜鸟供包台 · " + chosen.ApplicationId, "actual Unicode COM shortcut readback: " + Path.GetFileName(entry.Path));
            }
            var upgraded = chosen with { SourceCommit = new string('b', 40), ReleaseRoot = Path.Combine(root, "升级目录-87654321") };
            ReleaseDirectory(upgraded);
            foreach (var wrongPrevious in new[] { chosen with { RuntimeAccountSid = "S-1-5-21-2" }, chosen with { DataRoot = Path.Combine(root, "other-data") }, chosen with { ApplicationId = "other-app" } })
            {
                try { DeploymentShortcuts.Create(upgraded, programs, desktop, wrongPrevious); throw new Exception("unowned previous deployment unexpectedly replaced"); }
                catch (IOException) { Check(true, "upgrade refuses previous deployment with different account/data/application"); }
            }
            DeploymentShortcuts.Create(upgraded, programs, desktop, chosen);
            DeploymentShortcuts.Remove(chosen, programs, desktop);
            var stable = DeploymentShortcuts.Plan(upgraded, programs, desktop).Last();
            using (var upgradedLink = new ShellLinkFile(stable.Path)) Check(File.Exists(stable.Path) && string.Equals(upgradedLink.TargetPath, stable.Target, StringComparison.OrdinalIgnoreCase), "upgrade updates stable desktop target and old uninstall preserves the new entry");
            var legacy = Path.Combine(desktop, "菜鸟供包台 · sportmaster · aaaaaaaa · 12345678.lnk");
            using (var legacyLink = new ShellLinkFile()) { legacyLink.TargetPath = Path.Combine(chosen.ReleaseRoot, "desktop", "Ipc.Desktop.exe"); legacyLink.Save(legacy); }
            DeploymentShortcuts.Remove(chosen, programs, desktop);
            Check(!File.Exists(legacy) && File.Exists(stable.Path), "legacy suffixed entry is removed by exact ownership without deleting current stable entry");
            DeploymentShortcuts.Remove(upgraded, programs, desktop);
            DeploymentShortcuts.Create(chosen, programs, desktop);
            var conflict = DeploymentShortcuts.Plan(chosen, programs, desktop).Last();
            using (var other = new ShellLinkFile(conflict.Path)) { other.TargetPath = Path.Combine(root, "其他程序.exe"); other.Save(conflict.Path); }
            try { DeploymentShortcuts.Create(chosen, programs, desktop); throw new Exception("conflicting link unexpectedly overwritten"); }
            catch (IOException) { Check(true, "different-target shortcut is preserved and reported"); }
        }
        var unrelated = Path.Combine(desktop, "unrelated.txt"); File.WriteAllText(unrelated, "keep");
        DeploymentShortcuts.Remove(chosen, programs, desktop);
        var retained = DeploymentShortcuts.Plan(chosen, programs, desktop).Last();
        Check(DeploymentShortcuts.Plan(chosen, programs, desktop).SkipLast(1).All(p => !File.Exists(p.Path)) &&
            File.Exists(retained.Path) && File.ReadAllText(unrelated) == "keep", "removal deletes owned links and preserves a retargeted desktop link and unrelated files");
        var menuEntry = DeploymentShortcuts.Plan(chosen, programs, desktop)[0];
        using (var changed = new ShellLinkFile()) { changed.TargetPath = menuEntry.Target; changed.Arguments = "--not-owned"; changed.Save(menuEntry.Path); }
        try { DeploymentShortcuts.Create(chosen, programs, desktop); throw new Exception("arguments conflict unexpectedly overwritten"); }
        catch (IOException) { Check(true, "same-target different-arguments link refuses replacement"); }
        DeploymentShortcuts.Remove(chosen, programs, desktop);
        Check(File.Exists(menuEntry.Path), "uninstall preserves different-arguments link");
        var corrupt = DeploymentShortcuts.Plan(chosen, programs, desktop)[1];
        File.WriteAllText(corrupt.Path, "invalid link");
        var before = File.ReadAllBytes(corrupt.Path);
        try { DeploymentShortcuts.Remove(chosen, programs, desktop); throw new Exception("invalid COM load unexpectedly succeeded"); }
        catch (System.Runtime.InteropServices.COMException) { Check(File.ReadAllBytes(corrupt.Path).SequenceEqual(before), "invalid link read fails visibly and preserves original bytes"); }
        Console.WriteLine("NOT_RUN machine-wide installation, UAC, devices and field acceptance; shortcut COM operations used isolated temporary roots only.");
    }
}
