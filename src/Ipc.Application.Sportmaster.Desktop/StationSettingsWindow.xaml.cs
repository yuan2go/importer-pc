using System.ComponentModel;
using System.Windows;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

// Owns a view and a temporary grant only. The station keeps its one complete
// draft/page so invalid text and unconfirmed save identities survive this window.
public partial class StationSettingsWindow : Window
{
    readonly SupplyStationViewModel model;
    readonly MaintenancePage editor;
    readonly IWorkstationAdministration authority;
    sealed record DisplayField(string Label, string Value, string AutomationId);
    string? session;
    bool authenticating, closing, closeReady;
    public StationSettingsWindow(SupplyStationViewModel model, MaintenancePage editor)
    {
        InitializeComponent(); UiTypography.Apply(this);
        editor.SetStationSettingsLayout(EditActions);
        this.model = model; this.editor = editor; authority = (IWorkstationAdministration)model.Application;
        DataContext = model;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width); Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        model.PropertyChanged += Changed;
        Loaded += (_, _) => UpdateState();
        IsVisibleChanged += (_, _) => { if (IsVisible) RefreshSavedPreferences(); };
        Closing += ClosingSettings;
        Closed += (_, _) => { model.PropertyChanged -= Changed; editor.SetStationSettingsLayout(null); EditorHost.Content = null; if (model.OwnedManagementSession == session) model.OwnedManagementSession = null; };
    }
    void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(model.Snapshot) or nameof(model.IsManagementMode) or nameof(model.ManagementStatus) or nameof(model.Fresh)) UpdateState();
    }
    void UpdateState()
    {
        var granted = ReferenceEquals(EditorHost.Content, editor) && session is not null && model.IsManagementMode && model.ManagementStatus.SessionId == session;
        EditorHost.IsEnabled = granted && model.Fresh;
        EditorHost.Visibility = granted ? Visibility.Visible : Visibility.Collapsed;
        ReadOnlyBody.Visibility = granted ? Visibility.Collapsed : Visibility.Visible;
        EditSettings.Visibility = granted ? Visibility.Collapsed : Visibility.Visible;
        EditActions.Visibility = SettingsDraft.Visibility = granted ? Visibility.Visible : Visibility.Collapsed;
        ReturnToWorkstation.Content = granted ? "暂回作业" : "返回作业";
        ReturnToWorkstation.ToolTip = granted ? "隐藏设置窗，保留当前输入与编辑授权" : "隐藏设置窗，返回工作站";
        CloseSettings.Content = granted ? "结束编辑" : "关闭";
        CloseSettings.ToolTip = granted ? "关闭设置窗并结束本次授权" : "关闭设置窗";
        AuthorizationText.Text = granted ? $"编辑授权至 {model.ManagementStatus.ExpiresAt?.ToLocalTime():HH:mm}" :
            session is null ? "只读模式" : "授权待核对 · 草稿保留";
        EffectiveSettingsCards.Visibility = model.Fresh && model.Snapshot is not null ? Visibility.Visible : Visibility.Collapsed;
        if (!model.Fresh || model.Snapshot is not { } snapshot)
        {
            ReadOnlySettings.Text = "尚未取得有效设置，请等待工作站状态读取。";
            return;
        }

        var configuration = snapshot.Configuration.Effective;
        var rcs = configuration.Wcs;
        var host = rcs is not null && Uri.TryCreate(rcs.Target, UriKind.Absolute, out var uri)
            ? new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri.GetLeftPart(UriPartial.Authority) : "待配置";
        var revision = snapshot.Configuration.Revision.ToString();
        ReadOnlySettings.Text = snapshot.HasPendingConfiguration == true
            ? $"设置版本 #{revision} · 有待生效变更"
            : $"当前生效版本 #{revision} · 无待生效变更";
        UpdateFields(StationFields,
        [
            new("本站", configuration.StationId, "StationSettings.StationId"),
            new("供包台编号", string.IsNullOrWhiteSpace(configuration.StationCode) ? "待配置" : configuration.StationCode, "StationSettings.StationCode"),
            new("RCS 主机", host, "StationSettings.RcsHost"),
            new("仓 ID", rcs?.Parameters.GetValueOrDefault("zone-id") ?? "未单独保存", "StationSettings.Zone"),
            new("生产线 ID", rcs?.Parameters.GetValueOrDefault("project-id") ?? "未单独保存", "StationSettings.Project")
        ]);
        UpdateFields(LeftCameraFields, CameraFields("left"));
        UpdateFields(RightCameraFields, CameraFields("right"));

        DisplayField[] CameraFields(string position)
        {
            var topology = configuration.Topology;
            var lane = topology?.Positions.SingleOrDefault(p => p.Id == position);
            var reader = lane is null ? null : topology!.Readers.Single(r => r.DeviceId == lane.ReaderDeviceId);
            var prefix = "StationSettings.Camera." + position;
            return
            [
                new("IP", reader?.Parameters?.GetValueOrDefault("device.ip") is { Length: > 0 } ip ? ip : "未配置", prefix + ".Ip"),
                new("物理序列号", reader?.Parameters?.GetValueOrDefault("device.serial") is { Length: > 0 } serial ? serial : "未配置", prefix + ".Serial"),
                new("RCS 相机编码", rcs?.Parameters.GetValueOrDefault(position + "-bcr-code") is { Length: > 0 } code ? code : "本位停用", prefix + ".Code")
            ];
        }
    }
    static void UpdateFields(System.Windows.Controls.ItemsControl target, DisplayField[] fields)
    {
        if (target.ItemsSource is not DisplayField[] current || !current.SequenceEqual(fields))
            target.ItemsSource = fields;
    }
    void RefreshSavedPreferences()
    {
        // Read the saved per-user files independently of the edit session and its draft.
        var saved = new SmDisplayPreferenceDraft();
        PreferenceFields.ItemsSource = new DisplayField[]
        {
            new("默认打开方式", Value("WindowOpeningMode", saved.OpeningMode == PresentationPreferences.WindowOpeningMode.FullScreen ? "最大化（全屏）" : "窗口"), "StationSettings.WindowMode"),
            new("效率显示", Value("ShowPace", saved.ShowPace ? "显示" : "隐藏"), "StationSettings.Pace"),
            new("提示音音量", Value("CueVolume", saved.CueVolume == 0 ? "静音" : $"{saved.CueVolume}%"), "StationSettings.CueVolume")
        };
        string Value(string path, string value)
        {
            var key = path switch { "WindowOpeningMode" => "window", "ShowPace" => "pace", _ => "audio" };
            var name = path switch { "WindowOpeningMode" => "窗口方式", "ShowPace" => "效率显示", _ => "提示音" };
            model.SetPreferenceNotice(key, saved.CanEdit(path) ? null : name + "偏好未能读取，原文件保留；请检查本机设置文件与访问权限。");
            return saved.CanEdit(path) ? value : "未能读取";
        }
    }
    async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (authenticating || closing) return;
        authenticating = true; EditSettings.IsEnabled = false;
        try
        {
            var password = new ManagementModeWindow(authority, "设置编辑", bindSession: true) { Owner = this };
            password.ShowDialog();
            // A grant may exist even when its follow-up observation failed and
            // the password window was canceled. Retain its identity for exit.
            if (password.EnteredSessionId is { Length: > 0 } grantedId) { session = grantedId; model.OwnedManagementSession = grantedId; }
            if (!password.Entered) return;
            await model.RefreshManagementModeAsync();
            if (!model.IsManagementMode || password.EnteredSessionId is not { Length: > 0 } id || model.ManagementStatus.SessionId != id)
                throw new UnauthorizedAccessException("尚未确认本窗编辑授权，请重新验证。");
            session = id;
            await model.RunAsync(model.EnsureMaintenanceLoadedAsync, "读取完整设置草稿");
            if (!model.HasLoadedConfiguration) throw new InvalidOperationException("设置读取未完成，原配置保留，请核对状态后重试。");
            EditorHost.Content = editor; UpdateState();
        }
        catch (Exception error) { model.Message = ChineseInterfaceText.ParameterMessage(error.Message, "设置编辑尚未打开，原配置和输入保留。"); }
        finally { authenticating = false; EditSettings.IsEnabled = true; }
    }
    async void ClosingSettings(object? sender, CancelEventArgs e)
    {
        if (closeReady) return;
        e.Cancel = true; if (closing || authenticating) return;
        closing = true; IsEnabled = false;
        try
        {
            await System.Windows.Threading.Dispatcher.Yield();
            await CloseAfterDrainAsync();
        }
        catch (Exception error) { model.Message = "设置窗口尚未关闭，原授权和输入保留：" + ChineseInterfaceText.ParameterMessage(error.Message, "授权结束或原操作收尾未确认，请再次关闭重试。"); }
        finally { closing = false; IsEnabled = true; }
    }
    internal async Task CloseForOwnerStopAsync()
    {
        if (closing || authenticating) throw new InvalidOperationException("设置窗口仍有授权或关闭操作，请等待原结果。");
        closing = true; IsEnabled = false;
        try { await CloseAfterDrainAsync(); }
        finally { closing = false; IsEnabled = true; }
    }
    async Task CloseAfterDrainAsync()
    {
        ConfigurationCloseGuard.CommitAndFindInvalid(editor);
        await model.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        if (session is not null) await model.ServiceAsync(() => authority.ExitManagementModeAsync(session));
        await model.RefreshManagementModeAsync();
        closeReady = true; Close();
    }
    void Close_Click(object sender, RoutedEventArgs e) => Close();
    void Return_Click(object sender, RoutedEventArgs e) => (Owner as SupplyStationWindow)?.ReturnToWorkstation();
}
