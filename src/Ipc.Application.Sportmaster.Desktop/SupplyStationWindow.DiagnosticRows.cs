using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    FrameworkElement CreateDiagnosticRows()
    {
        var panel = new StackPanel(); var cards = new Grid();
        cards.ColumnDefinitions.Add(new()); cards.ColumnDefinitions.Add(new());
        cards.RowDefinitions.Add(new() { Height = GridLength.Auto }); cards.RowDefinitions.Add(new() { Height = GridLength.Auto }); panel.Children.Add(cards);
        var details = new Dictionary<string, TextBlock>(); var actions = new List<Action>();
        TextBlock Bound(string property, object source)
        {
            var text = ProductText(""); text.SetBinding(TextBlock.TextProperty, new Binding(property) { Source = source }); return text;
        }
        StackPanel Card(string title, string key, FrameworkElement status)
        {
            var content = new StackPanel();
            var heading = new DockPanel(); DockPanel.SetDock(status, Dock.Right); heading.Children.Add(status); heading.Children.Add(ProductText(title, true)); content.Children.Add(heading);
            var card = new Border { Child = content, BorderThickness = new(1), CornerRadius = new(8), Padding = new(16), Margin = new(0, 0, 12, 12) };
            card.SetResourceReference(Border.BackgroundProperty, "Surface"); card.SetResourceReference(Border.BorderBrushProperty, "Line");
            AutomationProperties.SetAutomationId(card, "DiagnosticCard." + key);
            Grid.SetRow(card, cards.Children.Count / 2); Grid.SetColumn(card, cards.Children.Count % 2); cards.Children.Add(card); diagnosticObjects[key] = card; return content;
        }
        TextBlock Badge(object source, string prefix)
        {
            var badge = new TextBlock { Padding = new(8, 4, 8, 4), Margin = new(8, 6, 0, 0), VerticalAlignment = VerticalAlignment.Top, FontSize = UiTypography.Label };
            foreach (var (property, suffix) in new[] { (TextBlock.TextProperty, "Text"), (TextBlock.ForegroundProperty, "Foreground"), (TextBlock.BackgroundProperty, "Background") })
                badge.SetBinding(property, new Binding(prefix + "." + suffix) { Source = source });
            return badge;
        }
        void Connection(string name, string id, string diagnosis, string time, SupplyStationPositionViewModel? position)
        {
            var row = Card(name, id, position is null ? Badge(ViewModel, "RcsPresentation") : Badge(position, "DevicePresentation"));
            row.Children.Add(Bound(diagnosis, ViewModel));
            var buttons = new WrapPanel(); var primary = new Button { MinHeight = 44, MinWidth = 112, Margin = new(0, 6, 12, 6) }; buttons.Children.Add(primary);
            bool deferred = true, ready = false;
            async Task Configure() => await OpenOperatorSettingsAsync(position?.ReaderId, position is null ? ViewModel.Snapshot?.Configuration.Effective.Wcs?.InstanceId : null);
            primary.Click += async (_, _) =>
            {
                if (deferred || ready) { await Configure(); return; }
                await ViewModel.RunAsync(async () =>
                {
                    await ViewModel.RequireManagementModeAsync();
                    if (!ViewModel.Fresh) throw new InvalidOperationException("状态读取已中断，请等待恢复后再连接。");
                    var target = position?.ReaderId ?? ViewModel.Snapshot?.Configuration.Effective.Wcs?.InstanceId ?? throw new InvalidOperationException("RCS 尚未配置。");
                    var receipt = await ViewModel.Application.ReconnectDeviceAsync(target);
                    ViewModel.Message = receipt.Status switch
                    {
                        "Reconnected" => name + "已完成重连操作，请查看当前连接状态。",
                        "MaintenanceIncomplete" => name + "重新连接未完成。" + (position is null
                            ? "请检查服务地址和网络，具体原因见日志。"
                            : "请检查供电、网线和相机设置，具体原因见日志。"),
                        "Rejected" => name + "暂不能重新连接。" + Ipc.Application.ParcelFlow.Desktop.ChineseInterfaceText.ParameterMessage(
                            receipt.Reason, "请等当前操作结束后重试。"),
                        _ => name + "的重新连接结果待确认，请查看连接状态和日志。"
                    };
                }, "处理" + name + "连接");
            };
            buttons.Children.Add(ProductButton("调试", () => { if (position is null) SetPage("debug"); else ShowPositionDebug(position); return Task.CompletedTask; })); row.Children.Add(buttons);
            var facts = new StackPanel(); facts.Children.Add(Bound(time, ViewModel));
            var technical = ProductText("等待读取设备详情"); technical.FontSize = UiTypography.Label; facts.Children.Add(technical); details[id] = technical;
            var edit = new Button { Content = "修改连接设置", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
            edit.SetBinding(IsEnabledProperty, new Binding(nameof(ViewModel.CanStartOperation)) { Source = ViewModel });
            edit.Click += async (_, _) => await Configure(); facts.Children.Add(edit);
            if (position is not null)
            {
                facts.Children.Add(ProductText("清空托盘后校准在位判断。校准后可连续自动供包；重连或重启后须重新校准。校准期间保持空盘，等待详情显示空托盘已确认。"));
                var teach = new Button { Content = "确认空托盘并校准", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
                AutomationProperties.SetAutomationId(teach, "CalibrateEmptyTray." + id);
                teach.Click += async (_, _) => await ViewModel.RunAsync(async () =>
                {
                    await ViewModel.RequireManagementModeAsync();
                    var snapshot = ViewModel.Snapshot ?? throw new InvalidOperationException("请等待状态读取恢复。");
                    var result = await ViewModel.Application.CommandAsync(new(Wire.Id(), snapshot.Revision, "CalibrateEmptyTray", position.ReaderId));
                    ViewModel.Message = result.Status == "Persisted" ? "空托盘校准已开始，请保持画面稳定并查看设备详情。" : "校准未开始。请确认本位没有当前包裹、相机已连接，并刷新后重试。";
                }, "校准空托盘");
                actions.Add(() => teach.IsEnabled = ViewModel.IsManagementMode && ViewModel.Fresh && !ViewModel.Busy);
                facts.Children.Add(teach);
            }
            row.Children.Add(new Expander { Header = "详情", Content = facts, Margin = new(0, 8, 0, 0) });
            actions.Add(() =>
            {
                var snapshot = ViewModel.Snapshot; var config = snapshot?.Configuration.Effective;
                deferred = config is null || (position is null ? config.Wcs is not { } w || ParcelConnectionSetup.IsDeferred(config, w.InstanceId) : ParcelConnectionSetup.IsDeferred(config, id));
                var device = snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == id);
                var health = snapshot?.Health.SingleOrDefault(h => h.InstanceId == config?.Wcs?.InstanceId);
                ready = ViewModel.Fresh && (position is null ? health is { Ready: true, RemoteConfirmation: RemoteConnectionConfirmation.ResponseReceived } && health.ConfirmedAt <= DateTimeOffset.UtcNow && health.ValidUntil > DateTimeOffset.UtcNow : device?.IsValid(DateTimeOffset.UtcNow) == true);
                var connecting = !deferred && position is not null && device?.Health is DeviceHealth.Connecting or DeviceHealth.Reconnecting or DeviceHealth.Stopping;
                primary.Content = deferred ? "去设置" : ready ? "设置" : connecting ? "正在连接…" : "重新连接";
                primary.IsEnabled = ViewModel.IsManagementMode && ViewModel.Fresh && !ViewModel.Busy && !connecting;
                AutomationProperties.SetName(primary, name + "：" + primary.Content);
            });
        }
        Connection("左相机", ViewModel.Left.ReaderId, nameof(ViewModel.LeftConnectionDiagnosis), nameof(ViewModel.LeftConnectionTime), ViewModel.Left);
        Connection("右相机", ViewModel.Right.ReaderId, nameof(ViewModel.RightConnectionDiagnosis), nameof(ViewModel.RightConnectionTime), ViewModel.Right);
        Connection("RCS", "shared", nameof(ViewModel.RcsConnectionDiagnosis), nameof(ViewModel.RcsConnectionTime), null);
        var storageBadge = ProductText("等待读取"); storageBadge.FontSize = UiTypography.Label;
        var storage = Card("存储", "storage", storageBadge); var storageText = ProductText(""); storage.Children.Add(storageText);
        var storageSettings = new Button { Content = "数据与记录设置", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        storageSettings.SetBinding(IsEnabledProperty, new Binding(nameof(ViewModel.CanStartOperation)) { Source = ViewModel });
        storageSettings.Click += async (_, _) => { await OpenOperatorSettingsAsync(); if (ViewModel.IsManagementMode && ViewModel.HasLoadedConfiguration) maintenance.NavigateToParameter("HistoryDays"); }; storage.Children.Add(storageSettings);
        var storageDetails = new StackPanel(); storageDetails.Children.Add(Bound(nameof(ViewModel.BackupStatusText), ViewModel));
        storage.Children.Add(new Expander { Header = "详情", Content = storageDetails });
        actions.Add(() =>
        {
            var capacity = ViewModel.Fresh ? ViewModel.Snapshot?.Capacity : null;
            var unavailable = capacity?.AdmissionBlock == ParcelInputBlockReason.StorageUnavailable;
            var free = capacity?.FreeStorageBytes;
            var (label, ink) = unavailable ? ("作业存储异常", "Danger") :
                free is null ? ("状态未确认", "Muted") : ("正常", "Success");
            storageBadge.Text = label;
            storageBadge.SetResourceReference(TextBlock.ForegroundProperty, ink);
            storageText.Text = capacity is null ? "等待存储状态更新。" : free is { } bytes
                ? $"剩余 {bytes / 1048576:N0} MiB。低于 {capacity.MinimumFreeBytes / 1048576:N0} MiB 时停止新供包。"
                : "剩余空间未确认。请检查 D 盘并等待状态更新。";
        });

        var logState = ProductText("详细日志状态未确认"); var toggle = new Button { Content = "开启详细日志 30 分钟", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        var logDetails = new StackPanel(); logDetails.Children.Add(logState); logDetails.Children.Add(toggle);
        diagnosticLogDetails = logDetails; // shown inside the page's single "技术详情" section
        CancellationTokenSource? lifetime = null; long generation = 0; bool reading = false, setting = false;
        RuntimeDiagnosticSettings? current = null;
        void UpdateActions() { foreach (var action in actions) action(); }
        PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName is nameof(ViewModel.Snapshot) or nameof(ViewModel.Fresh) or nameof(ViewModel.Busy) or nameof(ViewModel.IsManagementMode)) UpdateActions(); };
        async Task Read()
        {
            if (reading || lifetime is null || !ViewModel.IsManagementMode) return;
            reading = true; var version = generation; var token = lifetime.Token;
            try
            {
                await ViewModel.RequireManagementModeAsync();
                foreach (var position in new[] { ViewModel.Left, ViewModel.Right })
                {
                    if (ViewModel.Application is not ICameraAcquisitionDiagnostics api) continue;
                    var sample = await api.ReadCameraAcquisitionAsync(position.ReaderId, token);
                    if (token.IsCancellationRequested || version != generation || !ViewModel.IsManagementMode) return;
                    details[position.ReaderId].Text = CameraAcquisitionPresentation.Describe(sample);
                }
                var latency = ViewModel.Application is IStationDiagnostics station ? await station.ReadRequestLatencyAsync(token) : null;
                var settings = ViewModel.Application is IRuntimeDiagnosticsControl logs ? await logs.DiagnosticSettingsAsync() : null;
                if (token.IsCancellationRequested || version != generation || !ViewModel.IsManagementMode) return;
                details["shared"].Text = latency is null || latency.Samples == 0 ? "当前运行尚无取格口耗时样本。" : $"最近 {latency.Coverage.TotalMinutes:F0} 分钟 · {latency.Samples} 次 · 中位 {latency.MedianMs:F0} ms · P95 {latency.P95Ms:F0} ms";
                current = settings; logState.Text = settings?.DebugEnabled == true ? "详细日志已开启 · " + settings.ExpiresAt?.ToLocalTime().ToString("HH:mm:ss") + " 自动关闭" : "详细日志已关闭，常规日志继续记录";
                toggle.Content = settings?.DebugEnabled == true ? "关闭详细日志" : "开启详细日志 30 分钟"; toggle.IsEnabled = settings is not null && !setting;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception) { if (version == generation) { foreach (var text in details.Values) text.Text = "详情读取中断，旧指标已失效；正在自动重试。"; logState.Text = "详细日志状态未确认"; toggle.IsEnabled = false; } }
            finally { reading = false; UpdateActions(); }
        }
        toggle.Click += async (_, _) =>
        {
            if (setting || lifetime is null || ViewModel.Application is not IRuntimeDiagnosticsControl api) return;
            setting = true; toggle.IsEnabled = false;
            try { await ViewModel.RequireManagementModeAsync(); await api.SetDebugLoggingAsync(current?.DebugEnabled != true); }
            catch (Exception error) { logState.Text = Ipc.Application.ParcelFlow.Desktop.ChineseInterfaceText.ParameterMessage(error.Message, "日志设置结果未确认，请等待状态自动更新。"); }
            finally { setting = false; await Read(); }
        };
        var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; poll.Tick += async (_, _) => { UpdateActions(); await Read(); };
        panel.Loaded += async (_, _) => { generation++; lifetime = new(); ViewModel.PropertyChanged += changed; UpdateActions(); poll.Start(); await Read(); };
        panel.Unloaded += (_, _) => { generation++; poll.Stop(); ViewModel.PropertyChanged -= changed; lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; current = null; };
        return panel;
    }
}
