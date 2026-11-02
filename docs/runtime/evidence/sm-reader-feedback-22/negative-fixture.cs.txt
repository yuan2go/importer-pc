using System.Diagnostics;
using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;

// Kept independent of the new display contract so the same assertion runs on the pre-fix product.
internal static class ReaderIngressTerminal22
{
    internal static async Task Run(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "ingress-terminal22"), 1500);
        await h.StartAsync();
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-BASELINE"), 1);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.State == ParcelState.SlotResolved));
        var original = (await h.App.SnapshotAsync()).Inputs!.Single(i => i.PositionId == "left").Observation;
        await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(p => p.Position == "left").Processed == 1));
        var rejected = original with { Id = Wire.Id(), AdmissionId = "synthetic.expired", SourceEventId = "synthetic.expired", Sequence = 2,
            ObservedAt = DateTimeOffset.UtcNow.AddSeconds(-2), DecodedTicks = Stopwatch.GetTimestamp(), Codes = ["SYNTH-STALE"] };
        if (!(bool)typeof(ParcelRuntime).GetMethod("Enqueue", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(h.App, [rejected])!)
            throw new InvalidOperationException("Synthetic stale observation was not handed off");
        await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(p => p.Position == "left").Processed == 2));
        var input = (await h.App.SnapshotAsync()).Inputs!.Single(i => i.PositionId == "left");
        if (input.Observation.Id != rejected.Id || input.Outcome != ParcelInputOutcome.Rejected || h.Peer.Requests.Count != 1)
            throw new InvalidOperationException("B1: processed stale observation has no matching terminal rejection; read feedback can remain orphaned");
        Console.WriteLine("PASS READER22 processed stale observation has a matching durable terminal rejection and no new POST");
    }
}
