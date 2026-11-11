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
            Check(window.WindowStyle == WindowStyle.None && window.WindowState == WindowState.Maximized && PresentationPreferences.ReadWindowMode("sportmaster") == PresentationPreferences.WindowOpeningMode.FullScreen, "Escape preserves full screen and its saved default");
            window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.F11) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Check(!window.InputBindings.OfType<KeyBinding>().Any(b => b.Key == Key.F11) && window.WindowStyle == WindowStyle.None, "F11 has no full-screen exit binding");
            window.WindowState = WindowState.Minimized; await Flush();
            Check(window.WindowState == WindowState.Minimized, "full screen can minimize to the taskbar");
            window.WindowState = WindowState.Normal; await Flush();
            Check(window.WindowState == WindowState.Maximized && window.WindowStyle == WindowStyle.None, "restoring from the taskbar returns to full screen");
            var reopened = new Window { Width = 900, Height = 640 };
            WindowOpeningPreference.Attach(reopened, "sportmaster", message => throw new InvalidOperationException(message));
            reopened.Show(); await Flush();
            Check(reopened.WindowStyle == WindowStyle.None && reopened.WindowState == WindowState.Maximized, "a newly opened native window restores the persisted default");
            reopened.Close();
            PresentationPreferences.SaveWindowMode("sportmaster", PresentationPreferences.WindowOpeningMode.Window);
            var firstDraft = new StartupConfigurationDraft(vm.Snapshot!.Configuration.Effective, await runtime.CapabilitiesAsync());
            var firstEditor = new ParameterSettingsControl { FirstConfiguration = true, Model = firstDraft.CreateParameterEditor((p, codes) => Task.FromResult(BarcodeFiltering.Select(p, codes))) };
            var firstHost = new Window { Content = firstEditor, Width = 900, Height = 700 }; firstHost.Show();
            firstEditor.NavigateTo("WindowOpeningMode"); await Flush();
            var firstOpening = Descendants(firstEditor).OfType<ComboBox>().Single(b => AutomationProperties.GetAutomationId(b) == "WindowOpeningMode");
            firstOpening.SelectedValue = PresentationPreferences.WindowOpeningMode.FullScreen; await Flush();
            Check(PresentationPreferences.ReadWindowMode("sportmaster") == PresentationPreferences.WindowOpeningMode.Window && firstEditor.OpeningModeDraft == PresentationPreferences.WindowOpeningMode.FullScreen,
                "first configuration retains the selected full-screen draft until configuration save");
            firstEditor.SaveDisplayPreferences(); firstHost.Close();
            var firstWorkstation = new Ipc.Application.Sportmaster.Desktop.SupplyStationWindow(runtime, null, "standard"); firstWorkstation.Show(); await Flush();
            Check(firstWorkstation.WindowStyle == WindowStyle.None && firstWorkstation.WindowState == WindowState.Maximized && ((Button)firstWorkstation.FindName("MinimizeFullScreen")).IsVisible,
                "the first workstation after first-configuration save opens full screen with a taskbar minimize control");
            firstWorkstation.Close();

            opening.SelectedValue = PresentationPreferences.WindowOpeningMode.Window; await Flush();
            var preference = Path.Combine(evidence, "user-preferences", "presentation", "sportmaster.window.json");
            var original = File.ReadAllBytes(preference);
            File.WriteAllText(preference, "{broken");
            await Save();
            Check(vm.HasUnsavedWindowPreference && save.IsEnabled && File.ReadAllText(preference) == "{broken" && vm.WindowPreferenceStatus.Contains("草稿保留"), "failed save preserves both the original file and the visible draft for retry");
            File.WriteAllBytes(preference, original); await Save();
            Check(PresentationPreferences.ReadWindowMode("sportmaster") == PresentationPreferences.WindowOpeningMode.Window && window.WindowStyle != WindowStyle.None && !vm.HasUnsavedWindowPreference, "retry saves the window default and clears pending changes");
            vm.StructuredPolicy!.BarcodePattern = "SYNTHETIC[0-9]+";
            vm.WindowModeDraft = PresentationPreferences.WindowOpeningMode.FullScreen;
            Check(await vm.PrepareConfigurationReviewAsync() && vm.ReviewedConfigurationChanges.Any(c => c.Path == "WindowOpeningMode") && vm.ReviewedConfigurationChanges.Any(c => c.Path == "BarcodePattern"),
                "mixed review includes both the barcode rule and the local opening mode");
            vm.SmPreferences.ShowPace = false; vm.SmPreferences.CueVolume = 25;
            Check(await vm.PrepareConfigurationReviewAsync() && vm.ReviewedConfigurationChanges.Any(c => c.Path == "ShowPace") && vm.ReviewedConfigurationChanges.Any(c => c.Path == "CueVolume"), "mixed review includes pace and audio in the same draft");
            File.WriteAllText(preference, "{broken");
            var review = new ConfigurationSaveReviewWindow(window, vm);
            try
            {
                review.Show(); await Flush();
                var confirm = Descendants(review).OfType<Button>().Single(b => b.Name == "ConfirmSaveChanges");
                Check(confirm.IsEnabled, "mixed save keeps existing configuration admission guards");
                confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var until = DateTime.UtcNow.AddSeconds(15);
                do { await Task.Delay(20); await Flush(); } while (vm.Busy && DateTime.UtcNow < until);
                Check(!vm.Busy && !vm.HasUnsavedConfiguration && vm.HasUnsavedDisplayPreferences && vm.HasPreferenceSaveFailure &&
                    PresentationPreferences.ReadCueVolume("sportmaster") == 25 && PresentationPreferences.Read("sportmaster", "pace") == false && File.ReadAllText(preference) == "{broken",
                    "mixed save confirms the business revision and the two successful local files while retaining only the failed window draft");
                var committedRevision = vm.Snapshot!.Configuration.Revision;
                File.WriteAllBytes(preference, original);
                Check(await vm.PrepareConfigurationReviewAsync() && vm.ReviewedConfigurationChanges.Single().Path == "WindowOpeningMode", "partial retry reviews only the remaining local preference");
                confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                do { await Task.Delay(20); await Flush(); } while (vm.Busy && DateTime.UtcNow < until);
                Check(!vm.HasUnsavedDisplayPreferences && vm.Snapshot!.Configuration.Revision == committedRevision &&
                    PresentationPreferences.ReadWindowMode("sportmaster") == PresentationPreferences.WindowOpeningMode.FullScreen && window.WindowStyle == WindowStyle.None,
                    "retry applies the remaining local file immediately without submitting the successful business configuration again");
            }
            finally { review.Close(); }
        }
        finally { window.Close(); await runtime.StopAsync(default); }
    }
}
