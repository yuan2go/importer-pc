using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyDebugBatchActions(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS DEBUG-BATCH " + text); }
        async Task Until(Func<bool> ready)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!ready()) { deadline.Token.ThrowIfCancellationRequested(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(10, deadline.Token); }
        }
        await using var harness = new CameraRoundHarness(root, Path.Combine(evidence, "batch-data"));
        await harness.StartAsync(); var runtime = harness.App;
        var initial = await runtime.SnapshotAsync();
        await runtime.CommandAsync(new(Wire.Id(), initial.Revision, "Pause"));
        var api = DispatchProxy.Create<IManualDebugUiFixture, DebugBatchBoundary>();
        var probe = (DebugBatchBoundary)(object)api; probe.Inner = runtime;
        var vm = new SupplyStationViewModel(api); await vm.RefreshAsync();
        var type = typeof(SupplyStationWindow).Assembly.GetType("Ipc.Application.Sportmaster.Desktop.StationDebugControl", true)!;
        var control = (UserControl)Activator.CreateInstance(type, [vm, (Func<string, Task>)(_ => Task.CompletedTask), (Func<string, Task>)(_ => Task.CompletedTask)])!;
        var window = new Window { Content = control, Width = 960, Height = 600, Title = "连续试扫生命周期 · 合成边界" }; window.Show();
        object? Field(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control);
        Button Button(string id) => Descendants(control).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == id);
        void Click(string id) => Button(id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        try
        {
            await Until(() => Button("StartCameraSampleBatch").IsEnabled);
            probe.HoldEnd = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Click("StartCameraSampleBatch"); await Until(() => probe.ReadEntered.Task.IsCompleted || Field("busy") is false);
            Check(probe.ReadEntered.Task.IsCompleted, "sampling reaches frame delivery; " + ((TextBlock)Field("batchSummary")!).Text + " / " + ((TextBlock)Field("feedback")!).Text);
            Check(probe.BeginCommands.Single() is { SingleReadRounds: false } begin && begin.DeviceId == vm.Left.ReaderId,
                "native selected-camera action freezes its actual device identity into the Application command");
            Check(Field("busy") is true && Button("StopCameraSampleBatch").IsEnabled, "stop is executable during the actual busy sampling wait");
            Click("StopCameraSampleBatch"); await Until(() => probe.EndCommands.Count == 1);
            Check(Field("batch") is not null && !Button("StopCameraSampleBatch").IsEnabled && !Button("StartCameraSampleBatch").IsEnabled,
                "stop waits for real Application closure and blocks duplicate starts and stops");
            Button("StopCameraSampleBatch").BringIntoView(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            CaptureInternal(evidence, "debug-batch-stopping", window, lines);
            probe.HoldEnd.SetResult(); await Until(() => Field("batch") is null && Field("busy") is false);
            Check((await runtime.CameraInspectionStatusAsync(default)).State == "Closed", "stopping closes the original durable inspection session");
            probe.Reset(); probe.FailEndOnce = true;
            Click("StartCameraSampleBatch"); await probe.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Click("StopCameraSampleBatch");
            await Until(() => Field("busy") is false && Button("StopCameraSampleBatch").IsEnabled);
            Check(Field("batch") is not null && !Button("StartCameraSampleBatch").IsEnabled, "release failure retains original identity, samples and an explicit retry");
            Button("StopCameraSampleBatch").BringIntoView(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            CaptureInternal(evidence, "debug-batch-release-failed", window, lines);
            var original = probe.EndCommands.Single(); Click("StopCameraSampleBatch"); await Until(() => Field("batch") is null);
            Check(probe.EndCommands.Count == 2 && probe.EndCommands.All(c => c == original), "retry reconciles the same End command and session without another Begin");
            probe.Reset(); probe.CompleteFrames = true;
            Click("StartCameraSampleBatch"); await Until(() => probe.EndCommands.Count == 1 && Field("batch") is null && Field("busy") is false);
            Check((await runtime.CameraInspectionStatusAsync(default)).State == "Closed", "natural twenty-frame completion uses the same actual session cleanup");
            probe.Reset(); Click("StartCameraSampleBatch"); await probe.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            window.Content = null;
            await Until(() => Field("batch") is null && Field("busy") is false);
            window.Content = control; await Until(() => Button("StartCameraSampleBatch").IsEnabled);
            Check((await runtime.CameraInspectionStatusAsync(default)).State == "Closed" && !(await runtime.SnapshotAsync()).Operations.Any(),
                "departure closes only the owned sample, reloading permits another legal operation and creates no RCS requests");
            window.Width = 800; window.Height = 420; window.UpdateLayout(); Button("StartCameraSampleBatch").BringIntoView();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); CaptureInternal(evidence, "debug-batch-ended-800", window, lines);
        }
        finally { probe.HoldEnd?.TrySetResult(); window.Close(); }
    }
}

public class DebugBatchBoundary : ProjectionProbe
{
    public TaskCompletionSource ReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource? HoldEnd;
    public bool FailEndOnce, CompleteFrames;
    public readonly List<EndCameraInspection> EndCommands = [];
    public readonly List<BeginCameraInspection> BeginCommands = [];
    public void Reset() { HoldEnd = null; EndCommands.Clear(); ReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously); CompleteFrames = false; }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == nameof(ICameraInspection.BeginCameraInspectionAsync)) BeginCommands.Add((BeginCameraInspection)args![0]!);
        if (method!.Name == nameof(ICameraInspection.ReadCameraFramesAsync)) return Frames((string)args![0]!, (string)args[1]!, (CancellationToken)args[5]!);
        if (method.Name == nameof(ICameraInspection.EndCameraInspectionAsync)) return End((EndCameraInspection)args![0]!);
        return base.Invoke(method, args);
    }
    async Task<CameraFramePage> Frames(string session, string reader, CancellationToken token)
    {
        ReadEntered.TrySetResult();
        if (!CompleteFrames) await Task.Delay(Timeout.Infinite, token);
        return new(Enumerable.Range(1, 20).Select(n => new CameraFrameMetadata(Wire.Id(), session, reader, "synthetic", n, DateTimeOffset.UtcNow, 8, 8, [], 0, null, null, null, "", false, "NotCaptured")).ToArray(), 20, 20, false);
    }
    async Task<CameraInspectionStatus> End(EndCameraInspection command)
    {
        EndCommands.Add(command);
        if (HoldEnd is { } hold) await hold.Task;
        if (FailEndOnce) { FailEndOnce = false; throw new IOException("Synthetic inspection release failure"); }
        return await ((ICameraInspection)Inner).EndCameraInspectionAsync(command, default);
    }
}
