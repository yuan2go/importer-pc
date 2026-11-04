using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;

static partial class Program
{
    static async Task VerifyParcelPictureCopy(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS PICTURE-COPY " + text); }
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "picture-copy")); await h.StartAsync();
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-PICTURE-COPY"), 1);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.State == ParcelState.SlotResolved));
        var op = (await h.App.SnapshotAsync()).Operations.Single();
        var vm = new SupplyStationViewModel(h.App); await vm.RefreshAsync();
        ParcelPictureWindow Open(ParcelPictureQuery query) => (ParcelPictureWindow)Activator.CreateInstance(typeof(ParcelPictureWindow),
            BindingFlags.NonPublic | BindingFlags.Instance, null, [vm, query, op.ParcelCode], null)!;
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
        lines.Add("Native WPF/Windows clipboard with synthetic original pixels and loopback RCS; physical devices and customer data NOT_RUN.");
    }
}
