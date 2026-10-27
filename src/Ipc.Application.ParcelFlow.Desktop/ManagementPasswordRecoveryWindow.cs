using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed class ManagementPasswordRecoveryWindow : Window
{
    public ManagementPasswordRecoveryWindow(Func<string, string, Task> reset)
    {
        Title = "重置管理密码"; Width = 520; Height = 580; MinWidth = 360; MinHeight = 320;
        StartupAppearance.Apply(this); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { Margin = new(24, 20, 24, 20) };
        layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); Content = layout;
        var body = new StackPanel(); layout.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        body.Children.Add(new TextBlock { Text = "重置管理密码", FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis });
        body.Children.Add(new TextBlock { Text = "输入根密码和新的管理密码。重置后需要用新密码进入管理模式。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 12) });
        PasswordBox Field(string name, string label)
        {
            var input = new PasswordBox { Name = name, MaxLength = 256, MinHeight = 44 };
            body.Children.Add(new Label { Content = label, Target = input, Padding = new(0), Margin = new(0, 8, 0, 4) }); body.Children.Add(input);
            AutomationProperties.SetName(input, label); return input;
        }
        var root = Field("RecoveryRootPassword", "根密码");
        var next = Field("RecoveryNewPassword", "新管理密码");
        var confirmation = Field("RecoveryConfirmation", "再次输入新密码");
        body.Children.Add(new TextBlock { Text = "5–256 个字符，可使用纯数字。已有管理授权将在重置后退出。", FontSize = UiTypography.Label, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, Margin = new(0, 8, 0, 8) };
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite); Grid.SetRow(feedback, 1); layout.Children.Add(feedback);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetRow(actions, 2); layout.Children.Add(actions);
        var cancel = new Button { Content = "取消", IsCancel = true, MinHeight = 44, MinWidth = 88, Margin = new(0, 0, 12, 0) };
        var submit = new Button { Name = "ResetManagementPassword", Content = "重置管理密码", MinHeight = 44, MinWidth = 140, IsDefault = true };
        StartupAppearance.Primary(submit); actions.Children.Add(cancel); actions.Children.Add(submit);
        bool busy = false;
        foreach (var input in new[] { root, next, confirmation }) input.PasswordChanged += (_, _) => { if (!busy) feedback.Text = ""; };
        void Clear() { root.Clear(); next.Clear(); confirmation.Clear(); }
        cancel.Click += (_, _) => Close();
        submit.Click += async (_, _) =>
        {
            if (busy) return; busy = true; body.IsEnabled = actions.IsEnabled = false;
            var original = root.Password; var replacement = next.Password; var repeated = confirmation.Password;
            try
            {
                if (replacement != repeated) throw new InvalidDataException("两次新管理密码不一致。");
                await reset(original, replacement); DialogResult = true;
            }
            catch (Exception error) { feedback.Text = ChineseInterfaceText.Message(error.Message); }
            finally { Clear(); busy = false; body.IsEnabled = actions.IsEnabled = true; root.Focus(); }
        };
        Loaded += (_, _) => { root.Focus(); Keyboard.Focus(root); };
        Closing += (_, e) => e.Cancel = busy && DialogResult != true;
        Closed += (_, _) => Clear();
    }
}
