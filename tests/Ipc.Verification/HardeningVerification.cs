using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;
using Microsoft.Data.Sqlite;

static class HardeningVerification
{
    public static async Task Run(string root, string url, string workspace)
    {
        var failures = new List<Exception>();
        async Task Check(string name, Func<Task> test)
        {
            Console.WriteLine("RUN H04 " + name);
            try { await test(); Console.WriteLine("PASS H04 " + name); }
            catch (Exception ex) { Console.WriteLine("FAIL H04 " + name + ": " + ex.Message); failures.Add(ex); }
        }
        await Check("overload + Prepared controlled disposition", async () =>
        {
            await using var h = await Rig.New(root, url);
            var receipt = await h.Command("OpenContext", value: "000001");
            h.App.Latch("Overload", "SIMULATION source inspection required");
            Assert(await h.App.ClaimAsync() is null, "Overload must prevent transport");
            await h.Command("ClearOverload", value: "SIMULATION source inspected", expect: "RejectedByGuard");
            await h.Command("CancelPrepared", receipt.RequestId, "SIMULATION original intent not delivered");
            Assert(h.Store.Request(receipt.RequestId!)!.Status == RequestStatus.CancelledBeforeDispatch, "Prepared terminated with identity retained");
            await h.Command("ClearOverload", value: "SIMULATION source inspected");
            var original = h.Store.Request(receipt.RequestId!)!;
            Assert(h.Store.Audits(original.Id).Any(x => x.Kind == "CancelPrepared") && h.Store.Audits(original.SessionId).Any(x => x.Kind == "ClearOverload"), "Termination and fault acknowledgement audited independently");
            Assert(h.Store.Responses(original.Id).Length == 0 && await h.Port.QueryAsync(original, CancellationToken.None) is null, "No remote call or invented response");
            Assert(!(await h.App.SnapshotAsync()).Admit.Allowed, "Local termination must retain independent disposition block");
        });
        await Check("old active alarms survive 350 historical occurrences", async () =>
        {
            await using var h = await Rig.New(root, url); await h.Command("Pause");
            var original = (await h.App.SnapshotAsync()).Alarms.Single(x => x.Code.StartsWith("OperatorPaused"));
            var priorHistory = (await h.App.AlarmHistoryAsync(0, 200)).Length;
            for (int i = 0; i < 350; i++) h.Store.SaveAlarm(new(Wire.Id(), "synthetic-history", "station", "fixture", i.ToString(), null, DateTimeOffset.UtcNow, null, null, DateTimeOffset.UtcNow));
            Assert((await h.App.SnapshotAsync()).Alarms.Any(x => x.Id == original.Id), "Historical pagination cannot hide active occurrence");
            await h.Command("AckAlarm", original.Id);
            Assert((await h.App.SnapshotAsync()).ActiveAlarmCount == 1, "History and ack do not change active count or create duplicate occurrences");
            await h.Command("Resume");
            Assert(h.Store.Alarm(original.Id) is { ClearedAt: not null, AckedBy: not null }, "Original occurrence independently recovered");
            var page1 = await h.App.AlarmHistoryAsync(0, 200); var page2 = await h.App.AlarmHistoryAsync(200, 200);
            Assert(page1.Length == 200 && page2.Length == 151 + priorHistory && !page1.Select(x => x.Id).Intersect(page2.Select(x => x.Id)).Any(), "History pages exhaustive with no overlap");
            await h.Command("Pause");
            Assert((await h.App.SnapshotAsync()).Alarms.Single().Id != original.Id && (await h.App.SnapshotAsync()).Alarms.Single().AckedBy is null, "New occurrence inherits neither ack nor recovery");
        });
        await Check("source marker does not resurrect after acknowledged recovery", async () =>
        {
            var cfg = Rig.Config(url); var directory = Path.Combine(root, "h04-marker");
            using var store = new SupplyStore(Path.Combine(directory, "station.db"));
            var injection = new FaultInjection(); await using var app = new StationApplication(store, cfg, new SimulationRequestContract(cfg), injection);
            var source = new ManualObservationSource(cfg.Devices[0].DeviceId); var other = new ManualObservationSource(cfg.Devices[1].DeviceId);
            await using var module = new ObservationModule(app, [source, other], 1);
            await module.StartAsync(CancellationToken.None);
            async Task Command(string kind) { var s = await app.SnapshotAsync(); var r = await app.ExecuteAsync(new(Wire.Id(), s.State.Revision, kind, Value: kind == "Login" ? "maintenance" : "SIMULATION source inspected", EvidenceVersion: s.OverloadVersion)); Assert(r.Status == "Persisted", r.Reason); }
            await Command("Login");
            await using var port = new LoopbackIntegration(cfg);
            await app.SetWcsHealthAsync(await port.HealthAsync(CancellationToken.None));
            var before = await app.SnapshotAsync();
            var open = await app.ExecuteAsync(new(Wire.Id(), before.State.Revision, "OpenContext", Value: "000001"));
            Assert(open.Status == "Persisted", "Context requires current independent loopback health: " + open.Reason);
            var context = (await app.ClaimAsync())!.Value;
            Assert(await app.BeforeSendAsync(context.Request, context.Attempt), "Context transport authorized");
            await app.CompleteAsync(context.Request, context.Attempt, await port.ExecuteAsync(context.Request, CancellationToken.None));
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int once = 0;
            injection.Hook = point => { if (point == "T1" && Interlocked.Increment(ref once) == 1) { started.SetResult(); release.Task.GetAwaiter().GetResult(); } };
            source.Emit("Enter", "entity-1"); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            other.Emit("Enter", "entity-2"); Assert(!source.Emit("Enter", "entity-3"), "Overflow reproduced"); release.SetResult();
            try
            {
                await Task.Delay(250);
                var draining = await app.SnapshotAsync();
                Assert(draining.State.Lanes.Count == 2 && draining.State.Lanes.Values.All(x => x.Occupancy == Occupancy.Occupied), "Both pre-overload admitted entity events processed, not silently discarded/rejected");
                Assert(!source.Emit("Read", "entity-1", ["000123"], 1), "Overload closes new source input after drain");
                var stale = await app.ExecuteAsync(new(Wire.Id(), draining.State.Revision, "ClearOverload", Value: "SIMULATION stale inspection", EvidenceVersion: draining.OverloadVersion));
                Assert(stale.Status == "RejectedByGuard", "Changed overflow evidence invalidates acknowledgement");
                await Command("ClearOverload");
                await Task.Delay(250); Assert(!File.Exists(Path.Combine(directory, "overload-marker")), "Acknowledged overflow must not rewrite stale marker");
                Assert(!app.IsOverloaded, "Source and app agree on acknowledged latch");
                // Deterministically create a second overload with the same module, retaining the first generation's audit.
                var auditCount = store.Audits(cfg.StationId).Count(x => x.Kind == "ClearOverload");
                started = new(TaskCreationOptions.RunContinuationsAsynchronously); release = new(TaskCreationOptions.RunContinuationsAsynchronously); once = 0;
                Assert(source.Emit("Read", "entity-1", ["000123"], 1), "New input accepted after acknowledgement"); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert(other.Emit("Read", "entity-2", ["000123"], 1), "Second lane queued");
                Assert(!source.Emit("Read", "entity-1", ["000123"], 1), "New overflow after recovery"); release.SetResult();
                await Task.Delay(150);
                Assert(File.Exists(Path.Combine(directory, "overload-marker")) && app.IsOverloaded && store.Audits(cfg.StationId).Count(x => x.Kind == "ClearOverload") == auditCount, "New overflow marker retained, old acknowledgement cannot clear it");
            }
            finally { release.TrySetResult(); await module.StopAsync(CancellationToken.None); }
            // A fresh application reconstructs the outstanding overload from the durable marker.
            await using var restarted = new StationApplication(store, cfg, new SimulationRequestContract(cfg));
            Assert((await restarted.SnapshotAsync()).RuntimeFaults.Any(x => x.StartsWith("Overload")), "Restart preserves the new unacknowledged overflow");
        });
        await Check("original actor expiry cannot reauthorize Prepared or release entity", async () =>
        {
            var clock = new TestClock(); var cfg = Rig.Config(url); var directory = Path.Combine(root, "h04-expiry");
            string requestId, frozen;
            await using (var h = await Rig.Open(directory, cfg, clock: clock))
            {
                await h.OpenBox(); var op = await h.Enter("left");
                await h.Event("left", "Read", op.AdmissionId, ["000123"], 1); await h.Drain();
                requestId = (await h.Command("Submit", op.Id)).RequestId!; frozen = h.Store.Request(requestId)!.Payload;
                clock.Advance(TimeSpan.FromHours(9)); await h.Command("Login", value: "operator");
                var recovery = (await h.App.SnapshotAsync()).Recovery.Single();
                Assert(recovery.Dispatch.Reasons.Contains("OriginalActorExpired") && await h.App.ClaimAsync() is null, "Renewing current login does not reauthorize frozen original actor");
                await h.Command("CancelPrepared", requestId, "SIMULATION original actor expired", expect: "RejectedByGuard");
                await h.Command("Login", value: "maintenance");
                await h.Command("CancelPrepared", requestId, "SIMULATION local non-delivery inspected");
                var cancelled = h.Store.Request(requestId)!;
                Assert(cancelled.Payload == frozen && h.Store.AcceptedQuantity(cancelled.SessionId) == 0 && h.Store.Attempts(requestId).Length == 0, "Payload unchanged; termination is neither transmission nor acceptance");
                Assert(h.Store.Requests(op.Id).Single(x => x.Kind == RequestKind.Validate).Status == RequestStatus.Accepted, "Earlier validation/reservation evidence retained");
                await h.Event("left", "Depart", op.AdmissionId);
                await h.Event("right", "Enter", Wire.Id());
                var after = await h.App.SnapshotAsync();
                Assert(after.State.Lanes["left"].Occupancy == Occupancy.Occupied && !after.State.Lanes.ContainsKey("right") && !after.Close.Allowed, "No entity release, new admission or close from local termination");
            }
            await using (var h = await Rig.Open(directory, cfg, clock: clock))
                Assert(h.Store.Request(requestId)!.Payload == frozen && !(await h.App.SnapshotAsync()).Admit.Allowed, "Restart retains local disposition block and immutable original");
        });
        await Check("cancel competes with pre-network guard and durable handoff", async () =>
        {
            for (int i = 0; i < 8; i++)
            {
                await using var h = await Rig.New(root, url); await h.OpenBox(); var op = await h.Enter("left");
                await h.Event("left", "Read", op.AdmissionId, ["000123"], 1); await h.Drain();
                var submit = await h.Command("Submit", op.Id); var claim = (await h.App.ClaimAsync())!.Value;
                await h.Command("Pause");
                var snap = await h.App.SnapshotAsync();
                var cancel = h.App.ExecuteAsync(new(Wire.Id(), snap.State.Revision, "CancelPrepared", submit.RequestId, "SIMULATION cancel versus pre-network guard"));
                var send = h.App.BeforeSendAsync(claim.Request, claim.Attempt);
                await Task.WhenAll(cancel, send);
                Assert(!send.Result, "Pause blocks handoff under competing cancellation");
                if (h.Store.Request(submit.RequestId!)!.Status == RequestStatus.Prepared) await h.Command("CancelPrepared", submit.RequestId, "SIMULATION persisted NotDelivered proof");
                var original = h.Store.Request(submit.RequestId!)!;
                Assert(original.Status == RequestStatus.CancelledBeforeDispatch && h.Store.Attempts(original.Id).Single().Disposition == "NotDelivered", "Only persisted non-delivery permits termination");
                Assert(!await h.App.BeforeSendAsync(claim.Request, claim.Attempt) && await h.App.ClaimAsync() is null, "Stale claim cannot resurrect terminal request");
                Assert(await h.Port.QueryAsync(original, CancellationToken.None) is null, "Cancelled request absent from independent receiving ledger");
            }
            await using var sentRig = await Rig.New(root, url); await sentRig.OpenBox(); var sentOp = await sentRig.Enter("left");
            await sentRig.Event("left", "Read", sentOp.AdmissionId, ["000123"], 1); await sentRig.Drain();
            await sentRig.Command("Submit", sentOp.Id); var sent = (await sentRig.App.ClaimAsync())!.Value;
            Assert(await sentRig.App.BeforeSendAsync(sent.Request, sent.Attempt), "Handoff authorized");
            await sentRig.Command("Pause"); await sentRig.Command("CancelPrepared", sent.Request.Id, "SIMULATION too late to locally terminate", expect: "RejectedByGuard");
            Assert(sentRig.Store.Attempts(sent.Request.Id).Single().Disposition == "TransportHandoff", "Handoff authorization durable before transport");
            var response = await sentRig.Port.ExecuteAsync(sent.Request, CancellationToken.None);
            await sentRig.App.UnknownAsync(sent.Request, sent.Attempt, "SIMULATION response lost");
            await sentRig.Command("CancelPrepared", sent.Request.Id, "SIMULATION unknown must reconcile", expect: "RejectedByGuard");
            await sentRig.App.CompleteAsync(sent.Request, sent.Attempt, response); await sentRig.App.CompleteAsync(sent.Request, null, response);
            Assert(sentRig.Store.AcceptedQuantity(sent.Request.SessionId) == 1 && sentRig.Store.Responses(sent.Request.Id).Length == 1, "Late and duplicate receipts count once on original request despite pause");
        });
        await Check("all Prepared kinds terminate without inventing release or acceptance", async () =>
        {
            foreach (var kind in Enum.GetValues<RequestKind>())
            {
                await using var h = await Rig.New(root, url); CommandReceipt receipt;
                if (kind == RequestKind.Context) receipt = await h.Command("OpenContext", value: "000001");
                else
                {
                    await h.OpenBox();
                    if (kind is RequestKind.Validate or RequestKind.Submit)
                    {
                        var op = await h.Enter("left"); await h.Event("left", "Read", op.AdmissionId, ["000123"], 1);
                        if (kind == RequestKind.Submit) { await h.Drain(); receipt = await h.Command("Submit", op.Id); }
                        else { var req = h.Store.PendingRequests().Single(); receipt = new("Persisted", "", 0, RequestId: req.Id); }
                    }
                    else
                    {
                        receipt = await h.Command("CheckClose");
                        if (kind == RequestKind.Close) { await h.Drain(); var s = await h.App.SnapshotAsync(); receipt = await h.Command("ConfirmClose", s.State.Session!.CloseCheckRequest, "CONFIRM"); }
                    }
                }
                var original = h.Store.Request(receipt.RequestId!)!; await h.Command("Pause");
                await h.Command("CancelPrepared", original.Id, "SIMULATION original local non-delivery proof");
                Assert(h.Store.Request(original.Id) == original with { Status = RequestStatus.CancelledBeforeDispatch }, "Immutable request fields retained for " + kind);
                Assert(!(await h.App.SnapshotAsync()).Admit.Allowed && !(await h.App.SnapshotAsync()).Close.Allowed, "Disposition remains required for " + kind);
                // Contradictory late evidence is retained and blocked, never promoted to local success.
                var contrary = new IntegrationResponse(original.Id, original.PayloadHash, Wire.Id(), "Rejected", "SIMULATION unexpected late receipt");
                await h.App.CompleteAsync(original, null, contrary);
                Assert(h.Store.Request(original.Id)!.Status == RequestStatus.Unknown && h.Store.Responses(original.Id).Single() == contrary, "Unexpected evidence after proven local cancellation quarantined for " + kind);
                Assert((await h.App.DetailsAsync(original.Id)).Single().Request.Id == original.Id, "Request alarm detail resolves only original request");
            }
        });
        await Check("SIMULATION-v1 baseline JSON compatibility and forward format gate", async () =>
        {
            var fixtures = Path.Combine(workspace, "tests/Ipc.Verification/fixtures");
            var requestText = File.ReadAllText(Path.Combine(fixtures, "simulation-v1-request.json")).TrimEnd();
            var responseText = File.ReadAllText(Path.Combine(fixtures, "simulation-v1-response.json")).TrimEnd();
            var original = Wire.Decode<BusinessRequest>(requestText); var response = Wire.Decode<IntegrationResponse>(responseText);
            var payload = Wire.Decode<FixturePayload>(original.Payload); var cfg = Rig.Config(url) with { Policy = payload.Policy, FixtureDemand = payload.InitialDemand };
            var contract = new SimulationRequestContract(cfg);
            var intent = new RequestIntent(payload.StationId, payload.SessionId, payload.OwnerId, payload.OwnerRevision, payload.Kind, payload.Barcode, payload.Package, payload.Quantity, payload.CheckToken, payload.Actor);
            Assert(contract.Encode(intent, payload.Fault) == original.Payload && Wire.Hash(original.Payload) == original.PayloadHash, "Old payload bytes/hash preserved by separated codec");
            Assert(Wire.Encode(response) == responseText && contract.Authority(original).Actor == payload.Actor, "Old response fields and frozen actor unchanged");
            contract.ValidateResponse(original, response);
            await using var h = await Rig.New(root, url);
            using var db = new SqliteConnection("Data Source=" + h.Store.Path); db.Open(); using var sql = db.CreateCommand(); sql.CommandText = "SELECT count(*) FROM schema_history";
            Assert(Convert.ToInt32(sql.ExecuteScalar()) == 4, "Historical migrations retained and format4 appended");
            sql.CommandText = "SELECT checksum FROM schema_history WHERE id=1";
            Assert((string?)sql.ExecuteScalar() == Wire.Hash(File.ReadAllText(Path.Combine(fixtures, "schema1.sql"))), "Original schema1 SQL checksum is unchanged");
            sql.CommandText = "SELECT checksum FROM schema_history WHERE id=2";
            Assert((string?)sql.ExecuteScalar() == AlignmentDataVerification.Format2Checksum, "Historical format2 checksum is unchanged");
            sql.CommandText = "SELECT checksum FROM schema_history WHERE id=3";
            Assert((string?)sql.ExecuteScalar() == AlignmentDataVerification.Format3Checksum, "Historical format3 checksum is unchanged");
            sql.CommandText = "SELECT version || ':' || minimum_reader FROM storage_format WHERE id=1";
            Assert((string?)sql.ExecuteScalar() == "4:4", "Format4 records a matching minimum reader");
        });
        await Check("same alarm code retains independent owners and recovery", async () =>
        {
            await using var h = await Rig.New(root, url); await h.OpenBox();
            var l = await h.Enter("left"); var r = await h.Enter("right");
            await h.Command("SetFixtureFault", value: "Reject"); await h.Event("left", "Read", l.AdmissionId, ["000123"], 1); await h.Drain();
            await h.Command("SetFixtureFault", value: "Reject"); await h.Event("right", "Read", r.AdmissionId, ["000123"], 1); await h.Drain();
            var alarms = (await h.App.SnapshotAsync()).Alarms.Where(x => x.Code == "OperationRejected").ToArray();
            Assert(alarms.Length == 2 && alarms.Select(x => x.OwnerId).ToHashSet().SetEquals([l.Id, r.Id]), "Same code binds two distinct owners");
            var leftAlarm = alarms.Single(x => x.OwnerId == l.Id); var rightAlarm = alarms.Single(x => x.OwnerId == r.Id);
            await h.Command("AckAlarm", leftAlarm.Id); await h.Event("left", "Read", l.AdmissionId, ["000123"], 1); await h.Drain();
            Assert(h.Store.Alarm(leftAlarm.Id) is { AckedBy: not null, ClearedAt: not null } && h.Store.Alarm(rightAlarm.Id) is { AckedBy: null, ClearedAt: null }, "Ack and recovery never bleed across owners");
            Assert((await h.App.DetailsAsync(r.Id)).All(x => x.Request.OwnerId == r.Id), "Detail only contains selected original owner");
        });
        await Check("SQLite local termination rollback and acknowledged restart", async () =>
        {
            var cfg = Rig.Config(url); var directory = Path.Combine(root, "h04-rollback"); string id, payload;
            await using (var h = await Rig.Open(directory, cfg))
            {
                id = (await h.Command("OpenContext", value: "000001")).RequestId!; payload = h.Store.Request(id)!.Payload;
                h.App.Latch("Overload", "SIMULATION pending source inspection");
                using var db = new SqliteConnection("Data Source=" + h.Store.Path); db.Open(); using var sql = db.CreateCommand();
                sql.CommandText = "CREATE TRIGGER fail_local_terminal BEFORE UPDATE ON requests WHEN NEW.status='CancelledBeforeDispatch' BEGIN SELECT RAISE(ABORT,'SIMULATION terminal persistence failure'); END;"; sql.ExecuteNonQuery();
                await h.Command("CancelPrepared", id, "SIMULATION local proof inspected", expect: "StorageFailure");
                Assert(h.Store.Request(id)!.Status == RequestStatus.Prepared && !h.Store.Load().Blocks.Any(x => x.Code == "LocalDispositionRequired"), "Actual SQLite rollback retains Prepared and rolls back owner/block atomically");
                sql.CommandText = "SELECT state FROM outbox WHERE request='" + id + "'";
                Assert((string?)sql.ExecuteScalar() == "Prepared" && !h.Store.Audits(id).Any(x => x.Kind == "CancelPrepared"), "No partial Outbox or false termination audit");
                await h.Command("ClearOverload", value: "SIMULATION cannot acknowledge failed evidence", expect: "RejectedByGuard");
                sql.CommandText = "DROP TRIGGER fail_local_terminal"; sql.ExecuteNonQuery();
            }
            await using (var h = await Rig.Open(directory, cfg))
            {
                Assert(h.App.IsOverloaded && h.Store.Request(id)!.Payload == payload, "Unacknowledged failure and original bytes survive restart");
                await h.Command("CancelPrepared", id, "SIMULATION storage healthy; original local proof inspected");
                await h.Command("ClearOverload", value: "SIMULATION source/entity inspected after persistence recovery");
                Assert((await h.App.DetailsAsync(id)).Single().Audit.Any(x => x.Kind == "CancelPrepared"), "Request detail retains terminal audit by exact request identity");
            }
            await using (var h = await Rig.Open(directory, cfg))
            {
                Assert(!h.App.IsOverloaded && !File.Exists(Path.Combine(directory, "overload-marker")), "Acknowledged overload remains cleared after real reopen");
                Assert(!(await h.App.SnapshotAsync()).Admit.Allowed && h.Store.Request(id)!.Status == RequestStatus.CancelledBeforeDispatch, "Independent disposition hold survives cleared overload and restart");
            }
        });
        if (failures.Count != 0) throw new AggregateException(failures);
    }
    static void Assert(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
}

sealed class TestClock : TimeProvider
{
    DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}
