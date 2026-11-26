using Ipc.Application.Contracts;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

internal static class SmFieldReadiness
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS P1 " + message);
    }

    public static async Task Run(string workspace, string root)
    {
        await Discovery();
        await Calibration(workspace, root);
    }

    static async Task Discovery()
    {
        var devices = new CameraRoundHarness.RoundApi(new("camera", "hikrobot-mvs-reader", "left", "synthetic"))
        {
            EnumerationOverride = () =>
            [
                new("192.0.2.10", "Z10", new string('M', 90) + "\n", "5\t.0", [10]),
                new("192.0.2.2", "Z2A", "Model", "Firmware", [2]),
                new("192.0.2.2", "Z2B", "Model", "Firmware", [2]),
                new("192.0.2.21", "bad\rserial", "Model", "Firmware", []),
                new("0.0.0.0", "NONE", "", "", []),
                new("255.255.255.255", "BROADCAST", "", "", []),
                new("239.1.1.1", "MULTICAST", "", "", [])
            ]
        };
        await using var lookup = new CameraIdentityLookup(_ => devices);
        var found = lookup.Discover("zxing-cpp");
        Check(found.Select(camera => camera.Serial).SequenceEqual(["Z2A", "Z2B", "Z10"]) &&
            found[0].HasIpConflict && found[1].HasIpConflict && !found[2].HasIpConflict &&
            found[2].Model.Length == 80 && found[2].Firmware == "5.0",
            "numeric IPv4 order, duplicate IP rows, valid serials and cleaned SDK labels");
        devices.EnumerationOverride = () => [];
        Check(lookup.Discover("mvid-native").Length == 0, "no camera enumerates as empty");
        devices.EnumerationOverride = () => throw new IOException("synthetic enumeration failure");
        try { _ = lookup.Discover("mvid-native"); throw new InvalidOperationException("Discovery error was swallowed"); }
        catch (IOException error) when (error.Message == "synthetic enumeration failure")
        {
            Check(devices.Finalizes == 3 && devices.Creates == 0 && devices.Opens == 0 && devices.Starts == 0,
                "every enumeration releases SDK ownership without creating, opening or starting a camera");
        }
    }

    static async Task Calibration(string workspace, string root)
    {
        await using var fixture = new CameraRoundHarness(workspace, Path.Combine(root, "disabled-lane"))
        { ConfigureFactory = factory => factory.UseAcquisitionDirectly = true };
        fixture.Peer.HeartbeatTransform = body => body.Replace("\"TASKING\"", "\"IDLE\"", StringComparison.Ordinal);
        await fixture.StartAsync(authorizeManagement: false);
        var app = fixture.App;

        async Task<BusinessReceipt> Teach(string scope)
        {
            BusinessReceipt? reply = null;
            await CameraRoundHarness.Until(async () =>
            {
                var snapshot = await app.SnapshotAsync();
                reply = await app.CommandAsync(new(Wire.Id(), snapshot.Revision, "CalibrateEmptyTray", fixture.Left, scope));
                return reply.Status != "Conflict";
            });
            return reply!;
        }
        async Task<ParcelBarcodeReceipt> Submit(string id, string code)
        {
            var snapshot = await app.SnapshotAsync();
            var lane = snapshot.Positions!.Single(position => position.Id == id);
            return await app.SubmitParcelBarcodeAsync(new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, id, code,
                ParcelBarcodeInputKind.Keyboard, null, lane.CurrentOperation)
            {
                ExpectedRoundId = lane.CurrentRoundId,
                ExpectedArrivalGeneration = lane.Arrival!.Generation,
                ExpectedPlacementBoundary = lane.Arrival.PlacementBoundary
            }, default);
        }

        Check((await Teach("DisabledLane")).Status != "Persisted", "unauthorized calibration is refused");
        await fixture.AuthorizeAsync();
        Check((await Teach("DisabledLane")).Status != "Persisted", "enabled lane cannot use isolated calibration");
        var leftWork = await Submit("left", "SYNTH-BEFORE-CALIBRATION");
        Check(leftWork.Status == "Admitted", "fixture establishes an actual current round");
        await CameraRoundHarness.Until(async () => (await app.OperationAsync(leftWork.OperationId!))?.State == ParcelState.SlotResolved);
        var workspaceId = Wire.Id();
        await app.SetManagementWorkspaceAsync(workspaceId, true, default);
        Check((await Teach("StationDebug")).Status != "Persisted", "station debug cannot calibrate a lane with an open round");
        await app.SetManagementWorkspaceAsync(workspaceId, false, default);

        var before = await app.SnapshotAsync();
        var left = before.Positions!.Single(position => position.Id == "left");
        var epoch = fixture.Factory.Sources[fixture.Left].Epoch;
        Check((await app.SetPositionEnabledAsync(new(Wire.Id(), before.RuntimeId, "left", left.SupplyControl!.Revision, false), default)).Status == "Accepted",
            "left lane is disabled under the production command guard");
        Check((await Teach("DisabledLane")).Status == "Persisted", "authorized calibration starts with left disabled and no active round");
        var rightWork = await Submit("right", "SYNTH-DURING-CALIBRATION");
        Check(rightWork.Status == "Admitted", "the other lane accepts its own request while calibration is active");
        for (uint frame = 1; frame <= 20; frame++)
        {
            fixture.Api(fixture.Left).Enqueue(CameraRoundHarness.EmptyTray, frame);
            await Task.Delay(100);
        }
        var after = await app.SnapshotAsync();
        left = after.Positions!.Single(position => position.Id == "left");
        Check(left.TrayPresence is { Calibrated: true, Calibrating: false, Presence: TrayPresence.Absent } &&
            left.SupplyControl?.Mode == StationPositionMode.Disabled && !left.CameraConnectionSuspended &&
            left.CurrentRoundId is null && fixture.Api(fixture.Left).Opens == 1 && fixture.Factory.Sources[fixture.Left].Epoch == epoch,
            "real acquisition confirms a stable empty tray without a reopen, left round, or left decode");
        await CameraRoundHarness.Until(async () => (await app.OperationAsync(rightWork.OperationId!))?.State == ParcelState.SlotResolved);
        Check(!after.Paused && fixture.Peer.Requests.Count == 2 && fixture.Factory.Decoders[fixture.Left].Count == 0,
            "calibration sends no RCS request and does not pause or replay either side");
    }
}
