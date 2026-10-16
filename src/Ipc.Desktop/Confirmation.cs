using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Ipc.Desktop;
public static class Confirmation
{
    public static bool Ask(Window owner, string message)
    {
        var returnFocus = Keyboard.FocusedElement;
        var window = new Window { Owner = owner, Title = "确认操作", Width = 480, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20), Foreground = Brushes.Black });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true };
        var confirm = new Button { Content = "确认", IsDefault = false };
        cancel.Click += (_, _) => window.DialogResult = false;
        confirm.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); panel.Children.Add(buttons); window.Content = panel;
        KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);
        window.Loaded += (_, _) => cancel.Focus();
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) e.Handled = true; };
        var result = window.ShowDialog() == true;
        if (returnFocus is IInputElement input) Keyboard.Focus(input);
        return result;
    }
}
