using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyIndependentSettings(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS INDEPENDENT-SETTINGS " + text); }
        async Task Until(Func<bool> ready) => await CameraRoundHarness.Until(async () =>
        { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); return ready(); });
        void Press(Button button) => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
        await using var h = new ManualSupplyHarness(root, Path.Combine(evidence, "independent-settings"), deferred: true);
        await h.StartAsync();
        const string password = "SYNTH-SETTINGS-12345";
        await h.App.ConfigureManagementPasswordAsync(password);
        await h.App.EnterManagementModeAsync(password);
        var priorSession = (await h.App.ManagementModeStatusAsync()).SessionId;
        var window = new SupplyStationWindow(h.App, null, "standard") { ConfirmDiscardDrafts = _ => true };
        window.Show(); var vm = window.ViewModel;
        await Until(() => vm.Fresh); await SetClient(window, 1366, 768);
        var entry = (Button)window.FindName("StationSettingsEntry");
        Check(entry.IsVisible, "ordinary workstation exposes its independent Settings entry");
        async Task<StationSettingsWindow> Open()
        {
            Press(entry); await Until(() => System.Windows.Application.Current.Windows.OfType<StationSettingsWindow>().Any(w => w.IsVisible));
            return System.Windows.Application.Current.Windows.OfType<StationSettingsWindow>().Single();
        }
        async Task<ManagementModeWindow> BeginEdit(StationSettingsWindow settings)
        {
            Press((Button)settings.FindName("EditSettings"));
            await Until(() => System.Windows.Application.Current.Windows.OfType<ManagementModeWindow>().Any(w => w.IsVisible));
            var login = System.Windows.Application.Current.Windows.OfType<ManagementModeWindow>().Single();
            await Until(() => Descendants(login).OfType<PasswordBox>().Any(p => p.Name == "ManagementPassword" && p.IsVisible && p.IsEnabled));
            return login;
        }
        async Task Authenticate(StationSettingsWindow settings)
        {
            var login = await BeginEdit(settings);
            Descendants(login).OfType<PasswordBox>().Single(p => p.Name == "ManagementPassword").Password = password;
            Press(Descendants(login).OfType<Button>().Single(b => b.Name == "EnterManagementMode"));
            try { await Until(() => !login.IsVisible && ((ContentControl)settings.FindName("EditorHost")).IsVisible && vm.HasLoadedConfiguration && !vm.Busy); }
            catch (TimeoutException error) { throw new InvalidOperationException($"Settings login visible={login.IsVisible}; entered={login.Entered}; authorized={vm.IsManagementMode}; fresh={vm.Fresh}; loaded={vm.HasLoadedConfiguration}; busy={vm.Busy}; feedback={vm.Message}", error); }
        }
        KeyboardTraversal.CheckWindow(window, "workstation", Check);
        var first = await Open();
        CaptureInternal(evidence, "settings-independent-readonly", (FrameworkElement)first.Content, lines);
        Check(((ScrollViewer)first.FindName("ReadOnlyBody")).IsVisible && !((ContentControl)first.FindName("EditorHost")).IsVisible,
            "a foreign existing grant cannot turn the new settings window into an editor");
        TextBox Field(string id) => Descendants(first).OfType<TextBox>().Single(field =>
            AutomationProperties.GetAutomationId(field) == "StationSettings." + id && field.IsVisible && field.IsReadOnly);
        Check(Field("Camera.left.Code").Text == h.Config.Wcs!.Parameters["left-bcr-code"] &&
            Field("Camera.right.Code").Text == h.Config.Wcs.Parameters["right-bcr-code"] &&
            Field("RcsHost").Text == new Uri(h.Config.Wcs.Target).GetLeftPart(UriPartial.Authority),
            "ordinary field summary shows actual configured RCS host and camera codes rather than misreading a redacted configuration as missing");
        Check(!(await h.App.SnapshotAsync()).Paused && (await h.App.ManagementModeStatusAsync()).SessionId == priorSession,
            "read-only viewing neither pauses production nor revokes the existing grant");
        var cancel = await BeginEdit(first); cancel.Close();
        await Until(() => !cancel.IsVisible);
        Check((await h.App.ManagementModeStatusAsync()).SessionId == priorSession && !(await h.App.SnapshotAsync()).Paused,
            "canceling fresh password verification preserves the original authorization and production state");
        await Authenticate(first);
        var fields = Descendants(first).OfType<ParameterSettingsControl>().Single();
        var readers = vm.ReaderDrafts.Select(d => d.Capability.InstanceId).ToArray();
        Check(fields.Audience == SmParameterAudience.Field && readers.Length == 2 &&
            fields.NavigateTo(readers[0] + "/device.ip"), "authorized workstation opens its field-owned camera card");
        first.UpdateLayout();
        Check(!(await h.App.SnapshotAsync()).Paused && ((TextBlock)first.FindName("AuthorizationText")).Text.StartsWith("编辑授权至 ") &&
            readers.All(id => !fields.NavigateTo(id + "/code.formats") && !fields.NavigateTo(id + "/reader.engine")) &&
            !Descendants(fields).OfType<FrameworkElement>().Any(e => AutomationProperties.GetAutomationId(e).EndsWith("/code.formats")) &&
            new[] { "left-bcr-code", "right-bcr-code" }.All(key => Descendants(fields).OfType<TextBox>()
                .Any(box => box.IsVisible && AutomationProperties.GetAutomationId(box).EndsWith("/" + key))),
            "unpaused editor exposes both camera codes and excludes professional readers and barcode formats");
        var hints = Descendants(fields).OfType<TextBlock>().Where(t => t.IsVisible &&
            (t.Text.StartsWith("根据 IP 自动读取") || t.Text.Contains("投递位取格口与心跳共用的相机编码"))).ToArray();
        Check(hints.Length == 4 && hints.All(t => t.TextWrapping == TextWrapping.Wrap && t.TextTrimming == TextTrimming.None &&
            t.ActualHeight > t.FontSize * 1.5), "both serial numbers and both lane codes display complete multiline operator guidance");
        KeyboardTraversal.CheckWindow(first, "authorized settings", Check);
        CaptureInternal(evidence, "settings-unpaused-cameras", (FrameworkElement)first.Content, lines);
        first.Width = 760; first.Height = 480;
        first.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        CaptureInternal(evidence, "settings-independent-editor-minimum", first, lines);
        Check(!(await h.App.SnapshotAsync()).Paused && !System.Windows.Application.Current.Windows.OfType<OperationsViewFixture>().Any(),
            "fresh settings authorization and complete draft loading do not acquire the management production fence");
        var editor = ((ContentControl)first.FindName("EditorHost")).Content;
        vm.StructuredPolicy!.StationCode = "SYNTH-UNSAVED-SETTINGS";
        first.Close(); await Until(() => !first.IsVisible);
        Check(!(await h.App.ManagementModeStatusAsync()).Authorized && vm.StructuredPolicy.StationCode == "SYNTH-UNSAVED-SETTINGS" && vm.HasUnsavedConfiguration,
            "closing ends this grant while preserving the one complete station draft");
        var second = await Open(); await Authenticate(second);
        Check(ReferenceEquals(editor, ((ContentControl)second.FindName("EditorHost")).Content) && vm.StructuredPolicy.StationCode == "SYNTH-UNSAVED-SETTINGS",
            "reopening reuses original controls and draft after a new password verification");
        var back = (Button)second.FindName("ReturnToWorkstation");
        var retainedSession = (await h.App.ManagementModeStatusAsync()).SessionId;
        await BarcodeNativeKeyboard.ClickAsync(second, back, new Point(12, 12));
        await Until(() => window.IsActive);
        Check(window.IsEnabled && !second.IsVisible && vm.StructuredPolicy.StationCode == "SYNTH-UNSAVED-SETTINGS" &&
            (await h.App.ManagementModeStatusAsync()).SessionId == retainedSession,
            "returning hides the owned editor so the workstation is unobstructed, retaining its draft and grant");
        var right = Descendants(window).OfType<SupplyStationPosition>().Single(p => ReferenceEquals(p.DataContext, vm.Right));
        var barcode = (TextBox)right.FindName("BarcodeEntry");
        var textEvents = 0; var enterEvents = 0;
        barcode.PreviewTextInput += (_, _) => textEvents++;
        barcode.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, e) => { if (e.Key == Key.Enter) enterEvents++; }), true);
        await BarcodeNativeKeyboard.ClickAsync(window, barcode, new Point(12, 12));
        Check(window.IsActive && barcode.IsKeyboardFocusWithin, "a real mouse click selects the visible workstation PDA field");
        const string pdaCode = "000123456789";
        await BarcodeNativeKeyboard.SendAsync(window, barcode, pdaCode.Select(c => (ushort)c).Append((ushort)0x0d).ToArray());
        await Until(() => vm.Right.SlotAvailable && !vm.Busy);
        var submitted = vm.Right.Operation!;
        var original = submitted.Wcs!.Request;
        Check(h.Peer.Requests.Count == 1 && textEvents == pdaCode.Length && enterEvents == 1 &&
            JsonDocument.Parse(h.Peer.Requests.Single()).RootElement.GetProperty("source").GetString() == "PDA",
            "native SendInput reaches the workstation HWND, text/Enter handlers and exactly one PDA request while settings stay open");
        var left = Descendants(window).OfType<SupplyStationPosition>().Single(p => ReferenceEquals(p.DataContext, vm.Left));
        var manual = (TextBox)left.FindName("BarcodeEntry");
        await BarcodeNativeKeyboard.ClickAsync(window, manual, new Point(12, 12));
        Check(window.IsActive && manual.IsKeyboardFocusWithin, "a real mouse click selects the visible workstation manual field");
        foreach (var digit in "000987") { await BarcodeNativeKeyboard.SendAsync(window, manual, [(ushort)digit]); await Task.Delay(300); }
        await BarcodeNativeKeyboard.SendAsync(window, manual, [0x0d]);
        await Until(() => vm.Left.SlotAvailable && !vm.Busy);
        Check(h.Peer.Requests.Count == 2 && h.Peer.Requests.Any(body => JsonDocument.Parse(body).RootElement.GetProperty("source").GetString() == "MANUAL"),
            "native slow keyboard input uses the original MANUAL route without closing or pausing the settings draft");
        Check(ReferenceEquals(await Open(), second) && ReferenceEquals(editor, ((ContentControl)second.FindName("EditorHost")).Content) &&
            vm.StructuredPolicy.StationCode == "SYNTH-UNSAVED-SETTINGS" && (await h.App.ManagementModeStatusAsync()).SessionId == retainedSession,
            "returning to settings restores the same window, controls, complete draft and unexpired grant");
        await vm.RefreshAsync();
        Check(await vm.PrepareConfigurationReviewAsync(), "the complete existing candidate validates without a prior management pause");
        var review = new ConfigurationSaveReviewWindow(second, vm);
        review.Show();
        Press(Descendants(review).OfType<Button>().Single(b => b.Name == "ConfirmSaveChanges"));
        await Until(() => !vm.Busy && vm.ConfigurationSubmissionOutcome.Length > 0);
        await vm.RefreshAsync();
        Check(vm.Snapshot!.Configuration.Pending?.StationCode == "SYNTH-UNSAVED-SETTINGS" && !vm.Snapshot.Paused,
            "saving uses original Pending/CAS behavior and leaves actual production pause unchanged: " + vm.ConfigurationSubmissionOutcome + " / " + vm.Message);
        review.Close();
        Check(Wire.Encode((await h.App.OperationAsync(submitted.Id))!.Wcs!.Request) == Wire.Encode(original) && h.Peer.Requests.Count == 2,
            "configuration editing cannot rewrite or resend an original request");
        await BarcodeNativeKeyboard.ClickAsync(second, back, new Point(12, 12));
        Check(!second.IsVisible, "returning to work after save keeps the settings window hidden until explicitly restored");
        await vm.RequestActivationAsync!();
        window.Close();
        Check(window.IsVisible && second.IsVisible && !(await h.App.SnapshotAsync()).Paused,
            "reopen and owner exit cannot silently pause or discard the active settings window");
        vm.StructuredPolicy.StationCode = "SYNTH-RETAIN-ON-REVOCATION";
        var old = (await h.App.ManagementModeStatusAsync()).SessionId;
        await h.App.EnterManagementModeAsync(password); var replacement = (await h.App.ManagementModeStatusAsync()).SessionId;
        await vm.RefreshManagementModeAsync();
        await Until(() => !((ContentControl)second.FindName("EditorHost")).IsVisible);
        Check(vm.StructuredPolicy.StationCode == "SYNTH-RETAIN-ON-REVOCATION" && old != replacement,
            "replacement authorization locks the old editor and preserves unsaved input");
        CaptureInternal(evidence, "settings-independent-locked", second, lines);
        second.Close(); await Until(() => !second.IsVisible);
        Check((await h.App.ManagementModeStatusAsync()).SessionId == replacement && (await h.App.ManagementModeStatusAsync()).Authorized,
            "late closure of an older settings window cannot revoke the replacement grant");
        await h.App.ExitManagementModeAsync();
        window.Close(); await Until(() => !window.IsVisible);
        Check(!(await h.App.SnapshotAsync()).Paused, "settings lifetime never manufactures a pause or resume command");
    }
}
