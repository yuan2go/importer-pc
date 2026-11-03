using Ipc.Platform;

static class IndustrialOpaqueRemoval
{
    public static async Task Run(string workspace, string root)
    {
        var checks = 0;
        void Check(bool value, string detail) { if (!value) throw new Exception("FAIL IE01-OPAQUE " + detail); checks++; Console.WriteLine("PASS IE01-OPAQUE " + detail); }
        void Reject<T>(Action action, string detail) where T : Exception
        { try { action(); } catch (T) { Check(true, detail); return; } throw new Exception("Expected " + typeof(T).Name + ": " + detail); }
        var data = Path.Combine(root, "opaque-original-data"); var leaseRoot = Path.Combine(root, "synthetic-machine-leases");
        var old = Path.Combine(data, "Production", "old-station", "sportmaster");
        var simulation = Path.Combine(data, "Simulation", "other-station", "sportmaster");
        Directory.CreateDirectory(old); Directory.CreateDirectory(simulation);
        var originals = new[] { Path.Combine(old, "configuration.json"), Path.Combine(old, "sportmaster.db"), Path.Combine(simulation, "sportmaster.db") };
        foreach (var file in originals) File.WriteAllText(file, "synthetic unreadable original: { not JSON or SQLite - retain exact bytes " + Path.GetFileName(file));
        var hashes = originals.ToDictionary(p => p, Wire.FileHash);
        Check(ApplicationRootOwnership.Resource("sportmaster", data) == ApplicationRootOwnership.Resource("sportmaster", data + Path.DirectorySeparatorChar), "root key canonicalizes a trailing directory separator");
        Check(ApplicationRootOwnership.Resource("sportmaster", data) != ApplicationRootOwnership.Resource("sportmaster", data + "-other"), "different data roots have different fences");
        Reject<InvalidDataException>(() => ApplicationRootOwnership.Resource("../outside", data), "invalid application identity rejected before file access");
        Reject<InvalidDataException>(() => ApplicationRootOwnership.Resource("sportmaster", "relative-root"), "relative data root rejected");
        Reject<InvalidDataException>(() => ApplicationRootOwnership.Resource("sportmaster", Path.GetPathRoot(data)!), "volume root cannot become a data ownership scope");
        if (OperatingSystem.IsWindows())
        {
            Reject<InvalidDataException>(() => ApplicationRootOwnership.Resource("sportmaster", @"C:\synthetic-not-created"), "Windows data scope cannot silently fall back to C drive");
            Reject<InvalidDataException>(() => { using var denied = ApplicationRootOwnership.AcquireAt(@"C:\synthetic-not-created", "sportmaster", data); }, "Windows global lease cannot be created on C drive");
        }
        using (var scope = new OpaqueRemovalOwnership(leaseRoot, "sportmaster", data, default))
        {
            Check(scope.Directories.Length == 2, "both existing station/mode roots discovered without decoding corrupt originals");
            Reject<IOException>(() => { using var competitor = ApplicationRootOwnership.AcquireAt(leaseRoot, "sportmaster", data); }, "global fence prevents a new runtime startup during opaque maintenance");
            Reject<IOException>(() => { using var competitor = ApplicationRootOwnership.AcquireAt(leaseRoot, "sportmaster", data + "-different-root"); }, "same application cannot evade maintenance by starting with another data root");
            foreach (var area in scope.Directories)
                Reject<IOException>(() => { using var competitor = new ResourceLease(area, ["application-owner"]); }, "existing station owner remains held through opaque maintenance");
            Check(hashes.All(p => Wire.FileHash(p.Key) == p.Value), "unreadable seed/DB bytes are never parsed or changed by ownership inspection");
        }
        using (ApplicationRootOwnership.AcquireAt(leaseRoot, "sportmaster", data))
            Reject<IOException>(() => { using var scope = new OpaqueRemovalOwnership(leaseRoot, "sportmaster", data, default); }, "existing root owner prevents opaque removal inspection");
        using (new ResourceLease(simulation, ["application-owner"]))
            Reject<IOException>(() => { using var scope = new OpaqueRemovalOwnership(leaseRoot, "sportmaster", data, default); }, "existing per-station legacy owner cannot be bypassed");
        using (ApplicationRootOwnership.AcquireAt(leaseRoot, "sportmaster", data))
            Check(true, "partial discovery failure releases acquired global fence");
        using (new ResourceLease(old, ["application-owner"]))
            Check(true, "partial discovery failure releases earlier station owners");
        var missing = Path.Combine(root, "missing-original-data");
        using (var empty = new OpaqueRemovalOwnership(leaseRoot, "sportmaster", missing, default))
            Check(empty.Directories.Length == 0 && !Directory.Exists(missing), "missing original data remains missing; no empty station or database created");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel(); var untouched = Path.Combine(root, "cancelled-lease-root");
            Reject<OperationCanceledException>(() => { using var scope = new OpaqueRemovalOwnership(untouched, "sportmaster", missing, cancellation.Token); }, "cancellation accepted before first ownership side effect");
            Check(!Directory.Exists(untouched), "pre-cancelled inspection creates no lease directory");
        }
        var fileRoot = Path.Combine(root, "not-a-directory"); File.WriteAllText(fileRoot, "synthetic original");
        Reject<InvalidDataException>(() => OpaqueRemovalOwnership.Discover("sportmaster", fileRoot, default), "file replacing a data directory cannot be treated as an empty station");

        await using (var blocked = new ManualSupplyHarness(workspace, Path.Combine(root, "blocked-start"), deferred: true))
        {
            blocked.RootOwnership = () => ApplicationRootOwnership.AcquireAt(leaseRoot, "sportmaster", blocked.Launch.DataRoot);
            using (new OpaqueRemovalOwnership(leaseRoot, "sportmaster", blocked.Launch.DataRoot, default))
            {
                try { await blocked.StartAsync(); throw new Exception("startup bypassed root fence"); }
                catch (IOException) { Check(true, "actual Application startup cannot pass the injected global fence"); }
                Check(!Directory.Exists(blocked.Launch.DataRoot), "blocked startup has no seed/DB writes under original data root");
                Check(blocked.Factory.CreateCalls == 0, "blocked startup never calls capability factory Create");
            }
        }
        await using (var running = new ManualSupplyHarness(workspace, Path.Combine(root, "running"), deferred: true))
        {
            running.RootOwnership = () => ApplicationRootOwnership.AcquireAt(leaseRoot, "sportmaster", running.Launch.DataRoot);
            await running.StartAsync();
            Reject<IOException>(() => { using var scope = new OpaqueRemovalOwnership(leaseRoot, "sportmaster", running.Launch.DataRoot, default); }, "actual running Application keeps opaque maintenance out");
            await running.App.StopAsync(default);
            Reject<IOException>(() => { using var scope = new OpaqueRemovalOwnership(leaseRoot, "sportmaster", running.Launch.DataRoot, default); }, "Stop alone does not release global ownership before full disposal");
            running.Factory.FailDisposal = true;
            try { await running.App.DisposeAsync(); throw new Exception("factory release failure hidden"); }
            catch (IOException) { Check(running.App.HasUnreleasedResources, "actual failed factory disposal keeps resource ownership explicit"); }
            Reject<IOException>(() => { using var scope = new OpaqueRemovalOwnership(leaseRoot, "sportmaster", running.Launch.DataRoot, default); }, "failed resource release retains global maintenance fence");
            running.Factory.FailDisposal = false; await running.App.DisposeAsync();
            var database = Path.Combine(running.Launch.DataRoot, running.Config.Mode.ToString(), running.Config.StationId, "sportmaster", "sportmaster.db");
            var databaseHash = Wire.FileHash(database);
            using (var scope = new OpaqueRemovalOwnership(leaseRoot, "sportmaster", running.Launch.DataRoot, default))
                Check(scope.Directories.Length == 1 && Wire.FileHash(database) == databaseHash, "complete disposal allows ownership inspection without reopening actual SQLite");
        }
        Check(hashes.All(p => Wire.FileHash(p.Key) == p.Value), "all corrupt original canaries retained after every refusal and cleanup");
        Console.WriteLine("IE01-OPAQUE checks=" + checks + "; real file locks and Application/SQLite with injected machine lease root; Windows policy/UAC/uninstall/SDK/VM NOT_RUN");
    }
}
