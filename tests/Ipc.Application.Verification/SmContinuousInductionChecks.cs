using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

namespace Ipc.Verification;

// Real Application, isolated SQLite and built-in HTTP adapter. Physical evidence
// here is controlled; the acquisition implementations are exercised separately.
internal static class SmContinuousInductionChecks
{
    static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS CONTINUOUS " + message); }
    static PositionSnapshot Lane(ParcelSnapshot s) => s.Positions!.Single(p => p.Id == "left");
    public static async Task Run(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "continuous"), deferred: false, mode: RunMode.Simulation);
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config with
        { BarcodeFilter = new("GOOD-[0-9]+", "BOTTOM-[0-9]+"), ReadTimeoutMs = 1000 });
        await h.StartAsync();
        foreach (var camera in h.Factory.Cameras.Values) camera.ConfirmConnected();
        var left = h.Factory.Cameras[h.Config.Topology!.Readers[0].DeviceId];
        async Task Ready() => await CameraRoundHarness.Until(async () => Lane(await h.App.SnapshotAsync()).Arrival?.CameraBoundaryReady == true);
        async Task Deliver(Observation observation)
        {
            var progress = h.App.WorkerProgress().Single(p => p.Position == "left");
            var before = progress.Processed + progress.ObservedOnly;
            Check(left.Deliver(observation), "current physical source hands off input");
            await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(p => p.Position == "left") is var after && after.Processed + after.ObservedOnly > before));
        }
        async Task Presence(TrayPresence state, long? first = null) =>
            await Deliver(left.Capture([], new(state, "qualified-controlled-physical-interval") { FirstPresentTicks = first }));
        async Task Read(params string[] codes) => await Deliver(left.Capture(codes));
        async Task Removed()
        {
            await Presence(TrayPresence.Absent);
            Check(Lane(await h.App.SnapshotAsync()).CurrentRoundId is null, "trusted empty ends local parcel without an operator click");
        }
        async Task<ParcelOperation> Result(string id)
        {
            ParcelOperation? result = null;
            await CameraRoundHarness.Until(async () => (result = await h.App.OperationAsync(id))?.Wcs?.State is StageState.Accepted or StageState.Unknown or StageState.Rejected);
            return result!;
        }
        await Ready();
        long HeartbeatWrites()
        {
            var database = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, h.Config.ApplicationId, "sportmaster.db");
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            db.Open(); using var query = db.CreateCommand();
            query.CommandText = "SELECT count(*) FROM parcel_audit WHERE event='HeartbeatIntentPrepared'";
            return (long)query.ExecuteScalar()!;
        }
        // Warm the durable lease after startup capture commits. Subsequent
        // identical members still perform HTTP but do not journal every tick.
        await h.App.CheckRcsHeartbeatAsync(default);
        var beforeWrites = HeartbeatWrites(); var beforeHeartbeats = h.Peer.HeartbeatRequests.Count;
        for (var i = 0; i < 20; i++) await h.App.CheckRcsHeartbeatAsync(default);
        Check(h.Peer.HeartbeatRequests.Count == beforeHeartbeats + 20 && HeartbeatWrites() == beforeWrites,
            "20 actual unchanged heartbeats retain durable membership intent with zero additional intent writes");
        var vehicle = Lane(await h.App.SnapshotAsync()).Arrival!;
        var repeatedWallTime = DateTimeOffset.UtcNow;
        var emptyDraft = await h.Command("left", "GOOD-1");
        // N03/N04/E01/E02/E06: no-code, illegal and multiple-code parcels can all
        // end on the same unclaimed car; text never serves as a placement identity.
        foreach (var codes in new[] { Array.Empty<string>(), new[] { "INVALID" }, new[] { "GOOD-1", "GOOD-1" } })
        {
            await Presence(TrayPresence.Present); await Read(codes);
            var before = await h.App.SnapshotAsync();
            var oldCommand = await h.Command("left", "GOOD-1");
            var oldFrame = left.Capture(["GOOD-1"]);
            await Deliver(left.Capture([], new(TrayPresence.Absent, "qualified-controlled-empty")) with { ObservedAt = repeatedWallTime });
            await Ready();
            Check(h.Peer.Requests.IsEmpty && Lane(await h.App.SnapshotAsync()) is { BarcodeInput.Camera: true, BarcodeInput.Manual: true } lane &&
                lane.Arrival!.Generation == vehicle.Generation && lane.Arrival.VehicleNo == vehicle.VehicleNo,
                "unsent exception restores real same-car automatic/manual readiness, without enable/disable");
            await Deliver(oldFrame);
            Check((await h.App.SubmitParcelBarcodeAsync(oldCommand, default)).Status == "Conflict" && h.Peer.Requests.IsEmpty,
                "old capture and PDA command cannot cross same-car removal");
            Check((await h.App.SubmitParcelBarcodeAsync(emptyDraft, default)).Status == "Conflict" && Lane(await h.App.SnapshotAsync()).CurrentRoundId is null,
                "empty-lane input identity changes on each physical ending even with repeated wall-clock timestamps");
            emptyDraft = await h.Command("left", "GOOD-1");
            Check((await h.App.RoundHistoryAsync()).Items.Any(r => r.Id == Lane(before).CurrentRoundId && r.EndReason == "TrustedRemoval"),
                "original exception and physical ending remain queryable");
        }
        // R01/E11/E12: frozen T1 intent held immediately before T2, then removed.
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.App.BeforeSmAttemptForVerification = _ => { entered.TrySetResult(); return release.Task; };
        try
        {
            await Read("GOOD-1"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var original = (await h.App.SnapshotAsync()).Rounds.Single().OperationId!;
            await Removed(); await Ready();
            release.TrySetResult();
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity!.ManagedExecutions == 0);
            Check((await h.App.OperationAsync(original))!.Wcs is { Attempts: 0, State: StageState.Cancelled } && h.Peer.Requests.IsEmpty,
                "T1 removal atomically cancels the frozen intent and releases its same-car claim with zero HTTP");
        }
        finally { release.TrySetResult(); h.App.BeforeSmAttemptForVerification = null; }
        var started = Stopwatch.GetTimestamp();
        await Read("GOOD-1");
        var accepted = await Result((await h.App.SnapshotAsync()).Rounds.Single().OperationId!);
        Console.WriteLine("MEASURE synthetic capture-to-durable-result-ms=" + Stopwatch.GetElapsedTime(started).TotalMilliseconds.ToString("F3"));
        await Read("GOOD-1"); await Read("GOOD-2");
        Check(accepted.Wcs is { Attempts: 1, State: StageState.Accepted } && h.Peer.Requests.Count == 1,
            "N01/N02 same-code next physical parcel automatically sends exactly one real HTTP request");
        await Removed(); await Ready(); await Read("GOOD-2");
        Check(h.Peer.Requests.Count == 1 && !Lane(await h.App.SnapshotAsync()).Admit,
            "N05/R05 accepted car remains occupied after physical removal; no second dispatch");

        await h.NextVehicleAsync(); await Ready();
        // E09: continue only the same capture-time occupied interval. Clock is real.
        var firstPresent = Stopwatch.GetTimestamp();
        await Presence(TrayPresence.Present, firstPresent);
        await Task.Delay(650); await Presence(TrayPresence.Present, firstPresent);
        await Task.Delay(450); await Presence(TrayPresence.Present, firstPresent);
        Check((await h.App.SnapshotAsync()).Rounds.Single() is { AutoExpired: true, SafeContinuation: true },
            "E09 fast deadline expires once while original physical identity remains eligible for bounded continuation");
        await Deliver(left.Capture(["GOOD-9"]) with { ContinuousPresenceTicks = firstPresent });
        await Result((await h.App.SnapshotAsync()).Rounds.Single().OperationId!);
        Check(h.Peer.Requests.Count == 2, "E09 valid late decode automatically dispatches once in the original reliable round");
        await h.NextVehicleAsync(); await Ready();
        firstPresent = Stopwatch.GetTimestamp(); await Presence(TrayPresence.Present, firstPresent);
        await Presence(TrayPresence.Unknown);
        await Task.Delay(1050);
        var replacement = Stopwatch.GetTimestamp(); await Presence(TrayPresence.Present, replacement);
        await Deliver(left.Capture(["GOOD-10"]) with { ContinuousPresenceTicks = replacement });
        Check(h.Peer.Requests.Count == 2 && (await h.App.SnapshotAsync()).Rounds.Single() is { OperationId: null, SafeContinuation: false },
            "E10 broken continuity cannot grant a fresh automatic window to a suspected replacement");
        await Removed(); await Ready();

        // R04/R02/S02/S04: local ending and durable remote uncertainty are independent.
        h.Peer.Mode = "reject"; await Read("GOOD-11");
        var rejected = await Result((await h.App.SnapshotAsync()).Rounds.Single().OperationId!);
        await Removed();
        Check(Lane(await h.App.SnapshotAsync()) is { CurrentRoundId: null, Admit: false, Arrival.TargetOperation: not null, Arrival.RecoveryReason: not null },
            "R04 unspecified refusal ends physically but preserves the per-lane remote fence");
        h.Peer.Mode = "assigned";
        var right = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "GOOD-12"), default);
        await Result(right.OperationId!);
        Check(right.Status == "Admitted" && h.Peer.Requests.Count == 4, "S02 right remains productive while left refusal is fenced");
        await ManagementModeFixture.Authorize(h.App);
        var frozen = (await h.App.ReadCameraOperationDetailAsync(rejected.Id, default))!.Original.Wcs!.Request.IntegrationRequest!.Body;
        await h.App.DisposeAsync(); await h.StartAsync();
        await ManagementModeFixture.Authorize(h.App);
        Check(!Lane(await h.App.SnapshotAsync()).Admit && Lane(await h.App.SnapshotAsync()).Arrival!.TargetOperation == rejected.Id &&
            (await h.App.ReadCameraOperationDetailAsync(rejected.Id, default))!.Original.Wcs!.Request.IntegrationRequest!.Body == frozen && h.Peer.Requests.Count == 4,
            "S04 physical end and restart do not lose refusal responsibility, rewrite original bytes or replay HTTP");
        Console.WriteLine("R03 NOT_RUN: customer no-side-effect rejection contract not supplied; no whitelist inferred from success/code/msg");
    }
}
