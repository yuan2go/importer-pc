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
        var api = new MvidApi("utf-8");
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
        Check(ReaderSettings.Read(new Dictionary<string, string>(parameters) { ["code.formats"] = "[\"Aztec\"]" }).Formats.SequenceEqual(new[] { "All" }), "native recognition ignores software-only symbology filters and accepts the full SDK result");
        var output = Marshal.AllocHGlobal(MvidNative.OutputBytes);
        try
        {
            Marshal.Copy(new byte[MvidNative.OutputBytes], 0, output, MvidNative.OutputBytes);
            var encoding = MvidNative.EncodingFor("utf-8");
            RecognitionSymbol[] Read() => MvidNative.CopyCodes(output, 640, 240, encoding);
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
            Check(Read().All(s => s.Format == "Code128"), "native results retain SDK symbology metadata without a software filter");
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
        Check(MvidNative.IsRecognitionFailure(unchecked((int)0x80002200)) && MvidNative.IsRecognitionFailure(unchecked((int)0x80002300)) &&
            !MvidNative.IsRecognitionFailure(unchecked((int)0x80000214)) && !MvidNative.IsRecognitionFailure(unchecked((int)0x80002100)),
            "native barcode algorithm failure is frame-local while transport and camera failures remain connection faults");
        await FreshFramesProveLiveness();
        await RejectedPlacement(workspace, root);
        await InvalidRejectedPlacement(workspace, root);
        await ManualCorrection(workspace, root);
        await StopDuringCapacityWait();
        await DebugFlow(workspace, root);
        await RcsOfflineCameraReads.Run(workspace, Path.Combine(root, "rcs-native"), nativeResults: true);
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
    static async Task RejectedPlacement(string workspace, string root)
    {
        var exclusion = new BarcodeFilterPolicy("", "SYNTH-(VEHICLE|MATRIX)");
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "native-rejected-placement"), stillMs: "50", nativeResults: true, barcodeFilter: exclusion);
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        var vm = new SupplyStationViewModel(h.App); vm.SetViewport(1366, 768);
        var parcel = new CameraRoundHarness.RoundPixels(Enumerable.Repeat((byte)95, 640 * 240).ToArray(), 640, 240);
        for (var y = 50; y < 170; y++) for (var x = 150; x < 450; x++) parcel.Data[y * parcel.Width + x] = 230;
        var qr = Symbol("SYNTH-VEHICLE") with { Format = "QRCode" };
        var dm = Symbol("SYNTH-MATRIX") with { Format = "DataMatrix" };
        uint number = 0;
        async Task Frame(RecognitionSymbol[] symbols, CameraRoundHarness.RoundPixels? image = null)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var next = ++number;
                h.Api(h.Left).EnqueueNative(image ?? parcel, next, symbols);
                MvsFrameLatency? trace = null;
                await CameraRoundHarness.Until(() => Task.FromResult((trace = h.Factory.Sources[h.Left].Timings.Snapshot().RecentFrames.SingleOrDefault(f => f.Number == next)) is not null));
                if (trace!.DecodeAttempted) return;
                await Task.Delay(80);
            }
            throw new TimeoutException("Synthetic frame did not settle");
        }
        long Received() => h.App.WorkerProgress().Single(p => p.Position == "left").Received;
        await Frame([qr, dm], CameraRoundHarness.EmptyTray); await vm.RefreshAsync();
        Check(Received() == 0 && h.Peer.Requests.Count == 0 && !vm.Left.IsProcessing && vm.Left.DisplayPresentation.Tone != Ipc.Application.ParcelFlow.Desktop.ParcelPresentationTone.Rejected,
            "only explicitly excluded cart codes are no-code frames: no durable parcel input, multi-code alarm or request");
        await Frame([Symbol("SYNTH-REJECT-A"), Symbol("SYNTH-REJECT-B", 120), qr]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Inputs!.Any(i => i.PositionId == "left" && i.Outcome == ParcelInputOutcome.Rejected));
        await vm.RefreshAsync();
        var rejected = vm.Left.Input!;
        Check(rejected.Observation.Codes.Length == 2 && rejected.BarcodeSelection?.Outcome == "AmbiguousCodes" && h.Peer.Requests.Count == 0,
            "camera applies the fixed-code regex before rejecting the two remaining package labels");
        Check(vm.Left.CanViewCurrentPicture && vm.Left.DisplayNextStep.Contains("取下包裹后再上件"), "rejected parcel offers its picture and tells the operator to remove it");
        var picture = await h.App.ReadParcelPictureAsync(vm.Left.CurrentPictureQuery!, default);
        Check(picture.Image is { } original && original.Metadata.ObservationId == rejected.Observation.Id && original.Gray8.SequenceEqual(parcel.Data),
            "multi-code picture is the exact full original frame that was rejected");
        var received = Received();
        for (var i = 0; i < 6; i++)
        {
            await Frame(i % 2 == 0 ? [Symbol("SYNTH-REJECT-B", 120), Symbol("SYNTH-REJECT-A"), qr] : [Symbol("SYNTH-REJECT-A"), qr]);
            vm.Left.ApplyReadFeedback((await h.App.ReadPresentationFeedbackAsync(default)).Single(r => r.PositionId == "left"));
            Check(!vm.Left.IsProcessing && vm.Left.CanViewCurrentPicture, "immediate read cue retains the rejection before any refreshed business snapshot");
            await vm.RefreshAsync();
            Check(vm.Left.Input!.Observation.Id == rejected.Observation.Id && !vm.Left.IsProcessing && vm.Left.CanViewCurrentPicture &&
                vm.Left.DisplayPresentation.Tone == Ipc.Application.ParcelFlow.Desktop.ParcelPresentationTone.Rejected,
                "repeated/reordered/partial reads keep the rejected placement and picture without a processing flash");
        }
        Check(Received() == received && h.Peer.Requests.Count == 0, "rejected resting parcel does not refill the durable lane or send a single-label partial read");
        await Frame([]); await vm.RefreshAsync();
        Check(vm.Left.CanViewCurrentPicture && vm.Left.DisplayPresentation.Tone == Ipc.Application.ParcelFlow.Desktop.ParcelPresentationTone.Rejected,
            "a missed decode of the unchanged rejected parcel retains its result and picture");
        var epoch = h.Factory.Sources[h.Left].Epoch;
        await h.App.ReconnectDeviceAsync(h.Left);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[h.Left].Epoch != epoch));
        await Frame([Symbol("SYNTH-REJECT-A"), Symbol("SYNTH-REJECT-B", 120), qr]); await vm.RefreshAsync();
        Check(!vm.Left.IsProcessing && vm.Left.CanViewCurrentPicture && Received() == received, "reconnect rereads the rejected parcel without restarting processing");
        var emptyStart = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++) { await Frame([qr], CameraRoundHarness.EmptyTray); if (i < 2) await Task.Delay(450); }
        await vm.RefreshAsync();
        Check(DateTimeOffset.UtcNow - emptyStart >= TimeSpan.FromMilliseconds(800) && !vm.Left.CanViewCurrentPicture &&
            vm.Left.DisplayPresentation.Tone == Ipc.Application.ParcelFlow.Desktop.ParcelPresentationTone.Neutral,
            "three completed QR-only empty-tray frames over 800ms clear the rejected result and picture");
        Check((await h.App.ReadParcelPictureAsync(new(ObservationId: rejected.Observation.Id), default)).Image is null,
            "an old rejected observation cannot borrow a later image after removal");
        await Frame([Symbol("SYNTH-REJECT-A") with { Format = "QRCode" }, qr]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.State == ParcelState.SlotResolved));
        await vm.RefreshAsync();
        Check(h.Peer.Requests.Count == 1 && vm.Left.Barcode == "SYNTH-REJECT-A" && vm.Left.ShowConfirmedTask,
            "after removal, the PACKAGE QR plus the excluded vehicle QR requests a chute exactly once");
        Check(CameraBarcodeCandidates.Excluded(null, [qr.Text]) is null && CameraBarcodeCandidates.Excluded(exclusion, ["SYNTH-VEHICLE-EXTRA"]) is { Length: 0 },
            "no guessed exclusion is installed and regex matching covers the complete code rather than a substring");
        var manual = rejected.Observation with { Codes = ["SYNTH-MANUAL"], Symbols = [qr], OperatorInput = new(Wire.Id(), "Keyboard", "synthetic", null, DateTimeOffset.UtcNow) };
        Check(CameraBarcodeCandidates.WithoutBackground(exclusion, manual).Codes.SequenceEqual(manual.Codes), "camera background normalization does not alter independent manual input");
        Check(CameraBarcodeCandidates.Excluded(new("", "["), [qr.Text]) is null &&
            CameraBarcodeCandidates.Excluded(exclusion, [qr.Text], "incomplete") is null,
            "invalid or incomplete filtering cannot turn a frame into an empty-tray observation");
        await CameraRoundHarness.Until(async () => (await h.App.ReadDiagnosticTailAsync(null, null, default)).Entries.Any(e => e.Text.Contains("SYNTH-VEHICLE") && e.Text.Contains("Candidates") && e.Text.Contains("QRCode") && e.Text.Contains("Excluded")));
        Check(true, "normal diagnostic log retains full raw cart/package candidates, symbology and exclusion decisions");
        var protectedText = DiagnosticText.Protect(Wire.Encode(new { Codes = new[] { "SYNTH-CANDIDATE" }, Barcode = "SYNTH-BARCODE", token = "SYNTH-CREDENTIAL", Body = Wire.Encode(new { loadCode = "SYNTH-LOAD", password = "SYNTH-PASSWORD" }) }));
        Check(protectedText.Contains("SYNTH-CANDIDATE") && protectedText.Contains("SYNTH-BARCODE") && protectedText.Contains("SYNTH-LOAD") &&
            !protectedText.Contains("SYNTH-CREDENTIAL") && !protectedText.Contains("SYNTH-PASSWORD"),
            "diagnostic redaction preserves barcode values in nested JSON while still hiding credentials");

        await Frame([Symbol("SYNTH-CORRECT-A"), Symbol("SYNTH-CORRECT-B", 120), qr]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Inputs!.Any(i => i.Observation.Codes.Contains("SYNTH-CORRECT-A") && i.Outcome == ParcelInputOutcome.Rejected));
        var basis = await h.App.SnapshotAsync();
        var manualReceipt = await h.App.SubmitParcelBarcodeAsync(new(Wire.Id(), basis.RuntimeId, basis.Configuration.Revision, "left", "SYNTH-MANUAL-REJECT", ParcelBarcodeInputKind.Keyboard,
            basis.Inputs!.Single(i => i.PositionId == "left").Observation.Id, basis.Positions!.Single(p => p.Id == "left").CurrentOperation), default);
        Check(manualReceipt.Status == "Admitted", "operator can correct a rejected multi-code parcel with independent manual input");
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(manualReceipt.OperationId!))?.Wcs?.State == StageState.Accepted);
        await Frame([Symbol("SYNTH-CORRECT-A"), Symbol("SYNTH-CORRECT-B", 120), qr]);
        await CameraRoundHarness.Until(async () => { await vm.RefreshAsync(); return vm.Left.ShowConfirmedTask; });
        Check(h.Peer.Requests.Count == 2 && vm.Left.Barcode == "SYNTH-MANUAL-REJECT", "same rejected labels cannot overwrite the manual correction or create another request");

        RecognitionSymbol[] fixedCodes = [Symbol("SYNTH-FIXED-ONE") with { Format = "QRCode" }, Symbol("SYNTH-FIXED-TWO", 120) with { Format = "QRCode" }];
        await Frame(fixedCodes);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Inputs!.Any(i => i.Observation.Codes.Contains("SYNTH-FIXED-ONE") && i.Outcome == ParcelInputOutcome.Rejected));
        basis = await h.App.SnapshotAsync();
        var updated = basis.Configuration.Effective with { BarcodeFilter = new("", "SYNTH-(VEHICLE|MATRIX|FIXED-(ONE|TWO))") };
        h.Factory.Expect(updated);
        var savedRule = await h.App.StageConfigurationAsync(basis.Configuration.Revision, updated, "Synthetic fixed cart-code exclusion");
        Check(savedRule.Status == "Effective", "fixed cart-code regex can be applied live without restarting the cameras");
        for (var i = 0; i < 3; i++) { await Frame(fixedCodes); if (i < 2) await Task.Delay(450); }
        await vm.RefreshAsync();
        Check(!vm.Left.CanViewCurrentPicture && vm.Left.DisplayPresentation.Tone == Ipc.Application.ParcelFlow.Desktop.ParcelPresentationTone.Neutral && h.Peer.Requests.Count == 2,
            "newly excluded fixed cart codes clear their old rejection after three empty observations even though the cart image did not move");

        await Frame([Symbol("SYNTH-DRAIN-A"), Symbol("SYNTH-DRAIN-B", 120), qr]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Inputs!.Any(i => i.Observation.Codes.Contains("SYNTH-DRAIN-A") && i.Outcome == ParcelInputOutcome.Rejected));
        basis = await h.App.SnapshotAsync();
        await h.App.SetPositionEnabledAsync(new(Wire.Id(), basis.RuntimeId, "left", basis.Positions!.Single(p => p.Id == "left").SupplyControl!.Revision, false), default);
        Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").SupplyControl!.Mode == StationPositionMode.Disabling,
            "disabling a rejected parcel waits for removal even though no business operation was created");
        for (var i = 0; i < 3; i++) { await Frame([qr], CameraRoundHarness.EmptyTray); if (i < 2) await Task.Delay(450); }
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").SupplyControl!.Mode == StationPositionMode.Disabled);
        Check(h.Peer.Requests.Count == 2, "confirmed removal completes disabling without sending the rejected parcel");
    }
    static async Task InvalidRejectedPlacement(string workspace, string root)
    {
        foreach (var (invalid, name) in new[] { ("", "empty"), ("SYNTH\u001fCONTROL", "control") })
        {
            await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "invalid-rejected-" + name), stillMs: "0", nativeResults: true);
            await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
            var parcel = new CameraRoundHarness.RoundPixels(Enumerable.Repeat((byte)190, 640 * 240).ToArray(), 640, 240);
            h.Api(h.Left).EnqueueNative(parcel, 1, [Symbol(invalid)]);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Inputs!.Any(i => i.PositionId == "left" && i.Outcome == ParcelInputOutcome.Rejected));
            var basis = await h.App.SnapshotAsync();
            var rejected = basis.Inputs!.Single(i => i.PositionId == "left");
            Check(rejected.Observation.Codes.Single() == invalid && rejected.BarcodeSelection?.SelectedCode is null,
                "invalid " + name + " candidate remains exact rejection evidence");
            var receipt = await h.App.SetPositionEnabledAsync(new(Wire.Id(), basis.RuntimeId, "left", basis.Positions!.Single(p => p.Id == "left").SupplyControl!.Revision, false), default);
            var state = (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").SupplyControl!;
            Check(receipt.Status == "Accepted" && state.Mode == StationPositionMode.Disabling && state.PlacementCode is { Length: > 0 } key && !key.Any(char.IsControl),
                "invalid " + name + " barcode can persist a safe draining identity without throwing");
            Check((await h.App.ReadParcelPictureAsync(new(ObservationId: rejected.Observation.Id), default)).Image?.Gray8.SequenceEqual(parcel.Data) == true,
                "disabling retains the invalid " + name + " parcel original until removal");
            for (uint frame = 2; frame <= 4; frame++)
            {
                h.Api(h.Left).EnqueueNative(CameraRoundHarness.EmptyTray, frame, []);
                await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[h.Left].Timings.Snapshot().RecentFrames.Any(f => f.Number == frame && f.DecodeAttempted)));
                if (frame < 4) await Task.Delay(450);
            }
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").SupplyControl!.Mode == StationPositionMode.Disabled);
            Check(h.Peer.Requests.Count == 0, "invalid " + name + " parcel drains after confirmed removal without sending a request");
        }
    }
    static async Task ManualCorrection(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "native-manual-correction"), stillMs: "0", nativeResults: true);
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        var parcel = new CameraRoundHarness.RoundPixels(Enumerable.Repeat((byte)90, 640 * 240).ToArray(), 640, 240);
        async Task Frame(uint number, RecognitionSymbol[] symbols, CameraRoundHarness.RoundPixels? pixels = null)
        {
            h.Api(h.Left).EnqueueNative(pixels ?? parcel, number, symbols);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).CameraReads?.Any(r => r.PositionId == "left" && r.Frame.FrameNumber == number) == true);
        }
        await Frame(1, [Symbol("SYNTH-CAMERA-ORIGINAL")]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.Wcs?.State == StageState.Accepted));
        var basis = await h.App.SnapshotAsync();
        await Frame(2, [Symbol("SYNTH-CAMERA-ORIGINAL")]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Inputs!.Single(i => i.PositionId == "left").Observation.SourceEventId == "mvid.frame.2");
        var receipt = await h.App.SubmitParcelBarcodeAsync(new(Wire.Id(), basis.RuntimeId, basis.Configuration.Revision, "left", "SYNTH-MANUAL-CORRECTION", ParcelBarcodeInputKind.Keyboard,
            basis.Inputs!.Single(i => i.PositionId == "left").Observation.Id, basis.Positions!.Single(p => p.Id == "left").CurrentOperation), default);
        Check(receipt.Status == "Admitted", "continuous same-placement camera reads do not invalidate the operator's manual input basis");
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(receipt.OperationId!))?.Wcs?.State == StageState.Accepted);
        var requests = h.Peer.Requests.Count;
        await Frame(3, [Symbol("SYNTH-CAMERA-ORIGINAL")]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Inputs!.Single(i => i.PositionId == "left").Observation.SourceEventId == "mvid.frame.3");
        var vm = new SupplyStationViewModel(h.App); vm.SetViewport(1366, 768); await vm.RefreshAsync();
        Check((await h.App.ReadPresentationFeedbackAsync(default)).Single(r => r.PositionId == "left").PlacementOperationId == receipt.OperationId &&
            h.Peer.Requests.Count == requests && vm.Left.Operation?.Id == receipt.OperationId && vm.Left.Barcode == "SYNTH-MANUAL-CORRECTION" && vm.Left.ShowConfirmedTask,
            "unchanged camera code cannot replace the manual barcode/chute or send another request");
        for (uint n = 4; n <= 6; n++) { await Frame(n, [], CameraRoundHarness.EmptyTray); if (n < 6) await Task.Delay(450); }
        await CameraRoundHarness.Until(async () => { await vm.RefreshAsync(); return !vm.Left.ShowConfirmedTask; });
        Check(!vm.Left.ShowConfirmedTask, "removing the physical parcel releases its manual correction and result");
        await Frame(7, [Symbol("SYNTH-CAMERA-ORIGINAL")]);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == requests + 1));
        Check(h.Peer.Requests.Count == requests + 1, "replacing the parcel permits exactly one new camera request");
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.Acquisition?.SourceEventId == "mvid.frame.7" && o.Wcs?.State == StageState.Accepted));
        basis = await h.App.SnapshotAsync();
        receipt = await h.App.SubmitParcelBarcodeAsync(new(Wire.Id(), basis.RuntimeId, basis.Configuration.Revision, "left", "SYNTH-SECOND-CORRECTION", ParcelBarcodeInputKind.Keyboard,
            basis.Inputs!.Single(i => i.PositionId == "left").Observation.Id, basis.Positions!.Single(p => p.Id == "left").CurrentOperation), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(receipt.OperationId!))?.Wcs?.State == StageState.Accepted);
        await vm.RefreshAsync();
        Check(vm.Left.ShowConfirmedTask, "second manual correction has its own confirmed result before changing parcels");
        await Frame(8, [Symbol("SYNTH-NEXT-PARCEL")]);
        var changedRead = (await h.App.ReadPresentationFeedbackAsync(default)).Single(r => r.PositionId == "left");
        vm.Left.ApplyReadFeedback(changedRead);
        Check(changedRead.SingleCode == "SYNTH-NEXT-PARCEL" && changedRead.PlacementOperationId is null && !vm.Left.ShowConfirmedTask,
            "a genuinely different camera code immediately invalidates the previous manual chute before the next snapshot");
    }
    static async Task FreshFramesProveLiveness()
    {
        var parameters = new Dictionary<string, string>
        { ["device.ip"] = "192.0.2.20", ["device.serial"] = "SYNTH-NATIVE-LIVE", ["reader.engine"] = "mvid-native", ["motion.still-ms"] = "0" };
        var api = new CameraRoundHarness.RoundApi(new("native-live", BundledMvs.ReaderImplementation, "synthetic", "1") { Parameters = parameters });
        await using var source = new MvsAcquisition(new("native-live", "native-live", parameters), api, _ => new CancellationTokenSource());
        var count = 0;
        await source.StartControlledAsync("native-live-epoch", _ => { Interlocked.Increment(ref count); return true; }, _ => { }, default);
        for (uint frame = 1; frame <= 24; frame++)
        {
            api.EnqueueNative(CameraRoundHarness.EmptyTray, frame, [Symbol("SYNTH-LIVE")]);
            await CameraRoundHarness.Until(() => Task.FromResult(Volatile.Read(ref count) == frame));
            await Task.Delay(100);
        }
        await source.StopAsync(default);
        Check(api.HealthReads == 0 && api.Starts == 1 && api.Destroys == 1, "fresh native frames prove liveness without register polling or reconnecting a healthy stream");
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
    static async Task DebugFlow(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "mvid-debug"), nativeResults: true);
        await h.StartAsync();
        var epoch = h.Factory.Sources[h.Left].Epoch;
        var scope = await h.BeginAsync(); await h.ArmAsync(h.Left); await h.WaitForCameraAdmissionAsync();
        Check(!scope.BusinessFenced && h.Factory.Sources[h.Left].Epoch == epoch && h.Api(h.Left).Opens == 1,
            "native debugging uses the current camera session without pausing production or reopening the camera");
        h.Api(h.Left).EnqueueNative(CameraRoundHarness.EmptyTray, 1, [Symbol("SYNTH-NATIVE-DEBUG")]);
        h.Api(h.Right).EnqueueNative(CameraRoundHarness.EmptyTray, 1, [Symbol("SYNTH-NATIVE-DEBUG-RIGHT")]);
        var locked = await h.LockedAsync();
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.DebugRequest is null && o.Wcs?.State == StageState.Accepted) == 2);
        var image = await h.App.ReadCameraOriginalAsync(locked.Frame!.Id, default);
        Check(image is not null && image.Metadata.SourceEventId == "mvid.frame.1" && image.Metadata.Symbols.Single().Text == locked.Barcode &&
            image.Metadata.Symbols.Single().Corners.SequenceEqual(Symbol(locked.Barcode!).Corners) && h.Peer.Requests.Count == 2,
            "native debug locks the same-frame original and barcode geometry while both positions continue supplying");
        h.Api(h.Left).EnqueueNative(CameraRoundHarness.EmptyTray, 2, [Symbol("SYNTH-NATIVE-BEFORE-SEND")]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-NATIVE-BEFORE-SEND" && o.Wcs?.State == StageState.Accepted));
        Check(h.Peer.Requests.Count == 3 && (await h.App.OperationAsync(locked.OperationId!))?.Wcs is { State: StageState.Prepared, Attempts: 0 },
            "an unsent prepared native debug request cannot block the next production parcel");
        var sent = await h.App.SendCameraRoundAsync(await h.SendCommandAsync(locked), default);
        Check(h.Peer.Requests.Count == 4 && h.Peer.Requests.Last() == locked.RequestBody && sent.HttpResponse is { StatusCode: 200, Complete: true } &&
            (await h.App.OperationAsync(sent.OperationId!))?.Wcs?.State == StageState.Accepted,
            "explicit native debug send makes one independent RCS request using the frozen body");
        h.Api(h.Left).EnqueueNative(CameraRoundHarness.EmptyTray, 3, [Symbol("SYNTH-NATIVE-DEBUG-NEXT")]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-NATIVE-DEBUG-NEXT" && o.Wcs?.State == StageState.Accepted));
        Check((await h.App.ReadCameraReadRoundAsync(default)).Frame!.Id == locked.Frame.Id && h.Factory.Decoders[h.Left].Count == 0 && h.Peer.Requests.Count == 5,
            "locked native debug picture does not freeze production, use ZXing or change to a later frame");
        await h.EndAsync();
        Check(h.Peer.Requests.Count == 5 && !(await h.App.CameraInspectionStatusAsync(default)).BusinessFenced,
            "ending native debugging clears the capture boundary without replaying a request");
    }
}
