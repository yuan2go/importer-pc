using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;

static class ClosureVerification
{
    public static async Task Run(string root, string url, string workspace)
    {
        await Check("E-01 current input invalidation, legitimate correction and original receipts", () => CurrentInput(root, url));
        await Check("E-03 source/epoch/evidence CAS and Unknown isolation", () => SourceReview(root, url));
        await Check("E-05 source silence, replay/future rejection and controlled reconnect", () => SourceHealth(root, url));
        await Check("E-06 empty-session close chain, exact queries, paging and retention boundary", () => History(root, url));
        await Check("E-08 ordered cue ownership, replay deduplication and bounded overflow", () => Notifications(root, url));
        await Check("E-09 independent stop/dispose failures and retained active ownership", () => Resources(root, url));
    }

    static async Task CurrentInput(string root, string url)
    {
        await using var h = await Rig.New(root, url); await h.OpenBox(); var op = await h.Enter("left");
        await h.Event("left", "Read", op.AdmissionId, ["000123"], 1); await h.Drain();
        var confirmed = await h.Op("left"); var task = confirmed.Task!;
        var validation = (await h.App.DetailsAsync(op.Id)).Single(x => x.Request.Kind == RequestKind.Validate);
        await h.Command("SetFixtureFault", value: "Reject"); await h.Command("Submit", op.Id); await h.Drain();
        Assert((await h.Op("left")).Status == OperationStatus.Rejected, "Actual receiver business rejection precedes correction");
        var originals = (await h.App.DetailsAsync(op.Id)).Select(x => x.Request).ToArray();
        foreach (var codes in new[] { Array.Empty<string>(), new[] { "000123", "000456" }, new[] { "illegal" } })
        {
            await h.Event("left", "Read", op.AdmissionId, codes, 1); var current = await h.Op("left");
            Assert(current.Status == OperationStatus.ValidationRejected && current.CurrentInput!.Codes.SequenceEqual(codes), "Latest invalid observation stays explicit");
            Assert(current.Task is null && current.Package is null && current.Remaining is null && current.Supply is null && current.Barcode == "", "New invalid input withdraws old current task/quantities/barcode");
            Assert(current.PreviousConfirmed?.Task == task && current.PreviousConfirmed.Barcode == "000123" && current.PreviousConfirmed.Supply == 1, "Last proven semantic and quantity snapshot remains historical");
        }
        await h.Event("left", "Read", op.AdmissionId, ["000456"], 2);
        var pending = await h.Op("left");
        Assert(pending.Status == OperationStatus.ValidationPending && pending.Barcode == "000456" && pending.Package == 2 && pending.Task is null && pending.Remaining is null && pending.Supply is null, "Legitimate corrected input has no authority until its own new receipt");
        await h.App.CompleteAsync(validation.Request, null, validation.Responses.Single());
        var afterLate = await h.Op("left");
        Assert(afterLate.Revision == pending.Revision && afterLate.CurrentInput!.ObservationId == pending.CurrentInput!.ObservationId && afterLate.Status == OperationStatus.ValidationPending && afterLate.Task is null, "Old duplicate receipt cannot reactivate old task or replace latest input");
        await h.Drain(); var revised = await h.Op("left");
        Assert(revised.Status == OperationStatus.AwaitingAction && revised.Task is not null && revised.Task != task && revised.Supply == 2, "Corrected input receives its own task and quantity authority");
        foreach (var original in originals) Assert(h.Store.Request(original.Id) == original, "Correction preserves original request identity/payload/target/contract");
        Assert((await h.App.DetailsAsync(op.Id)).Length == 3 && h.Store.AcceptedQuantity(op.SessionId) == 0, "Validation/rejection/correction chains do not invent acceptance");
    }

    static ObservationReviewSelection Select(BlockReason block) => new(block.Code, block.Scope, block.Source, block.SourceEpoch, block.EvidenceVersion);
    static async Task SourceReview(string root, string url)
    {
        await using var h = await Rig.New(root, url); await h.OpenBox(); var op = await h.Enter("left");
        await h.Event("left", "Read", op.AdmissionId, ["000123"], 1); await h.Drain();
        await h.Command("SetFixtureFault", value: "Drop"); await h.Command("Submit", op.Id); await h.Drain();
        var unknown = (await h.App.UnknownRequestsAsync()).Single();
        async Task Old(string lane)
        {
            var device = h.ConfigValue.Devices.Single(x => x.Role == lane);
            await h.App.ReceiveAsync(new(Wire.Id(), device.DeviceId, "obsolete-" + lane, Wire.Id(), 999, Wire.Id(), "Enter", [], null, DateTimeOffset.UtcNow));
        }
        await Old("left"); var reviewed = await h.App.SnapshotAsync(); var old = reviewed.State.Blocks.Single(x => x.Code == "ObservationReview");
        var staleCommand = new Command(Wire.Id(), reviewed.State.Revision, "ReviewObservations", Value: Wire.Encode(new ObservationReviewEvidence([Select(old)], "SIMULATION inspected old left source")));
        await Old("left"); await Old("right"); var current = await h.App.SnapshotAsync();
        Assert((await h.App.ExecuteAsync(staleCommand)).Status == "Conflict", "Old projection cannot clear newer source facts");
        Assert((await h.App.ExecuteAsync(staleCommand with { Id = Wire.Id(), ExpectedRevision = current.State.Revision })).Status == "RejectedByGuard", "Current command revision still rejects an old evidence version");
        var left = current.State.Blocks.Single(x => x.Code == "ObservationReview" && x.Scope == "left");
        Assert(left.EvidenceVersion > old.EvidenceVersion, "Repeated source rejection advances evidence version");
        var command = new Command(Wire.Id(), current.State.Revision, "ReviewObservations", Value: Wire.Encode(new ObservationReviewEvidence([Select(left)], "SIMULATION inspected current left source and entity")));
        var receipt = await h.App.ExecuteAsync(command); Assert(receipt.Status == "Persisted" && await h.App.ExecuteAsync(command) == receipt, "Source review has one durable idempotent command receipt");
        var result = await h.App.SnapshotAsync();
        Assert(result.State.Blocks.Count(x => x.Code == "ObservationReview") == 1 && result.State.Blocks.Single(x => x.Code == "ObservationReview").Scope == "right", "Review only clears selected source/epoch/version");
        Assert((await h.App.ExecuteAsync(command with { Value = command.Value + " " })).Status == "Conflict", "Reusing review command ID with changed content conflicts");
        await Expect<GuardException>(() => h.App.ReviewObservationsAsync("SIMULATION unversioned review must fail"));
        Assert((await h.App.UnknownRequestsAsync()).Single() == unknown && await h.App.ClaimAsync() is null && h.Store.AcceptedQuantity(unknown.SessionId) == 0, "Review cannot release Unknown, resend it or invent acceptance");
    }

    static async Task SourceHealth(string root, string url)
    {
        var clock = new TestClock(); var cfg = Rig.Config(url);
        await using var h = await Rig.Open(Path.Combine(root, "closure-health"), cfg, clock: clock);
        await h.OpenBox(); var op = await h.Enter("left"); await h.Event("left", "Read", op.AdmissionId, ["000123"], 1); await h.Drain();
        h.AutoConfirmSyntheticHealth = false;
        var fresh = await h.App.SnapshotAsync(); var left = fresh.State.Devices["left"];
        clock.Advance(TimeSpan.FromSeconds(6));
        var stale = await h.App.SnapshotAsync(); var anotherRead = await h.App.SnapshotAsync();
        Assert(stale.GeneratedAt > fresh.GeneratedAt && anotherRead.State.Devices["left"].ConfirmedAt == left.ConfirmedAt && stale.OperationGuards["left"].Reasons.Contains("Device:left:Expired"), "Fresh projections never renew silent source health");
        await h.App.SetDeviceHealthAsync(new(left.DeviceId, left.Epoch, DeviceHealth.Ready, left.ConfirmedAt!.Value, clock.GetUtcNow().AddMinutes(1), left.Source));
        Assert((await h.App.SnapshotAsync()).State.Devices["left"] == left, "Equal-time replay cannot extend validity");
        await h.App.SetDeviceHealthAsync(new(left.DeviceId, left.Epoch, DeviceHealth.Ready, clock.GetUtcNow().AddSeconds(1), clock.GetUtcNow().AddSeconds(6), left.Source));
        Assert((await h.App.SnapshotAsync()).State.Devices["left"] == left, "Future source confirmation is rejected");
        await h.App.SetDeviceHealthAsync(new(left.DeviceId, left.Epoch, DeviceHealth.Ready, clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(5), "unregistered-source"));
        Assert((await h.App.SnapshotAsync()).State.Devices["left"] == left, "Unregistered source cannot renew valid epoch");
        foreach (var device in fresh.State.Devices.Values)
            await h.App.SetDeviceHealthAsync(new(device.DeviceId, device.Epoch, DeviceHealth.Ready, clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(5), device.Source));
        await h.App.SetWcsHealthAsync(true);
        Assert((await h.App.SnapshotAsync()).OperationGuards["left"].Allowed, "New reliable source confirmation restores health independently of identity/entity permission");
        var nextEpoch = Wire.Id(); const string nextSource = "SIMULATION:controlled-reconnect";
        await Expect<GuardException>(() => h.App.RegisterDeviceEpochAsync(left.DeviceId, "wrong-previous", nextEpoch, nextSource));
        await h.App.RegisterDeviceEpochAsync(left.DeviceId, left.Epoch, nextEpoch, nextSource);
        Assert((await h.App.SnapshotAsync()).State.Devices["left"] is { Health: DeviceHealth.Connecting, ConfirmedAt: null }, "Controlled CAS registration creates reachable unconfirmed new connection");
        await h.App.SetDeviceHealthAsync(new(left.DeviceId, left.Epoch, DeviceHealth.Faulted, clock.GetUtcNow(), null, left.Source));
        Assert((await h.App.SnapshotAsync()).State.Devices["left"].Epoch == nextEpoch, "Old health callback cannot replace registered epoch");
        await h.App.SetDeviceHealthAsync(new(left.DeviceId, nextEpoch, DeviceHealth.Ready, clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(5), nextSource));
        var reconnected = await h.App.SnapshotAsync();
        Assert(reconnected.OperationGuards["left"].Reasons.Any(x => x.StartsWith("EntityEpochChanged")), "Healthy reconnect does not renew old physical entity permit");
        var oldCallback = new Observation(Wire.Id(), left.DeviceId, left.Epoch, Wire.Id(), 9999, op.AdmissionId, "Read", ["999999"], 9, clock.GetUtcNow());
        await h.App.ReceiveAsync(oldCallback);
        Assert(h.Store.HasObservation(oldCallback) && (await h.Op("left")).Barcode == "000123" && (await h.App.SnapshotAsync()).State.Blocks.Any(x => x.Code == "ObservationReview"), "Late old observation is retained for review without overwriting task");
        h.Epochs["left"] = nextEpoch;
        await h.Event("left", "Revalidate", op.AdmissionId); await h.ReviewSources("SIMULATION inspected old callback and current entity permit");
        Assert((await h.App.SnapshotAsync()).OperationGuards["left"].Allowed && (await h.Op("left")).PermitEpoch == nextEpoch, "Explicit synthetic entity revalidation plus exact source review restores only permitted work");
    }

    static async Task Close(Rig h)
    {
        await h.Command("CheckClose"); await h.Drain(); var session = (await h.App.SnapshotAsync()).State.Session!;
        await h.Command("ConfirmClose", session.CloseCheckRequest, "CONFIRM"); await h.Drain();
        Assert((await h.App.SnapshotAsync()).State.Session!.Status == SessionStatus.Closed, "Independent close acceptance closes synthetic session");
    }
    static async Task History(string root, string url)
    {
        await using var h = await Rig.New(root, url); var closed = new List<string>();
        for (var index = 0; index < 3; index++)
        {
            await h.Command("OpenContext", value: "00000" + (index + 1)); await h.Drain();
            var session = (await h.App.SnapshotAsync()).State.Session!; closed.Add(session.Id);
            await Close(h);
            var detail = await h.App.SessionDetailsAsync(session.Id);
            Assert(detail.OperationCount == 0 && detail.AcceptedQuantity == 0 && detail.OpenedAt is not null && detail.Session.Barcode == session.Barcode, "Empty closed box is independently discoverable with original identity/time");
            Assert(detail.Requests.Select(x => x.Request.Kind).SequenceEqual([RequestKind.Context, RequestKind.CheckClose, RequestKind.Close]) && detail.Requests.All(x => x.Attempts.Length == 1 && x.Responses.Length == 1), "Empty box retains full Context/CheckClose/Close/Attempt/Response chain");
        }
        var first = await h.App.QueryHistoryAsync(new(Limit: 1));
        Assert(first.TotalCount == 3 && first.HasNext && first.Query.ThroughRowId is > 0 && first.Query.ThroughId is not null, "First query freezes explicit paging identity boundary");
        await h.Command("OpenContext", value: "000004"); await h.Drain();
        var fourth = (await h.App.SnapshotAsync()).State.Session!;
        var second = await h.App.QueryHistoryAsync(first.Query with { Offset = 1 });
        var third = await h.App.QueryHistoryAsync(first.Query with { Offset = 2 });
        Assert(second.TotalCount == 3 && third.TotalCount == 3 && !third.HasNext && new[] { first, second, third }.SelectMany(x => x.Items).Select(x => x.Id).ToHashSet().SetEquals(closed), "Paging excludes later parent insertion and has no overlap or omitted empty sessions");
        Assert((await h.App.QueryHistoryAsync(new(BoxBarcode: "000004", SessionStatus: SessionStatus.Active))).Items.Single().Id == fourth.Id, "Exact box/status filters reach current session");
        Assert((await h.App.QueryHistoryAsync(new(BoxBarcode: "00000"))).TotalCount == 0, "Typed barcode filter never searches arbitrary JSON substrings");
        Assert((await h.App.QueryHistoryAsync(new(ItemBarcode: "000123"))).TotalCount == 0, "Empty sessions do not fabricate item children");
        var time = fourth.OpenedAt.ToOffset(TimeSpan.FromHours(8));
        Assert((await h.App.QueryHistoryAsync(new(FromInclusive: time.AddSeconds(-1), UntilExclusive: time.AddSeconds(1), BoxBarcode: fourth.Barcode))).Items.Single().Id == fourth.Id, "Date filters compare absolute instants with explicit timezone offsets");
        await Expect<ArgumentException>(() => h.App.QueryHistoryAsync(new(FromInclusive: time, UntilExclusive: time)));
        await Expect<ArgumentOutOfRangeException>(() => h.App.QueryHistoryAsync(new(Limit: 201)));
        await Expect<KeyNotFoundException>(() => h.App.SessionDetailsAsync("missing-session"));
        Assert((await h.App.QueryHistoryAsync(new(RequestKind: RequestKind.Close, RequestStatus: RequestStatus.Accepted))).TotalCount == 3, "Session request filters discover historical close chain independently of operations");
        var removed = h.Store.PruneClosedSessions(DateTimeOffset.UtcNow.AddDays(1), "SIMULATION verification", "Synthetic retention boundary deliberately removed");
        Assert(removed == 3 && h.Store.Session(fourth.Id) is not null && h.Store.Requests().All(x => x.SessionId == fourth.Id), "Retention removes entire closed aggregates and preserves active context");
        await Expect<InvalidOperationException>(() => h.App.QueryHistoryAsync(first.Query with { Offset = 1 }));
        Assert((await h.App.QueryHistoryAsync(new())).Items.Single().Id == fourth.Id, "New query recovers explicitly after original paging boundary removal");
    }

    static async Task Notifications(string root, string url)
    {
        await using (var h = await Rig.New(root, url))
        {
            await h.OpenBox(); var left = await h.Enter("left"); var right = await h.Enter("right");
            foreach (var op in new[] { left, right })
            {
                await h.Command("SetFixtureFault", value: "Reject"); await h.Event(op.Lane, "Read", op.AdmissionId, ["000123"], 1); await h.Drain();
            }
            var first = (await h.App.PeekNotificationAsync())!;
            Assert(first.OperationId == left.Id && first.Lane == "left" && first.Status == OperationStatus.ValidationRejected && await h.App.PeekNotificationAsync() == first, "Unplayed cue remains exact queue head across polls/consumers");
            await Expect<GuardException>(() => h.App.AcknowledgeNotificationAsync(first.Sequence + 1));
            var detail = (await h.App.DetailsAsync(left.Id)).Single(); await h.App.CompleteAsync(detail.Request, null, detail.Responses.Single());
            Assert((await h.App.SnapshotAsync()).NotificationBacklog == 2, "Repeated persisted evidence never duplicates necessary cue");
            await h.App.AcknowledgeNotificationAsync(first.Sequence); var second = (await h.App.PeekNotificationAsync())!;
            Assert(second.Sequence > first.Sequence && second.OperationId == right.Id && second.Lane == "right", "Adjacent two-lane results preserve order and both source identities");
            await Expect<GuardException>(() => h.App.AcknowledgeNotificationAsync(first.Sequence));
            await h.App.AcknowledgeNotificationAsync(second.Sequence);
            Assert(await h.App.PeekNotificationAsync() is null, "Only ordered acknowledgement consumes successful playback");
        }
        var cfg = Rig.Config(url); var directory = Path.Combine(root, "closure-cue-overflow");
        await using (var h = await Rig.Open(directory, cfg))
        {
            await h.OpenBox(); var op = await h.Enter("left");
            for (var index = 0; index < 65; index++) await h.Event("left", "Read", op.AdmissionId, [], 1);
            var full = await h.App.SnapshotAsync();
            Assert(full.NotificationBacklog == 64 && full.NotificationOverflow == 1 && full.RuntimeFaults.Any(x => x.StartsWith("OperatorNotificationBacklog")) && !full.Admit.Allowed, "Cue capacity cannot silently drop required feedback or continue admission");
            var head = (await h.App.PeekNotificationAsync())!;
            await h.App.AcknowledgeNotificationAsync(head.Sequence);
            Assert((await h.App.SnapshotAsync()).NotificationBacklog == 63 && !(await h.App.SnapshotAsync()).Admit.Allowed, "Freeing cue capacity does not pretend the lost feedback was delivered or clear independent failure");
        }
        await using (var restarted = await Rig.Open(directory, cfg))
            Assert((await restarted.App.SnapshotAsync()).NotificationBacklog == 0 && await restarted.App.PeekNotificationAsync() is null && (await restarted.App.HistoryAsync()).Length == 1, "Restart preserves business history without replaying every historical cue");
    }

    static async Task Resources(string root, string url)
    {
        var cfg = Rig.Config(url);
        using var store = new SupplyStore(Path.Combine(root, "closure-resource-stop", "station.db"));
        await using var app = new StationApplication(store, cfg, new SimulationRequestContract(cfg));
        // Module resource ownership is tested against real workers and the real app/store,
        // using controlled synthetic sources with intentionally failing lifecycle methods.
        var good = new LifecycleSource(cfg.Devices[0].DeviceId);
        var broken = new LifecycleSource(cfg.Devices[1].DeviceId) { StopFailure = true };
        var module = new ObservationModule(app, [good, broken], 8);
        await module.StartAsync(CancellationToken.None);
        var stopped = await Capture(() => module.StopAsync(CancellationToken.None));
        Assert(stopped.ToString().Contains("SIMULATION source stop") && good.StopCount == 1 && broken.StopCount == 1 && module.HasActiveWork && module.OwnershipDiagnostics.Any(x => x.Contains(broken.DeviceId)), "Failed source stop preserves original failure and owned callback while independent source stops");
        var disposed = await Capture(() => module.DisposeAsync().AsTask());
        Assert(disposed.ToString().Contains("SIMULATION source stop") && good.DisposeCount == 1 && broken.DisposeCount == 0 && module.HasUnreleasedResources, "Independent stopped source disposes; failed source remains owned and undisposed");
        broken.StopFailure = false; await module.DisposeAsync();
        Assert(!module.HasActiveWork && !module.HasUnreleasedResources && broken.DisposeCount == 1, "Successful later stop/dispose reaches observable resource terminal state");
        // A distinct adapter disposal failure cannot skip release of another source.
        var disposeCfg = Rig.Config(url);
        using var disposeStore = new SupplyStore(Path.Combine(root, "closure-resource-dispose", "station.db"));
        await using var disposeApp = new StationApplication(disposeStore, disposeCfg, new SimulationRequestContract(disposeCfg));
        var one = new LifecycleSource(disposeCfg.Devices[0].DeviceId) { DisposeFailure = true };
        var two = new LifecycleSource(disposeCfg.Devices[1].DeviceId);
        var disposeModule = new ObservationModule(disposeApp, [one, two], 8);
        await disposeModule.StartAsync(CancellationToken.None);
        var failure = await Capture(() => disposeModule.DisposeAsync().AsTask());
        Assert(failure.ToString().Contains("SIMULATION source dispose") && one.DisposeCount == 1 && two.DisposeCount == 1 && !disposeModule.HasActiveWork && disposeModule.HasUnreleasedResources, "Adapter dispose failure retains failure and attempts every independent release");
        one.DisposeFailure = false; await disposeModule.DisposeAsync();
        Assert(one.DisposeCount == 2 && two.DisposeCount == 1 && !disposeModule.HasUnreleasedResources, "Retry only unreleased source; already disposed owner is not repeated");
        // A timed-out call remains one live task. No new stop/dispose may race it.
        var timedCfg = Rig.Config(url);
        using var timedStore = new SupplyStore(Path.Combine(root, "closure-resource-timeout", "station.db"));
        await using var timedApp = new StationApplication(timedStore, timedCfg, new SimulationRequestContract(timedCfg));
        var held = new LifecycleSource(timedCfg.Devices[0].DeviceId) { StopRelease = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var independent = new LifecycleSource(timedCfg.Devices[1].DeviceId);
        var timed = new ObservationModule(timedApp, [held, independent], 8); await timed.StartAsync(CancellationToken.None);
        try
        {
            var timeout = await Capture(() => timed.StopAsync(CancellationToken.None));
            Assert(timeout.ToString().Contains("TimeoutException") && held.StopCount == 1 && independent.StopCount == 1 && timed.HasActiveWork, "Stop timeout does not cancel live ownership or skip independent stop");
            held.StopRelease.SetResult(); await timed.DisposeAsync();
            Assert(held.StopCount == 1 && held.DisposeCount == 1 && !timed.HasUnreleasedResources && !timed.HasActiveWork, "Late completion reuses original stop task, then releases resource exactly once");
        }
        finally { held.StopRelease.TrySetResult(); await timed.DisposeAsync(); }
        var calls = new List<string>();
        var a = new LifecycleModule("a", calls) { DisposeFailure = true };
        var b = new LifecycleModule("b", calls) { StopFailure = true };
        var catalog = new ModuleCatalog([a, b]); await catalog.StartAsync(CancellationToken.None);
        var catalogFailure = await Capture(() => catalog.DisposeAsync().AsTask());
        Assert(catalogFailure.ToString().Contains("SIMULATION module stop") && catalogFailure.ToString().Contains("SIMULATION module dispose") && calls.Contains("a:dispose") && calls.Contains("b:dispose"), "Catalog aggregates original stop/dispose failures while attempting independent modules");
        a.DisposeFailure = false; b.StopFailure = false; await catalog.DisposeAsync();
        Assert(!catalog.HasActiveWork && !catalog.HasUnreleasedResources, "Catalog retry reaches final state without forgetting incomplete resources");
    }

    static async Task Check(string name, Func<Task> action) { Console.WriteLine("RUN E07 " + name); await action(); Console.WriteLine("PASS E07 " + name); }
    static void Assert(bool value, string reason) { if (!value) throw new InvalidOperationException("ASSERT E07: " + reason); }
    static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    static async Task<Exception> Capture(Func<Task> action) { try { await action(); } catch (Exception failure) { return failure; } throw new InvalidOperationException("Injected lifecycle failure did not execute"); }
}

sealed class LifecycleSource(string deviceId) : IControlledObservationSource
{
    public string DeviceId { get; } = deviceId;
    public string Epoch { get; private set; } = "not-started";
    public string ConfirmationSource => "SIMULATION:lifecycle-probe";
    public bool StopFailure { get; set; }
    public bool DisposeFailure { get; set; }
    public TaskCompletionSource? StopRelease { get; set; }
    public int StopCount { get; private set; }
    public int DisposeCount { get; private set; }
    public Task StartControlledAsync(string epoch, Func<Observation, bool> receive, Action<DeviceHealthConfirmation> health, CancellationToken token)
    {
        Epoch = epoch; var now = DateTimeOffset.UtcNow;
        health(new(DeviceId, Epoch, DeviceHealth.Ready, now, now.AddSeconds(5), ConfirmationSource)); return Task.CompletedTask;
    }
    public async Task StopAsync(CancellationToken token)
    {
        StopCount++; if (StopFailure) throw new InvalidOperationException("SIMULATION source stop failure " + DeviceId);
        if (StopRelease is not null) await StopRelease.Task;
    }
    public ValueTask DisposeAsync()
    {
        DisposeCount++; if (DisposeFailure) throw new InvalidOperationException("SIMULATION source dispose failure " + DeviceId);
        return ValueTask.CompletedTask;
    }
}

sealed class LifecycleModule(string id, List<string> calls) : IRuntimeModule, IRuntimeOwnership
{
    public string Id => id;
    public string[] Dependencies => [];
    public bool StopFailure { get; set; }
    public bool DisposeFailure { get; set; }
    bool active, released;
    public bool HasActiveWork => active;
    public bool HasUnreleasedResources => !released;
    public string[] OwnershipDiagnostics => active ? [id + ": synthetic active owner"] : [];
    public Task StartAsync(CancellationToken token) { active = true; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken token) { calls.Add(id + ":stop"); if (StopFailure) throw new InvalidOperationException("SIMULATION module stop failure " + id); active = false; return Task.CompletedTask; }
    public ValueTask DisposeAsync() { calls.Add(id + ":dispose"); if (DisposeFailure) throw new InvalidOperationException("SIMULATION module dispose failure " + id); active = false; released = true; return ValueTask.CompletedTask; }
}
