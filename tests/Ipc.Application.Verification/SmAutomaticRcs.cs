using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Platform;

static partial class SportmasterRcs
{
    public static async Task AutomaticRcs(string workspace, string root)
    {
        await NoDestinationPolicyChecks(workspace, root);
        var removed = RcsTaskOptions.RetiredKeys;
        var initial = new SportmasterFlow().InitialConfiguration();
        Check(!initial.Wcs!.Parameters.Keys.Intersect(removed).Any(), "SM new stations require no allocation range or fixed-chute fields");
        var schema = Wire.Decode<ParameterField[]>(File.ReadAllText(Path.Combine(workspace, "src/Ipc.Integrations.SportmasterRcs/parameters.json")));
        Check(!schema.Any(p => removed.Contains(p.Key)), "retired options are absent from the component schema");
        await using var peer = new Peer();
        var binding = Binding(peer.Endpoint);
        Check(ExtensionSchema.Validate(schema, binding.Parameters).Length == 0, "ordinary RCS configuration validates without retired fields");
        await using var service = new SportmasterRcsTaskService(binding);
        Check(service.UsesAutomaticRequestIds && service.GetRequestIdRange(binding) is null, "SM requests use automatic persisted IDs and have no allocation budget");
        var request = Request(binding);
        foreach (var id in new long[] { 1, 10001, 1000001, long.MaxValue })
        {
            var wire = service.PrepareRequest(request, id);
            using var json = JsonDocument.Parse(wire.Body);
            var args = json.RootElement.GetProperty("data")[0].GetProperty("params");
            Check(wire.RequestId == id && !args.GetProperty("specifiedChute").GetBoolean() && args.GetProperty("chuteCode").ValueKind == JsonValueKind.Null,
                "ordinary request uses its assigned ID without a configured ceiling: " + id);
        }
        var old = binding with { Parameters = new(binding.Parameters) { ["request-id-base"] = "810000000", ["request-id-count"] = "10000", ["specified-chute"] = "true", ["chute-code"] = "OLD-CHUTE" } };
        await using var oldService = new SportmasterRcsTaskService(old);
        var oldRequest = Request(old); var seedWire = oldService.PrepareRequest(oldRequest, 810000001);
        var originalBody = seedWire.Body.Replace("\"specifiedChute\":false", "\"specifiedChute\":true", StringComparison.Ordinal).Replace("\"chuteCode\":null", "\"chuteCode\":\"OLD-CHUTE\"", StringComparison.Ordinal);
        var originalWire = seedWire with { Body = originalBody, BodyHash = Wire.Hash(originalBody) };
        var frozen = oldRequest with { IntegrationRequest = originalWire }; var originalBytes = Wire.Encode(frozen);
        Check(RcsDestinationProtocol.MatchesFrozen(new(old), frozen, originalWire), "already-frozen fixed-chute wire evidence is still validated without regeneration");
        var fresh = oldService.PrepareRequest(oldRequest, 900000001);
        using (var json = JsonDocument.Parse(fresh.Body)) Check(!json.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("specifiedChute").GetBoolean(), "retired old settings cannot enable fixed chutes for new requests");
        await using (var factory = new ParcelCapabilityFactory(new(Path.Combine(workspace, "config/sportmaster"), Path.Combine(root, "automatic-schema"), null, null)))
        {
            var candidate = initial with { Wcs = old };
            Check(!factory.Normalize(candidate).Wcs!.Parameters.Keys.Intersect(removed).Any(), "candidate normalization removes obsolete fields before configuration CAS");
            var draft = new CapabilityDraft("integration", factory.Catalog().Providers, old with { Implementation = "sportmaster-rcs" }, true);
            foreach (var p in draft.Parameters.Where(p => p.Field.Key is "left-station-code" or "right-station-code")) p.Value = "SYNTH-STATION";
            Check(!draft.Parameters.Any(p => removed.Contains(p.Field.Key)) && !draft.Build().Parameters.Keys.Intersect(removed).Any(), "daily and first-run drafts cannot save retired fields");
        }
        Check(Wire.Encode(frozen) == originalBytes && peer.Requests.Count == 0, "configuration cleanup leaves original requests byte-identical and never sends them");
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "automatic-runtime"), deferred: true);
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        async Task<ParcelOperation> Submit(string code)
        {
            var receipt = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", code), default);
            Check(receipt.Status == "Admitted", "ordinary manual source is admitted without allocation parameters: " + code);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.Id == receipt.OperationId && o.State == ParcelState.SlotResolved));
            return (await h.App.SnapshotAsync()).Operations.Single(o => o.Id == receipt.OperationId);
        }
        var first = await Submit("AUTO-FIRST"); var second = await Submit("AUTO-SECOND");
        var firstId = first.Wcs!.Request.IntegrationRequest!.RequestId; var secondId = second.Wcs!.Request.IntegrationRequest!.RequestId;
        Check(firstId > 10000 && secondId > firstId, "runtime assigns increasing numeric IDs beyond the removed 10000 ceiling");
        var unchanged = Wire.Encode(second.Wcs.Request);
        await h.App.DisposeAsync(); await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        var third = await Submit("AUTO-RESTART");
        Check(third.Wcs!.Request.IntegrationRequest!.RequestId > secondId && h.Peer.Requests.Count == 3, "restart preserves the permanent ID floor and sends only the new request");
        var snapshot = await h.App.SnapshotAsync();
        Check(Wire.Encode(snapshot.Operations.Single(o => o.Id == second.Id).Wcs!.Request) == unchanged, "reopening retains original IDs and exact wire bytes");
        Check(!snapshot.AuxiliaryReasons!.Any(r => r.StartsWith("IntegrationIdBudget:", StringComparison.Ordinal)), "SM diagnostics contain no allocation capacity or renewal requirement");
    }
}
