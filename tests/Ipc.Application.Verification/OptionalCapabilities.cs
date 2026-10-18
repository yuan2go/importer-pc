using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Application.DirectPlc;
using Ipc.Application.WcsToPlc;
using Ipc.Assembly;
using Ipc.Platform;
using Ipc.Reference.Protocol;

static class OptionalCapabilities
{
    public static async Task Run(string workspace, string root)
    {
        List<object> matrix = [];
        void Pass(bool condition, string message) { if (!condition) throw new Exception("FAIL AC18: " + message); Console.WriteLine("PASS AC18: " + message); }
        ParcelConfiguration Config(string id) => Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config", id, "business.json")));
        CapabilityBinding Binding(string kind) => new(kind, "reference-" + kind, "1.0.0", kind + ".one", "reference://" + kind + "/one", "PARCEL-REFERENCE-v1", kind == "login" ? new() { ["subjects"] = "alice,bob" } : []);
        ParcelConfiguration Options(string id, bool login, bool weight, bool images) => Config(id) with
        {
            Login = login ? Binding("login") : null,
            Weight = new(weight ? WeightSource.Measured : WeightSource.Disabled, WeightUnit.Kg, 3),
            Scale = weight ? Binding("weight") : null,
            Images = images ? new(Binding("camera"), Binding("upload")) : null
        };
        FlowDefinition Flow(string id) => id switch { "sportmaster" => new SportmasterFlow(), "direct-plc" => new DirectPlcFlow(), _ => new WcsToPlcFlow() };
        async Task<ParcelRuntime> Open(ParcelConfiguration config, string run)
        {
            var directory = Path.Combine(root, run, "config"); Directory.CreateDirectory(directory); ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), config);
            var launch = new BusinessLaunch(directory, Path.Combine(root, run, "data"), null, null);
            var app = new ParcelRuntime(launch, Flow(config.ApplicationId), new ParcelCapabilityFactory(launch));
            try { await app.StartAsync(CancellationToken.None); return app; } catch { await app.DisposeAsync(); throw; }
        }
        async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> ready)
        {
            for (int i = 0; i < 150; i++) { var s = await app.SnapshotAsync(); if (ready(s)) return s; await Task.Delay(20); }
            throw new Exception("Timed out: " + Wire.Encode(await app.SnapshotAsync()));
        }
        async Task<BusinessReceipt> Command(ParcelRuntime app, string kind, string? owner = null)
        { var s = await app.SnapshotAsync(); return await app.CommandAsync(new(Wire.Id(), s.Revision, kind, owner)); }
        ReferencePeer.PeerEntry ReadPeer(StageFact stage) => Wire.Decode<ReferencePeer.PeerEntry>(File.ReadAllText(Path.Combine(stage.Request.Binding.Parameters["peer-root"], Wire.Hash(stage.Request.Binding.Target), stage.Request.Id + ".json")));
        foreach (var id in new[] { "sportmaster", "direct-plc", "wcs-to-plc" })
        foreach (var login in new[] { false, true })
        foreach (var weight in new[] { false, true })
        foreach (var images in new[] { false, true })
        {
            var name = id + "-" + login + "-" + weight + "-" + images;
            var c = Options(id, login, weight, images);
            await using var app = await Open(c, name);
            var initial = await app.SnapshotAsync();
            Pass(initial.Health.Length == 1 + (id != "direct-plc" ? 1 : 0) + (id != "sportmaster" ? 1 : 0) + (login ? 1 : 0) + (weight ? 1 : 0) + (images ? 2 : 0), name + " creates only selected runtime dependencies");
            if (login)
            {
                Pass(!app.EmitReference("unauthenticated", "00001", Wire.Id()) && (await app.SnapshotAsync()).Operations.Length == 0, name + " unauthenticated observation cannot enter the queue or create an operation");
                Pass((await app.LoginAsync("alice", "incorrect")).Status == "Rejected", name + " rejected login preserves no session");
                Pass((await app.LoginAsync("alice", "SIMULATION_ONLY")).Status == "Authenticated", name + " authenticates through selected component");
            }
            app.EmitReference("parcel-one", "00001", Wire.Id());
            if (weight)
            {
                var waiting = await Wait(app, x => x.Operations.FirstOrDefault()?.State == ParcelState.WaitingWeight);
                Pass(waiting.Operations.Single() is { Wcs: null, Plc: null, Capture: null }, name + " waits before every external stage");
                var wrong = new WeightMeasurement(Wire.Id(), "another-parcel", c.Scale!.Target, 2.345m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow);
                app.EmitReferenceWeight(wrong); await Task.Delay(30);
                Pass((await app.SnapshotAsync()).Operations.Single().State == ParcelState.WaitingWeight, name + " wrong entity weight cannot advance");
                var now = DateTimeOffset.UtcNow;
                app.EmitReferenceWeight(wrong with { Id = Wire.Id(), AdmissionId = "parcel-one", MeasuredAt = now.AddSeconds(-10) });
                app.EmitReferenceWeight(wrong with { Id = Wire.Id(), AdmissionId = "parcel-one", Unit = WeightUnit.G, MeasuredAt = now });
                app.EmitReferenceWeight(wrong with { Id = Wire.Id(), AdmissionId = "parcel-one", Quality = MeasurementQuality.Unstable, Stable = false, MeasuredAt = now });
                await Task.Delay(30);
                Pass((await app.SnapshotAsync()).Operations.Single().Plc is null, name + " stale/unit/quality failures cannot send PLC");
                app.EmitReferenceWeight(wrong with { Id = Wire.Id(), AdmissionId = "parcel-one", MeasuredAt = DateTimeOffset.UtcNow });
            }
            var s = await Wait(app, x => x.Operations.FirstOrDefault()?.State == (id == "sportmaster" ? ParcelState.SlotResolved : ParcelState.PlcAccepted));
            var op = s.Operations.Single();
            Pass(op.Operator?.Subject == (login ? "alice" : null) && (op.Weight is not null) == weight && (op.Image?.UploadedUri is not null) == images, name + " freezes selected identity, actual weight and uploaded image");
            foreach (var stage in new[] { op.Wcs, op.Plc }.OfType<StageFact>())
            {
                var peer = ReadPeer(stage); var payload = Wire.Decode<ParcelPayload>(peer.Request.Payload);
                Pass(peer.Executions == 1 && payload.Operator == op.Operator && payload.Weight == op.Weight && payload.Image == op.Image, name + " independent receiver contains original operator/weight/image exactly once");
                if (stage == op.Plc && id == "wcs-to-plc") Pass(payload.Slot == op.Slot && payload.WcsRequestId == op.Wcs!.Request.Id, name + " PLC receives exact original WCS slot and request");
            }
            if (images)
            {
                var upload = ReadPeer(op.Upload!);
                var remote = Path.Combine(op.Upload!.Request.Binding.Parameters["peer-root"], Wire.Hash(op.Upload.Request.Binding.Target), op.Upload.Request.Id + ".png");
                Pass(upload.Executions == 1 && Wire.FileHash(remote) == op.Image!.Sha256 && new FileInfo(remote).Length == op.Image.Length, name + " uploader actually transfers captured PNG bytes to independent receiver");
            }
            app.EmitReference("parcel-one", "00001", Wire.Id()); await Task.Delay(30);
            Pass((await app.SnapshotAsync()).Operations.Length == 1 && (await app.SnapshotAsync()).RuntimeId == initial.RuntimeId, name + " repeated admission shares owner and does not repeat stages");
            if (id != "sportmaster")
            {
                if (login) Pass((await app.LogoutAsync()).Status == "Rejected", name + " accepted PLC with unknown physical completion protects operator");
                await app.ReconcileAsync(op.Id);
            }
            if (login)
            {
                Pass((await app.LogoutAsync()).Status == "LoggedOut" && (await app.LoginAsync("bob", "SIMULATION_ONLY")).Status == "Authenticated", name + " changes operator only after original operation settles");
                Pass((await app.SnapshotAsync()).Operations.Single().Operator == op.Operator, name + " operator change preserves old immutable snapshot");
            }
            matrix.Add(new { Application = id, Login = login, MeasuredWeight = weight, Images = images, Status = "PASS", Operation = op.Id, Operator = op.Operator?.Subject, Wcs = op.Wcs?.Request.PayloadHash, Plc = op.Plc?.Request.PayloadHash, Image = op.Image?.Sha256 });
        }
        // Uploaded bytes may have committed before the reply disappeared. Restart queries
        // original stages and never repeats capture/upload or advances PLC on its own.
        foreach (var lostKind in new[] { "capture", "upload", "plc" })
        {
            var run = "lost-" + lostKind; var c = Options("wcs-to-plc", true, false, true);
            var lost = (lostKind == "capture" ? c.Images!.Camera : lostKind == "upload" ? c.Images!.Uploader : c.Plc!) with { Parameters = new() { ["reply-mode"] = "lose-after-commit", ["complete-on-query"] = "true" } };
            if (lostKind != "plc") lost = lost with { Parameters = new() { ["reply-mode"] = "lose-after-commit" } };
            c = lostKind == "capture" ? c with { Images = c.Images! with { Camera = lost } } : lostKind == "upload" ? c with { Images = c.Images! with { Uploader = lost } } : c with { Plc = lost };
            ParcelOperation original;
            await using (var app = await Open(c, run))
            {
                await app.LoginAsync("alice", "SIMULATION_ONLY"); app.EmitReference("lost-parcel", "009", Wire.Id());
                var s = await Wait(app, x => x.Operations.FirstOrDefault()?.State == (lostKind == "plc" ? ParcelState.PlcUnknown : ParcelState.ImageUnknown)); original = s.Operations.Single();
                Pass(lostKind == "plc" || original.Wcs is null && original.Plc is null, run + " uncertain image stage blocks all downstream effects");
                Pass((await app.LogoutAsync()).Status == "Rejected", run + " uncertainty cannot be cleared by logout");
                await Command(app, "Pause"); s = await app.SnapshotAsync();
                Pass((await app.StageConfigurationAsync(s.Configuration.Revision, s.Configuration.Effective with { Login = null })).Status == "PendingRestart", run + " feature change is pending only");
            }
            await using (var app = await Open(c, run))
            {
                var s = await app.SnapshotAsync(); var op = s.Operations.Single();
                Pass(s.Configuration.Status == "ActivationFailed" && s.CurrentOperator is null && op.Operator == original.Operator, run + " restart keeps original responsibility and requires new session only for new admission");
                var a = lostKind == "capture" ? op.Capture! : lostKind == "upload" ? op.Upload! : op.Plc!;
                var b = lostKind == "capture" ? original.Capture! : lostKind == "upload" ? original.Upload! : original.Plc!;
                Pass(Wire.Encode(a.Request) == Wire.Encode(b.Request), run + " exact original frozen request survives restart");
                await app.ReconcileAsync(op.Id); s = await app.SnapshotAsync(); op = s.Operations.Single();
                if (lostKind != "plc")
                {
                    Pass(op.Plc is null, run + " original query does not dispatch downstream PLC");
                    Pass((await Command(app, "ContinuePrepared", op.Id)).Status == "Rejected", run + " pause still guards never-attempted downstream continuation");
                    Pass((await app.LoginAsync("bob", "SIMULATION_ONLY")).Status == "Rejected" && (await app.LoginAsync("alice", "SIMULATION_ONLY")).Status == "Authenticated", run + " only the original subject can recover a lost session with unfinished work");
                    Pass((await Command(app, "Resume")).Status == "Persisted", run + " software pause can be released after reconciliation");
                    Pass((await Command(app, "ContinuePrepared", op.Id)).Status == "Persisted", run + " explicit continuation dispatches only never-attempted downstream intents");
                    s = await app.SnapshotAsync(); op = s.Operations.Single();
                    Pass(op.State == ParcelState.PlcAccepted && op.Operator == original.Operator && op.Capture!.Attempts == 1 && op.Upload!.Attempts == 1 && op.Wcs!.Attempts == 1 && op.Plc!.Attempts == 1, run + " continuation preserves original operator and executes every phase once");
                }
                var stage = lostKind == "capture" ? op.Capture! : lostKind == "upload" ? op.Upload! : op.Plc!;
                Pass(stage.State == StageState.Accepted && stage.Attempts == 1, run + " query resolves original fact without resend");
            }
        }
        foreach (var fault in new[] { "upload", "wcs" })
        {
            var c = Options("wcs-to-plc", false, false, true);
            c = fault == "upload" ? c with { Images = c.Images! with { Uploader = c.Images.Uploader with { Parameters = new() { ["decision"] = "reject" } } } } : c with { Wcs = c.Wcs! with { Parameters = new() { ["decision"] = "reject" } } };
            await using var app = await Open(c, "reject-" + fault); app.EmitReference("rejected-parcel", "009", Wire.Id());
            var s = await Wait(app, x => x.Operations.FirstOrDefault()?.State == (fault == "upload" ? ParcelState.ImageRejected : ParcelState.WcsRejected));
            Pass(s.Operations.Single().Plc is null, fault + " explicit rejection never creates PLC intent");
        }
        // A full backup restores image bytes and receipt custody, while the external
        // upload ledger remains independent of client restore.
        var backupConfig = Options("sportmaster", true, false, true);
        await using (var app = await Open(backupConfig, "image-backup"))
        {
            await app.LoginAsync("alice", "SIMULATION_ONLY"); app.EmitReference("backup-parcel", "001", Wire.Id());
            var s = await Wait(app, x => x.Operations.FirstOrDefault()?.State == ParcelState.SlotResolved); var original = s.Operations.Single();
            await Command(app, "Pause"); var backup = Path.Combine(root, "images.backup.db");
            Pass((await app.BackupAsync(backup)).Status == "BackupCreated", "image backup includes actual image custody");
            var data = Path.Combine(root, "restored-images"); var destination = Path.Combine(data, "Simulation", backupConfig.StationId, backupConfig.ApplicationId);
            ParcelRecovery.Restore(backup, destination);
            Pass(Wire.FileHash(Path.Combine(destination, original.Image!.RelativeFile)) == original.Image.Sha256, "isolated restore preserves exact original PNG bytes");
            await app.StopAsync(CancellationToken.None); await app.DisposeAsync();
            var launch = new BusinessLaunch(Path.Combine(root, "image-backup", "config"), data, null, null);
            await using var restored = new ParcelRuntime(launch, Flow("sportmaster"), new ParcelCapabilityFactory(launch)); await restored.StartAsync(CancellationToken.None);
            s = await restored.SnapshotAsync();
            Pass(s.Paused && s.CurrentOperator is null && s.Operations.Single().Image == original.Image && s.Operations.Single().Operator == original.Operator && ReadPeer(original.Upload!).Executions == 1, "restore stays paused without reviving login or repeating remote upload");
        }
        // New image reply application also depends on durable local pixels. Verify both
        // crash windows through the shared original-raw recovery mechanism.
        foreach (var receiptNumber in new[] { 1, 2 })
        {
            var c = Options("wcs-to-plc", false, false, true); var run = "raw-image-" + receiptNumber;
            var app = await Open(c, run); var count = 0;
            app.Boundary = point => { if (point == "AfterRawDurable" && ++count == receiptNumber) throw new IOException("AC18 original image raw durable, T3 interrupted"); };
            app.EmitReference("raw-image-parcel", "001", Wire.Id());
            await Wait(app, x => x.BlockingReasons.Any(r => r.Contains("worker failed")));
            try { await app.DisposeAsync(); throw new Exception("Expected image T3 worker failure"); }
            catch (InvalidOperationException) { Pass(true, "image raw T3 failure remains visible " + receiptNumber); }
            await using var restored = await Open(c, run); var snapshot = await restored.SnapshotAsync(); var op = snapshot.Operations.Single();
            Pass(op.Wcs is null && op.Plc is null && op.Capture?.State == StageState.Accepted &&
                (receiptNumber == 1 ? op.Upload?.State == StageState.Prepared : op.Upload?.State == StageState.Accepted), "startup replays original durable image raw locally without dispatch " + receiptNumber);
            Pass((await Command(restored, "ContinuePrepared", op.Id)).Status == "Persisted", "explicit image recovery continuation accepted " + receiptNumber);
            op = (await restored.SnapshotAsync()).Operations.Single();
            Pass(op.State == ParcelState.PlcAccepted && op.Capture!.Attempts == 1 && op.Upload!.Attempts == 1 && ReadPeer(op.Upload).Executions == 1, "image raw recovery never recaptures or retransmits " + receiptNumber);
        }
        var validation = Options("sportmaster", false, false, true); var flow = Flow("sportmaster");
        Pass(flow.Validate(validation with { Images = validation.Images! with { Uploader = null! } }).Length > 0, "incomplete optional image dependency is rejected before construction");
        Pass(flow.Validate(validation with { Images = validation.Images! with { MaximumBytes = int.MaxValue } }).Length > 0, "unbounded image allocation is rejected before construction");
        var unavailable = Options("sportmaster", true, false, false); unavailable = unavailable with { Login = unavailable.Login! with { Parameters = new() { ["subjects"] = "alice", ["reply-mode"] = "unavailable" } } };
        await using (var app = await Open(unavailable, "auth-unavailable"))
            Pass((await app.LoginAsync("alice", "SIMULATION_ONLY")).Status == "Unavailable" && (await app.SnapshotAsync()).CurrentOperator is null, "authentication network failure differs from credential rejection");
        ExtensionStore.AtomicWrite(Path.Combine(root, "site-capabilities-matrix.json"), new { Baseline = "d3a757d6314a2d690bcfc4e7763cfd4849b97319", Cases = matrix, Windows = "NOT_RUN", RealProtocols = "BLOCKED" });
        Console.WriteLine("PASS AC18 COMPLETE: 24 optional-feature combinations and original identity/image/PLC failure recovery");
    }
}
