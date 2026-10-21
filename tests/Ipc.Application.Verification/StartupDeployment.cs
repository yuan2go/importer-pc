using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;

static class StartupDeployment
{
    public static async Task Run(string workspace, string root)
    {
        var config = Path.Combine(root, "independent-config"); Directory.CreateDirectory(config);
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        File.WriteAllText(Path.Combine(config, "business.json"), Wire.Encode(seed));
        // A broken optional inventory must not prevent reading/editing built-in settings.
        File.WriteAllText(Path.Combine(config, "extension-store.json"), "{broken optional inventory");
        await using var factory = new ParcelCapabilityFactory(new(config, Path.Combine(root, "independent-data"), null, null));
        var catalog = factory.Catalog();
        Check(catalog.Providers.Any(x => x.Implementation == "reference-integration") && catalog.InventoryDiagnostic is not null,
            "built-in providers survive malformed optional inventory with an explicit diagnostic");
        factory.Validate(factory.Normalize(seed));
        Check(!Directory.Exists(Path.Combine(root, "independent-data")), "catalog and basic validation create no runtime or database");
        try { factory.Validate(seed with { Mode = RunMode.Production }); throw new Exception("Production silently admitted"); }
        catch (InvalidDataException) { Check(true, "Production still refuses absent real capabilities"); }
        await Offline(config, root, seed);
        Media(root);
    }
    static async Task Offline(string config, string root, ParcelConfiguration seed)
    {
        var data = Path.Combine(root, "offline-data"); var launch = new BusinessLaunch(config, data, null, null);
        int constructed = 0;
        IParcelCapabilityFactory Factory() => new ParcelCapabilityFactory(launch) { Boundary = _ => { constructed++; throw new Exception("Offline maintenance constructed a reader"); } };
        var service = new ParcelStartupMaintenance(launch, new SportmasterFlow(), Factory, () => { });
        var read = await service.ReadAsync();
        Check(read.Candidate is not null && !Directory.Exists(data), "offline read finds seed without creating runtime/data");
        var form = new StartupConfigurationDraft(read.Candidate!, read.Catalog);
        Check(Wire.Encode(form.Build()) == Wire.Encode(read.Candidate), "unchanged offline form preserves complete original configuration");
        form.TimeoutMs = "2000"; var candidate = form.Build();
        Check(candidate.SchemaVersion == seed.SchemaVersion && Wire.Encode(candidate.Topology) == Wire.Encode(seed.Topology) && candidate.ApplicationId == seed.ApplicationId && candidate.TimeoutMs == 2000,
            "typed offline editor preserves schema/topology/identity and edits basic settings");
        Check((await service.ValidateAsync(candidate)).Status == "Validated" && !Directory.Exists(data), "offline static validation opens no runtime/database");
        var unauthorized = new ParcelStartupMaintenance(launch, new SportmasterFlow(), Factory, () => throw new UnauthorizedAccessException("test authority"));
        await Reject<UnauthorizedAccessException>(() => unauthorized.StageAsync(read.ConfigurationHash, candidate, "denied"), "authority checked before creating data");
        Check(!Directory.Exists(data), "denied maintenance leaves absent root absent");
        var staged = await service.StageAsync(read.ConfigurationHash, candidate, "repair synthetic timeout");
        var directory = staged.RuntimeDirectory!; var file = Path.Combine(directory, "configuration.json");
        var envelope = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(file));
        Check(Wire.Encode(envelope.Effective) == Wire.Encode(seed) && envelope.Pending?.TimeoutMs == 2000 && envelope.Status == "PendingRestart" && !File.Exists(Path.Combine(directory, seed.ApplicationId + ".db")),
            "first offline save preserves Effective and stages Pending without creating DB");
        var originals = Directory.GetFiles(Path.Combine(directory, "startup-maintenance"), "original-configuration.json", SearchOption.AllDirectories);
        Check(originals.Length == 1 && Wire.FileHash(originals[0]) == Wire.FileHash(Path.Combine(config, "business.json")), "original seed bytes retained before first stage");
        var saved = File.ReadAllBytes(file); var hash = Wire.FileHash(file);
        await Reject<InvalidOperationException>(() => service.StageAsync(read.ConfigurationHash, candidate, "stale"), "stale editor hash rejected");
        Check(Wire.FileHash(file) == hash, "CAS rejection preserves configuration bytes");
        using (var owner = new ResourceLease(directory, ["application-owner"]))
            await Reject<IOException>(() => service.StageAsync(staged.ConfigurationHash, candidate, "live owner"), "live application ownership blocks maintenance");
        await Reject<InvalidDataException>(() => service.StageAsync(staged.ConfigurationHash, candidate with { StationId = "another-station" }, "identity"), "offline editing cannot change station identity");
        using (var repository = new ParcelRepository(directory, seed.ApplicationId)) { }
        var database = Path.Combine(directory, seed.ApplicationId + ".db");
        Dictionary<string, string> DbHashes() => Directory.GetFiles(directory).Where(p => Path.GetFileName(p).StartsWith(seed.ApplicationId + ".db", StringComparison.Ordinal)).ToDictionary(p => p, Wire.FileHash);
        var beforeDb = DbHashes();
        staged = await service.StageAsync(staged.ConfigurationHash, candidate with { TimeoutMs = 2100 }, "correct retry timeout");
        Check(beforeDb.OrderBy(x => x.Key).SequenceEqual(DbHashes().OrderBy(x => x.Key)), "offline stage leaves original DB/WAL bytes and file set unchanged");
        var backup = Path.Combine(root, "offline-complete.db");
        using (var repository = new ParcelRepository(directory, seed.ApplicationId))
            ParcelRecovery.CreateBackup(directory, backup, envelope.Effective, envelope.Pending, new FileInfo(database).Length, repository.Backup, (_, _) => { });
        var sourceDb = Wire.FileHash(database);
        File.WriteAllText(file, "{corrupt original configuration"); var corrupt = Wire.FileHash(file);
        Check((await service.ReadAsync()).Candidate is null && Wire.FileHash(file) == corrupt, "corrupt original is diagnosed and preserved without replacement");
        var targetRoot = Path.Combine(root, "offline-isolated-restored");
        var plan = await service.PlanRestoreAsync(backup, targetRoot);
        Check(!Directory.Exists(targetRoot), "complete backup preview makes no destination");
        using (var owner = new ResourceLease(directory, ["application-owner"]))
            await Reject<IOException>(() => service.RestoreAsync(backup, plan.Destination, plan.BackupSha256), "restore requires original runtime ownership release");
        await service.RestoreAsync(backup, plan.Destination, plan.BackupSha256);
        Check(File.Exists(Path.Combine(plan.Destination, "RESTORED-ISOLATED.json")) && Wire.FileHash(file) == corrupt && Wire.FileHash(database) == sourceDb,
            "complete backup restores isolated custody while preserving corrupt original and source DB");
        await Reject<IOException>(() => service.RestoreAsync(backup, plan.Destination, plan.BackupSha256), "restore cannot overwrite an existing target");
        File.AppendAllText(backup + ".recovery/COMPLETED.json", " ");
        await Reject<InvalidDataException>(() => service.RestoreAsync(backup, Path.Combine(root, "changed-set"), plan.BackupSha256), "restore pins complete backup manifest as well as DB");
        Check(constructed == 0, "offline operations never construct reader/SDK ports");
        File.WriteAllBytes(file, saved);
        var missingPackage = new PackageReference("missing", "1.0.0", new string('A', 64));
        var binding = seed.Wcs! with { Implementation = "extension:missing/missing", Version = "1.0.0", Package = missingPackage, Module = "missing" };
        var missing = seed with { Wcs = binding, CapabilityInstances = seed.CapabilityInstances?.Select(b => b.InstanceId == binding.InstanceId ? binding : b).ToArray(), Extensions = new([missingPackage], []) };
        Check(new SportmasterFlow().Validate(missing).Length == 0, "missing extension fixture retains structurally valid binding identities");
        File.Delete(Path.Combine(config, "extension-store.json"));
        await Reject<IOException>(() => service.ValidateAsync(missing), "missing configured extension still refuses validation");
    }
    static void Media(string root)
    {
        var media = Path.Combine(root, "synthetic-windows-media");
        foreach (var area in new[] { "app", "desktop" })
        {
            var directory = Path.Combine(media, area); Directory.CreateDirectory(Path.Combine(directory, "config"));
            File.WriteAllText(Path.Combine(directory, area == "app" ? "Ipc.Assembly.exe" : "Ipc.Desktop.exe"), "synthetic binary bytes, never executed");
            File.WriteAllText(Path.Combine(directory, "config", "business.json"), "{}");
            var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(directory, p).Replace('\\', '/'), Wire.FileHash);
            ExtensionStore.AtomicWrite(Path.Combine(directory, "application-release.json"), new ApplicationRelease(2, "sportmaster", new string('a', 40), [], [], files));
        }
        File.WriteAllText(Path.Combine(media, "STARTUP-DEPLOYMENT.md"), "Synthetic operator guide");
        var inventory = Path.Combine(media, "SHA256SUMS");
        File.WriteAllLines(inventory, Directory.GetFiles(media, "*", SearchOption.AllDirectories).Select(p => Wire.FileHash(p) + "  " + Path.GetRelativePath(media, p).Replace('\\', '/')));
        var inspected = DeploymentMedia.Inspect(media, "sportmaster");
        var target = Path.Combine(root, "copied-media"); inspected.CopyTo(target);
        Check(DeploymentMedia.Inspect(target, "sportmaster").Files.Count == inspected.Files.Count, "fixed media is copied and reverified before deployment registration");
        Check(File.Exists(Path.Combine(target, "STARTUP-DEPLOYMENT.md")), "installed release retains checksummed operating guides");
        RejectSync<IOException>(() => inspected.CopyTo(target), "side-by-side deployment refuses existing release destination");
        var extra = Path.Combine(media, "desktop", "extra.dll"); File.WriteAllText(extra, "unexpected");
        RejectSync<InvalidDataException>(() => DeploymentMedia.Inspect(media, "sportmaster"), "extra unregistered DLL is rejected"); File.Delete(extra);
        File.WriteAllText(Path.Combine(media, "app", "Ipc.Assembly.exe"), "changed");
        RejectSync<InvalidDataException>(() => DeploymentMedia.Inspect(media, "sportmaster"), "changed application binary is rejected");
        RejectSync<InvalidDataException>(() => inspected.CopyTo(Path.Combine(root, "incomplete-release")), "source mutation during copy refuses publication");
        Check(!File.Exists(Path.Combine(root, "incomplete-release", "deployment-location.json")), "incomplete media has no successful deployment location");
        var linked = Path.Combine(root, "linked-media"); Directory.CreateSymbolicLink(linked, media);
        try { RejectSync<IOException>(() => DeploymentMedia.Inspect(linked, "sportmaster"), "linked media ancestor is refused"); }
        finally { Directory.Delete(linked); }
        using var rsa = System.Security.Cryptography.RSA.Create(3072);
        var trust = Path.Combine(root, "synthetic-trust.json");
        ExtensionStore.AtomicWrite(trust, new ExtensionTrust(1, [new("synthetic-test", rsa.ExportSubjectPublicKeyInfoPem(), true)]));
        _ = WindowsDeploymentSetup.ReadTrust(trust, RunMode.Simulation);
        RejectSync<InvalidDataException>(() => WindowsDeploymentSetup.ReadTrust(trust, RunMode.Production), "installer refuses test publisher in Production");
        File.WriteAllText(trust, "PRIVATE KEY");
        RejectSync<InvalidDataException>(() => WindowsDeploymentSetup.ReadTrust(trust, RunMode.Simulation), "installer refuses private signing material");
    }
    static async Task Reject<T>(Func<Task> action, string text) where T : Exception
    { try { await action(); throw new Exception("Expected rejection: " + text); } catch (T) { Check(true, text); } }
    static void RejectSync<T>(Action action, string text) where T : Exception
    { try { action(); throw new Exception("Expected rejection: " + text); } catch (T) { Check(true, text); } }
    static void Check(bool value, string text) { if (!value) throw new Exception("FAIL " + text); Console.WriteLine("PASS " + text); }
}
