using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using Ipc.Platform;

[SupportedOSPlatform("windows")]
static class ShortcutLifecycle
{
    public static void Run(string root)
    {
        var failures = new List<Exception>(); var checks = 0;
        Console.WriteLine($"OS={Environment.OSVersion}; Apartment={Thread.CurrentThread.GetApartmentState()}");
        void Check(bool value, string name)
        { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
        void Scenario(string name, Action<string> action)
        {
            var directory = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
            try { action(directory); }
            catch (Exception error) { Console.WriteLine($"FAIL {name}: {error}"); failures.Add(error); }
        }
        void Reject(Action action, string name)
        {
            var rejected = false;
            try { action(); } catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException or InvalidDataException or ArgumentException) { rejected = true; }
            Check(rejected, name);
        }
        Scenario("disposed", directory =>
        {
            var link = new ShellLinkFile(); link.Dispose(); link.Dispose();
            var rejected = false;
            try { _ = link.TargetPath; } catch (ObjectDisposedException) { rejected = true; }
            Check(rejected, "disposed wrapper rejects access deterministically after repeated Dispose");
        });
        Scenario("current-user-startup", directory =>
        {
            var options = Wire.Decode<DeploymentShortcutOptions>("""{"StartMenu":true,"Desktop":false}""");
            Check(!options.AutoStart && !Wire.Encode(options).Contains("AutoStart", StringComparison.Ordinal), "old installation JSON preserves the default and original encoding");
            var old = Release(Path.Combine(directory, "old")) with { Shortcuts = new(false, false, true) };
            var current = Release(Path.Combine(directory, "current")) with
            { Shortcuts = old.Shortcuts, DataRoot = old.DataRoot, ConfigurationDirectory = old.ConfigurationDirectory };
            var startup = Directory.CreateDirectory(Path.Combine(directory, "Startup")).FullName;
            var menu = Path.Combine(directory, "菜单");
            var desktop = Path.Combine(directory, "桌面");
            var planned = DeploymentShortcuts.Plan(old, menu, desktop, startup).Single();
            Check(planned.Arguments == "" && Path.GetFileName(planned.Path) == InstallationIdentity.DisplayName + ".lnk",
                "autostart points only to this instance's workstation apphost with empty arguments");
            var other = Path.Combine(startup, InstallationIdentity.IsTest ? "菜鸟供包台.lnk" : "菜鸟供包台（测试）.lnk");
            Write(planned with { Path = other }); var otherHash = Wire.FileHash(other);
            DeploymentShortcuts.Create(old, menu, desktop, startup: startup);
            Check(DeploymentShortcuts.ReadAutoStart(old, startup), "owned user startup shortcut is written and recognized");
            DeploymentShortcuts.Create(current, menu, desktop, old, startup);
            Check(DeploymentShortcuts.ReadAutoStart(current, startup) && !DeploymentShortcuts.ReadAutoStart(old, startup), "update transfers only an owned startup entry to the new executable");
            DeploymentShortcuts.Create(old, menu, desktop, current, startup);
            Check(DeploymentShortcuts.ReadAutoStart(old, startup), "rollback can transfer the same entry back to the retained release");
            DeploymentShortcuts.Create(old with { Shortcuts = new(false, false) }, menu, desktop, startup: startup);
            Check(!File.Exists(planned.Path), "disabling removes the matching startup shortcut");
            DeploymentShortcuts.Create(old, menu, desktop, startup: startup);
            Write(planned with { Target = Path.Combine(directory, "user-selected.exe") }); var foreign = Wire.FileHash(planned.Path);
            DeploymentShortcuts.Remove(old, menu, desktop, startup);
            Check(Wire.FileHash(planned.Path) == foreign && Wire.FileHash(other) == otherHash,
                "uninstall preserves the user-retargeted link and the other instance's distinct startup name");
        });
        Scenario("directory-conflict", directory =>
        {
            var location = Release(directory); var menu = Path.Combine(directory, "菜单"); var desktop = Path.Combine(directory, "桌面");
            var path = DeploymentShortcuts.Plan(location, menu, desktop).Single().Path;
            Directory.CreateDirectory(path);
            Reject(() => DeploymentShortcuts.Create(location, menu, desktop), "create reports a directory at an exact shortcut path");
            Reject(() => DeploymentShortcuts.Remove(location, menu, desktop), "uninstall reports a directory at an exact shortcut path rather than treating it as absent");
            Check(Directory.Exists(path), "uninstall preserves the conflicting directory");
        });
        Scenario("empty-target", directory =>
        {
            var location = Release(directory); var menu = Path.Combine(directory, "菜单"); var desktop = Directory.CreateDirectory(Path.Combine(directory, "桌面")).FullName;
            var path = DeploymentShortcuts.Plan(location, menu, desktop).Single().Path;
            using (var empty = new ShellLinkFile()) empty.Save(path);
            var hash = Wire.FileHash(path);
            Reject(() => DeploymentShortcuts.Create(location, menu, desktop), "repair rejects a valid shell-link container with no file target");
            Reject(() => DeploymentShortcuts.Remove(location, menu, desktop), "uninstall reports an unreadable ownership target instead of silently treating it as foreign");
            Check(Wire.FileHash(path) == hash, "empty-target link is preserved byte for byte");
        });
        Scenario("unicode-and-repair", directory =>
        {
            var location = Release(Path.Combine(directory, "中文𠀀🙂 空格")) with { Shortcuts = new(true, true) };
            var menu = Path.Combine(directory, "菜单𠀀"); var desktop = Path.Combine(directory, "桌面🙂");
            DeploymentShortcuts.Create(location, menu, desktop);
            var plan = DeploymentShortcuts.Plan(location, menu, desktop);
            foreach (var entry in plan)
                Check(ReadTarget(entry.Path) == entry.Target, "independent native Unicode target read: " + Path.GetFileName(entry.Path));
            File.Delete(plan[1].Path);
            using (var damaged = new ShellLinkFile(plan[0].Path)) { damaged.Description = "needs repair"; damaged.SetIcon("incorrect.ico", 3); damaged.Save(plan[0].Path); }
            DeploymentShortcuts.Create(location, menu, desktop);
            foreach (var entry in plan)
            {
                using var link = new ShellLinkFile(entry.Path);
                Check(link.TargetPath == entry.Target && link.Arguments == entry.Arguments && link.WorkingDirectory == entry.WorkingDirectory &&
                    link.Description == "菜鸟供包台" && link.IconLocation == entry.Target + ",0", "repair restores missing entry and owned projection fields: " + Path.GetFileName(entry.Path));
            }
            var helperPath = Path.Combine(directory, "参数𠀀🙂.lnk");
            using (var helper = new ShellLinkFile()) { helper.TargetPath = plan[0].Target; helper.Arguments = "--合成参数=𠀀🙂"; helper.Save(helperPath); }
            using (var helper = new ShellLinkFile(helperPath)) Check(helper.Arguments == "--合成参数=𠀀🙂", "helper Unicode arguments round trip beyond ACP936 repertoire");
            DeploymentShortcuts.Remove(location, menu, desktop); DeploymentShortcuts.Remove(location, menu, desktop);
            Check(plan.All(entry => !File.Exists(entry.Path)), "owned Unicode entries removed and repeated uninstall is idempotent");
        });
        Scenario("stable-sm-names", directory =>
        {
            var old = Release(directory) with { Shortcuts = new(true, true) };
            var menu = Path.Combine(directory, "菜单"); var desktop = Path.Combine(directory, "桌面");
            var plan = DeploymentShortcuts.Plan(old, menu, desktop);
            Check(plan.Length == 3 && plan.Count(e => e.Arguments == "") == 2 && plan.Count(e => Path.GetFileName(e.Path) == "菜鸟供包台.lnk") == 2 &&
                plan.Single(e => Path.GetFileName(e.Path) == "供包运维工具.lnk").Arguments == InstallationIdentity.OperationsArgument && plan.All(e => e.Target == plan[0].Target) &&
                plan.All(e => !e.Path.Contains("sportmaster") && !e.Path.Contains(old.SourceCommit[..8])), "start menu and desktop use the plain product name without internal identities");
            var legacy = Path.Combine(menu, "菜鸟供包台", "菜鸟供包台 · SM供包业务 · sportmaster · " + old.SourceCommit[..8] + " · " + Path.GetFileName(old.ReleaseRoot)[^8..] + " · 运行.lnk");
            Directory.CreateDirectory(Path.GetDirectoryName(legacy)!); Write(plan[0] with { Path = legacy });
            var oldRepair = plan[0] with { Path = Path.Combine(menu, "菜鸟供包台", "菜鸟供包台 · 启动修复.lnk"), Arguments = "--maintenance" };
            Write(oldRepair);
            var changedSetup = plan[0] with { Path = Path.Combine(menu, "菜鸟供包台", "菜鸟供包台 · 安装维护.lnk"), Target = Path.Combine(directory, "user-retargeted.exe"), Arguments = "--setup" };
            Write(changedSetup); var changedHash = Wire.FileHash(changedSetup.Path);
            DeploymentShortcuts.Create(old, menu, desktop);
            Check(!File.Exists(oldRepair.Path) && Wire.FileHash(changedSetup.Path) == changedHash, "replaced owned repair entry is removed while user-retargeted maintenance entry is preserved");
            Check(!File.Exists(legacy) && plan.All(e => File.Exists(e.Path)), "same-version repair replaces the registered long-name entry without leaving a duplicate");
            var next = Release(Path.Combine(directory, "新版本")) with { SourceCommit = new string('b', 40), DataRoot = old.DataRoot, Shortcuts = new(true, true) };
            DeploymentShortcuts.Create(next, menu, desktop, old); DeploymentShortcuts.Remove(old, menu, desktop);
            Check(DeploymentShortcuts.Plan(next, menu, desktop).All(e => ReadTarget(e.Path) == e.Target), "upgrade retargets all stable entries and old-installation cleanup preserves the new links");
            DeploymentShortcuts.Create(next with { Shortcuts = new(false, false) }, menu, desktop, next);
            Check(DeploymentShortcuts.Plan(next, menu, desktop).All(e => !File.Exists(e.Path)) && Wire.FileHash(changedSetup.Path) == changedHash,
                "unchecking menu and desktop removes both owned app entries while preserving the user-retargeted legacy link");
        });
        Scenario("registered-entry-inventory", directory =>
        {
            var current = Release(Path.Combine(directory, "双程序")) with { Shortcuts = new(true, true) };
            var operations = Path.Combine(current.ReleaseRoot, "desktop", InstallationIdentity.LegacyOperationsExecutable);
            File.WriteAllText(operations, "synthetic operations; never executed");
            var menu = Path.Combine(directory, "菜单"); var desktop = Path.Combine(directory, "桌面");
            var legacyInventory = Path.Combine(directory, "legacy-inventory.json");
            File.WriteAllText(legacyInventory, Wire.Encode(new { Files = new Dictionary<string, string> { ["desktop/" + InstallationIdentity.LegacyOperationsExecutable] = Wire.FileHash(operations) } }));
            var legacy = current with { InventoryFile = legacyInventory, InventorySha256 = Wire.FileHash(legacyInventory) };
            DeploymentShortcuts.Create(legacy, menu, desktop);
            var plan = DeploymentShortcuts.Plan(current, menu, desktop);
            var tool = plan.Single(p => p.Arguments == InstallationIdentity.OperationsArgument);
            Check(ReadTarget(tool.Path) == operations, "retained dual-executable inventory uses its own tool");
            var upgraded = Release(Path.Combine(directory, "同版覆盖")) with { Shortcuts = current.Shortcuts, DataRoot = current.DataRoot, PreviousDeployment = legacy };
            DeploymentShortcuts.Create(upgraded, menu, desktop);
            using (var link = new ShellLinkFile(tool.Path))
                Check(link.TargetPath == Path.Combine(upgraded.ReleaseRoot, "desktop", InstallationIdentity.DesktopExecutable) &&
                    link.Arguments == InstallationIdentity.OperationsArgument, "same-version upgrade replaces the owned legacy link target and arguments");
            current = upgraded;
            plan = DeploymentShortcuts.Plan(current, menu, desktop);
            tool = plan.Single(p => p.Arguments == InstallationIdentity.OperationsArgument);
            var target = Release(Path.Combine(directory, "回滚目标")) with { SourceCommit = new string('b', 40), DataRoot = current.DataRoot, Shortcuts = current.Shortcuts };
            var inventory = Path.Combine(directory, "目标库存.json");
            File.WriteAllText(inventory, Wire.Encode(new { Files = new Dictionary<string, string> { ["desktop/" + InstallationIdentity.DesktopExecutable] = Wire.FileHash(Path.Combine(target.ReleaseRoot, "desktop", InstallationIdentity.DesktopExecutable)) } }));
            target = target with { InventoryFile = inventory, InventorySha256 = Wire.FileHash(inventory), PreviousDeployment = current };
            DeploymentShortcuts.Create(target, menu, desktop);
            Check(ReadTarget(plan[0].Path) == Path.Combine(target.ReleaseRoot, "desktop", InstallationIdentity.DesktopExecutable) && !File.Exists(tool.Path),
                "rollback projects only entries declared by the retained inventory and removes the superseded operations link");
            DeploymentShortcuts.Create(current, menu, desktop, target);
            Check(ReadTarget(tool.Path) == tool.Target, "returning to the current dual-program deployment restores its operations entry");
            var before = plan.ToDictionary(p => p.Path, p => Wire.FileHash(p.Path));
            File.AppendAllText(inventory, " ");
            Reject(() => DeploymentShortcuts.Create(target, menu, desktop, current), "changed registered inventory rejects projection before touching current entries");
            Check(before.All(p => Wire.FileHash(p.Key) == p.Value), "inventory failure preserves all current entry bytes");
        });
        Scenario("historical-and-missing-target", directory =>
        {
            var location = Release(directory) with { Shortcuts = new(true, true) };
            var menu = Path.Combine(directory, "菜单"); var desktop = Path.Combine(directory, "桌面");
            DeploymentShortcuts.Create(location, menu, desktop);
            var plan = DeploymentShortcuts.Plan(location, menu, desktop);
            // Freeze the actual pre-LIA naming contract independently of current Plan.
            var legacyId = "sportmaster · aaaaaaaa · 12345678";
            var historical = (from folder in new[] { "菜鸟供包台", "Importer PC" }
                              from item in new[] { (Name: "运行", Args: ""), (Name: "启动修复", Args: "--maintenance"), (Name: "安装维护", Args: "--setup") }
                              select new DeploymentShortcut(Path.Combine(menu, folder, legacyId + " · " + item.Name + ".lnk"), plan[0].Target, item.Args, location.ReleaseRoot)).ToArray();
            foreach (var folder in historical.Select(entry => Path.GetDirectoryName(entry.Path)!).Distinct()) Directory.CreateDirectory(folder);
            var customName = plan[0] with { Path = Path.Combine(menu, "Importer PC", Path.GetFileName(plan[0].Path)) };
            Write(customName); var customHash = Wire.FileHash(customName.Path);
            foreach (var entry in historical) Write(entry);
            var oldDesktop = plan[^1] with { Path = Path.Combine(desktop, "菜鸟供包台 · sportmaster · aaaaaaaa · 12345678.lnk") };
            Write(oldDesktop);
            File.Delete(plan[0].Target);
            DeploymentShortcuts.Remove(location, menu, desktop);
            Check(plan.All(entry => !File.Exists(entry.Path)) && historical.Append(oldDesktop).All(entry => File.Exists(entry.Path)), "uninstall removes current owned links and preserves unregistered historical names when target is missing");
            Check(Wire.FileHash(customName.Path) == customHash, "uninstall preserves an unregistered custom-name placement instead of scanning matching targets");
        });
        Scenario("preserve-conflicts", directory =>
        {
            var location = Release(directory) with { Shortcuts = new(true, true) };
            var menu = Path.Combine(directory, "菜单"); var desktop = Path.Combine(directory, "桌面");
            DeploymentShortcuts.Create(location, menu, desktop);
            var plan = DeploymentShortcuts.Plan(location, menu, desktop);
            Write(plan[^1] with { Target = Path.Combine(directory, "其他𠀀.exe") });
            var hash = Wire.FileHash(plan[^1].Path);
            Reject(() => DeploymentShortcuts.Create(location, menu, desktop), "create refuses user-retargeted stable entry");
            DeploymentShortcuts.Remove(location, menu, desktop);
            Check(Wire.FileHash(plan[^1].Path) == hash, "create and uninstall preserve retargeted link bytes");
            Write(plan[0] with { Arguments = "--maintenance " });
            hash = Wire.FileHash(plan[0].Path);
            Reject(() => DeploymentShortcuts.Create(location, menu, desktop), "arguments comparison preserves trailing whitespace and refuses takeover");
            DeploymentShortcuts.Remove(location, menu, desktop);
            Check(Wire.FileHash(plan[0].Path) == hash, "uninstall preserves same-target different-arguments bytes");
            File.Delete(plan[0].Path); // The fixture owner resolves this conflict before testing a separate corrupt entry.
            File.WriteAllText(plan[1].Path, "not a shell link"); hash = Wire.FileHash(plan[1].Path);
            Reject(() => DeploymentShortcuts.Create(location, menu, desktop), "repair refuses damaged existing file");
            Reject(() => DeploymentShortcuts.Remove(location, menu, desktop), "uninstall reports damaged existing file");
            Check(Wire.FileHash(plan[1].Path) == hash, "damaged link retained byte for byte after repair and uninstall");
        });
        Scenario("length", directory =>
        {
            var old = Release(directory); var menu = Path.Combine(directory, "菜单"); var desktop = Path.Combine(directory, "桌面");
            DeploymentShortcuts.Create(old, menu, desktop);
            var stable = DeploymentShortcuts.Plan(old, menu, desktop).Single().Path;
            var hash = Wire.FileHash(stable);
            var longRoot = Path.Combine(directory, new string('a', 80), new string('b', 80), new string('c', 80), "发布-87654321");
            var next = Release(longRoot) with { SourceCommit = new string('b', 40) };
            var failed = false;
            try { DeploymentShortcuts.Create(next, menu, desktop, old); }
            catch (Exception error) when (error is IOException or COMException or ArgumentException) { failed = true; }
            Console.WriteLine($"Long target UTF16 length={Path.Combine(next.ReleaseRoot, "desktop", "Ipc.Desktop.exe").Length}; rejected={failed}");
            Check(failed ? Wire.FileHash(stable) == hash : ReadTarget(stable) == Path.Combine(next.ReleaseRoot, "desktop", "Ipc.Desktop.exe"),
                "long target either round trips completely or rejects while preserving the original stable entry");
        });
        Scenario("save-failure", directory =>
        {
            var location = Release(directory); var menu = Path.Combine(directory, "菜单"); var desktop = Path.Combine(directory, "桌面");
            DeploymentShortcuts.Create(location, menu, desktop);
            var entry = DeploymentShortcuts.Plan(location, menu, desktop).Single(); var hash = Wire.FileHash(entry.Path);
            using (var locked = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Reject(() => DeploymentShortcuts.Create(location, menu, desktop), "sharing conflict reports failed repair");
            Check(Wire.FileHash(entry.Path) == hash, "failed repair preserves original bytes");
            DeploymentShortcuts.Create(location, menu, desktop);
            using (var locked = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Reject(() => DeploymentShortcuts.Remove(location, menu, desktop), "sharing conflict reports failed uninstall");
            Check(Wire.FileHash(entry.Path) == hash, "failed uninstall retains the original shortcut");
            DeploymentShortcuts.Remove(location, menu, desktop);
            Check(!File.Exists(entry.Path), "repair and removal succeed after sharing owner releases its handle");
        });
        Scenario("untrusted-descriptor", directory =>
        {
            var old = Release(directory); var menu = Path.Combine(directory, "菜单"); var desktop = Path.Combine(directory, "桌面");
            DeploymentShortcuts.Create(old, menu, desktop);
            File.WriteAllText(Path.Combine(old.ReleaseRoot, "deployment-location.json"), Wire.Encode(old));
            var next = Release(Path.Combine(directory, "新版本")) with { SourceCommit = new string('b', 40) };
            var stable = DeploymentShortcuts.Plan(old, menu, desktop).Single().Path; var hash = Wire.FileHash(stable);
            Reject(() => DeploymentShortcuts.Create(next, menu, desktop), "repair cannot adopt user-writable deployment descriptor outside a trusted program root");
            Check(Wire.FileHash(stable) == hash, "untrusted descriptor rejection preserves original stable entry");
        });
        Scenario("partial-projection", directory =>
        {
            var location = Release(directory) with { Shortcuts = new(true, true) };
            var menu = Path.Combine(directory, "菜单"); var desktop = Path.Combine(directory, "桌面");
            var plan = DeploymentShortcuts.Plan(location, menu, desktop);
            Directory.CreateDirectory(Path.GetDirectoryName(plan[2].Path)!);
            File.WriteAllText(plan[2].Path, "user file preserved"); var hash = Wire.FileHash(plan[2].Path);
            Reject(() => DeploymentShortcuts.Create(location, menu, desktop), "projection failure is reported after earlier entries were created");
            Check(plan.Length == 3 && File.Exists(plan[0].Path) && File.Exists(plan[1].Path) && Wire.FileHash(plan[2].Path) == hash,
                "partial projection retains completed entries and original conflicting bytes");
            var retained = plan[2].Path + ".preserved";
            File.Move(plan[2].Path, retained); // Explicit fixture-owner conflict resolution, never product auto-adoption.
            DeploymentShortcuts.Create(location, menu, desktop);
            Check(plan.All(entry => ReadTarget(entry.Path) == entry.Target) && Wire.FileHash(retained) == hash,
                "repair reenters the same projection after explicit conflict resolution without deleting the original");
            DeploymentShortcuts.Remove(location, menu, desktop);
        });
        Scenario("reparse", directory =>
        {
            var location = Release(directory); var menu = Path.Combine(directory, "菜单");
            var outside = Directory.CreateDirectory(Path.Combine(directory, "outside")).FullName;
            var sentinel = Path.Combine(outside, "keep.txt"); File.WriteAllText(sentinel, "keep"); var hash = Wire.FileHash(sentinel);
            var junction = Path.Combine(directory, "junction");
            string? createdLeaf = null; var createdJunction = false;
            try
            {
            JunctionFixture.Create(junction, outside); createdJunction = true;
            Reject(() => DeploymentShortcuts.Create(location, menu, junction), "create refuses a reparse ancestor");
            Reject(() => DeploymentShortcuts.Remove(location, menu, junction), "uninstall refuses a reparse ancestor");
            var desktop = Directory.CreateDirectory(Path.Combine(directory, "桌面")).FullName;
            var leaf = DeploymentShortcuts.Plan(location, menu, desktop).Single().Path; JunctionFixture.Create(leaf, outside); createdLeaf = leaf;
            Reject(() => DeploymentShortcuts.Create(location, menu, desktop), "create refuses a reparse leaf");
            Reject(() => DeploymentShortcuts.Remove(location, menu, desktop), "uninstall refuses a reparse leaf");
            Check(Wire.FileHash(sentinel) == hash && Directory.GetFileSystemEntries(outside).Length == 1, "path guards preserve outside inventory");
            }
            finally
            {
                try { if (createdLeaf is not null) Directory.Delete(createdLeaf); }
                finally { if (createdJunction) Directory.Delete(junction); }
            }
        });
        Scenario("resource-and-apartment", directory =>
        {
            var target = Path.Combine(Release(directory).ReleaseRoot, "desktop", "Ipc.Desktop.exe");
            var path = Path.Combine(directory, "循环𠀀.lnk"); var invalid = Path.Combine(directory, "bad.lnk"); File.WriteAllText(invalid, "invalid");
            void Batch()
            {
                for (var i = 0; i < 100; i++)
                {
                    var badLoad = false;
                    try { using var corrupt = new ShellLinkFile(invalid); } catch (COMException) { badLoad = true; }
                    if (!badLoad) throw new Exception("Invalid link unexpectedly loaded");
                    using (var link = new ShellLinkFile())
                    {
                        link.TargetPath = target;
                        var saveFailed = false;
                        try { link.Save(Path.Combine(directory, "missing", "fail.lnk")); } catch (Exception error) when (error is COMException or DirectoryNotFoundException) { saveFailed = true; }
                        if (!saveFailed) throw new Exception("Missing parent unexpectedly saved");
                        link.Save(path);
                    }
                    using (var read = new ShellLinkFile(path)) if (read.TargetPath != target) throw new Exception("Loop target changed");
                    File.Move(path, path + ".renamed"); File.Delete(path + ".renamed");
                }
            }
            Batch();
            using var process = System.Diagnostics.Process.GetCurrentProcess(); var before = process.HandleCount;
            Batch(); process.Refresh(); var after = process.HandleCount;
            Check(after <= before + 8, $"100 success/load-failure/save-failure loops release files without forced GC; process handles {before}->{after}");
            Exception? failure = null;
            var thread = new Thread(() => { try { Batch(); } catch (Exception error) { failure = error; } }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            if (!thread.Join(TimeSpan.FromSeconds(20))) throw new Exception("STA shortcut loop exceeded fixture deadline");
            if (failure is not null) throw new Exception("STA shortcut operations failed", failure);
            Check(true, "real COM create/load/save/failure/dispose also runs on one isolated STA thread");
        });
        Console.WriteLine($"Shortcut lifecycle: {checks} assertions; {failures.Count} failed scenarios");
        Console.WriteLine("NOT_RUN protected descriptor success/RepairSystem/ResumeRemoval with real machine registration, UAC, installation, devices and field qualification");
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    static DeploymentLocation Release(string root)
    {
        var release = Path.Combine(root, "发布-12345678"); var target = Path.Combine(release, "desktop", InstallationIdentity.DesktopExecutable);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, "synthetic target; never executed");
        return new("sportmaster", new string('a', 40), release, Path.Combine(root, "config"), Path.Combine(root, "data"), "S-1-5-21-1", Shortcuts: new(false, true));
    }
    static void Write(DeploymentShortcut entry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(entry.Path)!);
        using var link = new ShellLinkFile(); link.TargetPath = entry.Target; link.Arguments = entry.Arguments;
        link.WorkingDirectory = entry.WorkingDirectory; link.Save(entry.Path);
    }

    // Independent unmanaged-buffer read, not ShellLinkFile's StringBuilder getter.
    static string ReadTarget(string path)
    {
        var instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new("00021401-0000-0000-C000-000000000046"), true)!)!;
        var buffer = Marshal.AllocHGlobal(65536);
        try
        {
            ((IPersistFile)instance).Load(path, 0);
            var hr = ((INativeLinkRead)instance).GetPath(buffer, 32768, IntPtr.Zero, 4);
            if (hr != 0) throw new COMException("Independent GetPath did not return S_OK", hr);
            return Marshal.PtrToStringUni(buffer)!;
        }
        finally { Marshal.FreeHGlobal(buffer); Marshal.FinalReleaseComObject(instance); }
    }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface INativeLinkRead
    {
        [PreserveSig] int GetPath(IntPtr value, int count, IntPtr findData, uint flags);
    }
}
