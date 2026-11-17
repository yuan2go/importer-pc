using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Ipc.Application.ParcelFlow.Desktop;

// Limited to the installation and authorization surfaces; parameter controls keep their own styles.
public static class StartupAppearance
{
    public static void Apply(Window window)
    {
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Ipc.Application.ParcelFlow.Desktop;component/StartupAppearance.xaml", UriKind.Relative) });
        window.SetResourceReference(Control.BackgroundProperty, "Surface"); window.SetResourceReference(Control.ForegroundProperty, "Ink");
        UiTypography.Apply(window);
        window.UseLayoutRounding = true;
        FitToWorkArea(window);
    }
    public static void FitToWorkArea(Window window)
    {
        void Fit()
        {
            var work = SystemParameters.WorkArea;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                var info = new MonitorInformation { Size = Marshal.SizeOf<MonitorInformation>() };
                if (!GetMonitorInfoW(MonitorFromWindow(handle, 2), ref info)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                var dpi = VisualTreeHelper.GetDpi(window);
                work = new Rect(info.Work.Left / dpi.DpiScaleX, info.Work.Top / dpi.DpiScaleY,
                    (info.Work.Right - info.Work.Left) / dpi.DpiScaleX, (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY);
            }
            window.MaxWidth = Math.Max(320, work.Width - 16); window.MaxHeight = Math.Max(240, work.Height - 16);
            window.MinWidth = Math.Min(window.MinWidth, window.MaxWidth); window.MinHeight = Math.Min(window.MinHeight, window.MaxHeight);
        }
        window.SourceInitialized += (_, _) => Fit(); window.DpiChanged += (_, _) => Fit(); window.LocationChanged += (_, _) => Fit(); Fit();
    }
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInformation { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInformation information);
    public static void Primary(Button button) => button.SetResourceReference(FrameworkElement.StyleProperty, "StartupPrimary");
}
