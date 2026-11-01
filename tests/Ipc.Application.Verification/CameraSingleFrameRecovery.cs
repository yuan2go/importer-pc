using Ipc.Application.Contracts;

internal static class CameraSingleFrameRecovery
{
    internal static async Task Run(string workspace, string folder)
    {
        await using var h = new CameraRoundHarness(workspace, folder, 30000); await h.StartAsync();
        var opens = h.Api(h.Left).Opens; var stops = h.Api(h.Left).Stops; var requests = h.Peer.Requests.Count;
        var source = h.Factory.Sources[h.Left];
        h.Api(h.Left).NextLostPackets = 1;
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-INCOMPLETE"), 1);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Api(h.Left).Releases >= 1 && (source.ReadDiagnostics().AcquireCalls > 1 || source.Failure.Length > 0)));
        if (source.Failure.Length != 0 || h.Peer.Requests.Count != requests || h.Api(h.Left).HasFrame)
            throw new InvalidOperationException("Single incomplete software-reader frame must be returned/rejected without fencing the camera: " + source.Failure);
        Console.WriteLine("PASS FRAME-RECOVERY incomplete frame returned without business request or camera fence");
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-AFTER-INCOMPLETE"), 2);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-AFTER-INCOMPLETE" && o.State == ParcelState.SlotResolved));
        if (source.Failure.Length != 0 || h.Api(h.Left).Opens != opens || h.Api(h.Left).Stops != stops || h.Peer.Requests.Count != requests + 1)
            throw new InvalidOperationException($"Following valid frame must resolve on the original owner without reconnect or duplicate request: failure={source.Failure}, opens={opens}->{h.Api(h.Left).Opens}, stops={stops}->{h.Api(h.Left).Stops}, requests={requests}->{h.Peer.Requests.Count}; bodies=" + string.Join(";", h.Peer.Requests));
        Console.WriteLine("PASS FRAME-RECOVERY next valid frame resolves through real ZXing/Application/SQLite/RCS on original camera owner");
    }
}
