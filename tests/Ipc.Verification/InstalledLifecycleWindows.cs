using System.Reflection;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Ipc.Platform;
using Microsoft.Win32;

// Real protected deployment/ARP/Unicode shortcut transactions, with a unique
// synthetic product. No application-ID override, global policy replacement or
// production runtime/SDK is used to make the lifecycle pass.
[SupportedOSPlatform("windows")]
static class InstalledLifecycleWindows
{
    sealed record ChildReceipt(bool AdminEnabled, bool ProgramWriteDenied, bool NoDuplicate, string Source, string Data);
    public static void Child(string[] args)
    {
        var options = args[1..].Chunk(2).ToDictionary(p => p[0], p => p[1]);
        MachineRuntimeSetup.EnsureRuntimeIdentity(_ => throw new Exception("Original installed machine policy must not be replaced"));
        using var identity = WindowsIdentity.GetCurrent();
        var admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        if (admin) throw new Exception("Daily child is still elevated");
        var denied = false;
        try { File.AppendAllText(options["--program"], "UNEXPECTED WRITE"); }
        catch (UnauthorizedAccessException) { denied = true; }
        if (!denied) throw new Exception("Ordinary child changed a protected program file");
        var source = File.ReadAllText(options["--program"]);
        var data = File.ReadAllText(options["--data"]);
        ExtensionStore.AtomicWrite(options["--receipt"], new ChildReceipt(admin, denied,
            !OrdinaryRuntimeProcess.RelaunchCurrentIfElevated([]), source, data));
    }
    public static void Run()
    {
        void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); Console.WriteLine("PASS INSTALLED-LIFECYCLE " + reason); }
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        if (WindowsStoragePaths.ProtectedAnchor is null) throw new InvalidOperationException("Initialize an isolated D-drive storage location on CI; do not change the existing local anchor");
        var machine = WindowsDeploymentSetup.MachineRoot;
        var policyFile = Path.Combine(machine, "runtime-policy.json");
        if (!File.Exists(policyFile))
        {
            if (Directory.Exists(Path.Combine(machine, "deployments")) && Directory.EnumerateFiles(Path.Combine(machine, "deployments"), "*.json").Any())
                throw new InvalidOperationException("Existing deployment without policy retained; verification cannot replace it");
            MachineRuntimeSetup.InstallForAccount(MachineRuntimePolicy.SharedUsersSid);
        }
        var policy = Wire.Decode<MachineRuntimePolicy>(File.ReadAllText(policyFile));
        var sid = new SecurityIdentifier(policy.RuntimeAccountSid);
        var originalPolicy = Wire.FileHash(policyFile);
        var productionFile = WindowsDeploymentSetup.Registration("sportmaster");
        var originalProduction = File.Exists(productionFile) ? Wire.FileHash(productionFile) : null;
        var production = WindowsDeploymentSetup.Current("sportmaster");
        var programRoot = production is null ? TrustedProgramRoots.Default : Directory.GetParent(Directory.GetParent(production.ReleaseRoot)!.FullName)!.FullName;
        var appId = "lifecycle-" + Wire.Id()[..12];
        var privateRoot = Path.Combine(Path.GetTempPath(), appId); Directory.CreateDirectory(privateRoot);
        var dataParent = Path.Combine(machine, "lifecycle-verification"); WindowsStoragePaths.PrepareProtectedDirectory(dataParent, sid);
        var dataRoot = Path.Combine(dataParent, appId);
        var seedRoot = Path.Combine(privateRoot, "seed"); Directory.CreateDirectory(seedRoot);
        var seed = Path.Combine(seedRoot, "business.json"); File.WriteAllText(seed, "{\"SyntheticLifecycle\":true}");
        var menu = Path.Combine(privateRoot, "开始菜单"); var desktop = Path.Combine(privateRoot, "桌面");
        var mediaA = Medium("a", new string('a', 40)); var mediaB = Medium("b", new string('b', 40));
        DeploymentLocation? prior = null;
        void Project(DeploymentLocation location)
        {
            DeploymentShortcuts.Create(location, menu, desktop, prior);
            if (prior is not null && prior.ReleaseRoot != location.ReleaseRoot) DeploymentShortcuts.Remove(prior, menu, desktop);
        }
        WindowsDeploymentRequest Request(DeploymentMedia media) => new(media.Root, appId, media.SourceCommit, sid.Value, dataRoot, seedRoot,
            RunMode.Simulation, null, null, [], ConfigurationHashes: new() { ["business.json"] = Wire.FileHash(seed) }, ProgramRoot: programRoot,
            ExpectedRegistrationHash: File.Exists(WindowsDeploymentSetup.Registration(appId)) ? Wire.FileHash(WindowsDeploymentSetup.Registration(appId)) : null,
            Shortcuts: new(true, true), OperationId: Wire.Id()) { Recovery = new() };
        DeploymentLocation Install(WindowsDeploymentRequest request, Action<DeploymentLocation>? project = null, Action<WindowsInstallationStage>? progress = null)
            => WindowsDeploymentSetup.Install(request, _ => { }, _ => { }, project ?? Project, progress);
        var firstRequest = Request(mediaA); var first = Install(firstRequest); prior = first;
        Check(WindowsDeploymentSetup.InspectInstallation(firstRequest).Status == "Completed" && first.LifecycleState == "Installed", "fresh protected install commits one authoritative deployment and complete receipt");
        var supervisionRoot = DesktopRecoverySupervisor.StateDirectory(first);
        var initialLedger = Wire.FileHash(Path.Combine(supervisionRoot,"state.json"));
        Check(File.ReadAllText(Path.Combine(supervisionRoot,"state.json")).Contains("New"), "trusted first installation publishes complete initial supervision without enabling automatic launch");
        Directory.Move(supervisionRoot,supervisionRoot+".retained");
        var data = Path.Combine(dataRoot, "original-request.json"); File.WriteAllText(data, "SYNTHETIC original request and response custody");
        var dataHash = Wire.FileHash(data); var configHash = Wire.FileHash(Path.Combine(first.ConfigurationDirectory, "business.json"));
        var rootsFile = Path.Combine(machine, "trusted-program-roots.json"); var rootsHash = Wire.FileHash(rootsFile);
        VerifyOrdinary(first, "first");
        var repair = Install(Request(mediaA));
        Check(repair.ReleaseRoot != first.ReleaseRoot && repair.DataRoot == first.DataRoot && repair.ConfigurationDirectory == first.ConfigurationDirectory &&
            Wire.FileHash(data) == dataHash && Wire.FileHash(Path.Combine(repair.ConfigurationDirectory, "business.json")) == configHash && Wire.FileHash(rootsFile) == rootsHash,
            "same-source reinstall keeps account/data/configuration and does not rewrite unchanged trusted roots");
        Check(!Directory.Exists(supervisionRoot),"disabled reinstall does not recreate a missing supervision directory");
        var recoveryRefusalHash=Wire.FileHash(WindowsDeploymentSetup.Registration(appId));
        try { Install(Request(mediaA) with {Recovery=new(Enabled:true)});throw new Exception("Missing previous supervision accepted"); }
        catch(IOException) { Check(Wire.FileHash(WindowsDeploymentSetup.Registration(appId))==recoveryRefusalHash&&!Directory.Exists(supervisionRoot),"enabling an existing installation refuses missing supervision before registration changes"); }
        Directory.Move(supervisionRoot+".retained",supervisionRoot);
        Check(Wire.FileHash(Path.Combine(supervisionRoot,"state.json"))==initialLedger,"original supervision bytes retained across disabled reinstall");
        var migrationTarget=Path.Combine(dataParent,appId+"-restored");WindowsDeploymentSetup.PrepareRestoreDataRoot(migrationTarget);
        using(var migration=WindowsDeploymentSetup.PrepareRecoveryMigration(repair,migrationTarget,Wire.FileHash(WindowsDeploymentSetup.Registration(appId))))
        {
            var refused=false;try{Install(Request(mediaA));}catch(IOException){refused=true;}
            Check(refused && Wire.FileHash(WindowsDeploymentSetup.Registration(appId))==recoveryRefusalHash &&
                Wire.FileHash(Path.Combine(DesktopRecoverySupervisor.StateDirectory(repair with {DataRoot=migrationTarget}),"state.json"))==initialLedger,
                "prepared migration holds actual protected deployment writer lease before any business source fence");
        }
        VerifyShortcuts(repair); VerifyOrdinary(repair, "repair"); prior = repair;
        var before = Wire.FileHash(WindowsDeploymentSetup.Registration(appId));
        var failed = Request(mediaB);
        try { Install(failed, progress: stage => { if (stage == WindowsInstallationStage.CopyingFiles) throw new IOException("synthetic precommit failure"); }); throw new Exception("Expected precommit failure"); }
        catch (IOException) { Check(Wire.FileHash(WindowsDeploymentSetup.Registration(appId)) == before && WindowsDeploymentSetup.InspectInstallation(failed).Status == "Failed" && Wire.FileHash(data) == dataHash,
            "precommit failure preserves the installed deployment, original data and failed operation receipt"); }
        var unknown = Request(mediaB);
        try { Install(unknown, _ => throw new IOException("synthetic committed shortcut interruption")); throw new Exception("Expected projection interruption"); }
        catch (IOException) { Check(WindowsDeploymentSetup.InspectInstallation(unknown).Status == "CommittedNeedsSystemRepair", "postcommit interruption is inspected as committed and requires projection repair rather than repeat install"); }
        var updated = WindowsDeploymentSetup.Current(appId)!;
        Check(updated.SourceCommit == mediaB.SourceCommit && updated.DataRoot == dataRoot && Wire.FileHash(data) == dataHash, "source update commits the new program while retaining original data and account");
        var committedHash = Wire.FileHash(WindowsDeploymentSetup.Registration(appId));
        try { Install(unknown); throw new Exception("Repeated committed operation accepted"); }
        catch (InvalidOperationException) { Check(Wire.FileHash(WindowsDeploymentSetup.Registration(appId)) == committedHash, "stale/repeated committed install cannot create a second deployment"); }
        var failedJournal = WindowsDeploymentSetup.InstallationOperationFile(unknown); var failedHash = Wire.FileHash(failedJournal);
        var foreignRepairCalled = false;
        try { WindowsDeploymentSetup.RepairCommittedInstallation(unknown with { DataRoot = dataRoot + "-other" }, committedHash, (_, _) => foreignRepairCalled = true); throw new Exception("Mismatched repair accepted"); }
        catch (InvalidOperationException) { Check(!foreignRepairCalled && Wire.FileHash(failedJournal) == failedHash && Wire.FileHash(WindowsDeploymentSetup.Registration(appId)) == committedHash, "different original request cannot borrow the committed registration to repair another deployment"); }
        try { WindowsDeploymentSetup.RepairCommittedInstallation(unknown, committedHash, (_, _) => throw new IOException("synthetic repair failure")); throw new Exception("Expected repair failure"); }
        catch (IOException) { Check(Wire.FileHash(failedJournal) == failedHash && WindowsDeploymentSetup.InspectInstallation(unknown).Status == "CommittedNeedsSystemRepair", "failed projection repair preserves the fixed original operation and permits inspection, not a fresh installation"); }
        WindowsDeploymentSetup.RepairCommittedInstallation(unknown, committedHash, (location, previous) =>
        {
            DeploymentShortcuts.Create(location, menu, desktop, previous);
            if (previous is not null) DeploymentShortcuts.Remove(previous, menu, desktop);
        });
        using (var repaired = System.Text.Json.JsonDocument.Parse(File.ReadAllText(failedJournal)))
            Check(WindowsDeploymentSetup.InspectInstallation(unknown).Status == "Completed" &&
                Wire.FileHash(repaired.RootElement.GetProperty("SystemRepairArchive").GetString()!) == failedHash &&
                DeploymentShortcuts.Plan(repair, menu, desktop).Where(p => p.Arguments != "" || Path.GetFileName(p.Path) != "菜鸟供包台.lnk").All(p => !File.Exists(p.Path)),
                "successful repair completes the original receipt, preserves failed bytes and removes the exact previous Start Menu projections");
        var repairedHash = Wire.FileHash(failedJournal);
        WindowsDeploymentSetup.RepairCommittedInstallation(unknown, committedHash, (location, previous) => DeploymentShortcuts.Create(location, menu, desktop, previous));
        Check(Wire.FileHash(failedJournal) == repairedHash && Wire.FileHash(data) == dataHash, "repeated completed projection repair does not rewrite the original receipt or business data");
        VerifyShortcuts(updated); VerifyOrdinary(updated, "update-repaired"); prior = updated;
        var displaced = Request(mediaA) with { DataRoot = Path.Combine(dataParent, appId + "-different") };
        try { Install(displaced); throw new Exception("Unexpected data switch"); }
        catch (InvalidDataException) { Check(Wire.FileHash(WindowsDeploymentSetup.Registration(appId)) == committedHash && Wire.FileHash(data) == dataHash, "normal repair/update cannot silently switch the original data root"); }
        try { WindowsDeploymentSetup.Remove(appId, committedHash, _ => throw new InvalidOperationException("synthetic unresolved responsibility"), _ => { }); throw new Exception("Removal guard ignored"); }
        catch (InvalidOperationException) { Check(Wire.FileHash(WindowsDeploymentSetup.Registration(appId)) == committedHash && File.Exists(Path.Combine(updated.ReleaseRoot, "app", "Ipc.Platform.dll")), "responsibility rejection occurs before detach or program deletion"); }
        var foreign = Path.Combine(updated.ReleaseRoot, "foreign-evidence.txt"); File.WriteAllText(foreign, "retain outside inventory");
        var backups = 0;
        var removed = WindowsDeploymentSetup.Remove(appId, committedHash, _ => { File.Copy(data, Path.Combine(dataRoot, "full-backup.txt")); backups++; }, d => DeploymentShortcuts.Remove(d, menu, desktop));
        Check(backups == 1 && removed.Status == "RemovedWithRetainedFiles" && File.ReadAllText(foreign) == "retain outside inventory" && Wire.FileHash(data) == dataHash &&
            !File.Exists(Path.Combine(desktop, "菜鸟供包台.lnk")), "authorized uninstall preserves data/backup and unlisted evidence while removing only its own shortcuts and inventoried program");
        using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        using (var arp = hklm.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ImporterPc." + appId))
            Check(arp is null, "retired deployment is removed from the real Windows installed-product projection");
        Check(WindowsDeploymentSetup.InspectRemoval(appId).RemainingFiles!.Contains("foreign-evidence.txt"), "post-uninstall inspection observes actual unlisted remnants without deleting them");
        var retiredSource=WindowsDeploymentSetup.Current(appId)!;var retiredHash=Wire.FileHash(WindowsDeploymentSetup.Registration(appId));
        var refusedTarget=Path.Combine(dataParent,appId+"-retired-migration");Directory.CreateDirectory(refusedTarget);
        var originalLedgerHash=Wire.FileHash(Path.Combine(supervisionRoot,"state.json"));
        try{using var rejectedMigration=WindowsDeploymentSetup.PrepareRecoveryMigration(retiredSource,refusedTarget,retiredHash);throw new Exception("Retired deployment entered migration");}
        catch(InvalidOperationException){Check(Wire.FileHash(WindowsDeploymentSetup.Registration(appId))==retiredHash && Wire.FileHash(Path.Combine(supervisionRoot,"state.json"))==originalLedgerHash &&
            !Directory.Exists(DesktopRecoverySupervisor.StateDirectory(retiredSource with {DataRoot=refusedTarget})),"retired registration is refused in preparation before business handoff or target supervision publication");}
        Directory.Move(supervisionRoot,supervisionRoot+".retired-retained");
        var reinstall = Install(Request(mediaB)); prior = reinstall;
        Check(!Directory.Exists(supervisionRoot),"disabled reinstall after real retirement preserves missing supervision instead of fabricating a new budget");
        VerifyShortcuts(reinstall); VerifyOrdinary(reinstall, "reinstall-after-remove");
        Check(reinstall.ConfigurationDirectory == first.ConfigurationDirectory && Wire.FileHash(data) == dataHash && File.Exists(foreign), "uninstall then reinstall retains original seed/data/evidence and starts with ordinary privileges again");
        _ = WindowsDeploymentSetup.Remove(appId, Wire.FileHash(WindowsDeploymentSetup.Registration(appId)), _ => { }, d => DeploymentShortcuts.Remove(d, menu, desktop));
        Check(Wire.FileHash(policyFile) == originalPolicy && (File.Exists(productionFile) ? Wire.FileHash(productionFile) : null) == originalProduction && Wire.FileHash(rootsFile) == rootsHash,
            "complete lifecycle leaves the real sportmaster deployment, machine runtime policy and trusted-root bytes unchanged");
        Console.WriteLine("EVIDENCE " + privateRoot);
        Console.WriteLine("BOUNDARY unique synthetic product; actual protected D-drive files/ARP/COM shortcuts/ordinary Windows processes. Business/SDK/RCS, actual restart queue and target-site acceptance NOT_RUN.");

        DeploymentMedia Medium(string name, string source)
        {
            var root = Path.Combine(privateRoot, "media-" + name);
            foreach (var area in new[] { "app", "desktop" })
            {
                var folder = Path.Combine(root, area); Directory.CreateDirectory(folder);
                var names = area == "app" ? new[] { "Ipc.Assembly.exe", "Ipc.Assembly.dll", "Ipc.Platform.dll", "config/business.json" } : new[] { "Ipc.Desktop.exe", "config/business.json" };
                foreach (var file in names)
                {
                    var destination = Path.Combine(folder, file); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (file == "config/business.json") File.Copy(seed, destination); else File.WriteAllText(destination, "SYNTHETIC NEVER EXECUTED " + source);
                }
                ExtensionStore.AtomicWrite(Path.Combine(folder, "application-release.json"), new ApplicationRelease(2, appId, source, ["synthetic"], ["standard"], names.ToDictionary(n => n, n => Wire.FileHash(Path.Combine(folder, n))), "1.0.0"));
            }
            return DeploymentMedia.Inspect(root, appId);
        }
        void VerifyShortcuts(DeploymentLocation location)
        {
            foreach (var item in DeploymentShortcuts.Plan(location, menu, desktop))
            {
                using var link = new ShellLinkFile(item.Path);
                Check(link.TargetPath == item.Target && link.Arguments == item.Arguments && link.WorkingDirectory == item.WorkingDirectory, "real Unicode shortcut follows the current authoritative release: " + Path.GetFileName(item.Path));
            }
        }
        void VerifyOrdinary(DeploymentLocation location, string stage)
        {
            var receipt = Path.Combine(dataRoot, "ordinary-" + stage + ".json");
            var executable = Environment.ProcessPath!;
            var prefix = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? new[] { Assembly.GetExecutingAssembly().Location } : [];
            using var child = OrdinaryRuntimeProcess.Start(executable, [..prefix, "--lifecycle-ordinary-child", "--program", Path.Combine(location.ReleaseRoot, "app", "Ipc.Platform.dll"), "--data", data, "--receipt", receipt], Environment.CurrentDirectory);
            Check(child.Wait(30000) == 0, "actual daily process passes installed machine identity after " + stage);
            var proof = Wire.Decode<ChildReceipt>(File.ReadAllText(receipt));
            Check(!proof.AdminEnabled && proof.ProgramWriteDenied && proof.NoDuplicate && proof.Source.Contains(location.SourceCommit) && proof.Data == File.ReadAllText(data), "ordinary daily process can read its current program/write data, cannot mutate protected program or relaunch recursively after " + stage);
        }
    }
}
