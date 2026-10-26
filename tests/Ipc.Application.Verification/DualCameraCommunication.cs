using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Drivers;
using Ipc.Platform;

static class DualCameraCommunication
{
    static void Check(bool value, string message)
    { if (!value) throw new Exception("FAIL DCC01: " + message); Console.WriteLine("PASS DCC01: " + message); }
    static TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static async Task Run(string workspace, string root)
    {
        await Protocols();
        await StopOwnership();
        await ImmediateStartup(workspace, Path.Combine(root, "immediate"));
        foreach (var cancel in new[] { false, true }) await FailedStartup(workspace, Path.Combine(root, "failure-" + cancel), cancel);
        await SingleSideUnavailable(workspace, Path.Combine(root, "one-unavailable"));
        await StartupOverload(workspace, Path.Combine(root, "overload"));
        await ConfirmationEvidence();
        Console.WriteLine("PASS DCC01 COMPLETE");
    }
    static async Task ImmediateStartup(string workspace, string path)
    {
        using var leftListener = new TcpListener(IPAddress.Loopback, 0); leftListener.Start();
        using var rightListener = new TcpListener(IPAddress.Loopback, 0); rightListener.Start();
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        var rs = c.Topology!.Readers.Select((r, i) => r with { Driver = "tcp-line", Parameters = new()
        { ["device.ip"] = "127.0.0.1", ["device.port"] = ((IPEndPoint)(i == 0 ? leftListener : rightListener).LocalEndpoint).Port.ToString(), ["entity.mode"] = "line" } }).ToArray();
        c = c with { Reader = rs[0], Topology = c.Topology with { Readers = rs } };
        Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), c);
        var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null);
        var factory = new StartupFactory(new ParcelCapabilityFactory(launch));
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } };
        var start = app.StartAsync(default);
        using var left = await leftListener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await left.GetStream().WriteAsync(Encoding.UTF8.GetBytes("000123\n"));
        try
        {
            Check(await factory.FirstIngress.Task.WaitAsync(TimeSpan.FromSeconds(5)), "startup bounded reception is open before business admission");
            var starting = await app.SnapshotAsync();
            Check(starting.Operations.Length == 0 && starting.Availability?.Admit == false && !starting.Positions!.Any(p => p.Admit),
                "startup barrier forbids operations and external requests while another reader is not started");
        }
        finally { factory.Release.TrySetResult(true); }
        await start.WaitAsync(TimeSpan.FromSeconds(8));
        using var right = await rightListener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Check(await factory.FirstIngress.Task, "legal frame sent immediately on connect enters bounded queue during startup");
        Check((await Wait(app, s => s.Inputs?.Length == 1)).Devices!.Single(d => d.DeviceId == rs[0].DeviceId).Health == DeviceHealth.Ready,
            "startup ingress rejection does not spuriously disconnect left camera");
        var original = await Wait(app, s => s.Operations.Any(o => o.State == ParcelState.SlotResolved));
        var first = original.Operations.Single();
        Check(first.PositionId == "left" && first.ReaderBinding!.DeviceId == rs[0].DeviceId && first.Acquisition!.Epoch == original.Devices!.Single(d => d.DeviceId == rs[0].DeviceId).Epoch,
            "effective endpoint maps through DeviceId and topology to original left acquisition");
        await left.GetStream().WriteAsync(new byte[] { 255, 10 });
        await Wait(app, s => s.Devices!.Single(d => d.DeviceId == rs[0].DeviceId).Health == DeviceHealth.Faulted);
        await right.GetStream().WriteAsync(Encoding.UTF8.GetBytes("000123\n"));
        var dual = await Wait(app, s => s.Operations.Count(o => o.State == ParcelState.SlotResolved) == 2);
        Check(dual.Operations.Select(o => o.AdmissionId).Distinct().Count() == 2 && dual.Operations.Single(o => o.PositionId == "right").ReaderBinding!.DeviceId == rs[1].DeviceId,
            "left protocol fault does not block right; same barcode retains two entities and matching original requests");
        Check(!dual.Devices!.Any(d => d.Source.Contains("127.0.0.1")) && !dual.Health.Any(h => h.Detail.Contains("127.0.0.1")), "ordinary health projection conceals actual TCP endpoints");
        await Command(app, "Pause"); await ManagementModeFixture.Authorize(app);
        await right.GetStream().WriteAsync(Encoding.UTF8.GetBytes("000456\n"));
        var blocked = await Wait(app, s => s.Inputs!.Any(i => i.PositionId == "right" && i.Observation.Codes.Contains("000456")));
        Check(blocked.Inputs!.Single(i => i.PositionId == "right").Outcome == ParcelInputOutcome.Blocked && blocked.Operations.Length == 2 && blocked.Devices!.Single(d => d.DeviceId == rs[1].DeviceId).Health == DeviceHealth.Ready,
            "business guard rejection is audited without disconnecting healthy TCP or creating an RCS request");
        var rightEpoch = blocked.Devices!.Single(d => d.DeviceId == rs[1].DeviceId).Epoch;
        Check((await app.ReconnectDeviceAsync(rs[0].DeviceId)).Status == "Reconnected", "explicit single-side reconnect stops original and opens a new epoch");
        using var leftAgain = await leftListener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
        var reconnected = await app.SnapshotAsync();
        Check(reconnected.Devices!.Single(d => d.DeviceId == rs[1].DeviceId).Epoch == rightEpoch && reconnected.Operations.All(o => o.Wcs!.Attempts == 1),
            "left reconnect preserves right epoch and never replays original requests");
        var old = first.Acquisition!;
        Check(!factory.Left!.Previous!(new(Wire.Id(), rs[0].DeviceId, old.Epoch, "late", 999, "late", "Read", ["000999"], null, DateTimeOffset.UtcNow)),
            "old registered callback cannot contaminate reconnected camera");
        Check(reconnected.Devices!.Single(d => d.DeviceId == rs[1].DeviceId).Source.Contains("LastValidObservationAt") &&
            reconnected.Devices!.Single(d => d.DeviceId == rs[1].DeviceId).Source.Contains("LastConfirmedAt") &&
            reconnected.Devices!.Single(d => d.DeviceId == rs[1].DeviceId).Source.Contains("LivenessTimeoutSeconds") &&
            reconnected.Devices!.Single(d => d.DeviceId == rs[1].DeviceId).Source.Contains("TCP keepalive is not application heartbeat; business admission is separate"),
            "management snapshot separates local endpoints, finite confirmation, recent bytes and valid observation evidence");
        var ui = new Ipc.Application.Sportmaster.Desktop.SupplyStationViewModel(app); await ui.RefreshDisplayAsync();
        var diagnostic = ui.ParameterRuntimeSummary(new(rs[1].DeviceId, rs[1].DeviceId, "右相机"));
        Check(ui.Right.DeviceLabel == "相机：模拟连接" && reconnected.Devices!.Single(d => d.DeviceId == rs[1].DeviceId).IsValid(DateTimeOffset.UtcNow) &&
            diagnostic.Contains("LastValidObservationAt") && diagnostic.Contains("静默断网"),
            "synthetic workstation labels the confirmed simulated connection; management detail keeps distinct receive/observation evidence and recovery limits");
        using var nextListener = new TcpListener(IPAddress.Loopback, 0); nextListener.Start();
        var revision = reconnected.Configuration;
        var nextReaders = rs.Select((r, i) => i == 0 ? r : r with { Parameters = new(r.Parameters!) { ["device.port"] = ((IPEndPoint)nextListener.LocalEndpoint).Port.ToString() } }).ToArray();
        var candidate = c with { Reader = nextReaders[0], Topology = c.Topology with { Readers = nextReaders } };
        BusinessReceipt staged;
        do { await Wait(app, x => x.Availability!.Configure); staged = await app.StageConfigurationAsync(revision.Revision, candidate, "Synthetic TCP endpoint change"); }
        while (staged.Status == "Rejected" && staged.Reason == "Pause and authenticate maintenance before configuration");
        Check(staged.Status == "PendingRestart", "endpoint edit saves one Pending candidate through original configuration CAS");
        Check((await app.ReconnectDeviceAsync(rs[1].DeviceId)).Status == "Reconnected", "reconnect uses old Effective endpoint while Pending exists");
        using var oldRightAgain = await rightListener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
        var pending = await app.SnapshotAsync();
        Check(!nextListener.Pending() && pending.Configuration.Effective.Topology!.Readers[1].Parameters!["device.port"] == rs[1].Parameters!["device.port"],
            "save and reconnect cannot masquerade as Pending activation");
        await app.DisposeAsync();
        await using var activated = new ParcelRuntime(launch, new SportmasterFlow(), new ParcelCapabilityFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await activated.StartAsync(default);
        using var activeLeft = await leftListener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
        using var activeRight = await nextListener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
        await ManagementModeFixture.Authorize(activated);
        var effective = await activated.SnapshotAsync();
        Check(effective.Configuration.Pending is null && effective.Configuration.Effective.Topology!.Readers[1].Parameters!["device.port"] == nextReaders[1].Parameters!["device.port"] && effective.Paused,
            "controlled restart alone activates saved endpoint and preserves pause");
        Check(effective.Operations.Single(o => o.Id == first.Id).Wcs!.Request.PayloadHash == first.Wcs!.Request.PayloadHash && effective.Operations.All(o => o.Wcs!.Attempts == 1),
            "configuration activation preserves original request bytes, target and attempt count");
    }
    static async Task Command(ParcelRuntime app, string kind)
    {
        for (var i = 0; i < 100; i++)
        {
            var snapshot = await app.SnapshotAsync(); var r = await app.CommandAsync(new(Wire.Id(), snapshot.Revision, kind));
            if (r.Status == "Persisted") return;
            if (r.Status != "Conflict" && r.Reason != "Unsupported command or an application guard is closed") throw new Exception(r.Reason);
            await Task.Delay(20);
        }
        throw new TimeoutException("Command did not drain");
    }
    static DeviceBinding TcpBinding(TcpListener listener) => new("parcel-reader", "tcp-line", "synthetic.camera", "1")
    { Parameters = new() { ["device.ip"] = "127.0.0.1", ["device.port"] = ((IPEndPoint)listener.LocalEndpoint).Port.ToString(), ["entity.mode"] = "line" } };
    static async Task Until(Func<bool> condition)
    { for (var i = 0; i < 250 && !condition(); i++) await Task.Delay(10); if (!condition()) throw new TimeoutException("TCP evidence wait"); }
    static async Task Protocols()
    {
        foreach (var scenario in new[] { "split", "observe", "max-lf", "max-crlf", "long", "utf8", "empty", "control", "partial", "eof", "reject", "throw" })
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var b = TcpBinding(listener); if (scenario == "observe") b = b with { Parameters = new(b.Parameters!) { ["entity.mode"] = "observe-only" } };
            await using var source = new TcpLineObservationSource(b);
            var observations = new ConcurrentQueue<Observation>(); var states = new ConcurrentQueue<DeviceHealthConfirmation>();
            await source.StartControlledAsync("protocol-epoch", o => { observations.Enqueue(o); if (scenario == "throw") throw new InvalidOperationException("private callback detail"); return scenario != "reject"; }, states.Enqueue, default);
            using var peer = await listener.AcceptTcpClientAsync();
            Check(peer.Available == 0, scenario + ": connecting sends no handshake or application data");
            byte[] bytes = scenario switch
            {
                "split" => Encoding.UTF8.GetBytes("条码\r\nSAME\nSAME\n"), "observe" => Encoding.UTF8.GetBytes("CODE\n"),
                "max-lf" => Encoding.UTF8.GetBytes(new string('x', 4096) + "\n"), "max-crlf" => Encoding.UTF8.GetBytes(new string('x', 4096) + "\r\n"),
                "long" => Encoding.UTF8.GetBytes(new string('x', 4097)), "utf8" => [255, 10], "empty" => [10], "control" => [65, 0, 10],
                "partial" => Encoding.UTF8.GetBytes("PARTIAL"), "eof" => [], _ => Encoding.UTF8.GetBytes("CODE\nSECOND\n")
            };
            if (scenario == "split") { await peer.GetStream().WriteAsync(bytes.AsMemory(0, 1)); await Task.Delay(20); await peer.GetStream().WriteAsync(bytes.AsMemory(1)); }
            else await peer.GetStream().WriteAsync(bytes);
            if (scenario is "partial" or "eof") peer.Close();
            var good = scenario is "split" or "observe" or "max-lf" or "max-crlf";
            if (good)
            {
                await Until(() => observations.Count == (scenario == "split" ? 3 : 1));
                Check(source.LastFailure is null, scenario + ": complete valid frame retained without transport failure");
                if (scenario == "split") Check(observations.Select(o => o.Codes[0]).SequenceEqual(new[] { "条码", "SAME", "SAME" }) && observations.Select(o => o.AdmissionId).Distinct().Count() == 3,
                    "split UTF8 and coalesced LF/CRLF retain order and distinct same-code entities");
                if (scenario == "observe") Check(observations.Single().Kind == "UncorrelatedRead", "observe-only never invents an entity boundary");
            }
            else
            {
                await Until(() => states.Any(h => h.Health == DeviceHealth.Faulted));
                var expected = scenario switch { "long" => "FrameTooLong", "utf8" => "InvalidUtf8", "empty" => "EmptyFrame", "control" => "ControlCharacter", "partial" => "IncompleteFrame", "eof" => "RemoteClosed", "reject" => "IngressRejected", _ => "IngressCallbackFailure" };
                Check(source.LastFailure == expected && observations.Count == (scenario is "reject" or "throw" ? 1 : 0), scenario + ": typed failure fences subsequent frames without truncation");
                Check(!states.Last().Source.Contains("private callback detail") && !listener.Pending(), "failure evidence omits raw data and never auto-reconnects");
            }
            await source.StopAsync(default);
            Check(states.Last().Health == DeviceHealth.Stopped, scenario + ": stop drains reading and local-confirmation tasks");
        }
    }
    static async Task StopOwnership()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        await using var source = new TcpLineObservationSource(TcpBinding(listener));
        using var release = new ManualResetEventSlim(); var entered = Signal<bool>();
        try
        {
            await source.StartControlledAsync("owned", _ => { entered.TrySetResult(true); release.Wait(); return true; }, _ => { }, default);
            using var peer = await listener.AcceptTcpClientAsync(); await peer.GetStream().WriteAsync(Encoding.UTF8.GetBytes("CODE\n"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); using var budget = new CancellationTokenSource(80);
            var cancelled = false; try { await source.StopAsync(budget.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "blocked callback allows bounded Stop cancellation without pretending drain completed");
            var refused = false; try { await source.StartControlledAsync("wrong", _ => true, _ => { }, default); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "cancelled stop retains original resource ownership and refuses overlapping start");
        }
        finally { release.Set(); }
        await source.StopAsync(default);
        await source.StartControlledAsync("owned-next", _ => true, _ => { }, default);
        using var next = await listener.AcceptTcpClientAsync(); Check(source.Epoch == "owned-next", "explicit drain permits controlled new epoch");
    }
    static async Task ConfirmationEvidence()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var binding = TcpBinding(listener);
        var leaseRoot = Path.Combine(Path.GetTempPath(), "dcc01-lease-" + Wire.Id());
        using (var original = new ResourceLease(leaseRoot, ["reader:tcp:" + TcpLineObservationSource.EndpointIdentity(binding)]))
        {
            var alias = binding with { DeviceId = "alias", Parameters = new(binding.Parameters!) { ["device.port"] = "000" + binding.Parameters!["device.port"] } };
            var refused = false; try { using var duplicate = new ResourceLease(leaseRoot, ["reader:tcp:" + TcpLineObservationSource.EndpointIdentity(alias)]); } catch (IOException) { refused = true; }
            Check(refused, "physical endpoint remains exclusive across device aliases and numeric port spellings");
        }
        await using var source = new TcpLineObservationSource(binding); var session = new ObservationSession(source);
        var originalEndpoint = TcpLineObservationSource.EndpointIdentity(binding);
        binding.Parameters!["device.port"] = "1"; // Caller draft mutation must not alter this running source.
        var transitions = new ConcurrentQueue<DeviceHealthConfirmation>();
        await session.StartAsync(_ => true, transitions.Enqueue, default); using var peer = await listener.AcceptTcpClientAsync();
        await Task.Delay(1100); await peer.GetStream().WriteAsync(Encoding.UTF8.GetBytes("CODE\n"));
        await Until(() =>
        {
            var evidence = session.Health!.Source;
            using var json = System.Text.Json.JsonDocument.Parse(evidence[evidence.IndexOf('{')..]);
            return json.RootElement.GetProperty("LastValidObservationAt").ValueKind == System.Text.Json.JsonValueKind.String;
        });
        var current = session.Health!.Source;
        using var facts = System.Text.Json.JsonDocument.Parse(current[current.IndexOf('{')..]);
        Check(transitions.Count(h => h.Health == DeviceHealth.Ready) == 1 && facts.RootElement.GetProperty("ConfiguredRemote").GetString() == originalEndpoint,
            "health refreshes and valid data update current evidence without transition-log flooding or mutable-draft rebinding");
    }
    static async Task StartupOverload(string workspace, string path)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        var r = TcpBinding(listener) with { DeviceId = c.Reader.DeviceId };
        c = c with { QueueCapacity = 1, Reader = r, Topology = c.Topology! with { Readers = [r, c.Topology!.Readers[1]] } };
        Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), c);
        var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null); var factory = new StartupFactory(new(launch));
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } };
        try
        {
            var start = app.StartAsync(default); using var peer = await listener.AcceptTcpClientAsync();
            await peer.GetStream().WriteAsync(Encoding.UTF8.GetBytes("000123\n000456\n"));
            await Until(() => factory.Left?.Failure == "IngressRejected"); factory.Release.TrySetResult(true); await start;
            var s = await Wait(app, x => x.Inputs?.Length == 1);
            Check(s.Inputs!.Single().Outcome == ParcelInputOutcome.Blocked && s.Operations.Length == 0 && File.Exists(Path.Combine(launch.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId, "overload.json")),
                "startup queue overflow preserves durable side-specific overload fence; no dropped-frame success");
            Check(app.EmitReferenceAt("right", "right-overload", "000789", "right-overload"), "healthy right source still receives after left startup overflow");
            await Wait(app, x => x.Operations.Any(o => o.PositionId == "right" && o.State == ParcelState.SlotResolved));
        }
        finally { factory.Release.TrySetResult(true); }
    }
    static async Task FailedStartup(string workspace, string path, bool cancel)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        var r = TcpBinding(listener) with { DeviceId = c.Reader.DeviceId };
        c = c with { Reader = r, Topology = c.Topology! with { Readers = [r, c.Topology!.Readers[1]] } };
        Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), c);
        var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null); var factory = new StartupFactory(new(launch)) { Fail = !cancel };
        int failureAuditCompleted = 0, prematureDisposition = 0;
        var app = new ParcelRuntime(launch, new SportmasterFlow(), factory)
        {
            RuntimeIdentityPreflight = () => { },
            Boundary = point =>
            {
                // Hold the original startup writer while another worker can run.
                // This detects the ownership overlap without relying on SQLite to crash.
                if (point == "BeforeStartupFailureAudit") Thread.Sleep(150);
                if (point == "AfterStartupFailureAudit") Volatile.Write(ref failureAuditCompleted, 1);
                if (point == "BeforeStartupIngressDisposition" && Volatile.Read(ref failureAuditCompleted) == 0)
                    Interlocked.Exchange(ref prematureDisposition, 1);
            }
        };
        using var cancellation = new CancellationTokenSource();
        try
        {
            var start = app.StartAsync(cancellation.Token); using var peer = await listener.AcceptTcpClientAsync();
            await peer.GetStream().WriteAsync(Encoding.UTF8.GetBytes("000123\n")); await factory.FirstIngress.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (cancel) cancellation.Cancel(); else factory.Release.TrySetResult(true);
            var failed = false; try { await start; } catch (Exception e) when (e is InvalidDataException or OperationCanceledException) { failed = true; }
            Check(failed && (await app.SnapshotAsync()).Operations.Length == 0, (cancel ? "cancelled" : "failed") + " startup cannot dispatch buffered observations");
        }
        finally { factory.Release.TrySetResult(true); await app.DisposeAsync(); }
        Check(failureAuditCompleted == 1 && prematureDisposition == 0,
            (cancel ? "cancelled" : "failed") + " startup records its terminal audit before buffered-input disposition shares the database owner");
        var db = Path.Combine(launch.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId, "sportmaster.db");
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = db, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT count(*) FROM parcel_audit WHERE event='StartupIngressBlocked'";
        Check(Convert.ToInt64(command.ExecuteScalar()) == 1, "unsuccessful startup drains bounded input into explicit durable inspection evidence");
    }
    static async Task SingleSideUnavailable(string workspace, string path)
    {
        using var unavailable = new TcpListener(IPAddress.Loopback, 0); unavailable.Start();
        using var good = new TcpListener(IPAddress.Loopback, 0); good.Start();
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        var rs = new[] { TcpBinding(unavailable) with { DeviceId = c.Topology!.Readers[0].DeviceId }, TcpBinding(good) with { DeviceId = c.Topology!.Readers[1].DeviceId } };
        unavailable.Stop(); c = c with { Reader = rs[0], Topology = c.Topology with { Readers = rs } };
        Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), c);
        var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null);
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), new ParcelCapabilityFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await app.StartAsync(default); using var peer = await good.AcceptTcpClientAsync();
        await peer.GetStream().WriteAsync(Encoding.UTF8.GetBytes("000123\n"));
        var s = await Wait(app, x => x.Operations.Any(o => o.State == ParcelState.SlotResolved));
        Check(s.Devices!.Single(d => d.DeviceId == rs[0].DeviceId).Health == DeviceHealth.Faulted && s.Operations.Single().PositionId == "right", "startup connection refusal isolates left and allows independent right business");
        await Command(app, "Pause"); await ManagementModeFixture.Authorize(app);
        Check((await app.ReconnectDeviceAsync(rs[0].DeviceId)).Status == "MaintenanceIncomplete", "connection-refused controlled reconnect returns classified incomplete receipt instead of escaping SocketException");
    }
    static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> condition)
    {
        for (var i = 0; i < 200; i++) { var s = await app.SnapshotAsync(); if (condition(s)) return s; await Task.Delay(20); }
        throw new TimeoutException("DCC01: " + Wire.Encode(await app.SnapshotAsync()));
    }
    sealed class StartupFactory(ParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        public TaskCompletionSource<bool> FirstIngress { get; } = Signal<bool>();
        public TaskCompletionSource<bool> Release { get; } = Signal<bool>();
        public StartupReader? Left;
        public bool Fail;
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public ParcelPorts Create(ParcelConfiguration c)
        {
            var p = inner.Create(c); var rs = p.Readers!.ToDictionary(x => x.Key, x => x.Value);
            rs[c.Reader.DeviceId] = Left = new StartupReader(rs[c.Reader.DeviceId], FirstIngress, Release, () => Fail);
            return p with { Reader = rs[c.Reader.DeviceId], Readers = rs };
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class StartupReader(IControlledObservationSource inner, TaskCompletionSource<bool> ingress, TaskCompletionSource<bool> release, Func<bool> fail) : IControlledObservationSource
    {
        public string? Failure => (inner as TcpLineObservationSource)?.LastFailure;
        public Func<Observation, bool>? Previous, Current;
        public string DeviceId => inner.DeviceId; public string Epoch => inner.Epoch; public string ConfirmationSource => inner.ConfirmationSource;
        public Task StartAsync(Func<Observation, bool> r, Action<string, string, DeviceHealth> h, CancellationToken t) => throw new NotSupportedException();
        public async Task StartControlledAsync(string e, Func<Observation, bool> r, Action<DeviceHealthConfirmation> h, CancellationToken t)
        {
            Previous = Current; Current = r;
            await inner.StartControlledAsync(e, o => { var accepted = r(o); ingress.TrySetResult(accepted); return accepted; }, h, t);
            await ingress.Task.WaitAsync(TimeSpan.FromSeconds(4), t); // Deterministic scheduling, never production code.
            await release.Task.WaitAsync(t);
            if (fail()) throw new InvalidDataException("Synthetic startup activation failure");
        }
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
