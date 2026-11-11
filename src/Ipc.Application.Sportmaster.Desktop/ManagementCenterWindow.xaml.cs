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
    readonly Action<string> navigate;
    readonly Func<Task> authenticate, renew, password;
    public ManagementCenterWindow(SupplyStationViewModel model, Action<string> navigate, Func<Task> authenticate, Func<Task> renew, Func<Task> password)
    {
        InitializeComponent(); UiTypography.Apply(this);
        DataContext = model; this.navigate = navigate; this.authenticate = authenticate; this.renew = renew; this.password = password;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width); Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        UpdateAuthorization(model.ManagementStatus);
    }
    public void ShowPage(FrameworkElement page) { if (!ReferenceEquals(ManagementPageHost.Content, page)) ManagementPageHost.Content = page; }
    public void DetachPage() => ManagementPageHost.Content = null;
    public void UpdateAuthorization(ManagementModeStatus status)
    {
        AuthorizedBody.Visibility = status.Authorized ? Visibility.Visible : Visibility.Collapsed;
        AuthorizationGate.Visibility = status.Authorized ? Visibility.Collapsed : Visibility.Visible;
        ManagementNavigation.IsEnabled = RenewAuthorization.IsEnabled = PasswordSettings.IsEnabled = status.Authorized;
        AuthorizationTitle.Text = status.Available ? "请重新验证管理密码" : "暂时无法核对管理授权";
        AuthorizationDetail.Text = status.Available ? "管理授权已结束，当前页面已锁定，未保存的设置保留。关闭管理中心后恢复供包。" : "授权服务通信未确认，管理操作暂时不可用；正在重新核对原授权，无需重复输入密码。自动供包及读码保持暂停。";
        AuthenticateAuthorization.Content = status.Available ? "验证管理密码" : "重新检查授权";
        AuthorizationStatus.Text = closeFailure ?? (status.Authorized ? $"授权至 {status.ExpiresAt?.ToLocalTime():HH:mm} · 关闭窗口后结束授权" : "管理授权已结束；可验证密码后继续。");
    }
    public void SetClosing(bool value)
    {
        IsEnabled = !value;
        if (value) { closeFailure = null; AuthorizationStatus.ToolTip = null; AuthorizationStatus.Text = "正在结束管理操作，请稍候…"; System.Windows.Automation.AutomationProperties.SetItemStatus(AuthorizationStatus, "Closing"); }
    }
    public void ShowCloseFailure(string reason, string detail)
    {
        AuthorizationStatus.Text = closeFailure = "管理窗口尚未关闭，原资源及未保存设置保留。请处理原因后再次关闭：" + reason;
        AuthorizationStatus.ToolTip = detail;
        System.Windows.Automation.AutomationProperties.SetItemStatus(AuthorizationStatus, "CloseFailed");
    }
    void Navigate_Click(object sender, RoutedEventArgs e) => navigate((string)((Button)sender).Tag);
    async void Authenticate_Click(object sender, RoutedEventArgs e) => await authenticate();
    async void Renew_Click(object sender, RoutedEventArgs e) => await renew();
    async void Password_Click(object sender, RoutedEventArgs e) => await password();
    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
