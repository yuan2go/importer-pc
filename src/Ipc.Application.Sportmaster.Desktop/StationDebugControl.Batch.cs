using System.Windows;
using System.Windows.Controls;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.Sportmaster.Desktop;

internal sealed partial class StationDebugControl
{
    string? batchSession;
    void InitializeBatch(Panel body)
    {
        var actions = new WrapPanel(); var start = Action("采样 20 帧"); var end = Action("停止试扫");
        var summary = Text("连续试扫只统计识别到的帧数，不计入生产。");
        actions.Children.Add(start); actions.Children.Add(end); body.Children.Add(actions); body.Children.Add(summary);
        start.Click += async (_, _) => await Run(async token =>
        {
            var snapshot = await vm.Application.SnapshotAsync();
            var session = await Inspection.BeginCameraInspectionAsync(new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, CameraInspectionMode.ObserveProduction, "连续二十帧软件识别测试", new(1, 64, 1000), true), token);
            batchSession = session.SessionId ?? throw new InvalidOperationException("未取得试扫会话身份");
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(30));
            var frames = new List<CameraFrameMetadata>(); long cursor = 0; var selected = Position.ReaderId;
            try
            {
                while (frames.Count < 20)
                {
                    var page = await Inspection.ReadCameraFramesAsync(batchSession, selected, cursor, null, Math.Min(20 - frames.Count, 20), budget.Token);
                    foreach (var frame in page.Rows.Where(f => f.DeviceId == selected)) if (!frames.Any(f => f.Id == frame.Id)) frames.Add(frame);
                    cursor = Math.Max(cursor, page.Cursor);
                    summary.Text = $"已取得 {frames.Count}/20 帧；正常供包继续，采样不额外发送调试请求";
                    if (frames.Count < 20) await Task.Delay(100, budget.Token);
                }
            }
            catch (OperationCanceledException) { summary.Text = "试扫已中断，保留已取得的样本。"; }
            finally
            {
                try { await Inspection.StopCameraInspectionCaptureAsync(batchSession, "连续试扫完成，结束采集", CancellationToken.None); }
                catch (Exception error) { vm.Message = Ipc.Application.ParcelFlow.Desktop.ChineseInterfaceText.ParameterMessage(error.Message, "试扫停止结果未确认，请查看调试记录。"); }
            }
            summary.Text += frames.Count == 0 ? "\n没有可计算的有效样本。" : $"\n样本 {frames.Count} 帧 · 有码 {frames.Count(f => f.Symbols.Length > 0)} 帧 · 有码帧占比 {100d * frames.Count(f => f.Symbols.Length > 0) / frames.Count:F1}% · 平均解码 {frames.Average(f => f.DecodeMilliseconds):F1} ms。正常供包状态未改变。";
        });
        end.Click += async (_, _) => await Run(async token =>
        {
            var original = await Inspection.CameraInspectionStatusAsync(token);
            if (original.SingleReadRounds || original.SessionId is null) throw new InvalidOperationException("没有连续试扫，或另一个扫码轮次正在进行。");
            if (batchSession is not null && batchSession != original.SessionId) throw new InvalidOperationException("当前采集属于其他会话，不能结束。");
            await Inspection.EndCameraInspectionAsync(new(Wire.Id(), original.SessionId, false, false, "结束连续试扫采集"), token);
            batchSession = null; summary.Text = "连续试扫已结束，正常供包状态未改变。";
        });
    }
}
