using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;

static partial class Program
{
    static async Task VerifyManagementCloseReadRace(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS CLOSE-READ " + message); }
        async Task Until(Func<bool> ready)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!ready()) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(10, deadline.Token); }
        }
        await using var h = new ManualSupplyHarness(root, Path.Combine(evidence, "close-read"), deferred: true, rightCode: false, leftCode: false);
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        var api = DispatchProxy.Create<IManualDebugUiFixture, ClosingReadBoundary>();
        var probe = (ClosingReadBoundary)(object)api; probe.Inner = h.App;
        var window = new SupplyStationWindow(api, null, "standard"); window.Show();
        try
        {
            await Until(() => window.ViewModel.Fresh && window.ViewModel.IsManagementMode);
            window.ShowPositionDebug(window.ViewModel.Left);
            await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var center = System.Windows.Application.Current.Windows.OfType<ManagementCenterWindow>().Single(c => c.IsVisible && ReferenceEquals(c.Owner, window));
            center.Close(); await probe.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // The cancelled read remains an outstanding owned call until this
            // boundary releases it. Allow the actual close handler to enter drain.
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(center.IsVisible && !center.IsEnabled, "closing waits for the actual cancelled read to settle");
            probe.Release.TrySetResult();
            await Until(() => !center.IsVisible || center.IsEnabled);
            Check(!center.IsVisible && window.IsEnabled, "the original close completes after cancellation; " + ((TextBlock)center.FindName("AuthorizationStatus")).ToolTip);
            Check((await h.App.SnapshotAsync()).Positions!.All(p => p.Admit) && h.Peer.Requests.IsEmpty,
                "a settled owned read cancellation permits original workspace closure and legal manual admission without HTTP");
            // A real service failure must still be observed by both the caller
            // and the barrier; the cancellation fix cannot mask arbitrary faults.
            var failed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var call = window.ViewModel.ServiceAsync(() => failed.Task);
            var drain = window.ViewModel.DrainAsync(); failed.SetException(new IOException("Synthetic actual service failure"));
            bool callerFailed = false, drainFailed = false;
            try { await call; } catch (IOException) { callerFailed = true; }
            try { await drain; } catch (IOException) { drainFailed = true; }
            Check(callerFailed && drainFailed, "drain preserves real service errors and their original caller ownership");
            await window.ViewModel.DrainAsync();
            Check(await window.ViewModel.ServiceAsync(() => Task.FromResult(7)) == 7, "a settled failure leaves the next legitimate service operation usable");
        }
        finally { probe.Release.TrySetResult(); window.Close(); }
    }
}

public class ClosingReadBoundary : ProjectionProbe
{
    public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource CancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override object? Invoke(MethodInfo? method, object?[]? args)
        => method!.Name == nameof(ICameraInspection.CameraInspectionStatusAsync) ? Read((CancellationToken)args![0]!) : base.Invoke(method, args);
    async Task<CameraInspectionStatus> Read(CancellationToken token)
    {
        Entered.TrySetResult();
        try { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException("Unreachable synthetic read"); }
        catch (OperationCanceledException) { CancellationObserved.TrySetResult(); await Release.Task; throw; }
    }
}
