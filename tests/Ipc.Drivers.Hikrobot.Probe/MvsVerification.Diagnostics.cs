using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;
internal static partial class MvsVerification
{
    static async Task VerifyAcquisitionDiagnostics()
    {
        using var pixels = new PixelLoan(4);
        var api = new Fake(); var decoder = new ControlledDecoder { Codes = [] }; var delivered = 0;
        await using var source = new MvsAcquisition(new("synthetic.preview", api.Ip, new Dictionary<string, string> { ["device.ip"] = api.Ip }), api, _ => new Lease(), decoder: decoder);
        await source.StartControlledAsync("preview-epoch", _ => { delivered++; return true; }, _ => { }, default);
        await Until(() => source.ReadDiagnostics().NoData > 2);
        Check(source.ReadDiagnostics() is { State: "WaitingForFrame", Frames: 0, DecodeStarted: 0 }, "connected no-data loop is explicitly distinct from successful frame acquisition and decoding");
        source.ReadPreview(); api.Frames.Enqueue(pixels.Frame);
        await Until(() => source.ReadDiagnostics().EmptyFrames == 1);
        Check(source.ReadPreview() is { FrameNumber: 1 } && source.ReadDiagnostics() is { DecodeStarted: 1, DecodeCompleted: 1, Candidates: 0, State: "NoBarcode" } && delivered == 0,
            "a real empty decode still exposes a video frame and completed/empty counters without a business observation");
        await source.SetInspectionAsync(new("paused-inspection", true, _ => true) { DecodeAllowed = _ => false }, default);
        await Task.Delay(210); source.ReadPreview(); api.Frames.Enqueue(pixels.Frame with { Number = 2 });
        await Until(() => source.ReadPreview()?.FrameNumber == 2);
        Check(source.ReadDiagnostics() is { Frames: 2, DecodeCompleted: 1, DecodePaused: true, State: "DecodePaused" } && decoder.Calls == 1 && delivered == 0 && api.Freed == 2,
            "paused single-round decoding continues live video and releases native loans without decoding or sending a candidate");
        await Task.Delay(3100);
        Check(source.ReadDiagnostics().State == "FrameStale", "a previously received frame ages to explicit stalled acquisition even while connection remains online");
        await source.SetInspectionAsync(null, default); decoder.Codes = ["SYNTH-CODE"]; api.Frames.Enqueue(pixels.Frame with { Number = 3 });
        await Until(() => source.ReadDiagnostics().Delivered == 1);
        Check(source.ReadDiagnostics() is { Frames: 3, DecodeStarted: 2, DecodeCompleted: 2, Candidates: 1, Delivered: 1, Rejected: 0 }, "candidate detection and accepted handoff are counted separately from frames and empty decodes");
        await source.StopAsync(default);
        await source.StartControlledAsync("new-epoch", _ => false, _ => { }, default);
        Check(source.ReadDiagnostics() is { Epoch: "new-epoch", Frames: 0, DecodeCompleted: 0, LastFrameAt: null } && source.ReadPreview() is null,
            "reconnect resets epoch counters, frame freshness and video without carrying old evidence forward");
        api.Frames.Enqueue(pixels.Frame); await Until(() => source.Failure.Length > 0);
        Check(source.ReadDiagnostics() is { Candidates: 1, Delivered: 0, Rejected: 1, State: "Faulted" } && source.Undelivered is not null,
            "rejected handoff remains an explicit retained original and is not misreported as decoder failure or successful delivery");
    }
}
