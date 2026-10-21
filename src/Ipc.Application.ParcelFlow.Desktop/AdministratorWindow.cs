using System.Windows;
using System.Windows.Controls;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed class AdministratorWindow : Window
{
    public AdministratorWindow(IWorkstationAdministration service) : this(service.AdministratorStatusAsync, service.ProvisionAdministratorAsync,
        service.LoginAdministratorAsync, service.LogoutAdministratorAsync, service.AdministratorAccountsAsync, service.MaintainAdministratorAsync) { }
    public AdministratorWindow(LocalAdministrator service) : this(() => Task.FromResult(service.Status()),
        (a, p) => Task.Run(() => service.Provision(a, p)), (a, p) => Task.Run(() => service.Login(a, p)),
        () => Task.Run(service.Logout), () => Task.Run(service.Accounts), (a, p, e) => Task.Run(() => service.Maintain(a, p, e))) { }
    AdministratorWindow(Func<Task<AdministratorStatus>> status, Func<string, string, Task> provision, Func<string, string, Task<bool>> login,
        Func<Task> logout, Func<Task<AdministratorAccount[]>> accounts, Func<string, string?, bool, Task> maintain)
    {
        Title = "软件管理员"; Width = 560; Height = 610; MinWidth = 440; MinHeight = 360; FontSize = 16; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Margin = new(24) }; Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(new TextBlock { Text = "软件管理员", FontSize = 24, FontWeight = FontWeights.SemiBold });
        var state = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 20) }; body.Children.Add(state);
        body.Children.Add(new TextBlock { Text = "账号" }); var name = new TextBox { Name = "AdministratorAccount", MaxLength = 64, MinHeight = 44 }; body.Children.Add(name);
        body.Children.Add(new TextBlock { Text = "密码", Margin = new(0, 12, 0, 0) }); var proof = new PasswordBox { Name = "AdministratorPassword", MaxLength = 256, MinHeight = 44 }; body.Children.Add(proof);
        body.Children.Add(new TextBlock { Text = "首次开通和新密码：12～256个字符，至少5种不同字符。会话15分钟后到期。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 12) });
        var actions = new WrapPanel(); body.Children.Add(actions);
        var enter = new Button { Name = "AdministratorLogin", Content = "登录", MinHeight = 44, MinWidth = 112 }; var leave = new Button { Content = "退出管理员", MinHeight = 44, Margin = new(12, 0, 0, 0) }; actions.Children.Add(enter); actions.Children.Add(leave);
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 12) }; body.Children.Add(feedback);
        var tools = new StackPanel(); var expand = new Expander { Header = "管理员账号维护", Content = tools, Margin = new(0, 12, 0, 0) }; body.Children.Add(expand);
        var list = new ListBox { MaxHeight = 120, DisplayMemberPath = "Name" }; tools.Children.Add(list);
        tools.Children.Add(new TextBlock { Text = "选择账号，或在上方填写新账号与新密码。留空密码只改变启用状态。", TextWrapping = TextWrapping.Wrap });
        var enabled = new CheckBox { Content = "启用此管理员", IsChecked = true, MinHeight = 44 }; tools.Children.Add(enabled);
        var save = new Button { Content = "保存账号更改并退出管理员", MinHeight = 44 }; tools.Children.Add(save);
        bool busy = false, refreshing = false;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        async Task Refresh(bool reloadAccounts = true)
        {
            var s = await status(); state.Text = s.Authorized ? "当前账号：" + s.Account + " · 管理员\n到期：" + s.ExpiresAt?.ToLocalTime().ToString("HH:mm") : s.Provisioned ? "未登录管理员；操作员身份和在途作业保持。" : "首次使用：请开通首个软件管理员。请妥善保管密码。";
            enter.Content = s.Provisioned ? "登录" : "开通并登录"; leave.IsEnabled = s.Authorized; expand.Visibility = s.Authorized ? Visibility.Visible : Visibility.Collapsed;
            if (s.Authorized && reloadAccounts) list.ItemsSource = await accounts();
        }
        async Task Run(Func<Task> action)
        {
            if (busy) return; busy = true; body.IsEnabled = false;
            try { await action(); feedback.Text = "操作完成。"; await Refresh(); }
            catch (Exception e) { feedback.Text = e.Message; }
            finally { proof.Clear(); busy = false; body.IsEnabled = true; }
        }
        enter.Click += async (_, _) => { var p = proof.Password; proof.Clear(); await Run(async () => { if (!(await status()).Provisioned) await provision(name.Text, p); if (!await login(name.Text, p)) throw new UnauthorizedAccessException("账号或密码不正确。"); }); };
        leave.Click += async (_, _) => await Run(logout);
        save.Click += async (_, _) => { var p = proof.Password; proof.Clear(); await Run(() => maintain(name.Text, p.Length == 0 ? null : p, enabled.IsChecked == true)); };
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is AdministratorAccount a) { name.Text = a.Name; enabled.IsChecked = a.Enabled; } };
        Loaded += async (_, _) => { try { await Refresh(); } catch (Exception e) { feedback.Text = e.Message; } };
        timer.Tick += async (_, _) => { if (busy || refreshing) return; refreshing = true; try { await Refresh(false); } catch (Exception e) { expand.Visibility = Visibility.Collapsed; feedback.Text = e.Message; } finally { refreshing = false; } };
        Loaded += (_, _) => timer.Start(); Closing += (_, e) => e.Cancel = busy; Closed += (_, _) => { proof.Clear(); timer.Stop(); };
    }
}
