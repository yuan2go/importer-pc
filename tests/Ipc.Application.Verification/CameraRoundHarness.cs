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
    public readonly RoundPeer Peer = new();
    public readonly BusinessLaunch Launch;
    public readonly ParcelConfiguration Config;
    public ParcelRuntime App { get; private set; } = null!;
    public RoundFactory Factory { get; private set; } = null!;
    public string Left => Config.Topology!.Readers[0].DeviceId;
    public string Right => Config.Topology!.Readers[1].DeviceId;
    public RoundApi Api(string device) => Factory.Apis[device];
    public CameraRoundHarness(string workspace, string folder)
    {
        if (!nativeLoaded)
        {
            var cache = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? throw new InvalidOperationException("D-drive restored cache required");
            var library = NativeLibrary.Load(Path.Combine(cache, "zxingcpp/0.5.3/runtimes/win-x64/native/ZXing.dll"));
            NativeLibrary.SetDllImportResolver(typeof(BarcodeReader).Assembly, (name, _, _) => name is "ZXing" or "ZXing.dll" ? library : 0);
            nativeLoaded = true;
        }
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        var readers = seed.Topology!.Readers.Select((r, i) => r with { Driver = "hikrobot-mvs-reader", Parameters = new()
            { ["device.ip"] = "192.0.2." + (11 + i), ["device.serial"] = "SYNTH-ROUND-" + i, ["entity.mode"] = "read-result", ["acquisition.trigger"] = "off", ["code.encoding"] = "utf-8" } }).ToArray();
        var binding = new CapabilityBinding("integration", "sportmaster-rcs", "1.0.0", seed.Wcs!.InstanceId, Peer.Endpoint, "SPORTMASTER-RCS-SORTER-v1", new()
        { ["zone-id"] = "71", ["project-id"] = "83", ["specified-chute"] = "false", ["request-id-base"] = "820000000", ["request-id-count"] = "100000",
          ["heartbeat-interval-seconds"] = "3600", ["left-station-code"] = "TEST-LEFT", ["left-bcr-code"] = "TEST-LEFT-READER", ["right-station-code"] = "TEST-RIGHT", ["right-bcr-code"] = "TEST-RIGHT-READER" });
        Config = seed with { Reader = readers[0], Topology = seed.Topology with { Readers = readers }, Wcs = binding, Login = null, DeferredConnections = [] };
        var configuration = Path.Combine(folder, "config"); Directory.CreateDirectory(configuration);
        ExtensionStore.AtomicWrite(Path.Combine(configuration, "business.json"), Config);
        Launch = new(configuration, Path.Combine(folder, "data"), null, null);
    }
    public async Task StartAsync()
    {
        Factory = new(Launch, Config);
        App = new(Launch, new SportmasterFlow(), Factory) { RuntimeIdentityPreflight = () => { if (Config.Mode != RunMode.Simulation) throw new InvalidOperationException("Simulation fixture only"); } };
        await App.StartAsync(default);
        await Until(async () => (await App.SnapshotAsync()).Devices?.Count(d => d.IsValid(DateTimeOffset.UtcNow)) == 2);
        await AuthorizeAsync();
    }
    public async Task AuthorizeAsync()
    {
        if (!(await App.ManagementModeStatusAsync()).Configured) await App.ConfigureManagementPasswordAsync(Password);
        if (!await App.EnterManagementModeAsync(Password)) throw new InvalidOperationException("Synthetic management grant rejected");
    }
    public async Task<CameraInspectionStatus> BeginAsync()
    {
        var snapshot = await App.SnapshotAsync();
        await App.BeginCameraInspectionAsync(new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, CameraInspectionMode.Isolated, "Synthetic isolated round verification", new(), true) { SingleReadRounds = true }, default);
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
    internal sealed class RoundFactory(BusinessLaunch launch, ParcelConfiguration expected) : IParcelCapabilityFactory
    {
        readonly ParcelCapabilityFactory validator = new(launch, BusinessCompatibility.Read(typeof(SportmasterFlow).Assembly), typeof(SportmasterFlow).Assembly);
        public readonly Dictionary<string, RoundApi> Apis = [];
        public readonly Dictionary<string, MvsAcquisition> Sources = [];
        public readonly Dictionary<string, RoundDecoder> Decoders = [];
        public ParcelConfiguration Normalize(ParcelConfiguration c) => validator.Normalize(c);
        public void ValidateStructure(ParcelConfiguration c) => validator.ValidateStructure(c);
        public void Validate(ParcelConfiguration c)
        { if (c.Mode != RunMode.Simulation || Wire.Encode(c) != Wire.Encode(Normalize(expected))) throw new InvalidOperationException("Exact Simulation round fixture only"); validator.ValidateStructure(c); }
        public CapabilityRequirements Requirements(CapabilityBinding b) => validator.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c)
        {
            Validate(c); var readers = new Dictionary<string, IControlledObservationSource>();
            foreach (var binding in c.Topology!.Readers)
            {
                var api = new RoundApi(binding); Apis.Add(binding.DeviceId, api); var decoder = new RoundDecoder(api); Decoders.Add(binding.DeviceId, decoder);
                var source = new MvsAcquisition(new(binding.DeviceId, binding.DeviceId, binding.Parameters!), api,
                    serial => new ResourceLease(Path.Combine(launch.DataRoot, "synthetic-device-leases"), [serial]), decoder: decoder);
                readers.Add(binding.DeviceId, source); Sources.Add(binding.DeviceId, source);
            }
            var wcs = new SportmasterRcsTaskService(c.Wcs!); wcs.ConfigureRuntime(c);
            return new(readers[c.Reader.DeviceId], null, wcs, null, new Dictionary<string, string> { [wcs.InstanceId] = "synthetic-round-rcs-implementation" }, Readers: readers);
        }
        public ValueTask DisposeAsync() => validator.DisposeAsync();
    }
    internal sealed class RoundDecoder(RoundApi api) : IMonoBarcodeDecoder
    {
        readonly SoftwareDecoder inner = new("utf-8", () => { });
        public int Count;
        public RecognitionSymbol[] Last = [];
        public MvsTimingSnapshot? TimingSnapshot => inner.TimingSnapshot;
        public void Prepare() => inner.Prepare();
        public string[] Decode(byte[] p, int length, int width, int height) => DecodeDetailed(p, length, width, height).Select(s => s.Text).ToArray();
        public RecognitionSymbol[] DecodeDetailed(byte[] p, int length, int width, int height)
        { if (api.HasFrame) throw new InvalidOperationException("Recognition must follow SDK loan return"); Last = inner.DecodeDetailed(p, length, width, height); Interlocked.Increment(ref Count); return Last; }
        public void Release() => inner.Release();
    }
    internal sealed class RoundApi(DeviceBinding binding) : IMvsApi
    {
        readonly ConcurrentQueue<(RoundPixels Pixels, uint Number)> queue = new();
        public int Releases, Acquires, Clears, Opens, Starts, Stops;
        public bool FailNextClear;
        public readonly ManualResetEventSlim ClearGate = new(true);
        public volatile bool Discovered = true;
        nint buffer; uint trigger = 1, acquisition;
        public bool HasFrame { get; private set; }
        public void Enqueue(RoundPixels p, uint number) => queue.Enqueue((p, number));
        public void Initialize() { }
        public MvsDevice[] Enumerate() => Discovered ? [new(binding.Parameters!["device.ip"], binding.Parameters["device.serial"], "SYNTHETIC", "SYNTHETIC", [])] : [];
        public void Create(MvsDevice d) { }
        public void Open() => Interlocked.Increment(ref Opens);
        public uint EnumValue(string key) => key == "PixelFormat" ? MvsNative.Mono8 : key == "AcquisitionMode" ? acquisition : trigger;
        public void SetEnumValue(string key, uint value) { if (key == "TriggerMode") trigger = value; else if (key == "AcquisitionMode") acquisition = value; else throw new InvalidOperationException(key); }
        public long PayloadSize() => 8388608;
        public void ConfigureBuffers(uint count) { }
        public void Start() => Interlocked.Increment(ref Starts);
        public MvsFrame? Acquire()
        {
            if (!queue.TryDequeue(out var frame)) { Thread.Sleep(4); return null; }
            if (HasFrame) throw new InvalidOperationException("Synthetic SDK loan leaked");
            buffer = Marshal.AllocHGlobal(frame.Pixels.Data.Length); Marshal.Copy(frame.Pixels.Data, 0, buffer, frame.Pixels.Data.Length); HasFrame = true;
            Interlocked.Increment(ref Acquires);
            return new(buffer, (uint)frame.Pixels.Width, (uint)frame.Pixels.Height, MvsNative.Mono8, (ulong)frame.Pixels.Data.Length, frame.Number, 0, 0);
        }
        public void Release() { if (!HasFrame) throw new InvalidOperationException("Double SDK return"); Marshal.FreeHGlobal(buffer); HasFrame = false; Interlocked.Increment(ref Releases); }
        public void ClearBuffers() { Interlocked.Increment(ref Clears); ClearGate.Wait(); if (FailNextClear) { FailNextClear = false; throw new IOException("Synthetic SDK cache clear failed"); } while (queue.TryDequeue(out _)) { } }
        public bool Connected() => Discovered;
        public void Stop() => Interlocked.Increment(ref Stops);
        public void Close() { }
        public void Destroy() { }
        public void Shutdown() { }
    }
    internal sealed class RoundPeer : IAsyncDisposable
    {
        readonly HttpListener listener = new(); readonly CancellationTokenSource stopped = new(); readonly Task loop;
        readonly ConcurrentBag<Task> handlers = [];
        public readonly ConcurrentQueue<string> Requests = new();
        public TaskCompletionSource? Hold { get; set; }
        public string Mode = "assigned";
        public string Endpoint { get; }
        public RoundPeer()
        {
            var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
            Endpoint = "http://127.0.0.1:" + port + "/rcs/sorter/api"; listener.Prefixes.Add("http://127.0.0.1:" + port + "/"); listener.Start(); loop = Loop();
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
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8); var body = await reader.ReadToEndAsync(); Requests.Enqueue(body);
                var hold = Hold; if (hold is not null) await hold.Task.WaitAsync(stopped.Token);
                using var json = JsonDocument.Parse(body); var id = json.RootElement.GetProperty("requestId").GetInt64();
                var reply = JsonSerializer.Serialize(new { requestId = id, result = new[] { new { index = 1, success = Mode != "reject", code = Mode == "reject" ? "SYNTH_REJECT" : null,
                    message = "Synthetic response", args = (object?)null, command = "sorter.task.dest_request", data = new { chuteCode = "SYNTH-CHUTE", length = (decimal?)null,
                        width = (decimal?)null, height = (decimal?)null, weight = (decimal?)null, taskId = "SYNTH-TASK", skuId = "SYNTH-SKU" } } } });
                var bytes = Encoding.UTF8.GetBytes(reply); context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, stopped.Token);
            }
            catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
            catch (HttpListenerException) { } // A deliberate cancellation may close its client.
            catch (IOException) { }
            finally { try { context.Response.Close(); } catch (ObjectDisposedException) when (stopped.IsCancellationRequested) { } }
        }
        public async ValueTask DisposeAsync() { stopped.Cancel(); Hold?.TrySetResult(); listener.Close(); await loop; await Task.WhenAll(handlers); stopped.Dispose(); }
    }
}
