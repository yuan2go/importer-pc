using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
using Ipc.Presentation.Wpf;

static partial class Program
{
    static async Task VerifyReaderFeedback22Ui(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS READER22-NATIVE " + text); }
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "reader22-native"), 15000);
        await h.StartAsync();
        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show();
        try
        {
            await SetClient(window, 1366, 768); window.Activate();
            async Task Until(Func<bool> predicate)
            {
                for (var i = 0; i < 200; i++) { await window.ViewModel.RefreshAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (predicate()) return; await Task.Delay(20); }
                throw new TimeoutException("Reader22 native state did not converge");
            }
            await Until(() => window.ViewModel.Fresh && window.ViewModel.ViewportReady);
            var vm = window.ViewModel;
            var left = Descendants(window).OfType<SupplyStationPosition>().Single(p => ReferenceEquals(p.DataContext, vm.Left));
            var right = Descendants(window).OfType<SupplyStationPosition>().Single(p => ReferenceEquals(p.DataContext, vm.Right));
            var leftEntry = (TextBox)left.FindName("BarcodeEntry"); var rightEntry = (TextBox)right.FindName("BarcodeEntry");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(leftEntry.IsKeyboardFocused && vm.InputTargetPosition == "left", "startup restores a visible explicit left input target after viewport readiness");
            rightEntry.Focus(); rightEntry.Text = "00123";
            var dialogEntry = new TextBox(); var dialog = new Window { Title = "Synthetic reader22 dialog", Owner = window, Content = dialogEntry, Width = 420, Height = 180 };
            dialog.Show(); dialog.Activate(); dialogEntry.Focus(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(dialogEntry.IsKeyboardFocused && vm.InputTargetPosition == "", "deactivation clears the visible target without taking text from another window");
            dialog.Close(); window.Activate(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(rightEntry.IsKeyboardFocused && rightEntry.Text == "00123" && vm.InputTargetPosition == "right", "dialog return restores this window's recent right input and preserves the real draft");
            rightEntry.Clear();
            h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH-RIGHT-PREVIOUS"), 1);
            await Until(() => vm.Right.ShowConfirmedTask && vm.Right.CanViewCurrentPicture && vm.Right.CanSubmitBarcode);
            var picture = (Button)right.FindName("CurrentBarcode"); picture.Focus(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(picture.IsKeyboardFocused, "explicit keyboard focus on a workspace action remains available");
            var before = h.Peer.Requests.Count;
            await BarcodeNativeKeyboard.SendAsync(window, rightEntry, "000999".Select(c => (ushort)c).Append((ushort)0x0D).ToArray(), picture);
            await Until(() => vm.Right.ShowConfirmedTask && vm.Right.Barcode == "000999");
            Check(rightEntry.IsKeyboardFocused && h.Peer.Requests.Count == before + 1 && !window.OwnedWindows.OfType<Window>().Any(w => w.IsVisible),
                "native scanner text moves from picture button to the recent entry before Enter; submits one complete new barcode without opening a picture");
            typeof(SupplyStationWindow).GetMethod("SetPage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, ["history"]);
            window.Activate(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(!rightEntry.IsKeyboardFocused && !leftEntry.IsKeyboardFocused, "history navigation does not steal focus into a detached workstation entry");
            typeof(SupplyStationWindow).GetMethod("SetPage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, ["workstation"]);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(rightEntry.IsKeyboardFocused, "return to workstation restores the most recently used position");
            var state = vm.Snapshot!.Devices!.Single(d => d.DeviceId == vm.Right.ReaderId);
            var apply = vm.Right.GetType().GetMethod("ApplyReadFeedback", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var feedback = new ParcelReadFeedback(vm.Snapshot.RuntimeId, "right", Wire.Id(), state.DeviceId, state.Epoch, "SYNTH-UNACKNOWLEDGED",
                Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow) { SourceSequence = 100, PendingDisplayBudgetMs = 1000 };
            apply.Invoke(vm.Right, [feedback]);
            await Until(() => !vm.Right.IsProcessing);
            Check(!vm.Right.IsProcessing && !vm.Right.ShowConfirmedTask && !vm.Right.CanViewCurrentPicture && vm.Right.DisplayStatus.Contains("失效"),
                "native expired-feedback surface ends processing and hides all previous task/picture authority");
            foreach (var (width, height) in new[] { (1366, 768), (800, 420) })
            {
                await SetClient(window, width, height); window.UpdateLayout();
                CaptureInternal(evidence, "reader22-expired-" + width, (Grid)window.FindName("ClientArea"), lines);
                var reason = (TextBox)right.FindName("AttentionReason");
                Check(reason.IsVisible && reason.ActualHeight >= reason.FontSize * 1.2 && reason.Text.Contains("没有本次有效格口"),
                    width + " expired feedback exposes at least a complete actionable reason line");
            }
            typeof(SupplyStationWindow).GetMethod("SetPage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, ["diagnostics"]);
            await Until(() => vm.CanStartOperation);
            var healthAction = Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "刷新体检"));
            var releaseAction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var action = vm.RunAsync(() => releaseAction.Task, "Synthetic held action");
            try
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(!healthAction.IsEnabled, "a busy action is visibly disabled instead of silently ignoring an enabled click");
            }
            finally { releaseAction.TrySetResult(); }
            await action; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(healthAction.IsEnabled, "completion restores the same guarded action without rebuilding the workspace");
            // Settings are the existing shared first-setup/daily model, not a new config authority.
            var catalog = new Ipc.Assembly.ParcelCapabilityFactory(new(Path.Combine(root, "config/sportmaster"), evidence, null, null)).Catalog();
            var draft = new StartupConfigurationDraft(new Ipc.Application.Sportmaster.SportmasterFlow().InitialConfiguration(), catalog);
            var editor = new ParameterSettingsControl { Model = draft.CreateParameterEditor((p, c) => Task.FromResult(Ipc.Application.ParcelFlow.BarcodeFiltering.Select(p, c))) };
            var settings = new Window { Title = "Synthetic reader22 audio settings", Content = editor, Width = 1100, Height = 760 };
            settings.Show(); editor.NavigateTo("CueVolume"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var slider = Descendants(editor).OfType<Slider>().Single(s => System.Windows.Automation.AutomationProperties.GetName(s) == "CueVolume");
            slider.Value = 30;
            Descendants(editor).OfType<Button>().Single(b => Equals(b.Content, "保存提示音音量")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(PresentationPreferences.ReadCueVolume("sportmaster") == 30, "shared system preferences save volume with the existing bounded user-root policy");
            Check(Descendants(editor).OfType<Button>().Count(b => b.Content is string text && text.StartsWith("试听")) == 3, "three distinctly labelled cue previews are reachable in the existing settings page");
            CaptureInternal(evidence, "reader22-audio-settings", editor, lines); settings.Close();
            var failures = new List<string>(); using var player = new OperatorCuePlayer(failures.Add);
            var waveHashes = new HashSet<string>();
            foreach (var cue in Enum.GetValues<OperatorCue>())
            {
                var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Sounds", "ipc-" + cue.ToString().ToLowerInvariant() + ".wav"));
                Check(System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && BitConverter.ToInt16(bytes, 22) == 1 && BitConverter.ToInt32(bytes, 24) == 22050,
                    cue + " ships a complete original mono PCM WAV in the actual output");
                waveHashes.Add(Convert.ToHexString(SHA256.HashData(bytes))); player.Play(cue, 30);
            }
            await Task.Delay(300);
            var players = (System.Collections.IDictionary)typeof(OperatorCuePlayer).GetField("players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(player)!;
            Check(waveHashes.Count == 3 && players.Count == 3, "three distinct waveforms use three bounded media owners independent of system event sounds; audible device NOT_RUN");
            player.SetVolume(0);
            Check(players.Values.Cast<System.Windows.Media.MediaPlayer>().All(p => p.Volume == 0), "live volume zero mutes existing software cue owners without changing system volume");
            player.Dispose(); Check(players.Count == 0, "disposing the cue owner closes and clears every media resource");
            lines.Add("INFO Media failures (if any) are surfaced by product callback: " + string.Join(";", failures));
        }
        finally { window.Close(); }
    }
}
