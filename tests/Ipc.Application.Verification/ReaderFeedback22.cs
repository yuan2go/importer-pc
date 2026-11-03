using System.Diagnostics;
using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

internal static class ReaderFeedback22
{
    static void Check(bool ok, string detail) { if (!ok) throw new InvalidOperationException(detail); Console.WriteLine("PASS READER22 " + detail); }
    internal static async Task Run(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "reader22"), 1500);
        await h.StartAsync();
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-CURRENT"), 1);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.State == ParcelState.SlotResolved));
        var original = (await h.App.SnapshotAsync()).Inputs!.Single(i => i.PositionId == "left").Observation;
        var enqueue = typeof(ParcelRuntime).GetMethod("Enqueue", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var reason in new[] { "age", "epoch", "future", "sequence", "operator" })
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
                "age" => observation with { ObservedAt = DateTimeOffset.UtcNow.AddSeconds(-2) },
                "epoch" => observation with { Epoch = "obsolete-epoch" },
                "future" => observation with { ObservedAt = DateTimeOffset.UtcNow.AddMinutes(1) },
                "operator" => observation with { OperatorInput = new OperatorInputEvidence("forged", "Keyboard", "synthetic", null, DateTimeOffset.UtcNow) },
                _ => observation with { Sequence = original.Sequence }
            };
            Check((bool)enqueue.Invoke(h.App, [observation])!, reason + " synthetic source is durably handed off, not treated as admission");
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Inputs!.Any(i => i.Observation.Id == observation.Id));
            var snapshot = await h.App.SnapshotAsync(); var input = snapshot.Inputs!.Single(i => i.PositionId == "left");
            Check(input.Outcome == ParcelInputOutcome.Rejected && input.OperationId is null && input.Reason.StartsWith("UntrustedObservation") &&
                input.Observation.CaptureSessionId is null && input.Observation.OperatorInput is null && snapshot.Operations.Length == 1 && h.Peer.Requests.Count == 1,
                reason + " has an explicit terminal rejection without trusting capture IDs or creating any operation/POST");
            if (reason == "operator") Check(!(await h.App.ReadPresentationFeedbackAsync(default)).Single(r => r.PositionId == "left").OperatorInput,
                "camera-supplied operator metadata cannot claim the manual-command presentation channel");
        }
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-NEXT"), 2);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-NEXT" && o.State == ParcelState.SlotResolved));
        Check(h.Peer.Requests.Count == 2, "rejected untrusted observations do not advance trusted sequence or replay the original request");
        var oldEpoch = h.Factory.Sources[h.Left].Epoch;
        await h.App.ReconnectDeviceAsync(h.Left);
        await CameraRoundHarness.Until(async () => h.Factory.Sources[h.Left].Epoch != oldEpoch && (await h.App.SnapshotAsync()).Devices!.Single(d => d.DeviceId == h.Left).IsValid(DateTimeOffset.UtcNow));
        var previousProcessed = h.App.WorkerProgress().Single(p => p.Position == "left").Processed;
        var oldQueued = original with
        {
            Id = Wire.Id(),
            SourceEventId = "synthetic.old-queued",
            AdmissionId = "synthetic.old-queued",
            Sequence = 99,
            DecodedTicks = Stopwatch.GetTimestamp(),
            ObservedAt = DateTimeOffset.UtcNow
        };
        Check((bool)enqueue.Invoke(h.App, [oldQueued])!, "old queued camera epoch remains a durable input to be explicitly disposed after reconnect");
        await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(p => p.Position == "left").Processed > previousProcessed));
        var retired = (await h.App.SnapshotAsync()).Inputs!.Single(i => i.PositionId == "left");
        Check(retired.Observation.Id == oldQueued.Id && retired.Outcome == ParcelInputOutcome.Rejected && h.Peer.Requests.Count == 2,
            "actual reconnect's retired epoch has a matching terminal rejection, not orphan processing or request replay");
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-NEW-EPOCH"), 1);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-NEW-EPOCH" && o.State == ParcelState.SlotResolved));
        Check(h.Peer.Requests.Count == 3, "first verified frame in the new epoch creates exactly one independent request");
        var vm = new SupplyStationViewModel(h.App); await vm.RefreshAsync(); vm.SetViewport(1366, 768);
        var state = vm.Snapshot!.Devices!.Single(d => d.DeviceId == h.Left);
        ParcelReadFeedback Read(long ticks, string epoch) => new(vm.Snapshot.RuntimeId, "left", Wire.Id(), h.Left, epoch, "SYNTH-LATEST", ticks, DateTimeOffset.UtcNow)
        { SourceSequence = 100, PendingDisplayBudgetMs = 1000 };
        vm.Left.ApplyReadFeedback(Read(Stopwatch.GetTimestamp() - 2 * Stopwatch.Frequency, state.Epoch));
        Check(vm.Left.ShowConfirmedTask && !vm.Left.IsProcessing, "feedback older than the snapshot watermark cannot replace its accepted current observation");
        vm.Left.ApplyReadFeedback(Read(Stopwatch.GetTimestamp(), state.Epoch));
        Check(vm.Left.IsProcessing && !vm.Left.ShowConfirmedTask, "a newer orphan cue first withdraws the prior accepted destination");
        await Task.Delay(1100);
        Check(!vm.Left.IsProcessing && !vm.Left.ShowConfirmedTask && !vm.Left.CanViewCurrentPicture && vm.Left.DisplayStatus.Contains("失效"),
            "orphan feedback exceeds its monotonic display deadline without reviving an old chute/picture");
        vm.Left.ApplyReadFeedback(Read(Stopwatch.GetTimestamp(), "old-epoch"));
        Check(!vm.Left.IsProcessing && !vm.Left.ShowConfirmedTask && vm.Left.DisplayStatus.Contains("失效"), "unacknowledged feedback from a previous connection epoch is explicitly invalidated");
        vm.Left.ApplyReadFeedback(Read(Stopwatch.GetTimestamp(), state.Epoch));
        Check(vm.Left.IsProcessing && !vm.Left.ShowConfirmedTask, "fresh current-epoch observation resumes processing without a cached destination");
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
            Check(PresentationPreferences.ReadCueVolume("sportmaster") == 0, "first-use cue volume defaults to mute");
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
