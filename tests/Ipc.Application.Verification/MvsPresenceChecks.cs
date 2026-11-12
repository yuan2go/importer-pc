using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Contracts;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;
using Ipc.Verification;

internal static class MvsPresenceChecks
{
    const int W = 384, H = 256;
    static void Check(bool ok, string message)
    { if (!ok) throw new InvalidOperationException(message); Console.WriteLine("PASS PRESENCE " + message); }
    static byte[] Pixels(bool parcel, int exposure = 0)
    {
        var pixels = new byte[W * H];
        for (var y = 0; y < H; y++) for (var x = 0; x < W; x++)
            pixels[y * W + x] = (byte)((parcel && x > 100 && x < 250 && y > 70 && y < 190 ? 180 : 70 + (x / 20 + y / 20) % 2 * 30) + exposure);
        return pixels;
    }
    internal static async Task Run(string workspace, string root)
    {
        ReplaySamples(root);
        var model = new MvsTrayPresence(); var ticks = Stopwatch.GetTimestamp();
        TrayPresenceEvidence? Sample(bool parcel, int ms, int exposure = 0)
        {
            ticks += (long)(ms / 1000d * Stopwatch.Frequency);
            var pixels = Pixels(parcel, exposure); var memory = Marshal.AllocHGlobal(pixels.Length);
            try { Marshal.Copy(pixels, 0, memory, pixels.Length); return model.Observe(memory, W, H, ticks); }
            finally { Marshal.FreeHGlobal(memory); }
        }
        Check(Sample(true, 100) is null, "startup with a parcel never learns a false empty reference");
        model.Teach(); Sample(false, 100); Sample(true, 100);
        for (var i = 0; i < 20; i++) Sample(true, 100);
        Check(model.Status.Contains("校准失败"), "a parcel placed during teaching cannot become a newly learned empty background");
        model.Teach();
        for (var i = 0; i < 20; i++) Sample(false, 100);
        Check(model.Status == "空托盘已确认", "explicit empty teaching requires stable multi-frame evidence");
        Check(model.Snapshot() is { Calibrated: true, Calibrating: false, Presence: TrayPresence.Absent, ExposureValid: true, ReferenceSha256: { Length: 64 }, ChangedFraction: 0 },
            "structured model evidence identifies the calibrated reference, actual empty state and measured scene difference");
        Sample(true, 100); Sample(true, 100); Sample(true, 100);
        Check(model.Status == "包裹在位", "parcel pixels establish presence independently of barcode results");
        Sample(false, 100); Sample(false, 100); Sample(false, 700);
        Check(model.Status != "空托盘已确认", "a frame gap resets the continuous removal span");
        for (var i = 0; i < 10; i++) Sample(false, 100);
        Check(model.Status == "空托盘已确认", "return to the taught reference confirms removal after 800ms");
        model.BreakContinuity();
        Check(model.Snapshot() is { Calibrated: true, Presence: TrayPresence.Unknown, ChangedFraction: null } && model.Status.Contains("未知"),
            "a broken frame sequence withdraws the stale empty status immediately while retaining only the reference");
        Sample(false, 100, 40);
        Check(model.Status.Contains("未知"), "large exposure changes invalidate physical inference");
        Check(model.Snapshot() is { Calibrated: true, Presence: TrayPresence.Unknown, ExposureValid: false, BrightnessShift: 40 },
            "exposure-invalid evidence remains explicitly unknown despite retaining the session's calibrated reference");
        await using (var restarted = new MvsAcquisition(new("presence-restart", "presence-restart", new Dictionary<string, string>
        { ["device.ip"] = "192.0.2.21", ["device.serial"] = "SYNTH-OFFLINE-0" }), new Camera(false), _ => new Lease(), decoder: new Decoder(new Camera(false))))
        {
            await restarted.StartControlledAsync(Wire.Id(), _ => true, _ => { }, default);
            restarted.CalibrateEmptyTray();
            await CameraRoundHarness.Until(() => Task.FromResult(restarted.TrayPresenceStatus == "空托盘已确认"));
            await restarted.StopAsync(default);
            await restarted.StartControlledAsync(Wire.Id(), _ => true, _ => { }, default);
            Check(restarted.TrayPresenceStatus.Contains("未校准"), "reconnecting the actual acquisition discards its old empty reference");
            Check(restarted.ReadDiagnostics().TrayPresenceModel is { Calibrated: false, ReferenceSha256: null, Presence: TrayPresence.Unknown },
                "reconnected acquisition diagnostics cannot carry a prior session's empty-tray identity");
        }
        foreach (var native in new[] { false, true })
        {
            var camera = new Camera(native); MvsAcquisition? source = null;
            await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "presence-" + native), false, mode: RunMode.Simulation)
            {
                CameraSource = binding => binding.DeviceId.EndsWith("left", StringComparison.OrdinalIgnoreCase)
                    ? CreateSource(binding)
                    : new ManualSupplyHarness.SilentCamera(binding.DeviceId)
            };
            MvsAcquisition CreateSource(DeviceBinding binding)
            {
                source = new MvsAcquisition(new(binding.DeviceId, binding.DeviceId, binding.Parameters!), camera, _ => new Lease(), decoder: native ? null : new Decoder(camera));
                source.EnableOriginalFrames(); return source;
            }
            h.Peer.ReplyVehicleStates = true;
            await h.StartAsync();
            Check(source is not null, "actual acquisition is installed in the Application composition");
            var s = await h.App.SnapshotAsync();
            var unauthorized = await h.App.CommandAsync(new(Wire.Id(), s.Revision, "CalibrateEmptyTray", source!.DeviceId));
            Check(unauthorized.Status == "Rejected", "empty teaching requires application management authorization");
            await ManagementModeFixture.Authorize(h.App);
            var window = Wire.Id();
            await h.App.SetManagementWorkspaceAsync(window, true, default);
            await CameraRoundHarness.Until(async () =>
            {
                s = await h.App.SnapshotAsync();
                var result = await h.App.CommandAsync(new(Wire.Id(), s.Revision, "CalibrateEmptyTray", source.DeviceId));
                if (result.Status == "Conflict") return false;
                if (result.Status != "Persisted") throw new InvalidOperationException(Wire.Encode(new { result, Device = s.Devices, source.TrayPresenceStatus }));
                return true;
            });
            await CameraRoundHarness.Until(() => Task.FromResult(source.TrayPresenceStatus == "空托盘已确认"));
            Check(h.Peer.Requests.IsEmpty, "teaching while recognition is paused creates no RCS request");
            Check(Ipc.Application.Sportmaster.Desktop.CameraAcquisitionPresentation.Describe(await h.App.ReadCameraAcquisitionAsync(source.DeviceId, default)).Contains("空托盘已确认"),
                "actual camera diagnostics expose the completed calibration to the operator");
            await h.App.SetManagementWorkspaceAsync(window, false, default);
            await h.App.ExitManagementModeAsync();
            await h.App.CheckRcsHeartbeatAsync(default);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").BarcodeInput!.Camera);
            camera.Parcel = true;
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Rounds.Any(r => r.OperationId is not null));
            var first = (await h.App.SnapshotAsync()).Rounds.Single(r => r.PositionId == "left");
            await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(first.OperationId!))?.State == ParcelState.SlotResolved);
            await Task.Delay(3200);
            Check((await h.App.SnapshotAsync()).Rounds.Any(r => r.Id == first.Id) && h.Peer.Requests.Count == 1,
                "reliable still-present parcel survives 3sec and never resubmits");
            camera.Parcel = false;
            await CameraRoundHarness.Until(async () => !(await h.App.SnapshotAsync()).Rounds.Any(r => r.PositionId == "left"));
            Check((await h.App.RoundHistoryAsync(search: first.Id)).Items.Single().EndReason == "TrustedRemoval",
                "actual frame evidence ends the round without manual confirmation");
            camera.Parcel = true;
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 2));
            var second = (await h.App.SnapshotAsync()).Rounds.Single(r => r.PositionId == "left");
            Check(second.Id != first.Id,
                "same code on a new physical placement automatically starts one independent request");
            await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(second.OperationId!))?.State == ParcelState.SlotResolved);
            async Task Switch(string position, bool enabled)
            {
                var snapshot = await h.App.SnapshotAsync();
                var control = snapshot.Positions!.Single(p => p.Id == position).SupplyControl!;
                var result = await h.App.SetPositionEnabledAsync(new(Wire.Id(), snapshot.RuntimeId, position, control.Revision, enabled), default);
                if (result.Status != "Accepted") throw new InvalidOperationException(result.Reason);
            }
            await Switch("right", false); await Switch("left", false);
            Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").SupplyControl?.Mode == StationPositionMode.Disabling &&
                await h.App.CheckRcsHeartbeatAsync(default) == RcsHeartbeatResult.Received,
                "stopping supply keeps RCS heartbeat online while a calibrated tray still holds its parcel");
            camera.Parcel = false;
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left") is
                { CurrentRoundId: null, SupplyControl.Mode: StationPositionMode.Disabled, CameraConnectionSuspended: true });
            Check((await h.App.RoundHistoryAsync(search: second.Id)).Items.Single().EndReason == "TrustedRemoval",
                "actual frame evidence ends the round and finishes disabling without clicking parcel removal");
            Check(h.Peer.Requests.Count == 2 && (await h.App.SnapshotAsync()).Positions!.All(p => p.Vehicle is null),
                "disabled lanes retain the original requests and never show an idle vehicle cue");
            await CameraRoundHarness.Until(async () => !(await h.App.SnapshotAsync()).Rounds.Any(r => r.PositionId == "left") &&
                (await h.App.SnapshotAsync()).Capacity!.ManagedExecutions == 0);
        }
    }
    static void ReplaySamples(string root)
    {
        var samples = Path.Combine(root, "presence-samples"); Directory.CreateDirectory(samples);
        var planFile = Path.Combine(samples, "manifest.json"); var reportFile = Path.Combine(root, "presence-report.json");
        List<PresenceSampleReplay.Frame> frames = [];
        foreach (var (number, ms, parcel) in new[] { (1u, 0d, false), (2u, 400d, false), (3u, 800d, false), (4u, 1200d, false), (5u, 1600d, false), (8u, 2000d, true), (9u, 2100d, true), (10u, 2200d, true) })
        {
            var name = "frame-" + number + (number == 1 ? ".pgm" : ".png"); var file = Path.Combine(samples, name);
            File.WriteAllBytes(file, number == 1 ? System.Text.Encoding.ASCII.GetBytes($"P5\n{W} {H}\n255\n").Concat(Pixels(parcel)).ToArray() : ParcelRuntime.EncodeOriginalPng(Pixels(parcel), W, H));
            frames.Add(new("session-one", number, ms, name, Wire.FileHash(file), W, H, number == 1, parcel ? TrayPresence.Present : TrayPresence.Absent));
        }
        frames.Add(frames[^1] with { SessionId = "session-two", FrameNumber = 1, HostElapsedMs = 0, ConfirmEmptyCalibration = false });
        var plan = new PresenceSampleReplay.Plan(1, new string('a', 40), "Synthetic", "synthetic-camera", "mvid-native", "Synthetic", frames.ToArray());
        ExtensionStore.AtomicWrite(planFile, plan);
        var before = Directory.GetFiles(samples).ToDictionary(f => f, Wire.FileHash);
        PresenceSampleReplay.Run(planFile, reportFile);
        using (var report = JsonDocument.Parse(File.ReadAllText(reportFile)))
        {
            var rows = report.RootElement.GetProperty("Frames");
            Check(report.RootElement.GetProperty("Status").GetString() == "SAMPLE_REPLAY_REVIEW_REQUIRED" &&
                rows[4].GetProperty("Model").GetProperty("Presence").GetString() == "Absent" &&
                rows[7].GetProperty("Model").GetProperty("Presence").GetString() == "Present" &&
                rows[8].GetProperty("Model").GetProperty("Calibrated").GetBoolean() == false &&
                report.RootElement.GetProperty("FalseEmptyAgainstDeclaredLabels").GetInt32() == 0 && before.All(f => Wire.FileHash(f.Key) == f.Value),
                "offline captured-PGM/PNG replay uses the production model, resets sessions and preserves originals without granting field qualification");
        }
        ExtensionStore.AtomicWrite(planFile, plan with { Frames = [frames[0] with { RelativeFile = "../outside.png" }] });
        var denied = Path.Combine(root, "refused-presence.json");
        try { PresenceSampleReplay.Run(planFile, denied); throw new InvalidOperationException("Traversal sample accepted"); }
        catch (InvalidDataException) { Check(!File.Exists(denied), "invalid replay source paths publish no successful report"); }
        ExtensionStore.AtomicWrite(planFile, plan);
        File.AppendAllText(Path.Combine(samples, frames[0].RelativeFile), "changed-original");
        try { PresenceSampleReplay.Run(planFile, denied); throw new InvalidOperationException("Changed sample accepted"); }
        catch (InvalidDataException) { Check(!File.Exists(denied), "changed original samples are retained and refuse before report publication"); }
    }
    sealed class Lease : IDisposable { public void Dispose() { } }
    sealed class Decoder(Camera camera) : IMonoBarcodeDecoder
    {
        public void Prepare() { }
        public void Release() { }
        public string[] Decode(byte[] p, int length, int w, int h) => camera.Parcel ? ["PRESENCE-123456"] : [];
    }
    sealed class Camera(bool native) : IMvsApi
    {
        public volatile bool Parcel;
        nint data; uint number;
        public bool HasFrame => data != 0;
        public bool ReturnsDecodedFrames => native;
        public void Initialize() { }
        public MvsDevice[] Enumerate() => [new("192.0.2.21", "SYNTH-OFFLINE-0", "synthetic", "synthetic", [])];
        public void Create(MvsDevice device) { }
        public void Open() { }
        public uint EnumValue(string key) => key == "PixelFormat" ? MvsNative.Mono8 : key == "AcquisitionMode" ? 2u : 0u;
        public long PayloadSize() => W * H;
        public void ConfigureBuffers(uint nodes, uint strategy = 0) { }
        public void ClearBuffers() { }
        public void Start() { }
        public MvsFrame? Acquire()
        {
            Thread.Sleep(25); var p = Pixels(Parcel); data = Marshal.AllocHGlobal(p.Length); Marshal.Copy(p, 0, data, p.Length);
            return new(data, W, H, MvsNative.Mono8, (ulong)p.Length, ++number, 0, 0)
            { NativeSymbols = Parcel ? [new("PRESENCE-123456", "Code128", [], 0)] : [] };
        }
        public void Release() { Marshal.FreeHGlobal(data); data = 0; }
        public bool Connected() => true;
        public void Stop() { }
        public void Close() { }
        public void Destroy() { }
        public void Shutdown() { }
    }
}
