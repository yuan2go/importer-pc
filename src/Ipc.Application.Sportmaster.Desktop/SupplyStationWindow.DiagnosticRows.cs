using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    FrameworkElement CreateDiagnosticRows()
    {
        var panel = new StackPanel(); var rows = new Dictionary<string, TextBlock>();
        panel.Children.Add(ProductText("体检只读取现有相机和心跳数据，不发送测试请求；有码帧占比受空场、多码与解码抽帧影响，不代表包裹漏读率。"));
        foreach (var position in new[] { ViewModel.Left, ViewModel.Right })
        {
            var cameraName = position.PositionId == "left" ? "左相机" : "右相机";
            var row = new DockPanel { Margin = new(0, 8, 0, 12) }; var facts = new StackPanel(); facts.Children.Add(ProductText(cameraName, true));
            var text = ProductText("等待读取当前连接"); text.FontSize = UiTypography.Label; rows.Add(position.ReaderId, text); facts.Children.Add(text);
            var actions = new StackPanel { Margin = new(12, 0, 0, 0) }; DockPanel.SetDock(actions, Dock.Right);
            actions.Children.Add(ProductButton("设置" + cameraName, () => OpenOperatorSettingsAsync(position.ReaderId)));
            actions.Children.Add(ProductButton("调试" + cameraName, () => { ShowPositionDebug(position); return Task.CompletedTask; }));
            row.Children.Add(actions); row.Children.Add(facts); panel.Children.Add(row); diagnosticObjects[position.ReaderId] = row;
        }
        var rcs = ProductText("RCS：等待读取"); var storage = ProductText(""); panel.Children.Add(rcs); panel.Children.Add(storage);
        var logState = ProductText("详细日志：等待读取"); var toggle = new Button { Content = "开启详细日志 30 分钟", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(logState); panel.Children.Add(toggle);
        CancellationTokenSource? lifetime = null; long generation = 0; bool reading = false, setting = false;
        RuntimeDiagnosticSettings? current = null;
        async Task Read()
        {
            if (reading || lifetime is null || !ViewModel.IsManagementMode) return;
            reading = true; var version = generation; var token = lifetime.Token;
            try
            {
                await ViewModel.RequireManagementModeAsync();
                foreach (var entry in rows)
                {
                    if (ViewModel.Application is not ICameraAcquisitionDiagnostics api) { entry.Value.Text = "当前来源没有采集诊断能力"; continue; }
                    var value = await api.ReadCameraAcquisitionAsync(entry.Key, token);
                    if (token.IsCancellationRequested || version != generation || !ViewModel.IsManagementMode) return;
                    entry.Value.Text = CameraAcquisitionPresentation.Describe(value);
                }
                var snapshot = await ViewModel.Application.SnapshotAsync(); token.ThrowIfCancellationRequested();
                var latency = ViewModel.Application is IStationDiagnostics station ? await station.ReadRequestLatencyAsync(token) : null;
                var settings = ViewModel.Application is IRuntimeDiagnosticsControl logs ? await logs.DiagnosticSettingsAsync() : null;
                if (version != generation || !ViewModel.IsManagementMode) return;
                var health = snapshot.Health.FirstOrDefault(h => h.InstanceId == snapshot.Configuration.Effective.Wcs?.InstanceId);
                var rcsState = health?.Ready != true ? "组件未就绪" : health.RemoteConfirmation == RemoteConnectionConfirmation.ResponseReceived && health.ValidUntil > DateTimeOffset.UtcNow && health.ConfirmedAt <= DateTimeOffset.UtcNow ? "心跳正常（只表示 RCS 可连接）" : "等待 RCS 确认";
                rcs.Text = "RCS：" + rcsState + $" · 确认于 {health?.ConfirmedAt.ToLocalTime():HH:mm:ss} · 有效至 {health?.ValidUntil.ToLocalTime():HH:mm:ss}" + (latency is null ? "" : $"\n当前运行最近 {latency.Coverage.TotalMinutes:F1} 分钟 · 请求样本 {latency.Samples} · 中位 {latency.MedianMs?.ToString("F1") ?? "—"} ms · P95 {latency.P95Ms?.ToString("F1") ?? "—"} ms · 本次运行容量淘汰 {latency.Omitted}（不含调试，最多4096样本）");
                storage.Text = $"存储剩余 {snapshot.Capacity?.FreeStorageBytes / 1048576} MiB · 备份状态：{ViewModel.BackupStatusText}";
                current = settings;
                logState.Text = settings?.DebugEnabled == true ? "详细日志已开启 · " + settings.ExpiresAt?.ToLocalTime().ToString("HH:mm:ss") + " 自动关闭" : "详细日志已关闭，常规日志继续记录";
                toggle.Content = settings?.DebugEnabled == true ? "关闭详细日志" : "开启详细日志 30 分钟";
                toggle.IsEnabled = settings is not null && !setting;
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (version == generation) { foreach (var text in rows.Values) text.Text = "诊断读取中断，旧指标已失效"; rcs.Text = "诊断读取中断，请刷新体检；持续失败时导出诊断记录。"; storage.Text = "存储与备份读取中断，原指标已失效"; logState.Text = "详细日志状态未确认"; toggle.IsEnabled = false; } }
            finally { reading = false; }
        }
        toggle.Click += async (_, _) =>
        {
            if (setting || lifetime is null || ViewModel.Application is not IRuntimeDiagnosticsControl api) return;
            setting = true; toggle.IsEnabled = false;
            try { await ViewModel.RequireManagementModeAsync(); await api.SetDebugLoggingAsync(current?.DebugEnabled != true); }
            catch (Exception error) { logState.Text = Ipc.Application.ParcelFlow.Desktop.ChineseInterfaceText.ParameterMessage(error.Message, "日志设置结果未确认，请刷新体检；持续失败时导出诊断记录。"); }
            finally { setting = false; await Read(); }
        };
        var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; poll.Tick += async (_, _) => await Read();
        panel.Loaded += async (_, _) => { generation++; lifetime = new(); poll.Start(); await Read(); };
        panel.Unloaded += (_, _) => { generation++; poll.Stop(); lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; current = null; foreach (var text in rows.Values) text.Text = "等待读取当前连接"; };
        return panel;
    }
}
