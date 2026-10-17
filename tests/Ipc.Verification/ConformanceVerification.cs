using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Ipc.Adapters;
using Ipc.Desktop;
using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

static class ConformanceVerification
{
    public static string Format4Checksum => Wire.Hash("""
        CREATE TABLE response_conflicts(id TEXT PRIMARY KEY,request TEXT NOT NULL REFERENCES requests(id),json TEXT NOT NULL);
        CREATE INDEX response_conflicts_request ON response_conflicts(request);
        UPDATE storage_format SET version=4,minimum_reader=4 WHERE id=1;
        """);
    static void Assert(bool value, string reason) { if (!value) throw new Exception("AC14: " + reason); Console.WriteLine("PASS AC14 " + reason); }
    static void Sql(string path, string sql)
    { using var db = new SqliteConnection("Data Source=" + path); db.Open(); using var command = db.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
    static async Task Wait(Func<Task<bool>> condition)
    { var watch = Stopwatch.StartNew(); while (!await condition()) { if (watch.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("AC14 condition"); await Task.Delay(25); } }
    static void Refuse(Action action, string reason)
    { try { action(); } catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException or System.Text.Json.JsonException) { Console.WriteLine("PASS AC14 refused " + reason); return; } throw new Exception("AC14 accepted " + reason); }
    public static async Task Run(string root, string url, string workspace)
    {
        root = Path.Combine(root, "conformance-14"); Directory.CreateDirectory(root);
        await Conflicts(root, url);
        await LostReceiptCustody(root, url);
        await DraftAndInput(root, url, workspace);
        await Notifications(root, url);
        await HeadlessOwnership(root, url);
        await HealthAndConsumers(root, url, workspace);
        await AlignmentDataVerification.LegacyMigration(root, workspace);
        Console.WriteLine("PASS AC14 CONFORMANCE COMPLETE: A01-A04/R01-R03 core; WINDOWS_NOT_RUN");
    }
    static async Task Conflicts(string root, string url)
    {
        foreach (var commitFailure in new[] { false, true })
        {
            var cfg = Rig.Config(url, 500); var directory = Path.Combine(root, "conflict-" + commitFailure);
            BusinessRequest original; IntegrationResponse accepted; string ownerId, oldSession;
            await using (var h = await Rig.Open(directory, cfg))
            {
                await h.OpenBox(); var op = await h.Enter("left"); ownerId = op.Id; oldSession = op.SessionId;
                await h.Event("left", "Read", op.AdmissionId, ["000099"], 1); await h.Drain();
                await h.Command("Submit", op.Id); await h.Drain();
                var detail = (await h.App.DetailsAsync(op.Id)).Single(x => x.Request.Kind == RequestKind.Submit);
                original = detail.Request; accepted = detail.Responses.Single();
                await h.App.CompleteAsync(original, null, accepted);
                Assert(h.Store.Conflicts(original.Id).Length == 0 && h.Store.AcceptedQuantity(oldSession) == 1, "same ID/content duplicates preserve unique acceptance without conflict");
                // Move to a new box before a late response: neither the incoming receipt nor
                // its conflict is allowed to change the new box's operation or counter.
                if (!commitFailure)
                {
                    await h.Event("left", "Depart", op.AdmissionId); await h.Event("left", "Reset", op.AdmissionId);
                    await h.Command("CheckClose"); await h.Drain();
                    var session = (await h.App.SnapshotAsync()).State.Session!;
                    await h.Command("ConfirmClose", session.CloseCheckRequest, "CONFIRM"); await h.Drain();
                    await h.Command("OpenContext", value: "000002"); await h.Drain();
                }
                var priorAttempts = Wire.Encode(h.Store.Attempts(original.Id));
                var contrary = accepted with { Result = "Rejected", Reason = "SYNTHETIC contradiction of original acceptance" };
                if (commitFailure) Sql(h.Store.Path, "CREATE TRIGGER reject_conflict BEFORE INSERT ON response_conflicts BEGIN SELECT RAISE(ABORT,'AC14 real SQLite T3 failure'); END;");
                await h.App.CompleteAsync(original, null, contrary);
                var snap = await h.App.SnapshotAsync();
                if (commitFailure)
                {
                    Assert(h.Store.Conflicts(original.Id).Length == 0 && h.Store.Load().Blocks.All(x => x.Code != "ConflictingEvidence") && snap.RuntimeFaults.Any(x => x.StartsWith("StorageFailure")), "actual SQLite rollback is not reported as durable conflict or success");
                    Assert(Directory.GetFiles(Path.Combine(directory, "receipt-ingress")).Length == 1 && !snap.Admit.Allowed && await h.App.ClaimAsync() is null, "failed T3 retains flushed raw receipt custody and blocks dispatch");
                    Refuse(() => SupplyStore.BackupReadOnly(h.Store.Path, Path.Combine(directory, "unsafe-backup.db")), "database-only backup cannot drop uncommitted raw receipt custody");
                    Sql(h.Store.Path, "DROP TRIGGER reject_conflict;");
                }
                else
                {
                    await h.App.CompleteAsync(original, null, contrary);
                    var distinctId = Wire.Id();
                    await h.App.CompleteAsync(original, null, contrary with { EvidenceId = distinctId, Reason = "Distinct contradictory evidence",
                        Task = contrary.Task is null ? null : contrary.Task with { Provenance = contrary.Task.Provenance with { EvidenceId = distinctId } } });
                    Assert(h.Store.Conflicts(original.Id).Length == 2 && h.Store.Responses(original.Id).Single() == accepted, "same-ID altered content and different-ID contradiction retain deduplicated raw occurrences and immutable original response");
                    Assert(snap.State.Session!.Id != oldSession && snap.AcceptedQuantity == 0 && snap.State.Lanes.Count == 0, "late old-box conflict neither adopts old owner into new box nor changes its counter");
                }
                Assert(Wire.Encode(h.Store.Attempts(original.Id)) == priorAttempts, "late receipt does not rewrite original completed transport attempt");
                Assert(h.Store.Request(original.Id) == original && h.Store.AcceptedQuantity(oldSession) == 1, "terminal request/payload/hash/binding/acceptance fact remain exact");
            }
            await using (var reopened = await Rig.Open(directory, cfg))
            {
                await reopened.ConfirmSyntheticHealth();
                var snap = await reopened.App.SnapshotAsync();
                Assert(!snap.Admit.Allowed && !snap.Submit.Allowed && !snap.Close.Allowed && snap.Recovery.Any(x => x.Request.Id == original.Id) && (await reopened.App.UnknownRequestsAsync()).Any(x => x.Id == original.Id), "restart rediscovers terminal request reconciliation through real recovery readers and all business guards");
                Assert(reopened.Store.Conflicts(original.Id).Length == (commitFailure ? 1 : 2) && reopened.Store.Request(original.Id) == original && reopened.Store.AcceptedQuantity(oldSession) == 1, "restart replays receipt custody atomically without rewriting or recounting acceptance");
                var detail = (await reopened.App.DetailsAsync(original.Id)).Single();
                Assert(detail.Conflicts.All(x => x.OriginalEvidenceId == x.Response.EvidenceId && x.ContentSha256 == Wire.Hash(Wire.Encode(x.Response)) && x.ReceivedAt <= DateTimeOffset.UtcNow) && detail.Conflicts.Length > 0, "history carries original raw evidence identity/source/time/hash");
                Assert(SupplyStore.InspectReadOnly(reopened.Store.Path).PendingRequests == 1 && reopened.Store.PruneClosedSessions(DateTimeOffset.UtcNow.AddDays(1), "synthetic", "retain contradictory history") == 0, "maintenance and retention cannot omit terminal unresolved responsibility");
                var draft = cfg with { Version = "CONFLICT-BLOCKED" }; await reopened.Command("SaveConfig", value: Wire.Encode(draft));
                var revision = (await reopened.App.ConfigsAsync()).Single(x => x.Content.Version == draft.Version);
                await reopened.Command("ActivateConfig", revision.Id, expect: "RejectedByGuard");
                await reopened.App.CompleteAsync(original, null, accepted);
                Assert(reopened.Store.PendingCount() == 1 && !ConfigurationActivation.Evaluate((await reopened.App.SnapshotAsync()).State, revision, cfg.Version, true, DateTimeOffset.UtcNow).Allowed, "a later matching query does not erase contradiction or provide a generic unlock");
            }
        }
    }
    static async Task LostReceiptCustody(string root, string url)
    {
        var cfg = Rig.Config(url); var directory = Path.Combine(root, "receipt-custody-unwritable"); BusinessRequest original;
        await using (var h = await Rig.Open(directory, cfg))
        {
            await h.OpenBox(); original = h.Store.Requests().Single(); var response = h.Store.Responses(original.Id).Single();
            var receipts = Path.Combine(directory, "receipt-ingress"); Directory.Delete(receipts); File.WriteAllText(receipts, "synthetic unusable receipt directory");
            await h.App.CompleteAsync(original, null, response with { Result = "Rejected", Reason = "Unable to take raw custody" });
            Assert((await h.App.SnapshotAsync()).RuntimeFaults.Any(x => x.StartsWith("StorageFailure")) && h.Store.Conflicts(original.Id).Length == 0, "unwritable receipt custody reports failure without claiming raw evidence was saved");
        }
        File.Delete(Path.Combine(directory, "receipt-ingress"));
        await using var recovered = await Rig.Open(directory, cfg);
        var snapshot = await recovered.App.SnapshotAsync();
        Assert(snapshot.State.Blocks.Any(x => x.Code == "ReceiptPersistenceUncertain" && x.Source == original.Id) && !snapshot.Admit.Allowed && snapshot.Recovery.Any(x => x.Request.Id == original.Id) && (await recovered.App.UnknownRequestsAsync()).Any(x => x.Id == original.Id), "pre-existing durable intake fence prevents silent restart release when raw custody itself failed");
        Assert(recovered.Store.Request(original.Id) == original && recovered.Store.Conflicts(original.Id).Length == 0 && SupplyStore.InspectReadOnly(recovered.Store.Path).PendingRequests == 1, "uncaptured raw receipt is explicitly uncertain; original terminal fact and maintenance responsibility retained");
    }
    static async Task DraftAndInput(string root, string url, string workspace)
    {
        var cfg = Rig.Config(url); await using var h = await Rig.Open(Path.Combine(root, "draft-ui-path"), cfg);
        var draft = new ConfigurationDraft(); draft.Replace(cfg);
        var imported = cfg with { Version = "IMPORTED", Integration = cfg.Integration with { Id = "imported", Revision = "target-r2" },
            Extensions = new([], [], [], [new("requests", "source", cfg.Integration with { Id = "imported", Revision = "target-r2" })]) };
        draft.Replace(imported); draft["Version"] = "EDITED"; draft["Endpoint"] = cfg.Integration.Endpoint; draft.Devices[0].DeviceId = "changed-device";
        var beforeInvalid = Wire.Encode(draft.Build());
        Refuse(() => draft.Import("{bad-json", _ => []), "malformed import preserves existing draft");
        Refuse(() => draft.Import(Wire.Encode(cfg), _ => ["invalid selected input"]), "invalid import preserves existing draft");
        Assert(Wire.Encode(draft.Build()) == beforeInvalid, "failed import cannot partially overwrite any draft field");
        var expected = draft.Build(); var saved = await h.Command("SaveConfig", value: Wire.Encode(expected));
        var revision = (await h.App.ConfigsAsync()).Single(x => x.Id == saved.OwnerId);
        Assert(Wire.Encode(revision.Content) == Wire.Encode(expected) && cfg.Version == "SIM-1" && draft.Extensions.Ports!.Single().Target.Id == "imported", "import/cross-page fields/requests target/preview/save/readback use one draft; Effective remains unchanged");
        var state = (await h.App.SnapshotAsync()).State;
        var allowed = ConfigurationActivation.Evaluate(state, revision with { Status = "Validated" }, cfg.Version, false, DateTimeOffset.UtcNow);
        Assert(allowed.Allowed && !ConfigurationActivation.Evaluate(state, null, cfg.Version, false, DateTimeOffset.UtcNow).Allowed &&
            !ConfigurationActivation.Evaluate(state, revision with { Status = "Rejected" }, cfg.Version, false, DateTimeOffset.UtcNow).Allowed &&
            !ConfigurationActivation.Evaluate(state, revision with { BaseVersion = "old" }, cfg.Version, false, DateTimeOffset.UtcNow).Allowed, "activation uses this selection and current business context without hidden-page state");
        // The imported uninstalled binding is deliberately Rejected. Exercise the same
        // real save/activation authority with a complete valid draft next.
        await h.Command("SaveConfig", value: Wire.Encode(cfg with { Version = "VALID-UI" }));
        var valid = (await h.App.ConfigsAsync()).Single(x => x.Content.Version == "VALID-UI");
        var reviewed = await h.App.SnapshotAsync();
        var oldWindow = new Command(Wire.Id(), reviewed.State.Revision, "ActivateConfig", valid.Id);
        await h.Command("SaveConfig", value: Wire.Encode(cfg with { Version = "WINDOW-B" }));
        Assert((await h.App.ExecuteAsync(oldWindow)).Status == "Conflict", "two-window CAS rejects reviewed activation after competing save");
        await h.Command("Login", value: "operator"); await h.Command("ActivateConfig", valid.Id, expect: "RejectedByGuard");
        await h.Command("Login", value: "maintenance");
        await h.Command("ActivateConfig", valid.Id);
        Assert((await h.App.ConfigsAsync()).Single(x => x.Id == valid.Id).Status == "PendingActivation", "direct selected revision reaches real activation authority and PendingActivation");
        var code = File.ReadAllText(Path.Combine(workspace, "src/Ipc.Desktop/MainWindow.xaml.cs"));
        Assert(code.Contains("ConfigurationActivation.Evaluate(snapshot.State, activation") && !code.Contains("kind == \"ActivateConfig\" && (!ConfigsList") && code.Contains("Draft.Build()") &&
            File.ReadAllText(Path.Combine(workspace, "src/Ipc.Desktop/ExtensionsView.xaml.cs")).Contains("get => owner!.Draft.Extensions"), "actual WPF callers are wired to shared activation and draft implementations exercised here");
        // Real ScanRetained commands plus the exact draft-consumption implementation used
        // by LaneControl: duplicate A has no quantity effect; B is a separate retained unit.
        await using var split = await Rig.New(root, url, demand: 2);
        await split.OpenBox(); var op = await split.Enter("left"); await split.Event("left", "Read", op.AdmissionId, ["000123"], 4); await split.Drain();
        var input = new ScanInputDraft(); input.SelectOwner(op.Id);
        foreach (var barcode in new[] { "A", "A", "B" })
        {
            input.Text = barcode + "\t"; var submission = input.Capture(Wire.Id()); var current = await split.App.SnapshotAsync();
            var receipt = await split.App.ExecuteAsync(new(submission.CommandId, current.State.Revision, "ScanRetained", op.Id, submission.Text));
            Assert(input.Consume(submission, submission.CommandId, receipt) && input.Text == "", "persisted scan consumes only its own Enter/Tab input " + barcode);
        }
        Assert((await split.Op("left")).SplitScans.SetEquals(["A", "B"]), "A/A/B counts two identities through actual application");
        input.Text = "old"; var sent = input.Capture(Wire.Id()); input.Text = "new";
        Assert(!input.Consume(sent, sent.CommandId, new("Persisted", "", 1, op.Id)) && input.Text == "new", "late receipt cannot clear newer input");
        var rejected = input.Capture(Wire.Id()); Assert(!input.Consume(rejected, rejected.CommandId, new("RejectedByGuard", "", 1, op.Id)) && input.Text == "new", "rejection retains scan draft");
        input.SelectOwner("next-operation"); input.Text = "new owner";
        Assert(!input.Consume(sent, sent.CommandId, new("Persisted", "", 1, op.Id)) && input.Text == "new owner", "switching jobs detaches old input and late acknowledgement");
    }
    static async Task Notifications(string root, string url)
    {
        await using var h = await Rig.New(root, url, demand: 1000); await h.OpenBox();
        var delivered = new List<OperatorNotification>();
        await using var consumer = new HeadlessNotifications(h.App, (cue, token) => { lock (delivered) delivered.Add(cue); return Task.CompletedTask; }, reason => h.App.Latch("OperatorNotificationDeliveryFailure", reason));
        for (int i = 0; i < 66; i++)
        {
            var op = await h.Enter("left"); await h.Event("left", "Read", op.AdmissionId, ["000123"], 1); await h.Drain(); await h.Command("Submit", op.Id); await h.Drain();
            await Wait(() => { lock (delivered) return Task.FromResult(delivered.Count == i + 1); });
            OperatorNotification cue; lock (delivered) cue = delivered.Last();
            Assert(cue.OperationId == op.Id, "headless delivered current operation " + i);
            await consumer.AcknowledgeAsync(cue.Sequence);
            await h.Event("left", "Depart", op.AdmissionId); await h.Event("left", "Reset", op.AdmissionId);
        }
        var snapshot = await h.App.SnapshotAsync();
        Assert(delivered.Count == 66 && snapshot.NotificationBacklog == 0 && snapshot.NotificationOverflow == 0 && snapshot.Admit.Allowed, "explicit delivery+sequence ack sustains beyond 64/65 without dropping queue protection");
        await consumer.StopAsync();
        var failing = new HeadlessNotifications(h.App, (_, _) => throw new IOException("synthetic downstream failure"), reason => h.App.Latch("OperatorNotificationDeliveryFailure", reason));
        var last = await h.Enter("left"); await h.Event("left", "Read", last.AdmissionId, ["000123"], 1); await h.Drain(); await h.Command("Submit", last.Id); await h.Drain();
        try { await failing.Worker; } catch (IOException) { }
        snapshot = await h.App.SnapshotAsync();
        Assert(!snapshot.Admit.Allowed && snapshot.NotificationBacklog == 1 && snapshot.RuntimeFaults.Any(x => x.StartsWith("OperatorNotificationDeliveryFailure")), "consumer failure retains unacknowledged cue and blocks new work");
        try { await failing.DisposeAsync(); } catch (IOException) { }
        // failure is intentionally observed; Dispose repeats it, so explicitly handled below
    }
    static async Task HeadlessOwnership(string root, string url)
    {
        var config = Rig.Config(url); var area = Path.Combine(root, "headless-ownership"); var cfg = Path.Combine(area, "config"); Directory.CreateDirectory(cfg);
        var install = Path.GetDirectoryName(typeof(ProductRuntime).Assembly.Location)!; var definition = ReferenceProduct.Definition;
        var manifest = new ProductManifest(1, definition.ProductId, config.Mode, definition.Runtime, "any", definition.BusinessModule, definition.Views,
            definition.Modules.Select(x => new ModuleSpec(x.Id, x.Kind, x.Version, x.ContractMajor, x.Dependencies, x.File, Wire.FileHash(Path.Combine(install, x.File)))).ToArray());
        File.WriteAllText(Path.Combine(cfg,"product.json"), Wire.Encode(manifest)); File.WriteAllText(Path.Combine(cfg,"station.json"), Wire.Encode(config)); File.WriteAllText(Path.Combine(cfg,"presentation.json"),Wire.Encode(new PresentationSettings()));
        var boot = new BootOptions(Path.Combine(cfg,"product.json"),Path.Combine(cfg,"station.json"),Path.Combine(cfg,"presentation.json"),Path.Combine(area,"data"),install,Path.Combine(area,"leases"));
        var runtime = new ProductRuntime(boot, NullLogger<ProductRuntime>.Instance); await runtime.StartAsync(CancellationToken.None);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.StartHeadlessNotifications(async (_, _) => { entered.TrySetResult(); await release.Task; });
        async Task Command(string kind, string? owner = null, string? value = null)
        {
            var snapshot = await runtime.Application!.SnapshotAsync(); var result = await runtime.CommandAsync(new(Wire.Id(), snapshot.State.Revision, kind, owner, value));
            Assert(result.Status == "Persisted", "headless Product command " + kind);
        }
        await Wait(async () => (await runtime.Application!.SnapshotAsync()).State.IntegrationHealth?.IsValid(DateTimeOffset.UtcNow) == true);
        await Command("Login", value:"maintenance"); await Command("OpenContext",value:"000001");
        await Wait(async () => (await runtime.Application!.SnapshotAsync()).State.Session?.Status == SessionStatus.Active);
        await runtime.EmitAsync("left","Enter"); await Wait(async () => (await runtime.Application!.SnapshotAsync()).State.Lanes.ContainsKey("left"));
        await runtime.EmitAsync("left","Read",["000123"],1);
        await Wait(async () => (await runtime.Application!.SnapshotAsync()).State.Lanes["left"].Status == OperationStatus.AwaitingAction);
        var op = (await runtime.Application!.SnapshotAsync()).State.Lanes["left"]; await Command("Submit",op.Id);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        bool refused = false;
        try { await runtime.DisposeAsync(); } catch (AggregateException) { refused = true; }
        Assert(refused, "nonterminating delivery task prevents successful runtime disposal");
        Refuse(() => { using var lease = new ResourceLease(runtime.DataDirectory!, ["database"]); }, "notification owner still holds actual database lease");
        Assert((await runtime.Application.SnapshotAsync()).NotificationBacklog == 1, "cancelled undelivered cue is not falsely acknowledged");
        release.TrySetResult(); await Task.Delay(100); await runtime.DisposeAsync();
        using var released = new ResourceLease(runtime.DataDirectory!,["database"]);
        Assert(true,"terminated delivery releases application and database ownership on repeated cleanup");
    }
    static string SourceCommit(string workspace)
    {
        using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD") { WorkingDirectory = workspace, RedirectStandardOutput = true })!;
        var result = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("Git source identity unavailable"); return result;
    }
    static async Task HealthAndConsumers(string root, string url, string workspace)
    {
        var area = Path.Combine(root, "health-consumers"); Directory.CreateDirectory(area);
        var plugin = await ArchitectureClosureVerification.Build(workspace, area, "IntegrationExtension");
        using var key = RSA.Create(3072);
        var trust = new ExtensionTrust(1, [new("reference-test", key.ExportSubjectPublicKeyInfoPem(), true)]);
        var trustFile = Path.Combine(area, "trust.json"); File.WriteAllText(trustFile, Wire.Encode(trust));
        var manifest = ArchitectureClosureVerification.Manifest("ac14.integration", plugin, "Ipc.Reference.IntegrationExtension.IntegrationModule", "integration", SourceCommit(workspace));
        var archive = ArchitectureClosureVerification.Package(area, "integration", manifest, key, plugin);
        var leases = Path.Combine(area, "leases"); var manager = new ExtensionStore(Path.Combine(area, "store"), trust, RunMode.Simulation, leases);
        var installed = manager.Install(archive, Wire.FileHash(archive));
        var primary = new HealthEndpoint(); var auxiliary = new HealthEndpoint();
        await using var primaryOwner = primary; await using var auxiliaryOwner = auxiliary;
        var primaryTarget = new IntegrationBinding("primary", "p1", "SIMULATION-v1", primary.Url);
        var auxTarget = new IntegrationBinding("auxiliary", "a1", "SIMULATION-v1", auxiliary.Url);
        var config = Rig.Config(primary.Url) with { TimeoutMs = 300, Extensions = new([installed.Package], [],
            [new("main", installed.Package.Id, "reference-http", true, []), new("aux", installed.Package.Id, "reference-http", true, [])],
            [new("requests", "main", primaryTarget), new("auxiliary-status", "aux", auxTarget)]), Integration = primaryTarget };
        var install = Path.GetDirectoryName(typeof(ProductRuntime).Assembly.Location)!; var definition = ReferenceProduct.Definition;
        var hostManifest = new ProductManifest(1, definition.ProductId, config.Mode, definition.Runtime, "any", definition.BusinessModule, definition.Views,
            definition.Modules.Select(x => new ModuleSpec(x.Id, x.Kind, x.Version, x.ContractMajor, x.Dependencies, x.File, Wire.FileHash(Path.Combine(install, x.File)))).ToArray());
        var cfg = Path.Combine(area, "config"); Directory.CreateDirectory(cfg);
        File.WriteAllText(Path.Combine(cfg, "product.json"), Wire.Encode(hostManifest)); File.WriteAllText(Path.Combine(cfg, "station.json"), Wire.Encode(config)); File.WriteAllText(Path.Combine(cfg, "presentation.json"), Wire.Encode(new PresentationSettings()));
        auxiliary.Ready = false;
        await using (var runtime = new ProductRuntime(new(Path.Combine(cfg, "product.json"), Path.Combine(cfg, "station.json"), Path.Combine(cfg, "presentation.json"), Path.Combine(area, "data"), install, leases, ExtensionRoot: manager.Root, ExtensionTrustFile: trustFile), NullLogger<ProductRuntime>.Instance))
        {
            await runtime.StartAsync(CancellationToken.None); Assert(runtime.Diagnostics.Count == 0, "actual ProductRuntime starts with two independently installed service instances");
            var app = runtime.Application!;
            await Wait(async () => (await app.SnapshotAsync()).State.ServiceHealth.Count == 2);
            var state = await app.SnapshotAsync();
            Assert(state.State.IntegrationServiceHealth?.Status == ServiceStatus.Ready && !state.Admit.Reasons.Any(x => x.StartsWith("Wcs")) && runtime.IntegrationDiagnostics.Single(x => x.InstanceId == "aux").LastFailure is not null, "primary healthy/auxiliary bad attributes precise error without blocking requests dependency");
            primary.Ready = false; auxiliary.Ready = true;
            await Wait(async () => { var s = await app.SnapshotAsync(); return s.State.ServiceHealth["requests"].Status == ServiceStatus.Unavailable && s.State.ServiceHealth["auxiliary-status"].Status == ServiceStatus.Ready; });
            state = await app.SnapshotAsync(); Assert(!state.Admit.Allowed && state.Admit.Reasons.Any(x => x.StartsWith("Wcs")), "required requests dependency failure blocks actual business guard");
            primary.Ready = true; auxiliary.DelayMs = 1000;
            await Wait(async () => (await app.SnapshotAsync()).State.ServiceHealth["requests"].Status == ServiceStatus.Ready);
            state = await app.SnapshotAsync(); var primaryFact = state.State.ServiceHealth["requests"];
            Assert(runtime.IntegrationDiagnostics.All(x => x.Target is not null && x.ConfigurationSha256 is not null && x.Lifecycle is not null), "diagnostics separate lifecycle and target/deployment health identity");
            var maintenance = await app.SnapshotAsync();
            var login = await runtime.CommandAsync(new(Wire.Id(), maintenance.State.Revision, "Login", Value: "maintenance"));
            Assert(login.Status == "Persisted", "maintenance identity uses actual runtime command contract");
            var windowDraft = new ConfigurationDraft(); windowDraft.Import(Wire.Encode(config with { Version = "MULTI-IMPORTED" }), runtime.ValidateConfiguration);
            windowDraft["Timeout"] = "350"; windowDraft["Version"] = "MULTI-EDITED";
            var editing = windowDraft.Extensions; var editedService = editing.Services!.Single(x => x.Id == "aux") with { Parameters = new() { ["metadata.label"] = "cross-page import" } };
            windowDraft.Extensions = ExtensionDraftEditing.SaveService(editing, "aux", editedService, editing.Ports!.Single(x => x.InstanceId == "aux"));
            var configurationSnapshot = windowDraft.Build(); maintenance = await app.SnapshotAsync();
            var saved = await runtime.CommandAsync(new(Wire.Id(), maintenance.State.Revision, "SaveConfig", config.Version, Wire.Encode(configurationSnapshot)));
            var roundtrip = (await app.ConfigsAsync()).Single(x => x.Id == saved.OwnerId);
            Assert(saved.Status == "Persisted" && roundtrip.Status == "Validated" && Wire.Encode(roundtrip.Content) == Wire.Encode(configurationSnapshot) && runtime.Configuration!.Version == config.Version,
                "multi-device/service/requests import across both draft editors saves and rereads exact content while Effective stays unchanged");
            var concrete = (StationApplication)app;
            await concrete.SetIntegrationHealthAsync([primaryFact with { Epoch = "old-epoch", Sequence = primaryFact.Sequence + 100, Status = ServiceStatus.Unavailable }]);
            Assert((await app.SnapshotAsync()).State.ServiceHealth["requests"].Epoch == primaryFact.Epoch, "old-generation late health cannot replace current fact");
            var original = new IntegrationRuntime(config with { Integration = primaryTarget with { Revision = "old", Endpoint = "http://127.0.0.1:1/" }, Extensions = null }, null); original.Initialize();
            var intent = new RequestIntent(config.StationId, Wire.Id(), Wire.Id(), 1, RequestKind.Submit, "000123", 1, 1, null, new("SIM", SimulationRole.Maintenance, DateTimeOffset.UtcNow.AddHours(1)));
            var payload = original.Encode(intent, "None"); var oldRequest = new BusinessRequest(Wire.Id(), intent.OwnerId, intent.SessionId, intent.Kind, 1, Wire.Id(), payload, Wire.Hash(payload), original.Freeze().Target, RequestStatus.Unknown, DateTimeOffset.UtcNow, original.Freeze());
            await original.DisposeAsync();
            // Internal router is the same runtime implementation; query-only old target
            // is independent of the current instances monitored by the real Product.
            await using var router = new IntegrationRuntime(config, manager); router.Initialize();
            try { await router.QueryAsync(oldRequest, CancellationToken.None); } catch (HttpRequestException) { }
            await router.RefreshHealthAsync(CancellationToken.None); await Task.Delay(100);
            Assert(router.Diagnostics.Single(x => x.Historical).LastFailure?.StartsWith("Original query:") == true && router.Diagnostics.Where(x => !x.Historical).All(x => x.Target != oldRequest.Binding), "failed historical query retains exact old target and separate failure attribution");
        }
        var clock = new TestClock();
        await using (var expired = await Rig.Open(Path.Combine(area, "expired-health"), Rig.Config(url), clock: clock))
        {
            var now = clock.GetUtcNow(); var fact = new IntegrationHealthFact("builtin.reference", "requests", expired.ConfigValue.Version, new string('A',64), expired.ConfigValue.Integration,
                "current", 1, ServiceStatus.Ready, now, now.AddSeconds(1), null);
            await expired.App.SetIntegrationHealthAsync([fact]); clock.Advance(TimeSpan.FromSeconds(2));
            Assert((await expired.App.SnapshotAsync()).Admit.Reasons.Any(x => x.StartsWith("WcsHealth:Expired")), "expired primary fact revokes business guard without a read refreshing it");
        }
        var offlineA = Path.Combine(area, "offline-a", "station.db"); var offlineB = Path.Combine(area, "offline-b", "station.db");
        foreach (var file in new[] { offlineA, offlineB })
        {
            using var store = new SupplyStore(file); var state = store.Load();
            var content = config with { Version = file == offlineA ? "A" : "B", Extensions = file == offlineA ? new([installed.Package], []) : null };
            store.Commit(state, "SyntheticEffective", "deployment", "synthetic", config: new(Wire.Id(), "seed", content, "synthetic", "Effective", "", DateTimeOffset.UtcNow));
            manager.RegisterConsumer(file);
        }
        // The real Product's stopped root also references the package. Remove only its
        // actual Effective deployment for this isolated uninstall test, preserving audits.
        var runtimeDb = Path.Combine(area, "data", "Simulation", config.StationId, "station.db");
        foreach (var file in new[] { runtimeDb }) using (var store = new SupplyStore(file)) store.Commit(store.Load(), "SyntheticDisabled", "deployment", "synthetic", config: new(Wire.Id(), config.Version, config with { Version = "disabled", Extensions = null }, "synthetic", "Effective", "", DateTimeOffset.UtcNow));
        Refuse(() => manager.Uninstall(installed.Package, () => manager.InspectConsumers(SupplyStore.InspectRequiredExtensionPackages)), "B cannot uninstall while stopped A retains Effective exact package");
        using (var a = new SupplyStore(offlineA)) a.Commit(a.Load(), "SyntheticDisabled", "deployment", "synthetic", config: new(Wire.Id(), "A", config with { Version = "A-disabled", Extensions = null }, "synthetic", "Effective", "", DateTimeOffset.UtcNow));
        var pendingId = Wire.Id();
        using (var a = new SupplyStore(offlineA)) a.Commit(a.Load(), "SyntheticPending", pendingId, "synthetic", config: new(pendingId,"A-disabled",config with { Version="A-pending",Extensions=new([installed.Package],[]) },"synthetic","PendingActivation","",DateTimeOffset.UtcNow));
        Refuse(() => manager.Uninstall(installed.Package, () => manager.InspectConsumers(SupplyStore.InspectRequiredExtensionPackages)), "stopped A Pending deployment also pins exact package");
        using (var a = new SupplyStore(offlineA))
        {
            a.Commit(a.Load(), "SyntheticPendingRejected", pendingId, "synthetic", config:new(pendingId,"A-disabled",config with { Version="A-pending",Extensions=new([installed.Package],[]) },"synthetic","Rejected","",DateTimeOffset.UtcNow));
            var state = a.Load(); state.Session = new() { Status=SessionStatus.Closed, Barcode="000001" };
            a.Commit(state,"SyntheticOldRequest","old-request","synthetic");
            var request = new BusinessRequest(Wire.Id(),state.Session.Id,state.Session.Id,RequestKind.Context,1,Wire.Id(),"synthetic",Wire.Hash("synthetic"),config.Integration,RequestStatus.Unknown,DateTimeOffset.UtcNow,
                new("extension",installed.Package,"reference-http","main",config.Version,new string('A',64),manifest.PublicContracts!["Ipc.Supply.Contracts"],config.Integration,300,config.Policy,120,[]));
            a.Commit(state,"SyntheticOldUnknown",request.Id,"synthetic",request:request);
            Refuse(() => manager.Uninstall(installed.Package, () => manager.InspectConsumers(SupplyStore.InspectRequiredExtensionPackages)), "stopped original Unknown pins its exact implementation even without an Effective binding");
            a.Commit(state,"SyntheticAuthoritativeResolved",request.Id,"synthetic",request:request with {Status=RequestStatus.Rejected});
        }
        File.Move(offlineA, offlineA + ".held");
        Refuse(() => manager.Uninstall(installed.Package, () => manager.InspectConsumers(SupplyStore.InspectRequiredExtensionPackages)), "unreadable offline consumer cannot be called unreferenced");
        File.Move(offlineA + ".held", offlineA);
        manager.Uninstall(installed.Package, () => manager.InspectConsumers(SupplyStore.InspectRequiredExtensionPackages));
        Assert(manager.Inventory().Single(x => x.Package == installed.Package).Status == "Uninstalled", "shared-store uninstall succeeds only after every registered root clears responsibility");
    }
    sealed class HealthEndpoint : IAsyncDisposable
    {
        readonly HttpListener listener = new(); readonly Task worker;
        public volatile bool Ready = true; public volatile int DelayMs;
        public string Url { get; }
        public HealthEndpoint()
        {
            var finder = new TcpListener(IPAddress.Loopback, 0); finder.Start(); var port = ((IPEndPoint)finder.LocalEndpoint).Port; finder.Stop();
            Url = $"http://127.0.0.1:{port}/"; listener.Prefixes.Add(Url); listener.Start();
            worker = Task.Run(async () =>
            {
                while (listener.IsListening)
                {
                    HttpListenerContext context;
                    try { context = await listener.GetContextAsync(); } catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { break; }
                    try { if (DelayMs > 0) await Task.Delay(DelayMs); var bytes = System.Text.Encoding.UTF8.GetBytes(Ready ? "SIMULATION-v1" : "UNAVAILABLE"); await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close(); }
                    catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException) { }
                }
            });
        }
        public async ValueTask DisposeAsync() { listener.Stop(); await worker; listener.Close(); }
    }
}
