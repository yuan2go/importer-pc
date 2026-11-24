using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Presentation.Wpf;

static partial class Program
{
    static async Task VerifyStationThemeResources(string root, string evidence, List<string> lines)
    {
        var failures = 0;
        void Check(bool ok, string detail)
        {
            lines.Add((ok ? "PASS" : "FAIL") + " STATION-THEME " + detail);
            if (!ok) failures++;
        }

        void CheckBrush(Brush? actual, string key, string control)
        {
            // The verification host links the same Themes/Workstation.xaml entry as the SM host.
            var expected = (SolidColorBrush)System.Windows.Application.Current.FindResource(key);
            Check(actual is SolidColorBrush brush && brush.Color.A == 255 && brush.Opacity == 1 &&
                brush.Color == expected.Color, $"{control} uses opaque {key} {expected.Color}; actual {actual?.ToString() ?? "<null>"}");
        }

        await using var station = new ManualSupplyHarness(root, Path.Combine(evidence, "station-theme"), deferred: true)
            { StationClock = new PdaRoundClock() };
        await station.StartAsync();
        var window = new SupplyStationWindow(station.App, null, "standard");
        await using var owner = new NativeWindowOwner(window);
        window.Show();
        var work = SystemParameters.WorkArea;
        await SetClient(window, work.Width, work.Height);
        var vm = window.ViewModel;
        async Task Until(Func<bool> ready) => await CameraRoundHarness.Until(async () =>
        {
            await vm.RefreshAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            return ready();
        });

        await Until(() => vm.Fresh && vm.Left.CanSubmitBarcode && vm.Right.CanSubmitBarcode);
        var client = (Grid)window.FindName("ClientArea");
        var left = Descendants(window).OfType<SupplyStationPosition>().Single(p => ReferenceEquals(p.DataContext, vm.Left));
        var right = Descendants(window).OfType<SupplyStationPosition>().Single(p => ReferenceEquals(p.DataContext, vm.Right));
        var leftResult = (Border)left.FindName("ResultPanel");
        var rightResult = (Border)right.FindName("ResultPanel");
        var remove = (Button)right.FindName("ConfirmManualPlacementRemoval");
        var details = (Button)left.FindName("WorkDetails");
        CaptureInternal(evidence, "station-theme-ready", client, lines);
        CheckBrush(window.Background, "Surface", "workstation window background");
        CheckBrush(client.Background, "Surface", "workstation client background");
        CheckBrush(((Button)window.FindName("StationSettingsEntry")).Foreground, "Ink", "settings button foreground");
        CheckBrush(((Border)left.FindName("PositionOutline")).BorderBrush, "Line", "left position card border");
        CheckBrush(((Border)right.FindName("PositionOutline")).BorderBrush, "Line", "right position card border");
        CheckBrush(leftResult.BorderBrush, "Line", "ordinary result border");
        Check(!remove.IsVisible && !details.IsEnabled, "empty positions hide removal and disable details");

        station.Peer.Mode = "reject";
        var rejection = await station.App.SubmitParcelBarcodeAsync(await station.Command("right", "SYNTH-THEME-RIGHT"), default);
        if (rejection.OperationId is not { } rejectedId)
            throw new InvalidOperationException("Synthetic right parcel was not admitted: " + rejection.Detail);
        await Until(() => vm.Right.Operation?.Id == rejectedId && vm.Right.CenteredRcsRejection && remove.IsVisible);

        station.Peer.Mode = "accept";
        station.Peer.ReplySlot = "L-0123";
        var confirmation = await station.App.SubmitParcelBarcodeAsync(await station.Command("left", "SYNTH-THEME-LEFT"), default);
        if (confirmation.OperationId is not { } confirmedId)
            throw new InvalidOperationException("Synthetic left parcel was not admitted: " + confirmation.Detail);
        await Until(() => vm.Left.Operation?.Id == confirmedId && vm.Left.ShowConfirmedTask && details.IsEnabled);
        CaptureInternal(evidence, "station-theme-confirmed-and-removal", client, lines);
        var wave = (TextBox)left.FindName("WaveValue");
        Check(wave.IsVisible && wave.Text == "未返回", "confirmed task shows the missing wave placeholder");
        CheckBrush(wave.Foreground, "Muted", "missing wave foreground");
        CheckBrush(leftResult.BorderBrush, "Success", "confirmed result border");
        CheckBrush(rightResult.BorderBrush, "Line", "rejected result border");
        CheckBrush(remove.BorderBrush, "Warning", "visible removal button border");
        CheckBrush(details.BorderBrush, "Accent", "enabled details button border");

        try
        {
            var template = (DataTemplate)window.FindResource("PreferenceNoticeBell");
            var notice = (Grid)template.LoadContent();
            var popup = notice.Children.OfType<Popup>().Single();
            var title = Descendants(popup.Child).OfType<TextBlock>().Single(t => t.Text == "本机偏好通知");
            Check(title.FontWeight == UiTypography.Emphasis, "preference notice template loads with the shared emphasis weight");
        }
        catch (XamlParseException error)
        {
            Check(false, "preference notice template could not be instantiated: " + error.GetBaseException().Message);
        }

        if (failures != 0)
            throw new InvalidOperationException($"{failures} station theme assertions failed.");
    }
}
