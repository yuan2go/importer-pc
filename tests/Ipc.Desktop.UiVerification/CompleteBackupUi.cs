using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Platform;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;

static partial class Program
{
    static async Task VerifyCompleteBackupUi(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS BACKUP-UI " + message); }
        async Task Until(Func<bool> ready)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!ready()) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(10, deadline.Token); }
        }
        await using var h = new ManualSupplyHarness(root, Path.Combine(evidence, "backup-ui"), deferred: true);
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        var api = DispatchProxy.Create<IManualDebugUiFixture, BackupCancelBoundary>();
        var probe = (BackupCancelBoundary)(object)api; probe.Inner = h.App;
        var window = new SupplyStationWindow(api, null, "standard") { ConfirmDiscardDrafts = _ => true }; window.Show();
        try
        {
            await Until(() => window.ViewModel.Fresh && window.ViewModel.IsManagementMode);
            window.ShowPositionDiagnostics(window.ViewModel.Left);
            await Until(() => System.Windows.Application.Current.Windows.OfType<ManagementCenterWindow>().Any(c => c.IsVisible && ReferenceEquals(c.Owner, window)));
            var center = System.Windows.Application.Current.Windows.OfType<ManagementCenterWindow>().Single(c => c.IsVisible && ReferenceEquals(c.Owner, window));
            var original = await h.App.SnapshotAsync();
            foreach (var id in new[] { "CompleteBackup.Diagnostic", "CompleteBackup.Settings" })
            {
                if (id.EndsWith("Settings", StringComparison.Ordinal))
                {
                    Descendants(center).OfType<Button>().Single(b => Equals(b.Tag, "maintenance")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(() => window.ViewModel.HasLoadedConfiguration && !window.ViewModel.Busy && Descendants(center).OfType<ParameterSettingsControl>().Any(e => e.Model is not null));
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Check(Descendants(center).OfType<ParameterSettingsControl>().Single().NavigateTo("BackupEnabled"), "manual backup settings resolve through the current parameter directory");
                }
                else Descendants(center).OfType<Expander>().Single(e => e.Content is FrameworkElement content && Descendants(content).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == id)).IsExpanded = true;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); center.UpdateLayout();
                var action = Descendants(center).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == id);
                action.BringIntoView(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                await Until(() => action.IsVisible && action.IsEnabled);
                Check(action.IsVisible && action.IsEnabled, id + " is reachable in its actual native page; visible=" + action.IsVisible + "; enabled=" + action.IsEnabled + "; busy=" + window.ViewModel.Busy);
                bool cancelled = false, duplicateDisabled = false;
                var owner = new WindowInteropHelper(center).Handle;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                timer.Tick += (_, _) => BackupDialogNative.EnumThreadWindows(BackupDialogNative.GetCurrentThreadId(), (handle, _) =>
                {
                    var name = new StringBuilder(64); BackupDialogNative.GetClassName(handle, name, name.Capacity);
                    if (name.ToString() != "#32770" || BackupDialogNative.GetWindow(handle, 4) != owner) return true;
                    // Close only this test window's native file dialog. No path
                    // is selected, confirmed, typed or written.
                    duplicateDisabled = !action.IsEnabled; cancelled = true; BackupDialogNative.SendMessage(handle, 0x10, 0, 0); return true;
                }, 0);
                timer.Start();
                try
                {
                    action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(() => cancelled && !window.ViewModel.Busy);
                }
                finally { timer.Stop(); }
                Check(duplicateDisabled, id + " disables repeated input while the native picker owns the operation");
                var after = await h.App.SnapshotAsync();
                Check(probe.Backups == 0 && after.Paused == original.Paused && after.Operations.Length == original.Operations.Length && center.IsVisible,
                    id + " opens the owned native picker; cancellation preserves production state and creates no backup");
            }
            center.Close(); await Until(() => !center.IsVisible);
            Check((await h.App.SnapshotAsync()).Positions!.All(p => p.Admit), "both cancelled entry flows leave ordinary manual supply recoverable");
        }
        finally { window.Close(); }
    }
    static class BackupDialogNative
    {
        internal delegate bool Enumerate(nint window, nint state);
        [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumThreadWindows(uint thread, Enumerate callback, nint state);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")] internal static extern int GetClassName(nint window, StringBuilder text, int capacity);
        [DllImport("user32.dll")] internal static extern nint GetWindow(nint window, uint command);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] internal static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    }
}

public class BackupCancelBoundary : ProjectionProbe
{
    public int Backups;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name != nameof(IParcelApplication.BackupAsync)) return base.Invoke(method, args);
        Interlocked.Increment(ref Backups);
        return Task.FromException<BusinessReceipt>(new IOException("Unexpected backup after native cancellation"));
    }
}
