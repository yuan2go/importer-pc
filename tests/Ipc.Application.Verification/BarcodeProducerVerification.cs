using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Drivers;
using Ipc.Platform;

internal static class BarcodeProducerVerification
{
    static void Check(bool result, string risk)
    { if (!result) throw new Exception("FAIL PRODUCER: " + risk); Console.WriteLine("PASS PRODUCER: " + risk); }
    static Dictionary<string, string> Parameters(int port = 1234) => new()
    {
        ["device.ip"] = "127.0.0.1", ["device.port"] = port.ToString(), ["profile"] = "sequenced",
        ["producer.id"] = "synthetic.reader", ["producer.version"] = "1.0.0", ["camera.serial"] = "SYNTHETIC-01",
        ["producer.config-hash"] = new('a', 64), ["entity.mode"] = "trigger",
        ["entity.evidence"] = "Synthetic: one trigger per new entity; all candidates; no replay; inspect disconnected parcels",
        ["heartbeat-timeout-seconds"] = "3", ["handshake-timeout-seconds"] = "1"
    };
    static DeviceBinding Binding(Dictionary<string, string> p) => new("parcel-reader", "tcp-line", "synthetic.producer", "1") { Parameters = p };
    public static async Task Run(string workspace, string root)
    {
        Golden(workspace);
        Configuration(root);
        foreach (var failure in new[] { "SequenceGap", "SequenceDuplicate", "SequenceRollback", "HandshakeIdentityMismatch", "EpochMismatch", "TriggerDiscontinuity" })
            await FencedFault(root, failure);
        await Timeout(root, false);
        await Timeout(root, true);
        await StreamIdentity(root);
        await EpochAndOwnership(root);
        await ApplicationAdmission(workspace, root, "observe-only");
        await ApplicationAdmission(workspace, root, "trigger");
        await ApplicationSwitch(workspace, root);
        Console.WriteLine("PASS PRODUCER COMPLETE; loopback/synthetic only, real camera/producer/site NOT_RUN");
    }
    static void Golden(string workspace)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "docs/protocols/barcode-producer-v1.golden.json")));
        var root = doc.RootElement;
        foreach (var test in root.GetProperty("cases").EnumerateArray())
        {
            var parser = new SequencedBarcodeProtocol(Parameters(), root.GetProperty("epoch").GetString()!);
            Check(JsonNode.DeepEquals(JsonNode.Parse(parser.Open), JsonNode.Parse(root.GetProperty("clientOpen").GetRawText())), "client open challenges this registration");
            string outcome = "ok"; var triggers = new List<long>(); var codes = new List<string[]>();
            try
            {
                foreach (var frame in test.GetProperty("serverLines").EnumerateArray())
                {
                    var result = parser.Accept(frame.GetString()!);
                    if (result.Reply is not null)
                        Check(JsonNode.DeepEquals(JsonNode.Parse(result.Reply), JsonNode.Parse(root.GetProperty("clientArm").GetRawText())), "arm preserves the producer's frozen baseline");
                    if (result.Codes is not null) { triggers.Add(parser.Trigger); codes.Add(result.Codes); }
                }
            }
            catch (ProducerProtocolException ex) { outcome = ex.Category; }
            Check(outcome == test.GetProperty("outcome").GetString(), test.GetProperty("name").GetString() + ": " + test.GetProperty("risk").GetString());
            if (test.GetProperty("name").GetString() == "new-trigger-same-code")
                Check(triggers.SequenceEqual([6L, 7L]) && codes.Count == 2 && codes[0].SequenceEqual(codes[1]), "same code, independent trigger identities; heartbeats create no entity");
            if (test.GetProperty("name").GetString() == "zero-and-multiple-candidates")
                Check(codes.Count == 2 && codes[0].Length == 0 && codes[1].Length == 2, "zero/multiple results remain complete for Application filtering");
        }
    }
    static void Configuration(string root)
    {
        var p = Parameters(); TcpLineObservationSource.Validate(Binding(p));
        void Reject(Dictionary<string, string> values, string risk)
        {
            try { TcpLineObservationSource.Validate(Binding(values)); }
            catch (InvalidDataException) { Check(true, risk); return; }
            throw new Exception("Accepted invalid config: " + risk);
        }
        Reject(new(p) { ["entity.evidence"] = " " }, "business mode requires explicit evidence");
        Reject(new(p) { ["entity.mode"] = "line" }, "sequenced cannot use uncorrelated line policy");
        Reject(new(p) { ["heartbeat-timeout-seconds"] = "2" }, "heartbeat timeout cannot precede agreed delivery tolerance");
        Reject(new(p) { ["camera.serial"] = " SYNTHETIC-01" }, "physical aliases cannot evade lease/configuration identity");
        Reject(new(p) { ["profile"] = "plain" }, "plain cannot pretend to validate handshake identity");
        var plain = new Dictionary<string, string> { ["device.ip"] = "127.0.0.1", ["device.port"] = "1234" };
        TcpLineObservationSource.Validate(Binding(plain));
        Check(!BarcodeProducerConfiguration.BusinessEnabled(Binding(plain)), "old plain endpoint defaults to observation, never silently enables business");
        try { BarcodeProducerConfiguration.Validate(Binding(plain), true); throw new Exception("missing Production identity accepted"); }
        catch (InvalidDataException) { Check(true, "formal plain endpoint requires a physical camera identity"); }
        var c = new SportmasterFlow().InitialConfiguration() with { Mode = RunMode.Simulation, DeferredConnections = [] };
        var readers = c.Topology!.Readers.Select((r, i) => r with { Driver = "tcp-line", Parameters = new(p) { ["device.port"] = (1234 + i).ToString() } }).ToArray();
        c = c with { Reader = readers[0], Topology = c.Topology with { Readers = readers } };
        void Duplicate(ParcelConfiguration candidate, string risk)
        {
            try { ParcelCapabilityFactory.ValidateProducerBindings(candidate); throw new Exception("duplicate physical camera accepted"); }
            catch (InvalidDataException) { Check(true, risk); }
        }
        Duplicate(c, "different TCP endpoints cannot configure two producers for one camera");
        readers[1] = readers[1] with { Driver = "hikrobot-mvs", Parameters = new() { ["device.ip"] = "192.0.2.1", ["device.serial"] = "SYNTHETIC-01", ["entity.mode"] = "observe-only" } };
        Duplicate(c with { Topology = c.Topology with { Readers = readers } }, "MVS diagnostic and TCP production cannot configure the same camera");
        var initial = new SportmasterFlow().InitialConfiguration();
        var catalog = new ParcelCapabilityFactory(new(Path.Combine(root, "draft-config"), root, null, null)).Catalog();
        var draft = new StartupConfigurationDraft(initial, catalog);
        Check(draft.Readers.All(r => r.Provider?.Implementation == "tcp-line") && draft.Build().Reader.Parameters!["entity.mode"] == "observe-only", "first setup and daily model never promote observation to line mode");
    }
    sealed class Peer : IAsyncDisposable
    {
        public readonly TcpListener Listener = new(IPAddress.Loopback, 0);
        public readonly ConcurrentQueue<Observation> Observations = new();
        public readonly ConcurrentQueue<DeviceHealthConfirmation> Health = new();
        public readonly string Epoch = Wire.Id();
        public readonly string Boot = new('2', 32);
        public readonly TcpLineObservationSource Source;
        TcpClient? socket;
        StreamReader? reader;
        readonly string expectedSerial;
        public Peer(string root, string deviceId = "synthetic.producer", string serial = "SYNTHETIC-01")
        {
            Listener.Start();
            expectedSerial = serial;
            var settings = Parameters(((IPEndPoint)Listener.LocalEndpoint).Port); settings["camera.serial"] = serial;
            Source = new(Binding(settings) with { DeviceId = deviceId }, key => new ResourceLease(Path.Combine(root, "leases"), [key]));
        }
        public async Task Connect(Func<Observation, bool>? receive = null)
        {
            await Source.StartControlledAsync(Epoch, receive ?? (o => { Observations.Enqueue(o); return true; }), Health.Enqueue, default);
            socket = await Listener.AcceptTcpClientAsync(); reader = new(socket.GetStream(), Encoding.UTF8, false, leaveOpen: true);
            var open = JsonNode.Parse((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)))!)!;
            Check(open["epoch"]!.GetValue<string>() == Epoch && !Health.Any(h => h.Health == DeviceHealth.Ready), "socket connect alone cannot publish sequenced Ready");
        }
        public object Hello(string? serial = null) => new { type = "hello", version = 1, epoch = Epoch, producerId = "synthetic.reader", producerVersion = "1.0.0", cameraSerial = serial ?? expectedSerial, configHash = new string('a', 64), bootId = Boot, sequence = 10, trigger = 5 };
        public object Frame(string type, long sequence, long trigger, string? epoch = null) => type == "event"
            ? new { type, version = 1, epoch = epoch ?? Epoch, bootId = Boot, sequence, trigger, codes = new[] { "0000123" } }
            : new { type, version = 1, epoch = epoch ?? Epoch, bootId = Boot, sequence, trigger };
        public async Task Write(object frame) => await socket!.GetStream().WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(frame) + "\n"));
        public async Task Handshake()
        {
            // Split the hello across reads, then coalesce ready and the first heartbeat.
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Hello()) + "\r\n");
            await socket!.GetStream().WriteAsync(bytes.AsMemory(0, 3)); await socket.GetStream().WriteAsync(bytes.AsMemory(3));
            var arm = JsonNode.Parse((await reader!.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)))!)!;
            Check(arm["type"]!.GetValue<string>() == "arm" && arm["sequence"]!.GetValue<int>() == 10 && !Health.Any(h => h.Health == DeviceHealth.Ready), "hello/arm alone cannot admit a parcel");
            await socket.GetStream().WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Frame("ready", 10, 5)) + "\n" + JsonSerializer.Serialize(Frame("heartbeat", 11, 5)) + "\n"));
            await Until(() => Source.Diagnostics.Heartbeats == 1);
            Check(Health.Last().Health == DeviceHealth.Ready && Observations.IsEmpty, "ready plus valid heartbeat establishes health without inventing an entity");
        }
        public async ValueTask DisposeAsync()
        { await Source.DisposeAsync(); reader?.Dispose(); socket?.Dispose(); Listener.Stop(); }
    }
    static async Task Until(Func<bool> condition, int ms = 4000)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < ms) await Task.Delay(10);
        if (!condition()) throw new TimeoutException("Expected producer state did not arrive");
    }
    static async Task FencedFault(string root, string failure)
    {
        await using var peer = new Peer(root); await peer.Connect();
        if (failure == "HandshakeIdentityMismatch") await peer.Write(peer.Hello("WRONG-CAMERA"));
        else
        {
            await peer.Handshake();
            var n = failure switch { "SequenceGap" => 13, "SequenceDuplicate" => 11, "SequenceRollback" => 10, _ => 12 };
            await peer.Write(peer.Frame("event", n, failure == "TriggerDiscontinuity" ? 5 : 6, failure == "EpochMismatch" ? new('3', 32) : null));
        }
        await Until(() => peer.Health.Any(h => h.Health == DeviceHealth.Faulted));
        var state = peer.Health.Last();
        Check(peer.Source.LastFailure == failure && peer.Observations.IsEmpty && state.ValidUntil is null && state.Epoch == peer.Epoch && peer.Source.Epoch != peer.Epoch &&
            peer.Source.Diagnostics.Failures.GetValueOrDefault(failure) == 1, failure + " fences business, counts the cause and retires the original epoch");
        Check(!state.Source.Contains("SYNTHETIC-01") && !state.Source.Contains("WRONG-CAMERA") && !state.Source.Contains("0000123"), "fault diagnostics expose no barcode or physical serial");
        Check(!peer.Listener.Pending(), "fault never automatically reconnects or replays inputs");
    }
    static async Task Timeout(string root, bool streaming)
    {
        await using var peer = new Peer(root); await peer.Connect();
        if (streaming)
        {
            await peer.Handshake();
            // Events continue while the producer's heartbeat task is stalled.
            for (var n = 12; n < 17; n++) { await peer.Write(peer.Frame("event", n, n - 6)); await Task.Delay(400); }
        }
        await Until(() => peer.Health.Any(h => h.Health == DeviceHealth.Faulted));
        Check(peer.Source.LastFailure == (streaming ? "HeartbeatTimeout" : "HandshakeTimeout") && peer.Health.Last().ValidUntil is null,
            streaming ? "barcode traffic cannot hide a stalled producer heartbeat" : "a silent peer cannot remain indefinitely Connecting");
    }
    static async Task StreamIdentity(string root)
    {
        async Task<Observation> Read(string deviceId, string serial)
        {
            await using var peer = new Peer(root, deviceId, serial);
            await peer.Connect(); await peer.Handshake(); await peer.Write(peer.Frame("event", 12, 6));
            await Until(() => peer.Observations.Count == 1);
            return peer.Observations.Single();
        }
        var left = await Read("synthetic.left", "SYNTHETIC-LEFT");
        var right = await Read("synthetic.right", "SYNTHETIC-RIGHT");
        var reconnect = await Read("synthetic.left", "SYNTHETIC-LEFT");
        Check(left.AdmissionId != right.AdmissionId && left.SourceEventId != right.SourceEventId,
            "one service process can use the same boot and trigger counters on independent camera streams without merging entities");
        Check(left.Epoch != reconnect.Epoch && left.AdmissionId == reconnect.AdmissionId,
            "a fresh connection token cannot disguise the same original stream trigger as a new entity");
    }
    static async Task EpochAndOwnership(string root)
    {
        await using var peer = new Peer(root);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        await peer.Connect(o => { peer.Observations.Enqueue(o); entered.Set(); release.Wait(); return true; }); await peer.Handshake();
        await peer.Write(peer.Frame("event", 12, 6));
        Check(entered.Wait(3000), "original callback reached the real reader task");
        try
        {
            using var cancelled = new CancellationTokenSource(30);
            try { await peer.Source.StopAsync(cancelled.Token); throw new Exception("Stop claimed completed"); } catch (OperationCanceledException) { }
            try { using var duplicate = new ResourceLease(Path.Combine(root, "leases"), ["device:hikrobot-gige:SYNTHETIC-01"]); throw new Exception("MVS acquired active camera"); }
            catch (IOException) { Check(true, "cancelled Stop keeps the physical camera lease until the original callback drains"); }
            try { await peer.Source.StartControlledAsync(Wire.Id(), _ => true, _ => { }, default); throw new Exception("overlapping start accepted"); }
            catch (InvalidOperationException) { Check(true, "a cancelled drain cannot start a second owner"); }
        }
        finally { release.Set(); }
        await peer.Source.StopAsync(default);
        using var free = new ResourceLease(Path.Combine(root, "leases"), ["device:hikrobot-gige:SYNTHETIC-01"]);
        Check(free.Resources.Length == 1, "actual completed Stop releases the shared MVS diagnostic lease");
    }
    static async Task ApplicationSwitch(string workspace, string root)
    {
        // Real Application/SQLite ownership; a manual source supplies synthetic entities.
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        config = config with { Wcs = config.Wcs! with { Parameters = new(config.Wcs.Parameters) { ["reply-mode"] = "lose-after-commit" } }, StationId = "producer-switch-" + Wire.Id() };
        var path = Path.Combine(root, "switch"); Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), config);
        var launch = new BusinessLaunch(path, Path.Combine(root, "switch-data"), null, null);
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), OperatingSystem.IsWindows() ? new P0SimulationFactory(launch) : new ParcelCapabilityFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await app.StartAsync(default);
        app.EmitReferenceAt("left", "synthetic.inflight", "0000123", "synthetic.event");
        await UntilAsync(async () => (await app.SnapshotAsync()).Operations.SingleOrDefault()?.State == ParcelState.WcsUnknown);
        var snapshot = await app.SnapshotAsync();
        await app.CommandAsync(new(Wire.Id(), snapshot.Revision, "Pause")); await ManagementModeFixture.Authorize(app);
        snapshot = await app.SnapshotAsync();
        var readers = config.Topology!.Readers.Select((r, i) => i == 0 ? r with { Driver = "tcp-line", Parameters = new() { ["device.ip"] = "127.0.0.1", ["device.port"] = "1234", ["entity.mode"] = "observe-only" } } : r).ToArray();
        var candidate = config with { Reader = readers[0], Topology = config.Topology with { Readers = readers } };
        var receipt = await app.StageConfigurationAsync(snapshot.Configuration.Revision, candidate, "synthetic producer switch");
        Check(receipt.Status == "Rejected" && receipt.Reason.Contains("ProducerSwitchBlocked") && (await app.SnapshotAsync()).Configuration.Pending is null,
            "an in-flight entity prevents saving a new producer even after pause and management authorization");
        var original = (await app.SnapshotAsync()).Operations.Single();
        var request = Wire.Encode(original.Wcs!.Request);
        Check((await app.ReconcileAsync(original.Id)).Status != "Rejected", "original unknown is reconciled by its original query contract");
        await UntilAsync(async () => (await app.SnapshotAsync()).Operations.Single().State == ParcelState.SlotResolved);
        snapshot = await app.SnapshotAsync();
        receipt = await app.StageConfigurationAsync(snapshot.Configuration.Revision, candidate, "synthetic: old producer stopped, camera released, entities inspected");
        Check(receipt.Status == "PendingRestart", "settled responsibilities allow an explicitly reviewed producer candidate");
        Check((await app.ReconnectDeviceAsync(config.Reader.DeviceId)).Status == "Reconnected", "controlled reconnect remains available using Effective");
        snapshot = await app.SnapshotAsync();
        Check(snapshot.Configuration.Effective.Reader.Driver == "manual" && snapshot.Configuration.Pending!.Reader.Driver == "tcp-line" && snapshot.Paused &&
            snapshot.Operations.Single().Wcs!.Attempts == 1 && Wire.Encode(snapshot.Operations.Single().Wcs!.Request) == request,
            "Pending is not hot-applied; reconnect preserves pause and original request bytes/attempt count");
    }
    static async Task ApplicationAdmission(string workspace, string root, string mode)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        var parameters = Parameters(((IPEndPoint)listener.LocalEndpoint).Port); parameters["entity.mode"] = mode;
        parameters["handshake-timeout-seconds"] = "5";
        var left = c.Reader with { Driver = "tcp-line", Parameters = parameters };
        c = c with { Reader = left, Topology = c.Topology! with { Readers = [left, c.Topology.Readers[1]] }, DeferredConnections = [c.Topology.Readers[1].DeviceId] };
        var path = Path.Combine(root, "admission-" + mode); Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), c);
        var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null);
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), OperatingSystem.IsWindows() ? new P0SimulationFactory(launch) : new ParcelCapabilityFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await app.StartAsync(default); using var socket = await listener.AcceptTcpClientAsync();
        using var input = new StreamReader(socket.GetStream(), Encoding.UTF8, false, leaveOpen: true);
        var epoch = JsonNode.Parse((await input.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)))!)!["epoch"]!.GetValue<string>();
        Check(!(await app.SnapshotAsync()).Availability!.Admit, "real Application cannot admit before the producer handshake");
        using var golden = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "docs/protocols/barcode-producer-v1.golden.json")));
        var frames = golden.RootElement.GetProperty("cases")[0].GetProperty("serverLines").EnumerateArray().Select(n => JsonNode.Parse(n.GetString()!)!).ToArray();
        async Task Send(JsonNode node) { node["epoch"] = epoch; await socket.GetStream().WriteAsync(Encoding.UTF8.GetBytes(node.ToJsonString() + "\n")); }
        // ToJsonString defaults to a single line; the production framer still owns its full byte budget.
        await Send(frames[0]); _ = await input.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)); await Send(frames[1]);
        await Send(frames[2]); await Send(frames[3]);
        await UntilAsync(async () => (await app.SnapshotAsync()).Inputs?.Length == 1);
        if (mode == "trigger")
        {
            await UntilAsync(async () => (await app.SnapshotAsync()).Operations.SingleOrDefault()?.State == ParcelState.SlotResolved);
            await Send(frames[4]);
            await UntilAsync(async () => (await app.SnapshotAsync()).Operations.Count(o => o.State == ParcelState.SlotResolved) == 2);
            var s = await app.SnapshotAsync();
            Check(s.Operations.Select(o => o.AdmissionId).Distinct().Count() == 2 && s.Operations.All(o => o.Wcs!.Attempts == 1), "same barcode in two valid triggers creates two independent, single-attempt requests");
            var bad = frames[4].DeepClone(); bad["sequence"] = 15; bad["trigger"] = 8; await Send(bad);
            await UntilAsync(async () => (await app.SnapshotAsync()).Devices!.Single(d => d.DeviceId == left.DeviceId).Health == DeviceHealth.Faulted);
            s = await app.SnapshotAsync();
            Check(!s.Positions!.Single(p => p.Id == "left").Admit && s.Operations.Length == 2, "source sequence fault closes Application admission without a compensating request");
        }
        else
        {
            var s = await app.SnapshotAsync();
            Check(s.Devices!.Single(d => d.DeviceId == left.DeviceId).IsValid(DateTimeOffset.UtcNow) && !s.Availability!.Admit && s.Operations.Length == 0 && s.Inputs!.Single().Observation.Kind == "UncorrelatedRead",
                "healthy observe-only source remains visible but cannot enable business or create a request");
        }
    }
    static async Task UntilAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 200; i++) { if (await condition()) return; await Task.Delay(20); }
        throw new TimeoutException("Application state did not arrive");
    }
}
