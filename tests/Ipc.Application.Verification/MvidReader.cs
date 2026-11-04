using System.Runtime.InteropServices;
using System.Text;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

internal static class MvidReader
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS MVID " + text); }
    static RecognitionSymbol Symbol(string text, int y = 20) => new(text, "Code128", [new(20, y), new(300, y), new(300, y + 40), new(20, y + 40)], 0);
    static void Reject(Action action, string text)
    {
        try { action(); } catch (Exception e) when (e is MvsFrameRejection or InvalidDataException) { Check(true, text); return; }
        throw new InvalidOperationException("Expected rejection: " + text);
    }
    internal static async Task ProbeRuntime()
    {
        await using var source = BundledMvs.CreateReader(new("parcel-reader", BundledMvs.ReaderImplementation, "synthetic-native-probe", "1")
        { Parameters = new() { ["device.ip"] = "192.0.2.1", ["device.serial"] = "SYNTHETIC", ["reader.engine"] = "mvid-native" } });
        var api = new MvidApi(new(["All"], true, "mvid-native"), "utf-8");
        api.Initialize();
        Check(true, "actual pinned MVID library loads and reports the expected native ABI version");
        nint first = 0, second = 0;
        try
        {
            MvidNative.Check(MvidNative.MVID_CR_CreateHandle(ref first, 3), "Create first algorithm handle");
            MvidNative.Check(MvidNative.MVID_CR_CreateHandle(ref second, 3), "Create second algorithm handle");
            Check(first != 0 && second != 0 && first != second, "actual native library creates two independent algorithm handles");
        }
        finally
        {
            try { if (second != 0) MvidNative.Check(MvidNative.MVID_CR_DestroyHandle(second), "Destroy second handle"); }
            finally { if (first != 0) MvidNative.Check(MvidNative.MVID_CR_DestroyHandle(first), "Destroy first handle"); }
        }
        api.Shutdown(); Check(true, "actual native handles are destroyed successfully");
        Console.WriteLine("BOUNDARY: actual SDK load/version/algorithm handles only; camera binding/acquisition NOT_RUN");
    }
    static void Boundaries()
    {
        Check(ReaderSettings.Read(new Dictionary<string, string>()).Engine == "zxing-cpp", "existing settings keep software recognition");
        var parameters = new Dictionary<string, string> { ["reader.engine"] = "mvid-native", ["code.formats"] = "[\"Code128\",\"EAN13\"]" };
        Check(ReaderSettings.Read(parameters).Engine == "mvid-native", "native recognition is an explicit per-camera setting");
        Reject(() => ReaderSettings.Read(new Dictionary<string, string>(parameters) { ["reader.engine"] = "unknown" }), "unknown engines cannot fall back silently");
        Reject(() => ReaderSettings.Read(new Dictionary<string, string>(parameters) { ["code.formats"] = "[\"Aztec\"]" }), "unsupported native symbologies are refused");
        var output = Marshal.AllocHGlobal(MvidNative.OutputBytes);
        try
        {
            Marshal.Copy(new byte[MvidNative.OutputBytes], 0, output, MvidNative.OutputBytes);
            var encoding = MvidNative.EncodingFor("utf-8");
            RecognitionSymbol[] Read(string[]? formats = null) => MvidNative.CopyCodes(output, 640, 240, encoding, formats ?? ["All"]);
            Check(Read().Length == 0, "a complete empty native list remains an empty-frame fact");
            void Write(int index, string text, int y, int type = 128)
            {
                var pointer = output + MvidNative.ImageBytes + 4 + index * MvidNative.CodeBytes;
                var bytes = Encoding.UTF8.GetBytes(text + "\0"); Marshal.Copy(bytes, 0, pointer, bytes.Length);
                Marshal.WriteInt32(pointer, 4096, bytes.Length); Marshal.WriteInt32(pointer, 4100, type);
                var corners = Symbol(text, y).Corners;
                for (var i = 0; i < 4; i++) { Marshal.WriteInt32(pointer, 4104 + i * 8, corners[i].X); Marshal.WriteInt32(pointer, 4108 + i * 8, corners[i].Y); }
            }
            Write(0, "SYNTH-NATIVE", 20); Write(1, "SYNTH-NATIVE", 120);
            Marshal.WriteInt32(output, MvidNative.ImageBytes, 2);
            var both = Read();
            Check(both.Length == 2 && both.All(s => s.Text == "SYNTH-NATIVE") && both[0].Corners[0].Y != both[1].Corners[0].Y,
                "two native regions with the same payload are preserved separately");
            Check(BarcodeFiltering.Select(null, both.Select(s => s.Text).ToArray()).Outcome == "MultipleCodes", "same-text native labels still reach multi-code rejection");
            var first = output + MvidNative.ImageBytes + 4;
            Marshal.WriteInt32(first, 4140, 1);
            Check(Read().Length == 2, "SDK filtered flags do not silently remove candidates before Application");
            Check(Read(["EAN13"]).Length == 0, "configured symbology selection applies to the complete returned list");
            Marshal.WriteInt32(first, 4096, 4097); Reject(() => Read(), "oversized text rejects the complete frame");
            Write(0, "SYNTH-NATIVE", 20);
            Marshal.WriteByte(first, 0, 255); Reject(() => Read(), "invalid UTF-8 rejects the complete frame");
            Write(0, "SYNTH-NATIVE", 20);
            Marshal.WriteInt32(first, 4104, 641); Reject(() => Read(), "out-of-image coordinates are rejected");
            Write(0, "SYNTH-NATIVE", 20);
            Marshal.WriteInt32(output, MvidNative.ImageBytes, 256); Reject(() => Read(), "saturated SDK result lists are not truncated and admitted");
        }
        finally { Marshal.FreeHGlobal(output); }
    }
    internal static async Task Run(string workspace, string root)
    {
        Boundaries();
        await StopDuringCapacityWait();
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "mvid-reader"), stillMs: "50", nativeResults: true);
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        var vm = new SupplyStationViewModel(h.App); vm.SetViewport(1366, 768);
        var parcel = new CameraRoundHarness.RoundPixels(Enumerable.Repeat((byte)100, 640 * 240).ToArray(), 640, 240);
        for (var y = 50; y < 170; y++) for (var x = 150; x < 450; x++) parcel.Data[y * parcel.Width + x] = 230;
        uint left = 0, right = 0;
        async Task<MvsFrameLatency> Frame(string device, CameraRoundHarness.RoundPixels picture, RecognitionSymbol[] symbols, MvsFrameRejection? rejection = null)
        {
            var number = device == h.Left ? ++left : ++right;
            h.Api(device).EnqueueNative(picture, number, symbols, rejection);
            MvsFrameLatency? trace = null;
            await CameraRoundHarness.Until(() => Task.FromResult((trace = h.Factory.Sources[device].Timings.Snapshot().RecentFrames.SingleOrDefault(f => f.Number == number)) is not null));
            return trace!;
        }
        async Task Show(string device, CameraRoundHarness.RoundPixels picture, RecognitionSymbol[] symbols, MvsFrameRejection? rejection = null)
        { await Frame(device, picture, symbols, rejection); await Task.Delay(80); await Frame(device, picture, symbols, rejection); }
        var placing = await Frame(h.Left, parcel, [Symbol("SYNTH-NATIVE-A")]);
        Check(!placing.DecodeAttempted && h.Peer.Requests.Count == 0, "the first moving native result only updates preview");
        await Task.Delay(80);
        await Frame(h.Left, parcel, [Symbol("SYNTH-NATIVE-A")]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.State == ParcelState.SlotResolved));
        Check(h.Peer.Requests.Count == 1 && h.Factory.Decoders[h.Left].Count == 0, "native result creates one RCS request without invoking ZXing");
        for (var i = 0; i < 3; i++) await Show(h.Left, parcel, [Symbol("SYNTH-NATIVE-A")]);
        Check(h.Peer.Requests.Count == 1, "continuous native reads do not resubmit the resting parcel");
        for (var i = 0; i < 4; i++) { await Show(h.Left, parcel, []); await Task.Delay(220); }
        await Show(h.Left, parcel, [], new MvsFrameRejection("SyntheticNativeRejected", "Synthetic invalid native candidate"));
        await CameraRoundHarness.Until(async () => { await vm.RefreshAsync(); return vm.Left.ShowConfirmedTask; });
        Check(vm.Left.Barcode == "SYNTH-NATIVE-A" && vm.Left.DisplaySlot == "SYNTH-CHUTE" && vm.Left.CanViewCurrentPicture && h.Peer.Requests.Count == 1,
            "unchanged native no-code and rejected frames retain the original barcode, picture and chute");
        await Show(h.Right, parcel, [Symbol("SYNTH-TWO", 20), Symbol("SYNTH-TWO", 120)]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Inputs!.Any(i => i.PositionId == "right"));
        Check(h.Peer.Requests.Count == 1, "two equal native labels on the right do not create a request");
        await Show(h.Right, parcel, [Symbol("SYNTH-RIGHT")]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.State == ParcelState.SlotResolved) == 2);
        Check(h.Peer.Requests.Count == 2, "the other native camera supplies independently");
        var epoch = h.Factory.Sources[h.Left].Epoch;
        await h.App.ReconnectDeviceAsync(h.Left);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[h.Left].Epoch != epoch));
        await Show(h.Left, parcel, []); await vm.RefreshAsync();
        Check(!vm.Left.ShowConfirmedTask, "a new camera session cannot retain the previous scene before rereading");
        await Show(h.Left, parcel, [Symbol("SYNTH-NATIVE-A")]);
        await CameraRoundHarness.Until(async () => { await vm.RefreshAsync(); return vm.Left.ShowConfirmedTask; });
        Check(h.Peer.Requests.Count == 2, "native reconnect restores the old placement result without another RCS request");
        for (var i = 0; i < 4; i++) { await Show(h.Left, CameraRoundHarness.EmptyTray, []); await Task.Delay(300); }
        await vm.RefreshAsync();
        Check(!vm.Left.ShowConfirmedTask && !vm.Left.CanViewCurrentPicture, "changed empty-tray frames release the native placement and picture");
        await Show(h.Left, parcel, [Symbol("SYNTH-NATIVE-A")]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.State == ParcelState.SlotResolved) == 3);
        Check(h.Peer.Requests.Count == 3, "replacing the same native-read parcel requests exactly once again");
        Check(h.Factory.Sources[h.Left].ReadDiagnostics().DecodeMedianMs == 12.5, "native SDK recognition time is reported instead of a zero software-decode time");
        var api = h.Api(h.Left); api.DestroyError = unchecked((int)0x80000003);
        try { await h.Factory.Sources[h.Left].StopAsync(default); throw new InvalidOperationException("Destroy failure accepted"); }
        catch (IOException) { Check(api.Destroys == 1 && api.Finalizes == 1, "failed native destroy retains ownership rather than reporting a second release"); }
        api.DestroyError = 0;
        await h.App.StopAsync(default);
        Check(api.Destroys == 2 && api.Finalizes == 2 && !api.HasFrame, "retrying a failed stop really releases the native owner");
        Console.WriteLine("BOUNDARY: synthetic native frame port + real Application/SQLite/loopback RCS; actual MVID camera NOT_RUN");
    }
    static async Task StopDuringCapacityWait()
    {
        var parameters = new Dictionary<string, string>
        { ["device.ip"] = "192.0.2.20", ["device.serial"] = "SYNTH-NATIVE-STOP", ["reader.engine"] = "mvid-native", ["motion.still-ms"] = "0" };
        var api = new CameraRoundHarness.RoundApi(new("native-stop", BundledMvs.ReaderImplementation, "synthetic", "1") { Parameters = parameters });
        await using var source = new MvsAcquisition(new("native-stop", "native-stop", parameters), api, _ => new CancellationTokenSource());
        source.EnableOriginalFrames();
        var inputs = new System.Collections.Concurrent.ConcurrentQueue<Observation>();
        await source.StartControlledAsync("synthetic-native-epoch", o => { inputs.Enqueue(o); return true; }, _ => { }, default);
        for (uint frame = 1; frame <= 32; frame++)
        {
            api.EnqueueNative(CameraRoundHarness.EmptyTray, frame, [Symbol("SYNTH-CAPACITY-" + frame)]);
            await CameraRoundHarness.Until(() => Task.FromResult(inputs.Count == frame));
        }
        api.EnqueueNative(CameraRoundHarness.EmptyTray, 33, [Symbol("SYNTH-ALREADY-DECODED")]);
        await CameraRoundHarness.Until(() => Task.FromResult(source.ReadDiagnostics().WaitingForOriginalCapacity));
        await source.StopAsync(default);
        Check(inputs.Count == 32 && source.Undelivered is { SourceEventId: "mvid.frame.33" } pending && pending.Codes.Single() == "SYNTH-ALREADY-DECODED",
            "stopping during image capacity wait retains the already decoded native input and its original identity");
        try { await source.StartControlledAsync("replacement-epoch", _ => true, _ => { }, default); }
        catch (InvalidOperationException)
        {
            Check(api.Starts == 1 && api.Destroys == 1 && !api.HasFrame, "undelivered native input fences restart after the SDK handle is really released");
            return;
        }
        throw new InvalidOperationException("Restart discarded a decoded native input");
    }
}
