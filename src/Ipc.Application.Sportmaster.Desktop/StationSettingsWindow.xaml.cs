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
    string? session;
    bool authenticating, closing, closeReady;
    public StationSettingsWindow(SupplyStationViewModel model, MaintenancePage editor)
    {
        InitializeComponent(); UiTypography.Apply(this);
        this.model = model; this.editor = editor; authority = (IWorkstationAdministration)model.Application;
        DataContext = model;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width); Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        model.PropertyChanged += Changed;
        Loaded += (_, _) => UpdateState();
        Closing += ClosingSettings;
        Closed += (_, _) => { model.PropertyChanged -= Changed; EditorHost.Content = null; };
    }
    void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(model.Snapshot) or nameof(model.IsManagementMode) or nameof(model.ManagementStatus) or nameof(model.Fresh)) UpdateState();
    }
    void UpdateState()
    {
        var granted = session is not null && model.IsManagementMode && model.ManagementStatus.SessionId == session;
        EditorHost.IsEnabled = granted && model.Fresh;
        EditorHost.Visibility = granted ? Visibility.Visible : Visibility.Collapsed;
        ReadOnlyBody.Visibility = granted ? Visibility.Collapsed : Visibility.Visible;
        EditSettings.Visibility = granted ? Visibility.Collapsed : Visibility.Visible;
        AuthorizationText.Text = granted ? $"编辑授权至 {model.ManagementStatus.ExpiresAt?.ToLocalTime():HH:mm} · 保存时执行原配置守卫" :
            session is null ? "只读查看，不暂停供包；关闭后未保存输入仍保留在当前供包台窗口。" : "编辑授权未确认或已结束，输入保留。重新验证后继续；供包状态保持原状。";
        if (!model.Fresh || model.Snapshot is not { } snapshot)
        { ReadOnlySettings.Text = "尚未取得有效设置，请等待工作站状态读取。"; return; }
        var c = snapshot.Configuration.Effective;
        var host = c.Wcs is { } rcs && Uri.TryCreate(rcs.Target, UriKind.Absolute, out var uri) ?
            new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri.GetLeftPart(UriPartial.Authority) : "待配置";
        ReadOnlySettings.Text = "本站：" + c.StationId + "\n供包台编号：" + c.StationCode + "\nRCS 主机：" + host +
            "\n配置修订：" + snapshot.Configuration.Revision + (snapshot.HasPendingConfiguration == true ? " · 有已保存待生效设置" : " · 无待生效设置") +
            string.Join("", (c.Topology?.Positions ?? []).Select(p =>
            {
                var reader = c.Topology!.Readers.Single(r => r.DeviceId == p.ReaderDeviceId);
                return "\n\n" + (p.Id == "left" ? "左相机" : "右相机") + "\nIP：" + (reader.Parameters?.GetValueOrDefault("device.ip") is { Length: > 0 } ip ? ip : "未配置") +
                    "\n物理序列号：" + (reader.Parameters?.GetValueOrDefault("device.serial") is { Length: > 0 } serial ? serial : "未配置") +
                    "\nRCS 相机编码：" + (c.Wcs?.Parameters.GetValueOrDefault(p.Id + "-bcr-code") is { Length: > 0 } code ? code : "本位停用");
            }));
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
            if (password.EnteredSessionId is { Length: > 0 } grantedId) session = grantedId;
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
            ConfigurationCloseGuard.CommitAndFindInvalid(editor);
            await model.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (session is not null) await model.ServiceAsync(() => authority.ExitManagementModeAsync(session));
            await model.RefreshManagementModeAsync();
            closeReady = true; Close();
        }
        catch (Exception error) { model.Message = "设置窗口尚未关闭，原授权和输入保留：" + ChineseInterfaceText.ParameterMessage(error.Message, "授权结束或原操作收尾未确认，请再次关闭重试。"); }
        finally { closing = false; IsEnabled = true; }
    }
    void Close_Click(object sender, RoutedEventArgs e) => Close();
    void Return_Click(object sender, RoutedEventArgs e) => (Owner as SupplyStationWindow)?.ReturnToWorkstation();
}
