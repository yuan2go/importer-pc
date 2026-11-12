using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
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
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "mvid-reader"), stillMs: "50", nativeResults: true)
            { StationClock = new TimeStorageFaultChecks.ManualTime() };
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        await h.TrayAsync(h.Left, TrayPresence.Present);
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
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Arrival?.CameraBoundaryReady == true);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[h.Left].Epoch != epoch));
        await Show(h.Left, parcel, []); await vm.RefreshAsync();
        Check(vm.Left.ShowConfirmedTask, "a new camera session cannot replace current-round authority or grant another first call");
        await Show(h.Left, parcel, [Symbol("SYNTH-NATIVE-A")]);
        await CameraRoundHarness.Until(async () => { await vm.RefreshAsync(); return vm.Left.ShowConfirmedTask; });
        Check(h.Peer.Requests.Count == 2, "native reconnect restores the old placement result without another RCS request");
        for (var i = 0; i < 4; i++) { await Show(h.Left, CameraRoundHarness.EmptyTray, []); await Task.Delay(300); }
        await vm.RefreshAsync();
        Check(vm.Left.ShowConfirmedTask, "changed no-code frames alone cannot release a native current round");
        await h.TrayAsync(h.Left, TrayPresence.Absent); await SmRoundFixture.NextVehicleAsync(h.App, "left"); await vm.RefreshAsync();
        Check(!vm.Left.ShowConfirmedTask && !vm.Left.CanViewCurrentPicture, "qualified removal releases the native current projection and picture");
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
        var exclusion = new BarcodeFilterPolicy("(?:SYNTH-PACKAGE-[0-9]+|SYNTH-VEHICLE)", "SYNTH-VEHICLE");
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "native-rejected-placement"), nativeResults: true, barcodeFilter: exclusion)
            { StationClock = new TimeStorageFaultChecks.ManualTime() };
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        var vm = new SupplyStationViewModel(h.App); vm.SetViewport(1366, 768);
        var parcel = new CameraRoundHarness.RoundPixels(Enumerable.Repeat((byte)95, 640 * 240).ToArray(), 640, 240);
        var qr = Symbol("SYNTH-VEHICLE") with { Format = "QRCode" };
        uint number = 0;
        async Task Frame(params RecognitionSymbol[] symbols)
        {
            var next = ++number;
            h.Api(h.Left).EnqueueNative(parcel, next, symbols);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[h.Left].Timings.Snapshot().RecentFrames.Any(f => f.Number == next)));
            await vm.RefreshAsync();
        }
        await Frame(qr, Symbol("ordinary-invalid"));
        Check(vm.Left.CurrentRound is null && h.Peer.Requests.IsEmpty && !vm.Left.IsProcessing,
            "native raw background/invalid candidates are retained as observations without manufacturing a parcel or timeout");
        await Frame(Symbol("SYNTH-PACKAGE-1"), Symbol("SYNTH-PACKAGE-2", 120), qr, Symbol("ordinary-invalid"));
        await CameraRoundHarness.Until(async () => { await vm.RefreshAsync(); return vm.Left.CurrentRound is { FormalCodes.Length: 2 }; });
        var round = vm.Left.CurrentRound!; var rejected = vm.Left.Input!;
        Check(round.OperationId is null && rejected.Observation.Codes.Length == 4 && round.FormalCodes.Length == 2 && h.Peer.Requests.IsEmpty,
            "native complete raw evidence precedes formal quantity selection; invalid/background labels do not join the formal pair");
        var picture = await h.App.ReadParcelPictureAsync(new(ObservationId: rejected.Observation.Id), default);
        Check(picture.Image?.Gray8.SequenceEqual(parcel.Data) == true, "native multi-code round retains the exact full original frame");
        await Frame(Symbol("SYNTH-PACKAGE-2", 120), Symbol("SYNTH-PACKAGE-1"), qr);
        Check(vm.Left.CurrentRound?.Id == round.Id && !vm.Left.IsProcessing && h.Peer.Requests.IsEmpty,
            "reordered multi-code observations stay correctable in the original round");
        await Frame(Symbol("SYNTH-PACKAGE-1") with { Format = "QRCode" }, qr, Symbol("ordinary-invalid"));
        await CameraRoundHarness.Until(async () => { await vm.RefreshAsync(); return vm.Left.ShowConfirmedTask; });
        Check(vm.Left.CurrentRound?.Id == round.Id && vm.Left.Barcode == "SYNTH-PACKAGE-1" && h.Peer.Requests.Count == 1,
            "a unique legal QR correction within the native round produces exactly one first request");
        await Frame(Symbol("SYNTH-PACKAGE-9"));
        Check(vm.Left.Barcode == "SYNTH-PACKAGE-1" && h.Peer.Requests.Count == 1, "changed native payload without removal cannot override a claimed round");
        await Frame(); await Frame(qr); await Frame(qr);
        Check(vm.Left.CurrentRound?.Id == round.Id && h.Peer.Requests.Count == 1, "no-code/background frames cannot certify native physical removal");
        var epoch = h.Factory.Sources[h.Left].Epoch;
        await h.App.ReconnectDeviceAsync(h.Left);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Arrival?.CameraBoundaryReady == true);
        await Frame(Symbol("SYNTH-PACKAGE-1"));
        Check(h.Factory.Sources[h.Left].Epoch != epoch && vm.Left.CurrentRound?.Id == round.Id && h.Peer.Requests.Count == 1,
            "native reconnect preserves current round and automatic suppression");
        await h.TrayAsync(h.Left, TrayPresence.Absent); await SmRoundFixture.NextVehicleAsync(h.App, "left"); await vm.RefreshAsync();
        Check(vm.Left.CurrentRound is null && !vm.Left.CanViewCurrentPicture, "qualified removal clears native current projection without deleting the original record");
        await Frame(Symbol("SYNTH-PACKAGE-1"));
        await CameraRoundHarness.Until(async () => { await vm.RefreshAsync(); return vm.Left.ShowConfirmedTask; });
        Check(h.Peer.Requests.Count == 2 && vm.Left.CurrentRound?.Id != round.Id, "the same native code in a qualified new cycle is a new request");
        Check(CameraBarcodeCandidates.Excluded(null, [qr.Text]) is null && CameraBarcodeCandidates.Excluded(exclusion, ["SYNTH-VEHICLE-EXTRA"]) is { Length: 0 },
            "no guessed exclusion is installed and fixed-code regex remains a complete-string match");
        Check(CameraBarcodeCandidates.Excluded(new("", "["), [qr.Text]) is null && CameraBarcodeCandidates.Excluded(exclusion, [qr.Text], "incomplete") is null,
            "invalid/incomplete filtering cannot manufacture a qualified physical absence");
        await CameraRoundHarness.Until(async () => (await h.App.ReadDiagnosticTailAsync(null, null, default)).Entries.Any(e => e.Text.Contains("SYNTH-VEHICLE") && e.Text.Contains("Candidates") && e.Text.Contains("QRCode") && e.Text.Contains("Excluded")));
        Check(true, "native diagnostics preserve complete candidates, symbology and selection decisions");
        var protectedText = DiagnosticText.Protect(Wire.Encode(new { Codes = new[] { "SYNTH-CANDIDATE" }, Barcode = "SYNTH-BARCODE", token = "SYNTH-CREDENTIAL", Body = Wire.Encode(new { loadCode = "SYNTH-LOAD", password = "SYNTH-PASSWORD" }) }));
        Check(protectedText.Contains("SYNTH-CANDIDATE") && protectedText.Contains("SYNTH-LOAD") && !protectedText.Contains("SYNTH-CREDENTIAL") && !protectedText.Contains("SYNTH-PASSWORD"),
            "native diagnostic redaction preserves barcodes and hides credentials");
        await h.TrayAsync(h.Left, TrayPresence.Absent); await SmRoundFixture.NextVehicleAsync(h.App, "left");
        await Frame(Symbol("SYNTH-PACKAGE-3"), Symbol("SYNTH-PACKAGE-4", 120));
        var basis = await h.App.SnapshotAsync();
        var state = basis.Positions!.Single(p => p.Id == "left").SupplyControl!;
        await h.App.SetPositionEnabledAsync(new(Wire.Id(), basis.RuntimeId, "left", state.Revision, false), default);
        Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left") is
            { SupplyControl.Mode: StationPositionMode.Disabled, CurrentRoundId: null },
            "native unread multi-code round disables immediately without removal evidence");
        Check(h.Peer.Requests.Count == 2, "operator disable never sends the rejected native round");
    }
    static async Task InvalidRejectedPlacement(string workspace, string root)
    {
        foreach (var (invalid, name) in new[] { ("", "empty"), ("SYNTH\u001fCONTROL", "control") })
        {
            await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "invalid-rejected-" + name), stillMs: "0", nativeResults: true);
            await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
            var parcel = new CameraRoundHarness.RoundPixels(Enumerable.Repeat((byte)190, 640 * 240).ToArray(), 640, 240);
            await h.TrayAsync(h.Left, TrayPresence.Present);
            h.Api(h.Left).EnqueueNative(parcel, 1, [Symbol(invalid)]);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Inputs!.Any(i => i.PositionId == "left" && i.Outcome == ParcelInputOutcome.Rejected));
            var basis = await h.App.SnapshotAsync();
            var rejected = basis.Inputs!.Single(i => i.PositionId == "left");
            var roundId = basis.Positions!.Single(p => p.Id == "left").CurrentRoundId;
            Check(rejected.Observation.Codes.Single() == invalid && rejected.BarcodeSelection?.SelectedCode is null,
                "invalid " + name + " candidate remains exact rejection evidence");
            var receipt = await h.App.SetPositionEnabledAsync(new(Wire.Id(), basis.RuntimeId, "left", basis.Positions!.Single(p => p.Id == "left").SupplyControl!.Revision, false), default);
            var state = (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").SupplyControl!;
            Check(receipt.Status == "Accepted" && state.Mode == StationPositionMode.Disabled && state.PlacementCode is null && (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").CurrentRoundId is null,
                "invalid " + name + " barcode disables immediately without requiring a usable code");
            await CameraRoundHarness.Until(async () => (await h.App.RoundHistoryAsync(search: roundId)).Items.Single().OriginalFrame?.State == "Saved");
            Check((await h.App.ReadParcelPictureAsync(new(RoundId: roundId), default)).Archived is { } archive &&
                SportmasterRcs.DecodeOriginalPng(archive.Png).SequenceEqual(parcel.Data),
                "disable retains the invalid " + name + " parcel original in its ended round history");
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").CameraConnectionSuspended);
            Check(h.Peer.Requests.Count == 0, "invalid " + name + " parcel disables and releases without sending a request");
        }
    }
    static async Task ManualCorrection(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "native-manual-correction"), nativeResults: true)
            { StationClock = new TimeStorageFaultChecks.ManualTime() };
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        var parcel = new CameraRoundHarness.RoundPixels(Enumerable.Repeat((byte)90, 640 * 240).ToArray(), 640, 240);
        async Task Frame(uint number, params RecognitionSymbol[] symbols)
        {
            h.Api(h.Left).EnqueueNative(parcel, number, symbols);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[h.Left].Timings.Snapshot().RecentFrames.Any(f => f.Number == number)));
        }
        await Frame(1, Symbol("SYNTH-CAMERA-ORIGINAL"), Symbol("SYNTH-OTHER-LABEL", 120));
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Rounds.SingleOrDefault() is { OperationId: null });
        var basis = await h.App.SnapshotAsync(); var id = basis.Rounds.Single().Id;
        await Frame(2, Symbol("SYNTH-CAMERA-ORIGINAL"), Symbol("SYNTH-OTHER-LABEL", 120));
        var receipt = await h.App.SubmitParcelBarcodeAsync(new(Wire.Id(), basis.RuntimeId, basis.Configuration.Revision, "left", "SYNTH-MANUAL-CORRECTION", ParcelBarcodeInputKind.Keyboard,
            basis.Inputs!.Single(i => i.PositionId == "left").Observation.Id, null) { ExpectedRoundId = id, ExpectedArrivalGeneration = basis.Positions!.Single(p => p.Id == "left").Arrival!.Generation, ExpectedPlacementBoundary = basis.Positions!.Single(p => p.Id == "left").Arrival!.PlacementBoundary }, default);
        Check(receipt.Status == "Admitted", "native unresolved multi-code round permits manual correction using its round identity");
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(receipt.OperationId!))?.Wcs?.State == StageState.Accepted);
        await Frame(3, Symbol("SYNTH-CAMERA-ORIGINAL"));
        var vm = new SupplyStationViewModel(h.App); vm.SetViewport(1366, 768); await vm.RefreshAsync();
        Check(h.Peer.Requests.Count == 1 && vm.Left.CurrentRound?.Id == id && vm.Left.Operation?.Id == receipt.OperationId && vm.Left.Barcode == "SYNTH-MANUAL-CORRECTION" && vm.Left.ShowConfirmedTask,
            "native camera observations cannot overwrite manual barcode/chute or acquire another request");
        await Frame(4, Symbol("SYNTH-NEXT-PARCEL")); await vm.RefreshAsync();
        Check(vm.Left.CurrentRound?.Id == id && vm.Left.ShowConfirmedTask && h.Peer.Requests.Count == 1, "changed native payload alone cannot retire the manual round");
        var blocked = await h.App.SubmitParcelBarcodeAsync(new(Wire.Id(), basis.RuntimeId, basis.Configuration.Revision, "left", "SYNTH-SECOND-CORRECTION", ParcelBarcodeInputKind.Keyboard, null, receipt.OperationId) { ExpectedRoundId = id, ExpectedArrivalGeneration = basis.Positions!.Single(p => p.Id == "left").Arrival!.Generation, ExpectedPlacementBoundary = basis.Positions!.Single(p => p.Id == "left").Arrival!.PlacementBoundary }, default);
        Check(blocked.Status == "Rejected" && h.Peer.Requests.Count == 1, "manual input cannot append a second first call to an already claimed native round");
        await h.TrayAsync(h.Left, TrayPresence.Absent); await SmRoundFixture.NextVehicleAsync(h.App, "left"); await vm.RefreshAsync();
        Check(vm.Left.CurrentRound is null && !vm.Left.ShowConfirmedTask, "qualified removal ends a manual success before its display timer");
        await Frame(5, Symbol("SYNTH-CAMERA-ORIGINAL"));
        await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 2));
        Check(h.Peer.Requests.Count == 2, "qualified re-placement permits one independent native camera request");
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
        using (var performance = JsonDocument.Parse(Wire.Encode(source.ReadPerformance())))
        {
            var pool = performance.RootElement.GetProperty("OriginalFrames");
            Check(pool.GetProperty("Frames").GetInt32() == 32 && pool.GetProperty("Bytes").GetInt64() <= pool.GetProperty("MaximumBytes").GetInt64() &&
                source.ReadDiagnostics() is { NativeBufferBorrowed: false, RetainedNativeFrameBytes: > 0 },
                "actual pool and retained native frame telemetry account for bounded capacity without retaining the SDK borrowed buffer");
        }
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
        await h.TrayAsync(h.Left, TrayPresence.Absent); await SmRoundFixture.NextVehicleAsync(h.App, "left");
        h.Api(h.Left).EnqueueNative(CameraRoundHarness.EmptyTray, 2, [Symbol("SYNTH-NATIVE-BEFORE-SEND")]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-NATIVE-BEFORE-SEND" && o.Wcs?.State == StageState.Accepted));
        Check(h.Peer.Requests.Count == 3 && (await h.App.OperationAsync(locked.OperationId!))?.Wcs is { State: StageState.Prepared, Attempts: 0 },
            "an unsent prepared native debug request cannot block the next production parcel");
        var sent = await h.App.SendCameraRoundAsync(await h.SendCommandAsync(locked), default);
        Check(h.Peer.Requests.Count == 3 && sent.SendAttempts == 0 && sent.HttpResponse is null && !sent.CanSend &&
            (await h.App.OperationAsync(locked.OperationId!))?.Wcs is { State: StageState.Prepared, Attempts: 0 },
            "a stale native debug request cannot send to a newer production-claimed arrival");
        await h.TrayAsync(h.Left, TrayPresence.Absent); await SmRoundFixture.NextVehicleAsync(h.App, "left");
        h.Api(h.Left).EnqueueNative(CameraRoundHarness.EmptyTray, 3, [Symbol("SYNTH-NATIVE-DEBUG-NEXT")]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-NATIVE-DEBUG-NEXT" && o.Wcs?.State == StageState.Accepted));
        Check((await h.App.ReadCameraReadRoundAsync(default)).Frame!.Id == locked.Frame.Id && h.Factory.Decoders[h.Left].Count == 0 && h.Peer.Requests.Count == 4,
            "locked native debug picture does not freeze production, use ZXing or change to a later frame");
        await h.EndAsync();
        Check(h.Peer.Requests.Count == 4 && !(await h.App.CameraInspectionStatusAsync(default)).BusinessFenced,
            "ending native debugging clears the capture boundary without replaying a request");
    }
}
