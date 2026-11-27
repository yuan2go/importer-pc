extern alias Fixture;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Media;
using Ipc.Presentation.Wpf;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;
using ManualSupplyHarness = Fixture::ManualSupplyHarness;
using P1PreviewCamera = Fixture::P1PreviewCamera;
using ManagementModeFixture = Fixture::ManagementModeFixture;

static partial class Program
{
    static void Press(Button button) =>
        button.Dispatcher.BeginInvoke(new Action(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));

    static async Task SmokeScreenshots(string root, string evidence, List<string> lines)
    {
        await CaptureInstaller(evidence, lines);
        await CaptureWorkstation(root, evidence, lines);
        await CaptureFirstConfiguration(evidence, lines);
        await CaptureUncalibrated(root, evidence, lines);

        var build = typeof(Program).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var operations = Path.Combine(root, "tests", "Ipc.Operations.Verification", "bin", build,
            "net10.0-windows", "Ipc.Operations.Verification.exe");
        var operationsEvidence = Path.Combine(evidence, "operations");
        var settings = new ProcessStartInfo(operations) { UseShellExecute = false };
        settings.ArgumentList.Add(root);
        settings.ArgumentList.Add(operationsEvidence);
        settings.ArgumentList.Add("--smoke-screenshots");
        using var process = Process.Start(settings) ?? throw new InvalidOperationException("Unable to start the isolated operations fixture.");
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2)); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        if (process.ExitCode != 0) throw new InvalidOperationException($"Operations screenshot process exited with {process.ExitCode}.");
        foreach (var png in Directory.EnumerateFiles(operationsEvidence, "operations-*.png"))
            File.Copy(png, Path.Combine(evidence, Path.GetFileName(png)));

        if (File.Exists(Path.Combine(evidence, "setup-step1-ru.png")))
        {
            using var archive = ZipFile.Open(Path.Combine(evidence, "PR4-i18n-125dpi.zip"), ZipArchiveMode.Create);
            foreach (var name in new[]
            {
                "installer-language-picker", "setup-step1-ru", "work-en-light", "work-ru-light", "work-ru-dark",
                "work-ru-compact", "work-unknown-guidance-ru", "settings-preferences-lang-theme-ru", "operations-overview-ru"
            })
                archive.CreateEntryFromFile(Path.Combine(evidence, name + ".png"), name + ".png", CompressionLevel.Optimal);
        }
    }

    static async Task CaptureInstaller(string evidence, List<string> lines)
    {
        var picker = new Ipc.Desktop.InstallerLanguageWindow("ru-RU");
        picker.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (picker.SelectedLanguage != "ru-RU") throw new InvalidOperationException("The installer did not preselect the resolved language.");
            var options = Visuals<ComboBox>(picker).Single(box => AutomationProperties.GetAutomationId(box) == "Installer.LanguageSelection");
            if (options.Items.Count != 3 || options.ActualHeight < 44) throw new InvalidOperationException("The installer must show all three languages in a touch-sized selector.");
            CaptureInternal(evidence, "installer-language-picker", (FrameworkElement)picker.Content, lines);
        }
        finally { picker.Close(); }

        var manager = new WorkstationLocalizationManager();
        var resources = System.Windows.Application.Current.Resources;
        manager.Apply(resources, "ru-RU");
        try
        {
            if (Environment.GetEnvironmentVariable("IPC_INSTALLER_SMOKE_MEDIA") is { Length: > 0 } media)
            {
                var work = Path.Combine(evidence, "isolated-installation");
                using var wizard = new InstallerPreview(media, work);
                wizard.Window.Show();
                try
                {
                    await AwaitUi(() => Visuals<Expander>(wizard.Window).Any(panel =>
                        AutomationProperties.GetName(panel) == UiText.Get("Setup.Advanced")));
                }
                catch (TimeoutException error)
                {
                    var text = string.Join(" | ", Visuals<TextBlock>(wizard.Window).Select(block => block.Text).Where(value => value.Length > 0));
                    throw new InvalidOperationException("The installer did not reach the verified options page: " + text, error);
                }
                var shortcuts = Visuals<Expander>(wizard.Window).Single(panel => AutomationProperties.GetName(panel) == UiText.Get("Setup.Advanced"));
                shortcuts.IsExpanded = true;
                await AwaitUi(() => Visuals<CheckBox>(wizard.Window).Any(check => Equals(check.Content, UiText.Get("Setup.StartMenuShortcut"))));
                await Capture(wizard.Window, 1000, 690, evidence, "setup-step1-ru", lines);
            }
            else
            {
                var work = Path.Combine(evidence, "isolated-installation");
                var waiting = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var window = new Ipc.Desktop.StartupSetupWindow(new BusinessLaunch(work, work, null, null),
                    (_, cancellation) => waiting.Task.WaitAsync(cancellation), initialLanguage: "ru-RU");
                window.Show();
                try { await Capture(window, 1000, 690, evidence, "setup-media-check-ru", lines); }
                finally { waiting.TrySetCanceled(); window.Close(); }
            }
        }
        finally { manager.Apply(resources, "zh-CN"); }
    }

    sealed class InstallerPreview : IDisposable
    {
        public Ipc.Desktop.StartupSetupWindow Window { get; }

        public InstallerPreview(string media, string isolatedWork)
        {
            Window = new(new BusinessLaunch(isolatedWork, isolatedWork, null, null), (_, _) => Task.FromResult(media),
                initialLanguage: "ru-RU");
            // Retain the real media inspection and rendering. Isolate live installation and continuation probes.
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(Ipc.Desktop.StartupSetupWindow).GetProperty("CurrentInstallation", instance)!
                .SetValue(Window, (Func<string, DeploymentLocation?>)(_ => null));
            typeof(Ipc.Desktop.StartupSetupWindow).GetProperty("DiscoverInstallation", instance)!
                .SetValue(Window, (Func<string, string, InstallationContinuation?>)((_, _) => null));
        }

        public void Dispose() => Window.Close();
    }

    static async Task CaptureWorkstation(string root, string evidence, List<string> lines)
    {
        const string password = "SYNTH-SETTINGS-12345";
        await using (var fixture = new ManualSupplyHarness(root, Path.Combine(evidence, "supply"), deferred: false))
        {
            fixture.Peer.HeartbeatTransform = body => body.Replace("\"TASKING\"", "\"IDLE\"", StringComparison.Ordinal);
            fixture.Peer.ReplyWaveNo = "WAVE-001";
            fixture.Peer.ReplyQuantity = "1";
            await fixture.StartAsync();
            foreach (var camera in fixture.Factory.Cameras.Values) camera.ConfirmConnected();
            await fixture.App.ConfigureManagementPasswordAsync(password);
            fixture.Peer.ReplySlot = "CH-08";
            using var theme = new WorkstationThemeBinding("sportmaster", System.Windows.Application.Current.Resources, Dispatcher.CurrentDispatcher,
                warning => throw new InvalidOperationException(warning));
            using var language = new WorkstationLanguageBinding("sportmaster", System.Windows.Application.Current.Resources, Dispatcher.CurrentDispatcher,
                warning => throw new InvalidOperationException(warning));
            var window = new SupplyStationWindow(fixture.App, null, "standard");
            await using var owner = new NativeWindowOwner(window);
            window.Show();
            var model = window.ViewModel;
            await AwaitUi(() => model.Fresh && model.Left.CanSubmitBarcode);
            var left = await fixture.App.SubmitParcelBarcodeAsync(await fixture.Command("left", "6950123456833"), default);
            if (left.OperationId is null) throw new InvalidOperationException(left.Detail);
            await AwaitUi(() => model.Left.ShowConfirmedTask && model.Left.Slot == "CH-08" && model.Right.CenteredStateTitle == "等待投包");
            await Capture(window, 1536, 816, evidence, "work-success-waiting", lines);
            AssertStatusDot(window, SupplyStationViewModel.StationStatusTone.Ready, "#FF147450");
            await Capture(window, 1536, 816, evidence, "work-theme-light", lines);
            var themedBefore = await fixture.App.SnapshotAsync();
            var requestsBeforeTheme = fixture.Peer.Requests.Count;
            PresentationPreferences.SaveTheme("sportmaster", WorkstationThemeManager.IndustrialDarkId);
            await AwaitUi(() => window.Background is SolidColorBrush background && background.Color == (Color)ColorConverter.ConvertFromString("#0F1722"));
            await Capture(window, 1536, 816, evidence, "work-theme-dark", lines);
            foreach (var position in Visuals<SupplyStationPosition>(window))
            {
                var disabled = (Button)position.FindName("DisableSupply");
                var heading = Visuals<TextBlock>(position).Single(text => AutomationProperties.GetName(text) == "投递位");
                if (disabled.Background is not SolidColorBrush buttonColor || buttonColor.Color != (Color)ColorConverter.ConvertFromString("#172231") ||
                    heading.Foreground is not SolidColorBrush headingColor || headingColor.Color != (Color)ColorConverter.ConvertFromString("#F1F5F9"))
                    throw new InvalidOperationException("The dark workstation retained an unthemed lane switch or heading.");
            }
            var themedAfter = await fixture.App.SnapshotAsync();
            if (model.Left.Slot != "CH-08" || !model.Left.ShowConfirmedTask ||
                themedBefore.RuntimeId != themedAfter.RuntimeId || themedBefore.Configuration.Revision != themedAfter.Configuration.Revision ||
                themedBefore.Paused != themedAfter.Paused || fixture.Peer.Requests.Count != requestsBeforeTheme ||
                !Devices(themedBefore).SequenceEqual(Devices(themedAfter)))
                throw new InvalidOperationException("Live palette replacement changed the workstation state or sent another request.");
            lines.Add("PASS THEME the same workstation retains its identity, current result, device epochs, configuration revision and supply state after live replacement");
            await VerifyWorkstationLanguages(fixture, window, left.OperationId, evidence, lines);

            PresentationPreferences.SaveTheme("sportmaster", WorkstationThemeManager.IndustrialLightId);
            await AwaitUi(() => window.Background is SolidColorBrush background && background.Color == (Color)ColorConverter.ConvertFromString("#F1F4F7"));
            var mute = (ToggleButton)window.FindName("MuteToggleButton");
            if (!mute.IsVisible || mute.IsChecked != false || mute.ActualWidth != 44 || mute.ActualHeight != 44)
                throw new InvalidOperationException("The 44 DIP cue control must start audible on a new profile.");
            mute.IsChecked = true;
            if (!Equals(AutomationProperties.GetName(mute), "恢复提示音（80%）") ||
                PresentationPreferences.ReadCueVolume("sportmaster") != PresentationPreferences.DefaultCueVolume ||
                (await fixture.App.SnapshotAsync()).Paused)
                throw new InvalidOperationException("Temporary mute changed the saved volume or paused supply.");
            await Capture(window, 1536, 816, evidence, "work-muted", lines);
            mute.IsChecked = false;

            fixture.Peer.Mode = "reject";
            fixture.Peer.RejectionMessage = string.Concat(Enumerable.Repeat("当前包裹未取得可用格口，请保留包裹和本条记录，联系维护人员核对原结果。", 7));
            var right = await fixture.App.SubmitParcelBarcodeAsync(await fixture.Command("right", "6950123456796"), default);
            if (right.OperationId is null) throw new InvalidOperationException(right.Detail);
            await AwaitUi(() => model.Right.CenteredRcsRejection);
            if (model.Right.ShowRcsGuidance)
                throw new InvalidOperationException("A confirmed business rejection must keep its own result view.");
            await Capture(window, 1536, 816, evidence, "work-long-rejection", lines);

            var rightPosition = Visuals<SupplyStationPosition>(window).Single(control => ReferenceEquals(control.DataContext, model.Right));
            var remove = (Button)rightPosition.FindName("ConfirmManualPlacementRemoval");
            await AwaitUi(() => remove.IsVisible && remove.IsEnabled);
            Press(remove);
            await AwaitUi(() => model.Right.CurrentRound is null && model.Right.CanSubmitBarcode);
            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Peer.Hold = hold;
            try
            {
                var pending = await fixture.App.SubmitParcelBarcodeAsync(await fixture.Command("right", "6950123456789"), default);
                if (pending.OperationId is null) throw new InvalidOperationException(pending.Detail);
                await AwaitUi(() => model.Right.Operation?.Wcs?.State == StageState.Unknown && model.Right.ShowRcsGuidance);
                var guidance = (ScrollViewer)rightPosition.FindName("RcsGuidancePanel");
                var details = (Button)rightPosition.FindName("WorkDetails");
                if (!guidance.IsVisible || model.Right.RcsGuidanceBarcode != "6950123456789" ||
                    model.Right.RcsGuidanceBarcodeSuffix != "后 4 位：6789" ||
                    !model.Right.RcsGuidanceSummary.StartsWith("取格口响应超时", StringComparison.Ordinal) ||
                    !remove.IsVisible || !details.IsEnabled || model.Right.CanSubmitBarcode)
                    throw new InvalidOperationException("The timed-out current parcel must show its barcode, recovery actions and existing submission block.");
                await Capture(window, 1536, 816, evidence, "work-unknown-guidance", lines);
                AssertStatusDot(window, SupplyStationViewModel.StationStatusTone.Warning, "#FF895509");
                if (guidance.ScrollableHeight > 1)
                    throw new InvalidOperationException("All three recovery steps must be visible at the regular workstation size.");
                var glyphs = GlyphRuns(guidance).ToArray();
                if (glyphs.Length == 0 || glyphs.Any(run =>
                    !run.GlyphTypeface.FontUri.ToString().Contains("NotoSansCJKsc-", StringComparison.OrdinalIgnoreCase) ||
                    run.GlyphTypeface.Weight.ToOpenTypeWeight() is not (400 or 500) ||
                    run.GlyphIndices.Any(index => index == 0)))
                    throw new InvalidOperationException("The visible recovery guidance must use the shipped Noto glyphs at 400 or 500.");
                lines.Add("PASS FONT visible RCS guidance uses only the shipped Noto Sans CJK SC glyphs");
                try
                {
                    PresentationPreferences.SaveLanguage("sportmaster", "ru-RU");
                    await Capture(window, 1366, 768, evidence, "work-unknown-guidance-ru", lines);
                    if (!guidance.IsVisible || model.Right.RcsGuidanceBarcode != "6950123456789" || !remove.IsVisible || !details.IsEnabled)
                        throw new InvalidOperationException("Russian recovery guidance lost the original barcode or recovery controls.");
                }
                finally
                {
                    PresentationPreferences.SaveLanguage("sportmaster", "zh-CN");
                    await NativeClientSize.Set(window, (FrameworkElement)window.Content, 1536, 816);
                }

                var sends = fixture.Peer.Requests.Count;
                Press(remove);
                await AwaitUi(() => model.Right.CurrentRound is null && model.Right.ShowRcsGuidance && !remove.IsVisible);
                if (model.Right.RcsGuidanceBarcode.Length != 0 || model.Right.CanSubmitBarcode ||
                    model.Right.Arrival?.TargetOperation != pending.OperationId || fixture.Peer.Requests.Count != sends || !details.IsEnabled)
                    throw new InvalidOperationException("Removing the parcel must clear the barcode and preserve the original remote fence and details without a new send.");
                Press(details);
                await AwaitUi(() => model.SelectedPage == "history" && model.Selected?.Id == pending.OperationId && !model.Busy);
                Press(Visuals<Button>(window).Single(button => Equals(button.Content, "作业")));
                await AwaitUi(() => model.SelectedPage == "workstation");
            }
            finally { fixture.Peer.Hold = null; hold.TrySetResult(); }

            Press(Visuals<Button>(window).Single(b => Equals(b.Content, "记录")));
            await AwaitUi(() => model.HistoryLoaded && !model.Busy);
            Press(Visuals<Button>(window).Single(b => Equals(b.Content, "今天")));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Press(Visuals<Button>(window).Single(b => AutomationProperties.GetAutomationId(b) == "History.Query"));
            await AwaitUi(() => !model.Busy && model.CanExportHistory);
            await Capture(window, 1536, 816, evidence, "records-jobs", lines);
            var roundTab = Visuals<TabItem>(window).Single(t => AutomationProperties.GetAutomationId(t) == "PositionHistory.ParcelRounds");
            roundTab.IsSelected = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var queryRounds = Visuals<Button>(window).Single(b => AutomationProperties.GetAutomationId(b) == "PositionHistory.QueryRounds");
            Press(queryRounds);
            await AwaitUi(() => queryRounds.IsEnabled && Visuals<DataGrid>(window).Any(grid => grid.IsVisible && grid.Items.Count > 0));
            await Capture(window, 1536, 816, evidence, "records-rounds", lines);

            Press((Button)window.FindName("StationSettingsEntry"));
            await AwaitUi(() => window.OwnedWindows.OfType<StationSettingsWindow>().Any(w => w.IsVisible));
            var station = window.OwnedWindows.OfType<StationSettingsWindow>().Single(w => w.IsVisible);
            await using var stationOwner = new NativeWindowOwner(station);
            await Capture(station, 1220, 800, evidence, "settings-readonly", lines);
            if (!Visuals<System.Windows.Controls.TextBox>(station).Any(value =>
                AutomationProperties.GetAutomationId(value) == "StationSettings.CueVolume" && value.Text == "80%"))
                throw new InvalidOperationException("The read-only station settings must show the default 80% cue volume.");
            Press((Button)station.FindName("EditSettings"));
            ManagementModeWindow? login = null;
            await AwaitUi(() =>
            {
                login = System.Windows.Application.Current.Windows.OfType<ManagementModeWindow>().FirstOrDefault(w => w.IsVisible);
                return login is not null && Visuals<PasswordBox>(login).Any(p => p.IsVisible && p.IsEnabled);
            });
            Visuals<PasswordBox>(login!).Single(p => p.IsVisible).Password = password;
            var enter = Visuals<Button>(login!).Single(b => b.Name == "EnterManagementMode");
            await AwaitUi(() => enter.IsEnabled);
            Press(enter);
            await AwaitUi(() => !login!.IsVisible && ((ContentControl)station.FindName("EditorHost")).IsVisible && model.HasLoadedConfiguration);
            if ((await fixture.App.SnapshotAsync()).Paused)
                throw new InvalidOperationException("Modeless station settings paused the workstation.");
            var policy = model.StructuredPolicy!;
            var savedStationCode = policy.StationCode;
            policy.StationCode = "SYNTH-EDIT-DRAFT";
            try
            {
                await AwaitUi(() => Visuals<Button>(station).Any(b => b.Name == "SaveChanges" && b.IsEnabled));
                await Capture(station, 1220, 800, evidence, "settings-edit-unpaused", lines);
                var editor = Visuals<MaintenancePage>(station).Single();
                var reader = model.ReaderDrafts.Single(draft => draft.Capability.Role == "left").Capability;
                if (!editor.NavigateToParameter(reader.InstanceId + "/device.ip"))
                    throw new InvalidOperationException("The left camera settings could not be opened from station settings.");
                await Capture(station, 1220, 800, evidence, "settings-edit-cameras", lines);
                var cameraEditor = Visuals<ParameterSettingsControl>(station).Single(editor => editor.Model is not null);
                await CaptureDiscovery(station, cameraEditor, evidence, lines);
                if (!editor.NavigateToParameter("AutoStart"))
                    throw new InvalidOperationException("AutoStart was not exposed in the field preferences page.");
                await Capture(station, 1220, 800, evidence, "settings-edit-preferences", lines);
                if (!editor.NavigateToParameter(nameof(SmDisplayPreferenceDraft.ThemeId)))
                    throw new InvalidOperationException("Theme is missing from display and cue preferences.");
                await Capture(station, 1220, 800, evidence, "settings-edit-preferences-theme", lines);
                var themePicker = Visuals<ComboBox>(station).Single(control => AutomationProperties.GetAutomationId(control) == "StationSettings.Theme");
                if (!themePicker.IsEnabled || themePicker.ActualHeight < 44 || !Equals(themePicker.SelectedValue, WorkstationThemeManager.IndustrialLightId))
                    throw new InvalidOperationException("Theme selector must show the current preference and preserve a 44 DIP hit area.");
                lines.Add("PASS THEME day-to-day settings show the 44 DIP theme selector");
                var autoStart = Visuals<CheckBox>(station).Single(control => AutomationProperties.GetAutomationId(control) == "StationSettings.AutoStart");
                if (autoStart.IsEnabled)
                    throw new InvalidOperationException("A source checkout must not be able to write the current Windows Startup folder.");
                if ((await fixture.App.SnapshotAsync()).Paused)
                    throw new InvalidOperationException("Camera settings paused the workstation.");
            }
            finally { policy.StationCode = savedStationCode; }

            var beforeSave = await fixture.App.SnapshotAsync();
            var themeControl = Visuals<ComboBox>(station).Single(control => AutomationProperties.GetAutomationId(control) == "StationSettings.Theme");
            var languageControl = Visuals<ComboBox>(station).Single(control => AutomationProperties.GetAutomationId(control) == "StationSettings.Language");
            if (!languageControl.IsEnabled || languageControl.Items.Count != 3 || languageControl.ActualHeight < 44)
                throw new InvalidOperationException("Display preferences must show the 44 DIP language selector with three languages.");
            languageControl.SelectedValue = "ru-RU";

            themeControl.SelectedValue = WorkstationThemeManager.IndustrialDarkId;
            await AwaitUi(() => model.HasUnsavedDisplayPreferences && !model.HasUnsavedConfiguration);
            Press(Visuals<Button>(station).Single(button => button.Name == "SaveChanges"));
            await AwaitUi(() => !model.HasUnsavedDisplayPreferences && window.Background is SolidColorBrush background &&
                background.Color == (Color)ColorConverter.ConvertFromString("#0F1722"));
            var afterSave = await fixture.App.SnapshotAsync();
            if (afterSave.Configuration.Revision != beforeSave.Configuration.Revision || afterSave.Paused != beforeSave.Paused ||
                !Devices(beforeSave).SequenceEqual(Devices(afterSave)) ||
                PresentationPreferences.ReadTheme("sportmaster", WorkstationThemeManager.IndustrialLightId) != WorkstationThemeManager.IndustrialDarkId ||
                !Equals(station.FindResource("Background"), window.FindResource("Background")))
                throw new InvalidOperationException("The normal save must apply local theme to both windows without changing business configuration or pausing.");
            if (PresentationPreferences.ReadLanguage("sportmaster") != "ru-RU" ||
                !Equals(station.FindResource("Common.SetupDisplay"), "Экран и звуки") ||
                !Equals(window.FindResource("Common.SetupDisplay"), "Экран и звуки"))
                throw new InvalidOperationException("The normal settings save did not apply Russian to both open windows.");
            await Capture(station, 1220, 800, evidence, "settings-preferences-lang-theme-ru", lines);
            lines.Add("PASS THEME AND LANGUAGE normal settings save updates both open windows without advancing business configuration or pausing");
            PresentationPreferences.SaveLanguage("sportmaster", "zh-CN");
            PresentationPreferences.SaveTheme("sportmaster", WorkstationThemeManager.IndustrialLightId);

            var snapshot = await fixture.App.SnapshotAsync();
            var rightControl = snapshot.Positions!.Single(position => position.Id == "right").SupplyControl!;
            await fixture.App.SetPositionEnabledAsync(new(Wire.Id(), snapshot.RuntimeId, "right", rightControl.Revision, false), default);
            await AwaitUi(() => model.Right.SupplyDisabled && model.OperatorStatusTone == SupplyStationViewModel.StationStatusTone.Warning);
            AssertStatusDot(window, SupplyStationViewModel.StationStatusTone.Warning, "#FF895509");

            fixture.Factory.RcsOffline = true;
            await AwaitUi(() => model.RcsState == SupplyStationViewModel.RcsConnectionState.Offline);
            AssertStatusDot(window, SupplyStationViewModel.StationStatusTone.Blocked, "#FFB52F38");
        }

        await using var unconfigured = new ManualSupplyHarness(root, Path.Combine(evidence, "empty"), deferred: true);
        unconfigured.Config.Wcs!.Parameters["left-bcr-code"] = "";
        unconfigured.Config.Wcs.Parameters["right-bcr-code"] = "";
        ExtensionStore.AtomicWrite(Path.Combine(unconfigured.Launch.ConfigurationDirectory, "business.json"), unconfigured.Config);
        await unconfigured.StartAsync(ready: false);
        var empty = new SupplyStationWindow(unconfigured.App, null, "standard");
        await using var emptyOwner = new NativeWindowOwner(empty);
        empty.Show();
        await AwaitUi(() => empty.ViewModel.Fresh);
        await Capture(empty, 1536, 816, evidence, "work-unconfigured", lines);
        AssertStatusDot(empty, SupplyStationViewModel.StationStatusTone.Blocked, "#FFB52F38");
    }

    static IEnumerable<(string, string)> Devices(ParcelSnapshot snapshot) =>
        (snapshot.Devices ?? []).Select(device => (device.DeviceId, device.Epoch)).OrderBy(device => device.DeviceId, StringComparer.Ordinal);

    static async Task VerifyWorkstationLanguages(ManualSupplyHarness fixture, SupplyStationWindow window, string operationId, string evidence, List<string> lines)
    {
        var before = await fixture.App.SnapshotAsync();
        var requests = fixture.Peer.Requests.Count;
        var model = window.ViewModel;
        var original = await fixture.App.OperationAsync(operationId)
            ?? throw new InvalidOperationException("The original work record is unavailable.");
        var frozenRequest = Wire.Encode(original.Wcs!.Request);
        PresentationPreferences.SaveTheme("sportmaster", WorkstationThemeManager.IndustrialLightId);
        try
        {
            PresentationPreferences.SaveLanguage("sportmaster", "en-US");
            await AwaitUi(() => model.Right.CenteredStateTitle == "Waiting for a parcel");
            await Capture(window, 1366, 768, evidence, "work-en-light", lines);
            PresentationPreferences.SaveLanguage("sportmaster", "ru-RU");
            await AwaitUi(() => model.Right.CenteredStateTitle == "Ожидается посылка");
            await Capture(window, 1366, 768, evidence, "work-ru-light", lines);
            PresentationPreferences.SaveTheme("sportmaster", WorkstationThemeManager.IndustrialDarkId);
            await AwaitUi(() => window.Background is SolidColorBrush brush && brush.Color == Color.FromRgb(15, 23, 34));
            await Capture(window, 1280, 760, evidence, "work-ru-dark", lines);
            await Capture(window, 800, 420, evidence, "work-ru-compact", lines);
            foreach (var lane in Visuals<SupplyStationPosition>(window))
                foreach (var name in new[] { "EnableSupply", "DisableSupply" })
                    if (lane.FindName(name) is Button { IsVisible: true } button && (button.ActualHeight < 44 || button.ActualWidth < 44))
                        throw new InvalidOperationException("Russian lane control is smaller than the required touch target.");

            var after = await fixture.App.SnapshotAsync();
            var retained = await fixture.App.OperationAsync(operationId);
            if (before.Configuration.Revision != after.Configuration.Revision || before.RuntimeId != after.RuntimeId ||
                before.Paused != after.Paused || !Devices(before).SequenceEqual(Devices(after)) || fixture.Peer.Requests.Count != requests ||
                retained?.Slot != "CH-08" || retained.ParcelCode != original.ParcelCode || Wire.Encode(retained.Wcs!.Request) != frozenRequest)
                throw new InvalidOperationException("Language replacement changed configuration, runtime, devices, or original facts.");
            lines.Add("PASS LANGUAGE same native window switched zh-CN to en-US to ru-RU, retaining original chute, task, runtime and device state");
        }
        finally
        {
            PresentationPreferences.SaveLanguage("sportmaster", "zh-CN");
            PresentationPreferences.SaveTheme("sportmaster", WorkstationThemeManager.IndustrialLightId);
            await NativeClientSize.Set(window, (FrameworkElement)window.Content, 1536, 816);
        }
    }

    static async Task CaptureDiscovery(Window station, ParameterSettingsControl editor, string evidence, List<string> lines)
    {
        var model = editor.Model!;
        var original = model.CameraDiscoverer;
        model.CameraDiscoverer = _ => Task.FromResult<DiscoveredCamera[]>([
            new("192.0.2.21", "SYNTH-OFFLINE-0", "MV-ID Series", "1.1", false),
            new("192.0.2.22", "SYNTH-OFFLINE-1", "MV-ID Series", "1.1", false),
            new("192.0.2.23", "SYNTH-CONFLICT", "MV-ID Series", "1.1", true)
        ]);
        try
        {
            Press(Visuals<Button>(station).Single(button => AutomationProperties.GetAutomationId(button) == "CameraDiscovery.Open"));
            Window? picker = null;
            await AwaitUi(() => (picker = station.OwnedWindows.OfType<Window>().SingleOrDefault(window =>
                window.IsVisible && window.Title == "扫描局域网相机")) is not null && Visuals<TextBlock>(picker).Any(text =>
                    text.Text == "地址冲突，请先修改相机 IP"));
            await using var pickerOwner = new NativeWindowOwner(picker!);
            var choices = Visuals<Button>(picker!).Where(button => Equals(button.Content, "设为左相机") || Equals(button.Content, "设为右相机")).ToArray();
            if (choices.Length != 6 || choices.Count(choice => choice.IsEnabled) != 2)
                throw new InvalidOperationException("Conflict rows and bindings owned by the opposite side must disable their actions.");
            await Capture(picker!, 900, 480, evidence, "camera-discovery-dialog", lines);
        }
        finally { model.CameraDiscoverer = original; }
    }

    static async Task CaptureUncalibrated(string root, string evidence, List<string> lines)
    {
        var folder = Path.Combine(evidence, "uncalibrated");
        await using var fixture = new ManualSupplyHarness(root, folder, deferred: false)
        { CameraSource = binding => P1PreviewCamera.Create(binding, folder) };
        fixture.Peer.HeartbeatTransform = body => body.Replace("\"TASKING\"", "\"IDLE\"", StringComparison.Ordinal);
        await fixture.StartAsync();
        var window = new SupplyStationWindow(fixture.App, null, "standard");
        await using var owner = new NativeWindowOwner(window);
        window.Show();
        await AwaitUi(() => window.ViewModel.Left.ShowUncalibratedHint && window.ViewModel.Left.CanSubmitBarcode);
        await Capture(window, 1536, 816, evidence, "work-uncalibrated-hint", lines);
    }

    static void AssertStatusDot(SupplyStationWindow window, SupplyStationViewModel.StationStatusTone expected, string color)
    {
        var dot = (System.Windows.Shapes.Ellipse)window.FindName("WorkStatusDot");
        if (window.ViewModel.OperatorStatusTone != expected || dot.Fill.ToString() != color)
        {
            var model = window.ViewModel;
            var reasons = string.Join(" / ", model.Snapshot?.Positions?.SelectMany(p => p.BlockingReasons) ?? []);
            throw new InvalidOperationException($"Station status: {model.OperatorStatus} · {dot.Fill} · {reasons}");
        }
    }

    static async Task CaptureFirstConfiguration(string evidence, List<string> lines)
    {
        var launch = new BusinessLaunch(Path.Combine(evidence, "first-config"), Path.Combine(evidence, "first-data"), null, null);
        ManagementModeFixture.AuthorizeRoot(launch.DataRoot);
        var type = Assembly.Load("Ipc.Desktop").GetType("Ipc.Desktop.StartupRepairWindow", throwOnError: true)!;
        var wizard = (Window)Activator.CreateInstance(type, [launch, null, true])!;
        type.GetProperty("ConfirmDiscardInputs", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(wizard, (Func<bool>)(() => true));
        await using var owner = new NativeWindowOwner(wizard);
        wizard.Show();
        ParameterSettingsControl? editor = null;
        await AwaitUi(() => (editor = Visuals<ParameterSettingsControl>(wizard).FirstOrDefault()) is { IsVisible: true, IsEnabled: true, Model: not null });
        await Capture(wizard, 1080, 700, evidence, "setup-step-1", lines);
        var step = 0;
        foreach (var label in new[] { "继续：左右相机", "继续：识读规则", "检查配置", "保存并进入" })
        {
            var actionId = label == "保存并进入" ? "Startup.SaveAndStart" : "Startup.Next";
            Button? primary = null;
            await AwaitUi(() => (primary = Visuals<Button>(wizard).FirstOrDefault(button =>
                button.IsVisible && button.IsEnabled && Equals(button.Content, label) &&
                AutomationProperties.GetAutomationId(button) == actionId)) is not null);
            var visible = ((Panel)primary!.Parent).Children.OfType<Button>().Where(button => button.IsVisible).ToArray();
            if (visible[^1] != primary)
                throw new InvalidOperationException($"Wizard primary action is not last: {label}");
            ++step;
            if (step == 2)
            {
                if (!Visuals<Button>(wizard).Any(button => button.IsVisible && AutomationProperties.GetAutomationId(button) == "CameraDiscovery.Open"))
                    throw new InvalidOperationException("First setup cameras page did not expose discovery.");
                await Capture(wizard, 1080, 700, evidence, "setup-step-2-cameras", lines);
            }
            if (step == 3) await Capture(wizard, 1080, 700, evidence, "setup-step-3", lines);
            if (label != "保存并进入") Press(primary);
        }
        await Capture(wizard, 1080, 700, evidence, "setup-step-4", lines);
        var appearance = Visuals<Expander>(wizard).Single(panel => panel.Header is string title && title.StartsWith("维护与显示", StringComparison.Ordinal));
        appearance.IsExpanded = true;
        await AwaitUi(() => Visuals<ComboBox>(wizard).Any(choice => choice.IsVisible && choice.IsEnabled &&
            AutomationProperties.GetAutomationId(choice) == "StationSettings.Theme"));
        lines.Add("PASS THEME first configuration exposes the theme preference in optional display settings");
    }
}
