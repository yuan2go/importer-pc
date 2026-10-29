using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Drivers;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;
using ZXingCpp;

static partial class SportmasterRcs
{
    public static async Task MvsSoftwareFlow(string workspace, string root, string[] args)
    {
        Console.WriteLine("VerifierSource="+typeof(SportmasterRcs).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
        var live = args.Contains("--live-ip");
        var bundled = args.Contains("--bundled-native");
        if (bundled && !live) throw new InvalidOperationException("Bundled path requires actual camera and explicit private identity file");
        string Value(string key) => args[Array.IndexOf(args, key) + 1];
        if (args.Contains("--private-pgm"))
        {
            PrepareZxingFixture(); var path = Value("--private-pgm"); var data = File.ReadAllBytes(path);
            var end = 0; for (var i = 0; i < 3; i++) end = Array.IndexOf(data, (byte)10, end) + 1;
            var header = System.Text.Encoding.ASCII.GetString(data, 0, end).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (header.Length != 4 || header[0] != "P5" || header[3] != "255") throw new InvalidDataException("Explicit diagnostic Mono8 PGM only");
            var pixels = data[end..]; var decoder = new SoftwareDecoder("utf-8", PrepareZxingFixture); decoder.Prepare();
            try { var codes = decoder.Decode(pixels, pixels.Length, int.Parse(header[1]), int.Parse(header[2]));
                ExtensionStore.AtomicWrite(path + ".dotnet-private.json", codes); Console.WriteLine("PRIVATE_PGM decoded=" + codes.Length); }
            finally { decoder.Release(); }
            return;
        }
        var endpoint = args.Contains("--rcs-endpoint") ? Value("--rcs-endpoint") : null;
        var pythonLedger = args.Contains("--rcs-ledger") ? Value("--rcs-ledger") : null;
        if (!live && endpoint is not null && (pythonLedger is null || !Path.IsPathFullyQualified(pythonLedger) || !File.Exists(pythonLedger)))
            throw new ArgumentException("Synthetic external Python validation requires an explicit existing private --rcs-ledger path");
        await using var peer = endpoint is null ? new Peer() : null;
        endpoint ??= peer!.Endpoint;
        if (new Uri(endpoint).AbsolutePath == "/") endpoint = endpoint.TrimEnd('/') + "/rcs/sorter/api";
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || !uri.IsLoopback)
            throw new InvalidDataException("Explicit loopback simulator only");
        if (!bundled) PrepareZxingFixture();
        MvsApi? inspector = null;
        MvsDevice? device = null;
        if (bundled) device = new(Value("--live-ip"), File.ReadAllText(Value("--serial-file")).Trim(), "PRIVATE", "PRIVATE", []);
        if (live && !bundled)
        {
            LoadMvsFixture(Value("--sdk-directory")); inspector = new MvsApi();
            try { inspector.Initialize(); device = inspector.Enumerate().Single(d => d.Ip == Value("--live-ip")); }
            finally { inspector.Shutdown(); }
        }
        var rcs = Binding(endpoint) with { Implementation = "sportmaster-rcs" };
        if (live) rcs.Parameters!["heartbeat-interval-seconds"] = "3";
        var c = HeartbeatConfiguration(workspace, rcs);
        var reader = c.Reader with { Driver = "hikrobot-mvs-reader", Parameters = new()
            { ["device.ip"] = device?.Ip ?? "192.0.2.11", ["device.serial"] = device?.Serial ?? "SYNTH-MVS-LEFT",
              ["entity.mode"] = "read-result", ["acquisition.trigger"] = "off", ["code.encoding"] = "utf-8" } };
        var all = c.Topology!.Readers.Select(r => r.DeviceId == c.Reader.DeviceId ? reader : r with { Driver = "hikrobot-mvs-reader", Parameters = new()
            { ["device.ip"] = "192.0.2.12", ["device.serial"] = "SYNTH-MVS-RIGHT", ["entity.mode"] = "read-result", ["acquisition.trigger"] = "off" } }).ToArray();
        c = c with { Reader = reader, Topology = c.Topology with { Readers = all }, Login = null,
            DeferredConnections = all.Where(r => r.DeviceId != reader.DeviceId).Select(r => r.DeviceId).ToArray() };
        var config = Path.Combine(root, "config"); Directory.CreateDirectory(config);
        ExtensionStore.AtomicWrite(Path.Combine(config, "business.json"), c);
        var launch = new BusinessLaunch(config, Path.Combine(root, "data"), null, null);
        var fake = live ? null : new SoftwareFrameApi(reader);
        var factory = new SoftwareFlowFactory(launch, c, fake, bundled);
        // Isolated Simulation fixture seam only. No production identity guard changes.
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), factory);
        if (!bundled || args.Contains("--isolated-simulation-preflight")) app.RuntimeIdentityPreflight = () => { if (c.Mode != RunMode.Simulation) throw new InvalidOperationException("Simulation fixture only"); };
        await app.StartAsync(default);
        // Full frozen HTTP custody is management-only, including in this isolated fixture.
        if (pythonLedger is not null) await ManagementModeFixture.Authorize(app);
        await Wait(app, s => s.Positions!.Single(p => p.Id == "left").Admit);
        Console.WriteLine("RuntimeIdentity=" + (bundled && !args.Contains("--isolated-simulation-preflight") ? "DEFAULT_GUARD" : "ISOLATED_SIMULATION_FIXTURE_NOT_PRODUCTION"));
        Console.WriteLine("MVS_SOFTWARE_READY " + (live ? "ACTUAL_CAMERA" : "SYNTHETIC_NATIVE") + "; RCS=LOCAL_SIMULATOR; no production acceptance implied");
        if (live)
        {
            var until = DateTimeOffset.UtcNow.AddSeconds(args.Contains("--seconds") ? int.Parse(Value("--seconds")) : 30);
            while (DateTimeOffset.UtcNow < until) await Task.Delay(250);
            var snapshot = await app.SnapshotAsync();
            ExtensionStore.AtomicWrite(Path.Combine(root, "live-snapshot-private.json"), snapshot);
            Console.WriteLine(Wire.Encode(new { Operations = snapshot.Operations.Length,
                Resolved = snapshot.Operations.Count(o => o.State == ParcelState.SlotResolved),
                Unknown = snapshot.Operations.Count(o => o.State == ParcelState.WcsUnknown),
                Inputs = snapshot.Inputs!.Length, Source = factory.Source!.ConfirmationSource }));
            if (!snapshot.Operations.Any(o => o.State == ParcelState.SlotResolved)) throw new Exception("Actual camera/RCS pipeline did not resolve any read request; inspect private evidence");
        }
        else
        {
            var pixels = args.Contains("--six-megapixel") ? SyntheticLatencyImages("SYNTH-READ-0001")[0] : MakeSoftwareBarcode("SYNTH-READ-0001");
            fake!.Enqueue(pixels, 1); fake.Enqueue(pixels, 2); fake.Enqueue(pixels, 5);
            Check(fake.Calls.Contains("set:TriggerMode=0") && fake.Calls.Contains("set:AcquisitionMode=2") && fake.Calls.IndexOf("set:AcquisitionMode=2") < fake.Calls.IndexOf("start"),"software reader configures and verifies continuous acquisition before starting, without a customer trigger setting");
            var s = await Wait(app, state => state.Operations.Length == 3 && state.Operations.All(o => o.State == ParcelState.SlotResolved));
            Check((peer is null ? VerifyPythonLedger(pythonLedger!, s.Operations) : peer.Requests.Count(r => r.Body.Contains("sorter.task.dest_request")) == 3) && s.Operations.Select(o => o.AdmissionId).Distinct().Count() == 3,
                "actual software decoder -> MVS lifecycle -> Application/SQLite -> actual RCS adapter: three identical codes remain three independent requests");
            Check(fake.Released == 3 && !fake.HasFrame && factory.Source!.ConfirmationSource.Contains("skippedVideoFrames=2"),
                "native loans are released; raw-video sampling gaps are explicit and do not pretend to lose recognized results");
            var frozen = s.Operations.ToDictionary(o => o.Id, o => Wire.Encode(o.Wcs!.Request));
            fake.Enqueue(new(new byte[200 * 100], 200, 100), 6);
            for (var i = 0; i < 100 && !factory.Source!.ConfirmationSource.Contains("emptyFrames=1"); i++) await Task.Delay(20);
            s = await app.SnapshotAsync();
            Check(s.Operations.Length == 3 && s.Operations.All(o => frozen[o.Id] == Wire.Encode(o.Wcs!.Request)), "blank video creates no request and original request bytes remain unchanged");
            if (peer is null)
            {
                Check(VerifyPythonLedger(pythonLedger!, s.Operations), "Python ledger has no blank-frame request, replay or changed original after the empty frame");
                VerifyPythonLedgerRefusals(pythonLedger!, s.Operations, root);
                ExtensionStore.AtomicWrite(Path.Combine(root,"mvs-python-snapshot-private.json"),s);
            }
            await app.StopAsync(default);
            Check((await app.SnapshotAsync()).Operations.All(o => o.OriginalFrame is { State: "Saved" } image &&
                image.RawSha256 == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels.Data))) && factory.Source!.RetainedOriginalFrames == 0,
                "actual software-reader pipeline saves admitted original frames losslessly and drains raw-frame leases on stop");
            Check(fake.Calls.TakeLast(4).SequenceEqual(["stop", "close", "destroy", "finalize"]) && !fake.HasFrame,
                "controlled shutdown actually returns buffers and stops/closes/destroys/finalizes in order");
            foreach (var throws in new[] { false, true })
            {
                var rejectedApi = new SoftwareFrameApi(reader);
                await using var rejected = new MvsAcquisition(new(reader.DeviceId, reader.DeviceId, reader.Parameters!), rejectedApi,
                    serial => new ResourceLease(Path.Combine(root, "rejected-leases"), [serial]),
                    decoder: new ReleasedFrameDecoder(new SoftwareDecoder("utf-8", PrepareZxingFixture), rejectedApi));
                var calls = 0; var faulted = false;
                await rejected.StartControlledAsync(Wire.Id(), _ => { calls++; if (throws) throw new IOException("Synthetic ingress failure"); return false; },
                    update => faulted |= update.Health == DeviceHealth.Faulted, default);
                rejectedApi.Enqueue(pixels, 1);
                for (var i = 0; i < 200 && !faulted; i++) await Task.Delay(10);
                await rejected.StopAsync(default);
                Check(faulted && calls == 1 && rejected.Undelivered?.Codes.Single() == "SYNTH-READ-0001" && !rejectedApi.HasFrame,
                    "rejected/thrown durable ingress retains the original decoded result and fences source after returning native loan");
                try { await rejected.StartControlledAsync(Wire.Id(), _ => true, _ => { }, default); throw new Exception("Undelivered source restarted blindly"); }
                catch (InvalidOperationException) { Check(true, "retained input prevents blind SDK restart/redelivery"); }
            }
            foreach (var ignore in new[] { false, true })
            {
                var refusedApi = new SoftwareFrameApi(reader) { FailConfigure=!ignore, IgnoreConfigure=ignore };
                await using var refused = new MvsAcquisition(new(reader.DeviceId,reader.DeviceId,reader.Parameters!),refusedApi,
                    serial=>new ResourceLease(Path.Combine(root,"configuration-leases"),[serial]),decoder:new SoftwareDecoder("utf-8",PrepareZxingFixture));
                try { await refused.StartControlledAsync(Wire.Id(),_=>true,_=>{},default);throw new Exception("Configuration failure silently accepted"); }
                catch(Exception ex) when(ex is IOException or InvalidDataException)
                { Check(!refusedApi.Calls.Contains("start")&&refusedApi.Calls.TakeLast(3).SequenceEqual(["close","destroy","finalize"]),"rejected or ineffective device settings refuse acquisition and release native owner"); }
            }
            Console.WriteLine("MVS_SOFTWARE_FLOW_PASS synthetic pixels/native port, actual ZXingCpp/Application/SQLite/HTTP; field acceptance NOT_RUN");
        }
        await app.StopAsync(default);
        ExtensionStore.AtomicWrite(Path.Combine(root, "mvs-timings.json"), new { Acquisition = factory.Source!.Timings.Snapshot(), Decoder = factory.Source.DecoderTimings,
            Tier = live ? "Actual camera; isolated fixture; local simulated RCS" : "Synthetic SDK; actual decoder/Application/SQLite; local simulated RCS",
            Boundary = "ObservedAt starts after SDK Acquire returns; excludes exposure/transport/SDK queue age. Durable handoff includes SQLite custody, excludes Application queue/business/RCS completion." });
        Console.WriteLine("EvidenceRoot=" + root);
    }

    static bool zxingPrepared;
    internal static void PrepareZxingFixture()
    {
        if (zxingPrepared) return;
        var rid = OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsMacOS() ? "osx-x64" : "linux-x64";
        var file = OperatingSystem.IsWindows() ? "ZXing.dll" : OperatingSystem.IsMacOS() ? "libZXing.dylib" : "libZXing.so";
        var root = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? throw new InvalidOperationException("Explicit restored NuGet root required");
        var handle = NativeLibrary.Load(Path.Combine(root, "zxingcpp", "0.5.3", "runtimes", rid, "native", file));
        NativeLibrary.SetDllImportResolver(typeof(BarcodeReader).Assembly, (name, _, _) => name is "ZXing" or "ZXing.dll" ? handle : 0);
        zxingPrepared = true;
    }
    static void LoadMvsFixture(string directory)
    {
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(directory)) throw new InvalidOperationException("Windows x64 with explicit SDK root required");
        foreach (var name in new[] { "MVGigEVisionSDK.dll", "MvCameraControl.dll" })
        {
            var handle = NativeLibrary.Load(Path.Combine(directory, name), typeof(MvsApi).Assembly,
                DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
            if (name == "MvCameraControl.dll") NativeLibrary.SetDllImportResolver(typeof(MvsApi).Assembly, (n, _, _) => n == name ? handle : 0);
        }
    }
    sealed record SoftwarePixels(byte[] Data, int Width, int Height);
    static SoftwarePixels MakeSoftwareBarcode(string value)
    {
        using var barcode = new Barcode(value, new CreatorOptions(BarcodeFormat.Code128));
        using var image = barcode.ToImage(new WriterOptions { Scale = 4, AddQuietZones = true });
        if (image.Format != ImageFormat.Lum) throw new InvalidOperationException("Expected writer Mono8 pixels");
        var bytes = new byte[checked(image.Width * image.Height)]; Marshal.Copy(image.Data, bytes, 0, bytes.Length);
        return new(bytes, image.Width, image.Height);
    }
    sealed class SoftwareFlowFactory(BusinessLaunch launch, ParcelConfiguration expected, SoftwareFrameApi? fake, bool bundled, IMonoBarcodeDecoder? decoder = null) : IParcelCapabilityFactory
    {
        readonly ParcelCapabilityFactory inner = new(launch, BusinessCompatibility.Read(typeof(SportmasterFlow).Assembly), typeof(SportmasterFlow).Assembly);
        public MvsAcquisition? Source;
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void ValidateStructure(ParcelConfiguration c) => inner.ValidateStructure(c);
        public void Validate(ParcelConfiguration c)
        {
            if (c.Mode != RunMode.Simulation || Wire.Encode(c) != Wire.Encode(inner.Normalize(expected))) throw new InvalidOperationException("Exact isolated Simulation fixture only");
            inner.ValidateStructure(c);
        }
        public ParcelPorts Create(ParcelConfiguration c)
        {
            Validate(c);
            if (bundled) { var actual = inner.Create(c); Source = (MvsAcquisition)actual.Reader; return actual; }
            var mapped = c.Topology!.Readers.Select(r => r with { Driver = "manual", Parameters = null }).ToArray();
            var reference = c with { Reader = mapped.Single(r => r.DeviceId == c.Reader.DeviceId), Topology = c.Topology with { Readers = mapped } };
            var ports = new P0SimulationFactory(launch).Create(reference);
            var readers = new Dictionary<string, IControlledObservationSource>(ports.Readers!);
            readers[c.Reader.DeviceId].DisposeAsync().AsTask().GetAwaiter().GetResult();
            Source = new(new(c.Reader.DeviceId, c.Reader.DeviceId, c.Reader.Parameters!), fake is null ? new MvsApi() : fake,
                serial => new ResourceLease(Path.Combine(launch.DataRoot, "simulation-leases"), ["device:hikrobot-gige:" + serial]),
                decoder: decoder ?? (fake is null ? new SoftwareDecoder("utf-8", PrepareZxingFixture) : new ReleasedFrameDecoder(new SoftwareDecoder("utf-8", PrepareZxingFixture), fake)));
            readers[c.Reader.DeviceId] = Source;
            var hashes = new Dictionary<string, string>(ports.ImplementationHashes) { [c.Reader.DeviceId] = Wire.FileHash(typeof(BundledMvs).Assembly.Location) };
            return ports with { Reader = Source, Readers = readers, ImplementationHashes = hashes };
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class ReleasedFrameDecoder(IMonoBarcodeDecoder inner, SoftwareFrameApi api) : IMonoBarcodeDecoder
    {
        public MvsTimingSnapshot? TimingSnapshot => inner.TimingSnapshot;
        public void Prepare() => inner.Prepare();
        public string[] Decode(byte[] pixels, int length, int width, int height)
        {
            if (api.HasFrame) throw new Exception("SDK buffer was retained through recognition/durable ingress");
            return inner.Decode(pixels, length, width, height);
        }
        public void Release() => inner.Release();
    }
    sealed class SoftwareFrameApi(DeviceBinding binding) : IMvsApi
    {
        readonly ConcurrentQueue<(SoftwarePixels Pixels, uint Number)> waiting = new();
        public List<string> Calls = [];
        public int Released;
        public int OpenAttempts, OpenFailures, StopAttempts;
        public bool FailConfigure, IgnoreConfigure;
        public volatile bool Discovered = true;
        public volatile bool FailStop;
        nint buffer;
        public bool HasFrame { get; private set; }
        public void Enqueue(SoftwarePixels pixels, uint number) => waiting.Enqueue((pixels, number));
        public void Initialize() => Calls.Add("initialize");
        public MvsDevice[] Enumerate() => Discovered ? [new(binding.Parameters!["device.ip"], binding.Parameters["device.serial"], "SYNTHETIC", "SYNTHETIC", [])] : [];
        public void Create(MvsDevice d) => Calls.Add("create");
        public void Open() { Calls.Add("open"); Interlocked.Increment(ref OpenAttempts); if (Interlocked.Decrement(ref OpenFailures) >= 0) throw new IOException("Synthetic camera busy"); }
        uint trigger = 1, acquisition;
        public uint EnumValue(string key) => key == "PixelFormat" ? MvsNative.Mono8 : key == "AcquisitionMode" ? acquisition : trigger;
        public void SetEnumValue(string key, uint value) { Calls.Add("set:"+key+"="+value); if(FailConfigure)throw new IOException("Synthetic camera write failure");if(IgnoreConfigure)return; if (key=="TriggerMode") trigger=value; else if (key=="AcquisitionMode") acquisition=value; else throw new Exception("Unexpected camera write"); }
        public long PayloadSize() => 8388608;
        public void ConfigureBuffers(uint count) { }
        public void Start() => Calls.Add("start");
        public MvsFrame? Acquire()
        {
            if (!waiting.TryDequeue(out var next)) { Thread.Sleep(5); return null; }
            if (HasFrame) throw new Exception("Native frame loan leaked");
            buffer = Marshal.AllocHGlobal(next.Pixels.Data.Length); Marshal.Copy(next.Pixels.Data, 0, buffer, next.Pixels.Data.Length); HasFrame = true;
            return new(buffer, (uint)next.Pixels.Width, (uint)next.Pixels.Height, MvsNative.Mono8, (ulong)next.Pixels.Data.Length, next.Number, 0, 0);
        }
        public void Release() { if (!HasFrame) throw new Exception("Double native release"); Marshal.FreeHGlobal(buffer); HasFrame = false; Interlocked.Increment(ref Released); }
        public bool Connected() => Discovered;
        public void Stop() { Calls.Add("stop"); Interlocked.Increment(ref StopAttempts); if (FailStop) throw new IOException("Synthetic native stop ownership retained"); }
        public void Close() => Calls.Add("close");
        public void Destroy() => Calls.Add("destroy");
        public void Shutdown() => Calls.Add("finalize");
    }
}
