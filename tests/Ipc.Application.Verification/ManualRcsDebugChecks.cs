using Ipc.Application.Contracts;
using Ipc.Platform;

internal static class ManualRcsDebugChecks
{
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS " + text); }
    public static async Task Run(string workspace, string root)
    {
        Console.WriteLine("EvidenceRoot=" + root);
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
        Check((await h.App.SnapshotAsync()).Operations.Single(o => o.Id == unknownId).Wcs is { State: StageState.Unknown, Attempts: 1 } && h.Peer.Requests.Count == 2,
            "unknown manual debug result retains one original attempt");
        try { await h.App.PrepareManualRcsRequestAsync(prepare with { CommandId = Wire.Id(), ExpectedRoundId = round.Id }, default); throw new Exception("unknown bypassed"); }
        catch (InvalidOperationException e) { Check(e.Message.Contains("原作业"), "new manual debug request cannot bypass unresolved original"); }
        var bytes = original.Wcs!.Request.IntegrationRequest!.Body;
        await h.App.DisposeAsync(); await h.StartAsync(ready: false); await ManagementModeFixture.Authorize(h.App);
        var restarted = await h.App.SnapshotAsync();
        Check(restarted.Operations.Single(o => o.Id == unknownId).Wcs is { State: StageState.Unknown, Attempts: 1 } &&
            (await h.App.ReadCameraOperationDetailAsync(original.Id, default))!.Original.Wcs!.Request.IntegrationRequest!.Body == bytes && h.Peer.Requests.Count == 2,
            "restart preserves manual debug frozen bytes/Unknown and never replays");
    }
}
