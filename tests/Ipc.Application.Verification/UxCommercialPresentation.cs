using System.Collections.Concurrent;
using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Assembly;
using Ipc.Platform;

static class UxCommercialPresentation
{
    static void Check(bool value, string reason)
    { if (!value) throw new Exception("FAIL UX-PRESENTATION: " + reason); Console.WriteLine("PASS UX-PRESENTATION: " + reason); }
    static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> predicate)
    {
        for (var i = 0; i < 200; i++) { var s = await app.SnapshotAsync(); if (predicate(s)) return s; await Task.Delay(20); }
        throw new TimeoutException("UX presentation reference chain timed out: " + Wire.Encode(await app.SnapshotAsync()));
    }
    public static async Task Run(string workspace, string root)
    {
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        var directory = Path.Combine(root, "ux-presentation"); Directory.CreateDirectory(directory);
        ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), config);
        var launch = new BusinessLaunch(directory, Path.Combine(directory, "data"), null, null);
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), new ParcelCapabilityFactory(launch));
        await app.StartAsync(CancellationToken.None);
        Check(app.EmitReferenceAt("left", "presentation-a", "00000123", "event-a"), "reference observation A enters left position");
        var first = await Wait(app, s => s.Operations.Any(x => x.AdmissionId == "presentation-a" && x.State == ParcelState.SlotResolved));
        var a = first.Operations.Single(x => x.AdmissionId == "presentation-a");
        Check(app.EmitReferenceAt("left", "presentation-b", "00000456", "event-b"), "independent reference observation B follows A");
        var baseline = await Wait(app, s => s.Operations.Any(x => x.AdmissionId == "presentation-b" && x.State == ParcelState.SlotResolved));
        var b = baseline.Operations.Single(x => x.AdmissionId == "presentation-b");
        var original = Wire.Encode(a.Wcs!.Request);
        var proxy = DispatchProxy.Create<IParcelApplication, UxPresentationProbe>(); var probe = (UxPresentationProbe)(object)proxy;
        var presented = baseline; probe.Read = () => Task.FromResult(presented);
        var ui = new SupplyStationViewModel(proxy); var classic = new ParcelViewModel(proxy, "test", "test");
        await ui.RefreshAsync(); await classic.RefreshAsync(); classic.SelectedPositionId = "left";
        Check(ui.Left.Operation?.Id == b.Id && ui.Left.Presentation.Foreground == ParcelPresentation.Accepted("").Foreground, "accepted current operation has explicit accepted semantics");
        var observed = baseline.Inputs!.Single(x => x.PositionId == "left").Observation;
        var noRead = new ParcelInputSnapshot("left", observed with { Id = "presentation-NoRead", Kind = "NoRead", Codes = [], ObservedAt = DateTimeOffset.UtcNow }, ParcelInputOutcome.Rejected, "Synthetic NoRead", null, DateTimeOffset.UtcNow);
        presented = baseline with { Inputs = [noRead], Positions = baseline.Positions!.Select(p => p.Id == "left" ? p with { CurrentOperation = null } : p).ToArray() };
        await ui.RefreshAsync(); await classic.RefreshAsync();
        Check(ui.Left.Operation is null && ui.Left.Status.Contains("识别失败") && ui.Left.Barcode.Contains("未识别") && ui.Left.Slot.Contains("未取得"), "NoRead displays latest failed observation with no previous successful slot");
        Check(classic.CurrentStage.Contains("识别失败") && classic.PositionRows.Single(x => x.Id == "left").Slot.Contains("未取得"), "classic and alternate use the same rejected input fact");
        var unknown = b with { State = ParcelState.WcsUnknown, Wcs = b.Wcs! with { State = StageState.Unknown, Replies = [] } };
        presented = presented with { Operations = baseline.Operations.Select(op => op.Id == b.Id ? unknown : op).ToArray(), Positions = baseline.Positions };
        await ui.RefreshAsync();
        Check(ui.Left.Operation?.Id == b.Id && ui.Left.OriginalResponsibility.Contains(b.Id) && ui.Left.Status.Contains("识别失败") && ui.Left.Slot.Contains("未取得"), "rejected new input preserves original unresolved command target without borrowing its result");
        var duplicate = noRead with { Observation = observed with { Id = "duplicate-A", Codes = [a.ParcelCode], ObservedAt = DateTimeOffset.UtcNow }, Outcome = ParcelInputOutcome.Duplicate, Reason = "Original entity already admitted", OperationId = a.Id };
        presented = baseline with { Inputs = [duplicate] }; await ui.RefreshAsync(); await classic.RefreshAsync();
        Check(ui.Left.Operation?.Id == b.Id && ui.Left.Barcode == a.ParcelCode && ui.Left.Status.Contains("重复") && ui.Left.LatestInputSummary.Contains(a.Id) && ui.Left.Slot.Contains("未取得"), "A duplicate while B is current names original A and does not relabel B green with A time");
        Check(ui.Left.ObservedAt.Contains(b.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz")) && classic.CurrentInputSummary.Contains(a.Id), "current operation observation time remains separate from latest duplicate source time in both layouts");
        presented = presented with { Positions = baseline.Positions!.Select(p => p.Id == "left" ? p with { CurrentOperation = null } : p).ToArray() }; await ui.RefreshAsync();
        Check(ui.Left.Operation is null && ui.Left.Status.Contains("重复") && ui.Left.Presentation.Foreground != ParcelPresentation.Accepted("").Foreground, "A to NoRead to A duplicate cannot silently restore green previous success");
        Check(Wire.Encode(a.Wcs!.Request) == original, "presentation never changes original request bytes or identity");
        var colors = new[] { ParcelPresentation.Neutral(""), ParcelPresentation.Pending(""), ParcelPresentation.Accepted(""), ParcelPresentation.Rejected(""), ParcelPresentation.Unknown(""), ParcelPresentation.Stale() };
        Check(colors.Select(x => x.Background).Distinct().Count() == 6 && colors.All(x => x.Icon.Length > 0), "normal, pending, accepted, rejected, unknown and stale have separate semantic backgrounds and text icons");
        Check(ParcelPresentation.Health(new("rcs", true, DateTimeOffset.UtcNow.AddMilliseconds(-20), DateTimeOffset.UtcNow.AddSeconds(2), "RCS client local lifecycle/configuration readiness only; upstream health endpoint is not supplied"), DateTimeOffset.UtcNow).Contains("客户端已启动，上游未确认"), "RCS lifecycle evidence never claims upstream readiness");
        await VerifyHistory(probe, baseline, a, b);
        await VerifyServiceContext(probe, baseline);
        await app.StopAsync(CancellationToken.None);
        Console.WriteLine("PASS UX-PRESENTATION COMPLETE; reference projection/service ownership only; Windows native NOT_RUN");
    }
    static async Task VerifyHistory(UxPresentationProbe probe, ParcelSnapshot baseline, ParcelOperation a, ParcelOperation b)
    {
        probe.Read = () => Task.FromResult(baseline);
        var vm = new ParcelViewModel((IParcelApplication)(object)probe, "test", "test"); await vm.RefreshAsync();
        var first = new TaskCompletionSource<ParcelHistoryPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<ParcelHistoryPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new SemaphoreSlim(0); var calls = 0;
        probe.History = _ => { var n = Interlocked.Increment(ref calls); entered.Release(); return n == 1 ? first.Task : second.Task; };
        var older = vm.LoadHistoryAsync(); await entered.WaitAsync(TimeSpan.FromSeconds(2));
        var newer = vm.LoadHistoryAsync(); await entered.WaitAsync(TimeSpan.FromSeconds(2));
        Check(vm.HistoryLoading && vm.HistoryStatus.Contains("读取"), "history exposes loading before service completion");
        second.SetResult(new([new(2, false, b)], null)); await newer; first.SetResult(new([new(1, false, a)], null)); await older;
        Check(vm.HistoryItems.Single().Operation.Id == b.Id && !vm.HistoryLoading, "late history result cannot replace newer page or busy state");
        vm.SelectedHistory = vm.HistoryItems.Single(); probe.History = _ => Task.FromResult(new ParcelHistoryPage([new(2, false, b)], null)); await vm.LoadHistoryAsync();
        Check(vm.Selected?.Id == b.Id && vm.SelectedHistory?.Operation.Id == b.Id, "history refresh retains original selection and detail identity");
        probe.History = _ => Task.FromException<ParcelHistoryPage>(new IOException("SYNTHETIC history read failure"));
        try { await vm.LoadHistoryAsync(); throw new Exception("Expected history read failure"); } catch (IOException) { }
        Check(vm.HistoryStatus.Contains("查询失败") && vm.HistoryItems.Single().Operation.Id == b.Id && !vm.HistoryLoading, "history read failure keeps prior page and is not misreported as empty");
        probe.History = _ => Task.FromResult(new ParcelHistoryPage([], null)); await vm.LoadHistoryAsync();
        Check(vm.HistoryStatus.Contains("没有作业记录") && !vm.HasNextHistory, "empty history has an explicit completed empty state");
        vm.Selected = a;
        Check(vm.StructuredDetails.Contains(a.Id) && vm.StructuredDetails.Contains(a.Wcs!.Request.Id) && vm.StructuredDetails.Contains("发送尝试") && vm.Details.Contains(a.Wcs!.Request.PayloadHash), "structured details preserve operation/request/attempt/reply distinctions and full original JSON");
        var reconciliation = new TaskCompletionSource<BusinessReceipt>(TaskCreationOptions.RunContinuationsAsynchronously); var commandEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.Reconcile = _ => { commandEntered.SetResult(); return reconciliation.Task; };
        var selectedRead = vm.QuerySelectedAsync(); await commandEntered.Task; vm.Selected = b;
        reconciliation.SetResult(new("Unknown", "Synthetic query", 1)); await selectedRead;
        Check(vm.Selected?.Id == b.Id, "late original-operation query cannot replace a newer selected operation");
    }
    static Task VerifyServiceContext(UxPresentationProbe probe, ParcelSnapshot snapshot) => Task.Run(() =>
    {
        using var context = new PresentationContext(); SynchronizationContext.SetSynchronizationContext(context);
        var uiThread = Environment.CurrentManagedThreadId;
        var work = Run(); context.Pump(work); work.GetAwaiter().GetResult();
        async Task Run()
        {
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); var serviceThread = 0; var changedOnUi = true;
            probe.Read = () => { serviceThread = Environment.CurrentManagedThreadId; entered.Set(); release.Wait(); return Task.FromResult(snapshot); };
            var vm = new ParcelViewModel((IParcelApplication)(object)probe, "test", "test");
            vm.PropertyChanged += (_, _) => changedOnUi &= Environment.CurrentManagedThreadId == uiThread;
            var refresh = vm.RefreshAsync();
            Check(entered.Wait(TimeSpan.FromSeconds(2)) && !refresh.IsCompleted && serviceThread != uiThread, "synchronously blocking snapshot service returns control without occupying the UI caller thread");
            var drain = vm.DrainAsync(); Check(!drain.IsCompleted, "close drain retains synchronous service ownership until actual completion");
            release.Set(); await refresh; await drain;
            Check(changedOnUi && vm.Fresh, "snapshot collection/property projection resumes on captured UI context");
            probe.Read = () => Task.FromResult(snapshot);
            var command = new TaskCompletionSource<BusinessReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
            var action = vm.RunAsync(async () => { await vm.ServiceAsync(() => command.Task); });
            var workflowDrain = vm.DrainAsync(); Check(vm.Busy && !workflowDrain.IsCompleted, "whole action and background service remain owned during close drain");
            command.SetResult(new("Persisted", "Synthetic", 1)); await action; await workflowDrain;
            Check(!vm.Busy && changedOnUi, "action completion restores Busy and projects on original context");
        }
    });
    sealed class PresentationContext : SynchronizationContext, IDisposable
    {
        readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = [];
        public override void Post(SendOrPostCallback d, object? state) => queue.Add((d, state));
        public void Pump(Task task) { while (!task.IsCompleted) if (queue.TryTake(out var item, 100)) item.Callback(item.State); }
        public void Dispose() => queue.Dispose();
    }
}

public class UxPresentationProbe : DispatchProxy
{
    public Func<Task<ParcelSnapshot>> Read = null!;
    public Func<long, Task<ParcelHistoryPage>> History = _ => Task.FromResult(new ParcelHistoryPage([], null));
    public Func<string, Task<BusinessReceipt>> Reconcile = _ => throw new InvalidOperationException("Unexpected reconcile");
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
    {
        "SnapshotAsync" => Read(),
        "HistoryAsync" => History((long)args![0]!),
        "OperationAsync" => ReadOperation((string)args![0]!),
        "ReconcileAsync" => Reconcile((string)args![0]!),
        _ => throw new InvalidOperationException("Unexpected presentation service " + method.Name)
    };
    async Task<ParcelOperation?> ReadOperation(string id) => (await Read()).Operations.SingleOrDefault(x => x.Id == id);
}
