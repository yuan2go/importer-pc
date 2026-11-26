using System.Windows;
using System.Windows.Media;

internal static class NativeClientSize
{
    internal static async Task Set(Window window, FrameworkElement client, double width, double height)
    {
        window.WindowState = WindowState.Normal;
        window.Width = width + window.ActualWidth - client.ActualWidth;
        window.Height = height + window.ActualHeight - client.ActualHeight;
        await Task.Delay(150); window.UpdateLayout();
        if (Math.Abs(client.ActualWidth - width) >= 1 || Math.Abs(client.ActualHeight - height) >= 1)
        {
            // A headless runner can expose a 1024-pixel desktop without an enumerable
            // display mode. Exercise a real, larger HWND backing surface there. This
            // fixture-only hook preserves product minimums; all exact client/content
            // assertions below still run. It is not physical-monitor acceptance.
            var dpi = VisualTreeHelper.GetDpi(window);
            var pixelsWide = (int)Math.Round((width + window.ActualWidth - client.ActualWidth) * dpi.DpiScaleX);
            var pixelsHigh = (int)Math.Round((height + window.ActualHeight - client.ActualHeight) * dpi.DpiScaleY);
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            var source = System.Windows.Interop.HwndSource.FromHwnd(handle)!;
            System.Windows.Interop.HwndSourceHook hook = (IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (message != 0x24) return IntPtr.Zero; // WM_GETMINMAXINFO
                NativeGeometry.DefWindowProc(hwnd, message, wParam, lParam);
                System.Runtime.InteropServices.Marshal.WriteInt32(lParam, 24, (int)Math.Ceiling(window.MinWidth * dpi.DpiScaleX));
                System.Runtime.InteropServices.Marshal.WriteInt32(lParam, 28, (int)Math.Ceiling(window.MinHeight * dpi.DpiScaleY));
                System.Runtime.InteropServices.Marshal.WriteInt32(lParam, 32, Math.Max(pixelsWide, System.Runtime.InteropServices.Marshal.ReadInt32(lParam, 32)));
                System.Runtime.InteropServices.Marshal.WriteInt32(lParam, 36, Math.Max(pixelsHigh, System.Runtime.InteropServices.Marshal.ReadInt32(lParam, 36)));
                // Let WPF's original Window hook cache these native tracking bounds
                // as well; suppressing it leaves MeasureOverride clamped to 1024.
                handled = false; return IntPtr.Zero;
            };
            source.AddHook(hook);
            try
            {
                var limits = System.Runtime.InteropServices.Marshal.AllocHGlobal(40);
                try { NativeGeometry.SendMessage(handle, 0x24, IntPtr.Zero, limits); }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(limits); }
                if (!NativeGeometry.SetWindowPos(handle, IntPtr.Zero, 0, 0, pixelsWide, pixelsHigh, 0x16))
                    throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                await Task.Delay(150); window.UpdateLayout();
                Console.WriteLine($"Native backing client={client.ActualWidth}x{client.ActualHeight}; physical work area={SystemParameters.WorkArea}; no physical-screen claim");
            }
            finally { source.RemoveHook(hook); }
        }
    }
    static class NativeGeometry
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
        internal static extern IntPtr DefWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
        internal static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
