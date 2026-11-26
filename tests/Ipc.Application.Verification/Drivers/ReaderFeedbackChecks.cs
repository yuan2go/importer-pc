using System.Diagnostics;
using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

internal static class ReaderFeedbackChecks
{
    static void Check(bool ok, string detail) { if (!ok) throw new InvalidOperationException(detail); Console.WriteLine("PASS READER22 " + detail); }
    internal static async Task Run(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "reader22"), 1500);
        await h.StartAsync();
        await h.WaitForCameraAdmissionAsync(h.Left);
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-CURRENT"), 1);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.State == ParcelState.SlotResolved));
        var first = await h.App.SnapshotAsync();
        var original = first.Inputs!.Single(i => i.PositionId == "left").Observation;
        var originalOperation = first.Operations.Single();
        var enqueue = typeof(ParcelRuntime).GetMethod("Enqueue", BindingFlags.NonPublic | BindingFlags.Instance)!;
        long Dispositions(string disposition) => h.App.WorkerProgress().Single(lane => lane.Position == "left").Dispositions.GetValueOrDefault(disposition);
        async Task ObserveRejected(Observation observation, string disposition)
        {
            var previous = Dispositions(disposition);
            Check((bool)enqueue.Invoke(h.App, [observation])!, "camera callback is handled: " + observation.SourceEventId);
            await CameraRoundHarness.Until(() => Task.FromResult(Dispositions(disposition) == previous + 1));
        }
        foreach (var reason in new[] { "epoch", "future", "sequence", "operator" })
        {
            var observation = original with
            {
                Id = Wire.Id(),
                SourceEventId = "synthetic-" + reason,
                AdmissionId = "synthetic." + reason,
                Codes = ["SYNTH-REJECTED-" + reason],
                DecodedTicks = Stopwatch.GetTimestamp(),
                Sequence = original.Sequence + 10,
                ObservedAt = DateTimeOffset.UtcNow,
                CaptureSessionId = "untrusted-capture-must-not-link"
            };
            observation = reason switch
            {
                "epoch" => observation with { Epoch = "obsolete-epoch" },
                "future" => observation with { ObservedAt = DateTimeOffset.UtcNow.AddMinutes(1) },
                "operator" => observation with { OperatorInput = new OperatorInputEvidence("forged", "Keyboard", "synthetic", null, DateTimeOffset.UtcNow) },
                _ => observation with { Sequence = original.Sequence }
            };
            await ObserveRejected(observation, reason == "epoch" ? "OldArrivalGeneration" : "UntrustedSource");
            var snapshot = await h.App.SnapshotAsync();
            Check(snapshot.Positions!.Single(lane => lane.Id == "left").CurrentRoundId == originalOperation.RoundId &&
                Wire.Encode(snapshot.Operations.Single().Wcs) == Wire.Encode(originalOperation.Wcs) && h.Peer.Requests.Count == 1,
                reason + " cannot replace the current round or create a second request");
            if (reason == "operator")
                Check((await h.App.ReadPresentationFeedbackAsync(default)).All(read => read.PositionId != "left" || !read.OperatorInput),
                    "camera-supplied operator metadata cannot claim the manual-command channel");
        }

        await SmRoundFixture.NextVehicleAsync(h.App, "left");
        await h.WaitForCameraAdmissionAsync(h.Left);
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-NEXT"), 2);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-NEXT" && o.State == ParcelState.SlotResolved));
        Check(h.Peer.Requests.Count == 2, "untrusted high sequences do not poison the next qualified parcel");
        var current = (await h.App.SnapshotAsync()).Inputs!.Single(i => i.PositionId == "left").Observation;
        await ObserveRejected(current with
        {
            Id = Wire.Id(),
            SourceEventId = "synthetic-age",
            AdmissionId = "synthetic.age",
            Sequence = current.Sequence + 1,
            ObservedAt = DateTimeOffset.UtcNow.AddSeconds(-2),
            DecodedTicks = Stopwatch.GetTimestamp(),
            CaptureSessionId = "untrusted-capture-must-not-link"
        }, "Expired");
        Check(h.Peer.Requests.Count == 2, "an expired observation cannot dispatch");

        var oldEpoch = h.Factory.Sources[h.Left].Epoch;
        Check(current.Epoch == oldEpoch, "the saved observation belongs to the session about to be retired");
        var reconnect = await h.App.ReconnectDeviceAsync(h.Left);
        Check(reconnect.Status == "Reconnected", "the fixture reconnects the left camera: " + reconnect.Reason);
        // Healthy device registration can publish before the new arrival capture boundary.
        await CameraRoundHarness.Until(async () =>
        {
            var snapshot = await h.App.SnapshotAsync();
            var device = snapshot.Devices!.Single(d => d.DeviceId == h.Left);
            return device.Epoch != oldEpoch && device.Epoch == h.Factory.Sources[h.Left].Epoch &&
                device.IsValid(DateTimeOffset.UtcNow) &&
                snapshot.Positions!.Single(lane => lane.Id == "left").Arrival?.CameraBoundaryReady == true;
        });
        await ObserveRejected(current with
        {
            Id = Wire.Id(),
            SourceEventId = "synthetic-old-queued",
            AdmissionId = "synthetic.old-queued",
            Sequence = 99,
            ObservedAt = DateTimeOffset.UtcNow,
            DecodedTicks = Stopwatch.GetTimestamp()
        }, "OldArrivalGeneration");
        Check(h.Peer.Requests.Count == 2, "the retired epoch cannot replay either request");
        await SmRoundFixture.NextVehicleAsync(h.App, "left");
        await h.WaitForCameraAdmissionAsync(h.Left);
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-NEW-EPOCH"), 1);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-NEW-EPOCH" && o.State == ParcelState.SlotResolved));
        Check(h.Peer.Requests.Count == 3, "the new epoch handles the next physical arrival exactly once");

        var vm = new SupplyStationViewModel(h.App);
        await vm.RefreshAsync();
        vm.SetViewport(1366, 768);
        var state = vm.Snapshot!.Devices!.Single(d => d.DeviceId == h.Left);
        var success = vm.Left.SuccessIdentity;
        Check(success is not null, "the current confirmed round owns its displayed chute");
        foreach (var (at, epoch) in new[] { (Stopwatch.GetTimestamp() - 2 * Stopwatch.Frequency, state.Epoch),
            (Stopwatch.GetTimestamp(), "old-epoch"), (Stopwatch.GetTimestamp(), state.Epoch) })
        {
            vm.Left.ApplyReadFeedback(new(vm.Snapshot.RuntimeId, "left", Wire.Id(), h.Left, epoch, "SYNTH-LATEST", at, DateTimeOffset.UtcNow)
            { SourceSequence = 100, PendingDisplayBudgetMs = 1000 });
            Check(vm.Left.SuccessIdentity == success && !vm.Left.IsProcessing,
                "read feedback alone cannot replace the confirmed round's result");
        }
        var options = BundledMvs.ReaderDescriptor.Parameters.Single(p => p.Key == "code.formats").Choices!;
        foreach (var option in options)
        {
            var settings = ReaderSettings.Read(new Dictionary<string, string> { ["code.formats"] = Wire.Encode(new[] { option }) });
            Check(settings.NativeFormats() is not null, "declared barcode option maps through installed ZXingCpp 0.5.3: " + option);
        }
        try { _ = new ReaderSettings(["MissingSyntheticFormat"], true).NativeFormats(); throw new InvalidOperationException("Missing map was accepted"); }
        catch (InvalidDataException error) { Check(error.Message.Contains("code.formats") && error.Message.Contains("MissingSyntheticFormat"), "unsupported mapping reports the exact configuration key/name instead of NullReferenceException"); }
        var priorRoot = PresentationPreferences.UserRootForVerification;
        PresentationPreferences.UserRootForVerification = () => Path.Combine(root, "audio-preference");
        try
        {
            Check(PresentationPreferences.ReadCueVolume("sportmaster") == PresentationPreferences.DefaultCueVolume, "first-use cue volume is 80 percent");
            foreach (var volume in new[] { 0, 30, 100 }) { PresentationPreferences.SaveCueVolume("sportmaster", volume); Check(PresentationPreferences.ReadCueVolume("sportmaster") == volume, "volume survives a preference read: " + volume); }
            try { PresentationPreferences.SaveCueVolume("sportmaster", 101); throw new InvalidOperationException("Out-of-range volume accepted"); }
            catch (InvalidDataException) { Check(PresentationPreferences.ReadCueVolume("sportmaster") == 100, "invalid volume preserves the last saved preference"); }
            var file = Path.Combine(root, "audio-preference/presentation/sportmaster.audio.json"); File.WriteAllText(file, "{\"Schema\":2,\"Volume\":30}");
            var before = File.ReadAllBytes(file);
            try { PresentationPreferences.SaveCueVolume("sportmaster", 50); throw new InvalidOperationException("Invalid schema overwritten"); }
            catch (InvalidDataException) { Check(File.ReadAllBytes(file).SequenceEqual(before), "unsupported preference preserves its original bytes"); }
        }
        finally { PresentationPreferences.UserRootForVerification = priorRoot; }
        h.Api(h.Right).SetEnumValue("TriggerMode", 1);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Devices!.Single(d => d.DeviceId == h.Right).Health == DeviceHealth.Faulted);
        await Task.Delay(1200);
        Check(h.Api(h.Right).Opens == 1 && (await h.App.SnapshotAsync()).Devices!.Single(d => d.DeviceId == h.Right).Health == DeviceHealth.Faulted,
            "external trigger configuration tampering stays Faulted for maintenance instead of unsafe automatic restart");
        Check((await h.App.SnapshotAsync()).Devices!.Single(d => d.DeviceId == h.Left).IsValid(DateTimeOffset.UtcNow), "configuration Faulted on one camera preserves the other verified camera owner");
    }
}
