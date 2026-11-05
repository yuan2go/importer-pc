using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyWindowPreference(string root, string evidence, List<string> lines)
    {
        void Check(bool valid, string reason) { if (!valid) throw new InvalidOperationException(reason); lines.Add("PASS WINDOW-PREFERENCE " + reason); }
        var launch = new BusinessLaunch(Path.Combine(evidence, "config"), Path.Combine(evidence, "data"), null, null);
        Directory.CreateDirectory(launch.ConfigurationDirectory);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "tests/Ipc.Application.Verification/sportmaster.synthetic.json"))) with { StationId = "window-" + Wire.Id() };
        ExtensionStore.AtomicWrite(Path.Combine(launch.ConfigurationDirectory, "business.json"), config);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize((IParcelApplication)runtime.Application);
        var vm = new ParcelViewModel(runtime, "Sportmaster", "合成原生验证"); await vm.RefreshAsync(); await vm.LoadCapabilitiesAsync();
        var page = new MaintenancePage { DataContext = vm };
        var window = new Window { Content = page, Width = 1366, Height = 768 };
        WindowOpeningPreference.Attach(window, "sportmaster", message => vm.Message = message);
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Save()
        {
            ((Button)page.FindName("SaveChanges")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var until = DateTime.UtcNow.AddSeconds(10);
            do { await Task.Delay(20); await Flush(); } while (vm.Busy && DateTime.UtcNow < until);
            Check(!vm.Busy, "bottom save completes");
        }
        try
        {
            window.Show(); await Flush();
            var control = Descendants(page).OfType<ParameterSettingsControl>().Single();
            control.ShowPage(0, "显示偏好"); await Flush();
            var opening = Descendants(control).OfType<ComboBox>().Single(x => AutomationProperties.GetAutomationId(x) == "WindowOpeningMode");
            var save = (Button)page.FindName("SaveChanges");
            var revision = vm.Snapshot!.Configuration.Revision;
            Check(!save.IsEnabled && !vm.HasUnsavedConfiguration, "unchanged window preference has no pending save");
            opening.SelectedValue = PresentationPreferences.WindowOpeningMode.FullScreen; await Flush();
            Check(save.IsEnabled && vm.HasUnsavedDisplayPreferences && vm.ConfigurationDraftStatusText == "有未保存的更改" && !vm.HasUnsavedConfiguration,
                "changing full screen enables the bottom save and marks local changes without a business draft");
            Check(vm.GetConfigurationChangeItems().Single().Label == "默认打开方式", "review identifies the exact local window change");
            await vm.RefreshAsync(); control.ShowPage(0, "数据与记录"); control.ShowPage(0, "显示偏好"); await Flush();
            Check(vm.WindowModeDraft == PresentationPreferences.WindowOpeningMode.FullScreen, "snapshot refresh and navigation retain the unsaved selection");
            await Save();
            Check(PresentationPreferences.ReadWindowMode("sportmaster") == PresentationPreferences.WindowOpeningMode.FullScreen && window.WindowStyle == WindowStyle.None && window.WindowState == WindowState.Maximized,
                "bottom save persists full screen and applies it to the live native window");
            Check(!save.IsEnabled && !vm.HasUnsavedDisplayPreferences && vm.Snapshot.Configuration.Revision == revision, "successful local save clears dirty state without changing the business revision");
            CaptureInternal(evidence, "window-preference-fullscreen", page, lines);
            window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Check(window.WindowStyle != WindowStyle.None && PresentationPreferences.ReadWindowMode("sportmaster") == PresentationPreferences.WindowOpeningMode.FullScreen, "Escape temporarily exits without erasing the saved default");
            var reopened = new Window { Width = 900, Height = 640 };
            WindowOpeningPreference.Attach(reopened, "sportmaster", message => throw new InvalidOperationException(message));
            reopened.Show(); await Flush();
            Check(reopened.WindowStyle == WindowStyle.None && reopened.WindowState == WindowState.Maximized, "a newly opened native window restores the persisted default");
            reopened.Close();
            opening.SelectedValue = PresentationPreferences.WindowOpeningMode.Window; await Flush();
            var preference = Path.Combine(evidence, "user-preferences", "presentation", "sportmaster.window.json");
            var original = File.ReadAllBytes(preference);
            File.WriteAllText(preference, "{broken");
            await Save();
            Check(vm.HasUnsavedWindowPreference && save.IsEnabled && File.ReadAllText(preference) == "{broken" && vm.WindowPreferenceStatus.Contains("未保存"), "failed save preserves both the original file and the visible draft for retry");
            File.WriteAllBytes(preference, original); await Save();
            Check(PresentationPreferences.ReadWindowMode("sportmaster") == PresentationPreferences.WindowOpeningMode.Window && window.WindowStyle != WindowStyle.None && !vm.HasUnsavedWindowPreference, "retry saves the window default and clears pending changes");
            vm.StructuredPolicy!.BarcodePattern = "SYNTHETIC[0-9]+";
            vm.WindowModeDraft = PresentationPreferences.WindowOpeningMode.FullScreen;
            Check(await vm.PrepareConfigurationReviewAsync() && vm.ReviewedConfigurationChanges.Any(c => c.Path == "WindowOpeningMode") && vm.ReviewedConfigurationChanges.Any(c => c.Path == "BarcodePattern"),
                "mixed review includes both the barcode rule and the local opening mode");
            var review = new ConfigurationSaveReviewWindow(window, vm);
            try
            {
                review.Show(); await Flush();
                var confirm = Descendants(review).OfType<Button>().Single(b => b.Name == "ConfirmSaveChanges");
                Check(confirm.IsEnabled, "mixed save keeps existing configuration admission guards");
                confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var until = DateTime.UtcNow.AddSeconds(15);
                do { await Task.Delay(20); await Flush(); } while (vm.Busy && DateTime.UtcNow < until);
                Check(!vm.Busy && !vm.HasUnsavedConfiguration && !vm.HasUnsavedWindowPreference &&
                    PresentationPreferences.ReadWindowMode("sportmaster") == PresentationPreferences.WindowOpeningMode.FullScreen && window.WindowStyle == WindowStyle.None,
                    "mixed save applies the window choice immediately while the review remains open");
            }
            finally { review.Close(); }
        }
        finally { window.Close(); await runtime.StopAsync(default); }
    }
}
