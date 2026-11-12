using Ipc.Application.Contracts;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

static class RcsOfflineCameraReads
{
    public static async Task Run(string workspace, string root, bool nativeResults)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS RCS-CAMERA " + message); }
        var buffered = 3;
        MvidApi.DrainRawFrames(() => buffered-- > 0 ? 0 : MvidNative.NoData);
        Check(buffered == -1, "native cache cutover drains each buffered raw frame and confirms the empty boundary");
        var rejected = false;
        try { MvidApi.DrainRawFrames(() => unchecked((int)0x80000214)); } catch (IOException) { rejected = true; }
        Check(rejected, "a real raw queue transport error still rejects the cutover");
        rejected = false;
        try { MvidApi.DrainRawFrames(() => 0); } catch (IOException) { rejected = true; }
        Check(rejected, "an unbounded raw queue never reports a false empty boundary");

        await using (var initial = new CameraRoundHarness(workspace, Path.Combine(root, "first-response"), heartbeatSeconds: 3600, nativeResults: nativeResults))
        {
            initial.Peer.HeartbeatUnavailable = true;
            await initial.StartAsync(waitForRcs: false);
            initial.Factory.Sources[initial.Left].ReadPreview();
            initial.Api(initial.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-BEFORE-FIRST-HEARTBEAT"), 1);
            await CameraRoundHarness.Until(() => Task.FromResult(initial.Factory.Sources[initial.Left].ReadPreview()?.FrameNumber == 1 && initial.Api(initial.Left).Acquires == initial.Api(initial.Left).Releases));
            var initialState = await initial.App.SnapshotAsync();
            Check(initial.Factory.Sources[initial.Left].ReadPreview()?.FrameNumber == 1 && initial.Api(initial.Left).PreviewAcquires > 0 &&
                initial.Factory.Decoders[initial.Left].Count == 0 && initial.Peer.Requests.IsEmpty && initial.Peer.HeartbeatRequests.Count > 0 &&
                initialState.Operations.Length == 0 && initialState.Positions!.Single(p => p.Id == "left").BarcodeInput is { Camera: false, Manual: false },
                "before first heartbeat response both camera routes only acquire preview, without recognition or a business request");
        }

        await using var h = new CameraRoundHarness(workspace, root, heartbeatSeconds: 1, nativeResults: nativeResults);
        var readAllowed = false; var inputs = 0; using var cycleDone = new ManualResetEventSlim();
        var binding = new DeviceBinding("parcel-reader", "hikrobot-mvs-reader", "preview-race", "1.0.0")
        { Parameters = new() { ["device.ip"] = "192.0.2.91", ["device.serial"] = "SYNTH-PREVIEW-RACE", ["reader.engine"] = nativeResults ? "mvid-native" : "zxing-cpp", ["motion.still-ms"] = "0" } };
        var api = new CameraRoundHarness.RoundApi(binding) { BeforePreviewReturn = () => readAllowed = true };
        var decoder = new CameraRoundHarness.RoundDecoder(api, binding.Parameters!);
        await using (var source = new MvsAcquisition(new(binding.DeviceId, binding.DeviceId, binding.Parameters!), api,
            serial => new ResourceLease(Path.Combine(root, "preview-lease"), [serial]),
            phaseChanged: phase => { if (phase == "get-image-buffer" && api.Releases == 1) cycleDone.Set(); }, decoder: nativeResults ? null : decoder))
        {
            await source.SetInspectionAsync(new("preview-only", false, _ => true, false) { DecodeAllowed = _ => readAllowed }, default);
            await source.StartControlledAsync("preview-race", _ => { Interlocked.Increment(ref inputs); return true; }, _ => { }, default);
            api.EnqueueNative(CameraRoundHarness.Barcode("SYNTH-RAW-PREVIEW"), 1,
                [new("SYNTH-RAW-PREVIEW", "Code128", [new(10, 10), new(50, 10), new(50, 30), new(10, 30)], 0)]);
            await CameraRoundHarness.Until(() => Task.FromResult(cycleDone.IsSet));
            await source.SetInspectionAsync(null, default);
            Check(inputs == 0 && decoder.Count == 0, "a readiness change inside raw acquisition cannot promote that preview into a recognition fact");
        }

        h.Peer.ReplyVehicleStates = true; h.Peer.HeartbeatUnavailable = true;
        await h.StartAsync(waitForRcs: false);
        async Task SwitchLeft(bool enabled)
        {
            var snapshot = await h.App.SnapshotAsync();
            var receipt = await h.App.SetPositionEnabledAsync(new(Wire.Id(), snapshot.RuntimeId, "left", snapshot.Positions!.Single(p => p.Id == "left").SupplyControl!.Revision, enabled), default);
            if (enabled && receipt.Status == "Rejected")
            {
                Check(snapshot.Positions!.Single(p => p.Id == "left").SupplyControl!.Mode == StationPositionMode.Disabled,
                    "offline re-enable cannot bypass missing withdrawal acknowledgement");
                var wasOffline = h.Peer.HeartbeatUnavailable; h.Peer.HeartbeatUnavailable = false;
                await h.AuthorizeAsync();
                await CameraRoundHarness.Until(async () =>
                {
                    await h.App.CheckRcsHeartbeatAsync(default);
                    var current = await h.App.SnapshotAsync(); var lane = current.Positions!.Single(p => p.Id == "left");
                    return (await h.App.ConfirmRemoteRecoveryAsync(new(Wire.Id(), current.RuntimeId, "left", lane.Arrival!.Generation,
                        "SYNTHETIC peer failed handlers have completed; no queued remote work; removal membership applied")
                    { ExpectedControlRevision = lane.SupplyControl!.Revision, ExpectedTargetOperation = lane.Arrival.TargetOperation, ExpectedRecoveryToken = lane.Arrival.RecoveryToken }, default)).Status == "Accepted";
                });
                snapshot = await h.App.SnapshotAsync();
                receipt = await h.App.SetPositionEnabledAsync(new(Wire.Id(), snapshot.RuntimeId, "left", snapshot.Positions!.Single(p => p.Id == "left").SupplyControl!.Revision, true), default);
                Check(receipt.Status == "Accepted", "explicit synthetic remote reconciliation permits re-enable");
                await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Arrival?.Confirmed == true);
                h.Peer.HeartbeatUnavailable = wasOffline;
                if (wasOffline) await h.App.CheckRcsHeartbeatAsync(default);
            }
            await CameraRoundHarness.Until(async () => enabled
                ? (await h.App.SnapshotAsync()).Devices!.SingleOrDefault(d => d.DeviceId == h.Left)?.IsValid(DateTimeOffset.UtcNow) == true
                : (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").CameraConnectionSuspended);
        }
        await SwitchLeft(false); await h.App.DisposeAsync(); await h.StartAsync(waitForAllCameras: false, waitForRcs: false);
        Check(h.Api(h.Left).Starts == 0 && (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").SupplyControl?.Mode == StationPositionMode.Disabled,
            "a camera persisted disabled at startup stays unopened until explicitly enabled");
        async Task<bool> RcsReady() => (await h.App.SnapshotAsync()).Health.Single(c => c.InstanceId == h.Config.Wcs!.InstanceId).Ready;
        await CameraRoundHarness.Until(async () => !await RcsReady());
        await SwitchLeft(true);
        int Requests() => h.Peer.Requests.Count(body => body.Contains("sorter.task.dest_request", StringComparison.Ordinal));
        async Task Frame(uint number, string code)
        {
            h.Factory.Sources[h.Left].ReadPreview();
            if (nativeResults) h.Api(h.Left).EnqueueNative(CameraRoundHarness.Barcode(code), number,
                [new(code, "Code128", [new(10, 10), new(50, 10), new(50, 30), new(10, 30)], 0)]);
            else h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode(code), number);
            // Preview is intentionally limited to 15 FPS and can skip this frame.
            // Synchronize the actual SDK loan; code delivery is awaited below by
            // its matching read round or original request, never by preview rate.
            await CameraRoundHarness.Until(() => Task.FromResult(h.Api(h.Left).LastReturnedFrame == number && h.Api(h.Left).Acquires == h.Api(h.Left).Releases));
        }
        var decoded = h.Factory.Decoders[h.Left].Count;
        var previewCalls = h.Api(h.Left).PreviewAcquires;
        await Frame(1, "SYNTH-OFFLINE");
        Check(h.Factory.Sources[h.Left].ReadPreview()?.FrameNumber == 1 && h.Api(h.Left).PreviewAcquires > previewCalls && h.Factory.Decoders[h.Left].Count == decoded && Requests() == 0 &&
            (await h.App.SnapshotAsync()).Operations.Length == 0, "offline RCS permits preview but no automatic recognition, work card or destination request");
        Check((await h.App.SnapshotAsync()).Devices!.All(d => d.IsValid(DateTimeOffset.UtcNow)), "RCS loss does not mark the connected cameras offline");
        await SwitchLeft(false);
        h.Peer.HeartbeatUnavailable = false; await h.AuthorizeAsync();
        await CameraRoundHarness.Until(async () =>
        {
            await h.App.CheckRcsHeartbeatAsync(default);
            var state = await h.App.SnapshotAsync(); var lane = state.Positions!.Single(p => p.Id == "left");
            return (await h.App.ConfirmRemoteRecoveryAsync(new(Wire.Id(), state.RuntimeId, "left", lane.Arrival!.Generation,
                "SYNTHETIC peer has completed failed handlers and applied withdrawal; no queued member changes")
            { ExpectedControlRevision = lane.SupplyControl!.Revision, ExpectedTargetOperation = lane.Arrival.TargetOperation, ExpectedRecoveryToken = lane.Arrival.RecoveryToken }, default)).Status == "Accepted";
        });
        h.Peer.HeartbeatUnavailable = true; await h.App.CheckRcsHeartbeatAsync(default);
        var beforeReplacement = await h.App.SnapshotAsync();
        var candidateReaders = beforeReplacement.Configuration.Effective.Topology!.Readers.Select(reader => reader.DeviceId == h.Left
            ? reader with { Parameters = new Dictionary<string, string>(reader.Parameters!) { ["device.ip"] = "192.0.2.91" } } : reader).ToArray();
        var candidate = beforeReplacement.Configuration.Effective with
        {
            Topology = beforeReplacement.Configuration.Effective.Topology with { Readers = candidateReaders },
            Reader = candidateReaders.Single(reader => reader.DeviceId == h.Left)
        };
        h.Factory.Expect(candidate);
        var saved = await h.App.StageConfigurationAsync(beforeReplacement.Configuration.Revision, candidate, "Synthetic disabled camera replacement");
        Check(saved.Status == "Effective" && h.Api(h.Left).Starts == 0, "replacing a disabled camera applies its configuration without opening it");
        await SwitchLeft(true); previewCalls = h.Api(h.Left).PreviewAcquires;
        await Frame(1, "SYNTH-REPLACED-OFFLINE");
        Check(h.Api(h.Left).PreviewAcquires > previewCalls && h.Factory.Decoders[h.Left].Count == 0 && Requests() == 0 &&
            (await h.App.SnapshotAsync()).Operations.Length == 0, "enabling the disabled replacement retains the RCS read guard before its first frame");
        var window = Wire.Id(); await h.App.SetManagementWorkspaceAsync(window, true, default);
        await h.BeginAsync(); await h.ArmAsync(h.Left);
        await Frame(2, "SYNTH-OFFLINE-DEBUG");
        var round = await h.LockedAsync();
        Check(round.Barcode == "SYNTH-OFFLINE-DEBUG" && Requests() == 0, "management can explicitly read a real frame while RCS is offline without sending a request");
        await h.App.SetManagementWorkspaceAsync(window, false, default);
        await Frame(3, "SYNTH-CLOSED-OFFLINE");
        Check(Requests() == 0 && (await h.App.SnapshotAsync()).Operations.All(o => o.DebugRequest is not null), "closing management while RCS remains offline keeps automatic reads paused");
        h.Peer.HeartbeatUnavailable = false;
        await CameraRoundHarness.Until(RcsReady); await h.WaitForCameraAdmissionAsync();
        await Frame(4, "SYNTH-RESTORED");
        await CameraRoundHarness.Until(() => Task.FromResult(Requests() == 1));
        var original = (await h.App.SnapshotAsync()).Operations.Single(o => o.DebugRequest is null);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Single(o => o.Id == original.Id).Wcs?.State == StageState.Accepted);
        original = (await h.App.SnapshotAsync()).Operations.Single(o => o.Id == original.Id);
        Check(Requests() == 1, "a fresh RCS confirmation automatically resumes exactly one new request");
        h.Peer.HeartbeatUnavailable = true;
        await CameraRoundHarness.Until(async () => !await RcsReady());
        await Frame(5, "SYNTH-RESTORED");
        var retained = (await h.App.SnapshotAsync()).Operations.Single(o => o.Id == original.Id);
        Check(Wire.Encode(retained.Wcs) == Wire.Encode(original.Wcs) && Requests() == 1, "a later outage preserves the original request and its confirmed result");
    }
}
