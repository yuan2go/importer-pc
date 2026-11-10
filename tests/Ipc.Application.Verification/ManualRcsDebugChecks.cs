using Ipc.Application.Contracts;
using Ipc.Platform;

internal static partial class ManualRcsDebugChecks
{
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS " + text); }
    public static async Task Run(string workspace, string root)
    {
        Console.WriteLine("EvidenceRoot=" + root);
        await Lifecycle(workspace, Path.Combine(root, "lifecycle"));
        Console.WriteLine("Synthetic camera ports; real SQLite and built-in RCS HTTP. No real hardware/site acceptance.");
        await using var h = new ManualSupplyHarness(workspace, root, deferred: true, rightCode: false, leftCode: false);
        await h.StartAsync();
        await ManagementModeFixture.Authorize(h.App);
        var snapshot = await h.App.SnapshotAsync();
        var prepare = new PrepareManualRcsRequest(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, "right", "0000011111", null);
        var round = await h.App.PrepareManualRcsRequestAsync(prepare, default);
        Check(round.ManualRequest && round.Frame is null && round.Barcode == "0000011111" && round.CanSend && h.Peer.Requests.IsEmpty,
            "prepare with no cameras/camera codes freezes a manual request without image or HTTP effect");
        Check((await h.App.PrepareManualRcsRequestAsync(prepare, default)).OperationId == round.OperationId && h.Peer.Requests.IsEmpty,
            "same prepare command retains the same original request");
        try { await h.App.PrepareManualRcsRequestAsync(prepare with { Barcode = "CHANGED" }, default); throw new Exception("changed prepare command accepted"); }
        catch (InvalidOperationException e) { Check(e.Message.Contains("reused"), "changed-content prepare cannot reuse command identity"); }
        var original = (await h.App.SnapshotAsync()).Operations.Single(o => o.Id == round.OperationId);
        original = (await h.App.ReadCameraOperationDetailAsync(original.Id, default))!.Original;
        Check(original.Acquisition is null && original.DebugRequest is null && original.OperatorDebugRequest?.RoundId == round.Id && original.OperatorInput?.CommandId == prepare.CommandId,
            "manual debug keeps operator evidence separate from physical camera evidence");
        using var body = System.Text.Json.JsonDocument.Parse(round.RequestBody!);
        Check(body.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("bcrCode").ValueKind == System.Text.Json.JsonValueKind.Null,
            "no camera code is invented in manual debug HTTP payload");
        var send = new SendCameraRound(Wire.Id(), snapshot.RuntimeId, round.Id!, round.RequestHash!);
        round = await h.App.SendCameraRoundAsync(send, default);
        Check(!round.CanSend && h.Peer.Requests.Count == 1 && (await h.App.SnapshotAsync()).Operations.Single(o => o.Id == round.OperationId).Wcs is { State: StageState.Accepted, Attempts: 1 },
            "explicit manual send reaches the actual RCS adapter exactly once");
        await h.App.SendCameraRoundAsync(send, default);
        Check(h.Peer.Requests.Count == 1 && (await h.App.SnapshotAsync()).Positions!.All(p => p.CurrentOperation is null),
            "repeat send does not replay; test destination is not projected as a production parcel");
        h.Peer.Mode = "malformed";
        round = await h.App.PrepareManualRcsRequestAsync(prepare with { CommandId = Wire.Id(), Barcode = "UNKNOWN", ExpectedRoundId = round.Id }, default);
        var unknownId = round.OperationId!;
        await h.App.SendCameraRoundAsync(new(Wire.Id(), snapshot.RuntimeId, round.Id!, round.RequestHash!), default);
        var unknown = (await h.App.OperationAsync(unknownId))!;
        Check(unknown.Wcs is { State: StageState.Unknown, Attempts: 1 } && h.Peer.Requests.Count == 2,
            "unknown manual debug result retains one original attempt");
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity?.HistoricalUnknown == 1);
        var independent = await h.App.PrepareManualRcsRequestAsync(prepare with { CommandId = Wire.Id(), ExpectedRoundId = round.Id }, default);
        Check(independent.OperationId != unknownId && independent.RequestId != round.RequestId && h.Peer.Requests.Count == 2,
            "a historical debug result allows a distinct preparation without sending the original or new request");
        await h.App.CloseUnsentDebugRoundAsync(new(Wire.Id(), snapshot.RuntimeId, independent.Id!), default);
        var bytes = original.Wcs!.Request.IntegrationRequest!.Body;
        await h.App.DisposeAsync(); await h.StartAsync(ready: false); await ManagementModeFixture.Authorize(h.App);
        var restarted = await h.App.SnapshotAsync();
        Check(restarted.Capacity?.HistoricalUnknown == 1 && restarted.Operations.All(o => o.Id != unknownId) &&
            (await h.App.OperationAsync(unknownId))?.Wcs is { State: StageState.Unknown, Attempts: 1 } &&
            (await h.App.ReadCameraOperationDetailAsync(original.Id, default))!.Original.Wcs!.Request.IntegrationRequest!.Body == bytes && h.Peer.Requests.Count == 2,
            "restart preserves manual debug frozen bytes/Unknown and never replays");
        await using var leaving = new ManualSupplyHarness(workspace, Path.Combine(root, "leaving"), deferred: true, rightCode: false, leftCode: false);
        await leaving.StartAsync(); await ManagementModeFixture.Authorize(leaving.App); var state = await leaving.App.SnapshotAsync();
        var unsent = await leaving.App.PrepareManualRcsRequestAsync(new(Wire.Id(), state.RuntimeId, state.Configuration.Revision, "left", "UNSENT", null), default);
        var close = new CloseUnsentDebugRound(Wire.Id(), state.RuntimeId, unsent.Id!);
        await leaving.App.CloseUnsentDebugRoundAsync(close, default); await leaving.App.CloseUnsentDebugRoundAsync(close, default);
        var cleaned = await leaving.App.SnapshotAsync();
        Check(cleaned.Operations.Single(o => o.Id == unsent.OperationId).Wcs is { State: StageState.Cancelled, Attempts: 0 } &&
            cleaned.Positions!.All(p => p.BarcodeInput?.Manual == true) && leaving.Peer.Requests.IsEmpty,
            "leaving a manual debug page cancels only its unsent intent, restores ordinary manual admission and never sends HTTP");
    }
}
