using System.IO;
using Ipc.Platform;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed class ManagementPasswordRecoveryWindow : Window
{
    public ManagementPasswordRecoveryWindow(Func<string, Task> reset, Func<Task<ManagementPasswordRecoveryReceipt?>>? inspect = null)
    {
        Title = "重置管理密码"; Width = 520; Height = 500; MinWidth = 360; MinHeight = 320;
        StartupAppearance.Apply(this); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { Margin = new(24, 20, 24, 20) };
        layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); Content = layout;
        var body = new StackPanel(); layout.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        body.Children.Add(new TextBlock { Text = "重置管理密码", FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis });
        body.Children.Add(new TextBlock { Text = "需要此电脑的Windows管理员确认。仅重置本站管理密码，不修改设备配置或业务记录。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 12) });
        PasswordBox Field(string name, string label)
        {
            var input = new PasswordBox { Name = name, MaxLength = 256, MinHeight = 44 };
            body.Children.Add(new Label { Content = label, Target = input, Padding = new(0), Margin = new(0, 8, 0, 4) }); body.Children.Add(input);
            AutomationProperties.SetName(input, label); return input;
        }
        var next = Field("RecoveryNewPassword", "新管理密码");
        var confirmation = Field("RecoveryConfirmation", "再次输入新密码");
        body.Children.Add(new TextBlock { Text = "5–256 个字符，可使用纯数字。已有管理授权将在重置后退出。", FontSize = UiTypography.Label, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) }.Themed(TextBlock.ForegroundProperty, "Danger");
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite); Grid.SetRow(feedback, 1); layout.Children.Add(feedback);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetRow(actions, 2); layout.Children.Add(actions);
        var cancel = new Button { Content = "取消", IsCancel = true, MinHeight = 44, MinWidth = 88, Margin = new(0, 0, 12, 0) };
        var submit = new Button { Name = "ResetManagementPassword", Content = "重置管理密码", MinHeight = 44, MinWidth = 140, IsDefault = true };
        StartupAppearance.Primary(submit); actions.Children.Add(cancel); actions.Children.Add(submit);
        bool busy = false;
        var inspectButton = new Button { Content = "核对上次重置", MinHeight = 44, Margin = new(0, 0, 12, 0), Visibility = inspect is null ? Visibility.Collapsed : Visibility.Visible };
        actions.Children.Insert(0, inspectButton);
        inspectButton.Click += async (_, _) =>
        {
            if (busy || inspect is null) return;
            busy = true; body.IsEnabled = actions.IsEnabled = false;
            try
            {
                var result = await inspect();
                feedback.Text = result is null ? "没有需要核对的本次重置。" : result.Completed ? "上次密码重置已完成，请使用该次新密码重新进入管理模式。" :
                    result.CanRetry ? "上次未完成且确认已失效，可以重新发起。" : "尚未取得完成事实，请等待后再次核对；不要重复重置。";
            }
            catch (Exception error) { feedback.Text = ChineseInterfaceText.Message(error.Message); }
            finally { busy = false; body.IsEnabled = actions.IsEnabled = true; }
        };
        foreach (var input in new[] { next, confirmation }) input.PasswordChanged += (_, _) => { if (!busy) feedback.Text = ""; };
        void Clear() { next.Clear(); confirmation.Clear(); }
        cancel.Click += (_, _) => Close();
        submit.Click += async (_, _) =>
        {
            if (busy) return; busy = true; body.IsEnabled = actions.IsEnabled = false;
            var replacement = next.Password; var repeated = confirmation.Password;
            try
            {
                if (replacement != repeated) throw new InvalidDataException("两次新管理密码不一致。");
                if (replacement.Length is < 5 or > 256 || replacement.Any(char.IsControl)) throw new InvalidDataException("管理密码须为5～256个字符，不能包含控制字符。");
                feedback.Text = "正在请求Windows管理员确认，请在授权窗口核对本站。";
                await reset(replacement); DialogResult = true;
            }
            catch (Exception error) { feedback.Text = ChineseInterfaceText.Message(error.Message); }
            finally { Clear(); busy = false; body.IsEnabled = actions.IsEnabled = true; next.Focus(); }
        };
        Loaded += (_, _) => { next.Focus(); Keyboard.Focus(next); };
        Closing += (_, e) => e.Cancel = busy && DialogResult != true;
        Closed += (_, _) => Clear();
    }
}
