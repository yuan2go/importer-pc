using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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
        Title = "管理模式"; Width = 480; Height = 440; MinWidth = 360; MinHeight = 320; MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 40);
        FontSize = 16; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { Margin = new(24) }; layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); Content = layout;
        var body = new StackPanel(); layout.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        body.Children.Add(new TextBlock { Text = "管理模式", FontSize = 24, FontWeight = FontWeights.SemiBold });
        var state = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 16) }; body.Children.Add(state);
        var legacy = new StackPanel { Visibility = Visibility.Collapsed }; body.Children.Add(legacy);
        legacy.Children.Add(new TextBlock { Text = "旧版凭据转换（仅升级一次）", FontWeight = FontWeights.SemiBold });
        legacy.Children.Add(new TextBlock { Text = "验证原凭据后设置统一管理密码，原账号账保留为历史。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 12) });
        var oldName = new TextBox { Name = "LegacyManagementAccount", MaxLength = 64, MinHeight = 44 }; legacy.Children.Add(new Label { Content = "旧版账号", Target = oldName, Padding = new(0) }); legacy.Children.Add(oldName);
        var oldProof = new PasswordBox { Name = "LegacyManagementPassword", MaxLength = 256, MinHeight = 44 }; legacy.Children.Add(new Label { Content = "旧版密码", Target = oldProof, Padding = new(0), Margin = new(0, 8, 0, 0) }); legacy.Children.Add(oldProof);
        var passwordFields = new StackPanel(); body.Children.Add(passwordFields);
        var proof = new PasswordBox { Name = "ManagementPassword", MaxLength = 256, MinHeight = 44 };
        var proofLabel = new Label { Content = "管理密码", Target = proof, Padding = new(0), Margin = new(0, 8, 0, 0) }; passwordFields.Children.Add(proofLabel); passwordFields.Children.Add(proof);
        var confirm = new PasswordBox { Name = "ManagementPasswordConfirmation", MaxLength = 256, MinHeight = 44 };
        var confirmation = new StackPanel(); confirmation.Children.Add(new Label { Content = "再次输入管理密码", Target = confirm, Padding = new(0), Margin = new(0, 8, 0, 0) }); confirmation.Children.Add(confirm); passwordFields.Children.Add(confirmation);
        var rules = new TextBlock { Text = "新密码：12～256个字符，至少5种不同字符。请妥善保管。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 12) }; passwordFields.Children.Add(rules);
        var changeBody = new StackPanel(); var changeTools = new Expander { Header = "修改管理密码", Content = changeBody, Visibility = Visibility.Collapsed, Margin = new(0, 12, 0, 0) }; body.Children.Add(changeTools);
        var next = new PasswordBox { Name = "NewManagementPassword", MaxLength = 256, MinHeight = 44 }; changeBody.Children.Add(new Label { Content = "新管理密码", Target = next, Padding = new(0) }); changeBody.Children.Add(next);
        var nextConfirm = new PasswordBox { Name = "NewManagementPasswordConfirmation", MaxLength = 256, MinHeight = 44 }; changeBody.Children.Add(new Label { Content = "再次输入新密码", Target = nextConfirm, Padding = new(0), Margin = new(0, 8, 0, 0) }); changeBody.Children.Add(nextConfirm);
        changeBody.Children.Add(new TextBlock { Text = rules.Text + " 修改后退出管理模式，原授权立即撤销。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 12) });
        var save = new Button { Name = "ChangeManagementPassword", Content = "修改密码并退出管理模式", MinHeight = 44 }; changeBody.Children.Add(save);
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 12) }; AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite); Grid.SetRow(feedback, 1); layout.Children.Add(feedback);
        var actions = new WrapPanel(); Grid.SetRow(actions, 2); layout.Children.Add(actions);
        var enterButton = new Button { Name = "EnterManagementMode", Content = "进入管理模式", MinHeight = 44, MinWidth = 140, IsDefault = true }; actions.Children.Add(enterButton);
        var leave = new Button { Name = "ExitManagementMode", Content = "退出管理模式", MinHeight = 44, Margin = new(0, 0, 12, 0) }; actions.Children.Add(leave);
        var cancel = new Button { Content = "关闭", MinHeight = 44, MinWidth = 88, Margin = new(12, 0, 0, 0), IsCancel = true }; actions.Children.Add(cancel);
        bool busy = false, refreshing = false, closeAfter = false; ManagementModeStatus current = new(false, false, null);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        async Task Refresh()
        {
            current = await status();
            state.Text = !current.Available ? "管理模式服务不可用，请使用同版安装器修复。作业和原输入保留。" : current.Authorized ? "管理模式已开启 · " + current.ExpiresAt?.ToLocalTime().ToString("HH:mm") + " 自动退出\n操作员账号和在途作业保持。" : current.NeedsLegacyConversion ? "此部署有旧版凭据，先完成一次性转换。" : current.Configured ? "输入管理密码以修改参数或查询日志。15分钟后自动退出，不改变操作员账号。" : "首次使用：设置管理密码，防止误改参数。";
            var preferredHeight = current.NeedsLegacyConversion ? 680 : current.Authorized ? 580 : current.Configured ? 420 : 520; if (Height != preferredHeight) Height = preferredHeight;
            passwordFields.Visibility = current.Authorized ? Visibility.Collapsed : Visibility.Visible; passwordFields.IsEnabled = current.Available; legacy.IsEnabled = current.Available;
            confirmation.Visibility = !current.Configured ? Visibility.Visible : Visibility.Collapsed; rules.Visibility = confirmation.Visibility;
            legacy.Visibility = current.NeedsLegacyConversion ? Visibility.Visible : Visibility.Collapsed;
            proofLabel.Content = current.NeedsLegacyConversion ? "新管理密码" : "管理密码";
            enterButton.Content = current.NeedsLegacyConversion ? "转换并进入管理模式" : current.Configured ? "进入管理模式" : "设置并进入管理模式";
            enterButton.Visibility = current.Authorized ? Visibility.Collapsed : Visibility.Visible; enterButton.IsEnabled = current.Available;
            leave.Visibility = current.Authorized ? Visibility.Visible : Visibility.Collapsed; changeTools.Visibility = leave.Visibility;
        }
        async Task Run(Func<Task> action)
        {
            if (busy) return; busy = true; body.IsEnabled = false; actions.IsEnabled = false;
            try { await action(); feedback.Text = "操作完成。"; await Refresh(); }
            catch (Exception error) { closeAfter = false; feedback.Text = error.Message; }
            finally { proof.Clear(); confirm.Clear(); oldProof.Clear(); next.Clear(); nextConfirm.Clear(); busy = false; body.IsEnabled = true; actions.IsEnabled = true; if (closeAfter) Close(); }
        }
        enterButton.Click += async (_, _) =>
        {
            var password = proof.Password; var repeated = confirm.Password; var oldPassword = oldProof.Password;
            await Run(async () =>
            {
                var fresh = await status();
                if (!fresh.Configured && password != repeated) throw new InvalidDataException("两次管理密码不一致，请重新输入。");
                if (fresh.NeedsLegacyConversion) { if (!await convert(oldName.Text, oldPassword, password)) throw new UnauthorizedAccessException("旧版凭据不正确，未转换或覆盖原密码。"); }
                else if (!fresh.Configured) await configure(password);
                if (!await enter(password)) throw new UnauthorizedAccessException("管理密码不正确。");
                closeAfter = true;
            });
        };
        leave.Click += async (_, _) => await Run(async () => { await exit(); closeAfter = true; });
        save.Click += async (_, _) => { var password = next.Password; var repeated = nextConfirm.Password; await Run(async () => { if (password != repeated) throw new InvalidDataException("两次新管理密码不一致。"); await change(password); closeAfter = true; }); };
        Loaded += async (_, _) => { try { await Refresh(); proof.Focus(); Keyboard.Focus(proof); } catch (Exception error) { feedback.Text = error.Message; enterButton.IsEnabled = false; } timer.Start(); };
        timer.Tick += async (_, _) => { if (busy || refreshing) return; refreshing = true; try { await Refresh(); } catch (Exception error) { changeTools.Visibility = Visibility.Collapsed; enterButton.IsEnabled = false; feedback.Text = error.Message; } finally { refreshing = false; } };
        Closing += (_, e) => e.Cancel = busy;
        Closed += (_, _) => { proof.Clear(); confirm.Clear(); oldProof.Clear(); next.Clear(); nextConfirm.Clear(); timer.Stop(); };
    }
}
