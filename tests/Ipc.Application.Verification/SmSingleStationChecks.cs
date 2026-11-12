using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Platform;

internal static class SmSingleStationChecks
{
    static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS SINGLESTATION " + message); }

    public static async Task Run(string workspace, string root)
    {
        const string station = "SYNTH-ONE-STATION";
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "single-station"), deferred: true);
        var config = h.Config with { StationCode = station };
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), config);
        await using var factory = new ManualSupplyHarness.ManualFactory(h.Launch);
        var draft = new StartupConfigurationDraft(config, factory.Catalog());
        draft.Policy.StationCode = "SYNTH-EDITED-STATION";
        Check(draft.Build().StationCode == "SYNTH-EDITED-STATION" && config.StationCode == station &&
            draft.Capabilities.Single(ParameterOwnership.IsRcs).Parameters.All(p => !p.Field.Key.EndsWith("-station-code", StringComparison.Ordinal)),
            "configuration edits one global supply station without per-side station parameters");

        await h.StartAsync();
        using (var heartbeat = JsonDocument.Parse(h.Peer.HeartbeatRequests.Last()))
        {
            var members = heartbeat.RootElement.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("params")).ToArray();
            Check(members.Length == 2 && members.All(x => x.GetProperty("stationCode").GetString() == station) &&
                members.Select(x => x.GetProperty("bcrCode").GetString()).ToHashSet().SetEquals(["MANUAL-LEFT", "MANUAL-RIGHT"]),
                "actual heartbeat shares the global station and keeps independent left/right camera codes");
        }
        var left = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "000123456"), default);
        var right = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "000123457", ParcelBarcodeInputKind.PdaKeyboard), default);
        if (left.Status != "Admitted" || right.Status != "Admitted")
            throw new InvalidOperationException("Synthetic submission receipts: " + Wire.Encode(new { left, right }));
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.Wcs?.State == StageState.Accepted) == 2);
        Check(left.Status == "Admitted" && right.Status == "Admitted" && h.Peer.Requests.Count == 2,
            "keyboard and PDA each send once at their own position without a connected camera");
        var sent = h.Peer.Requests.Select(body => JsonDocument.Parse(body)).ToArray();
        try
        {
            var destinations = sent.Select(json => json.RootElement.GetProperty("data")[0].GetProperty("params")).ToArray();
            Check(destinations.All(x => x.GetProperty("stationCode").GetString() == station) &&
                destinations.Select(x => x.GetProperty("bcrCode").GetString()).ToHashSet().SetEquals(["MANUAL-LEFT", "MANUAL-RIGHT"]),
                "actual destination requests use the heartbeat station and retain their own camera code");
        }
        finally { foreach (var json in sent) json.Dispose(); }

        await ManagementModeFixture.Authorize(h.App);
        var original = (await h.App.ReadCameraOperationDetailAsync(left.OperationId!, default))!.Original;
        var request = original.Wcs!.Request;
        var payload = Wire.Decode<ParcelPayload>(request.Payload);
        Check(payload.StationCode == station && original.FrozenConfiguration.StationCode == station,
            "persisted request freezes the station from its original operation configuration");
        await using (var service = new SportmasterRcsTaskService(request.Binding))
        {
            service.ConfigureRuntime(config with { StationCode = "SYNTH-LATER-STATION" });
            Check(service.PrepareRequest(request, request.IntegrationRequest!.RequestId).Body == request.IntegrationRequest.Body,
                "later runtime configuration cannot rewrite an original destination request");
        }
        foreach (var key in new[] { "left-station-code", "right-station-code" })
        {
            var invalid = config.Wcs! with { Parameters = new(config.Wcs!.Parameters) { [key] = "SYNTH-OBSOLETE" } };
            var rejected = false;
            try { _ = RcsRequestPolicy.TimeoutMs(invalid); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "obsolete " + key + " is rejected without migration or fallback");
        }
    }
}
