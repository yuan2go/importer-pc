using Ipc.Platform;

static class IndustrialLifecycle
{
    public static void Run(string root)
    {
        var count = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("FAIL IE01-LIFECYCLE " + message);
            count++; Console.WriteLine("PASS IE01-LIFECYCLE " + message);
        }
        void Reject<T>(Action action, string message) where T : Exception
        {
            try { action(); } catch (T) { Check(true, message); return; }
            throw new Exception("Expected " + typeof(T).Name + ": " + message);
        }
        Check(ReleaseVersionPolicy.Compare("1.10.0", "1.9.9") > 0, "version numbers compared numerically");
        Check(ReleaseVersionPolicy.Compare("1.0.0-rc.10", "1.0.0-rc.2") > 0, "numeric prerelease ordering");
        Check(ReleaseVersionPolicy.Compare("1.0.0", "1.0.0-rc.2") > 0, "stable follows prerelease");
        Check(ReleaseVersionPolicy.Compare("1.0.0+build1", "1.0.0+build2") == 0, "metadata does not change precedence");
        Reject<InvalidDataException>(() => ReleaseVersionPolicy.Compare("01.0.0", "1.0.0"), "leading-zero version rejected");
        Reject<InvalidDataException>(() => ReleaseVersionPolicy.Compare("1.0.0-01", "1.0.0"), "leading-zero prerelease rejected");
        var current = new ApplicationRelease(2, "sportmaster", new string('a', 40), [], [], [], "1.2.0");
        Reject<InvalidDataException>(() => ReleaseVersionPolicy.RequireForward(current with { BusinessVersion = "1.1.0" }, current), "downgrade rejected");
        Reject<InvalidDataException>(() => ReleaseVersionPolicy.RequireForward(current with { SourceCommit = new string('b', 40) }, current), "same-version different bytes rejected");
        ReleaseVersionPolicy.RequireForward(current, current); Check(true, "identical version/source supports repair");
        var parsed = InstallerCommandLine.Parse(["/quiet", "/norestart", @"/dataRoot=D:\station data", @"/log=D:\logs\result.json"]);
        Check(parsed.Action == "Install" && parsed.DataRoot == @"D:\station data" && Guid.TryParseExact(parsed.OperationId, "N", out _), "spaces and operation identity preserved");
        Check(InstallerCommandLine.Parse(["/QUIET", "/repair"]).Action == "Repair", "case insensitive switches");
        Check(InstallerCommandLine.Parse(["/quiet", "/uninstall"]).Action == "Uninstall", "uninstall distinct from install");
        Reject<ArgumentException>(() => InstallerCommandLine.Parse(["/quiet", "/repair", "/uninstall"]), "conflicting actions rejected");
        Reject<ArgumentException>(() => InstallerCommandLine.Parse(["/quiet", "/quiet"]), "duplicate switches rejected");
        Reject<ArgumentException>(() => InstallerCommandLine.Parse(["/quiet", "/password=secret"]), "no password command-line switch");
        Reject<ArgumentException>(() => InstallerCommandLine.Parse(["/quiet", "/dataRoot=relative"]), "relative root rejected");
        Reject<ArgumentException>(() => InstallerCommandLine.Parse(["/quiet", "/operationId=invalid"]), "invalid operation identity rejected");
        Check(InstallerCommandLine.IsQuiet(["/QUIET=false"]), "malformed quiet value still routes to noninteractive validation");
        Reject<ArgumentException>(() => InstallerCommandLine.Parse(["/quiet=false"]), "quiet flag with value fails without opening UI");
        Reject<ArgumentException>(() => InstallerCommandLine.Parse(["/quiet", "/operationId=" + Guid.Empty.ToString("N")]), "empty operation identity rejected before deployment");
        var canonical = Guid.NewGuid().ToString("N");
        Check(InstallerCommandLine.Parse(["/quiet", "/operationId=" + canonical.ToUpperInvariant()]).OperationId == canonical,
            "operation identity canonicalized for protected journal lookup");
        var files = Path.Combine(root, "inventory-cleanup"); Directory.CreateDirectory(files);
        File.WriteAllText(Path.Combine(files, "owned.txt"), "owned");
        File.WriteAllText(Path.Combine(files, "extra.txt"), "retain");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.CleanRemovalInventory(files, ["owned.txt", "../outside"], _ => false), "full inventory validated first");
        Check(File.Exists(Path.Combine(files, "owned.txt")), "invalid later entry cannot delete earlier file");
        var cleanup = WindowsDeploymentSetup.CleanRemovalInventory(files, ["owned.txt"], _ => false);
        Check(!cleanup.RestartRequired && !File.Exists(Path.Combine(files, "owned.txt")) && File.Exists(Path.Combine(files, "extra.txt")), "unowned files retained, no reboot");
        File.WriteAllText(Path.Combine(files, "locked.txt"), "locked");
        using (var held = new FileStream(Path.Combine(files, "locked.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var blocked = WindowsDeploymentSetup.CleanRemovalInventory(files, ["locked.txt"], _ => false);
            Check(!blocked.RestartRequired && blocked.Remaining.Length > 0, "unscheduled lock is leftovers, not false reboot");
        }
        var installed = new DeploymentLocation("sportmaster", new string('a', 40), files, Path.Combine(root, "config"), Path.Combine(root, "data"), "synthetic");
        var originalFiles = new Dictionary<string, string> { ["damaged.exe"] = new string('b', 64) };
        string Inventory(DeploymentLocation identity, Dictionary<string, string> names) => Wire.Encode(new
        {
            identity.ApplicationId,
            identity.SourceCommit,
            identity.ReleaseRoot,
            Files = names
        });
        var recorded = WindowsDeploymentSetup.ValidateInstalledInventory(installed, Inventory(installed, originalFiles));
        Check(recorded.SequenceEqual(new[] { "damaged.exe" }), "retained installation inventory supplies exact program-only removal scope");
        var absent = installed with { ReleaseRoot = Path.Combine(root, "already-removed-release") };
        Check(WindowsDeploymentSetup.ValidateInstalledInventory(absent, Inventory(absent, originalFiles)).SequenceEqual(recorded),
            "protected inventory remains readable after the complete release directory was removed");
        Check(WindowsDeploymentSetup.CleanRemovalInventory(absent.ReleaseRoot, recorded, _ => false).Remaining.Length == 0,
            "repeated cleanup of an already absent release completes without restoring its directory");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ValidateInstalledInventory(installed,
            Inventory(installed with { SourceCommit = new string('c', 40) }, originalFiles)), "inventory from different release rejected");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ValidateInstalledInventory(installed,
            Inventory(installed, new() { ["../outside.exe"] = new string('b', 64) })), "inventory traversal rejected before cleanup");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ValidateInstalledInventory(installed,
            Inventory(installed, new() { ["damaged.exe"] = "invalid" })), "inventory with invalid byte identity rejected");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ValidateInstalledInventory(installed,
            Inventory(installed, new() { ["damaged.exe"] = new string('b', 64), ["DAMAGED.exe"] = new string('b', 64) })), "case-aliased inventory entries rejected");
        File.WriteAllText(Path.Combine(files, "damaged.exe"), "corrupted installed program bytes");
        var removedDamaged = WindowsDeploymentSetup.CleanRemovalInventory(files, recorded, _ => false);
        Check(!File.Exists(Path.Combine(files, "damaged.exe")) && !removedDamaged.RestartRequired && File.Exists(Path.Combine(files, "extra.txt")),
            "damaged owned program can be removed while uninventoried files remain");
        var operation = Wire.Id(); var journal = Path.Combine(root, "sportmaster.remove-" + operation + ".json");
        var originalHash = Wire.Hash(Wire.Encode(installed));
        var prepared = new WindowsDeploymentSetup.RemovalState("Prepared", installed, ["owned.txt"])
        { OperationId = operation, ExpectedRegistrationHash = originalHash, WithoutBusinessInspection = true };
        File.WriteAllText(journal, Wire.Encode(prepared)); var originalBytes = File.ReadAllBytes(journal);
        var inspected = WindowsDeploymentSetup.ProjectRemoval(prepared, journal, installed, originalHash);
        Check(inspected.Status == "NotCommittedNeedsClosure" && inspected.OperationId == operation && inspected.WithoutBusinessInspection,
            "unchanged exact registration proves precommit interruption but requires explicit closure");
        Check(File.ReadAllBytes(journal).SequenceEqual(originalBytes), "read-only inspection cannot finalize or rewrite original intent");
        Check(WindowsDeploymentSetup.ProjectRemoval(prepared, journal, installed, new string('f', 64)).Status == "Unknown", "different registration bytes remain Unknown");
        Check(WindowsDeploymentSetup.ProjectRemoval(prepared, journal, installed with { DataRoot = Path.Combine(root, "other-data") }, originalHash).Status == "Unknown", "changed data root cannot be explained by matching hash alone");
        Check(WindowsDeploymentSetup.ProjectRemoval(prepared, journal, installed with { SourceCommit = new string('c', 40) }, originalHash).Status == "Unknown", "different release is not a precommit proof");
        var retired = installed with { LifecycleState = "RemovedDataRetained", MaintenanceOperation = journal };
        Check(WindowsDeploymentSetup.ProjectRemoval(prepared, journal, retired, "retired-hash").Status == "RetiredCleanupPending", "registration detach with lost final receipt continues original cleanup");
        Check(WindowsDeploymentSetup.ProjectRemoval(prepared, journal, retired with { ConfigurationDirectory = Path.Combine(root, "other-config") }, "hash").Status == "Unknown", "changed detached configuration does not authorize cleanup");
        var completed = prepared with { Status = "RemovedDataRetained" };
        Check(WindowsDeploymentSetup.ProjectRemoval(completed, journal, retired, "hash").Status == "RemovedDataRetained", "completed original matches exact retired registration");
        Check(WindowsDeploymentSetup.ProjectRemoval(completed, journal, installed, originalHash).Status == "Unknown", "completed receipt cannot claim current installed registration was removed");
        var legacy = prepared with { OperationId = null, ExpectedRegistrationHash = null };
        Check(WindowsDeploymentSetup.ProjectRemoval(legacy, journal, installed, originalHash).Status == "Unknown", "legacy intent without exact precondition cannot be declared uncommitted");
        Check(WindowsDeploymentSetup.ProjectRemoval(legacy, journal, retired, "hash").Status == "RetiredCleanupPending", "legacy exact detached registration remains resumable");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ValidateRemovalJournal("sportmaster", journal, prepared with { OperationId = Wire.Id() }), "journal filename binds original operation");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ValidateRemovalJournal("sportmaster", journal, prepared with { ExpectedRegistrationHash = "bad" }), "invalid registration digest rejected");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ValidateRemovalJournal("sportmaster", journal, prepared with { Status = "InventedSuccess" }), "unknown journal terminal cannot permit a second removal");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ValidateRemovalJournal("sportmaster", journal, prepared with { Files = ["../data.db"] }), "journal cannot expand removal into business data");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.RemovalOperationFile("sportmaster", Guid.Empty.ToString("N")), "empty removal operation identity rejected");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.RemovalOperationFile("../sportmaster", operation), "application traversal rejected before journal access");
        var marker = Path.Combine(files, "deployment-retired.json"); File.WriteAllText(marker, "synthetic original marker");
        var closeCalls = 0;
        Reject<InvalidOperationException>(() => WindowsDeploymentSetup.CloseRemovalBeforeCommit(prepared, journal, retired, "hash", () => closeCalls++), "committed original cannot be closed as not committed");
        Check(closeCalls == 0 && File.Exists(marker), "Unknown or committed closure refuses before touching marker");
        Reject<IOException>(() => WindowsDeploymentSetup.CloseRemovalBeforeCommit(prepared, journal, installed, originalHash, () => throw new IOException("synthetic marker failure")), "marker failure cannot finalize intent");
        Check(prepared.Status == "Prepared" && File.ReadAllBytes(journal).SequenceEqual(originalBytes), "failed closure preserves exact original journal bytes");
        var closed = WindowsDeploymentSetup.CloseRemovalBeforeCommit(prepared, journal, installed, originalHash, () => { File.Delete(marker); closeCalls++; });
        Check(closed.Status == "NotCommitted" && prepared.Status == "Prepared" && !File.Exists(marker) && closeCalls == 1, "explicit precommit closure only resolves original marker and intent");
        Check(File.ReadAllBytes(journal).SequenceEqual(originalBytes) && File.Exists(Path.Combine(files, "extra.txt")), "projection returns closed state separately and preserves original and unowned files");
        Check(WindowsDeploymentSetup.ProjectRemoval(closed, journal, installed, originalHash).Status == "NotCommitted", "closed intent is a known non-removal result");
        Console.WriteLine("IE01-LIFECYCLE checks=" + count + "; file/command/version boundaries; UAC/SCM/VM NOT_RUN");
    }
}
