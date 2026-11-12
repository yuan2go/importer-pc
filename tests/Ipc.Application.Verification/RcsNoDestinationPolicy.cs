using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Platform;

static partial class SportmasterRcs
{
    static async Task NoDestinationPolicyChecks(string workspace, string root)
    {
        await using var peer = new Peer();
        var seed = Binding(peer.Endpoint);
        Check(RcsRequestPolicy.NoDestToExp(seed), "omitted no-destination setting defaults to true");
        foreach (var invalid in new[] { "", "TRUE", "0", "yes" })
        {
            var binding = seed with { Parameters = new(seed.Parameters) { ["no-dest-to-exp"] = invalid } };
            try { _ = RcsRequestPolicy.TimeoutMs(binding); throw new Exception("invalid no-destination policy accepted"); }
            catch (InvalidDataException) { Check(true, "invalid no-destination setting rejected before I/O: " + invalid); }
        }
        foreach (var value in new[] { "true", "false" })
        {
            var binding = seed with { Parameters = new(seed.Parameters) { ["no-dest-to-exp"] = value } };
            await using var service = new SportmasterRcsTaskService(binding);
            service.ConfigureRuntime(HeartbeatConfiguration(workspace, binding)); await service.StartAsync(default);
            var request = Request(binding); var wire = service.PrepareRequest(request, 51);
            request = request with { IntegrationRequest = wire };
            using var json = JsonDocument.Parse(wire.Body);
            Check(json.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("noDestToExp").GetBoolean() == (value == "true"), "request freezes a JSON boolean for " + value);
            var original = Wire.Encode(request);
            foreach (var mode in new[] { "no-destination", "no-destination-null", "no-destination-without-data" })
            {
                peer.Mode = mode;
                var beforeRequest = peer.Requests.Count;
                var reply = await service.RequestDestinationAsync(request, default);
                // Heartbeats share the peer, not the destination request's evidence.
                // Force the interleaving instead of depending on scheduler timing.
                await service.SendHeartbeatAsync(default);
                var exchange = peer.Requests.Skip(beforeRequest).Single(sent => sent.Body == wire.Body);
                Check(reply.ProtocolAcknowledged == true && reply.BusinessAccepted == false && reply.Slot is null &&
                    reply.Destination is { Outcome: WcsDestinationOutcome.Rejected, Code: null, Message: "合成异常：未找到可用格口，请人工核对包裹。" },
                    "success=false with msg confirms rejection without inventing a code or slot: " + value + "/" + mode);
                Check(reply.Destination!.ManualHandlingRequired == (value == "false"), "manual-handling fact follows the frozen request policy");
                Check(Convert.FromBase64String(reply.HttpResponse!.BodyBase64).SequenceEqual(System.Text.Encoding.UTF8.GetBytes(exchange.Response)), "rejection retains its complete original response evidence across an interleaved heartbeat");
            }
            peer.Mode = "assigned";
            Check((await service.RequestDestinationAsync(request, default)).BusinessAccepted == true, "normal assigned destinations still work with " + value);
            peer.Mode = "blank-chute";
            Check((await service.RequestDestinationAsync(request, default)).BusinessAccepted is null, "success with an empty chute remains unknown even with " + value);
            Check(Wire.Encode(request) == original, "response handling preserves the original request for " + value);
            var stripped = wire.Body.Replace(",\"noDestToExp\":" + value, "", StringComparison.Ordinal);
            Check(!RcsDestinationProtocol.MatchesFrozen(new(binding), request, wire with { Body = stripped, BodyHash = Wire.Hash(stripped) }), "an explicit frozen policy cannot be removed");
        }
        await using (var originalService = new SportmasterRcsTaskService(seed))
        {
            var request = Request(seed); var wire = originalService.PrepareRequest(request, 52);
            var body = wire.Body.Replace(",\"taskId\":52", "", StringComparison.Ordinal)
                .Replace(",\"noDestToExp\":true", "", StringComparison.Ordinal);
            var oldWire = wire with { Body = body, BodyHash = Wire.Hash(body) };
            Check(RcsDestinationProtocol.MatchesFrozen(new(seed), request with { IntegrationRequest = oldWire }, oldWire), "original requests predating the policy remain valid immutable evidence");
        }
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "no-destination-runtime"), deferred: true);
        var configuration = h.Config with { Wcs = h.Config.Wcs! with { Parameters = new(h.Config.Wcs!.Parameters) { ["no-dest-to-exp"] = "false" } } };
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), configuration);
        h.Peer.Mode = "no-destination";
        await h.StartAsync();
        var command = await h.Command("left", "SYNTH-NO-DEST");
        var receipt = await h.App.SubmitParcelBarcodeAsync(command, default);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.Id == receipt.OperationId && o.State == ParcelState.WcsRejected));
        var op = (await h.App.SnapshotAsync()).Operations.Single(o => o.Id == receipt.OperationId);
        Check(op.Wcs!.State == StageState.Rejected && op.Wcs.Attempts == 1 && op.Slot is null && ParcelPresentation.RcsManualHandling(op) &&
            ParcelPresentation.RcsException(op) == "合成异常：未找到可用格口，请人工核对包裹。", "runtime confirms a failed result and preserves the RCS msg for manual handling: " +
            $"stage={op.Wcs.State}; attempts={op.Wcs.Attempts}; slot={op.Slot ?? "<null>"}; policy={op.Wcs.Request.Binding.Parameters.GetValueOrDefault("no-dest-to-exp")}; manual={ParcelPresentation.RcsManualHandling(op)}; msg={ParcelPresentation.RcsException(op)}");
        var repeated = await h.App.SubmitParcelBarcodeAsync(command, default);
        Check(repeated.OperationId == receipt.OperationId && h.Peer.Requests.Count == 1, "same original command retains the rejection without another no-destination request");
        await ManagementModeFixture.Authorize(h.App);
        var frozen = Wire.Encode((await h.App.SnapshotAsync()).Operations.Single(o => o.Id == op.Id).Wcs!.Request);
        await h.App.DisposeAsync(); await h.StartAsync(ready: false); await ManagementModeFixture.Authorize(h.App);
        var restored = (await h.App.SnapshotAsync()).Operations.Single(o => o.Id == op.Id);
        Check(restored.State == ParcelState.WcsRejected && Wire.Encode(restored.Wcs!.Request) == frozen && h.Peer.Requests.Count == 1,
            "restart preserves the confirmed rejection, policy and original bytes without resend");
    }
}
