using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using System.Security.AccessControl;
using System.Security.Principal;

static class BackupSetDeliveryChecks
{
    sealed class InlineProgress(Action<MaintenanceProgress> action) : IProgress<MaintenanceProgress> { public void Report(MaintenanceProgress value) => action(value); }
    public static async Task Run(string workspace, string root)
    {
        Directory.CreateDirectory(root); var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL BACKUP-SET " + message); checks++; Console.WriteLine("PASS BACKUP-SET " + message); }
        async Task Reject<T>(Func<Task> action, string message) where T : Exception
        { try { await action(); } catch (T) { Check(true, message); return; } throw new Exception("Expected " + typeof(T).Name + ": " + message); }
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "station"), deferred: true);
        await h.StartAsync(); await h.App.DisposeAsync();
        await using var service = new ParcelStartupMaintenance(h.Launch, new SportmasterFlow(), () => new ManualSupplyHarness.ManualFactory(h.Launch), () => { }, () => "test-admin")
        { MaintenanceWorkspace = () => Directory.CreateDirectory(Path.Combine(root, "inspection-" + Wire.Id())).FullName, RestoreIntentReader = () => [] };
        var catalog = Directory.CreateDirectory(Path.Combine(root, "catalog")).FullName;
        var backup = Path.Combine(catalog, "完整备份.db"); var targetHeld = false;
        RecoveryFiles.CopyClosedForVerification = (_, target) =>
        {
            if (targetHeld || !target.StartsWith(backup + ".recovery", StringComparison.OrdinalIgnoreCase)) return;
            try { using var competitor = ParcelRecovery.HoldBackupSet(backup); }
            catch (IOException) { targetHeld = true; }
        };
        try { await service.BackupAsync(backup); }
        finally { RecoveryFiles.CopyClosedForVerification = null; }
        Check(targetHeld, "real backup creation holds the same target fence as listing, export, restore and deletion throughout custody copy");
        var originalIdentity = ParcelRecovery.SetIdentity(backup);
        var originalDbHash = Wire.FileHash(backup); var originalManifest = Wire.FileHash(Path.Combine(backup + ".recovery", "COMPLETED.json"));
        Check((await service.ListBackupsAsync(catalog, default)).Single() is { Status: "Verified", Identity: var listed } && listed == originalIdentity, "real offline backup is listed as one verified complete set with its original logical identity");
        var delivery = await service.ExportBackupSetAsync(backup, originalIdentity, Path.Combine(root, "delivered"), null, default);
        Check(delivery.Status == "Exported" && Path.GetFileName(delivery.Path) == Path.GetFileName(backup) && ParcelRecovery.SetIdentity(delivery.Path) == originalIdentity,
            "delivery preserves database filename, completion bytes and full set identity");
        Check(ParcelRecovery.InspectAnalysisBackup(delivery.Path, new SportmasterFlow()).DatabaseSha256 == originalDbHash, "delivered complete set passes production restore custody and database integrity verifier");
        var restore = await service.PlanRestoreAsync(delivery.Path, Path.Combine(root, "recovery-target"));
        Check(restore.BackupSha256 == originalIdentity, "normal restore entry can reselect the delivered set without manually assembling directories");
        Check(Wire.FileHash(backup) == originalDbHash && Wire.FileHash(Path.Combine(backup + ".recovery", "COMPLETED.json")) == originalManifest, "delivery leaves original backup bytes unchanged");
        Check((await service.ExportBackupSetAsync(backup, originalIdentity, Path.Combine(root, "delivered"), null, default)).Status == "Exported", "reselecting completed same-identity target inspects success without overwriting or repeating a copy");
        if (OperatingSystem.IsWindows())
        {
            var readOnlyDirectory = new DirectoryInfo(Path.GetDirectoryName(delivery.Path)!);
            var originalAccess = readOnlyDirectory.GetAccessControl();
            var directoryPaths = Directory.EnumerateDirectories(readOnlyDirectory.FullName, "*", SearchOption.AllDirectories).OrderBy(p => p.Length).ToArray();
            var originalDirectories = new Dictionary<string, DirectorySecurity>();
            foreach (var path in directoryPaths) originalDirectories.Add(path, new DirectoryInfo(path).GetAccessControl());
            var originalFileAccess = new Dictionary<string, FileSecurity>();
            foreach (var path in Directory.EnumerateFiles(readOnlyDirectory.FullName, "*", SearchOption.AllDirectories)) originalFileAccess.Add(path, new FileInfo(path).GetAccessControl());
            var deniedAccess = readOnlyDirectory.GetAccessControl();
            using var identity = WindowsIdentity.GetCurrent();
            deniedAccess.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny));
            var originalFiles = Directory.EnumerateFiles(readOnlyDirectory.FullName, "*", SearchOption.AllDirectories).ToDictionary(p => p, Wire.FileHash);
            try
            {
                readOnlyDirectory.SetAccessControl(deniedAccess);
                await Reject<UnauthorizedAccessException>(() => File.WriteAllTextAsync(Path.Combine(readOnlyDirectory.FullName, "must-not-write.txt"), "denied"),
                    "actual Windows source ACL rejects writes, including source-side lease creation");
                Check((await service.ListBackupsAsync(readOnlyDirectory.FullName, default)).Single().Status == "Verified", "complete backup on actual read-only source remains listable and verifiable");
                var readOnlyRestore = await service.PlanRestoreAsync(delivery.Path, Path.Combine(root, "read-only-source-restored"));
                using (ParcelRecovery.HoldBackupSet(delivery.Path))
                    await Reject<IOException>(() => service.PlanRestoreAsync(delivery.Path, Path.Combine(root, "blocked-restore")), "local shared fence still blocks competing restore of a read-only source");
                ParcelRecovery.Restore(delivery.Path, readOnlyRestore.Destination);
                Check(File.Exists(Path.Combine(readOnlyRestore.Destination, "RESTORED-ISOLATED.json")) &&
                    Wire.FileHash(Path.Combine(readOnlyRestore.Destination, "sportmaster.db")) == originalDbHash,
                    "normal restore copy consumes read-only complete source and publishes a verified isolated target");
                Check(Directory.EnumerateFiles(readOnlyDirectory.FullName, "*", SearchOption.AllDirectories).Count() == originalFiles.Count && originalFiles.All(p => Wire.FileHash(p.Key) == p.Value),
                    "listing, planning and restoring create no source files and leave every original byte unchanged");
            }
            finally
            {
                // A freshly read security object has no modified sections; mark
                // Access explicitly so SetAccessControl actually restores it.
                originalAccess.SetSecurityDescriptorSddlForm(originalAccess.GetSecurityDescriptorSddlForm(AccessControlSections.Access), AccessControlSections.Access);
                readOnlyDirectory.SetAccessControl(originalAccess);
                foreach (var directory in directoryPaths)
                {
                    var access = originalDirectories[directory];
                    access.SetSecurityDescriptorSddlForm(access.GetSecurityDescriptorSddlForm(AccessControlSections.Access), AccessControlSections.Access);
                    new DirectoryInfo(directory).SetAccessControl(access);
                }
                foreach (var file in originalFileAccess)
                {
                    file.Value.SetSecurityDescriptorSddlForm(file.Value.GetSecurityDescriptorSddlForm(AccessControlSections.Access), AccessControlSections.Access);
                    new FileInfo(file.Key).SetAccessControl(file.Value);
                }
            }
        }
        using (var cancelled = new CancellationTokenSource())
        {
            var target = Path.Combine(root, "cancelled-export");
            var progress = new InlineProgress(p => { if (p.Stage == MaintenanceStage.CopyingCustody && p.ProcessedBytes > 0) cancelled.Cancel(); });
            await Reject<MaintenanceCancelledException>(() => service.ExportBackupSetAsync(backup, originalIdentity, target, progress, cancelled.Token), "mid-copy cancellation has no completed delivery result");
            Check(!Directory.Exists(target) && Directory.EnumerateDirectories(root, "cancelled-export.incomplete-*").Any() && ParcelRecovery.SetIdentity(backup) == originalIdentity,
                "cancelled export retains named staging evidence and the intact original, without publishing final folder");
        }
        Check((await service.InspectBackupDeliveryAsync(backup, originalIdentity, Path.Combine(root, "cancelled-export"))).Status == "CopyIncomplete", "read-only original delivery query reports unpublished partial copy without modifying source or target");
        var resumedDelivery = await service.ExportBackupSetAsync(backup, originalIdentity, Path.Combine(root, "cancelled-export"), null, default);
        Check(resumedDelivery.Status == "Exported" && ParcelRecovery.SetIdentity(resumedDelivery.Path) == originalIdentity && !File.Exists(Path.Combine(root, "cancelled-export", ParcelRecovery.DeliveryMarker)),
            "normal export retry verifies and continues original partial folder then publishes the same full identity");
        File.Copy(backup, Path.Combine(catalog, "orphan.db"));
        var orphanRecovery = Path.Combine(catalog, "missing-db.db.recovery"); Directory.CreateDirectory(orphanRecovery);
        Check((await service.ListBackupsAsync(catalog, default)).Any(i => i.Status == "OrphanDatabase") && (await service.ListBackupsAsync(catalog, default)).Any(i => i.Status == "InvalidOrIncomplete"),
            "catalog distinguishes orphan database from incomplete recovery directory");
        var missingFile = Wire.Decode<ParcelRecoveryManifest>(File.ReadAllText(Path.Combine(delivery.Path + ".recovery", "COMPLETED.json"))).Files.Keys.First();
        File.Delete(Path.Combine(delivery.Path + ".recovery", missingFile));
        await Reject<InvalidDataException>(() => service.ExportBackupSetAsync(delivery.Path, originalIdentity, Path.Combine(root, "invalid-delivery"), null, default), "missing custody file refuses export instead of publishing success");
        var incomplete = (await service.ListBackupsAsync(Path.GetDirectoryName(delivery.Path)!, default)).Single(i => i.Path == delivery.Path);
        Check(incomplete is { Status: "InvalidOrIncomplete", CanDelete: true, CanRestore: false }, "incomplete but attributable set has an explicit bounded deletion identity and no restore permission");
        Check((await service.DeleteBackupSetAsync(delivery.Path, incomplete.Identity!, null, default)).Status == "Deleted", "missing-file set deletes only its remaining original declared custody without requiring a fake complete backup");
        var orphan = (await service.ListBackupsAsync(catalog, default)).Single(i => i.Path.EndsWith("orphan.db", StringComparison.Ordinal));
        Check(orphan is { Status: "OrphanDatabase", CanDelete: true } && (await service.DeleteBackupSetAsync(orphan.Path, orphan.Identity!, null, default)).Status == "Deleted",
            "explicit orphan deletion uses its actual current-format application identity and removes only that one database");
        var source = new DeploymentLocation("sportmaster", new string('a', 40), Path.Combine(root, "program"), h.Launch.ConfigurationDirectory, h.Launch.DataRoot, "synthetic");
        var intent = WindowsDeploymentSetup.NewRestoreIntent(source, backup, originalIdentity, Path.Combine(root, "pending-target"), Path.Combine(root, "pending-target", h.Config.Mode.ToString(), h.Config.StationId, "sportmaster"));
        service.RestoreIntentReader = () => [intent];
        service.RestoreEvidence = new(() => Wire.Hash("machine"), _ => source, _ => throw new Exception(), (_, _, _, _) => throw new Exception()) { RequireRegistration = false };
        await Reject<InvalidOperationException>(() => service.DeleteBackupSetAsync(backup, originalIdentity, null, default), "actual pending original restore reference refuses backup deletion before intent or file removal");
        Check(!File.Exists(backup + ".deleting.json") && ParcelRecovery.SetIdentity(backup) == originalIdentity, "reference refusal leaves complete source and original completion marker intact");
        service.RestoreIntentReader = () => [];
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await Reject<MaintenanceCancelledException>(() => service.DeleteBackupSetAsync(backup, originalIdentity, null, cancelled.Token), "pre-delete cancellation preserves all backup bytes");
        }
        var extra = Path.Combine(backup + ".recovery", "unknown-do-not-delete.txt");
        // The first deletion starts from a verified set. Inject an unknown file only
        // after its intent is frozen, at the real irreversible boundary.
        var added = false;
        var partial = await service.DeleteBackupSetAsync(backup, originalIdentity, new InlineProgress(p =>
        {
            if (!added && p.Stage == MaintenanceStage.RemovingBackupSet) { added = true; File.WriteAllText(extra, "unlisted retained original"); }
        }), default);
        Check(partial.Status == "Partial" && File.Exists(extra) && !File.Exists(backup) && !File.Exists(Path.Combine(backup + ".recovery", "COMPLETED.json")),
            "deletion removes only frozen files and invalidates completion while retaining unlisted content");
        Check((await service.ListBackupsAsync(catalog, default)).Single(i => i.Path == backup).Status == "Deleting", "partial deletion reopens from original frozen record after database removal");
        var retried = await service.DeleteBackupSetAsync(backup, originalIdentity, null, default);
        Check(retried.Status == "Partial" && File.ReadAllText(extra) == "unlisted retained original", "repeated original deletion does not expand scope to unknown files");
        File.Delete(extra);
        Check((await service.DeleteBackupSetAsync(backup, originalIdentity, null, default)).Status == "Deleted", "removing external obstruction permits original bounded deletion to finish");
        File.WriteAllText(backup, "later unrelated file at old name");
        Check((await service.DeleteBackupSetAsync(backup, originalIdentity, null, default)).Status == "Deleted" && File.ReadAllText(backup) == "later unrelated file at old name",
            "completed deletion query never reexecutes old frozen intent against later content at same path");
        File.Delete(backup);
        var lockedBackup = Path.Combine(catalog, "locked.db"); await service.BackupAsync(lockedBackup); var lockedIdentity = ParcelRecovery.SetIdentity(lockedBackup);
        var manifest = Wire.Decode<ParcelRecoveryManifest>(File.ReadAllText(Path.Combine(lockedBackup + ".recovery", "COMPLETED.json")));
        var held = Path.Combine(lockedBackup + ".recovery", manifest.Files.Keys.First());
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var locked = await service.DeleteBackupSetAsync(lockedBackup, lockedIdentity, null, default);
            Check(locked.Status == "Partial" && File.Exists(held) && !File.Exists(Path.Combine(lockedBackup + ".recovery", "COMPLETED.json")), "real Windows file sharing refusal retains original remainder and never reports complete deletion");
        }
        Check((await service.DeleteBackupSetAsync(lockedBackup, lockedIdentity, null, default)).Status == "Deleted", "released handle permits same original deletion identity to complete");
        var live = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, "sportmaster", "sportmaster.db");
        await Reject<InvalidDataException>(() => service.DeleteBackupSetAsync(live, originalIdentity, null, default), "backup custody cannot be used to delete live business originals");
        Check(File.Exists(live), "business database survives all backup delivery and deletion actions");
        Console.WriteLine("BACKUP-SET checks=" + checks + "; actual backup, files, SQLite, restore verifier; machine/UAC/SDK/RCS NOT_RUN");
    }
}
