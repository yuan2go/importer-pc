using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
namespace Ipc.Verification;

internal static class SmRoundLifecycle
{
    static void Check(bool ok, string detail)
    { if (!ok) throw new InvalidOperationException(detail); Console.WriteLine("PASS SMROUND " + detail); }
    static SmParcelRound? Current(ParcelSnapshot s, string side = "left") => s.Rounds.SingleOrDefault(r => r.PositionId == side);
    public static async Task Run(string workspace, string root)
    {
        var clock = new TimeStorageFaultChecks.ManualTime();
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "round-lifecycle"), deferred: false, mode: RunMode.Simulation) { StationClock = clock };
        var config = h.Config with { BarcodeFilter = new("(?:GOOD-[0-9]+|BG[0-9]+)", "BG[0-9]+"), ReadTimeoutMs = 1000,
            Retention = h.Config.Retention! with { HistoryDays = 7 } };
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), config);
        await h.StartAsync();
        foreach (var camera in h.Factory.Cameras.Values) camera.ConfirmConnected();
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.All(p => p.BarcodeInput?.Camera == true));
        var left = h.Factory.Cameras[h.Config.Topology!.Readers[0].DeviceId];
        async Task<ParcelSnapshot> Observe(string[] codes, TrayPresence? presence = null)
        {
            var before = h.App.WorkerProgress().Single(p => p.Position == "left").Processed;
            Check(left.Read(codes, presence is { } fact ? new(fact, "synthetic-qualified-presence") : null), "controlled source hands off a complete observation");
            await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(p => p.Position == "left").Processed > before));
            return await h.App.SnapshotAsync();
        }
        async Task<ParcelOperation> Resolved(string id) => await WaitOperation(id, op => op.Wcs?.State is StageState.Accepted or StageState.Rejected or StageState.Unknown);
        async Task<ParcelOperation> WaitOperation(string id, Func<ParcelOperation, bool> ready)
        {
            ParcelOperation? result = null;
            await CameraRoundHarness.Until(async () => (result = await h.App.OperationAsync(id)) is { } op && ready(op));
            return result!;
        }
        async Task Remove()
        {
            var snapshot = await h.App.SnapshotAsync();
            var target = snapshot.Positions!.Single(p => p.Id == "left").PlacementRemovalOperation;
            Check(target is not null, "unknown visual evidence exposes a targeted round-removal action");
            var id = Current(snapshot)!.Id;
            await CameraRoundHarness.Until(async () =>
            {
                var latest = await h.App.SnapshotAsync();
                if (Current(latest)?.Id != id) return Current(latest) is null;
                var receipt = await h.App.CommandAsync(new(Wire.Id(), latest.Revision, "ConfirmPlacementRemoval", "left", latest.Positions!.Single(p => p.Id == "left").PlacementRemovalOperation));
                return receipt.Status == "Persisted" && Current(await h.App.SnapshotAsync()) is null;
            });
            Check(Current(await h.App.SnapshotAsync()) is null, "explicit removal ends only its target round");
        }
        var s = await Observe([]);
        s = await Observe(["BG123", "ordinary-invalid"]);
        clock.Elapse(TimeSpan.FromSeconds(2));
        Check(Current(await h.App.SnapshotAsync()) is null && h.Peer.Requests.IsEmpty, "T05 unknown presence and no formal candidates create neither a parcel nor a timeout");

        s = await Observe(["ordinary-invalid", "BG123", "GOOD-100"]);
        var r = Current(s)!; var first = await Resolved(r.OperationId!);
        Check(r.FormalCodes.SequenceEqual(["GOOD-100"]) && first.ParcelCode == "GOOD-100" && h.Peer.Requests.Count == 1,
            "T01 all candidates are filtered before quantity/UI/request selection and produce one actual POST");
        await Observe([], TrayPresence.Absent);
        s = await Observe([], TrayPresence.Present); var correction = Current(s)!;
        s = await Observe(["GOOD-200", "GOOD-200"]);
        Check(Current(s) is { OperationId: null, FormalCodes.Length: 2 } duplicate && duplicate.Id == correction.Id,
            "T02 two physical same-text labels remain two candidates and do not dispatch");
        clock.Elapse(TimeSpan.FromMilliseconds(200)); s = await Observe(["bad"]);
        Check(Current(s)?.Id == correction.Id, "T03 invalid/multiple feedback remains the same round");
        s = await Observe(["GOOD-201"]); var corrected = await Resolved(Current(s)!.OperationId!);
        Check(corrected.ParcelCode == "GOOD-201" && corrected.RoundId == correction.Id && h.Peer.Requests.Count == 2,
            "T03 correction within the original window claims exactly one request");

        await Observe([], TrayPresence.Absent); s = await Observe([], TrayPresence.Present); var expiredId = Current(s)!.Id;
        clock.Elapse(TimeSpan.FromMilliseconds(999)); await Observe(["GOOD-301", "GOOD-302"]);
        clock.Elapse(TimeSpan.FromMilliseconds(2)); s = await h.App.SnapshotAsync();
        Check(Current(s) is { AutoExpired: true, State: SmRoundState.ReadException } expired && expired.Id == expiredId,
            "T04 read deadline is independent of frames and candidate changes");
        await Observe(["GOOD-303"]);
        Check(h.Peer.Requests.Count == 2, "T04 a late automatic decode cannot claim a request");
        var pda = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "GOOD-304", ParcelBarcodeInputKind.PdaKeyboard), default);
        var supplemented = await Resolved(pda.OperationId!);
        Check(pda.Status == "Admitted" && supplemented.RoundId == expiredId && supplemented.OperatorInput?.Kind == "PdaKeyboard" && h.Peer.Requests.Count == 3,
            "T04 PDA supplements the same expired round without moving the parcel");
        await Observe(["GOOD-305"]);
        Check(h.Peer.Requests.Count == 3, "T06 camera cannot append another submission after PDA owns the round");
        await Observe([], TrayPresence.Absent);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.App.BeforeSmAttemptForVerification = _ => { entered.TrySetResult(); return ready.Task; };
        try
        {
            var prepared = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "GOOD-350"), default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var unsent = (await h.App.OperationAsync(prepared.OperationId!))!;
            Check(unsent.Wcs is { Attempts: 0, State: StageState.Prepared }, "T07 frozen request exists before the executor's final send decision");
            await Remove(); ready.TrySetResult();
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity!.ManagedExecutions == 0);
            Check(h.Peer.Requests.Count == 3 && (await h.App.OperationAsync(unsent.Id))!.Wcs is { Attempts: 0, State: StageState.Cancelled },
                "T07 ending before first attempt closes the intent with zero external calls");
        }
        finally { ready.TrySetResult(); h.App.BeforeSmAttemptForVerification = null; }

        h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Peer.HoldWhen = body => body.Contains("GOOD-400", StringComparison.Ordinal);
        try
        {
            var a = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "GOOD-400"), default);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 4));
            var snapshot = await h.App.SnapshotAsync(); var oldTarget = snapshot.Positions!.Single(p => p.Id == "left").PlacementRemovalOperation;
            await Observe([], TrayPresence.Absent);
            s = await Observe(["GOOD-401"]); var b = Current(s)!;
            var bResult = await Resolved(b.OperationId!);
            Check(h.Peer.Requests.Count == 5 && !h.Peer.Hold.Task.IsCompleted && bResult.Slot is not null,
                "T08 same-position B actually receives a result while departed A is still in flight");
            var right = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "GOOD-402"), default);
            await Resolved(right.OperationId!);
            Check(h.Peer.Requests.Count == 6, "T13 right-position manual input completes independently of held left A");
            var before = await h.App.SnapshotAsync();
            var stale = await h.App.CommandAsync(new(Wire.Id(), before.Revision, "ConfirmPlacementRemoval", "left", oldTarget));
            Check(stale.Status != "Persisted" && Current(await h.App.SnapshotAsync())?.Id == b.Id, "T10 an old removal button cannot end B");
            h.Peer.Hold.TrySetResult(); await Resolved(a.OperationId!);
            Check(Current(await h.App.SnapshotAsync())?.Id == b.Id && (await h.App.OperationAsync(bResult.Id))!.Slot == bResult.Slot,
                "T09 late A result stays in A's record and cannot replace B");
        }
        finally { h.Peer.Hold?.TrySetResult(); h.Peer.Hold = null; h.Peer.HoldWhen = null; }
        await Remove();

        var command = await h.Command("left", "GOOD-500");
        var success = await h.App.SubmitParcelBarcodeAsync(command, default); await Resolved(success.OperationId!);
        var count = h.Peer.Requests.Count;
        clock.Elapse(TimeSpan.FromMilliseconds(2999)); Check(Current(await h.App.SnapshotAsync()) is not null, "T11 manual result retains its own display before 3000 ms");
        clock.Elapse(TimeSpan.FromMilliseconds(1)); Check(Current(await h.App.SnapshotAsync()) is null, "T11 manual/unknown-visual result ends at 3000 ms");
        await Observe(["GOOD-500"]);
        Check(Current(await h.App.SnapshotAsync()) is null && h.Peer.Requests.Count == count, "T11 continued camera reads remain suppressed after display reset");
        var replay = await h.App.SubmitParcelBarcodeAsync(command, default);
        Check(replay.OperationId == success.OperationId && h.Peer.Requests.Count == count, "T15 original CommandId replay returns its durable receipt without resending");
        var sameCodeNew = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "GOOD-500"), default); await Resolved(sameCodeNew.OperationId!);
        Check(sameCodeNew.OperationId != success.OperationId && h.Peer.Requests.Count == count + 1, "T11 a new complete manual same-code submission is a distinct round");
        await Remove();

        h.Peer.Mode = "reject";
        var refused = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "GOOD-600"), default); await Resolved(refused.OperationId!);
        var failedRound = Current(await h.App.SnapshotAsync())!;
        s = await Observe([], TrayPresence.Absent);
        Check(Current(s)?.Id == failedRound.Id && Current(s)?.RequireRemoval == true, "T12 visual absence cannot replace current manual failure confirmation");
        await Remove(); h.Peer.Mode = "assigned";

        await Observe([], TrayPresence.Present); clock.Elapse(TimeSpan.FromMilliseconds(1001));
        s = await h.App.SnapshotAsync();
        await Observe([], TrayPresence.Unknown);
        await Remove();
        Check(Current(s) is { OperationId: null, AutoExpired: true } && (await h.App.RoundHistoryAsync()).Items.Any(x => x.OperationId is null && x.EndReason == "OperatorConfirmedRemoval"),
            "T04 an unread round can be manually ended as unsent after visual evidence becomes unknown");
        var final = await h.App.SnapshotAsync();
        Check(final.Capacity is { CurrentRounds: 0, ManagedExecutions: 0, ExecutingRequests: 0 }, "T17 current occupancy and actual execution responsibilities are separate");
        Check((await h.App.RoundHistoryAsync()).Items.All(x => x.OperationId is null || x.AttemptStarted || x.EndReason == "OperatorConfirmedRemoval"),
            "T17 round history distinguishes attempted requests from locally closed unsent intents");

        // Already-ended manual requests cannot revive a confirmation branch.
        foreach (var mode in new[] { "reject", "malformed", "timeout" })
        {
            var baseline = h.Peer.Requests.Count;
            h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Peer.HoldWhen = body => body.Contains("GOOD-700", StringComparison.Ordinal);
            try
            {
                var old = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "GOOD-700"), default);
                await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == baseline + 1));
                await Observe([], TrayPresence.Absent);
                var newer = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "GOOD-701"), default);
                await Resolved(newer.OperationId!);
                var newerRound = Current(await h.App.SnapshotAsync())!;
                h.Peer.Mode = mode == "timeout" ? "assigned" : mode;
                if (mode == "timeout") await Resolved(old.OperationId!);
                h.Peer.Hold.TrySetResult(); await Resolved(old.OperationId!);
                var after = Current(await h.App.SnapshotAsync())!;
                Check(after.Id == newerRound.Id && !after.RequireRemoval && h.Peer.Requests.Count == baseline + 2,
                    "T09/T12 late old " + mode + " stays in its original record without reviving a removal branch");
            }
            finally { h.Peer.Hold?.TrySetResult(); h.Peer.Hold = null; h.Peer.HoldWhen = null; h.Peer.Mode = "assigned"; }
            await Remove();
        }
        var reset = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "GOOD-800"), default); await Resolved(reset.OperationId!);
        clock.Elapse(TimeSpan.FromMilliseconds(3000)); _ = await h.App.SnapshotAsync();
        var beforeRestart = h.Peer.Requests.Count;
        await h.App.DisposeAsync(); await h.StartAsync();
        left = h.Factory.Cameras[h.Config.Topology!.Readers[0].DeviceId]; left.ConfirmConnected();
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").BarcodeInput?.Camera == true);
        await Observe(["GOOD-800"]);
        Check(Current(await h.App.SnapshotAsync()) is null && h.Peer.Requests.Count == beforeRestart,
            "T16 restart preserves automatic suppression and does not replay an ended round");
        h.Peer.Mode = "reject";
        var requiring = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "GOOD-801"), default); await Resolved(requiring.OperationId!);
        var requiredId = Current(await h.App.SnapshotAsync())!.Id;
        await h.App.DisposeAsync(); await h.StartAsync(ready: false);
        Check(Current(await h.App.SnapshotAsync()) is { RequireRemoval: true } restored && restored.Id == requiredId && h.Peer.Requests.Count == beforeRestart + 1,
            "T15/T16 restart restores the exact current manual-removal responsibility without a second call");
        await Remove(); h.Peer.Mode = "assigned";
        left = h.Factory.Cameras[h.Config.Topology!.Readers[0].DeviceId]; left.ConfirmConnected();
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").BarcodeInput?.Manual == true);
        var baselineCompetition = h.Peer.Requests.Count;
        var competingCommand = await h.Command("left", "GOOD-901", ParcelBarcodeInputKind.PdaKeyboard);
        await Task.WhenAll(Observe(["GOOD-900"]), h.App.SubmitParcelBarcodeAsync(competingCommand, default));
        var competed = Current(await h.App.SnapshotAsync())!;
        var winner = await Resolved(competed.OperationId!);
        Check(h.Peer.Requests.Count == baselineCompetition + 1 && winner.RoundId == competed.Id && winner.ParcelCode is "GOOD-900" or "GOOD-901",
            "T06 competing actual camera handoff and PDA command share one durable submission claim");
        await Remove();
        var imageEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? readEvidenceRound = null;
        var imageRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.App.BeforeSmRoundImageForVerification = _ => { imageEntered.TrySetResult(); return imageRelease.Task; };
        try
        {
            await Observe([], TrayPresence.Present);
            var oldImageRound = Current(await Observe(["GOOD-910", "GOOD-911"]))!.Id;
            readEvidenceRound = oldImageRound;
            var pictureQuery = new ParcelPictureQuery(ObservationId: Current(await h.App.SnapshotAsync())!.Observation!.Id);
            var returnedImage = (await h.App.ReadParcelPictureAsync(pictureQuery, default)).Image!;
            var originalByte = returnedImage.Gray8[0]; returnedImage.Gray8[0] ^= 255;
            Check((await h.App.ReadParcelPictureAsync(pictureQuery, default)).Image!.Gray8[0] == originalByte,
                "T10 a caller cannot mutate the current round's retained original pixels");
            await imageEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Observe([], TrayPresence.Absent);
            var nextImageRound = Current(await Observe(["GOOD-912"]))!;
            await Resolved(nextImageRound.OperationId!); imageRelease.TrySetResult();
            await CameraRoundHarness.Until(async () => (await h.App.RoundHistoryAsync()).Items.Single(r => r.Id == oldImageRound).OriginalFrame?.State == "Saved");
            Check(Current(await h.App.SnapshotAsync())?.Id == nextImageRound.Id && (await h.App.ReadParcelPictureAsync(new(RoundId: oldImageRound), default)).Archived?.RoundId == oldImageRound,
                "T10 late image completion updates only its ended original round and remains exactly queryable");
            Check((await h.App.RoundHistoryAsync(search: "GOOD-910")).Items.Single().Id == oldImageRound,
                "T17 formal-candidate search includes an unsubmitted multi-code round without inventing a selected barcode");
        }
        finally { imageRelease.TrySetResult(); h.App.BeforeSmRoundImageForVerification = null; }
        await Remove();
        h.App.SmExecutionCapacityForVerification = 2;
        h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Peer.HoldWhen = body => body.Contains("GOOD-930", StringComparison.Ordinal) || body.Contains("GOOD-931", StringComparison.Ordinal);
        var beforeCapacity = h.Peer.Requests.Count;
        try
        {
            foreach (var code in new[] { "GOOD-930", "GOOD-931" })
            {
                var receipt = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", code), default);
                await WaitOperation(receipt.OperationId!, op => op.Wcs?.State == StageState.Dispatching);
                await Remove();
            }
            var capacityRound = Current(await Observe(["GOOD-932"]))!;
            Check(capacityRound.OperationId is null && capacityRound.ReadOutcome == "ExecutionCapacityBlocked" && h.Peer.Requests.Count == beforeCapacity + 2,
                "T13 actual task saturation refuses before claiming/sending and retains a correctable current round");
            var other = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "GOOD-933"), default);
            await Resolved(other.OperationId!);
            Check(h.Peer.Requests.Count == beforeCapacity + 3 && !h.Peer.Hold.Task.IsCompleted,
                "T13 a saturated left execution allowance cannot consume the right position's allowance");
            h.Peer.Hold.TrySetResult();
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity!.ManagedExecutions == 0);
            var rightRemoval = await h.App.SnapshotAsync();
            var capacitySubmission = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "GOOD-934", ParcelBarcodeInputKind.PdaKeyboard), default);
            var completed = await Resolved(capacitySubmission.OperationId!);
            Check(completed.RoundId == capacityRound.Id && h.Peer.Requests.Count == beforeCapacity + 4,
                "T13 draining actual owners releases capacity and permits one PDA claim in the same unsent round");
            Check((await h.App.CommandAsync(new(Wire.Id(), rightRemoval.Revision, "ConfirmPlacementRemoval", "right",
                rightRemoval.Positions!.Single(p => p.Id == "right").PlacementRemovalOperation))).Status == "Persisted" &&
                Current(await h.App.SnapshotAsync())?.Id == capacityRound.Id,
                "T10 another lane's receipt revision cannot invalidate an exact round/version removal or clear its neighbor");
            await Remove();
        }
        finally { h.Peer.Hold.TrySetResult(); h.Peer.Hold = null; h.Peer.HoldWhen = null; h.App.SmExecutionCapacityForVerification = 128; }
        await ManagementModeFixture.Authorize(h.App);
        var beforeBackup = await h.App.SnapshotAsync();
        Check((await h.App.CommandAsync(new(Wire.Id(), beforeBackup.Revision, "Pause"))).Status == "Persisted", "T17 backup uses the original paused custody fence");
        var backup = Path.Combine(root, "round-custody.db");
        var backupReceipt = await LifecycleOptimization.AfterMonitorDrain(h.App, () => h.App.BackupAsync(backup, default));
        Check(backupReceipt.Status == "BackupCreated" && ParcelRecovery.InspectAnalysisBackup(backup, new Ipc.Application.Sportmaster.SportmasterFlow()).DatabaseVersion == ParcelRepository.CurrentStorageVersion,
            "T17 complete backup verifies round/image custody together with original requests, receipts and permanent numbering");
        await h.App.DisposeAsync();
        var directory = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, "sportmaster");
        using (var repo = new ParcelRepository(directory, "sportmaster"))
        {
            var evidence = repo.Round(readEvidenceRound!)!;
            repo.Commit(null, "SyntheticRoundAge", "Isolated retention chronology only", round: evidence with { Version = evidence.Version + 1,
                StartedAt = DateTimeOffset.UtcNow.AddDays(-10), EndedAt = DateTimeOffset.UtcNow.AddDays(-10).AddSeconds(1) });
        }
        var database = Path.Combine(directory, "sportmaster.db");
        var retentionBackup = Path.Combine(root, "round-retention.db");
        ParcelRepository.ExpireHistoryAuthorized(database, Wire.FileHash(database), retentionBackup,
            new Ipc.Application.Sportmaster.SportmasterFlow(), (_, _) => { }, false, () => { });
        using (var repo = new ParcelRepository(directory, "sportmaster"))
        {
            Check(repo.Round(readEvidenceRound!) is { Observation: null, OriginalFrame.State: "Retired", EndedAt: not null },
                "T17 verified stopped retention compacts expired unsubmitted evidence while preserving round identity/outcome");
            Check(!File.Exists(Path.Combine(directory, "images", "original-frames", "round." + readEvidenceRound + ".png")) &&
                ParcelRecovery.InspectAnalysisBackup(retentionBackup, new Ipc.Application.Sportmaster.SportmasterFlow()).DatabaseVersion == ParcelRepository.CurrentStorageVersion,
                "T17 round image deletion follows the completed original backup, whose original custody still verifies");
        }
        await VerifySlowReaders(workspace, root);
    }

    // A controlled qualified presence producer decorates the real acquisition
    // source; it does not classify pixels or change the driver's SDK/decoder.
    sealed class PresenceSource(IControlledObservationSource inner) : IControlledObservationSource, IOriginalFrameSource
    {
        Func<Observation, bool>? receiver; long sequence;
        public string DeviceId => inner.DeviceId; public string Epoch => inner.Epoch; public string ConfirmationSource => inner.ConfirmationSource;
        public Task StartControlledAsync(string epoch, Func<Observation, bool> receive, Action<DeviceHealthConfirmation> health, CancellationToken token)
        {
            receiver = receive; sequence = 0;
            return inner.StartControlledAsync(epoch, observation => receive(observation with { Sequence = Interlocked.Increment(ref sequence) }), health, token);
        }
        public bool Present()
        {
            var id = Wire.Id();
            return receiver!(new(id, DeviceId, Epoch, "presence." + id, Interlocked.Increment(ref sequence), "presence." + id, "Presence", [], null, DateTimeOffset.UtcNow)
            { TrayPresence = new(TrayPresence.Present, "synthetic-qualified-presence-before-decode") });
        }
        public void EnableOriginalFrames() => ((IOriginalFrameSource)inner).EnableOriginalFrames();
        public IOriginalFrameLease RetainOriginalFrame(AcquisitionIdentity identity) => ((IOriginalFrameSource)inner).RetainOriginalFrame(identity);
        public void ReleaseOriginalFrame(AcquisitionIdentity identity) => ((IOriginalFrameSource)inner).ReleaseOriginalFrame(identity);
        public Task StopAsync(CancellationToken token) => inner.StopAsync(token);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    static async Task VerifySlowReaders(string workspace, string root)
    {
        foreach (var native in new[] { false, true })
        {
            var sources = new Dictionary<string, PresenceSource>();
            await using var h = new CameraRoundHarness(workspace, Path.Combine(root, native ? "slow-native" : "slow-software"), nativeResults: native)
            { ConfigureFactory = factory => factory.DecorateReader = inner => sources[inner.DeviceId] = new PresenceSource(inner) };
            await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
            Check(sources[h.Left].Present(), "T18 qualified presence arrives before the slow " + (native ? "native" : "software") + " decode");
            await CameraRoundHarness.Until(async () => Current(await h.App.SnapshotAsync()) is not null);
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            void Hold() { entered.Set(); release.Wait(); }
            if (native) h.Api(h.Left).BeforeAcquireReturn = Hold; else h.Factory.Decoders[h.Left].BeforeDecode = Hold;
            try
            {
                var pixels = CameraRoundHarness.Barcode("SYNTH-SLOW-ROUND");
                if (native) h.Api(h.Left).EnqueueNative(pixels, 1, [new("SYNTH-SLOW-ROUND", "Code128", [new(10, 10), new(400, 10), new(400, 100), new(10, 100)], 0)]);
                else h.Api(h.Left).Enqueue(pixels, 1);
                Check(entered.Wait(TimeSpan.FromSeconds(5)), "T18 actual acquisition/decoder enters the controlled slow boundary");
                await CameraRoundHarness.Until(async () => Current(await h.App.SnapshotAsync())?.AutoExpired == true);
                release.Set();
                await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(w => w.Position == "left").Processed >= 2));
                var round = Current(await h.App.SnapshotAsync());
                Check(round is { AutoExpired: true, OperationId: null } && h.Peer.Requests.IsEmpty,
                    "T18 deadline advances without another decoded frame and rejects the late " + (native ? "native" : "software") + " result");
            }
            finally { release.Set(); h.Api(h.Left).BeforeAcquireReturn = null; h.Factory.Decoders[h.Left].BeforeDecode = null; }
        }
    }
}
