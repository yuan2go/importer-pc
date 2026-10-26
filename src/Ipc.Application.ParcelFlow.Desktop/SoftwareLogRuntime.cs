using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed partial class SoftwareLogControl
{
    readonly CheckBox debugMode = new() { Content = "调试模式", MinHeight = 44, Margin = new(4, 0, 8, 4), IsEnabled = false,
        ToolTip = "立即生效，输出详细扫码、准入、请求、响应和耗时日志；重启后关闭。" };
    readonly CheckBox liveFollow = new() { Content = "实时跟随", MinHeight = 44, Margin = new(0, 0, 8, 4), Visibility = Visibility.Collapsed,
        ToolTip = "显示当前运行实例已写入的新日志，按关键词、级别和来源筛选；历史时间范围用于查询和导出。" };
    readonly Button pauseDisplay = new() { Content = "暂停显示", MinHeight = 44, Margin = new(0, 0, 8, 4), Visibility = Visibility.Collapsed,
        ToolTip = "只暂停终端画面；软件继续运行，后台日志继续写入。" };
    readonly DispatcherTimer diagnosticTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    CancellationTokenSource? liveCancellation;
    bool liveActive, displayPaused, liveReading, settingApplying, settingReading, updatingDebugControl, updatingFollowControl, diagnosticsClosed;
    long liveVersion, diagnosticVersion, settingsRevision = -1;
    int diagnosticTicks;
    Guid? liveStream;
    long? liveCursor;
    SoftwareLogQuery? liveFilter;
    string historyTerminalText = "", liveNotice = "";
    IRuntimeDiagnosticsControl? RuntimeDiagnostics => service as IRuntimeDiagnosticsControl;

    void InitializeRuntimeDiagnostics(WrapPanel actions)
    {
        debugMode.Visibility = RuntimeDiagnostics is null ? Visibility.Collapsed : Visibility.Visible;
        actions.Children.Add(debugMode); actions.Children.Add(liveFollow); actions.Children.Add(pauseDisplay);
        AutomationProperties.SetName(debugMode, "调试模式，立即生效"); AutomationProperties.SetAutomationId(debugMode, "DebugLoggingMode");
        AutomationProperties.SetName(liveFollow, "实时跟随日志"); AutomationProperties.SetAutomationId(pauseDisplay, "PauseLogDisplay");
        debugMode.Checked += async (_, _) => { if (!updatingDebugControl) await SetDebugMode(true); };
        debugMode.Unchecked += async (_, _) => { if (!updatingDebugControl) await SetDebugMode(false); };
        liveFollow.Checked += async (_, _) => { if (!updatingFollowControl) await StartLive(); };
        liveFollow.Unchecked += (_, _) => { if (!updatingFollowControl) { StopLive(true); UpdateActions(); } };
        pauseDisplay.Click += async (_, _) =>
        {
            if (!liveActive) return;
            displayPaused = !displayPaused;
            if (displayPaused) { liveVersion++; liveCancellation?.Cancel(); status.Text = "显示已暂停；可选中复制，后台日志继续写入，继续显示后补读。"; }
            UpdateActions();
            if (!displayPaused) await PollLive();
        };
        Loaded += async (_, _) => { diagnosticsClosed = false; diagnosticTimer.Start(); await RefreshDiagnosticSettings(); };
        diagnosticTimer.Tick += async (_, _) =>
        {
            if (diagnosticsClosed || !IsVisible || settingApplying) return;
            if (liveActive && !displayPaused && !busy) await PollLive();
            else if (++diagnosticTicks % 5 == 0) await RefreshDiagnosticSettings();
        };
    }
    void ApplyDiagnosticSettings(RuntimeDiagnosticSettings settings)
    {
        if (settingApplying || settings.Revision < settingsRevision) return;
        settingsRevision = settings.Revision; updatingDebugControl = true;
        try { debugMode.IsChecked = settings.DebugEnabled; }
        finally { updatingDebugControl = false; }
    }
    async Task RefreshDiagnosticSettings()
    {
        if (RuntimeDiagnostics is not { } control || diagnosticsClosed || settingApplying || settingReading) return;
        var version = diagnosticVersion; settingReading = true;
        try
        {
            var settings = await control.DiagnosticSettingsAsync();
            if (version != diagnosticVersion || diagnosticsClosed) return;
            ApplyDiagnosticSettings(settings);
        }
        catch (UnauthorizedAccessException) { if (version == diagnosticVersion) Revoke(); }
        catch (Exception error)
        {
            if (version == diagnosticVersion && !diagnosticsClosed)
            { settingsRevision = -1; status.Text = "运行日志设置暂未取得，请稍后重试。"; diagnosticText.Text = Ipc.Platform.DiagnosticText.Protect(error.ToString()); diagnostics.Visibility = Visibility.Visible; }
        }
        finally { settingReading = false; UpdateRuntimeDiagnosticActions(); }
    }
    async Task SetDebugMode(bool enabled)
    {
        if (RuntimeDiagnostics is not { } control || diagnosticsClosed || settingApplying) return;
        var version = ++diagnosticVersion; settingApplying = true; debugMode.IsEnabled = false;
        try
        {
            var settings = await control.SetDebugLoggingAsync(enabled);
            if (version != diagnosticVersion || diagnosticsClosed) return;
            settingApplying = false; ApplyDiagnosticSettings(settings);
            status.Text = settings.DebugEnabled ? "调试模式已开启，详细日志立即生效。" : "调试模式已关闭，常规日志继续记录。";
        }
        catch (UnauthorizedAccessException) { if (version == diagnosticVersion) Revoke(); }
        catch (Exception error)
        {
            if (version == diagnosticVersion && !diagnosticsClosed)
            { status.Text = "调试模式更改未确认，正在重新读取实际状态。"; diagnosticText.Text = Ipc.Platform.DiagnosticText.Protect(error.ToString()); diagnostics.Visibility = Visibility.Visible; }
        }
        finally
        {
            settingApplying = false; UpdateRuntimeDiagnosticActions();
            if (!diagnosticsClosed && version == diagnosticVersion) await RefreshDiagnosticSettings();
        }
    }
    void RuntimeViewChanged()
    {
        if (viewMode.SelectedIndex != 1) StopLive(true);
        UpdateActions();
    }
    void UpdateRuntimeDiagnosticActions()
    {
        debugMode.IsEnabled = RuntimeDiagnostics is not null && !diagnosticsClosed && !settingApplying && settingsRevision >= 0;
        var terminalMode = viewMode.SelectedIndex == 1 && RuntimeDiagnostics is not null;
        liveFollow.Visibility = pauseDisplay.Visibility = terminalMode ? Visibility.Visible : Visibility.Collapsed;
        liveFollow.IsEnabled = !busy && !diagnosticsClosed; pauseDisplay.IsEnabled = liveActive && !diagnosticsClosed;
        pauseDisplay.Content = displayPaused ? "继续显示" : "暂停显示";
        if (!liveActive) return;
        previousButton.IsEnabled = nextButton.IsEnabled = contextButton.IsEnabled = exportButton.IsEnabled = rangeButton.IsEnabled = false;
        effectiveRange.Text = "实时终端 · " + (displayPaused ? "已暂停显示，后台继续写入" : "跟随当前运行日志") +
            " · 当前关键词、级别、来源筛选 · 历史时间范围用于查询；退出实时跟随后可查询和导出";
    }
    async Task StartLive()
    {
        if (RuntimeDiagnostics is null || diagnosticsClosed || busy) { StopLive(true); return; }
        try { liveFilter = ReadQuery(); }
        catch (Exception error) { status.Text = error.Message; StopLive(true); return; }
        liveVersion++; liveActive = true; displayPaused = false; liveStream = null; liveCursor = null; liveNotice = "";
        ClearDetail(); terminal.Text = "正在读取当前运行日志…"; UpdateActions(); await PollLive();
    }
    void StopLive(bool restoreHistory)
    {
        var wasLive = liveActive;
        liveVersion++; liveCancellation?.Cancel(); liveActive = false; displayPaused = false;
        updatingFollowControl = true;
        try { liveFollow.IsChecked = false; }
        finally { updatingFollowControl = false; }
        if (restoreHistory && wasLive) terminal.Text = historyTerminalText;
    }
    void RevokeRuntimeDiagnostics()
    {
        diagnosticsClosed = true; diagnosticVersion++; diagnosticTimer.Stop(); StopLive(false);
        settingsRevision = -1; historyTerminalText = "";
        updatingDebugControl = true;
        try { debugMode.IsChecked = false; }
        finally { updatingDebugControl = false; }
    }
    static SoftwareLogEntry RuntimeEntry(RuntimeDiagnosticEntry entry)
    {
        string source = "运行", level = "信息";
        try
        {
            using var json = JsonDocument.Parse(entry.Text);
            if (json.RootElement.TryGetProperty("Source", out var s) && s.ValueKind == JsonValueKind.String) source = s.GetString()!;
            if (json.RootElement.TryGetProperty("Level", out var l) && l.ValueKind == JsonValueKind.String) level = l.GetString()!;
        }
        catch (JsonException) { }
        return new(new("live", entry.Sequence, "", "当前运行日志"), source, entry.WrittenAt, level, entry.Text);
    }
    async Task PollLive()
    {
        if (RuntimeDiagnostics is not { } control || !liveActive || displayPaused || liveReading || diagnosticsClosed) return;
        liveReading = true; var version = liveVersion;
        using var owner = new CancellationTokenSource(TimeSpan.FromSeconds(10)); liveCancellation = owner; var token = owner.Token;
        try
        {
            var batch = await control.ReadDiagnosticTailAsync(liveStream, liveCursor, token);
            var filter = liveFilter!;
            var text = await Task.Run(() =>
            {
                var regex = filter.Regex ? new Regex(filter.Keyword, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(25)) : null;
                var entries = batch.Entries.Select(RuntimeEntry).Where(e => (filter.Level.Length == 0 || filter.Level == e.Level) &&
                    (filter.Source.Length == 0 || e.Source.Contains(filter.Source, StringComparison.OrdinalIgnoreCase)) &&
                    (regex?.IsMatch(e.Text) ?? e.Text.Contains(filter.Keyword, StringComparison.OrdinalIgnoreCase)));
                return SoftwareLogText.Format(entries, token);
            }, token);
            if (!(await service.ManagementModeStatusAsync()).Authorized) { Revoke(); return; }
            token.ThrowIfCancellationRequested();
            if (version != liveVersion || !liveActive || displayPaused || diagnosticsClosed) return;
            if (liveCursor is null || liveStream != batch.Stream) terminal.Clear();
            liveStream = batch.Stream; liveCursor = batch.Cursor; ApplyDiagnosticSettings(batch.Settings);
            if (text.Length > 0)
            {
                terminal.AppendText((terminal.Text.Length == 0 ? "" : "\n\n") + text);
                if (terminal.Text.Length > SoftwareLogText.MaximumCharacters)
                {
                    var start = terminal.Text.Length - 64500;
                    if (char.IsLowSurrogate(terminal.Text[start])) start++;
                    terminal.Text = "【实时显示仅保留末尾内容；完整原文请按时间范围查询并导出。】\n" + terminal.Text[start..];
                }
                terminal.ScrollToEnd();
            }
            if (batch.Missed > 0) liveNotice = "暂停期间实时缓冲已轮转，" + batch.Missed + "条旧记录请到历史查询查看；";
            status.Text = (displayPaused ? "显示已暂停；" : "实时跟随；") + liveNotice + (batch.HasMore ? "正在补读缓冲日志；" : "") +
                (batch.DebugDropped > 0 ? "详细日志写入繁忙，已丢弃调试记录" + batch.DebugDropped + "条；" : "") + "后台日志继续记录，最多显示65536字符。";
        }
        catch (OperationCanceledException) when (version != liveVersion || diagnosticsClosed) { }
        catch (UnauthorizedAccessException) { if (version == liveVersion) Revoke(); }
        catch (Exception error)
        {
            if (version == liveVersion && !diagnosticsClosed)
            { displayPaused = true; status.Text = "实时读取未完成，显示已暂停；可继续显示重试，后台日志仍在记录。"; diagnosticText.Text = Ipc.Platform.DiagnosticText.Protect(error.ToString()); diagnostics.Visibility = Visibility.Visible; }
        }
        finally { if (ReferenceEquals(liveCancellation, owner)) liveCancellation = null; liveReading = false; UpdateActions(); }
    }
}
