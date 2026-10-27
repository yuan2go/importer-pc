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
        var configRoot = Path.Combine(evidence, "chain-config"); Directory.CreateDirectory(configRoot);
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json")));
        var binding = new CapabilityBinding("integration", "sportmaster-rcs", "1.0.0", "sportmaster.integration", peer.Endpoint, "SPORTMASTER-RCS-SORTER-v1", new()
        {
            ["source"] = "SYNTHETIC-HIERARCHY", ["locale"] = "zh_CN", ["zone-id"] = "71", ["project-id"] = "83",
            ["specified-chute"] = "false", ["request-id-base"] = "810000000", ["request-id-count"] = "10000", ["heartbeat-interval-seconds"] = "3600",
            ["left-station-code"] = "TASK-STATION", ["right-station-code"] = "TASK-STATION", ["left-bcr-code"] = "TASK-LEFT", ["right-bcr-code"] = "TASK-RIGHT"
        });
        var config = seed with { Wcs = binding, BarcodeFilter = new("^[0-9]+$"), TimeoutMs = 10000,
            Login = new CapabilityBinding("login", "reference-login", "1.0.0", "operator-login", "reference://operator", "PARCEL-REFERENCE-v1", new()) with { InstanceId = "synthetic.operator", Target = "reference://operator", Parameters = new() { ["subjects"] = "alice" } } };
        ExtensionStore.AtomicWrite(Path.Combine(configRoot, "business.json"), config);
        var launch = new BusinessLaunch(configRoot, Path.Combine(evidence, "chain-data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new HierarchyRcsFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize(runtime);
        var app = DispatchProxy.Create<IManagementParcelFixture, HierarchyCommandTransport>();
        var transport = (HierarchyCommandTransport)(object)app; transport.Inner = runtime;
        var window = (SupplyStationWindow)Presentation.Create(app, null, "standard", "supply-station");
        window.ConfirmDiscardDrafts = _ => true; window.Title = "HIERARCHY-02 · 合成任务链复核";
        window.Show(); var vm = window.ViewModel;
        await SetClient(window, 1366, 768); await vm.RefreshAsync();
        vm.OperatorSubject = "alice"; await vm.LoginAsync("SIMULATION_ONLY");
        var client = (Grid)window.FindName("ClientArea");
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        // Publish only synthetic task facts. Do not export account configuration,
        // session identities, the runtime database or unrelated machine data.
        async Task<object> EvidenceSnapshot()
        {
            var s = await runtime.SnapshotAsync();
            return new { s.RuntimeId, s.Revision, s.State, s.Paused, s.BlockingReasons, s.Positions, s.Inputs,
                Operations = s.Operations.Select(o => new { o.Id, o.AdmissionId, o.ObservationId, o.ObservedAt, o.ParcelCode, o.PositionId,
                    o.Acquisition, o.ReaderBinding, o.State, o.Slot, o.Reason, Wcs = o.Wcs is not { } w ? null : new {
                        w.State, w.Attempts, w.Failure, w.Replies, Request = new { w.Request.Id, w.Request.ApplicationId, w.Request.OperationId,
                            w.Request.AdmissionId, w.Request.Stage, w.Request.Binding, w.Request.ImplementationHash, w.Request.ConfigurationHash,
                            w.Request.PayloadHash, w.Request.PreparedAt, w.Request.IntegrationRequest } } }).ToArray() };
        }
        async Task Until(Func<bool> ready)
        {
            for (var i = 0; i < 200; i++) { await vm.RefreshAsync(); await Flush(); if (ready()) return; await Task.Delay(25); }
            ExtensionStore.AtomicWrite(Path.Combine(evidence, "chain-timeout.json"), new { Snapshot = await EvidenceSnapshot(), Exchanges = peer.Exchanges.ToArray() });
            throw new TimeoutException("Real Application did not reach the expected task fact");
        }
        async Task Observe(string side, string entity, string code)
        {
            Check(runtime.EmitReferenceAt(side, entity, code, "event-" + entity), "observation queued: " + side + "/" + entity);
            await Until(() => (side == "left" ? vm.Left : vm.Right).Input?.Observation.AdmissionId == entity);
        }
        async Task Record(string name)
        {
            await Flush();
            ExtensionStore.AtomicWrite(Path.Combine(evidence, "chain-" + name + ".json"), new
            {
                Scenario = name, Synthetic = true, Snapshot = await EvidenceSnapshot(), Exchanges = peer.Exchanges.ToArray(), Commands = transport.Receipts.ToArray(),
                Display = new { Left = new { vm.Left.Barcode, vm.Left.Slot, vm.Left.Status, vm.Left.AttentionReason, vm.Left.NextStep, vm.Left.WaveNo, vm.Left.TaskId, vm.Left.SkuId, vm.Left.ResponseQuantity, vm.Left.CanAdmit, vm.Left.CanContinue },
                    Right = new { vm.Right.Barcode, vm.Right.Slot, vm.Right.Status, vm.Right.CanAdmit }, vm.GlobalOperationFeedback }
            });
            CaptureInternal(evidence, "chain-" + name, client, lines);
        }
        SupplyStationPosition Position(string side) => Descendants(window).OfType<SupplyStationPosition>().Single(c => ReferenceEquals(c.DataContext, side == "left" ? vm.Left : vm.Right));
        TextBox Slot(string side) => (TextBox)Position(side).FindName("CurrentSlot");
        async Task NativeResult(string name, string status, string code, bool destination)
        {
            await Flush(); var left = Position("left"); var body = (ScrollViewer)left.FindName("PositionBody"); body.ScrollToTop(); await Flush();
            var barcode = (TextBox)left.FindName("CurrentBarcode");
            await Record(name);
            Check(vm.Left.Status == status && barcode.Text == code && Slot("left").IsVisible == destination, name + " result and barcode agree with independent scenario");
            bool Fits(FrameworkElement element) { var bounds = element.TransformToAncestor(body).TransformBounds(new Rect(element.RenderSize)); return bounds.Top >= 0 && bounds.Bottom <= body.ActualHeight && bounds.Left >= 0 && bounds.Right <= body.ActualWidth; }
            Check(Fits(barcode) && Inside((TextBlock)left.FindName("NextStep"), client), name + " barcode fits the position viewport and next action stays in the fixed footer");
            Check(Inside((Border)left.FindName("FixedActions"), client), name + " original detail action remains reachable");
        }

        await Observe("left", "entity-l1", "000123"); await Until(() => vm.Left.Slot == "L-07");
        await Observe("right", "entity-r1", "000123"); await Until(() => vm.Right.Slot == "R-09");
        Check(vm.Left.OperationId != vm.Right.OperationId && vm.Left.RequestId != vm.Right.RequestId && vm.Left.Admission != vm.Right.Admission, "same barcode has independent entities, operations and frozen requests");
        Check(vm.Left.WaveNo == "0007" && vm.Left.TaskId == "TASK-L1" && vm.Left.SkuId == "SKU-L1" && vm.Left.ResponseQuantity == "0002", "raw response fields retain leading zeros; response quantity is not request quantity 1");
        var original = vm.Left.Operation!; var rightOriginal = vm.Right.Operation!;
        foreach (var op in new[] { original, rightOriginal })
        {
            var request = op.Wcs!.Request; var exchange = peer.Exchanges.Single(e => e.Request == request.IntegrationRequest!.Body);
            Check(request.OperationId == op.Id && request.AdmissionId == op.AdmissionId && request.PayloadHash == Wire.Hash(request.Payload) &&
                request.IntegrationRequest!.BodyHash == Wire.Hash(exchange.Request) &&
                Encoding.UTF8.GetString(Convert.FromBase64String(op.Wcs.Replies.Single().HttpResponse!.BodyBase64)) == exchange.Response &&
                op.Wcs.Replies.Single().PhysicalCompleted != true && op.Wcs.Attempts == 1,
                op.PositionId + " acquisition/operation/frozen wire/raw HTTP receipt have one owner and one attempt, no physical-completion claim");
        }
        await Record("same-code-normal-1366"); await SetClient(window, 800, 420);
        var posts = peer.Exchanges.Count;
        await Observe("left", "entity-l-invalid", "INVALID");
        await NativeResult("invalid-after-slot-800", "条码筛选拒绝 · 没有匹配码", "INVALID", false);
        Check(vm.Left.Input!.OperationId is null && vm.Left.Operation is null && vm.Left.InputFailed && !vm.Left.CurrentTaskAvailable &&
            peer.Exchanges.Count == posts && vm.Right.OperationId == rightOriginal.Id && vm.Right.Slot == "R-09", "invalid new entity creates no operation/POST, cannot borrow old destination, right remains unchanged");
        Check(Wire.Encode((await runtime.SnapshotAsync()).Operations.Single(o => o.Id == original.Id).Wcs!.Request) == Wire.Encode(original.Wcs!.Request) && !vm.Left.CanReconcile,
            "completed original request remains in history, not in the rejected new input's disabled detail action");
        Check(vm.Left.NextStep.Contains("作业记录") && !vm.Left.NextStep.Contains("请查看详情") && !vm.Left.AttentionReason.Contains("NoMatchingCode"),
            "rejected input directs the operator to reachable history and explains filtering in Chinese");

        await Observe("left", "entity-l2", "000124"); await Until(() => vm.Left.Slot == "L-08");
        Check(vm.Left.WaveNo == "回执未包含" && vm.Left.TaskId == "TASK-L2" && vm.Left.SkuId == "SKU-L2" && vm.Left.ResponseQuantity == "回执未包含", "missing optional wave/quantity never inherit L1 or request quantity; required task/SKU remain current");
        await Record("missing-fields-800");
        await Observe("left", "entity-l3", "000125"); await peer.PendingEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Observe("right", "entity-r2", "000124"); await Until(() => vm.Right.Slot == "R-10");
        await NativeResult("pending-left-right-success-800", "正在取格口", "000125", false);
        Check(!vm.Left.CanAdmit && !vm.Left.CanContinue && vm.Right.CanAdmit && vm.Right.Slot == "R-10", "pending left does not block right and exposes no resend/continuation");
        peer.ReleasePending.TrySetResult(); await Until(() => vm.Left.Slot == "L-09");
        await Observe("left", "entity-l4", "000126"); await Until(() => vm.Left.Operation?.State == ParcelState.WcsRejected);
        await NativeResult("rejected-800", "业务拒绝", "000126", false);
        Check(vm.Left.AttentionReason == "合成格口关闭" && vm.Left.Operation!.Wcs!.Replies.Last().Destination?.Code == "SYNTH_CHUTE_CLOSED" && vm.Right.Slot == "R-10", "explicit business rejection displays the original message, retains its code and preserves the healthy right result");
        await Observe("left", "entity-l5", "000127"); await Until(() => vm.Left.Operation?.State == ParcelState.WcsUnknown);
        await NativeResult("unknown-800", "结果待核对", "000127", false);
        var unknown = vm.Left.Operation!; posts = peer.Exchanges.Count;
        Check(vm.Left.AttentionReason == "RCS未返回有效格口，请核对异常信息。", "unknown explains the invalid RCS destination in the primary result");
        Check(unknown.Wcs!.State == StageState.Unknown && !vm.Left.CanAdmit && !vm.Left.CanContinue && vm.Right.CanAdmit, "response missing required taskId leaves unknown custody on left, not a usable partial result or global pause");
        // Reconciliation uses the existing Application guard. Query=false must not POST.
        await runtime.ReconcileAsync(unknown.Id); await vm.RefreshAsync();
        Check(peer.Exchanges.Count == posts && vm.Left.Operation!.Wcs!.Attempts == 1 && vm.Left.Operation.Wcs.State == StageState.Unknown, "unsupported RCS reconciliation cannot resend or clear unknown");
        await Observe("left", "entity-l6-blocked", "000128");
        await NativeResult("blocked-after-unknown-800", "本次输入未准入", "000128", false);
        Check(vm.Left.OperationId == unknown.Id && vm.Left.Input!.OperationId is null && peer.Exchanges.Count == posts, "valid new barcode blocked by unknown retains original request owner");
        Check(vm.Left.AttentionReason.Contains("当前：原作业结果待核对"), "blocked new input separates its admission outcome from current unresolved custody");

        // Invalid receipt identity is injected at the capability boundary, not in a snapshot.
        // This explicitly synthetic port case is NOT a claim about an RCS query capability.
        await Observe("right", "entity-r-conflict", "000129"); await Until(() => vm.Right.Operation?.Wcs?.State == StageState.Conflict);
        Check(vm.Right.Operation!.State == ParcelState.Blocked && vm.Right.Status == "回执冲突 · 待核对" && !vm.Right.CurrentTaskAvailable && !vm.Right.CanAdmit && !vm.Right.CanContinue, "real Application rejects invalid original-stage identity as Conflict, with no destination or repeat permission");
        Check(vm.Right.AttentionReason.Contains("身份不符或存在矛盾"), "conflict explains incompatible receipt evidence, distinct from missing acceptance");
        await Record("conflict-right-800");

        // A normal, fresh operation snapshot cannot prove that a global command succeeded.
        Click(window, "日志查询"); await Flush();
        transport.RejectPause = true; await vm.PauseAsync(); await Flush();
        Check(!vm.Snapshot!.Paused && vm.Fresh && vm.GlobalOperationFeedback.Contains("SYNTH_PAUSE_NOT_SENT"), "pause transport rejection remains visible alongside a normal fresh unpaused snapshot");
        await Record("pause-rejected-logs-800");
        transport.RejectPause = false;
        transport.HoldPause = true; var pendingPause = vm.PauseAsync();
        await transport.PauseEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Click(window, "作业记录"); await Flush();
        transport.ReleasePause.TrySetResult(); await pendingPause; await Flush();
        // The committed receipt and the UI projection are separate facts. A newer
        // timer/history refresh may supersede Pause's own read; await its actual projection.
        await Until(() => vm.Snapshot!.Paused);
        Check(vm.Snapshot!.Paused && vm.GlobalOperationFeedback.Contains("暂停供包") && vm.HasGlobalOperationFeedback, "real persisted pause receipt arrives after navigation and remains visible on the receiving page; paused=" + vm.Snapshot.Paused + "; fresh=" + vm.Fresh + "; feedback=" + vm.GlobalOperationFeedback + "; receipt=" + Wire.Encode(transport.Receipts.Last()));
        await Record("late-pause-history-800");
        var history = Descendants(window).OfType<HistoryPage>().Single();
        var rows = (DataGrid)history.FindName("HistoryRows");
        var firstRow = Descendants(rows).OfType<DataGridRow>().OrderBy(r => r.GetIndex()).First();
        var scaleY = System.Windows.Media.VisualTreeHelper.GetDpi(rows).DpiScaleY;
        var rowBottomPixels = Math.Round((firstRow.TranslatePoint(new Point(), rows).Y + firstRow.ActualHeight) * scaleY);
        var viewportPixels = Math.Round(rows.ActualHeight * scaleY);
        Check(Inside(rows, client) && firstRow.ActualHeight >= 32 &&
            rowBottomPixels <= viewportPixels,
            $"minimum history viewport retains a complete result row with late pause receipt and unresolved custody visible: grid={rows.ActualHeight:F2}, rowTop={firstRow.TranslatePoint(new Point(), rows).Y:F2}, rowHeight={firstRow.ActualHeight:F2}, inside={Inside(rows, client)}");
        transport.HoldPause = false; Click(window, "日志查询"); await Flush();
        // Keep unresolved old receipt out of a new global intent; real Resume is rejected
        // because both positions now carry unsettled custody.
        await vm.RunAsync(() => vm.CommandAsync("Resume"), "恢复供包"); await Flush();
        Check(vm.Snapshot!.Paused && vm.GlobalOperationFeedback.Contains("恢复供包") && vm.GlobalOperationFeedback.Contains("当前执行条件未满足") && !vm.GlobalOperationFeedback.Contains("SYNTH_PAUSE_NOT_SENT"), "latest resume intent owns its real Application rejection and pause remains in force");
        await Record("resume-guard-logs-800");

        transport.FailSnapshots = true;
        try { await vm.RefreshAsync(); } catch (IOException error) when (error.Message == "SYNTH_READ_FAULT") { }
        Click(window, "工作台"); await Flush();
        Check(!vm.Fresh && !Slot("left").IsVisible && !Slot("right").IsVisible && !vm.Left.CanAdmit && !vm.Right.CanAdmit, "read failure invalidates both actionable results without altering application custody");
        await Record("stale-800"); transport.FailSnapshots = false; await Until(() => vm.Fresh);
        if (interactive)
        {
            await SetClient(window, 1092, 614); Click(window, "日志查询"); await Flush();
            File.WriteAllText(Path.Combine(root, ".artifacts/HIERARCHY-INTERACTIVE.txt"), evidence);
            lines.Add("INTERACTIVE ready: synthetic isolated runtime; close this window after native keyboard/mouse review.");
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult(); await closed.Task;
        }
        else { window.Close(); for (var i = 0; i < 100 && window.IsVisible; i++) await Task.Delay(20); }
        Check(peer.Exchanges.Count == posts + 1, "navigation, pause/resume and stale recovery send no additional destination requests");
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
    sealed class InvalidIdentityReceipt(IWcsPort inner) : IWcsPort, IWcsRequestPreparer, IWcsRuntimeConfiguration
    {
        public string InstanceId => inner.InstanceId;
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
    public HierarchyRcsPeer()
    {
        var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
        Endpoint = "http://127.0.0.1:" + port + "/rcs/sorter/api"; listener.Prefixes.Add("http://127.0.0.1:" + port + "/"); listener.Start(); loop = Loop();
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
    public async ValueTask DisposeAsync() { stop.Cancel(); listener.Close(); await loop; await Task.WhenAll(pending); stop.Dispose(); }
}
