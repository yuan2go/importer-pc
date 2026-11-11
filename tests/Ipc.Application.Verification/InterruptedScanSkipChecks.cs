using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static class InterruptedScanSkipChecks
{
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS SCAN-SKIP " + message); }
    static RecognitionSymbol Symbol(string code) => new(code, "Code128", [new(20, 20), new(120, 20), new(120, 60), new(20, 60)], 0);
    static string DirectoryFor(CameraRoundHarness h) => Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, "sportmaster");
    static SqliteConnection Open(CameraRoundHarness h)
    { var db = new SqliteConnection("Data Source=" + Path.Combine(DirectoryFor(h), "sportmaster.db")); db.Open(); return db; }
    static long Scalar(SqliteConnection db, string sql) { using var c = db.CreateCommand(); c.CommandText = sql; return (long)c.ExecuteScalar()!; }
    static Task Delivered(CameraRoundHarness h, string position, long count) => CameraRoundHarness.Until(() =>
    { var w = h.App.WorkerProgress().Single(w => w.Position == position); return Task.FromResult(w.Processed >= count && w.State == "Waiting"); });
    public static async Task Run(string workspace, string root)
    {
        await Restart(workspace, Path.Combine(root, "restart"));
        await ExpiredPlacement(workspace, Path.Combine(root, "expired"));
        await IndependentRequests(workspace, Path.Combine(root, "independent"));
    }
    static async Task Restart(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, root, nativeResults: true);
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        var s = await h.App.SnapshotAsync();
        await h.App.CommandAsync(new(Wire.Id(), s.Revision, "Pause"));
        var pixels = CameraRoundHarness.Barcode("SYNTH-SKIP-A");
        h.Api(h.Left).EnqueueNative(pixels, 1, [Symbol("SYNTH-SKIP-A")]); await Delivered(h, "left", 1);
        await h.App.ExitManagementModeAsync(); await h.App.DisposeAsync();
        using (var db = Open(h))
        {
            using var c = db.CreateCommand();
            c.CommandText = "UPDATE parcel_ingress SET state='Interrupted',completed_at=NULL";
            c.ExecuteNonQuery();
            foreach (var id in new[] { "synthetic-queued", "synthetic-started" })
            {
                var observation = new Observation(Wire.Id(), h.Right, "synthetic-old-epoch", id, 1, id, "Read", ["SYNTH-OLD-RIGHT"], null, DateTimeOffset.UtcNow.AddMinutes(-1));
                var body = Wire.Encode(new { Observation = observation, Operator = (object?)null, ReceivedAt = observation.ObservedAt });
                using var insert = db.CreateCommand();
                insert.CommandText = "INSERT INTO parcel_ingress(id,position,received_at,state,body,body_hash,session,started_at) VALUES($id,'right',$at,'Pending',$body,$hash,'synthetic-old-run',$start)";
                insert.Parameters.AddWithValue("$id", id); insert.Parameters.AddWithValue("$at", observation.ObservedAt.ToString("O"));
                insert.Parameters.AddWithValue("$body", body); insert.Parameters.AddWithValue("$hash", Wire.Hash(body));
                insert.Parameters.AddWithValue("$start", id == "synthetic-started" ? observation.ObservedAt.ToString("O") : DBNull.Value);
                insert.ExecuteNonQuery();
            }
        }
        using (var repo = new ParcelRepository(DirectoryFor(h), "sportmaster", ["left", "right"]))
            repo.Commit(null, "IngressRecoveryRequired", "Synthetic previous build input barrier", paused: true);
        await h.StartAsync(authorizeManagement: false, supervisedRestart: true); await h.WaitForCameraAdmissionAsync();
        s = await h.App.SnapshotAsync();
        Check(!s.Paused && s.Positions!.All(p => p.Admit) && !s.BlockingReasons.Any(r => r.StartsWith("IngressInterrupted:")),
            "both positions resume automatically after old Pending and Interrupted scans, without a management command");
        Check(!(await h.App.ManagementModeStatusAsync()).Active && h.Peer.Requests.Count == 0 && s.Operations.Length == 0 && s.Inputs!.Length == 0,
            "startup neither replays the old scans nor requires operator management authorization");
        Check(s.State == "Ready" && !s.BlockingReasons.Any(r => r.Contains("RecoveryReview")),
            "supervised restart does not introduce a station-wide recovery pause for skipped camera receipts");
        using (var db = Open(h))
            Check(Scalar(db, "SELECT count(*) FROM parcel_ingress WHERE state='Processed' AND detail LIKE 'SkippedPreviousCameraInput%'") == 3 &&
                Scalar(db, "SELECT count(*) FROM parcel_ingress") == 3,
                "automatic skip retains every original row and hash instead of deleting input evidence");
        await CameraRoundHarness.Until(() =>
        {
            using var reader = new StreamReader(new FileStream(Path.Combine(DirectoryFor(h), "diagnostics", "runtime.jsonl"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            var log = reader.ReadToEnd();
            return Task.FromResult(log.Contains("已自动跳过上次未完成的扫码") && log.Contains("synthetic-started"));
        });
        Check(true, "key logs identify automatic skips and the preserved original input identities without granting operator management access");
        h.Api(h.Left).EnqueueNative(pixels, 1, [Symbol("SYNTH-SKIP-A")]); await Delivered(h, "left", 1);
        h.Api(h.Left).EnqueueNative(pixels, 2, [Symbol("SYNTH-SKIP-A")]); await Delivered(h, "left", 2);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-SKIP-A" && o.State == ParcelState.SlotResolved));
        Check(h.Peer.Requests.Count == 1 && (await h.App.SnapshotAsync()).Operations.Single().State == ParcelState.SlotResolved,
            "a fresh parcel completes normally while continuous same-placement reads still request only once");
        h.Peer.ReplySlot = "";
        h.Api(h.Left).EnqueueNative(pixels, 3, [Symbol("SYNTH-SKIP-UNKNOWN")]); await Delivered(h, "left", 3);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-SKIP-UNKNOWN" && o.State == ParcelState.WcsUnknown));
        var unknown = (await h.App.SnapshotAsync()).Operations.Single(o => o.State == ParcelState.WcsUnknown);
        var request = Wire.Encode(unknown.Wcs!.Request); var replies = Wire.Encode(unknown.Wcs.Replies);
        await h.App.DisposeAsync();
        using (var db = Open(h))
        {
            using var c = db.CreateCommand(); c.CommandText = "UPDATE parcel_ingress SET state='Pending',completed_at=NULL WHERE id=(SELECT id FROM parcel_ingress ORDER BY received_at DESC LIMIT 1)"; c.ExecuteNonQuery();
        }
        using (var repo = new ParcelRepository(DirectoryFor(h), "sportmaster", ["left", "right"]))
            repo.Commit(null, "IngressRecoveryRequired", "Synthetic receipt lost after original request", paused: true);
        await h.StartAsync(authorizeManagement: false);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "right").Admit);
        s = await h.App.SnapshotAsync(); var original = s.Operations.Single(o => o.Id == unknown.Id);
        Check(!s.Paused && !s.Positions!.Single(p => p.Id == "left").Admit && s.Positions!.Single(p => p.Id == "right").Admit,
            "an original uncertain result keeps its own position guard without pausing the healthy position");
        Check(h.Peer.Requests.Count == 2 && original.Wcs!.Attempts == 1 && Wire.Encode(original.Wcs.Request) == request && Wire.Encode(original.Wcs.Replies) == replies,
            "skip does not claim non-delivery, change the original request or reply, or resend an unknown result");
        h.Peer.ReplySlot = "SYNTH-NEXT";
        h.Api(h.Right).EnqueueNative(pixels, 1, [Symbol("SYNTH-SKIP-NEXT")]); await Delivered(h, "right", 1);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-SKIP-NEXT" && o.State == ParcelState.SlotResolved));
        Check(h.Peer.Requests.Count == 3 && (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-SKIP-NEXT" && o.State == ParcelState.SlotResolved),
            "the healthy position continues processing new parcels without entering management");
    }
    static async Task ExpiredPlacement(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, root, nativeResults: true, timeoutMs: 1000);
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        var delay = 0; var pixels = CameraRoundHarness.Barcode("SYNTH-SLOW-A");
        using var entered = new ManualResetEventSlim();
        h.App.Boundary = point => { if (point == "BeforeIngressDurable" && Interlocked.Exchange(ref delay, 1) == 0) { entered.Set(); Thread.Sleep(5500); } };
        h.Api(h.Left).EnqueueNative(pixels, 1, [Symbol("SYNTH-SLOW-A")]);
        await CameraRoundHarness.Until(() => Task.FromResult(entered.IsSet));
        var rightPixels = CameraRoundHarness.Barcode("SYNTH-RIGHT-FAST");
        h.Api(h.Right).EnqueueNative(rightPixels, 1, [Symbol("SYNTH-RIGHT-FAST")]); await Delivered(h, "right", 1);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-RIGHT-FAST" && o.State == ParcelState.SlotResolved));
        var right = (await h.App.SnapshotAsync()).Operations.Single(o => o.PositionId == "right");
        Check(right.State == ParcelState.SlotResolved && h.App.WorkerProgress().Single(w => w.Position == "left").Processed == 0,
            "right camera completes its own RCS request while left camera durable handoff is stalled for 5.5 seconds");
        await Delivered(h, "left", 1); h.App.Boundary = null;
        var s = await h.App.SnapshotAsync();
        var leftInput = s.Inputs!.Single(i => i.PositionId == "left");
        Check(!s.Paused && leftInput.Reason.StartsWith("ObservationExpired;") && h.Peer.Requests.Count == 1 && s.Operations.All(o => o.PositionId != "left"),
            "a 5.5 second stalled scan is skipped locally instead of pausing supply or sending an expired request");
        ParcelPictureResult? rightPicture = null;
        await CameraRoundHarness.Until(async () => (rightPicture = await h.App.ReadParcelPictureAsync(new(OperationId: right.Id), default)).Status == "Available");
        Check((await h.App.ReadParcelPictureAsync(new(ObservationId: leftInput.Observation.Id), default)).Image!.Gray8.SequenceEqual(pixels.Data) &&
            rightPicture!.Archived?.Metadata.RawSha256 == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rightPixels.Data)),
            "skipped left parcel and successful right parcel retain their own full images without cross-camera substitution");
        var observed = h.App.WorkerProgress().Single(w => w.Position == "left").ObservedOnly;
        h.Api(h.Left).EnqueueNative(pixels, 2, [Symbol("SYNTH-SLOW-A")]);
        await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(w => w.Position == "left").ObservedOnly > observed));
        Check(h.Peer.Requests.Count == 1, "the skipped parcel stays skipped until removed; a repeated read cannot silently submit it");
        for (uint f = 3; f <= 5; f++)
        {
            h.Api(h.Left).EnqueueNative(CameraRoundHarness.EmptyTray, f, []);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).CameraReads.Any(r => r.PositionId == "left" && r.Frame.FrameNumber == f));
            if (f < 5) await Task.Delay(425);
        }
        h.Api(h.Left).EnqueueNative(pixels, 6, [Symbol("SYNTH-SLOW-A")]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-SLOW-A" && o.State == ParcelState.SlotResolved));
        Check(h.Peer.Requests.Count == 2 && !(await h.App.SnapshotAsync()).Paused,
            "three delivered empty frames spanning 800ms permit re-placement and exactly one fresh request without a resume command");
    }
    static async Task IndependentRequests(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, root, nativeResults: true);
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Peer.Hold = held; h.Peer.HoldWhen = body => body.Contains("TEST-LEFT-READER", StringComparison.Ordinal) && body.Contains("sorter.task.dest_request", StringComparison.Ordinal);
        var pixels = CameraRoundHarness.Barcode("SYNTH-BOTH-SAME");
        try
        {
            h.Api(h.Left).EnqueueNative(pixels, 1, [Symbol("SYNTH-BOTH-SAME")]);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 1));
            h.Api(h.Right).EnqueueNative(pixels, 1, [Symbol("SYNTH-BOTH-SAME")]); await Delivered(h, "right", 1);
            await Delivered(h, "left", 1);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.PositionId == "right" && o.State == ParcelState.SlotResolved));
            var s = await h.App.SnapshotAsync();
            Check(h.Peer.Requests.Count == 2 && s.Operations.Single(o => o.PositionId == "right").State == ParcelState.SlotResolved &&
                s.Operations.Single(o => o.PositionId == "left").Wcs is { Attempts: 1, State: StageState.Dispatching } && h.App.WorkerProgress().Single(w => w.Position == "left").Processed == 1,
                "left input completes while its RCS response remains pending; identical codes in different positions remain independent placements");
            h.Api(h.Right).EnqueueNative(pixels, 2, [Symbol("SYNTH-BOTH-SAME")]); await Delivered(h, "right", 2);
            Check(h.Peer.Requests.Count == 2, "right repeated observation is deduplicated locally while left original response remains pending");
        }
        finally { held.TrySetResult(); h.Peer.Hold = null; }
        await Delivered(h, "left", 1);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.All(o => o.State == ParcelState.SlotResolved));
        Check((await h.App.SnapshotAsync()).Operations.All(o => o.State == ParcelState.SlotResolved) && h.Peer.Requests.Count == 2,
            "late left response completes its original task without replacing right result or resubmitting either parcel");
    }
}
