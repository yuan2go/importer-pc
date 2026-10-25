using System.Windows;
using System.Windows.Input;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

// Local presentation only. No runtime/configuration or admission commands.
public static class WindowOpeningPreference
{
    public static void Attach(Window window, string application, Action<string> report)
    {
        var fullScreen = false;
        var style = window.WindowStyle;
        var resize = window.ResizeMode;
        var priorState = window.WindowState;
        void SetFullScreen(bool value)
        {
            if (value == fullScreen) return;
            if (value) { style = window.WindowStyle; resize = window.ResizeMode; priorState = window.WindowState; }
            window.WindowState = WindowState.Normal;
            window.WindowStyle = value ? WindowStyle.None : style;
            window.ResizeMode = value ? ResizeMode.NoResize : resize;
            window.WindowState = value ? WindowState.Maximized : priorState;
            fullScreen = value;
        }
        window.Loaded += (_, _) =>
        {
            try { SetFullScreen(PresentationPreferences.ReadWindowMode(application) == PresentationPreferences.WindowOpeningMode.FullScreen); }
            catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { report("窗口偏好读取失败，原文件保留：" + error.Message); }
        };
        var toggle = new RoutedCommand();
        window.CommandBindings.Add(new CommandBinding(toggle, (_, e) => { SetFullScreen(!fullScreen); e.Handled = true; }));
        window.InputBindings.Add(new KeyBinding(toggle, Key.F11, ModifierKeys.None));
        window.PreviewKeyDown += (_, e) => { if (fullScreen && e.Key == Key.Escape) { SetFullScreen(false); e.Handled = true; } };
    }
}
