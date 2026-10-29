using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ipc.Application.ParcelFlow.Desktop;

static partial class Program
{
    static async Task VerifyManagementPasswordLayout(string evidence, List<string> lines)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); lines.Add("PASS PASSWORD " + message); }
        void Capture(Window window, string name)
        {
            window.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(window);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(evidence, name + ".png")); encoder.Save(output);
            lines.Add($"CAPTURE {name}: {window.ActualWidth}x{window.ActualHeight} DIP; actual DPI {dpi.PixelsPerInchX}");
        }
        var service = new UiManagementProbe(); var dialog = new ManagementModeWindow(service);
        dialog.Show(); await Task.Delay(200); dialog.UpdateLayout();
        try
        {
            var bodyScroll = Descendants(dialog).OfType<ScrollViewer>().First();
            var proof = Descendants(dialog).OfType<PasswordBox>().Single(x => x.Name == "ManagementPassword");
            var confirm = Descendants(dialog).OfType<PasswordBox>().Single(x => x.Name == "ManagementPasswordConfirmation");
            var show = Descendants(dialog).OfType<CheckBox>().Single(x => Equals(x.Content, "显示密码"));
            var submit = Descendants(dialog).OfType<Button>().Single(x => x.Name == "EnterManagementMode");
            Check(bodyScroll.ScrollableHeight == 0 && show.IsVisible, "first setup displays both fields, reveal option and submit without scrolling");
            proof.Focus(); proof.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            Check(confirm.IsKeyboardFocused, "native Tab moves directly from password to confirmation");
            Capture(dialog, "password-default");
            service.ConfigureError = new IOException(string.Concat(Enumerable.Repeat("合成诊断：维护授权未完成，原数据保留。\n", 60)));
            proof.Password = confirm.Password = "11111"; submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(200); dialog.UpdateLayout();
            var feedbackScroll = Descendants(dialog).OfType<ScrollViewer>().Single(x => x.Content is TextBlock);
            Check(feedbackScroll.ActualHeight <= 80 && feedbackScroll.ScrollableHeight > 0, "long failure remains fully scrollable within a bounded feedback area");
            Check(proof.Password.Length == 0 && confirm.Password.Length == 0 && !service.Configured && submit.IsEnabled, "failed setup clears secrets and retains an actionable unconfigured state");
            foreach (var (width, height, name) in new[] { (520d, 540d, "password-long-error"), (400d, 360d, "password-compact-error") })
            {
                dialog.Width = width; dialog.Height = height; dialog.UpdateLayout();
                var bounds = submit.TransformToAncestor((FrameworkElement)dialog.Content).TransformBounds(new Rect(submit.RenderSize));
                Check(bounds.Bottom <= ((FrameworkElement)dialog.Content).ActualHeight && bounds.Top >= 0 && submit.IsEnabled, "submit remains visible after long failure at " + width + "x" + height);
                bodyScroll.ScrollToBottom(); dialog.UpdateLayout();
                Check(show.TransformToAncestor(bodyScroll).TransformBounds(new Rect(show.RenderSize)).Bottom <= bodyScroll.ActualHeight, "reveal option can be reached at " + width + "x" + height);
                Capture(dialog, name);
            }
            show.IsChecked = true; show.IsChecked = false;
            Check(Descendants(dialog).OfType<TextBox>().Where(x => System.Windows.Automation.AutomationProperties.GetName(x).StartsWith("显示")).All(x => x.Text.Length == 0), "concealment clears plain password buffers");
        }
        finally { dialog.Close(); }
    }
}
