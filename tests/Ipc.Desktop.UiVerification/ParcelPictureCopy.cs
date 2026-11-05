using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyParcelPictureCopy(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS PICTURE-COPY " + text); }
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "picture-copy")); await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-PICTURE-COPY"), 1);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.State == ParcelState.SlotResolved));
        var op = (await h.App.SnapshotAsync()).Operations.Single();
        var vm = new SupplyStationViewModel(h.App); await vm.RefreshAsync();
        ParcelPictureWindow Open(ParcelPictureQuery query) => (ParcelPictureWindow)Activator.CreateInstance(typeof(ParcelPictureWindow),
            BindingFlags.NonPublic | BindingFlags.Instance, null, [vm, query, query.OperationId == op.Id ? op.ParcelCode : "本次拒绝包裹"], null)!;
        var window = Open(new(OperationId: op.Id));
        var previousClipboard = Clipboard.GetDataObject();
        try
        {
            window.Show();
            var copy = Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "复制图片"));
            for (var i = 0; i < 200 && !copy.IsEnabled; i++) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(25); }
            Check(copy.IsEnabled, "copy becomes available only after the requested original picture loads");
            var original = (BitmapSource)Descendants(window).OfType<Image>().Single().Source;
            Click(window, "放大"); Click(window, "放大"); Click(window, "复制图片");
            Check(Clipboard.ContainsImage(), "native Windows clipboard receives an image suitable for pasting");
            var copied = Clipboard.GetImage() ?? throw new InvalidOperationException("Clipboard image was not available");
            Check(copied.PixelWidth == original.PixelWidth && copied.PixelHeight == original.PixelHeight,
                "copy preserves full original dimensions despite viewport zoom");
            byte[] Pixels(BitmapSource bitmap)
            {
                BitmapSource gray = bitmap.Format == PixelFormats.Gray8 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Gray8, null, 0);
                var bytes = new byte[gray.PixelWidth * gray.PixelHeight]; gray.CopyPixels(bytes, gray.PixelWidth, 0); return bytes;
            }
            Check(Pixels(original).SequenceEqual(Pixels(copied)), "copied pixels match the original without viewport scaling or recognition overlays");
            Check(Descendants(window).OfType<TextBlock>().Any(t => t.Text.Contains("已复制包裹原图")), "copy completion gives an operator-facing confirmation");
            CaptureInternal(evidence, "parcel-picture-copy", (FrameworkElement)window.Content, lines);
            window.Close();
            window = Open(new(OperationId: "SYNTH-NO-IMAGE")); window.Show();
            for (var i = 0; i < 40; i++) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(25); }
            Check(!Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "复制图片")).IsEnabled,
                "a missing original cannot copy a previous parcel image");
        }
        finally
        {
            window.Close();
            if (previousClipboard is not null) Clipboard.SetDataObject(previousClipboard, true); else Clipboard.Clear();
        }
        Check(h.Peer.Requests.Count == 1, "picture loading and copying do not create another chute request");
        await using var rejected = new CameraRoundHarness(root, Path.Combine(evidence, "rejected-picture"), nativeResults: true);
        await rejected.StartAsync(); await rejected.WaitForCameraAdmissionAsync();
        var raw = CameraRoundHarness.Barcode("SYNTH-REJECTED-PICTURE");
        RecognitionSymbol[] symbols = [new("SYNTH-REJECT-A", "Code128", [new(10, 20), new(300, 20), new(300, 60), new(10, 60)], 0),
            new("SYNTH-REJECT-B", "QRCode", [new(320, 20), new(360, 20), new(360, 60), new(320, 60)], 0)];
        rejected.Api(rejected.Left).EnqueueNative(raw, 1, symbols);
        await CameraRoundHarness.Until(async () => (await rejected.App.SnapshotAsync()).Inputs!.Any(i => i.Outcome == ParcelInputOutcome.Rejected));
        var station = new SupplyStationWindow(rejected.App, null, "standard"); station.Show();
        previousClipboard = Clipboard.GetDataObject();
        try
        {
            await SetClient(station, 1366, 768);
            for (var i = 0; i < 200 && !station.ViewModel.Left.CanViewCurrentPicture; i++) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(25); }
            Check(station.ViewModel.Left.CanViewCurrentPicture && !station.ViewModel.Left.IsProcessing,
                "native multi-code work card retains rejection and enables the picture button");
            vm = station.ViewModel;
            window = Open(vm.Left.CurrentPictureQuery!); window.Show();
            var copy = Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "复制图片"));
            for (var i = 0; i < 200 && !copy.IsEnabled; i++) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(25); }
            Check(copy.IsEnabled, "rejected multi-code original loads and enables copying");
            Click(window, "复制图片");
            var copied = Clipboard.GetImage() ?? throw new InvalidOperationException("Rejected original was not copied");
            var gray = new FormatConvertedBitmap(copied, PixelFormats.Gray8, null, 0);
            var pixels = new byte[gray.PixelWidth * gray.PixelHeight]; gray.CopyPixels(pixels, gray.PixelWidth, 0);
            Check(gray.PixelWidth == raw.Width && gray.PixelHeight == raw.Height && pixels.SequenceEqual(raw.Data),
                "rejected parcel copy preserves every original pixel, not the preview or overlay");
            CaptureInternal(evidence, "rejected-parcel-picture", (FrameworkElement)window.Content, lines);
            window.Close();
            CaptureInternal(evidence, "rejected-parcel-card", (Grid)station.FindName("ClientArea"), lines);
            Check(rejected.Peer.Requests.Count == 0, "viewing and copying a rejected parcel never creates an RCS request");
        }
        finally
        {
            window.Close(); station.Close();
            if (previousClipboard is not null) Clipboard.SetDataObject(previousClipboard, true); else Clipboard.Clear();
        }
        lines.Add("Native WPF/Windows clipboard with synthetic original pixels and loopback RCS; physical devices and customer data NOT_RUN.");
    }
}
