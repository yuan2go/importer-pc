using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

internal static class DebugNoIsolation31
{
    static void Check(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException(reason); Console.WriteLine("PASS DEBUG31 " + reason); }
    internal static string RuntimeDirectory(BusinessLaunch launch, ParcelConfiguration c) => Path.Combine(launch.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId);
    static string SeedHistoricalAudit(string directory)
    {
        using var repository = new ParcelRepository(directory, "sportmaster");
        var original = Wire.Encode(new { Id = Wire.Id(), CommandId = Wire.Id(), Reason = "Synthetic historical audit", SingleReadRounds = false });
        repository.Commit(null, "CameraIsolationStarted", original, paused: true);
        return original;
    }
    internal static async Task Run(string workspace, string root)
    {
        await using (var h = new ManualSupplyHarness(workspace, Path.Combine(root, "legacy"), true, mode: RunMode.Simulation))
        {
            await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
            var admitted = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "DEBUG31-ORIGINAL"), default);
            await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(admitted.OperationId!))?.Wcs?.State == StageState.Accepted);
            var original = Wire.Encode((await h.App.OperationAsync(admitted.OperationId!))!.Wcs);
            await h.App.DisposeAsync(); var audit = SeedHistoricalAudit(RuntimeDirectory(h.Launch, h.Config));
            await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
            var before = await h.App.SnapshotAsync();
            using (var db = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Path.Combine(RuntimeDirectory(h.Launch, h.Config), "sportmaster.db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                db.Open(); using var query = db.CreateCommand();
                query.CommandText = "SELECT detail FROM parcel_audit WHERE event='CameraIsolationStarted'";
                var retained = (string?)query.ExecuteScalar();
                query.CommandText = "SELECT count(*) FROM parcel_audit WHERE event='CameraIsolationRetired'";
                Check(retained == audit && (long)query.ExecuteScalar()! == 0,
                    "restart preserves historical audit bytes without interpreting or migrating retired isolation state");
            }
            Check(before.Availability!.Configure && !(await h.App.CameraInspectionStatusAsync(default)).BusinessFenced,
                "persisted legacy isolation cannot block an authorized save after restart");
            Check(before.BlockingReasons.All(r => !r.Contains("隔离") && !r.Contains("清场")) && Wire.Encode((await h.App.OperationAsync(admitted.OperationId!))!.Wcs) == original && h.Peer.Requests.Count == 1,
                "restart preserves original request/receipt bytes without replay or a clearance workflow");
            var endpoint = "http://127.0.0.1:9/rcs/sorter/api";
            var candidate = before.Configuration.Effective with { Wcs = before.Configuration.Effective.Wcs! with { Target = endpoint } };
            var saved = await h.App.StageConfigurationAsync(before.Configuration.Revision, candidate, "Synthetic RCS endpoint save after legacy isolation");
            var pending = await h.App.SnapshotAsync();
            Check(saved.Status == "PendingRestart" && pending.Configuration.Pending!.Wcs!.Target == endpoint && pending.Configuration.Effective.Wcs!.Target == h.Peer.Endpoint,
                "actual RCS address save persists a complete Pending candidate and retains old Effective");
            await h.App.DisposeAsync(); await h.StartAsync(ready: false); await ManagementModeFixture.Authorize(h.App);
            var applied = await h.App.SnapshotAsync();
            Check(applied.Configuration.Pending is null && applied.Configuration.Effective.Wcs!.Target == endpoint && applied.Configuration.Status == "Effective",
                "controlled restart activates the saved RCS address without obsolete isolation barriers");
            Check(Wire.Encode((await h.App.OperationAsync(admitted.OperationId!))!.Wcs) == original && h.Peer.Requests.Count == 1,
                "activation never changes or resends the historical original request");
            await h.App.ExitManagementModeAsync();
            Check(!(await h.App.SnapshotAsync()).Availability!.Configure && (await h.App.StageConfigurationAsync(applied.Configuration.Revision, h.Config, "Synthetic unauthorized save")).Status == "Rejected",
                "removing isolation does not grant save rights outside management mode");
        }
        await using (var h = new CameraRoundHarness(workspace, Path.Combine(root, "live")))
        {
            await h.StartAsync(); var before = await h.App.SnapshotAsync(); var scope = await h.BeginAsync();
            Check(!scope.BusinessFenced && (await h.App.SnapshotAsync()).Paused == before.Paused && (await h.App.SnapshotAsync()).Availability!.Configure,
                "scan debugging neither pauses the station nor disables settings");
            try
            {
                await h.BeginAsync();
                throw new InvalidOperationException("Concurrent collection unexpectedly replaced its owner");
            }
            catch (InvalidOperationException error) when (error.Message.Contains("已有调试采集"))
            {
                var retained = await h.App.CameraInspectionStatusAsync(default);
                Check(retained.SessionId == scope.SessionId && retained.State == "Active",
                    "rejected concurrent begin preserves the active collection and its owner");
            }
            await h.ArmAsync(h.Left);
            // Arming debug acquisition completes before the asynchronous camera
            // capability probes necessarily publish production admission readiness.
            await h.WaitForCameraAdmissionAsync();
            h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("DEBUG31-RIGHT"), 1);
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("DEBUG31-LEFT"), 1);
            var locked = await h.LockedAsync();
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.DebugRequest is null && o.Wcs?.State == StageState.Accepted) == 2);
            Check(locked.Frame is not null && locked.RequestBody is not null && h.Peer.Requests.Count == 2,
                "both positions continue production HTTP while the separate debug request stays unsent");
            var unsent = await h.App.SnapshotAsync();
            Check(unsent.Positions!.All(p => p.CurrentRoundId is not null && p.BarcodeInput is { Camera: false, Manual: false }) &&
                (await h.App.OperationAsync(locked.OperationId!))?.Wcs is { State: StageState.Prepared, Attempts: 0 },
                "an unsent debug request adds no production round; both real claimed parcels retain their own submission guards");
            var pixels = await h.App.ReadCameraOriginalAsync(locked.Frame!.Id, default);
            Check(pixels is not null && pixels.Metadata.SourceEventId == locked.Frame.SourceEventId && pixels.Metadata.Symbols.Single().Text == locked.Barcode,
                "debug result retains the exact original image, barcode and recognition geometry");
            var picture = await h.App.ReadParcelPictureAsync(new(OperationId: locked.OperationId), default);
            Check(picture.Status == "Available" && picture.Image?.Metadata.Id == locked.Frame.Id,
                "operation picture uses the exact passive debug session without requiring a retired isolation tag");
            var sent = await h.App.SendCameraRoundAsync(await h.SendCommandAsync(locked), default);
            Check(h.Peer.Requests.Count == 2 && sent.SendAttempts == 0 && sent.HttpResponse is null && !sent.CanSend &&
                (await h.App.OperationAsync(locked.OperationId!))?.Wcs is { State: StageState.Prepared, Attempts: 0 },
                "an explicit debug send cannot add a second real request to a production-claimed arrival");
            var performance = System.Text.Json.JsonSerializer.SerializeToElement(h.App.RuntimeProgress()).GetProperty("Flow").GetProperty("Recent").EnumerateArray().ToArray();
            Check(performance.Count(e => e.GetProperty("Kind").GetString() == "RcsSubmitCompleted") == 2 &&
                performance.Count(e => e.GetProperty("Kind").GetString() == "DebugRcsSubmitCompleted") == 0,
                "a blocked debug intention is never counted as an actual debug submission");
            var decoded = h.Factory.Decoders[h.Left].Count;
            await h.TrayAsync(h.Left, TrayPresence.Absent); await SmRoundFixture.NextVehicleAsync(h.App, "left");
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("DEBUG31-NEXT"), 2);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "DEBUG31-NEXT" && o.Wcs?.State == StageState.Accepted));
            Check(h.Factory.Decoders[h.Left].Count > decoded && (await h.App.ReadCameraReadRoundAsync(default)).Frame!.Id == locked.Frame.Id,
                "locked debug image never freezes normal recognition or gets overwritten by later parcels");
            var sends = h.Peer.Requests.Count; await h.EndAsync();
            Check(!(await h.App.CameraInspectionStatusAsync(default)).BusinessFenced && h.Peer.Requests.Count == sends && (await h.App.SnapshotAsync()).Paused == before.Paused,
                "ending debugging requires no clearance, changes no pause state and replays no request");
            try
            {
                var s = await h.App.SnapshotAsync();
                await h.App.BeginCameraInspectionAsync(new(Wire.Id(), s.RuntimeId, s.Configuration.Revision, CameraInspectionMode.Isolated, "Synthetic retired API", new(), true), default);
                throw new InvalidOperationException("Retired API recreated isolation");
            }
            catch (InvalidOperationException error) when (error.Message.Contains("已删除"))
            { Check(true, "legacy API cannot recreate the removed isolation feature"); }
        }
    }
}
