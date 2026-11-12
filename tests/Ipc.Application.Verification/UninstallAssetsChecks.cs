using Ipc.Platform;

static class UninstallAssetsChecks
{
    public static Task Run(string root)
    {
        var count = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("FAIL UNINSTALL-ASSETS " + why); count++; Console.WriteLine("PASS UNINSTALL-ASSETS " + why); }
        void Reject<T>(Action action, string why) where T : Exception
        { try { action(); } catch (T) { Check(true, why); return; } throw new Exception("Expected " + typeof(T).Name + ": " + why); }
        Fixture Make(string name) => new(Path.Combine(root, name));
        UninstallAssetsRecord Plan(Fixture f, DeploymentLocation[]? others = null) => WindowsDeploymentSetup.PlanUninstallAssets(f.Current, Wire.Id(), f.Machine, f.CacheParent,
            f.Inputs, others ?? [], (hash, json) => File.WriteAllText(Path.Combine(f.Machine, hash + ".inventory.json"), json));
        UninstallAssetsReceipt Apply(Fixture f, UninstallAssetsRecord record, Action<UninstallAssetsRecord> save,
            Func<string, bool>? executing = null, Action<string>? queue = null, MaintenanceExecution? execution = null)
            => WindowsDeploymentSetup.ApplyUninstallAssets(record, "synthetic-receipt", f.Current, save, executing ?? (_ => false),
                queue ?? (_ => throw new Exception("unexpected reboot scheduling")), execution);

        var complete = Make("all"); var originalData = Wire.FileHash(complete.DataFile); var plan = Plan(complete);
        Check(plan.Plan.Delete.Length == 3 && plan.Plan.Caches.Length == 2, "current/orphan caches and a historical release are selected from actual inventories after a previous purge");
        UninstallAssetsRecord saved = plan;
        var result = Apply(complete, plan, s => saved = s);
        Check(result.Status == "Completed" && plan.Plan.Delete.All(a => !Directory.Exists(a.Root)), "real reviewed deletion removes all owned program/cache roots");
        Check(Directory.EnumerateFiles(complete.Machine, "*.inventory.json").Count() == 2 && File.Exists(complete.SharedFile), "small original inventories and unrelated shared-runtime canaries remain");
        Check(Wire.FileHash(complete.DataFile) == originalData, "asset cleanup never removes retained business data or changes its bytes");
        Check(Apply(complete, saved, s => saved = s).Status == "Completed", "replay uses the same fixed inventory after its source caches disappeared");

        var mutation = Make("changed"); var frozen = Plan(mutation); var file = Path.Combine(mutation.OldProgram, "owned.dll");
        File.WriteAllText(file, "replacement after admission");
        Reject<InvalidDataException>(() => Apply(mutation, frozen, _ => throw new Exception("must not save")), "changed reviewed bytes reject before any deletion");
        Check(Directory.Exists(mutation.CurrentCache) && File.Exists(mutation.DataFile), "refusal leaves all other assets and legitimate retained data usable");
        File.WriteAllText(file, "synthetic program");
        Check(Apply(mutation, frozen, _ => { }).Status == "Completed", "restoring the exact admitted file permits the original cleanup");

        var admissionLock = Make("admission-lock");
        using (var locked = new FileStream(Path.Combine(admissionLock.OldProgram, "owned.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
            Reject<IOException>(() => Plan(admissionLock), "an unreadable file aborts admission instead of permanently excluding a recoverable owned version");
        Check(Directory.Exists(admissionLock.CurrentCache) && Apply(admissionLock, Plan(admissionLock), _ => { }).Status == "Completed", "unlocking before admission allows a fresh verified plan without any earlier target deletion");

        var extra = Make("extra"); File.WriteAllText(Path.Combine(extra.OldProgram, "unowned.txt"), "unowned canary"); var extraPlan = Plan(extra);
        var extraResult = Apply(extra, extraPlan, _ => { });
        Check(extraResult.Status == "Partial" && extraResult.Retained.Any(x => x.Root.EndsWith("unowned.txt", StringComparison.Ordinal)) &&
            File.ReadAllText(Path.Combine(extra.OldProgram, "unowned.txt")) == "unowned canary", "unlisted files remain explicit and cannot be reported as fully removed");
        var many = Make("bounded-preview");
        for (var i = 0; i < 20; i++) File.WriteAllText(Path.Combine(many.OldProgram, "extra-" + i + ".txt"), "unowned");
        var preview = Apply(many, Plan(many), _ => { });
        Check(preview.Status == "Partial" && preview.Retained.Length == 16 && preview.TotalRetained == 20 && preview.DetailsTruncated,
            "bounded pipe previews disclose their full retained count and never erase the authoritative inventory");

        var damaged = Make("damaged"); File.WriteAllText(Path.Combine(damaged.OldProgram, "owned.dll"), "damaged before admission");
        Check(Apply(damaged, Plan(damaged), _ => { }).Status == "Completed", "a damaged owned binary can be removed after its actual bytes are frozen");
        var invalid = Make("invalid"); invalid.Inputs[0] = invalid.Inputs[0] with { Json = "{}" };
        var invalidPlan = Plan(invalid);
        Check(invalidPlan.Plan.Retained.Any(x => x.Root == invalid.CurrentCache) && Directory.Exists(invalid.CurrentCache), "missing ownership evidence retains the cache instead of inventing a deletion scope");
        var shared = Make("shared"); var foreign = shared.Current with { ApplicationId = "direct-plc", MaintenanceRoot = shared.CurrentCache };
        var sharedPlan = Plan(shared, [foreign]);
        Check(!sharedPlan.Plan.Delete.Any(x => x.Root == shared.CurrentCache) && sharedPlan.Plan.Retained.Length != 0, "another installation's referenced cache is excluded");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.RequireUninstallAssetRoot(shared.Machine, shared.Current, shared.Machine, []), "machine evidence root cannot become a deletion area");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.RequireUninstallAssetRoot(Path.GetDirectoryName(shared.DataFile)!, shared.Current, shared.Machine, []), "business data cannot become an asset area");
        Reject<InvalidDataException>(() => Apply(shared, sharedPlan with { Plan = sharedPlan.Plan with { Scope = "replacement" } }, _ => { }), "a replaced registration or altered plan cannot inherit an old deletion");

        var partial = Make("locked"); var partialPlan = Plan(partial); FileStream? held = null; UninstallAssetsRecord partialSaved = partialPlan;
        try
        {
            var partialResult = Apply(partial, partialPlan, s =>
            {
                partialSaved = s;
                if (s.Status == "Cleaning") held = new FileStream(Path.Combine(partial.OldProgram, "owned.dll"), FileMode.Open, FileAccess.Read, FileShare.None);
            });
            Check(partialResult.Status == "Partial" && File.Exists(Path.Combine(partial.OldProgram, "owned.dll")), "a real late file lock retains the original area and truthful partial receipt");
        }
        finally { held?.Dispose(); }
        Check(Apply(partial, partialSaved, _ => { }).Status == "Completed", "releasing that lock allows the same plan to finish without restoring deleted caches");

        var lost = Make("lost"); var lostPlan = Plan(lost); UninstallAssetsRecord durable = lostPlan;
        Reject<InstallationOutcomeUnknownException>(() => Apply(lost, lostPlan, s => { if (s.Status == "Completed") throw new IOException("synthetic final receipt loss"); durable = s; }), "final receipt loss remains unknown even when files were removed");
        Check(durable.Status == "Cleaning" && Apply(lost, durable, _ => { }).Status == "Completed", "retry reconciles the original intent and absent roots after receipt loss");

        var cancelled = Make("cancelled"); var cancelPlan = Plan(cancelled); UninstallAssetsRecord cancelSaved = cancelPlan;
        using (var execution = new MaintenanceExecution())
        {
            Reject<OperationCanceledException>(() => Apply(cancelled, cancelPlan, s => { cancelSaved = s; if (s.Status == "Prepared") execution.TryCancel(); }, execution: execution), "cancellation before first deletion remains supported");
            Check(cancelSaved.Status == "NotStarted" && Directory.Exists(cancelled.OldProgram), "cancelled cleanup retains its evidence and every original file");
        }
        Check(Apply(cancelled, cancelSaved, _ => { }).Status == "Completed", "a later explicit action can complete the cancelled original plan");

        var live = Make("live-cache"); var livePlan = Plan(live); var queued = new List<string>(); UninstallAssetsRecord liveSaved = livePlan;
        var pending = Apply(live, livePlan, s => liveSaved = s, p => p == live.CurrentCache, queued.Add);
        Check(pending.Status == "RestartRequired" && Directory.Exists(live.CurrentCache) && !Directory.Exists(live.OldProgram), "executing cache stays intact while unrelated historical assets are immediately cleaned");
        Check(queued.Count > 0 && queued.All(p => TrustedProgramRoots.Contains(live.CurrentCache, p)) && File.Exists(live.DataFile), "only frozen self-cache paths enter the controlled OS boundary");
        var queuedCount = queued.Count;
        Check(Apply(live, liveSaved, _ => { }, p => p == live.CurrentCache, queued.Add).Status == "RestartRequired" && queued.Count == queuedCount,
            "repeated invocation does not duplicate a confirmed restart queue");
        Check(Apply(live, liveSaved, _ => { }).Status == "Completed", "an independent media host can finish the same pending cache without reboot or expanded scope");
        Console.WriteLine("UNINSTALL-ASSETS checks=" + count + "; real files/locks and production planner/deleter; OS reboot queue/registry/UAC NOT_RUN");
        return Task.CompletedTask;
    }

    sealed class Fixture
    {
        public string Machine { get; } public string CacheParent { get; } public string CurrentCache { get; }
        public string OldProgram { get; } public string DataFile { get; } public string SharedFile { get; }
        public DeploymentLocation Current { get; } public UninstallAssetInventory[] Inputs { get; }
        public Fixture(string root)
        {
            Machine = Path.Combine(root, "machine"); Directory.CreateDirectory(Machine);
            CacheParent = Path.Combine(root, "maintenance", "sportmaster");
            CurrentCache = Path.Combine(CacheParent, Guid.NewGuid().ToString("N"));
            var oldCache = Path.Combine(CacheParent, Guid.NewGuid().ToString("N"));
            var currentProgram = Path.Combine(root, "releases", "sportmaster-" + new string('a', 12) + "-" + Guid.NewGuid().ToString("N"));
            OldProgram = Path.Combine(root, "releases", "sportmaster-" + new string('b', 12) + "-" + Guid.NewGuid().ToString("N"));
            var data = Path.Combine(root, "data"); Directory.CreateDirectory(data); DataFile = Path.Combine(data, "owned.db"); File.WriteAllText(DataFile, "retained business canary");
            SharedFile = Path.Combine(root, "shared-vc-runtime.dll"); File.WriteAllText(SharedFile, "shared runtime canary");
            Current = new("sportmaster", new string('a', 40), currentProgram, Path.Combine(root, "config"), data, "synthetic", "Purged");
            UninstallAssetInventory Cache(string cache, string source, string program)
            {
                Directory.CreateDirectory(cache); File.WriteAllText(Path.Combine(cache, "owned.dll"), "synthetic program");
                var json = Wire.Encode(new { ApplicationId = "sportmaster", SourceCommit = source, ReleaseRoot = program,
                    Files = new Dictionary<string, string> { ["owned.dll"] = Wire.FileHash(Path.Combine(cache, "owned.dll")) } });
                File.WriteAllText(Path.Combine(cache, "installed-inventory.json"), json);
                return new(cache, json);
            }
            var first = Cache(CurrentCache, Current.SourceCommit, currentProgram);
            Current = Current with { MaintenanceRoot = CurrentCache, InventoryFile = Path.Combine(CurrentCache, "installed-inventory.json"), InventorySha256 = Wire.Hash(first.Json) };
            var second = Cache(oldCache, new string('b', 40), OldProgram); Directory.CreateDirectory(OldProgram);
            File.WriteAllText(Path.Combine(OldProgram, "owned.dll"), "synthetic program");
            File.WriteAllText(Path.Combine(OldProgram, "deployment-location.json"), Wire.Encode(Current with
            { SourceCommit = new string('b', 40), ReleaseRoot = OldProgram, MaintenanceRoot = oldCache, InventoryFile = Path.Combine(oldCache, "installed-inventory.json"), InventorySha256 = Wire.Hash(second.Json) }));
            Inputs = [first, second];
        }
    }
}
