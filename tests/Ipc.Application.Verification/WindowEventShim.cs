// Event/control boundary only: runs the unchanged window code-behind, never WPF layout,
// HWND ownership, dispatcher delivery, DPI or native resource loading.
namespace System.Windows
{
    public class RoutedEventArgs : EventArgs;
    public class SizeChangedEventArgs : EventArgs;
    public delegate void SizeChangedEventHandler(object sender, SizeChangedEventArgs e);
    public readonly struct Thickness { public Thickness(double value) { } }
    public enum WindowState { Normal, Maximized }
    public static class SystemParameters { public static (double Width, double Height) WorkArea => (1440, 980); }
    public class Window
    {
        public object? DataContext { get; set; }
        public Window? Owner { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool IsVisible { get; private set; }
        public double ActualWidth { get; set; } = 1440;
        public double ActualHeight { get; set; } = 980;
        public double FontSize { get; set; }
        public WindowState WindowState { get; set; }
        public event EventHandler? Loaded;
        public event System.ComponentModel.CancelEventHandler? Closing;
        public event EventHandler? Closed;
        bool disposed;
        public void Show() => IsVisible = true;
        public void RaiseLoaded() => Loaded?.Invoke(this, EventArgs.Empty);
        public void Close()
        {
            if (disposed) return;
            var args = new System.ComponentModel.CancelEventArgs(); Closing?.Invoke(this, args);
            if (args.Cancel) return;
            disposed = true; IsVisible = false; Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
namespace System.Windows.Controls
{
    public class ColumnDefinition;
    public class Grid
    {
        public List<ColumnDefinition> ColumnDefinitions { get; } = [];
        public List<object> Children { get; } = [];
        public System.Windows.Thickness Margin { get; set; }
        public double ActualWidth { get; set; } = 1440;
        public double ActualHeight { get; set; } = 980;
        public event System.Windows.SizeChangedEventHandler? SizeChanged;
        public void RaiseSizeChanged() => SizeChanged?.Invoke(this, new());
        public static void SetColumn(object control, int column) { }
    }
    public class ContentControl { public object? Content { get; set; } }
    public class PasswordBox { public string Password { get; set; } = ""; public void Clear() => Password = ""; }
}
namespace System.Windows.Threading
{
    public class DispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsEnabled { get; private set; }
        public event EventHandler? Tick;
        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
        public void Fire() { if (IsEnabled) Tick?.Invoke(this, EventArgs.Empty); }
    }
}
namespace Ipc.Application.ParcelFlow.Desktop
{
    public class HistoryPage;
    public class MaintenancePage;
}
namespace Ipc.Application.Sportmaster.Desktop
{
    public class SupplyStationPosition { public object? DataContext { get; set; } }
    public partial class SupplyStationWindow
    {
        readonly System.Windows.Controls.Grid ClientArea = new();
        readonly System.Windows.Controls.ContentControl PageHost = new();
        readonly System.Windows.Controls.PasswordBox LoginProof = new();
        void InitializeComponent() { }
    }
}
