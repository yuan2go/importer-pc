using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed class ManagementModeWindow : Window
{
    public ManagementModeWindow(IWorkstationAdministration service) : this(service.ManagementModeStatusAsync, service.ConfigureManagementPasswordAsync,
        service.EnterManagementModeAsync, service.ExitManagementModeAsync, service.ChangeManagementPasswordAsync, service.ConvertLegacyManagementCredentialsAsync) { }
    public ManagementModeWindow(IManagementMode service) : this(service.ManagementModeStatusAsync, service.ConfigureManagementPasswordAsync,
        service.EnterManagementModeAsync, service.ExitManagementModeAsync, service.ChangeManagementPasswordAsync, service.ConvertLegacyManagementCredentialsAsync) { }
    ManagementModeWindow(Func<Task<ManagementModeStatus>> status, Func<string, Task> configure, Func<string, Task<bool>> enter,
        Func<Task> exit, Func<string, Task> change, Func<string, string, string, Task<bool>> convert)
    {
        Title = "管理模式"; Width = 520; Height = 480; MinWidth = 360; MinHeight = 280;
        StartupAppearance.Apply(this); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { Margin = new(24, 20, 24, 20) }; layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); Content = layout;
        var body = new StackPanel(); layout.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var heading = new TextBlock { Text = "管理模式", FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis }; body.Children.Add(heading);
        var state = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 12) }; body.Children.Add(state);
        var legacy = new StackPanel { Visibility = Visibility.Collapsed }; body.Children.Add(legacy);
        legacy.Children.Add(new TextBlock { Text = "旧版凭据转换（仅升级一次）", FontWeight = UiTypography.Emphasis });
        legacy.Children.Add(new TextBlock { Text = "验证原凭据后设置统一管理密码，原账号账保留为历史。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 12) });
        var oldName = new TextBox { Name = "LegacyManagementAccount", MaxLength = 64, MinHeight = 44 }; legacy.Children.Add(new Label { Content = "旧版账号", Target = oldName, Padding = new(0) }); legacy.Children.Add(oldName);
        var oldProof = new PasswordBox { Name = "LegacyManagementPassword", MaxLength = 256, MinHeight = 44 }; legacy.Children.Add(new Label { Content = "旧版密码", Target = oldProof, Padding = new(0), Margin = new(0, 8, 0, 0) }); legacy.Children.Add(oldProof);
        var passwordFields = new StackPanel(); body.Children.Add(passwordFields);
        var proof = new PasswordBox { Name = "ManagementPassword", MaxLength = 256, MinHeight = 44 };
        var proofLabel = new Label { Content = "管理密码", Target = proof, Padding = new(0), Margin = new(0, 8, 0, 0) }; passwordFields.Children.Add(proofLabel); passwordFields.Children.Add(proof);
        var confirm = new PasswordBox { Name = "ManagementPasswordConfirmation", MaxLength = 256, MinHeight = 44 };
        var confirmation = new StackPanel(); confirmation.Children.Add(new Label { Content = "确认密码", Target = confirm, Padding = new(0), Margin = new(0, 12, 0, 4) }); confirmation.Children.Add(confirm); passwordFields.Children.Add(confirmation);
        var rules = new TextBlock { Text = "5–256 个字符，可使用纯数字。", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(98, 107, 120)), FontSize = UiTypography.Label, Margin = new(0, 8, 0, 12) }; passwordFields.Children.Add(rules);
        var changeBody = new StackPanel(); var changeTools = new Expander { Header = "修改管理密码", Content = changeBody, Visibility = Visibility.Collapsed, Margin = new(0, 12, 0, 0) }; body.Children.Add(changeTools);
        var next = new PasswordBox { Name = "NewManagementPassword", MaxLength = 256, MinHeight = 44 }; changeBody.Children.Add(new Label { Content = "新管理密码", Target = next, Padding = new(0) }); changeBody.Children.Add(next);
        var nextConfirm = new PasswordBox { Name = "NewManagementPasswordConfirmation", MaxLength = 256, MinHeight = 44 }; changeBody.Children.Add(new Label { Content = "再次输入新密码", Target = nextConfirm, Padding = new(0), Margin = new(0, 8, 0, 0) }); changeBody.Children.Add(nextConfirm);
        changeBody.Children.Add(new TextBlock { Text = rules.Text + " 修改后退出管理模式，原授权立即撤销。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 12) });
        var save = new Button { Name = "ChangeManagementPassword", Content = "修改密码并退出管理模式", MinHeight = 44 }; changeBody.Children.Add(save);
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 12) }; AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite); Grid.SetRow(feedback, 1); layout.Children.Add(feedback);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetRow(actions, 2); layout.Children.Add(actions);
        var enterButton = new Button { Name = "EnterManagementMode", Content = "进入管理模式", MinHeight = 44, MinWidth = 140, IsDefault = true }; actions.Children.Add(enterButton);
        StartupAppearance.Primary(enterButton); StartupAppearance.Primary(save); feedback.Foreground = Brushes.Firebrick;
        var leave = new Button { Name = "ExitManagementMode", Content = "退出管理模式", MinHeight = 44, Margin = new(0, 0, 12, 0) }; actions.Children.Add(leave);
        var cancel = new Button { Content = "取消", MinHeight = 44, MinWidth = 88, Margin = new(12, 0, 0, 0), IsCancel = true }; actions.Children.Insert(0, cancel); cancel.Margin = new(0, 0, 12, 0); cancel.Click += (_, _) => Close();
        var show = new CheckBox { Content = "显示密码", Margin = new(0, 8, 0, 0) }; passwordFields.Children.Add(show);
        var revealed = new List<(PasswordBox Secret, TextBox Plain)>();
        foreach (var secret in new[] { proof, confirm, oldProof, next, nextConfirm })
        {
            var parent = (Panel)secret.Parent; var index = parent.Children.IndexOf(secret); parent.Children.Remove(secret);
            var host = new Grid(); var plain = new TextBox { MaxLength = 256, Visibility = Visibility.Collapsed, MinHeight = 44 }; AutomationProperties.SetName(plain, "显示" + secret.Name);
            host.Children.Add(secret); host.Children.Add(plain); parent.Children.Insert(index, host); revealed.Add((secret, plain));
            plain.TextChanged += (_, _) => { if (plain.IsVisible) secret.Password = plain.Text; };
        }
        void Conceal()
        {
            foreach (var pair in revealed) { pair.Plain.Visibility = Visibility.Collapsed; pair.Secret.Visibility = Visibility.Visible; pair.Plain.Clear(); }
            show.IsChecked = false;
        }
        void ClearSecrets() { Conceal(); foreach (var pair in revealed) pair.Secret.Clear(); }
        show.Checked += (_, _) => { foreach (var pair in revealed) { pair.Plain.Text = pair.Secret.Password; pair.Secret.Visibility = Visibility.Collapsed; pair.Plain.Visibility = Visibility.Visible; } };
        show.Unchecked += (_, _) => Conceal();
        body.IsEnabled = false; enterButton.IsEnabled = false; leave.IsEnabled = false;
        bool busy = false, refreshing = false, closeAfter = false; double lastPreferredHeight = 0; ManagementModeStatus current = new(false, false, null);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        async Task Refresh()
        {
            var previous = current; current = await status();
            if (previous.Authorized && !current.Authorized || previous.Configured != current.Configured || !current.Available) ClearSecrets();
            Title = heading.Text = !current.Available ? "管理模式不可用" : current.Authorized ? "管理模式" : current.NeedsLegacyConversion ? "转换旧版管理凭据" : current.Configured ? "验证管理密码" : "设置管理密码";
            state.Text = !current.Available ? "管理模式服务不可用，请使用同版安装器修复。原输入保留。" : current.Authorized ? "管理模式已开启 · " + current.ExpiresAt?.ToLocalTime().ToString("HH:mm") + " 自动退出\n操作员账号和在途作业保持。" : current.NeedsLegacyConversion ? "此部署有旧版凭据，先完成一次性转换。" : current.Configured ? "输入管理密码以继续配置。管理模式 15 分钟后自动退出。" : "用于保护参数设置，请妥善保管。";
            var preferredHeight = current.NeedsLegacyConversion ? 680 : current.Authorized ? changeTools.IsExpanded ? 640 : 340 : current.Configured ? 380 : 480;
            if (lastPreferredHeight != preferredHeight) { Height = Math.Min(preferredHeight, MaxHeight); lastPreferredHeight = preferredHeight; }
            body.IsEnabled = true; leave.IsEnabled = current.Available;
            passwordFields.Visibility = current.Authorized ? Visibility.Collapsed : Visibility.Visible; passwordFields.IsEnabled = current.Available; legacy.IsEnabled = current.Available;
            confirmation.Visibility = !current.Configured ? Visibility.Visible : Visibility.Collapsed; rules.Visibility = confirmation.Visibility;
            legacy.Visibility = current.NeedsLegacyConversion ? Visibility.Visible : Visibility.Collapsed;
            proofLabel.Content = current.NeedsLegacyConversion ? "新管理密码" : "管理密码";
            enterButton.Content = current.NeedsLegacyConversion ? "转换并进入管理模式" : current.Configured ? "进入管理模式" : "设置并继续";
            enterButton.Visibility = current.Authorized ? Visibility.Collapsed : Visibility.Visible; enterButton.IsEnabled = current.Available;
            leave.Visibility = current.Authorized ? Visibility.Visible : Visibility.Collapsed; changeTools.Visibility = leave.Visibility;
        }
        async Task Run(Func<Task> action)
        {
            if (busy) return; busy = true; body.IsEnabled = false; actions.IsEnabled = false;
            try { await action(); feedback.Text = ""; await Refresh(); }
            catch (Exception error) { closeAfter = false; feedback.Text = error.Message; }
            finally { ClearSecrets(); busy = false; body.IsEnabled = true; actions.IsEnabled = true; if (closeAfter) Close(); else if (proof.IsVisible) proof.Focus(); }
        }
        enterButton.Click += async (_, _) =>
        {
            var password = proof.Password; var repeated = confirm.Password; var oldPassword = oldProof.Password;
            await Run(async () =>
            {
                var fresh = await status();
                if (!fresh.Available) throw new UnauthorizedAccessException("管理服务不可用，请检查安装状态。");
                if (!fresh.Configured && password != repeated) throw new InvalidDataException("两次管理密码不一致，请重新输入。");
                if (fresh.NeedsLegacyConversion) { if (!await convert(oldName.Text, oldPassword, password)) throw new UnauthorizedAccessException("旧版凭据不正确，未转换或覆盖原密码。"); }
                else if (!fresh.Configured)
                {
                    feedback.Text = "正在设置管理密码；如出现 Windows 授权窗口，请确认。";
                    await configure(password);
                }
                if (!await enter(password)) throw new UnauthorizedAccessException("管理密码不正确，请重新输入。");
                if (!(await status()).Authorized) throw new UnauthorizedAccessException("管理授权尚未生效，请重新验证后继续。");
                closeAfter = true;
            });
        };
        leave.Click += async (_, _) => await Run(async () => { await exit(); closeAfter = true; });
        save.Click += async (_, _) => { var password = next.Password; var repeated = nextConfirm.Password; await Run(async () => { if (password != repeated) throw new InvalidDataException("两次新管理密码不一致。"); await change(password); closeAfter = true; }); };
        Loaded += async (_, _) => { try { await Refresh(); proof.Focus(); Keyboard.Focus(proof); } catch (Exception error) { feedback.Text = error.Message; enterButton.IsEnabled = false; } timer.Start(); };
        timer.Tick += async (_, _) => { if (busy || refreshing) return; refreshing = true; try { await Refresh(); } catch (Exception error) { ClearSecrets(); body.IsEnabled = false; changeTools.Visibility = Visibility.Collapsed; enterButton.IsEnabled = false; leave.IsEnabled = false; feedback.Text = error.Message; } finally { refreshing = false; } };
        Closing += (_, e) => e.Cancel = busy;
        Closed += (_, _) => { ClearSecrets(); timer.Stop(); };
    }
}
