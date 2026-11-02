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
        var actions = new WrapPanel(); var start = Action("连续试扫 20 帧（不发送请求）"); var end = Action("确认移除测试件并结束试扫…");
        var summary = Text("连续试扫统计已解码帧，不代表20件包裹。两位生产受理均隔离，结果不计入生产统计。");
        actions.Children.Add(start); actions.Children.Add(end); body.Children.Add(actions); body.Children.Add(summary);
        start.Click += async (_, _) => await Run(async token =>
        {
            if (MessageBox.Show(Window.GetWindow(this), "连续试扫会暂停两位生产受理，不发送取格口请求。请停止真实投放，核对两位包裹和未完成作业，再继续。", "连续试扫", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            var snapshot = await vm.Application.SnapshotAsync();
            var session = await Inspection.BeginCameraInspectionAsync(new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, CameraInspectionMode.Isolated, "连续二十帧软件识别测试", new(1, 64, 1000), true), token);
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
                    summary.Text = $"已取得 {frames.Count}/20 帧；不发送业务请求";
                    if (frames.Count < 20) await Task.Delay(100, budget.Token);
                }
            }
            catch (OperationCanceledException) { summary.Text = "试扫已中断，保留已取得的样本和隔离。"; }
            finally
            {
                try { await Inspection.StopCameraInspectionCaptureAsync(batchSession, "连续试扫结束，保持隔离等待清场", CancellationToken.None); }
                catch (Exception error) { vm.Message = "试扫停止结果未确认，隔离保留：" + DiagnosticText.Protect(error.Message); }
            }
            summary.Text += frames.Count == 0 ? "\n没有可计算的有效样本。" : $"\n样本 {frames.Count} 帧 · 有码 {frames.Count(f => f.Symbols.Length > 0)} 帧 · 有码帧占比 {100d * frames.Count(f => f.Symbols.Length > 0) / frames.Count:F1}% · 平均解码 {frames.Average(f => f.DecodeMilliseconds):F1} ms。未恢复供包，请确认清场。";
        });
        end.Click += async (_, _) => await Run(async token =>
        {
            var original = await Inspection.CameraInspectionStatusAsync(token);
            if (original.SingleReadRounds || !original.BusinessFenced || original.SessionId is null) throw new InvalidOperationException("没有连续试扫隔离，或另一个按轮调试仍在进行。");
            if (batchSession is not null && batchSession != original.SessionId) throw new InvalidOperationException("当前隔离属于其他会话，不能结束。");
            if (MessageBox.Show(Window.GetWindow(this), "确认两位测试件均已移除，原未完成作业已经核对。这里只结束试扫隔离，仍保持软件暂停。", "确认清场", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            await Inspection.EndCameraInspectionAsync(new(Wire.Id(), original.SessionId, true, false, "两位测试件已移除，结束连续试扫"), token);
            batchSession = null; summary.Text = "试扫隔离已结束，软件仍暂停。请核对连接和作业后恢复供包。";
        });
    }
}
