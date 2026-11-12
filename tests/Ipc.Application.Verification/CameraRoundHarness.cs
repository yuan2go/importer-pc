using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Platform;
using ZXingCpp;

// Shared native-WPF/Application fixture: real driver + ZXing + SQLite + RCS adapter;
// only IMvsApi and the explicit loopback HTTP peer are synthetic. Never production qualification.
internal sealed class CameraRoundHarness : IAsyncDisposable
{
    static readonly string Password = "Fixture-" + Guid.NewGuid().ToString("N");
    static bool nativeLoaded;
    public const int SyntheticBlankFrames = 3, SyntheticBlankSpanMs = 800;
    public readonly RoundPeer Peer = new();
    public readonly BusinessLaunch Launch;
    public readonly ParcelConfiguration Config;
    public ParcelRuntime App { get; private set; } = null!;
    public Action<RoundFactory>? ConfigureFactory { get; init; }
    public TimeProvider? StationClock { get; init; }
    public RoundFactory Factory { get; private set; } = null!;
    public string Left => Config.Topology!.Readers[0].DeviceId;
    public string Right => Config.Topology!.Readers[1].DeviceId;
    public RoundApi Api(string device) => Factory.Apis[device];
    // Fallback window for non-frame sources; camera positions end a placement only on an empty tray.
    public int PlacementReleaseMs { get; init; } = 300;
    public static readonly RoundPixels EmptyTray = new(Enumerable.Repeat((byte)180, 640 * 240).ToArray(), 640, 240);
    // Synthetic lift includes an explicit qualified physical fact, independent of
    // decoded blank frames. No production image classifier is claimed by this fixture.
    // Returns the next frame number for the caller's own sequence.
    public async Task<uint> ReplaceParcelAsync(string device, uint next)
    {
        for (var i = 0; i < CameraRoundHarness.SyntheticBlankFrames; i++)
        {
            var decoded = Factory.Decoders[device].Count;
            Api(device).Enqueue(EmptyTray, next++);
            await Until(() => Task.FromResult(Factory.Decoders[device].Count > decoded));
            await Task.Delay(CameraRoundHarness.SyntheticBlankSpanMs / 2);
        }
        await TrayAsync(device, TrayPresence.Absent);
        await SmRoundFixture.NextVehicleAsync(App, Config.Topology!.Positions.Single(p => p.ReaderDeviceId == device).Id);
        return next;
    }
    public async Task TrayAsync(string device, TrayPresence presence)
    {
        var position = Config.Topology!.Positions.Single(p => p.ReaderDeviceId == device).Id;
        await Until(async () => (await App.SnapshotAsync()).Positions!.Single(p => p.Id == position).Arrival?.CameraBoundaryReady == true);
        var before = App.WorkerProgress().Single(p => p.Position == position).Processed;
        if (!Factory.Trays[device].Observe(presence)) throw new InvalidOperationException("Qualified fixture presence was not admitted");
        await Until(() => Task.FromResult(App.WorkerProgress().Single(p => p.Position == position).Processed > before));
    }
    // stillMs: "放稳后再识别" for both synthetic cameras; "0" decodes every frame.
    public CameraRoundHarness(string workspace, string folder, int? timeoutMs = null, int? queueCapacity = null, string? rcsEndpoint = null, int heartbeatSeconds = 1, string stillMs = "0", bool nativeResults = false, BarcodeFilterPolicy? barcodeFilter = null, string? stationCode = null, int? maximumOperations = null)
    {
        if (!nativeLoaded)
        {
            var libraryPath = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? throw new InvalidOperationException("D-drive restored cache required"), "zxingcpp/0.5.3/runtimes/win-x64/native/ZXing.dll")
                : Path.Combine(AppContext.BaseDirectory, "runtimes/linux-x64/native/libZXing.so");
            var library = NativeLibrary.Load(libraryPath);
            NativeLibrary.SetDllImportResolver(typeof(BarcodeReader).Assembly, (name, _, _) => name is "ZXing" or "ZXing.dll" ? library : 0);
            nativeLoaded = true;
        }
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        var readers = seed.Topology!.Readers.Select((r, i) => r with
        {
            Driver = "hikrobot-mvs-reader",
            Parameters = new()
            { ["reader.engine"] = nativeResults ? "mvid-native" : "zxing-cpp", ["motion.still-ms"] = stillMs, ["device.ip"] = "192.0.2." + (11 + i), ["device.serial"] = "SYNTH-ROUND-" + i, ["entity.mode"] = "read-result", ["acquisition.trigger"] = "off", ["code.encoding"] = "utf-8" }
        }).ToArray();
        var binding = new CapabilityBinding("integration", "sportmaster-rcs", "1.0.0", seed.Wcs!.InstanceId, rcsEndpoint ?? Peer.Endpoint, "SPORTMASTER-RCS-SORTER-v1", new()
        {
            ["zone-id"] = "71",
            ["project-id"] = "83",
            ["specified-chute"] = "false",
            ["request-id-base"] = "820000000",
            ["request-id-count"] = "100000",
            ["heartbeat-interval-ms"] = (heartbeatSeconds * 1000).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["left-bcr-code"] = "TEST-LEFT-READER",
            ["right-bcr-code"] = "TEST-RIGHT-READER"
        });
        Config = seed with { StationCode = stationCode ?? seed.StationCode, TimeoutMs = timeoutMs ?? seed.TimeoutMs, QueueCapacity = queueCapacity ?? seed.QueueCapacity, MaximumOperations = maximumOperations ?? seed.MaximumOperations, Reader = readers[0], Topology = seed.Topology with { Readers = readers }, Wcs = binding, Login = null, DeferredConnections = [], BarcodeFilter = barcodeFilter ?? seed.BarcodeFilter };
        var configuration = Path.Combine(folder, "config"); Directory.CreateDirectory(configuration);
        ExtensionStore.AtomicWrite(Path.Combine(configuration, "business.json"), Config);
        Launch = new(configuration, Path.Combine(folder, "data"), null, null);
    }
    public async Task StartAsync(bool waitForAllCameras = true, bool waitForRcs = true, bool authorizeManagement = true, bool supervisedRestart = false, string? rcsImplementationFingerprint = null)
    {
        Factory = new(Launch, Config);
        ConfigureFactory?.Invoke(Factory);
        if (rcsImplementationFingerprint is not null) Factory.RcsImplementationFingerprint = rcsImplementationFingerprint;
        App = new(Launch with { RequireRecoveryReview = supervisedRestart }, new SportmasterFlow(), Factory) { RuntimeIdentityPreflight = () => { if (Config.Mode != RunMode.Simulation) throw new InvalidOperationException("Simulation fixture only"); } };
        if (StationClock is not null) App.StationTime = StationClock;
        await App.StartAsync(default);
        SmRoundFixture.Register(App, Launch, Config);
        if (waitForAllCameras) await Until(async () => (await App.SnapshotAsync()).Devices?.Count(d => d.IsValid(DateTimeOffset.UtcNow)) == 2);
        if (authorizeManagement) await AuthorizeAsync();
        if (waitForRcs) await Until(async () => (await App.SnapshotAsync()).Health.Any(h => h.RemoteConfirmation == RemoteConnectionConfirmation.ResponseReceived));
        if (waitForRcs && waitForAllCameras) await Until(async () => (await App.SnapshotAsync()).Positions!.All(p => p.Arrival?.CameraBoundaryReady == true));
    }
    public async Task AuthorizeAsync()
    {
        if (!(await App.ManagementModeStatusAsync()).Configured) await App.ConfigureManagementPasswordAsync(Password);
        if (!await App.EnterManagementModeAsync(Password)) throw new InvalidOperationException("Synthetic management grant rejected");
    }
    public Task WaitForCameraAdmissionAsync() => Until(async () =>
        (await App.SnapshotAsync()).Positions!.All(p => p.BarcodeInput?.Camera == true));
    public async Task<CameraInspectionStatus> BeginAsync()
    {
        var snapshot = await App.SnapshotAsync();
        await App.BeginCameraInspectionAsync(new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, CameraInspectionMode.ObserveProduction, "Synthetic passive round verification", new(), true) { SingleReadRounds = true }, default);
        return await App.CameraInspectionStatusAsync(default);
    }
    public async Task<CameraReadRound> ArmAsync(string device)
    {
        var s = await App.SnapshotAsync(); var scope = await App.CameraInspectionStatusAsync(default); var prior = await App.ReadCameraReadRoundAsync(default);
        return await App.ArmCameraReadAsync(new(Wire.Id(), s.RuntimeId, s.Configuration.Revision, scope.SessionId!, device, prior.Id), default);
    }
    public async Task<CameraReadRound> LockedAsync()
    {
        CameraReadRound? value = null;
        await Until(async () => { value = await App.ReadCameraReadRoundAsync(default); if (value.State is "Faulted" or "Stopped") throw new InvalidOperationException(value.Detail); return value.State == "Locked"; });
        return value!;
    }
    public async Task<SendCameraRound> SendCommandAsync(CameraReadRound round) => new(Wire.Id(), (await App.SnapshotAsync()).RuntimeId, round.Id!, round.RequestHash!);
    public async Task EndAsync()
    {
        var s = await App.CameraInspectionStatusAsync(default);
        await App.EndCameraInspectionAsync(new(Wire.Id(), s.SessionId!, true, true, "Synthetic scene cleared and original responsibility checked"), default);
    }
    public static async Task Until(Func<Task<bool>> done)
    {
        for (var i = 0; i < 400; i++)
        {
            try { if (await done()) return; }
            catch (InvalidOperationException error) when (error.Message == "后台操作并发已达上限，请等待当前操作结束")
            { Console.WriteLine("READ_BACKPRESSURE: bounded read-only fixture waits for existing owners; no delivery command replay"); }
            await Task.Delay(20);
        }
        throw new TimeoutException("Round fixture did not converge");
    }
    public static RoundPixels Barcode(string text)
    {
        using var barcode = new Barcode(text, new CreatorOptions(BarcodeFormat.Code128));
        using var image = barcode.ToImage(new WriterOptions { Scale = 3, AddQuietZones = true });
        var bytes = new byte[checked(image.Width * image.Height)]; Marshal.Copy(image.Data, bytes, 0, bytes.Length);
        return new(bytes, image.Width, image.Height);
    }
    public static RoundPixels Multiple(string first, string second)
    {
        var a = Barcode(first); var b = Barcode(second); var w = a.Width + b.Width + 80; var h = Math.Max(a.Height, b.Height) + 80;
        var pixels = Enumerable.Repeat((byte)255, w * h).ToArray();
        for (var y = 0; y < a.Height; y++) a.Data.AsSpan(y * a.Width, a.Width).CopyTo(pixels.AsSpan((y + 40) * w + 20, a.Width));
        for (var y = 0; y < b.Height; y++) b.Data.AsSpan(y * b.Width, b.Width).CopyTo(pixels.AsSpan((y + 40) * w + a.Width + 60, b.Width));
        return new(pixels, w, h);
    }
    public async ValueTask DisposeAsync() { if (App is not null) await App.DisposeAsync(); await Peer.DisposeAsync(); }
    internal sealed record RoundPixels(byte[] Data, int Width, int Height);
    internal sealed class QualifiedTraySource(MvsAcquisition inner) : IAutomaticReconnectObservationSource, IOriginalFrameSource,
        IObservationPreviewSource, IObservationPreviewLeaseSource, IObservationInspectionSource, IObservationReadFeedbackSource,
        IObservationDiagnosticsSource, IObservationPerformanceSource, IProductionArrivalSource
    {
        ProductionArrivalCapture? arrival;
        public async Task SetArrivalAsync(ProductionArrivalCapture boundary, CancellationToken token)
        { await inner.SetArrivalAsync(boundary, token); arrival = boundary; }
        Func<Observation, bool>? receiver; long sequence;
        readonly object publication = new();
        bool Deliver(Observation value)
        {
            lock (publication) return receiver!(value with { Sequence = ++sequence });
        }
        public string DeviceId => inner.DeviceId;
        public string Epoch => inner.Epoch;
        public string ConfirmationSource => inner.ConfirmationSource;
        public Task StartControlledAsync(string epoch, Func<Observation, bool> receive, Action<DeviceHealthConfirmation> health, CancellationToken token)
        {
            receiver = receive; sequence = 0;
            return inner.StartControlledAsync(epoch, Deliver, health, token);
        }
        public bool Observe(TrayPresence presence)
        {
            var id = Wire.Id();
            return Deliver(new(id, DeviceId, Epoch, "physical." + id, 0, "physical." + id, "Presence", [], null, DateTimeOffset.UtcNow)
            { TrayPresence = new(presence, "controlled-qualified-fixture-event"), ArrivalGeneration = arrival?.ArrivalGeneration, CaptureBoundaryId = arrival?.BoundaryId, CaptureTicks = System.Diagnostics.Stopwatch.GetTimestamp() });
        }
        public void EnableOriginalFrames() => inner.EnableOriginalFrames();
        public IOriginalFrameLease RetainOriginalFrame(AcquisitionIdentity id) => inner.RetainOriginalFrame(id);
        public void ReleaseOriginalFrame(AcquisitionIdentity id) => inner.ReleaseOriginalFrame(id);
        public ObservationPreview? ReadPreview() => inner.ReadPreview();
        public ObservationPreviewLease? ReadPreviewLease() => inner.ReadPreviewLease();
        public Task SetInspectionAsync(InspectionRegistration? registration, CancellationToken token) => inner.SetInspectionAsync(registration, token);
        public string? InspectionFailure => inner.InspectionFailure;
        public event Action<ObservationReadFeedback>? ReadFeedback { add => inner.ReadFeedback += value; remove => inner.ReadFeedback -= value; }
        public ObservationDiagnostics ReadDiagnostics() => inner.ReadDiagnostics();
        public object ReadPerformance() => inner.ReadPerformance();
        public Task StopAsync(CancellationToken token) => inner.StopAsync(token);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    internal sealed class RoundFactory(BusinessLaunch launch, ParcelConfiguration expected) : IParcelCapabilityFactory
    {
        readonly ParcelCapabilityFactory validator = new(launch, BusinessCompatibility.Read(typeof(SportmasterFlow).Assembly), typeof(SportmasterFlow).Assembly);
        public readonly Dictionary<string, RoundApi> Apis = [];
        public readonly Dictionary<string, MvsAcquisition> Sources = [];
        public readonly Dictionary<string, RoundDecoder> Decoders = [];
        public readonly Dictionary<string, QualifiedTraySource> Trays = [];
        // Live replacement bookkeeping: how many ports were rebuilt, and whether new cameras are found.
        public int ReaderBuilds, WcsBuilds;
        public bool DiscoverReplacements = true;
        public Func<IControlledObservationSource, IControlledObservationSource>? DecorateReader { get; set; }
        public bool RejectLiveReaders = false, FailCandidateRelease = false;
        public bool CheckBuiltInProductionAdmission { get; set; }
        public string? AdmissionFailure(ParcelConfiguration c) =>
            CheckBuiltInProductionAdmission ? validator.AdmissionFailure(c with { Mode = RunMode.Production }) : null;
        // The fixture validates one exact configuration; a test names the next one before saving it.
        public void Expect(ParcelConfiguration next) => expected = next;
        public ParcelConfiguration Normalize(ParcelConfiguration c) => validator.Normalize(c);
        public void ValidateStructure(ParcelConfiguration c) => validator.ValidateStructure(c);
        public void Validate(ParcelConfiguration c)
        { if (c.Mode != RunMode.Simulation || Wire.Encode(c) != Wire.Encode(Normalize(expected))) throw new InvalidOperationException("Exact Simulation round fixture only"); validator.ValidateStructure(c); }
        public CapabilityRequirements Requirements(CapabilityBinding b) => validator.Requirements(b);
        public CapabilityCatalog Catalog() => validator.Catalog();
        MvsAcquisition Reader(DeviceBinding binding)
        {
            var api = new RoundApi(binding); Apis[binding.DeviceId] = api; var decoder = new RoundDecoder(api, binding.Parameters!); Decoders[binding.DeviceId] = decoder;
            var source = new MvsAcquisition(new(binding.DeviceId, binding.DeviceId, binding.Parameters!), api,
                serial => new ResourceLease(Path.Combine(launch.DataRoot, "synthetic-device-leases"), [serial]), decoder: api.ReturnsDecodedFrames ? null : decoder);
            Sources[binding.DeviceId] = source; return source;
        }
        public string RcsImplementationFingerprint { get; set; } = "synthetic-round-rcs-implementation";
        public ParcelPorts Create(ParcelConfiguration c)
        {
            Validate(c); var readers = new Dictionary<string, IControlledObservationSource>();
            foreach (var binding in c.Topology!.Readers)
            { var source = new QualifiedTraySource(Reader(binding)); Trays[binding.DeviceId] = source; readers.Add(binding.DeviceId, DecorateReader?.Invoke(source) ?? source); }
            var wcs = new SportmasterRcsTaskService(c.Wcs!); wcs.ConfigureRuntime(c);
            return new(readers[c.Reader.DeviceId], null, wcs, null, new Dictionary<string, string> { [wcs.InstanceId] = RcsImplementationFingerprint }, Readers: readers);
        }
        public ParcelLiveDevices? CreateLive(ParcelConfiguration c, IReadOnlyCollection<string> readerIds, bool integration)
        {
            Validate(c); var readers = new Dictionary<string, IControlledObservationSource>();
            foreach (var id in readerIds)
            {
                var source = new QualifiedTraySource(Reader(c.Topology!.Readers.Single(r => r.DeviceId == id))); Trays[id] = source;
                readers.Add(id, DecorateReader?.Invoke(source) ?? source); ReaderBuilds++;
                Apis[id].Discovered = DiscoverReplacements;
                if (FailCandidateRelease) Decoders[id].FailReleaseAt = 1;
            }
            SportmasterRcsTaskService? wcs = null;
            if (integration) { wcs = new(c.Wcs!); wcs.ConfigureRuntime(c); WcsBuilds++; }
            return new(RejectLiveReaders ? readers.ToDictionary(r => "unexpected-" + r.Key, r => r.Value) : readers, wcs, wcs is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [wcs.InstanceId] = RcsImplementationFingerprint });
        }
        public ValueTask DisposeAsync() => validator.DisposeAsync();
    }
    internal sealed class RoundDecoder(RoundApi api, IReadOnlyDictionary<string, string> parameters) : IMonoBarcodeDecoder
    {
        readonly SoftwareDecoder inner = new(parameters.GetValueOrDefault("code.encoding", "utf-8"), () => { }, settings: ReaderSettings.Read(parameters));
        public bool PreservesInput => true;
        public int Count;
        public int ReleaseCalls, FailReleaseAt;
        // The next N decodes return no code although the frame is unchanged (the decoder missing a resting parcel).
        public int MissNext;
        public int RejectNext;
        public RecognitionSymbol[] Last = [];
        public Action? BeforeDecode { get; set; }
        public MvsTimingSnapshot? TimingSnapshot => inner.TimingSnapshot;
        public void Prepare() => inner.Prepare();
        public string[] Decode(byte[] p, int length, int width, int height) => DecodeDetailed(p, length, width, height).Select(s => s.Text).ToArray();
        public RecognitionSymbol[] DecodeDetailed(byte[] p, int length, int width, int height)
        {
            if (!api.WasReturned(p.AsSpan(0, length))) throw new InvalidOperationException("Recognition must follow its own SDK loan return");
            BeforeDecode?.Invoke();
            if (Interlocked.Decrement(ref RejectNext) >= 0)
            {
                Interlocked.Increment(ref Count);
                throw new MvsFrameRejection("SyntheticRejection", "Synthetic complete-frame rejection");
            }
            Interlocked.Exchange(ref RejectNext, 0);
            Last = inner.DecodeDetailed(p, length, width, height);
            if (Interlocked.Decrement(ref MissNext) >= 0) Last = []; else Interlocked.Exchange(ref MissNext, 0);
            Interlocked.Increment(ref Count); return Last;
        }
        public void Release()
        {
            if (++ReleaseCalls == FailReleaseAt) throw new IOException("Synthetic decoder release failure");
            inner.Release();
        }
    }
    internal sealed class RoundApi(DeviceBinding binding) : IMvsApi
    {
        readonly ConcurrentQueue<(RoundPixels Pixels, uint Number, RecognitionSymbol[]? Symbols, MvsFrameRejection? Rejection)> queue = new();
        public int Releases, Acquires, Clears, Opens, Starts, Stops, Closes, Destroys, Finalizes;
        public int StopError = 0, CloseError = 0, DestroyError = 0;
        public Action? BeforeDestroy = null;
        public bool FailNextClear;
        public int NextLostPackets;
        public readonly ManualResetEventSlim ClearGate = new(true);
        public volatile bool Discovered = true;
        nint buffer; uint trigger = 1, acquisition;
        byte[]? borrowedPixels;
        uint borrowedFrame;
        public uint LastReturnedFrame;
        readonly ConcurrentDictionary<string, byte> returnedPixels = new();
        internal bool WasReturned(ReadOnlySpan<byte> pixels) => returnedPixels.ContainsKey(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)));
        public bool HasFrame { get; private set; }
        public bool ReturnsDecodedFrames => binding.Parameters?.GetValueOrDefault("reader.engine") == "mvid-native";
        public void Enqueue(RoundPixels p, uint number) => queue.Enqueue((p, number, null, null));
        public void EnqueueNative(RoundPixels p, uint number, RecognitionSymbol[] symbols, MvsFrameRejection? rejection = null)
            => queue.Enqueue((p, number, symbols, rejection));
        public void Initialize() { }
        public MvsDevice[] Enumerate() => Discovered ? [new(binding.Parameters!["device.ip"], binding.Parameters["device.serial"], "SYNTHETIC", "SYNTHETIC", [])] : [];
        public void Create(MvsDevice d) { }
        public void Open() => Interlocked.Increment(ref Opens);
        public uint EnumValue(string key) => key == "PixelFormat" ? MvsNative.Mono8 : key == "AcquisitionMode" ? acquisition : trigger;
        public void SetEnumValue(string key, uint value) { if (key == "TriggerMode") trigger = value; else if (key == "AcquisitionMode") acquisition = value; else throw new InvalidOperationException(key); }
        public long PayloadSize() => 8388608;
        public void ConfigureBuffers(uint count, uint strategy = 0) { }
        public void Start() => Interlocked.Increment(ref Starts);
        public int RecognitionAcquires, PreviewAcquires;
        public MvsFrame? Acquire() { Interlocked.Increment(ref RecognitionAcquires); return AcquireFrame(); }
        public Action? BeforePreviewReturn = null;
        public Action? BeforeAcquireReturn { get; set; }
        public MvsFrame? AcquirePreview()
        {
            Interlocked.Increment(ref PreviewAcquires); var frame = AcquireFrame();
            if (frame is not null) BeforePreviewReturn?.Invoke();
            return frame;
        }
        MvsFrame? AcquireFrame()
        {
            if (!queue.TryDequeue(out var frame)) { Thread.Sleep(4); return null; }
            if (HasFrame) throw new InvalidOperationException("Synthetic SDK loan leaked");
            borrowedPixels = frame.Pixels.Data; borrowedFrame = frame.Number;
            buffer = Marshal.AllocHGlobal(frame.Pixels.Data.Length); Marshal.Copy(frame.Pixels.Data, 0, buffer, frame.Pixels.Data.Length); HasFrame = true;
            Interlocked.Increment(ref Acquires);
            var started = System.Diagnostics.Stopwatch.GetTimestamp(); BeforeAcquireReturn?.Invoke();
            return new(buffer, (uint)frame.Pixels.Width, (uint)frame.Pixels.Height, MvsNative.Mono8, (ulong)frame.Pixels.Data.Length, frame.Number,
                Trigger: 0, LostPackets: (uint)Interlocked.Exchange(ref NextLostPackets, 0))
            { NativeSymbols = frame.Symbols, NativeRejection = frame.Rejection, NativeRecognitionMs = 12.5 + (BeforeAcquireReturn is null ? 0 : System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds) };
        }
        public void Release() { if (!HasFrame) throw new InvalidOperationException("Double SDK return"); Marshal.FreeHGlobal(buffer); HasFrame = false; returnedPixels.TryAdd(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(borrowedPixels!)), 0); borrowedPixels = null; LastReturnedFrame = borrowedFrame; Interlocked.Increment(ref Releases); }
        public void ClearBuffers() { Interlocked.Increment(ref Clears); ClearGate.Wait(); if (FailNextClear) { FailNextClear = false; throw new IOException("Synthetic SDK cache clear failed"); } while (queue.TryDequeue(out _)) { } }
        public int HealthReads;
        public bool Connected() { Interlocked.Increment(ref HealthReads); return Discovered; }
        public void Stop() { Interlocked.Increment(ref Stops); MvsNative.Check(Volatile.Read(ref StopError), "StopGrabbing"); }
        public void Close() { Interlocked.Increment(ref Closes); MvsNative.Check(Volatile.Read(ref CloseError), "CloseDevice"); }
        public void Destroy() { BeforeDestroy?.Invoke(); MvsNative.Check(Volatile.Read(ref DestroyError), "DestroyHandle"); Interlocked.Increment(ref Destroys); }
        public void Shutdown() => Interlocked.Increment(ref Finalizes);
    }
    internal sealed class RoundPeer : IAsyncDisposable
    {
        internal static readonly ConcurrentDictionary<string, RoundPeer> Peers = new();
        readonly HttpListener listener = new(); readonly CancellationTokenSource stopped = new(); readonly Task loop;
        readonly ConcurrentBag<Task> handlers = [];
        public readonly ConcurrentQueue<string> Requests = new();
        public readonly ConcurrentQueue<string> HeartbeatRequests = new();
        public Func<string, Task>? BusinessGate { get; set; }
        public Func<string, Task>? ResponseGate { get; set; }
        int businessRepliesAttempted;
        public int BusinessRepliesAttempted => Volatile.Read(ref businessRepliesAttempted);
        public TaskCompletionSource? Hold { get; set; }
        public Func<string, bool>? HoldWhen { get; set; }
        public string Mode = "assigned";
        public string? RejectionMessage { get; set; }
        public string ReplySlot = "SYNTH-CHUTE";
        public string? ReplyTaskId { get; set; } = "SYNTH-TASK";
        public string? ReplyWaveNo { get; set; }
        public string? ReplyQuantity { get; set; }
        public object? AssignedData { get; set; }
        public bool ReplyVehicleStates { get; set; } = true;
        public readonly ConcurrentDictionary<string, string> Vehicles = new();
        public Func<string, Task>? HeartbeatGate { get; set; }
        public Func<string, string, string>? VehicleReply { get; set; }
        public readonly ConcurrentQueue<(long Request, string Bcr, string Vehicle)> RemoteBindings = new();
        public string[] RemoteMembers = [];
        public Func<string, string>? HeartbeatTransform { get; set; }
        public volatile bool HeartbeatUnavailable = false;
        public string Endpoint { get; }
        public RoundPeer(int? selectedPort = null)
        {
            var reserve = new TcpListener(IPAddress.Loopback, selectedPort ?? 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
            Endpoint = "http://127.0.0.1:" + port + "/rcs/sorter/api"; listener.Prefixes.Add("http://127.0.0.1:" + port + "/"); listener.Start(); Peers[Endpoint] = this; loop = Loop();
        }
        async Task Loop()
        {
            try { while (!stopped.IsCancellationRequested) handlers.Add(Handle(await listener.GetContextAsync().WaitAsync(stopped.Token))); }
            catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
            catch (HttpListenerException) when (stopped.IsCancellationRequested) { }
        }
        async Task Handle(HttpListenerContext context)
        {
            try
            {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8); var body = await reader.ReadToEndAsync();
                using var json = JsonDocument.Parse(body); var id = json.RootElement.GetProperty("requestId").GetInt64();
                var heartbeat = json.RootElement.GetProperty("data").GetArrayLength() == 0 || json.RootElement.GetProperty("data")[0].GetProperty("command").GetString() == "sorter.task.station_heartbeat";
                (heartbeat ? HeartbeatRequests : Requests).Enqueue(body);
                if (heartbeat && HeartbeatGate is { } heartbeatGate) await heartbeatGate(body).WaitAsync(stopped.Token);
                if (!heartbeat && BusinessGate is { } gate) await gate(body).WaitAsync(stopped.Token);
                if (!heartbeat)
                {
                    var target = json.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("bcrCode").GetString() ?? "";
                    RemoteBindings.Enqueue((id, target, Vehicles.GetValueOrDefault(target, "SYNTH-AGV-" + (target.Contains("RIGHT", StringComparison.Ordinal) ? 2 : 1))));
                }
                if (!heartbeat && ResponseGate is { } responseGate) await responseGate(body).WaitAsync(stopped.Token);
                // Business-result uncertainty does not also simulate a heartbeat outage unless selected explicitly.
                var hold = Hold; if (hold is not null && (HoldWhen?.Invoke(body) ?? !heartbeat)) await hold.Task.WaitAsync(stopped.Token);
                if (HeartbeatUnavailable && heartbeat)
                { context.Response.StatusCode = 503; return; }
                if (heartbeat) RemoteMembers = json.RootElement.GetProperty("data").EnumerateArray()
                    .Select(i => i.GetProperty("params").GetProperty("bcrCode").GetString()!).ToArray();
                var reply = JsonSerializer.Serialize(new
                {
                    requestId = id,
                    result = new[] { new { index = 1, success = Mode != "reject", code = Mode == "reject" ? "SYNTH_REJECT" : null,
                    message = Mode == "reject" ? RejectionMessage ?? "Synthetic response" : "Synthetic response", args = (object?)null, command = "sorter.task.dest_request", data = AssignedData ?? new { chuteCode = ReplySlot, length = (decimal?)null,
                        width = (decimal?)null, height = (decimal?)null, weight = (decimal?)null, taskId = ReplyTaskId, skuId = "SYNTH-SKU", waveNo = ReplyWaveNo, quantity = ReplyQuantity } } }
                });
                if (Mode == "no-destination")
                    reply = JsonSerializer.Serialize(new
                    {
                        requestId = id,
                        result = new[] { new { index = 1, success = false,
                        command = "sorter.task.dest_request", msg = RejectionMessage ?? "合成异常：未找到可用格口，请人工核对包裹。", data = new { chuteCode = "" } } }
                    });
                if (!heartbeat && Mode == "no-device")
                    reply = JsonSerializer.Serialize(new
                    {
                        requestId = id,
                        result = new[] { new { index = 1, success = false, code = "RPC_R_NO_DEVICE", message = "无设备可用", args = new { }, command = "sorter.task.dest_request", data = (object?)null } }
                    });
                if (heartbeat)
                    reply = JsonSerializer.Serialize(new
                    {
                        requestId = id,
                        result = json.RootElement.GetProperty("data").EnumerateArray().Select(item => new
                        {
                            index = item.GetProperty("index").GetInt32(),
                            success = true,
                            command = "sorter.task.station_heartbeat",
                            data = new
                            {
                                stationCode = item.GetProperty("params").GetProperty("stationCode").GetString(),
                                bcrCode = item.GetProperty("params").GetProperty("bcrCode").GetString(),
                                agvNo = !ReplyVehicleStates ? null : Vehicles.GetValueOrDefault(item.GetProperty("params").GetProperty("bcrCode").GetString()!,
                                    VehicleReply?.Invoke(item.GetProperty("params").GetProperty("bcrCode").GetString()!, body) ?? "SYNTH-AGV-" + (item.GetProperty("params").GetProperty("bcrCode").GetString()!.Contains("RIGHT", StringComparison.Ordinal) ? 2 : 1)),
                                status = item.GetProperty("index").GetInt32() == 1 ? "IDLE" : "TASKING"
                            }
                        }).ToArray()
                    });
                var bytes = Encoding.UTF8.GetBytes(heartbeat && HeartbeatTransform is { } transform ? transform(reply) : !heartbeat && Mode == "malformed" ? "{\"invalid\":true}" : reply); context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
                try { await context.Response.OutputStream.WriteAsync(bytes, stopped.Token); }
                finally { if (!heartbeat) Interlocked.Increment(ref businessRepliesAttempted); }
            }
            catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
            catch (HttpListenerException) { } // A deliberate cancellation may close its client.
            catch (IOException) { }
            finally { try { context.Response.Close(); } catch (ObjectDisposedException) when (stopped.IsCancellationRequested) { } }
        }
        public async ValueTask DisposeAsync() { Peers.TryRemove(Endpoint, out _); stopped.Cancel(); Hold?.TrySetResult(); listener.Close(); await loop; await Task.WhenAll(handlers); stopped.Dispose(); }
    }
}
