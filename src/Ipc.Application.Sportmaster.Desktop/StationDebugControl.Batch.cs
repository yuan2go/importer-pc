using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.Sportmaster.Desktop;

internal sealed partial class StationDebugControl
{
    sealed class SampleBatch(BeginCameraInspection begin, string reader, CancellationToken owner)
    {
        public readonly BeginCameraInspection Begin = begin;
        public readonly string Reader = reader;
        public readonly CancellationTokenSource Wait = CancellationTokenSource.CreateLinkedTokenSource(owner);
        public readonly List<CameraFrameMetadata> Frames = [];
        public readonly TaskCompletionSource Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Session;
        public EndCameraInspection? End;
        public Task? Finishing;
        public bool BeginUnconfirmed, StopRequested, TimedOut;
        public string? Failure;
    }
    SampleBatch? batch;
    Button batchStart = null!, batchStop = null!;
    TextBlock batchSummary = null!;
    bool batchStopping;
    void InitializeBatch(Panel body)
    {
        var actions = new WrapPanel(); batchStart = Action("采样 20 帧"); batchStop = Action("停止试扫");
        AutomationProperties.SetAutomationId(batchStart, "StartCameraSampleBatch");
        AutomationProperties.SetAutomationId(batchStop, "StopCameraSampleBatch");
        batchSummary = Text("连续试扫只采集本窗选择相机的样本，不计入生产，不额外发送RCS请求。");
        AutomationProperties.SetLiveSetting(batchSummary, AutomationLiveSetting.Polite);
        actions.Children.Add(batchStart); actions.Children.Add(batchStop); body.Children.Add(actions); body.Children.Add(batchSummary);
        batchStart.Click += async (_, _) => await Run(StartBatchAsync);
        batchStop.Click += async (_, _) =>
        {
            try { await StopBatchAsync(); }
            catch (Exception error) { vm.Message = Ipc.Application.ParcelFlow.Desktop.ChineseInterfaceText.ParameterMessage(error.Message, "试扫结束尚未确认，请核对原会话后重试。"); }
        };
        UpdateBatchButtons();
    }
    void UpdateBatchButtons()
    {
        if (batchStart is null) return;
        batchStart.IsEnabled = active && !busy && batch is null && vm.IsManagementMode;
        batchStop.IsEnabled = batch is not null && !batchStopping;
        batchStop.Content = batchStopping ? "正在结束试扫…" : batch?.Failure is not null ? "核对并结束原试扫" : "停止试扫";
    }
    async Task StartBatchAsync(CancellationToken token)
    {
        if (batch is not null) throw new InvalidOperationException("原试扫尚未确认结束，请先核对并结束。");
        if (ownedScanSession is { } old) await CloseScanScopeAsync(old, ownedScanRound);
        var snapshot = await vm.ServiceAsync(() => vm.Application.SnapshotAsync());
        token.ThrowIfCancellationRequested();
        var operation = new SampleBatch(new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision,
            CameraInspectionMode.ObserveProduction, "本窗连续二十帧识读采样", new(1, 64, 1000), true), Position.ReaderId, token);
        batch = operation; UpdateBatchButtons();
        try
        {
            // Stop cancels this batch's wait, not Begin or the window lifetime.
            // Keep an uncertain Begin command for explicit same-identity reconciliation.
            operation.BeginUnconfirmed = true;
            var entered = await vm.ServiceAsync(() => Inspection.BeginCameraInspectionAsync(operation.Begin, CancellationToken.None));
            operation.Session = entered.SessionId ?? throw new InvalidOperationException("未取得试扫会话身份。");
            operation.BeginUnconfirmed = false;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(operation.Wait.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            long cursor = 0;
            while (operation.Frames.Count < 20)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var page = await vm.ServiceAsync(() => Inspection.ReadCameraFramesAsync(operation.Session, operation.Reader, cursor, null,
                    Math.Min(20 - operation.Frames.Count, 20), deadline.Token));
                foreach (var frame in page.Rows.Where(f => f.SessionId == operation.Session && f.DeviceId == operation.Reader))
                    if (!operation.Frames.Any(f => f.Id == frame.Id)) operation.Frames.Add(frame);
                cursor = Math.Max(cursor, page.Cursor);
                if (active && ReferenceEquals(batch, operation)) batchSummary.Text = $"已取得 {operation.Frames.Count}/20 帧；采样不发送RCS请求。";
                if (operation.Frames.Count < 20) await Task.Delay(100, deadline.Token);
            }
        }
        catch (OperationCanceledException) { operation.TimedOut = !operation.Wait.IsCancellationRequested; }
        catch (Exception error) { operation.Failure = Ipc.Application.ParcelFlow.Desktop.ChineseInterfaceText.ParameterMessage(error.Message, "本轮采样未完成，样本和原会话保留。"); }
        finally
        {
            try { await FinishBatchAsync(operation, reconcileBegin: false); }
            finally { operation.Completed.TrySetResult(); }
        }
    }
    async Task StopBatchAsync()
    {
        if (batch is not { } operation || batchStopping) return;
        var reconcile = operation.Completed.Task.IsCompleted;
        batchStopping = true; operation.StopRequested = true; operation.Wait.Cancel(); UpdateBatchButtons();
        if (active) batchSummary.Text = "正在结束本轮采样，等待原会话确认；不会撤销已发请求。";
        try
        {
            await operation.Completed.Task;
            if (reconcile && ReferenceEquals(batch, operation))
            {
                // Explicit retry reuses the original idempotent Begin/End identity.
                // No global status or another window's session is used as ownership.
                operation.Finishing = null;
                await FinishBatchAsync(operation, reconcileBegin: true);
            }
        }
        finally { batchStopping = false; UpdateBatchButtons(); }
    }
    Task FinishBatchAsync(SampleBatch operation, bool reconcileBegin)
        => operation.Finishing ??= FinishBatchCoreAsync(operation, reconcileBegin);
    async Task FinishBatchCoreAsync(SampleBatch operation, bool reconcileBegin)
    {
        try
        {
            if (operation.Session is null && operation.BeginUnconfirmed && reconcileBegin)
            {
                await vm.RequireManagementModeAsync();
                var original = await vm.ServiceAsync(() => Inspection.BeginCameraInspectionAsync(operation.Begin, CancellationToken.None));
                operation.Session = original.SessionId ?? throw new InvalidOperationException("原开始命令的结果仍未确认。");
                operation.BeginUnconfirmed = false;
            }
            if (operation.Session is null)
                throw new InvalidOperationException("试扫开始结果未确认；原命令保留，点击核对并结束原试扫。");
            operation.End ??= new(Wire.Id(), operation.Session, false, false, "结束本窗连续采样，保留样本与原请求");
            var ended = await vm.ServiceAsync(() => Inspection.EndCameraInspectionAsync(operation.End, CancellationToken.None));
            if (ended.SessionId != operation.Session || ended.State is not ("Closed" or "Stopped" or "Expired"))
                throw new InvalidOperationException("原试扫结束结果尚未确认。");
            if (!ReferenceEquals(batch, operation)) return;
            batch = null; operation.Wait.Dispose();
            if (active)
            {
                var frames = operation.Frames;
                batchSummary.Text = (operation.StopRequested ? "连续试扫已主动结束。" : operation.TimedOut ? "连续试扫已到时并结束。" : operation.Failure is not null ? "连续试扫失败，原会话已结束。" : "连续试扫已完成。") +
                    (operation.Failure is null ? "" : "\n" + operation.Failure) +
                    (frames.Count == 0 ? "\n没有可计算的有效样本。" : $"\n样本 {frames.Count} 帧 · 有码 {frames.Count(f => f.Symbols.Length > 0)} 帧 · 平均解码 {frames.Average(f => f.DecodeMilliseconds):F1} ms。") +
                    "\n原样本保留；自动供包保持管理暂停，关闭管理中心后按原状态恢复。";
            }
        }
        catch (Exception error)
        {
            operation.Failure ??= Ipc.Application.ParcelFlow.Desktop.ChineseInterfaceText.ParameterMessage(error.Message, "原试扫结束结果未确认。");
            if (active && ReferenceEquals(batch, operation)) batchSummary.Text = "试扫结束尚未确认；原会话、命令及样本保留。\n" + operation.Failure + "\n请核对并结束原试扫；不会新增RCS请求。";
        }
        finally { UpdateBatchButtons(); }
    }
}
