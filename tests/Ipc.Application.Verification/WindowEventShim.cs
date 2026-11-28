// Event/control boundary only: runs the unchanged window code-behind, never WPF layout,
// HWND ownership, dispatcher delivery, DPI or native resource loading.
namespace Ipc.Presentation.Wpf
{
    public static partial class UiTypography
    {
        // This portable boundary has no fonts. Native UiVerification is the
        // authority for GlyphRun resolution, weights, inheritance and geometry.
        public static void Apply(System.Windows.Window window) => window.FontSize = Body;
    }
}
namespace System.Windows
{
    public static class MessageBox { public static void Show(Window owner, string message, string title) { } }
    public class RoutedEventArgs : EventArgs;
    public class SizeChangedEventArgs : EventArgs;
    public delegate void SizeChangedEventHandler(object sender, SizeChangedEventArgs e);
    public readonly struct Thickness { public Thickness(double value) { } }
    public enum WindowState { Normal, Minimized, Maximized }
    public class FrameworkElement;
    public static class SystemParameters { public static (double Width, double Height) WorkArea => (1440, 980); }
    public class Window
    {
        public Threading.Dispatcher Dispatcher { get; } = new();
        public object? DataContext { get; set; }
        public Window? Owner { get; set; }
        public Window[] OwnedWindows => [];
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
        public bool Activate() => IsVisible; // Focus itself is verified only by native WPF.
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
    public class TextBlock { public string Text { get; set; } = ""; public object? ToolTip { get; set; } public System.Windows.Visibility Visibility { get; set; } }
    public class ColumnDefinition;
    public class Grid : System.Windows.FrameworkElement
    {
        public List<ColumnDefinition> ColumnDefinitions { get; } = [];
        public List<object> Children { get; } = [];
        public System.Windows.Thickness Margin { get; set; }
        public double ActualWidth { get; set; } = 1440;
        public double ActualHeight { get; set; } = 980;
        public event System.Windows.SizeChangedEventHandler? SizeChanged;
        public void RaiseSizeChanged() => SizeChanged?.Invoke(this, new());
        public static void SetColumn(object control, int column) { }
        public static void SetIsSharedSizeScope(object control, bool enabled) { }
    }
    public class ContentControl { public object? Content { get; set; } }
    public class PasswordBox { public string Password { get; set; } = ""; public void Clear() => Password = ""; }
}
namespace System.Windows.Threading
{
    public enum DispatcherPriority { Background }
    public class Dispatcher
    {
        public bool HasShutdownStarted => false;
        public static Task Yield() => System.Windows.Window.YieldClosingEvent();
        // Scheduling boundary only. Native dispatcher order is verified by the WPF fixture.
        public Task BeginInvoke(Action action) => Task.Run(action);
        public Task BeginInvoke(DispatcherPriority priority, Action action) => Task.Run(action);
    }
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
    // This cross-platform fixture tests close ownership only. Window modes are
    // exercised against real HWNDs by Ipc.Desktop.UiVerification --parameter-guidance-startup.
    public static class WindowOpeningPreference { public static void Attach(System.Windows.Window window, string application, Action<string> report) { } }
    public static class ConfigurationCloseGuard
    {
        public static bool ConfirmExit(System.Windows.Window owner, string impact) => true;
        public static bool HasUnsaved(ParcelViewModel vm, MaintenancePage page) => vm.HasUnsavedConfiguration;
        public static bool ConfirmDiscard(System.Windows.Window window, int count) => false;
        public static Task<bool> ReviewUnsavedAsync(System.Windows.Window window, int count, Func<Task>? save, string action) => Task.FromResult(false);
    }
    public class SoftwareLogControl(Ipc.Application.Contracts.IWorkstationAdministration service) : System.Windows.FrameworkElement { public Ipc.Application.Contracts.IWorkstationAdministration Service { get; } = service; public event EventHandler<string>? OperationRequested { add { } remove { } } public Task ShowOperationAsync(string id) => Task.CompletedTask; public Task SuspendAndDrainAsync() => Task.CompletedTask; public void ResumeAfterCancelledClose() { } }
    public class HistoryPage { public bool TechnicalLogNavigation { get; set; } public bool TechnicalMaintenance { get; set; } public object? DataContext { get; set; } public event EventHandler<string>? LogsRequested { add { } remove { } } public void ShowTaskDetails() { } }
    public static class ProductBrand { public static void Apply(System.Windows.Window window) { } }
    public class MaintenancePage
    {
        public Ipc.Application.Contracts.SmParameterAudience Audience { get; set; }
        public Func<string, System.Windows.FrameworkElement>? CameraPreviewFactory { get; set; }
        public bool IsEnabled { get; set; }
        public object? DataContext { get; set; }
        public event EventHandler? DisplayPreferencesRequested { add { } remove { } }
        public event EventHandler<ParameterDiagnosticTarget>? DiagnosticsRequested { add { } remove { } }
    }
}
namespace Ipc.Application.Sportmaster.Desktop
{
    internal sealed class StationCameraPreviewControl(SupplyStationViewModel vm, double previewHeight = 286, bool showDiagnostics = true) : System.Windows.FrameworkElement { public void SelectDevice(string device) { } public SupplyStationViewModel Model => vm; public double HeightBudget => previewHeight; public bool ShowsDiagnostics => showDiagnostics; }
    public class SupplyStationPosition { public object? DataContext { get; set; } }
    // Cross-platform event harness only; the real control is exercised by
    // Ipc.Desktop.UiVerification --camera-debug on Windows.
    internal sealed class StationDebugControl : System.Windows.FrameworkElement
    {
        public StationDebugControl(SupplyStationViewModel vm, Func<string, Task> settings, Func<string, Task> details) { }
        public void SelectPosition(string position) { }
        public void Revoke() { }
    }
    // Event-owner harness placeholder only. Product operations are exercised by
    // the native --product-closure-21 suite, not by this cross-platform shim.
    internal sealed class StationStatisticsControl(SupplyStationViewModel vm) : System.Windows.FrameworkElement { public SupplyStationViewModel ViewModel { get; } = vm; public bool HasUnsavedChanges => false; }
    public sealed class StationSettingsWindow : System.Windows.Window
    {
        public Task CloseForOwnerStopAsync() { Close(); return Task.CompletedTask; }
    }
    public static class Presentation
    {
        public static string BuildDefaultWorkstationPage => Ipc.Application.Contracts.WorkstationPages.SupplyStation;
    }

    public partial class SupplyStationWindow
    {
        object PositionHistory() => history;
        readonly System.Windows.Controls.TextBlock StationClock = new();
        readonly System.Windows.Controls.TextBlock TestInstallationLabel = new();
        readonly System.Windows.Controls.Grid workContent = new();
        void BuildOperatorWorkspace() { workContent.Children.Add(work); }
        void RestoreBarcodeFocus() { }
        void InitializeAudio() { }
        readonly System.Windows.Controls.Grid ClientArea = new();
        readonly System.Windows.Controls.ContentControl PageHost = new();
        void InitializeComponent() { }
        StationSettingsWindow? settingsWindow = null;
        System.Windows.Window ManagementDialogOwner => settingsWindow is { IsVisible: true } settings ? settings : this;
        bool ConfirmStationExit() => true;
        void ShowPresentationPreferenceFailure(string detail) => ViewModel.Message = detail;
        void ApplyNavigation() { }
        void EnforceManagementModeView() { }
    }
}
