using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
using Ipc.Reference.Protocol;

static class BackupHealthRecovery
{
    static void Check(bool value, string reason)
    { if (!value) throw new Exception(reason); Console.WriteLine("PASS BHR: " + reason); }
    static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> ready)
    {
        for (int n = 0; n < 250; n++) { var s = await app.SnapshotAsync().WaitAsync(TimeSpan.FromSeconds(1)); if (ready(s)) return s; await Task.Delay(20); }
        throw new Exception("Expected application state not reached: " + Wire.Encode(await app.SnapshotAsync()));
    }
    public static async Task Run(string workspace, string root)
    {
        // macOS temp aliases are links; use the physical test root for custody paths.
        if (OperatingSystem.IsMacOS() && root.StartsWith("/var/", StringComparison.Ordinal)) root = "/private" + root;
        List<string> failures = [];
        async Task Case(string name, Func<Task> test)
        {
            try { await test(); }
            catch (Exception ex) { failures.Add(name + ": " + ex.GetType().Name + ": " + ex.Message); Console.WriteLine("FAIL BHR: " + failures[^1]); }
        }
        ParcelConfiguration Config() => Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json"))) with { TimeoutMs = 200 };
        (ParcelRuntime App, BusinessLaunch Launch) Create(ParcelConfiguration c, string run)
        {
            var path = Path.Combine(root, run); Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), c);
            var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null);
            return (SportmasterApplication.Create(launch, new ParcelCapabilityFactory(launch)), launch);
        }
        async Task<BusinessReceipt> Command(ParcelRuntime app, string kind, string? owner = null)
        { var s = await app.SnapshotAsync(); return await app.CommandAsync(new(Wire.Id(), s.Revision, kind, owner)); }

        await Case("backup-destination-ancestors", async () =>
        {
            var c = Config(); var (app, launch) = Create(c, "backup-links"); await using var owned = app;
            await app.StartAsync(CancellationToken.None); await Command(app, "Pause");
            await app.AuthorizeMaintenanceAsync("maintenance", "SIMULATION_ONLY");
            var live = Path.Combine(launch.DataRoot, "Simulation", c.StationId, c.ApplicationId);
            async Task Reject(string destination, string detail)
            {
                try { await app.BackupAsync(destination); throw new Exception("Aliased backup accepted: " + detail); }
                catch (IOException) { Check(!File.Exists(destination) && !Directory.Exists(destination + ".recovery"), detail + " rejected before any custody output"); }
            }
            var alias = Path.Combine(root, "live-alias"); Directory.CreateSymbolicLink(alias, live);
            try
            {
                await Reject(Path.Combine(alias, "alias.db"), "linked destination parent into live application");
                await Reject(Path.Combine(alias, "not-created", "nested.db"), "linked ancestor above nonexistent destination parents");
            }
            finally { Directory.Delete(alias); }
            await Reject(Path.Combine(live, "inside.db"), "lexical live descendant");
            var dangling = Path.Combine(root, "dangling-alias"); Directory.CreateSymbolicLink(dangling, Path.Combine(live, "missing"));
            try { await Reject(Path.Combine(dangling, "dangling.db"), "dangling destination ancestor"); }
            finally { File.Delete(dangling); }
            Check((await app.BackupAsync(Path.Combine(root, "external", "new", "ordinary.db"))).Status == "BackupCreated", "ordinary external destination with new parents remains supported");
            if (OperatingSystem.IsWindows())
            {
                var junction = Path.Combine(root, "live-junction");
                var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argument in new[] { "/c", "mklink", "/J", junction, live }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!; await process.WaitForExitAsync();
                if (process.ExitCode != 0) throw new Exception("Junction fixture creation failed: " + await process.StandardError.ReadToEndAsync());
                try { await Reject(Path.Combine(junction, "junction.db"), "Windows Junction destination ancestor"); }
                finally { Directory.Delete(junction); }
            }
            else Console.WriteLine("BHR Windows Junction destination: NOT_RUN");
        });

        await Case("restored-mode-qualification", async () =>
        {
            // Synthetic offline Production custody tests the conflicting restore rules,
            // not a production adapter, approved deployment, or device operation.
            var c = Config(); var (app, launch) = Create(c, "restore-mode"); await using var owned = app;
            await app.StartAsync(CancellationToken.None); await Command(app, "Pause");
            await app.AuthorizeMaintenanceAsync("maintenance", "SIMULATION_ONLY");
            var backup = Path.Combine(root, "mode.db"); await app.BackupAsync(backup);
            var recovery = backup + ".recovery"; var configFile = Path.Combine(recovery, "configuration.json");
            var original = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(configFile));
            var production = original with { Effective = original.Effective with { Mode = RunMode.Production } };
            ExtensionStore.AtomicWrite(configFile, production);
            var manifestFile = Path.Combine(recovery, "COMPLETED.json"); var manifest = Wire.Decode<ParcelRecoveryManifest>(File.ReadAllText(manifestFile));
            ExtensionStore.AtomicWrite(manifestFile, manifest with { Files = new(manifest.Files) { ["configuration.json"] = Wire.FileHash(configFile) } });
            var target = Path.Combine(root, "production-restored", "Production", c.StationId, c.ApplicationId);
            ParcelRecovery.Restore(backup, target);
            Check(Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(Path.Combine(target, "configuration.json"))).Effective.Mode == RunMode.Production,
                "isolated recovery preserves Production mode and verified complete custody instead of relabeling Simulation");
            var config = Path.Combine(root, "production-config"); Directory.CreateDirectory(config);
            ExtensionStore.AtomicWrite(Path.Combine(config, "business.json"), production.Effective);
            var restoredLaunch = launch with { ConfigurationDirectory = config, DataRoot = Path.Combine(root, "production-restored") };
            await using var restored = SportmasterApplication.Create(restoredLaunch, new ParcelCapabilityFactory(restoredLaunch));
            try { await restored.StartAsync(CancellationToken.None); throw new Exception("Unqualified restored Production started"); }
            catch (InvalidDataException e) { Check(e.Message.Contains("Production NotReady", StringComparison.Ordinal), "restored Production still refuses missing real reader/qualification; no simulation fallback"); }
        });

        await Case("custody", async () =>
        {
            var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/optional/sportmaster/business.json")));
            c = c with { Login = null, Weight = new(WeightSource.Disabled, WeightUnit.Kg, 3), Scale = null,
                Images = c.Images! with { Uploader = c.Images.Uploader with { Parameters = new() { ["reply-mode"] = "lose-after-commit" } } } };
            var (app, launch) = Create(c, "custody"); await using var owned = app; await app.StartAsync(CancellationToken.None);
            Check(app.EmitReference("original-parcel", "00001", "original-event"), "original image observation admitted");
            var s = await Wait(app, x => x.Operations.SingleOrDefault()?.Upload?.State == StageState.Unknown); var op = s.Operations.Single();
            await Command(app, "Pause"); await app.AuthorizeMaintenanceAsync("maintenance", "SIMULATION_ONLY");
            var originalDirectory = Path.Combine(launch.DataRoot, "Simulation", c.StationId, c.ApplicationId);
            var originalImage = Path.Combine(originalDirectory, op.Image!.RelativeFile); var originalHash = Wire.FileHash(originalImage);
            var backup = Path.Combine(root, "custody.db");
            Check((await app.BackupAsync(backup)).Status == "BackupCreated", "actual BackupAsync includes unreleased nested image and receipt custody");
            var recovery = backup + ".recovery"; var completed = Path.Combine(recovery, "COMPLETED.json");
            var manifest = Wire.Decode<ParcelRecoveryManifest>(File.ReadAllText(completed));
            Check(manifest.Files.ContainsKey(op.Image.RelativeFile) && manifest.Files.Keys.All(x => !x.Contains('\\')) &&
                !manifest.Files.Keys.Any(x => x.EndsWith(".lease", StringComparison.Ordinal)), "manifest keys are slash paths and live leases are excluded");
            var restoredRoot = Path.Combine(root, "restored", "Simulation", c.StationId, c.ApplicationId);
            ParcelRecovery.Restore(backup, restoredRoot);
            Check(Wire.FileHash(Path.Combine(restoredRoot, op.Image.RelativeFile)) == originalHash, "restore preserves original image bytes");
            await app.StopAsync(CancellationToken.None); await app.DisposeAsync();
            var restoredLaunch = launch with { DataRoot = Path.Combine(root, "restored") };
            await using var restored = SportmasterApplication.Create(restoredLaunch, new ParcelCapabilityFactory(restoredLaunch));
            await restored.StartAsync(CancellationToken.None); s = await restored.SnapshotAsync(); var recovered = s.Operations.Single();
            Check(s.Paused && s.CurrentOperator is null && Wire.Encode(recovered.Upload!.Request) == Wire.Encode(op.Upload!.Request) && recovered.Upload.Attempts == 1 &&
                recovered.Upload.State == StageState.Unknown && Wire.FileHash(originalImage) == originalHash,
                "isolated restore retains original unknown upload identity/attempt, no login or automatic resend, source unchanged");
            await restored.ReconcileAsync(op.Id); s = await restored.SnapshotAsync();
            var ledger = Path.Combine(op.Upload!.Request.Binding.Parameters["peer-root"], Wire.Hash(op.Upload.Request.Binding.Target), op.Upload.Request.Id + ".json");
            Check(s.Operations.Single().Upload?.State == StageState.Accepted && Wire.Decode<ReferencePeer.PeerEntry>(File.ReadAllText(ledger)).Executions == 1,
                "restored query resolves independent upload ledger with exactly one external execution");

            void Reject(string name, ParcelRecoveryManifest altered)
            {
                ExtensionStore.AtomicWrite(completed, altered);
                var target = Path.Combine(root, "reject-" + name, c.ApplicationId);
                try { ParcelRecovery.Restore(backup, target); throw new Exception("Unsafe restore accepted: " + name); }
                catch (InvalidDataException) { Check(!Directory.Exists(target) && Wire.FileHash(originalImage) == originalHash, name + " rejected before target creation; original custody preserved"); }
                finally { ExtensionStore.AtomicWrite(completed, manifest); }
            }
            foreach (var bad in new[] { "../escape", "/absolute", "C:/drive", "images\\legacy.png", "images//empty.png", "images/./dot.png", "images/../parent.png", "images/AUX.png", "images/COM1.txt", "images/trailing.", "images/trailing ", "images/a:b", "images/a?b" })
            { var files = new Dictionary<string, string>(manifest.Files) { [bad] = originalHash }; Reject("path-" + Wire.Hash(bad)[..8], manifest with { Files = files }); }
            var imageKey = op.Image.RelativeFile; var alias = imageKey.ToUpperInvariant();
            if (alias != imageKey) Reject("case-alias", manifest with { Files = new(manifest.Files) { [alias] = originalHash } });
            Reject("bad-hash", manifest with { Files = new(manifest.Files) { [imageKey] = new string('0', 64) } });
            Reject("missing-file", manifest with { Files = new(manifest.Files) { ["images/missing.png"] = originalHash } });
            var extra = Path.Combine(recovery, "images/extra.png"); File.WriteAllText(extra, "synthetic extra");
            try { Reject("unlisted-file", manifest); } finally { File.Delete(extra); }
            var link = Path.Combine(recovery, "images/linked.png"); File.CreateSymbolicLink(link, originalImage);
            try { Reject("linked-file", manifest with { Files = new(manifest.Files) { ["images/linked.png"] = originalHash } }); } finally { File.Delete(link); }
            // Existing POSIX backups may contain names that cannot be restored safely on Windows.
            // Construct duplicate raw keys before deserialization can collapse them.
            var duplicated = System.Text.Json.JsonSerializer.Serialize(manifest).Replace("\"Files\":{", "\"Files\":{\"configuration.json\":\"" + manifest.Files["configuration.json"] + "\",");
            File.WriteAllText(completed, duplicated);
            try
            {
                var target = Path.Combine(root, "duplicate-json", c.ApplicationId);
                try { ParcelRecovery.Restore(backup, target); throw new Exception("Duplicate JSON key accepted"); }
                catch (InvalidDataException) { Check(!Directory.Exists(target), "raw duplicate JSON keys rejected before deserialization can collapse them"); }
            }
            finally { ExtensionStore.AtomicWrite(completed, manifest); }
            var imageBytes = File.ReadAllBytes(Path.Combine(recovery, imageKey)); File.WriteAllText(Path.Combine(recovery, imageKey), "changed synthetic image");
            try { Reject("changed-image", manifest); } finally { File.WriteAllBytes(Path.Combine(recovery, imageKey), imageBytes); }
            var linkDirectory = Path.Combine(recovery, "images/linked-directory"); Directory.CreateSymbolicLink(linkDirectory, originalDirectory);
            try { Reject("linked-directory", manifest); } finally { Directory.Delete(linkDirectory); }
            if (!OperatingSystem.IsWindows())
            {
            var invalid = Path.Combine(recovery, "images/AUX.png"); File.WriteAllText(invalid, "synthetic reserved name");
            try { Reject("windows-reserved-name", manifest with { Files = new(manifest.Files) { ["images/AUX.png"] = Wire.FileHash(invalid) } }); }
            finally { File.Delete(invalid); }
            }
            Console.WriteLine("BHR Windows actual BackupAsync separator reproduction: " + (OperatingSystem.IsWindows() ? "EXECUTED" : "NOT_RUN (host is not Windows)"));
        });

        foreach (var mode in new[] { PeerMode.StallHeaders, PeerMode.StallBody })
        await Case("startup-" + mode, async () =>
        {
            await using var server = new HealthServer { Mode = mode }; var c = Config();
            c = c with { Wcs = c.Wcs! with { Parameters = new(c.Wcs.Parameters) { ["endpoint"] = server.Url } } };
            var (app, _) = Create(c, "startup-" + mode); await using var owned = app;
            using var watchdog = new CancellationTokenSource(2500); var clock = Stopwatch.StartNew();
            await app.StartAsync(watchdog.Token); var s = await app.SnapshotAsync();
            Check(clock.ElapsedMilliseconds < 1800 && s.Health.Single(x => x.InstanceId == c.Wcs.InstanceId) is { Ready: false } &&
                !s.Availability!.Admit, "startup " + mode + " exhausts health budget as unavailable, no terminal worker failure");
            server.Mode = PeerMode.Ready; await Wait(app, x => x.Health.Any(h => h.InstanceId == c.Wcs.InstanceId && h.Ready) && x.Availability!.Admit);
            Check(true, "startup health can recover on later real confirmation " + mode);
        });

        await Case("caller-cancellation", async () =>
        {
            await using var server = new HealthServer { Mode = PeerMode.StallBody }; var c = Config();
            c = c with { TimeoutMs = 2000, Wcs = c.Wcs! with { Parameters = new() { ["endpoint"] = server.Url } } };
            var (app, _) = Create(c, "cancel-start"); await using var owned = app;
            using var caller = new CancellationTokenSource(); var start = app.StartAsync(caller.Token);
            await server.WaitForHealth(); caller.Cancel();
            try { await start; throw new Exception("Startup caller cancellation was swallowed"); }
            catch (OperationCanceledException) { Check(caller.IsCancellationRequested, "real startup cancellation propagates"); }
            await using var peer = new ReferencePeer(c.Wcs, Path.Combine(root, "peer"), "wcs-slot"); await peer.StartAsync(CancellationToken.None);
            using var direct = new CancellationTokenSource(); var probe = peer.HealthAsync(direct.Token);
            await Task.Delay(40); direct.Cancel();
            try { await probe; throw new Exception("Peer caller cancellation was swallowed"); }
            catch (OperationCanceledException) { Check(true, "ReferencePeer preserves caller cancellation because a single token does not identify its source"); }
        });

        await Case("stage-and-stop", async () =>
        {
            await using var server = new HealthServer(); var c = Config();
            c = c with { Wcs = c.Wcs! with { Parameters = new() { ["endpoint"] = server.Url } } };
            var (app, _) = Create(c, "stage"); await using var owned = app; await app.StartAsync(CancellationToken.None);
            server.Mode = PeerMode.StallBody;
            Check(app.EmitReference("health-stalled", "00002", "health-stalled"), "stage health observation queued");
            var s = await Wait(app, x => x.Operations.SingleOrDefault()?.State == ParcelState.Blocked); var op = s.Operations.Single();
            Check(op.Wcs is { State: StageState.Prepared, Attempts: 0 } && !s.BlockingReasons.Any(x => x.Contains("worker failed", StringComparison.OrdinalIgnoreCase)),
                "pre-send timeout retains Prepared identity and zero attempts without killing ingress");
            server.Mode = PeerMode.Ready; await Wait(app, x => x.Health.Any(h => h.InstanceId == c.Wcs.InstanceId && h.Ready));
            Check((await Command(app, "ContinuePrepared", op.Id)).Status == "Persisted", "explicit continuation can reach healthy provider without replacing original intent");
            // This server intentionally offers no submit endpoint: the transport may fail,
            // but the worker remains alive and the original attempted outcome stays Unknown.
            await Wait(app, x => x.Operations.Single().Wcs?.State == StageState.Unknown);
        });
        await Case("shutdown-before-send", async () =>
        {
            await using var server = new HealthServer(); var c = Config();
            c = c with { TimeoutMs = 2000, Wcs = c.Wcs! with { Parameters = new() { ["endpoint"] = server.Url } } };
            var (app, _) = Create(c, "stop-before-send"); await using var owned = app; await app.StartAsync(CancellationToken.None);
            var count = server.HealthRequests; server.Mode = PeerMode.StallBody;
            Check(app.EmitReference("shutdown-health", "00003", "shutdown-health"), "shutdown test observation queued");
            var s = await Wait(app, x => x.Operations.SingleOrDefault()?.Wcs?.State == StageState.Prepared);
            await server.WaitForHealth(count); await Command(app, "Pause");
            await app.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)); s = await app.SnapshotAsync();
            Check(s.State == "Stopped" && s.Operations.Single().Wcs is { State: StageState.Prepared, Attempts: 0, Failure: null },
                "shutdown during first pre-send health drains ownership without inventing device failure or delivery");
        });
        await Case("shutdown-after-send", async () =>
        {
            var c = Config() with { TimeoutMs = 2000 };
            var path = Path.Combine(root, "stop-after-send"); Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), c);
            var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null);
            var factory = new CancelAfterCommitFactory(new ParcelCapabilityFactory(launch));
            await using var app = SportmasterApplication.Create(launch, factory); await app.StartAsync(CancellationToken.None);
            app.EmitReference("shutdown-delivery", "00004", "shutdown-delivery"); await factory.Committed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await app.StopAsync(CancellationToken.None); var original = (await app.SnapshotAsync()).Operations.Single();
            Check(original.Wcs is { State: StageState.Unknown, Attempts: 1 }, "shutdown after external commit retains Unknown and original single attempt");
            await app.DisposeAsync();
            await using var restored = SportmasterApplication.Create(launch, new ParcelCapabilityFactory(launch)); await restored.StartAsync(CancellationToken.None);
            var recovered = (await restored.SnapshotAsync()).Operations.Single();
            Check(Wire.Encode(recovered.Wcs!.Request) == Wire.Encode(original.Wcs!.Request) && recovered.Wcs.Attempts == 1 && recovered.Wcs.State == StageState.Unknown,
                "restart after delivery cancellation preserves frozen request, no resend");
            await restored.ReconcileAsync(original.Id); var final = (await restored.SnapshotAsync()).Operations.Single();
            var ledger = Path.Combine(original.Wcs!.Request.Binding.Parameters["peer-root"], Wire.Hash(original.Wcs.Request.Binding.Target), original.Wcs.Request.Id + ".json");
            Check(final.Wcs?.State == StageState.Accepted && final.Wcs.Attempts == 1 && Wire.Decode<ReferencePeer.PeerEntry>(File.ReadAllText(ledger)).Executions == 1,
                "original query resolves cancelled delivery with exactly one independent execution");
        });
        foreach (var mode in new[] { PeerMode.OversizedDeclared, PeerMode.OversizedChunked, PeerMode.InvalidMarker, PeerMode.Unavailable })
        await Case("invalid-health-" + mode, async () =>
        {
            await using var server = new HealthServer { Mode = mode }; var binding = Config().Wcs!;
            binding = binding with { Parameters = new() { ["endpoint"] = server.Url } };
            await using var peer = new ReferencePeer(binding, root, "wcs-slot"); await peer.StartAsync(CancellationToken.None);
            using var caller = new CancellationTokenSource(1500); var health = await peer.HealthAsync(caller.Token);
            Check(!health.Ready && !caller.IsCancellationRequested, "bounded health rejects " + mode + " without buffering or waiting for a huge body");
        });
        await Case("legacy-health-cancellation", async () =>
        {
            await using var server = new HealthServer { Mode = PeerMode.StallHeaders };
            var config = Wire.Decode<StationConfig>(File.ReadAllText(Path.Combine(workspace, "config/station.simulation.json")));
            config = config with { TimeoutMs = 200, Integration = config.Integration with { Endpoint = server.Url.Replace("parcel-reference/", "") } };
            await using var adapter = new Ipc.Adapters.LoopbackIntegration(config);
            Check(!await adapter.HealthAsync(CancellationToken.None), "legacy loopback own HTTP timeout is unavailable");
            using var caller = new CancellationTokenSource(); var count = server.HealthRequests;
            var probe = adapter.HealthAsync(caller.Token); await server.WaitForHealth(count); caller.Cancel();
            try { await probe; throw new Exception("Legacy adapter swallowed caller cancellation"); }
            catch (OperationCanceledException) { Check(true, "legacy loopback preserves real caller cancellation"); }
        });
        await Case("accepted-query-cancellation", async () =>
        {
            var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/direct-plc/business.json")));
            var path = Path.Combine(root, "accepted-query"); Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), c);
            var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null);
            var factory = new CancelQueryFactory(new ParcelCapabilityFactory(launch));
            await using var app = Ipc.Application.DirectPlc.DirectPlcApplication.Create(launch, factory); await app.StartAsync(CancellationToken.None);
            app.EmitReference("accepted-original", "00005", "accepted-original");
            var original = (await Wait(app, x => x.Operations.SingleOrDefault()?.State == ParcelState.PlcAccepted)).Operations.Single();
            var query = app.ReconcileAsync(original.Id); await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await app.StopAsync(CancellationToken.None); await query;
            var final = (await app.SnapshotAsync()).Operations.Single();
            Check(final.State == ParcelState.PlcAccepted && final.Plc is { State: StageState.Accepted, Attempts: 1 } &&
                Wire.Encode(final.Plc.Replies) == Wire.Encode(original.Plc!.Replies) && Wire.Encode(final.Plc.Request) == Wire.Encode(original.Plc.Request),
                "query cancellation cannot erase existing PLC acceptance or original reply; physical completion remains unconfirmed");
        });
        if (failures.Count != 0) throw new Exception("BHR failures: " + string.Join(" | ", failures));
        Console.WriteLine("PASS BHR COMPLETE: custody, portable manifest rejection, bounded health, cancellation and original recovery semantics");
    }

    sealed class CancelAfterCommitFactory(ParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        public TaskCompletionSource Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public CapabilityRequirements Requirements(CapabilityBinding b) => inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c) { var p = inner.Create(c); return p with { Wcs = new CancelledWcs(p.Wcs!, Committed) }; }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class CancelledWcs(IWcsPort inner, TaskCompletionSource committed) : IWcsPort
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public async Task<StageReply> ResolveSlotAsync(FrozenRequest r, CancellationToken t)
        { var reply = await inner.ResolveSlotAsync(r, t); committed.TrySetResult(); await Task.Delay(Timeout.Infinite, t); return reply; }
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => inner.QueryAsync(r, t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class CancelQueryFactory(ParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public CapabilityRequirements Requirements(CapabilityBinding b) => inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c) { var p = inner.Create(c); return p with { Plc = new CancelledQuery(p.Plc!, Entered) }; }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class CancelledQuery(IPlcPort inner, TaskCompletionSource entered) : IPlcPort
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public Task<StageReply> SubmitAsync(FrozenRequest r, CancellationToken t) => inner.SubmitAsync(r, t);
        public async Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, t); return null; }
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    enum PeerMode { Ready, StallHeaders, StallBody, OversizedDeclared, OversizedChunked, InvalidMarker, Unavailable }
    sealed class HealthServer : IAsyncDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource stop = new();
        readonly ConcurrentBag<Task> clients = [];
        readonly Task loop;
        int mode, requests;
        public PeerMode Mode { get => (PeerMode)Volatile.Read(ref mode); set => Volatile.Write(ref mode, (int)value); }
        public int HealthRequests => Volatile.Read(ref requests);
        public string Url { get; }
        public HealthServer() { listener.Start(); Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/parcel-reference/"; loop = Accept(); }
        public async Task WaitForHealth(int after = 0)
        { for (int n = 0; n < 200; n++) { if (HealthRequests > after) return; await Task.Delay(10); } throw new Exception("No HTTP health request arrived"); }
        async Task Accept()
        {
            try { while (!stop.IsCancellationRequested) { var client = await listener.AcceptTcpClientAsync(stop.Token); clients.Add(Serve(client)); } }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
        async Task Serve(TcpClient client)
        {
            using (client)
            try
            {
                var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var request = await reader.ReadLineAsync(stop.Token); string? line;
                do { line = await reader.ReadLineAsync(stop.Token); } while (!string.IsNullOrEmpty(line));
                if (request?.StartsWith("GET /parcel-reference/health ", StringComparison.Ordinal) != true && request?.StartsWith("GET /health ", StringComparison.Ordinal) != true) return; // No submit endpoint: drop transport without claiming business acceptance.
                var selected = Mode; Interlocked.Increment(ref requests);
                if (selected == PeerMode.StallHeaders) { await Task.Delay(Timeout.Infinite, stop.Token); return; }
                const string marker = "PARCEL-REFERENCE-v1";
                var response = selected switch
                {
                    PeerMode.OversizedDeclared => "HTTP/1.1 200 OK\r\nContent-Length: 1000000000\r\nConnection: close\r\n\r\n",
                    PeerMode.OversizedChunked => "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n14\r\n" + marker + "XX\r\n0\r\n\r\n",
                    PeerMode.Unavailable => "HTTP/1.1 503 Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
                    _ => "HTTP/1.1 200 OK\r\nContent-Length: " + Encoding.ASCII.GetByteCount(marker) + "\r\nConnection: close\r\n\r\n" + (selected == PeerMode.Ready ? marker : selected == PeerMode.InvalidMarker ? new string('X', marker.Length) : "P")
                };
                var bytes = Encoding.ASCII.GetBytes(response);
                await stream.WriteAsync(bytes, stop.Token);
                if (selected is PeerMode.StallBody or PeerMode.OversizedDeclared) await Task.Delay(Timeout.Infinite, stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException) { }
        }
        public async ValueTask DisposeAsync()
        { stop.Cancel(); listener.Stop(); await loop; await Task.WhenAll(clients.ToArray()); stop.Dispose(); }
    }
}
