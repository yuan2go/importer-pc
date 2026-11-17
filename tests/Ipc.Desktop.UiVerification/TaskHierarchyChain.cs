using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Adapters;
using Ipc.Assembly;
using Ipc.Components;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Platform;

static partial class Program
{
    // Expected values are authored from the contract BEFORE running the parser/VM.
    // Only requestId is echoed from the independent loopback transport. No projected
    // snapshots are manufactured; all observations go through the real Application.
    static async Task VerifyHierarchyChain(string root, string evidence, List<string> lines, bool interactive)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); lines.Add("PASS CHAIN " + reason); }
        await using var peer = new HierarchyRcsPeer();
        var folder = Path.Combine(evidence, "chain-config"); Directory.CreateDirectory(folder);
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        var binding = new CapabilityBinding("integration", "sportmaster-rcs", "1.0.0", "sportmaster.integration", peer.Endpoint, RcsRequestPolicy.Contract,
            new() { ["zone-id"] = "71", ["project-id"] = "83", ["heartbeat-interval-ms"] = "1000", ["request-timeout-ms"] = "10000", ["left-bcr-code"] = "TASK-LEFT", ["right-bcr-code"] = "TASK-RIGHT" });
        var config = seed with { Wcs = binding, BarcodeFilter = new("^[0-9]+$"), TimeoutMs = 30000, Login = null };
        ExtensionStore.AtomicWrite(Path.Combine(folder, "business.json"), config);
        var launch = new BusinessLaunch(folder, Path.Combine(evidence, "chain-data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new HierarchyRcsFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); SmRoundFixture.Register(runtime, launch, config); await ManagementModeFixture.Authorize(runtime);
        var application = DispatchProxy.Create<IManagementParcelFixture, HierarchyCommandTransport>(); var transport = (HierarchyCommandTransport)(object)application; transport.Inner = runtime;
        var window = new SupplyStationWindow(application, null, "standard"); await using var owner = new NativeWindowOwner(window); window.Show(); var vm = window.ViewModel;
        async Task Until(Func<bool> ready)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(15))
            {
                await vm.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                if (ready()) return;
                await Task.Delay(20);
            }
            throw new TimeoutException("Actual current SM task did not converge");
        }
        async Task Observe(string side, string admission, string code)
        {
            var snapshot = await runtime.SnapshotAsync(); var lane = snapshot.Positions!.Single(p => p.Id == side);
            if (lane.CurrentRoundId is not null || lane.Arrival?.Submitted == true) await SmRoundFixture.NextVehicleAsync(runtime, side);
            Check(runtime.EmitReferencePresenceAt(side, TrayPresence.Present), "explicit synthetic presence: " + admission);
            Check(runtime.EmitReferenceAt(side, admission, code, "event-" + admission), "queued actual application input: " + admission);
            await Until(() => (side == "left" ? vm.Left : vm.Right).Input?.Observation.AdmissionId == admission);
        }
        try
        {
            await SetClient(window, 1366, 768); await Until(() => vm.Fresh && vm.Left.CanAdmit && vm.Right.CanAdmit);
            await Observe("left", "left-first", "000123"); await Until(() => vm.Left.ShowConfirmedTask);
            await Observe("right", "right-first", "000123"); await Until(() => vm.Right.ShowConfirmedTask);
            var left = vm.Left.Operation!; var right = vm.Right.Operation!;
            Check(left.Id != right.Id && left.Wcs!.Request.Id != right.Wcs!.Request.Id && vm.Left.Slot == "L-07" && vm.Right.Slot == "R-09", "same code has independent current rounds, original requests and actual returned destinations");
            foreach (var operation in new[] { left, right })
            {
                var exchange = peer.Exchanges.Single(item => item.Request == operation.Wcs!.Request.IntegrationRequest!.Body);
                Check(operation.Wcs!.Attempts == 1 && Encoding.UTF8.GetString(Convert.FromBase64String(operation.Wcs.Replies.Single().HttpResponse!.BodyBase64)) == exchange.Response,
                    "full frozen request and original raw HTTP bytes have the same operation owner: " + operation.PositionId);
            }
            await Observe("left", "left-invalid", "INVALID");
            Check(vm.Left.Operation is null && vm.Left.CurrentRound is { FormalCodes.Length: 0 } && vm.Right.Operation?.Id == right.Id && peer.Exchanges.Count == 2, "qualified new invalid parcel creates no request and cannot overwrite the other lane");
            await Observe("left", "left-pending", "000125"); await peer.PendingEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                await Observe("right", "right-next", "000124"); await Until(() => vm.Right.ShowConfirmedTask && vm.Right.Slot == "R-10");
                Check(vm.Left.IsProcessing && !vm.Left.ShowConfirmedTask && vm.Left.DisplaySlot == "处理中…", "the right lane completes while the left actual request is held; old left destination is hidden");
            }
            finally { peer.ReleasePending.TrySetResult(); }
            await Until(() => vm.Left.ShowConfirmedTask && vm.Left.Slot == "L-09");
            await Observe("left", "left-rejected", "000126"); await Until(() => vm.Left.Operation?.State == ParcelState.WcsRejected);
            Check(vm.Left.CenteredRcsRejection && !vm.Left.DisplaySlotVisible && vm.Right.Slot == "R-10", "confirmed RCS refusal belongs only to its current parcel and keeps the other result");
            await Observe("left", "left-unknown", "000127"); await Until(() => vm.Left.Operation?.Wcs?.State == StageState.Unknown);
            var unknown = vm.Left.Operation!; var frozen = Wire.Encode(unknown.Wcs); var before = peer.Exchanges.Count;
            await Observe("right", "right-after-unknown", "000124"); await Until(() => vm.Right.ShowConfirmedTask && vm.Right.Input?.Observation.AdmissionId == "right-after-unknown");
            Check(peer.Exchanges.Count == before + 1 && Wire.Encode((await runtime.OperationAsync(unknown.Id))!.Wcs) == frozen, "the other lane continues without replaying or rewriting the original Unknown");
            Check(runtime.EmitReferencePresenceAt("left", TrayPresence.Absent), "explicit trusted removal of the unknown parcel");
            await Until(() => vm.Left.CurrentRound is null);
            var state = await runtime.SnapshotAsync(); var lane = state.Positions!.Single(p => p.Id == "left");
            var command = new SubmitParcelBarcode(Wire.Id(), state.RuntimeId, state.Configuration.Revision, "left", "000128", ParcelBarcodeInputKind.Keyboard, null, null)
                { ExpectedRoundId = lane.CurrentRoundId, ExpectedArrivalGeneration = lane.Arrival!.Generation, ExpectedPlacementBoundary = lane.Arrival.PlacementBoundary };
            var refused = await runtime.SubmitParcelBarcodeAsync(command, default);
            Check(refused.Status == "Rejected" && peer.Exchanges.Count == before + 1 && lane.Arrival.TargetOperation == unknown.Id,
                "removal releases current ownership while the original unresolved target fence still refuses another left send");
            CaptureInternal(evidence, "current-independent-sm-chain", (Grid)window.FindName("ClientArea"), lines);
        }
        finally { peer.ReleasePending.TrySetResult(); }
    }

}

public sealed class HierarchyRcsFactory(BusinessLaunch launch) : IParcelCapabilityFactory
{
    readonly ParcelCapabilityFactory inner = new(launch);
    public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
    public void Validate(ParcelConfiguration c) => inner.Validate(c);
    public void ValidateStructure(ParcelConfiguration c) => inner.ValidateStructure(c);
    public CapabilityCatalog Catalog() => inner.Catalog();
    public CapabilityRequirements Requirements(CapabilityBinding b) => inner.Requirements(b);
    public ParcelPorts Create(ParcelConfiguration c)
    {
        // Match the existing native SyntheticFactory boundary: real Application
        // and RCS adapter, isolated manual inputs, no machine provisioning or
        // production device leases. The production factory still validates c.
        if (c.Mode != RunMode.Simulation || ParcelBindings.Readers(c).Any(r => r.Driver != "manual") ||
            c.Wcs is not { Implementation: "sportmaster-rcs", Package: null } ||
            !Uri.TryCreate(c.Wcs.Target, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback)
            throw new InvalidOperationException("Hierarchy fixture requires simulation/manual readers and loopback RCS");
        var readers = ParcelBindings.Readers(c).ToDictionary(r => r.DeviceId, r => (IControlledObservationSource)new ManualObservationSource(r.DeviceId));
        var rcs = new SportmasterRcsTaskService(c.Wcs); rcs.ConfigureRuntime(c);
        return new(readers[c.Reader.DeviceId], null, new InvalidIdentityReceipt(rcs), null,
            new Dictionary<string, string> { [rcs.InstanceId] = Wire.FileHash(typeof(SportmasterRcsTaskService).Assembly.Location) },
            Login: c.Login is null ? null : new ReferenceOperatorAuthenticator(c.Login), Readers: readers);
    }
    public ValueTask DisposeAsync() => inner.DisposeAsync();
    sealed class InvalidIdentityReceipt(IWcsPort inner) : IWcsPort, IWcsRequestPreparer, IWcsRuntimeConfiguration, IWcsHeartbeat, ISmHeartbeatControl
    {
        public string InstanceId => inner.InstanceId;
        public Task<RcsHeartbeatResult> SendHeartbeatAsync(CancellationToken token) => ((IWcsHeartbeat)inner).SendHeartbeatAsync(token);
        public void SetHeartbeatOwner(ISmHeartbeatOwner owner) => ((ISmHeartbeatControl)inner).SetHeartbeatOwner(owner);
        public void RefreshHeartbeat() => ((ISmHeartbeatControl)inner).RefreshHeartbeat();
        public void ConfigureRuntime(ParcelConfiguration c) => ((IWcsRuntimeConfiguration)inner).ConfigureRuntime(c);
        public IntegrationRequestIdRange? GetRequestIdRange(CapabilityBinding b) => ((IWcsRequestPreparer)inner).GetRequestIdRange(b);
        public PreparedIntegrationRequest PrepareRequest(FrozenRequest r, long sequence) => ((IWcsRequestPreparer)inner).PrepareRequest(r, sequence);
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => inner.QueryAsync(r, t);
        public async Task<StageReply> ResolveSlotAsync(FrozenRequest r, CancellationToken t)
        {
            var reply = await inner.ResolveSlotAsync(r, t);
            return Wire.Decode<ParcelPayload>(r.Payload).ParcelCode == "000129" ? reply with { RequestId = "synthetic-wrong-owner" } : reply;
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

public class HierarchyCommandTransport : ProjectionProbe
{
    public bool RejectPause, HoldPause, FailSnapshots;
    public readonly TaskCompletionSource PauseEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleasePause = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly ConcurrentQueue<object> Receipts = new();
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method!.Name == nameof(IParcelApplication.SnapshotAsync) && FailSnapshots) return Task.FromException<ParcelSnapshot>(new IOException("SYNTH_READ_FAULT"));
        if (method.Name == nameof(IBusinessApplication.CommandAsync)) return Command((BusinessCommand)arguments![0]!);
        return base.Invoke(method, arguments);
    }
    async Task<BusinessReceipt> Command(BusinessCommand command)
    {
        var result = command.Kind == "Pause" && RejectPause ? new BusinessReceipt("Rejected", "SYNTH_PAUSE_NOT_SENT", (await Inner.SnapshotAsync()).Revision) : await Inner.CommandAsync(command);
        Receipts.Enqueue(new { Command = command, Receipt = result, SyntheticRejection = command.Kind == "Pause" && RejectPause });
        if (command.Kind == "Pause" && HoldPause && result.Status != "Conflict") { PauseEntered.TrySetResult(); await ReleasePause.Task; }
        return result;
    }
}

sealed class HierarchyRcsPeer : IAsyncDisposable
{
    readonly HttpListener listener = new(); readonly CancellationTokenSource stop = new(); readonly Task loop;
    readonly ConcurrentBag<Task> pending = [];
    internal sealed record Exchange(string Request, string Response);
    public ConcurrentQueue<Exchange> Exchanges { get; } = new();
    public readonly TaskCompletionSource PendingEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleasePending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Endpoint { get; }
    public readonly ConcurrentDictionary<string, string> Vehicles = new();
    public HierarchyRcsPeer()
    {
        var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
        Endpoint = "http://127.0.0.1:" + port + "/rcs/sorter/api"; SmRoundFixture.VehiclePeers[Endpoint] = (camera, vehicle) => Vehicles[camera] = vehicle; listener.Prefixes.Add("http://127.0.0.1:" + port + "/"); listener.Start(); loop = Loop();
    }
    async Task Loop()
    {
        try { while (!stop.IsCancellationRequested) { var context = await listener.GetContextAsync().WaitAsync(stop.Token); pending.Add(Handle(context)); } }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (HttpListenerException) when (stop.IsCancellationRequested) { }
    }
    async Task Handle(HttpListenerContext context)
    {
        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8); var body = await reader.ReadToEndAsync();
        using var json = JsonDocument.Parse(body); var id = json.RootElement.GetProperty("requestId").GetInt64();
        var items = json.RootElement.GetProperty("data");
        if (items.GetArrayLength() == 0 || items[0].GetProperty("command").GetString() == "sorter.task.station_heartbeat")
        {
            var heartbeat = JsonSerializer.Serialize(new { requestId = id, result = items.EnumerateArray().Select(item => new
            {
                index = item.GetProperty("index").GetInt32(), success = true, command = "sorter.task.station_heartbeat",
                data = new { stationCode = item.GetProperty("params").GetProperty("stationCode").GetString(), bcrCode = item.GetProperty("params").GetProperty("bcrCode").GetString(), agvNo = Vehicles.GetValueOrDefault(item.GetProperty("params").GetProperty("bcrCode").GetString()!, "SYNTH-AGV-" + item.GetProperty("params").GetProperty("bcrCode").GetString()), status = "IDLE" }
            }).ToArray() });
            var payload = Encoding.UTF8.GetBytes(heartbeat); context.Response.ContentType = "application/json"; context.Response.ContentLength64 = payload.Length;
            try { await context.Response.OutputStream.WriteAsync(payload, stop.Token); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (IOException) { }
            catch (HttpListenerException) { }
            finally { context.Response.Close(); }
            return;
        }
        var parameter = json.RootElement.GetProperty("data")[0].GetProperty("params");
        var code = parameter.GetProperty("loadCode").GetString(); var left = parameter.GetProperty("bcrCode").GetString() == "TASK-LEFT";
        var data = (left, code) switch
        {
            (true, "000123") => "{\"chuteCode\":\"L-07\",\"waveNo\":\"0007\",\"taskId\":\"TASK-L1\",\"skuId\":\"SKU-L1\",\"quantity\":\"0002\"}",
            (true, "000124") => "{\"chuteCode\":\"L-08\"}",
            (true, "000125") => "{\"chuteCode\":\"L-09\"}",
            (false, "000123") => "{\"chuteCode\":\"R-09\"}",
            _ => "{\"chuteCode\":\"R-10\"}"
        };
        // Required task/SKU have literal scenario identities; measurements are
        // explicitly nullable. Only added waveNo/quantity may be absent.
        data = data[..^1] + ",\"length\":null,\"width\":null,\"height\":null,\"weight\":null" +
            (left && code == "000123" ? "}" : code == "000127" ? ",\"skuId\":\"SKU-MISSING-TASK\"}" : left ? ",\"taskId\":\"TASK-L2\",\"skuId\":\"SKU-L2\"}" : ",\"taskId\":\"TASK-R2\",\"skuId\":\"SKU-R2\"}");
        var response = "{\"requestId\":" + id + ",\"result\":[{\"index\":1,\"command\":\"sorter.task.dest_request\",\"args\":null,\"success\":" +
            (code == "000126" ? "false,\"code\":\"SYNTH_CHUTE_CLOSED\",\"message\":\"合成格口关闭\",\"data\":null" : "true,\"code\":null,\"message\":null,\"data\":" + data) + "}]}";
        Exchanges.Enqueue(new(body, response));
        try
        {
            if (code == "000125") { PendingEntered.TrySetResult(); await ReleasePending.Task.WaitAsync(stop.Token); }
            var bytes = Encoding.UTF8.GetBytes(response); context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { context.Response.Close(); }
    }
    public async ValueTask DisposeAsync() { SmRoundFixture.VehiclePeers.TryRemove(Endpoint, out _); stop.Cancel(); listener.Close(); await loop; await Task.WhenAll(pending); stop.Dispose(); }
}
