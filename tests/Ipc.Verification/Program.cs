using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

if (args.FirstOrDefault() == "--initialize-isolated-ci-storage")
{
    CleanStorageBootstrap.InitializeIsolatedCiStorage(); return;
}

if (args.FirstOrDefault() is "--clean-storage-bootstrap" or "--clean-storage-bootstrap-elevated")
{
    CleanStorageBootstrap.Run(args[0].EndsWith("-elevated", StringComparison.Ordinal)); return;
}
if (args.FirstOrDefault() == "--runtime-identity")
{
    RuntimeIdentityVerification.Run();
    return;
}
if (args.FirstOrDefault() == "--windows-runtime-setup")
{
    RuntimeIdentityVerification.RunWindowsSetup();
    return;
}

if (args.FirstOrDefault() == "--commercial-removal")
{
    CommercialRemovalVerification.Run(Path.Combine(Path.GetTempPath(), "ipc-removal-" + Wire.Id()));
    return;
}
SupplyContractRegistration.Register();

if (args.FirstOrDefault() == "--extension-interrupt")
{
    var trust = Wire.Decode<ExtensionTrust>(File.ReadAllText(args[2]));
    var installation = new ExtensionStore(args[1], trust, RunMode.Simulation, args[4]);
    installation.PhaseBoundary = phase => { if (phase == args[5]) Environment.Exit(73); };
    installation.Install(args[3], Wire.FileHash(args[3]));
    throw new InvalidOperationException("Requested abrupt installation boundary not reached");
}
if (args.FirstOrDefault() == "--child")
{
    var dir = args[1]; var point = args[2]; var cfg = Wire.Decode<StationConfig>(File.ReadAllText(Path.Combine(dir, "config.json")));
    var injection = new FaultInjection { CrashAt = point is "BeforeT1" or "AfterT1" or "AfterT2" or "AfterT3" or "T3Committed" ? point : null, FailT3 = point == "T3Failure" };
    await using var h = await Rig.Open(dir, cfg, injection);
    await h.Command("OpenContext", value: "000001");
    var claim = await h.App.ClaimAsync();
    if (claim is not null)
    {
        var (request, attempt) = claim.Value;
        if (await h.App.BeforeSendAsync(request, attempt))
        {
            var response = await h.Port.ExecuteAsync(request, CancellationToken.None);
            if (point == "Remote") Environment.FailFast("SIMULATION accepted remotely, before T3");
            await h.App.CompleteAsync(request, attempt, response);
            if (point == "T3Failure") Environment.FailFast("SIMULATION T3 write failure then process crash");
        }
    }
    throw new Exception("Expected crash window was not reached");
}

if (args.Contains("--help", StringComparer.Ordinal))
{
    if (args.Length != 1) throw new ArgumentException("--help must be used alone");
    Console.WriteLine(VerificationScopes.Help); return;
}
var workspace = Path.GetFullPath(args.FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal)) ?? ".");
var selector = args.FirstOrDefault(x => x.StartsWith("--", StringComparison.Ordinal)) ?? VerificationScopes.Default;
if (args.Count(x => x.StartsWith("--", StringComparison.Ordinal)) > 1 || args.Count(x => !x.StartsWith("--", StringComparison.Ordinal)) > 1)
    throw new ArgumentException("Use one selector and at most one positional workspace. Use --help for actual coverage.");
var selected = VerificationScopes.Select(selector);
var full = selector == VerificationScopes.Default;
Console.WriteLine("SELECT " + selector + ": " + selected.Coverage);
var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Environment.ProcessPath ?? "dotnet";
if (!Path.GetFileNameWithoutExtension(dotnet).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) dotnet = "dotnet";
var root = Path.Combine(Path.GetTempPath(), "ipc-verification-" + Wire.Id()); Directory.CreateDirectory(root);
Console.WriteLine("Evidence data retained at " + root);
var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
var url = $"http://127.0.0.1:{port}/";
var fixtureDll = Path.Combine(workspace, "src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll");
using var fixture = Start(dotnet, fixtureDll, "--url", url, "--data", Path.Combine(root, "receiver"));
using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
try
{
    await Eventually(async () => { try { return (await http.GetStringAsync(url + "health")) == "SIMULATION-v1"; } catch (HttpRequestException) { return false; } });
    if (selector == "--maintainability")
    {
        await HealthRenewalVerification.Run(root, url);
        await ArchitectureMaintainabilityVerification.Run(root, url, workspace);
        await OperationClosureVerification.EditingAndTiming(root, url, workspace);
    }
    if (selector == "--health-profile") await HealthRenewalVerification.Profile(root, url);
    if (full || selector is "--hardening" or "--alignment") await HardeningVerification.Run(root, url, workspace);
    if (full || selector is "--alignment" or "--data")
    {
        await AlignmentDataVerification.Run(root, url, workspace);
        await AlignmentCompositionVerification.Run(root, url, workspace);
        await AlignmentRuntimeVerification.Run(root, url, workspace);
    }
    if (full || selector == "--operation-closure") await OperationClosureVerification.Run(root, url, workspace);
    if (full || selector == "--conformance") await ConformanceVerification.Run(root, url, workspace);
    if (selector == "--architecture") await ArchitectureClosureVerification.Run(root, url, workspace);
    if (selector == "--current-contracts") await CurrentContractVerification.Run(root, url);
    if (selector == "--query-fixture") await OperationClosureVerification.QueryEndpointLifetime(root, url);
    if (full || selector == "--extensions") await ExtensionVerification.Run(root, url, workspace);
    if (full || selector == "--closure") await ClosureVerification.Run(root, url, workspace);
    if (full || selector == "--followup") await FollowupVerification.Run(root, url, workspace);
    if (full || selector == "--review-followup")
    {
        PresentationVerification.Run(workspace);
        await ProductOwnershipVerification.Run(root, url);
    }
    if (full || selector == "--baseline")
    {
    await Check("AC-01/10 lifecycle, bounded command queue and ownership", async () =>
    {
        var stopped = new List<string>(); var modules = new[] { new TestModule("a", [], stopped), new TestModule("b", ["a"], stopped, fail: true) };
        await using (var catalog = new ModuleCatalog(modules))
        {
            await Throws<InvalidOperationException>(() => catalog.StartAsync(CancellationToken.None));
            Assert(stopped.SequenceEqual(new[] { "b", "a" }), "Partial initialization must unwind current and previous owners in reverse order");
        }
        int overload = 0; var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var queue = new BoundedExecutor(1, () => overload++))
        {
            var first = queue.Execute(async () => { started.SetResult(); await release.Task; return 1; }); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = queue.Execute(() => Task.FromResult(2));
            await Throws<InvalidOperationException>(() => queue.Execute(() => Task.FromResult(3)));
            release.SetResult(); Assert(await first == 1 && await second == 2 && overload == 1, "Accepted queue items drain; rejected item is diagnosed");
        }
        var leases = Path.Combine(root, "leases");
        using var lease = new ResourceLease(leases, ["station", "device"]);
        await Throws<IOException>(() => { using var duplicate = new ResourceLease(leases, ["device"]); return Task.CompletedTask; });
    });
    await Check("AC-01/11 product assembly, modes and strict configuration", async () =>
    {
        var cfg = Rig.Config(url); var install = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var manifest = Manifest(install);
        Assert(ReferenceProduct.Validate(manifest, cfg, new(), install).Count == 0, "Approved explicit composition");
        var bad = manifest with { Modules = manifest.Modules.Select(x => x.Id == "supply" ? x with { ContractMajor = 9 } : x).ToArray() };
        Assert(ReferenceProduct.Validate(bad, cfg, new(), install).Count > 0, "Contract mismatch refused");
        Assert(ReferenceProduct.Validate(manifest with { Architecture = "impossible" }, cfg, new(), install).Count > 0, "Architecture refused");
        Assert(ReferenceProduct.Validate(manifest with { Modules = manifest.Modules.Select(x => x with { Sha256 = "changed" }).ToArray() }, cfg, new(), install).Count > 0, "Changed file refused");
        Assert(SupplyConfiguration.Validate(cfg with { Integration = cfg.Integration with { Endpoint = "https://example.com/" } }).Count > 0, "External fixture endpoint refused");
        Assert(SupplyConfiguration.Validate(cfg with { Mode = RunMode.Production }).Any(x => x.Contains("Production NotReady")), "Missing production capabilities refused");
        await Throws<System.Text.Json.JsonException>(() => Task.FromResult(Wire.Decode<StationConfig>(Wire.Encode(cfg).Replace("\"SchemaVersion\"", "\"UnknownField\""))));
        var bootRoot = Path.Combine(root, "product-config"); Directory.CreateDirectory(bootRoot);
        File.WriteAllText(Path.Combine(bootRoot, "product.json"), Wire.Encode(manifest)); File.WriteAllText(Path.Combine(bootRoot, "station.json"), Wire.Encode(cfg)); File.WriteAllText(Path.Combine(bootRoot, "presentation.json"), Wire.Encode(new PresentationSettings()));
        var boot = new BootOptions(Path.Combine(bootRoot, "product.json"), Path.Combine(bootRoot, "station.json"), Path.Combine(bootRoot, "presentation.json"), Path.Combine(root, "product-data"), install, Path.Combine(root, "product-leases"));
        using var host = ProductHost.Build(boot); await host.StartAsync();
        var runtime = host.Services.GetRequiredService<ProductRuntime>(); Assert(runtime.Diagnostics.Count == 0 && runtime.Application is not null, "Generic Host assembly starts");
        var identity = runtime.RuntimeId; await runtime.SavePresentationAsync(new("workstation", "compact"));
        Assert(runtime.RuntimeId == identity && runtime.Presentation.Layout == "compact", "Layout preference does not rebuild runtime");
        var app1 = runtime.Application; var app2 = host.Services.GetRequiredService<ProductRuntime>().Application;
        for (var i = 0; i < 50; i++) await app2!.SnapshotAsync();
        Assert(ReferenceEquals(app1, app2), "Multiple view facades share one business instance");
        using var second = ProductHost.Build(boot); await second.StartAsync();
        Assert(second.Services.GetRequiredService<ProductRuntime>().Diagnostics.Count > 0, "Second process ownership reports diagnostics"); await second.StopAsync();
        await runtime.ExportAsync(Path.Combine(root, "metadata-diagnostics.json"));
        Assert(!File.ReadAllText(Path.Combine(root, "metadata-diagnostics.json")).Contains(cfg.Integration.Endpoint), "Diagnostic excludes endpoint");
        await host.StopAsync(); host.Dispose();
        // A failed source activation must retain the previous effective version in the real DB.
        async Task<CommandReceipt> ReviewedMaintenance(ProductRuntime target, string kind, string? owner = null, string? value = null)
        {
            CommandReceipt? receipt = null;
            await Eventually(async () =>
            {
                var snapshot = await target.Application!.SnapshotAsync();
                receipt = await target.Application.ExecuteAsync(new(Wire.Id(), snapshot.State.Revision, kind, owner, value));
                // Conflict is an explicit pre-execution rejection. Re-read the projection;
                // never retry an uncertain result, an exception or a business rejection.
                return receipt.Status != "Conflict";
            });
            return receipt!;
        }
        async Task Login(ProductRuntime target)
        {
            var snapshot = await target.Application!.SnapshotAsync();
            var stale = await target.Application.ExecuteAsync(new(Wire.Id(), snapshot.State.Revision - 1, "Login", Value: "maintenance"));
            Assert(stale.Status == "Conflict", "Stale maintenance command remains rejected before execution");
            var receipt = await ReviewedMaintenance(target, "Login", value: "maintenance");
            Assert(receipt.Status == "Persisted", "Startup maintenance identity: " + receipt.Reason);
        }
        async Task Stage(ProductRuntime target, StationConfig draft)
        {
            var app = target.Application!; await Eventually(async () => !(await app.SnapshotAsync()).State.Blocks.Any(x => x.Source == "transport"));
            var login = await ReviewedMaintenance(target, "Login", value: "maintenance"); Assert(login.Status == "Persisted", "Maintenance login");
            var saved = await ReviewedMaintenance(target, "SaveConfig", value: Wire.Encode(draft)); Assert(saved.Status == "Persisted", "Draft persisted: " + saved.Status + ": " + saved.Reason);
            var revision = (await app.ConfigsAsync()).First();
            Assert((await ReviewedMaintenance(target, "ActivateConfig", revision.Id)).Status == "Persisted", "Restart activation staged");
        }
        using (var setup = ProductHost.Build(boot))
        {
            await setup.StartAsync(); var current = setup.Services.GetRequiredService<ProductRuntime>();
            var broken = cfg with { Version = "SIM-2", Devices = cfg.Devices.Select(x => x.Role == "left" ? x with { Driver = "replay", ReplayFile = "missing-replay.jsonl" } : x).ToArray() };
            await Stage(current, broken); await setup.StopAsync();
        }
        using (var failed = ProductHost.Build(boot))
        {
            await failed.StartAsync(); var current = failed.Services.GetRequiredService<ProductRuntime>();
            await Login(current);
            Assert(current.Diagnostics.Count > 0 && (await current.Application!.ConfigsAsync()).First().Status == "ActivationFailed", "Source initialization failure recorded without promoting configuration"); await failed.StopAsync();
        }
        using (var fallback = ProductHost.Build(boot))
        {
            await fallback.StartAsync(); var current = fallback.Services.GetRequiredService<ProductRuntime>();
            Assert(current.Presentation.Layout == "compact", "Presentation preference survives controlled restart");
            Assert(current.Configuration!.Version == cfg.Version && current.Diagnostics.Count == 0, "Failed activation retains and restarts old effective configuration");
            await Stage(current, cfg with { Version = "SIM-3", TimeoutMs = 800 }); await fallback.StopAsync();
        }
        using (var active = ProductHost.Build(boot))
        {
            await active.StartAsync(); var current = active.Services.GetRequiredService<ProductRuntime>();
            await Login(current);
            Assert(current.Configuration!.Version == "SIM-3" && (await current.Application!.ConfigsAsync()).First().Status == "Effective", "Configuration becomes Effective only after real owned modules initialize"); await active.StopAsync();
        }
    });
    await Check("AC-02/07 combined guards and independent alarms", async () =>
    {
        await using var h = await Rig.New(root, url);
        await h.OpenBox();
        foreach (var code in new[] { "EntityPermit", "WcsUnavailable", "License", "DeviceFault" })
        {
            await h.Command("SetSyntheticBlock", value: code); await h.Command("Pause"); await h.Command("Resume");
            Assert(!(await h.App.SnapshotAsync()).Admit.Allowed, "Resume cannot clear " + code);
            var blocked = await h.Command("CheckClose", expect: "RejectedByGuard"); Assert(blocked.Reason.Contains(code), "Shared close guard includes " + code);
            await h.Command("ClearSyntheticBlock", value: code);
        }
        await h.App.SetDeviceAsync(h.ConfigValue.Devices[0].DeviceId, h.Epochs["left"], DeviceHealth.Faulted);
        await h.Command("Pause"); await h.Command("Resume"); Assert(!(await h.App.SnapshotAsync()).Admit.Allowed, "Faulted device never green");
        await h.App.SetDeviceAsync(h.ConfigValue.Devices[1].DeviceId, h.Epochs["right"], (DeviceHealth)987);
        Assert((await h.App.SnapshotAsync()).State.Devices["right"].Health == DeviceHealth.Ready, "Unknown enum cannot replace registered source health");
        await h.App.SetDeviceAsync(h.ConfigValue.Devices[1].DeviceId, h.Epochs["right"], DeviceHealth.Unknown);
        var alarms = (await h.App.SnapshotAsync()).Alarms.Where(x => x.ClearedAt is null).OrderBy(x => x.Scope).ToArray(); Assert(alarms.Length >= 2, "Two independent source alarms");
        await h.Command("AckAlarm", alarms[0].Id);
        var after = (await h.App.SnapshotAsync()).Alarms; Assert(after.Single(x => x.Id == alarms[0].Id).AckedBy is not null && after.Single(x => x.Id == alarms[1].Id).AckedBy is null, "Ack is occurrence specific");
        await h.App.SetDeviceAsync(h.ConfigValue.Devices[0].DeviceId, h.Epochs["left"], DeviceHealth.Ready);
        Assert((await h.App.AlarmHistoryAsync()).Single(x => x.Id == alarms[0].Id).ClearedAt is not null, "Recovery and acknowledgement independently stored");
        await h.Command("Logout"); Assert(!(await h.App.SnapshotAsync()).Admit.Allowed, "Logout blocks admission");
    });
    await Check("AC-03/04/06 continuous same-code entities, correction and unique ledger", async () =>
    {
        await using var h = await Rig.New(root, url, demand: 100); await h.OpenBox(); var session = (await h.App.SnapshotAsync()).State.Session!.Id;
        var opIds = new HashSet<string>();
        for (int i = 0; i < 22; i++)
        {
            var lane = i % 2 == 0 ? "left" : "right"; var op = await h.Enter(lane);
            if (i == 1)
            {
                await h.Event(lane, "Read", op.AdmissionId, [], 1); Assert((await h.Op(lane)).Status == OperationStatus.ValidationRejected, "No-code retained");
                await h.Event(lane, "Read", op.AdmissionId, ["123", "456"], 1); Assert((await h.Op(lane)).Status == OperationStatus.ValidationRejected, "Multicode retained");
                await h.Event(lane, "Read", op.AdmissionId, ["illegal"], 1); Assert((await h.Op(lane)).Status == OperationStatus.ValidationRejected, "Invalid code retained");
            }
            if (i == 2) await h.Command("SetFixtureFault", value: "Reject");
            var sourceId = Wire.Id();
            await h.Event(lane, "Read", op.AdmissionId, ["000123"], 1, sourceId); await h.Drain();
            if (i == 2) { Assert((await h.Op(lane)).Status == OperationStatus.ValidationRejected, "Authority rejection"); await h.Event(lane, "Read", op.AdmissionId, ["000123"], 1); await h.Drain(); }
            var before = (await h.App.DetailsAsync(op.Id)).Length;
            await h.Event(lane, "Read", op.AdmissionId, ["000123"], 1, sourceId);
            Assert((await h.App.DetailsAsync(op.Id)).Length == before, "Source identity duplicate not a new request");
            await h.Command("Submit", op.Id); await h.Drain(); Assert((await h.Op(lane)).Status == OperationStatus.Accepted, "Submitted entity accepted");
            var detail = await h.App.DetailsAsync(op.Id); Assert(detail.Length >= 2, "Multiple requests per operation");
            var submit = detail.Single(x => x.Request.Kind == RequestKind.Submit);
            await h.App.CompleteAsync(submit.Request, null, submit.Responses.Single());
            await h.Event(lane, "Depart", op.AdmissionId); await h.Event(lane, "Reset", op.AdmissionId); opIds.Add(op.Id);
        }
        Assert(opIds.Count == 22 && h.Store.AcceptedQuantity(session) == 22, "Same code new entities counted independently, duplicate receipts never recount");
        var history = await h.App.HistoryAsync("000123"); Assert(history.Length == 22, "History reads stored snapshots");
        Assert((await h.App.SnapshotAsync()).State.Session!.Id == session, "Continuous same box, no scene reset");
        await h.App.ReceiveAsync(new(Wire.Id(), h.ConfigValue.Devices[0].DeviceId, "old-epoch", Wire.Id(), 999, Wire.Id(), "Enter", [], null, DateTimeOffset.UtcNow));
        Assert(!(await h.App.SnapshotAsync()).Admit.Allowed, "Old connection observations retained for review, never bound to new entity");
        await h.ReviewSources("SIMULATION source epoch inspected; no new entity admitted");
    });
    await Check("AC-04/09 split, atomic two-lane demand and close barrier", async () =>
    {
        await using var h = await Rig.New(root, url, demand: 6); await h.OpenBox();
        var left = await h.Enter("left"); var right = await h.Enter("right");
        await h.Event("left", "Read", left.AdmissionId, ["000009"], 9); await h.Event("right", "Read", right.AdmissionId, ["000006"], 6); await h.Drain();
        Assert((await h.Op("left")).Supply == 6 && (await h.Op("left")).Status == OperationStatus.SplitRequired, "9/6/3 synthetic keep policy");
        await h.Command("Submit", left.Id, expect: "RejectedByGuard");
        await h.Command("ScanRetained", left.Id, "unit-0"); await h.Command("ScanRetained", left.Id, "unit-0"); Assert((await h.Op("left")).SplitScans.Count == 1, "Retained unit identity deduplicated");
        await h.Command("ClearSplit", left.Id, "CONFIRM");
        for (var i = 0; i < 6; i++) await h.Command("ScanRetained", left.Id, "unit-" + i);
        await h.Command("Submit", left.Id); await h.Command("Submit", right.Id);
        var l = (await h.App.ClaimAsync())!.Value; var r = (await h.App.ClaimAsync())!.Value;
        Assert(await h.App.BeforeSendAsync(l.Request, l.Attempt) && await h.App.BeforeSendAsync(r.Request, r.Attempt), "Both prepared before external arbitration");
        var responses = await Task.WhenAll(h.Port.ExecuteAsync(l.Request, CancellationToken.None), h.Port.ExecuteAsync(r.Request, CancellationToken.None));
        // Persist in opposite order to prove receipt order does not decide demand.
        await h.App.CompleteAsync(r.Request, r.Attempt, responses[1]); await h.App.CompleteAsync(l.Request, l.Attempt, responses[0]);
        Assert(responses.Count(x => x.Result == "Accepted") == 1 && h.Store.AcceptedQuantity(l.Request.SessionId) == 6, "Receiver atomically arbitrates shared demand");
        foreach (var lane in new[] { "left", "right" }) { var op = await h.Op(lane); await h.Event(lane, "Depart", op.AdmissionId); await h.Event(lane, "Reset", op.AdmissionId); }
        await h.Command("CheckClose"); Assert(!(await h.App.SnapshotAsync()).Admit.Allowed, "Two-lane close barrier"); await h.Drain();
        var s = (await h.App.SnapshotAsync()).State.Session!; Assert(s.CloseRemaining == 0 && s.CloseToken is not null, "Authoritative close snapshot");
        await h.Command("ConfirmClose", s.CloseCheckRequest, "CONFIRM"); await h.Drain(); Assert((await h.App.SnapshotAsync()).State.Session!.Status == SessionStatus.Closed, "Only close receipt closes context");
    });
    await Check("AC-05/06/09 dropped/late response, original binding and close Unknown", async () =>
    {
        await using var h = await Rig.New(root, url, demand: 10); await h.OpenBox();
        var op = await h.Enter("left"); await h.Event("left", "Read", op.AdmissionId, ["000001"], 1); await h.Drain();
        await h.Command("SetFixtureFault", value: "Drop"); await h.Command("Submit", op.Id); await h.Drain();
        var unknown = (await h.App.UnknownRequestsAsync()).Single(); var originalPayload = unknown.Payload;
        await h.Command("Pause"); await h.Command("Resume"); await h.Command("Submit", op.Id, expect: "RejectedByGuard");
        Assert(await h.App.ClaimAsync() is null, "Unknown is never blindly resent");
        var evidence = await h.Port.QueryAsync(unknown, CancellationToken.None); Assert(evidence is not null, "Independent receiving ledger survived dropped response");
        await h.App.CompleteAsync(unknown, null, evidence!); Assert(h.Store.AcceptedQuantity(unknown.SessionId) == 1, "Reconcile acceptance once");
        await h.Event("left", "Depart", op.AdmissionId); await h.Event("left", "Reset", op.AdmissionId);
        var next = await h.Enter("left"); await h.App.CompleteAsync(unknown, null, evidence!);
        Assert((await h.Op("left")).Id == next.Id && (await h.Op("left")).Status == OperationStatus.Observed, "Late old receipt cannot overwrite current entity");
        await h.Event("left", "Read", next.AdmissionId, ["000001"], 1); await h.Drain(); await h.Command("CancelDraft", next.Id);
        await h.Event("left", "Depart", next.AdmissionId); await h.Event("left", "Reset", next.AdmissionId);
        await h.Command("CheckClose"); await h.Drain(); await h.Command("CancelClose"); Assert((await h.App.SnapshotAsync()).State.Session!.Status == SessionStatus.Active, "Shortage cancel restores active context");
        await h.Command("CheckClose"); await h.Drain(); var check = (await h.App.SnapshotAsync()).State.Session!;
        await h.Command("SetFixtureFault", value: "Drop"); await h.Command("ConfirmClose", check.CloseCheckRequest, "CONFIRM"); await h.Drain();
        var close = (await h.App.UnknownRequestsAsync()).Single(); Assert(close.Kind == RequestKind.Close && (await h.App.SnapshotAsync()).State.Session!.Status == SessionStatus.CloseUnknown, "CloseUnknown links original close identity");
        await h.Command("OpenContext", value: "000002", expect: "RejectedByGuard");
        await h.App.CompleteAsync(close, null, (await h.Port.QueryAsync(close, CancellationToken.None))!);
        Assert((await h.App.SnapshotAsync()).State.Session!.Status == SessionStatus.Closed && h.Store.Request(unknown.Id)!.Payload == originalPayload, "Close reconciled; original request immutable");
        // Different content under the same identity is rejected by the receiver.
        var changed = unknown with { Payload = originalPayload + " ", PayloadHash = Wire.Hash(originalPayload + " ") };
        await Throws<HttpRequestException>(() => h.Port.ExecuteAsync(changed, CancellationToken.None));
    });
    await Check("AC-05 query absent/unsupported is not proof of non-delivery; delayed receipts", async () =>
    {
        foreach (var fault in new[] { "QueryMissing", "QueryUnsupported", "Delay" })
        {
            await using var h = await Rig.New(root, url, timeout: 300); await h.Command("SetFixtureFault", value: fault); await h.Command("OpenContext", value: "000001"); await h.Drain();
            var request = (await h.App.UnknownRequestsAsync()).Single(); var query = await h.Port.QueryAsync(request, CancellationToken.None);
            if (fault == "Delay") { Assert(query is not null, "Late result is queryable after receiver commit"); await h.App.CompleteAsync(request, null, query!); }
            else { Assert(query is null && await h.App.ClaimAsync() is null, "Absent/unsupported query remains Unknown; no resend"); }
        }
    });
    await Check("AC-05 actual SQLite rollback and T3 storage failure", async () =>
    {
        await using var h = await Rig.New(root, url);
        using (var db = new SqliteConnection("Data Source=" + h.Store.Path))
        { db.Open(); using var c = db.CreateCommand(); c.CommandText = "CREATE TRIGGER fail_intent BEFORE INSERT ON requests BEGIN SELECT RAISE(ABORT,'SIMULATION T1 failure'); END;"; c.ExecuteNonQuery(); }
        var old = (await h.App.SnapshotAsync()).State.Revision;
        await h.Command("OpenContext", value: "000001", expect: "StorageFailure");
        Assert(h.Store.Requests().Length == 0 && h.Store.Load().Revision == old && h.Store.Load().Session is null, "T1 failure rolls back state and intent together");
    });
    await Check("AC-05/09 real process crash/restart windows", async () =>
    {
        foreach (var point in new[] { "BeforeT1", "AfterT1", "AfterT2", "Remote", "T3Failure", "AfterT3", "T3Committed" })
        {
            var cfg = Rig.Config(url); var dir = Path.Combine(root, "crash-" + point); Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "config.json"), Wire.Encode(cfg));
            using var child = Start(dotnet, Assembly.GetExecutingAssembly().Location, "--child", dir, point);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); Assert(child.ExitCode != 0, "Actual child process terminates at " + point);
            await using var h = await Rig.Open(dir, cfg);
            var requests = h.Store.Requests();
            if (point == "BeforeT1") Assert(requests.Length == 0, "Before T1 has no intent");
            else if (point == "AfterT1") { Assert(requests.Single().Status == RequestStatus.Prepared, "Prepared survives restart"); await h.Drain(); Assert(h.Store.Requests().Single().Status == RequestStatus.Accepted, "Prepared dispatch rechecks permits"); }
            else if (point is "AfterT3" or "T3Committed")
            {
                Assert(requests.Single().Status == RequestStatus.Accepted, "Committed acceptance survives process death");
                Assert(!h.Store.RecoveryRequired(requests.Single().Id) && h.Store.IncompleteReceipts().Length == 0 &&
                    (await h.App.SnapshotAsync()).State.Blocks.All(x => x.Code != "ReceiptPersistenceUncertain"), "Completed T3 process death remains operational without a false receipt quarantine");
            }
            else if (point == "T3Failure")
            {
                var request = requests.Single();
                Assert(request.Status == RequestStatus.Accepted && h.Store.Responses(request.Id).Length == 1 && await h.App.ClaimAsync() is null,
                    "Flushed raw receipt custody completes T3 on restart without a network resend");
                Assert(await h.Port.QueryAsync(request, CancellationToken.None) == h.Store.Responses(request.Id).Single() && h.Store.Attempts(request.Id).Length == 1,
                    "Custody recovery matches independent receiver and preserves original attempt");
            }
            else
            {
                var request = requests.Single(); Assert(request.Status == RequestStatus.Unknown && await h.App.ClaimAsync() is null, "Interrupted claim restored Unknown without resend");
                var response = await h.Port.QueryAsync(request, CancellationToken.None);
                if (point == "AfterT2") Assert(response is null, "No receiver record still does not permit blind resend");
                else { Assert(response is not null, "Remote acceptance persisted independently at " + point); await h.App.CompleteAsync(request, null, response!); Assert(h.Store.Requests().Single().Id == request.Id && h.Store.Attempts(request.Id).Length == 1, "Original ID and attempt retained through restart"); }
            }
        }
    });
    await Check("AC-02/05/06 durable T3 failure, stale commands, entity permit restart", async () =>
    {
        var cfg = Rig.Config(url); var directory = Path.Combine(root, "permit-restart");
        string owner, admission, requestId, frozen;
        await using (var h = await Rig.Open(directory, cfg))
        {
            await h.OpenBox(); var op = await h.Enter("left"); owner = op.Id; admission = op.AdmissionId;
            await h.Event("left", "Read", admission, ["000001"], 1); await h.Drain();
            var snapshot = await h.App.SnapshotAsync(); var command = new Command(Wire.Id(), snapshot.State.Revision, "Submit", owner);
            var first = await h.App.ExecuteAsync(command); var duplicate = await h.App.ExecuteAsync(command);
            Assert(first == duplicate, "Duplicate command returns original persisted receipt");
            Assert((await h.App.ExecuteAsync(command with { Value = "changed" })).Status == "Conflict", "Same command identity different content conflicts");
            Assert((await h.App.ExecuteAsync(new(Wire.Id(), snapshot.State.Revision, "Pause"))).Status == "Conflict", "Stale UI revision rejected");
            var pending = h.Store.Requests(owner).Single(x => x.Kind == RequestKind.Submit); requestId = pending.Id; frozen = pending.Payload;
        }
        using (var expired = new SupplyStore(Path.Combine(directory, "station.db")))
        {
            var persisted = expired.Load(); persisted.Lanes["left"].PermitExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1); persisted.Revision++;
            expired.Commit(persisted, "SyntheticExpiredPermit", owner, "verification", "Explicit expiry fixture");
        }
        await using (var h = await Rig.Open(directory, cfg))
        {
            Assert((await h.App.SnapshotAsync()).OperationGuards["left"].Reasons.Contains("EntityPermitExpired"), "Expired entity permit projects a separate blocking reason");
            Assert(await h.App.ClaimAsync() is null, "New process/device epoch requires physical permit revalidation before dispatch");
            await h.Event("left", "Revalidate", admission);
            var claim = (await h.App.ClaimAsync())!.Value;
            await h.App.SetDeviceAsync(cfg.Devices[0].DeviceId, h.Epochs["left"], DeviceHealth.Faulted);
            Assert(!await h.App.BeforeSendAsync(claim.Request, claim.Attempt), "Device fault between T2 and network blocks dispatch with NotDelivered proof");
            await h.App.SetDeviceAsync(cfg.Devices[0].DeviceId, "stale-epoch", DeviceHealth.Ready);
            Assert((await h.App.SnapshotAsync()).State.Devices["left"].Health == DeviceHealth.Faulted, "Stale health cannot replace current epoch");
            await h.App.SetDeviceAsync(cfg.Devices[0].DeviceId, h.Epochs["left"], DeviceHealth.Ready);
            claim = (await h.App.ClaimAsync())!.Value; Assert(await h.App.BeforeSendAsync(claim.Request, claim.Attempt), "Revalidated current source permit");
            var response = await h.Port.ExecuteAsync(claim.Request, CancellationToken.None);
            using (var db = new SqliteConnection("Data Source=" + h.Store.Path))
            { db.Open(); using var c = db.CreateCommand(); c.CommandText = "CREATE TRIGGER fail_response BEFORE INSERT ON responses BEGIN SELECT RAISE(ABORT,'SIMULATION T3 failure'); END;"; c.ExecuteNonQuery(); }
            await h.App.CompleteAsync(claim.Request, claim.Attempt, response);
            Assert(h.Store.Request(requestId)!.Status == RequestStatus.Dispatching && h.Store.AcceptedQuantity(claim.Request.SessionId) == 0 && !(await h.App.SnapshotAsync()).Admit.Allowed, "T3 SQL rollback keeps local ledger separate from received remote acceptance");
            using (var db = new SqliteConnection("Data Source=" + h.Store.Path))
            { db.Open(); using var c = db.CreateCommand(); c.CommandText = "DROP TRIGGER fail_response"; c.ExecuteNonQuery(); }
        }
        await using (var h = await Rig.Open(directory, cfg))
        {
            var request = h.Store.Request(requestId)!; Assert(request.Status == RequestStatus.Accepted && request.Payload == frozen && h.Store.Responses(request.Id).Length == 1 && await h.App.ClaimAsync() is null,
                "Interrupted T3 completes from durable raw custody with immutable original and no resend");
            await h.App.CompleteAsync(request, null, (await h.Port.QueryAsync(request, CancellationToken.None))!);
            Assert(h.Store.AcceptedQuantity(request.SessionId) == 1, "Actual SQL failure reconciles once from independent remote ledger");
        }
    });
    await Check("AC-10 actual observation queue full preserves accepted events and overflow marker", async () =>
    {
        var cfg = Rig.Config(url); var directory = Path.Combine(root, "queue-full");
        using var store = new SupplyStore(Path.Combine(directory, "station.db"));
        var injection = new FaultInjection(); await using var app = new StationApplication(store, cfg, new SimulationRequestContract(cfg), injection);
        var left = new ManualObservationSource(cfg.Devices[0].DeviceId); var right = new ManualObservationSource(cfg.Devices[1].DeviceId);
        await using var module = new ObservationModule(app, [left, right], 1);
        await module.StartAsync(CancellationToken.None);
        var s = await app.SnapshotAsync(); await app.ExecuteAsync(new(Wire.Id(), s.State.Revision, "Login", Value: "maintenance"));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int once = 0;
        injection.Hook = point => { if (point == "T1" && Interlocked.Increment(ref once) == 1) { started.SetResult(); release.Task.GetAwaiter().GetResult(); } };
        Assert(left.Emit("Enter", "first-entity"), "First event accepted by bounded source queue"); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert(right.Emit("Enter", "second-entity"), "Second event occupies queue capacity");
        Assert(!left.Emit("Read", "first-entity", ["000001"], 1), "Third event explicitly not admitted");
        release.SetResult(); await module.StopAsync(CancellationToken.None);
        Assert(File.Exists(Path.Combine(directory, "overload-marker")), "Independent overflow evidence survives stop");
        Assert((await app.SnapshotAsync()).RuntimeFaults.Any(x => x.StartsWith("Overload")), "Overload remains latched for controlled recovery");
        using var db = new SqliteConnection("Data Source=" + store.Path); db.Open(); using var count = db.CreateCommand(); count.CommandText = "SELECT count(*) FROM observations";
        Assert(Convert.ToInt32(count.ExecuteScalar()) == 2, "Both admitted events persisted, never silently discarded");
    });
    await Check("AC-04/06 guarded pre-network cancellation allows same-ID second attempt", async () =>
    {
        await using var h = await Rig.New(root, url); await h.Command("OpenContext", value: "000001");
        var claim = (await h.App.ClaimAsync())!.Value; await h.Command("Pause");
        Assert(!await h.App.BeforeSendAsync(claim.Request, claim.Attempt), "Changed guard proves request not delivered to network");
        await h.Command("Resume"); await h.Drain(); var r = h.Store.Requests().Single(); var attempts = h.Store.Attempts(r.Id);
        Assert(r.Id == claim.Request.Id && attempts.Length == 2 && attempts[0].Disposition == "NotDelivered" && attempts[1].Disposition == "Accepted", "Second attempt retains original request identity and payload");
    });
    await Check("AC-01/06/09 config validation/activation and backup quarantine", async () =>
    {
        await using var h = await Rig.New(root, url);
        var original = h.ConfigValue;
        await h.Command("SaveConfig", value: Wire.Encode(original with { Version = "SIM-2", TimeoutMs = 1 })); Assert((await h.App.ConfigsAsync()).First().Status == "Rejected", "Invalid draft retained, original effective");
        var changed = original with { Version = "SIM-3", Integration = original.Integration with { Revision = "SIM-2", Endpoint = "http://127.0.0.1:18744/" } };
        await h.Command("SaveConfig", value: Wire.Encode(changed)); var revision = (await h.App.ConfigsAsync()).First();
        await h.Command("ActivateConfig", revision.Id); Assert((await h.App.ConfigsAsync()).First().Status == "PendingActivation" && h.ConfigValue == original, "Activation waits for controlled restart");
        var backup = Path.Combine(root, "consistent-backup.db"); await h.App.BackupAsync(backup);
        var restore = Path.Combine(root, "restored"); Directory.CreateDirectory(restore);
        SupplyStore.RestoreVerified(backup, Path.Combine(restore, "station.db"));
        await Throws<FileNotFoundException>(() => { SupplyStore.RestoreVerified(Path.Combine(root, "absent-backup.db"), Path.Combine(restore, "invented.db")); return Task.CompletedTask; });
        File.WriteAllText(Path.Combine(restore, "restore-quarantine"), "SIMULATION restored snapshot");
        await using var restored = await Rig.Open(restore, original); Assert((await restored.App.SnapshotAsync()).State.Quarantined && await restored.App.ClaimAsync() is null, "Restored snapshot cannot auto dispatch");
        await restored.Command("ClearQuarantine", value: "SIMULATION receiver inventory inspected; no outstanding effect");
        Assert(!(await restored.App.SnapshotAsync()).State.Quarantined, "Evidence recorded before releasing quarantine");
    });
    await Check("AC-09 restored Prepared may already exist remotely, retention preserves current aggregate", async () =>
    {
        await using var h = await Rig.New(root, url);
        await h.Command("OpenContext", value: "000001"); var original = h.Store.Requests().Single();
        var backup = Path.Combine(root, "prepared-before-remote.db"); await h.App.BackupAsync(backup); await h.Drain();
        var directory = Path.Combine(root, "lagging-restored"); Directory.CreateDirectory(directory);
        SupplyStore.RestoreVerified(backup, Path.Combine(directory, "station.db")); File.WriteAllText(Path.Combine(directory, "restore-quarantine"), "restore");
        await using (var restored = await Rig.Open(directory, h.ConfigValue))
        {
            var retained = restored.Store.Request(original.Id)!;
            Assert(retained.Status == RequestStatus.Unknown && await restored.App.ClaimAsync() is null, "Restored Prepared becomes Unknown; no replay against external world");
            await restored.App.CompleteAsync(retained, null, (await restored.Port.QueryAsync(retained, CancellationToken.None))!);
            await restored.Command("ClearQuarantine", value: "SIMULATION receiver context and inventory reconciled");
        }
        async Task Close()
        {
            await h.Command("CheckClose"); await h.Drain(); var s = (await h.App.SnapshotAsync()).State.Session!;
            await h.Command("ConfirmClose", s.CloseCheckRequest, "CONFIRM"); await h.Drain();
        }
        await Close(); await h.Command("OpenContext", value: "000002"); await h.Drain(); await Close();
        using (var db = new SqliteConnection("Data Source=" + h.Store.Path))
        {
            db.Open(); using var c = db.CreateCommand(); c.CommandText = "UPDATE sessions SET json=json_set(json,'$.ClosedAt',$old) WHERE id=$id";
            c.Parameters.AddWithValue("$old", DateTimeOffset.UtcNow.AddDays(-10).ToString("O")); c.Parameters.AddWithValue("$id", original.SessionId); c.ExecuteNonQuery();
        }
        Assert(await h.App.PruneHistoryAsync(1, "SIMULATION retention approved for expired synthetic sessions") == 1, "Only expired closed aggregate pruned");
        Assert(h.Store.Request(original.Id) is null && h.Store.Requests().Length == 3 && h.Store.Audits("retention").Length == 1, "Current context/request history and retention audit remain");
    });
    await Check("AC-01/03/10 source replacement, capacity and resource stop", async () =>
    {
        var file = Path.Combine(root, "replay.jsonl"); File.WriteAllText(file, Wire.Encode(new ReplayEvent("Enter", "entity", [], null, "event")).Replace("\n", "") + "\n");
        var accepted = new List<Observation>();
        await using (var replay = new FileReplaySource("replay-device", file))
        { await replay.StartControlledAsync(Wire.Id(), x => { accepted.Add(x); return true; }, _ => { }, CancellationToken.None); Assert(replay.Advance(), "Replay uses observation capability"); await replay.StopAsync(CancellationToken.None); }
        await using (var manual = new ManualObservationSource("manual-device"))
        { await manual.StartControlledAsync(Wire.Id(), x => { accepted.Add(x); return false; }, _ => { }, CancellationToken.None); Assert(!manual.Emit("Enter", "different-entity"), "Rejected queue admission reported to source"); await manual.StopAsync(CancellationToken.None); await Throws<InvalidOperationException>(() => Task.FromResult(manual.Emit("Read", "entity"))); }
        Assert(accepted.Count == 2 && accepted[0].DeviceId != accepted[1].DeviceId && accepted[0].Epoch != accepted[1].Epoch, "Sources retain independent identities/epochs");
        CurrentContractVerification.CorruptDatabaseRefusal(root);
    });
    }
    Console.WriteLine("PASS selected verification scope " + selector + "; Windows desktop/SDK/field validation NOT_RUN by this core executable");
    Console.WriteLine("Evidence data retained at " + root);
}
finally { if (!fixture.HasExited) { fixture.Kill(entireProcessTree: true); await fixture.WaitForExitAsync(); } }

static async Task Check(string label, Func<Task> run) { Console.WriteLine("RUN " + label); await run(); Console.WriteLine("PASS " + label); }
static void Assert(bool condition, string reason) { if (!condition) throw new InvalidOperationException("ASSERT: " + reason); }
static async Task Throws<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
static async Task Eventually(Func<Task<bool>> condition)
{ for (var i = 0; i < 100; i++) { if (await condition()) return; await Task.Delay(50); } throw new TimeoutException("Fixture failed to start"); }
static Process Start(string dotnet, params string[] args)
{
    var info = new ProcessStartInfo(dotnet) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    foreach (var arg in args) info.ArgumentList.Add(arg);
    info.Environment["DOTNET_DbgEnableMiniDump"] = "0";
    var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start child");
    // Drain bounded OS pipes; retain only crash classification, never raw payloads in test output.
    process.OutputDataReceived += (_, _) => { }; process.ErrorDataReceived += (_, _) => { }; process.BeginOutputReadLine(); process.BeginErrorReadLine(); return process;
}
static ProductManifest Manifest(string install) => new(1, ReferenceProduct.Definition.ProductId, RunMode.Simulation, ReferenceProduct.Definition.Runtime, "any", ReferenceProduct.Definition.BusinessModule, ["workstation", "history", "alarms", "diagnostics"],
    ReferenceProduct.Definition.Modules.Select(x => new ModuleSpec(x.Id, x.Kind, x.Version, x.ContractMajor, x.Dependencies, x.File, Wire.FileHash(Path.Combine(install, x.File)))).ToArray());

sealed class TestModule(string id, string[] dependencies, List<string> stopped, bool fail = false) : IRuntimeModule
{
    public string Id => id; public string[] Dependencies => dependencies;
    public Task StartAsync(CancellationToken token) => fail ? throw new InvalidOperationException("Synthetic initialization failure") : Task.CompletedTask;
    public Task StopAsync(CancellationToken token) { stopped.Add(id); return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
sealed class Rig : IAsyncDisposable
{
    public required SupplyStore Store { get; init; }
    public required StationApplication App { get; init; }
    public required StationConfig ConfigValue { get; init; }
    public required IIntegrationPort Port { get; init; }
    public TimeProvider Clock { get; init; } = TimeProvider.System;
    public bool AutoConfirmSyntheticHealth { get; set; } = true;
    public Dictionary<string, string> Epochs { get; } = new() { ["left"] = Wire.Id(), ["right"] = Wire.Id() };
    readonly Dictionary<string, long> sequence = new() { ["left"] = 0, ["right"] = 0 };
    public static StationConfig Config(string url, int demand = 120, int timeout = 500) => new(1, "SIM-" + Wire.Id(), "SIM-1", RunMode.Simulation,
        [new("left", "manual", "L-" + Wire.Id(), "1"), new("right", "manual", "R-" + Wire.Id(), "1")], new("loopback", "1", "SIMULATION-v1", url), TimeoutMs: timeout, FixtureDemand: demand);
    public static Task<Rig> New(string root, string url, int demand = 120, int timeout = 500) => Open(Path.Combine(root, Wire.Id()), Config(url, demand, timeout));
    public static async Task<Rig> Open(string directory, StationConfig config, FaultInjection? faults = null, TimeProvider? clock = null)
    {
        var store = new SupplyStore(Path.Combine(directory, "station.db")); var app = new StationApplication(store, config, new SimulationRequestContract(config), faults, clock);
        var h = new Rig { Store = store, App = app, ConfigValue = config, Port = new LoopbackIntegration(config), Clock = clock ?? TimeProvider.System };
        // Child crash injection is armed only after startup/identity setup, at the business window.
        var crash = faults?.CrashAt; if (faults is not null) faults.CrashAt = null;
        foreach (var d in config.Devices) await app.SetDeviceAsync(d.DeviceId, h.Epochs[d.Role], DeviceHealth.Ready, h.Clock);
        await h.Command("Login", value: "maintenance"); if (faults is not null) faults.CrashAt = crash;
        return h;
    }
    public async Task<CommandReceipt> Command(string kind, string? owner = null, string? value = null, string expect = "Persisted")
    {
        if (AutoConfirmSyntheticHealth) await ConfirmSyntheticHealth();
        var s = await App.SnapshotAsync(); var r = await App.ExecuteAsync(new(Wire.Id(), s.State.Revision, kind, owner, value, EvidenceVersion: kind == "ClearOverload" ? s.OverloadVersion : null));
        if (r.Status != expect) throw new InvalidOperationException($"{kind}: expected {expect}; got {r.Status}: {r.Reason}"); return r;
    }
    public async Task OpenBox() { await Command("OpenContext", value: "000001"); await Drain(); if ((await App.SnapshotAsync()).State.Session?.Status != SessionStatus.Active) throw new InvalidOperationException("Box not active"); }
    public async Task<SupplyOperation> Enter(string lane) { await Event(lane, "Enter", Wire.Id()); return await Op(lane); }
    public async Task Event(string lane, string kind, string admission, string[]? codes = null, int? quantity = null, string? source = null)
    {
        if (AutoConfirmSyntheticHealth) await ConfirmSyntheticHealth();
        var d = ConfigValue.Devices.Single(x => x.Role == lane); await App.ReceiveAsync(new(Wire.Id(), d.DeviceId, Epochs[lane], source ?? Wire.Id(), ++sequence[lane], admission, kind, codes ?? [], quantity, Clock.GetUtcNow()));
    }
    public async Task<SupplyOperation> Op(string lane) => (await App.SnapshotAsync()).State.Lanes[lane];
    public async Task Drain()
    {
        if (AutoConfirmSyntheticHealth) await ConfirmSyntheticHealth();
        for (var i = 0; i < 100; i++)
        {
            var claim = await App.ClaimAsync(); if (claim is null) return;
            var (request, attempt) = claim.Value; if (!await App.BeforeSendAsync(request, attempt)) continue;
            try { await App.CompleteAsync(request, attempt, await Port.ExecuteAsync(request, CancellationToken.None)); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { await App.UnknownAsync(request, attempt, ex.GetType().Name); }
        }
        throw new InvalidOperationException("Outbox did not converge");
    }
    // Deliberate synthetic source events at scenario boundaries. Snapshot reads never
    // renew health. Preserve injected faults, registered source and epoch identity.
    public async Task ConfirmSyntheticHealth()
    {
        var devices = (await App.SnapshotAsync()).State.Devices;
        foreach (var device in devices.Values.Where(x => x.Health == DeviceHealth.Ready))
        {
            var now = Clock.GetUtcNow();
            await App.SetDeviceHealthAsync(new(device.DeviceId, device.Epoch, DeviceHealth.Ready, now, now.AddSeconds(5), device.Source));
        }
        await App.SetWcsHealthAsync(await Port.HealthAsync(CancellationToken.None));
    }
    public async Task ReviewSources(string evidence)
    {
        var snapshot = await App.SnapshotAsync();
        var selection = snapshot.State.Blocks.Where(x => x.Code == "ObservationReview")
            .Select(x => new ObservationReviewSelection(x.Code, x.Scope, x.Source, x.SourceEpoch, x.EvidenceVersion)).ToArray();
        var receipt = await App.ExecuteAsync(new(Wire.Id(), snapshot.State.Revision, "ReviewObservations", Value: Wire.Encode(new ObservationReviewEvidence(selection, evidence))));
        if (receipt.Status != "Persisted") throw new InvalidOperationException("Versioned source review failed: " + receipt.Reason);
    }
    public async ValueTask DisposeAsync() { await App.StopAsync(); await App.DisposeAsync(); await Port.DisposeAsync(); Store.Dispose(); }
}
