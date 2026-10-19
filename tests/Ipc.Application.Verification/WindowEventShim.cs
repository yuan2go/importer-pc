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
        bool disposed, insideClosing;
        [ThreadStatic] static TaskCompletionSource? closingEvent;
        internal static Task YieldClosingEvent() => closingEvent?.Task ?? Task.CompletedTask;
        public void Show() => IsVisible = true;
        public void RaiseLoaded() => Loaded?.Invoke(this, EventArgs.Empty);
        public void Close()
        {
            if (insideClosing) throw new InvalidOperationException("WPF does not allow Close during Closing");
            if (disposed) return;
            var args = new System.ComponentModel.CancelEventArgs();
            var prior = closingEvent; var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            closingEvent = completed; insideClosing = true;
            try { Closing?.Invoke(this, args); }
            finally { insideClosing = false; closingEvent = prior; completed.TrySetResult(); }
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
    public static class Dispatcher { public static Task Yield() => System.Windows.Window.YieldClosingEvent(); }
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
    public static class ConfigurationCloseGuard
    {
        public static bool HasUnsaved(ParcelViewModel vm, MaintenancePage page) => vm.HasUnsavedConfiguration;
        public static bool ConfirmDiscard(System.Windows.Window window, int count) => false;
    }
    public class HistoryPage;
    public static class ProductBrand { public static void Apply(System.Windows.Window window) { } }
    public class ProductSupportWindow(Ipc.Application.Contracts.IParcelApplication application, string? operation) : System.Windows.Window
    { public Ipc.Application.Contracts.IParcelApplication Application { get; } = application; public string? Operation { get; } = operation; }
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
