using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
            await Until(() => Descendants(login).OfType<PasswordBox>().Any(p => p.Name == "ManagementPassword" && p.IsVisible));
            return login;
        }
        async Task Authenticate(StationSettingsWindow settings)
        {
            var login = await BeginEdit(settings);
            Descendants(login).OfType<PasswordBox>().Single(p => p.Name == "ManagementPassword").Password = password;
            Press(Descendants(login).OfType<Button>().Single(b => b.Name == "EnterManagementMode"));
            await Until(() => !login.IsVisible && ((ContentControl)settings.FindName("EditorHost")).IsVisible && vm.HasLoadedConfiguration && !vm.Busy);
        }
        var first = await Open();
        CaptureInternal(evidence, "settings-independent-readonly", first, lines);
        Check(((ScrollViewer)first.FindName("ReadOnlyBody")).IsVisible && !((ContentControl)first.FindName("EditorHost")).IsVisible,
            "a foreign existing grant cannot turn the new settings window into an editor");
        Check(!(await h.App.SnapshotAsync()).Paused && (await h.App.ManagementModeStatusAsync()).SessionId == priorSession,
            "read-only viewing neither pauses production nor revokes the existing grant");
        var cancel = await BeginEdit(first); cancel.Close();
        await Until(() => !cancel.IsVisible);
        Check((await h.App.ManagementModeStatusAsync()).SessionId == priorSession && !(await h.App.SnapshotAsync()).Paused,
            "canceling fresh password verification preserves the original authorization and production state");
        await Authenticate(first);
        first.Width = 760; first.Height = 480;
        first.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        CaptureInternal(evidence, "settings-independent-editor-minimum", first, lines);
        Check(!(await h.App.SnapshotAsync()).Paused && !System.Windows.Application.Current.Windows.OfType<ManagementCenterWindow>().Any(),
            "fresh settings authorization and complete draft loading do not acquire the management production fence");
        var editor = ((ContentControl)first.FindName("EditorHost")).Content;
        vm.StructuredPolicy!.StationCode = "SYNTH-UNSAVED-SETTINGS";
        first.Close(); await Until(() => !first.IsVisible);
        Check(!(await h.App.ManagementModeStatusAsync()).Authorized && vm.StructuredPolicy.StationCode == "SYNTH-UNSAVED-SETTINGS" && vm.HasUnsavedConfiguration,
            "closing ends this grant while preserving the one complete station draft");
        var second = await Open(); await Authenticate(second);
        Check(ReferenceEquals(editor, ((ContentControl)second.FindName("EditorHost")).Content) && vm.StructuredPolicy.StationCode == "SYNTH-UNSAVED-SETTINGS",
            "reopening reuses original controls and draft after a new password verification");
        var submitted = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "SYNTH-SETTINGS-PARCEL"), default);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.Id == submitted.OperationId && o.Wcs?.State == StageState.Accepted));
        var original = (await h.App.SnapshotAsync()).Operations.Single(o => o.Id == submitted.OperationId).Wcs!.Request;
        Check(submitted.Status == "Admitted" && h.Peer.Requests.Count == 1, "the original Application can submit exactly once while settings are being edited");
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
        Check(Wire.Encode((await h.App.OperationAsync(submitted.OperationId!))!.Wcs!.Request) == Wire.Encode(original) && h.Peer.Requests.Count == 1,
            "configuration editing cannot rewrite or resend an original request");
        await vm.RequestActivationAsync!();
        await vm.RequestRetentionAsync!();
        window.Close();
        Check(window.IsVisible && second.IsVisible && !(await h.App.SnapshotAsync()).Paused,
            "reopen, retention and owner exit cannot silently pause or discard the active settings window");
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
