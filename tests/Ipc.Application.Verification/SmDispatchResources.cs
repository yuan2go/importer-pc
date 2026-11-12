using System.Collections.Concurrent;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Platform;

internal static class SmDispatchResources
{
    static void Check(bool value, string detail)
    { if (!value) throw new InvalidOperationException(detail); Console.WriteLine("PASS SEND-RESOURCE " + detail); }
    public static async Task Run(string workspace, string root)
    {
        await DefaultDeadlineQueuedRemoval(workspace, root);
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "send-congestion"), deferred: true, mode: RunMode.Simulation);
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config with
        { Wcs = h.Config.Wcs! with { Parameters = new(h.Config.Wcs.Parameters) { ["request-timeout-ms"] = "30000", ["heartbeat-interval-ms"] = "100" } } });
        await h.StartAsync();
        var holds = new ConcurrentDictionary<string, TaskCompletionSource>();
        h.Peer.BusinessGate = body => holds.FirstOrDefault(p => body.Contains("\"" + p.Key + "\"", StringComparison.Ordinal)).Value?.Task ?? Task.CompletedTask;
        async Task<ParcelBarcodeReceipt> Submit(string side, string code) => await h.App.SubmitParcelBarcodeAsync(await h.Command(side, code), default);
        TaskCompletionSource Hold(string code) => holds.GetOrAdd(code, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        async Task Fill(string prefix, int before)
        {
            for (var i = 0; i < SportmasterRcsTaskService.MaximumDestinationSends; i++)
            {
                var code = prefix + i; _ = Hold(code);
                await Submit("left", code);
                await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == before + i + 1));
                await SmRoundFixture.ConfirmRemovedAsync(h.App, "left");
            }
        }
        try
        {
            await Fill("SYNTH-HELD-", 0);
            var ceiling = h.Peer.Requests.Count;
            var queued = await Submit("right", "SYNTH-END-WHILE-QUEUED");
            await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Rcs.DispatchProgress.RightWaiting == 1));
            Check((await h.App.OperationAsync(queued.OperationId!))?.Wcs is { Attempts: 0, State: StageState.Prepared } && h.Peer.Requests.Count == ceiling,
                "real HTTP congestion waits before T2; a local permit is not a remote request");
            var heartbeat = h.Peer.HeartbeatRequests.Count;
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.HeartbeatRequests.Count > heartbeat));
            Check(h.Factory.Rcs.DispatchProgress is { Capacity: 7, Occupied: 7, ActiveSends: 7 } && h.Peer.Requests.Count == ceiling,
                "heartbeat remains usable while the bounded destination pool is full; total connection ceiling is unchanged");
            await SmRoundFixture.ConfirmRemovedAsync(h.App, "right");
            await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Rcs.DispatchProgress.RightWaiting == 0));
            Check((await h.App.OperationAsync(queued.OperationId!))?.Wcs is { Attempts: 0, State: StageState.Cancelled },
                "ending an unsent round cancels only its resource wait and retains zero attempts");

            var left = await Submit("left", "SYNTH-FAIR-LEFT");
            var rightGate = Hold("SYNTH-FAIR-RIGHT");
            var right = await Submit("right", "SYNTH-FAIR-RIGHT");
            await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Rcs.DispatchProgress is { LeftWaiting: 1, RightWaiting: 1 }));
            holds["SYNTH-HELD-0"].TrySetResult();
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == ceiling + 1));
            Check(h.Peer.Requests.Last().Contains("SYNTH-FAIR-RIGHT", StringComparison.Ordinal) &&
                (await h.App.OperationAsync(left.OperationId!))?.Wcs?.Attempts == 0 && (await h.App.OperationAsync(right.OperationId!))?.Wcs?.Attempts == 1,
                "one freed actual connection gives the other waiting position its fair turn before the busy left lane");
            rightGate.TrySetResult();
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == ceiling + 2));
            foreach (var hold in holds.Values) hold.TrySetResult();
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity?.ManagedExecutions == 0);
            Check(h.Peer.Requests.Count == ceiling + 2 && !h.Peer.Requests.Any(b => b.Contains("SYNTH-END-WHILE-QUEUED", StringComparison.Ordinal)) &&
                h.Factory.Rcs.DispatchProgress is { Occupied: 0, ActiveSends: 0, LeftWaiting: 0, RightWaiting: 0 },
                "original started calls finish, canceled round never calls later, and every real send owner drains");
            await SmRoundFixture.ConfirmRemovedAsync(h.App, "left"); await SmRoundFixture.ConfirmRemovedAsync(h.App, "right");

            var beforeLoad = h.Peer.Requests.Count;
            var loadClock = System.Diagnostics.Stopwatch.StartNew();
            List<object> loadSamples = [];
            for (var i = 0; i < 100; i++)
            {
                var side = i % 2 == 0 ? "left" : "right";
                var receipt = await Submit(side, "SYNTH-SUSTAINED-" + i);
                await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(receipt.OperationId!))?.Wcs?.State == StageState.Accepted);
                await SmRoundFixture.ConfirmRemovedAsync(h.App, side);
                using var process = System.Diagnostics.Process.GetCurrentProcess(); process.Refresh();
                var progress = h.Factory.Rcs.DispatchProgress;
                if (progress.Occupied > progress.Capacity || progress.ActiveSends > progress.Occupied || progress.LeftWaiting > 128 || progress.RightWaiting > 128)
                    throw new InvalidOperationException("Actual local send ownership exceeded its documented bound");
                loadSamples.Add(new { ElapsedMs = loadClock.Elapsed.TotalMilliseconds, progress,
                    process.PrivateMemorySize64, process.WorkingSet64, process.HandleCount, Threads = process.Threads.Count, Runtime = h.App.RuntimeProgress() });
            }
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity?.ManagedExecutions == 0);
            Check(h.Peer.Requests.Count == beforeLoad + 100 && h.Factory.Rcs.DispatchProgress is { Occupied: 0, ActiveSends: 0, LeftWaiting: 0, RightWaiting: 0 },
                "100 alternating independent manual rounds each make one actual POST and all managed send owners drain");
            ExtensionStore.AtomicWrite(Path.Combine(root, "send-resource-load.json"), new
            {
                Scope = "SYNTHETIC_SHORT_LOAD_ONLY; loopback peer shares this process; no field 24/72-hour qualification",
                Rounds = 100, ElapsedMs = loadClock.Elapsed.TotalMilliseconds, Samples = loadSamples
            });

            // Exercise actual Stop with both network calls and a queued unsent round.
            var beforeStop = h.Peer.Requests.Count;
            await Fill("SYNTH-STOP-", beforeStop);
            var pending = await Submit("right", "SYNTH-STOP-UNSENT");
            await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Rcs.DispatchProgress.RightWaiting == 1));
            await h.App.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(10));
            var directory = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, h.Config.ApplicationId);
            using var repository = new ParcelRepository(directory, h.Config.ApplicationId);
            Check(repository.ReadOperation(pending.OperationId!)?.Wcs?.Attempts == 0 &&
                h.Factory.Rcs.DispatchProgress is { Occupied: 0, ActiveSends: 0, LeftWaiting: 0, RightWaiting: 0 },
                "Stop drains real calls and queued reservations without inventing an attempt for the unsent round");
            Check(h.Peer.Requests.Count == beforeStop + SportmasterRcsTaskService.MaximumDestinationSends && !h.App.FatalFailure.IsCompleted,
                "congestion, removal and stop create no extra POST, retry or false fatal storage failure");
        }
        finally { foreach (var hold in holds.Values) hold.TrySetResult(); h.Peer.BusinessGate = null; }
    }
    static async Task DefaultDeadlineQueuedRemoval(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "default-deadline-queue"), deferred: true, mode: RunMode.Simulation);
        await h.StartAsync();
        await ManagementModeFixture.Authorize(h.App);
        var seed = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-PERMIT-SEED"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(seed.OperationId!))?.Wcs?.State == StageState.Accepted);
        var request = (await h.App.OperationAsync(seed.OperationId!))!.Wcs!.Request;
        Check(new Ipc.Application.Sportmaster.SportmasterFlow().RequestTimeoutMs((await h.App.OperationAsync(seed.OperationId!))!, ParcelStage.Wcs) == 1000,
            "omitted setting freezes the actual production request's effective deadline at 1000ms");
        await SmRoundFixture.ConfirmRemovedAsync(h.App, "left");
        // Occupy the real bounded resource without starting HTTP calls. The
        // existing congestion scenario separately holds real network responses.
        List<IWcsDispatchPermit> permits = [];
        try
        {
            for (var i = 0; i < SportmasterRcsTaskService.MaximumDestinationSends; i++)
                permits.Add(await h.Factory.Rcs.ReserveDispatchAsync(request, default));
            var queued = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-DEFAULT-REMOVED"), default);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Rcs.DispatchProgress.LeftWaiting == 1));
            await SmRoundFixture.ConfirmRemovedAsync(h.App, "left");
            await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Rcs.DispatchProgress.LeftWaiting == 0));
            Check((await h.App.OperationAsync(queued.OperationId!))?.Wcs is { Attempts: 0, State: StageState.Cancelled } && h.Peer.Requests.Count == 1,
                "1000ms default and round removal cancel the real pre-attempt wait with zero additional POSTs");
            foreach (var permit in permits) permit.Dispose(); permits.Clear();
            var next = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "SYNTH-DEFAULT-NEXT"), default);
            await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(next.OperationId!))?.Wcs?.State == StageState.Accepted);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Rcs.DispatchProgress.Occupied == 0));
            Check(h.Peer.Requests.Count == 2 && !h.Peer.Requests.Any(b => b.Contains("SYNTH-DEFAULT-REMOVED", StringComparison.Ordinal)),
                "released resources serve the other position once without reviving the ended request");
        }
        finally { foreach (var permit in permits) permit.Dispose(); }
    }
}
