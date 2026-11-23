using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

public sealed class ManagementTabSelectedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => values.Length == 2 && values[0] is string page && Equals(page, values[1]);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public partial class ManagementCenterWindow : Window
{
    string? closeFailure;
    bool workspaceReleased, workspaceReady, authorizing;
    public void LockAfterWorkspaceRelease() { workspaceReleased = true; UpdateAuthorization(((SupplyStationViewModel)DataContext).ManagementStatus); }
    readonly Action<string> navigate;
    readonly Func<Task> authenticate, renew, password;
    public ManagementCenterWindow(SupplyStationViewModel model, Action<string> navigate, Func<Task> authenticate, Func<Task> renew, Func<Task> password)
    {
        InitializeComponent(); UiTypography.Apply(this);
        DataContext = model; this.navigate = navigate; this.authenticate = authenticate; this.renew = renew; this.password = password;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width); Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        UpdateAuthorization(model.ManagementStatus);
    }
    public void ShowPage(FrameworkElement page, FrameworkElement? actions = null)
    {
        if (!ReferenceEquals(ManagementPageHost.Content, page)) ManagementPageHost.Content = page;
        ParameterActions.Content = actions;
    }
    public void DetachPage() { ManagementPageHost.Content = null; ParameterActions.Content = null; }
    public void UpdateAuthorization(ManagementModeStatus status)
    {
        if (workspaceReleased) status = status with { Active = false };
        bool canUseWorkspace = status.Authorized && workspaceReady;
        AuthorizedBody.Visibility = canUseWorkspace ? Visibility.Visible : Visibility.Collapsed;
        AuthorizationGate.Visibility = canUseWorkspace ? Visibility.Collapsed : Visibility.Visible;
        RenewAuthorization.IsEnabled = PasswordSettings.IsEnabled = canUseWorkspace;
        ParameterActions.Visibility = canUseWorkspace && ParameterActions.Content is not null ? Visibility.Visible : Visibility.Collapsed;
        AuthorizationStatus.Visibility = ParameterActions.Visibility == Visibility.Visible && closeFailure is null ? Visibility.Collapsed : Visibility.Visible;
        var page = ((SupplyStationViewModel)DataContext).SelectedPage;
        ConnectAuthorization.Visibility = status.Authorized ? Visibility.Collapsed : Visibility.Visible;
        ConnectAuthorization.IsEnabled = !workspaceReleased && !authorizing;
        AuthorizationTitle.Text = !status.Available ? "暂时无法核对管理授权" : "需要验证管理密码建立控制通道";
        AuthorizationDetail.Text = status.Available ? page switch
        {
            "debug" => "验证后可选择相机并执行调试扫码。",
            "maintenance" => "验证后可读取和编辑专业参数；原有草稿保留。",
            _ => "验证后可查看运行状态、日志及维护工具。"
        } : "授权服务通信未确认，正在重新核对原授权。";
        AuthenticateAuthorization.Content = status.Available ? "验证管理密码" : "重新检查授权";
        AuthenticateAuthorization.IsEnabled = !workspaceReleased && !status.Authorized && !authorizing;
        AuthorizationStatus.Text = closeFailure ?? (status.Authorized ? $"授权至 {status.ExpiresAt?.ToLocalTime():HH:mm} · 关闭窗口后结束授权" : "管理授权未确认");
    }
    public void ShowWorkspaceState(bool paused)
    { workspaceReady = paused; WorkspaceState.Visibility = paused ? Visibility.Visible : Visibility.Collapsed; }
    public void SetClosing(bool value)
    {
        IsEnabled = !value;
        if (value) { closeFailure = null; AuthorizationStatus.ToolTip = null; AuthorizationStatus.Text = "正在结束管理操作，请稍候…"; System.Windows.Automation.AutomationProperties.SetItemStatus(AuthorizationStatus, "Closing"); }
    }
    public void ShowCloseFailure(string reason, string detail)
    {
        AuthorizationStatus.Visibility = Visibility.Visible;
        AuthorizationStatus.Text = closeFailure = "管理窗口尚未关闭，原资源及未保存设置保留。请处理原因后再次关闭：" + reason;
        AuthorizationStatus.ToolTip = detail;
        System.Windows.Automation.AutomationProperties.SetItemStatus(AuthorizationStatus, "CloseFailed");
    }
    void Navigate_Click(object sender, RoutedEventArgs e) => navigate((string)((Button)sender).Tag);
    async void Authenticate_Click(object sender, RoutedEventArgs e)
    {
        if (authorizing) return;
        authorizing = true;
        ConnectAuthorization.IsEnabled = AuthenticateAuthorization.IsEnabled = false;
        try { await authenticate(); }
        finally { authorizing = false; UpdateAuthorization(((SupplyStationViewModel)DataContext).ManagementStatus); }
    }
    async void Renew_Click(object sender, RoutedEventArgs e) => await renew();
    async void Password_Click(object sender, RoutedEventArgs e) => await password();
    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
