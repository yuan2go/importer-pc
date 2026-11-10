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
        var originalOperator = Request(binding);
        var payload = Wire.Decode<ParcelPayload>(originalOperator.Payload);
        var operatorId = Wire.Id();
        var operatorPayload = Wire.Encode(payload with
        {
            Acquisition = null,
            AdmissionId = "manual." + operatorId,
            OperatorInput = new(operatorId, "Keyboard", "SYNTH-OPERATOR", null, DateTimeOffset.UtcNow)
        });
        originalOperator = originalOperator with { Payload = operatorPayload, PayloadHash = Wire.Hash(operatorPayload) };
        var operatorWire = service.PrepareRequest(originalOperator, 42);
        var legacyBody = operatorWire.Body.Replace("\"MANUAL\"", "\"BAR\"", StringComparison.Ordinal);
        var legacyWire = operatorWire with { Body = legacyBody, BodyHash = Wire.Hash(legacyBody) };
        Check(RcsDestinationProtocol.MatchesFrozen(new(binding), originalOperator with { IntegrationRequest = legacyWire }, legacyWire),
            "original manual request with the historical BAR envelope remains valid exact evidence");
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "automatic-runtime"), deferred: true);
        var configured = h.Config with { Wcs = h.Config.Wcs! with { Parameters = new(h.Config.Wcs!.Parameters)
        {
            [RcsRequestPolicy.DestinationMode] = "true",
            [RcsRequestPolicy.DestinationChuteCode] = "00123"
        } } };
        foreach (var invalid in new[] { "", "  ", "BAD\u0001", new string('A', 65) })
        {
            var invalidBinding = configured.Wcs! with { Parameters = new(configured.Wcs!.Parameters) { [RcsRequestPolicy.DestinationChuteCode] = invalid } };
            var deferred = configured with { Wcs = invalidBinding with { Target = "" }, DeferredConnections = [.. configured.DeferredConnections!, invalidBinding.InstanceId] };
            Check(new SportmasterFlow().Validate(deferred).Any(e => e.Contains("格口编码")), "invalid specified chute fails configuration validation even with a deferred address");
        }
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), configured);
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        async Task<ParcelOperation> Submit(string code, ParcelBarcodeInputKind kind = ParcelBarcodeInputKind.Keyboard)
        {
            var receipt = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", code, kind), default);
            Check(receipt.Status == "Admitted", "ordinary manual source is admitted without allocation parameters: " + code);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.Id == receipt.OperationId && o.State == ParcelState.SlotResolved));
            return (await h.App.SnapshotAsync()).Operations.Single(o => o.Id == receipt.OperationId);
        }
        var first = await Submit("AUTO-FIRST"); var second = await Submit("AUTO-SECOND", ParcelBarcodeInputKind.PdaKeyboard);
        foreach (var (sent, source) in h.Peer.Requests.Zip(new[] { "MANUAL", "PDA" }))
        {
            using var json = JsonDocument.Parse(sent);
            var parameters = json.RootElement.GetProperty("data")[0].GetProperty("params");
            Check(json.RootElement.GetProperty("source").GetString() == source && parameters.GetProperty("specifiedChute").GetBoolean() &&
                parameters.GetProperty("chuteCode").ValueKind == JsonValueKind.String && parameters.GetProperty("chuteCode").GetString() == "00123",
                "actual RCS POST freezes " + source + " and preserves the specified chute as a string including leading zeroes");
        }
        Check(first.Slot == "SYNTH-CHUTE" && second.Slot == "SYNTH-CHUTE", "operator result uses the actual RCS assignment");
        h.Peer.AssignedData = new { chuteCode = "SYNTH-CHUTE-ACCEPTED", height = "2.5", length = "-", quantity = 1, skuId = "-", taskId = "SYNTH-TASK", waveNo = "-", weight = "0.75", width = "-" };
        var firstId = first.Wcs!.Request.IntegrationRequest!.RequestId; var secondId = second.Wcs!.Request.IntegrationRequest!.RequestId;
        Check(firstId > 10000 && secondId > firstId, "runtime assigns increasing numeric IDs beyond the removed 10000 ceiling");
        var unchanged = Wire.Encode(second.Wcs.Request);
        await h.App.DisposeAsync(); await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        var third = await Submit("AUTO-RESTART");
        Check(third.Wcs!.Request.IntegrationRequest!.RequestId > secondId && h.Peer.Requests.Count == 3, "restart preserves the permanent ID floor and sends only the new request");
        var fieldReply = third.Wcs.Replies.Single();
        Check(third.Slot == "SYNTH-CHUTE-ACCEPTED" && fieldReply is { ProtocolAcknowledged: true, BusinessAccepted: true, PhysicalCompleted: null,
            Destination: { Quantity: "1", Length: null, Width: null, Height: 2.5m, Weight: 0.75m } },
            "a correlated RCS success with numeric quantity, measurement strings and dash placeholders settles the operation without claiming physical completion");
        using var fieldBody = JsonDocument.Parse(Convert.FromBase64String(fieldReply.HttpResponse!.BodyBase64));
        var fieldData = fieldBody.RootElement.GetProperty("result")[0].GetProperty("data");
        Check(fieldData.GetProperty("height").GetString() == "2.5" && fieldData.GetProperty("length").GetString() == "-" && fieldData.GetProperty("quantity").GetInt32() == 1,
            "the accepted operation retains the original RCS wire types and placeholders in its receipt");
        var snapshot = await h.App.SnapshotAsync();
        Check(Wire.Encode(snapshot.Operations.Single(o => o.Id == second.Id).Wcs!.Request) == unchanged, "reopening retains original IDs and exact wire bytes");
        Check(!snapshot.AuxiliaryReasons!.Any(r => r.StartsWith("IntegrationIdBudget:", StringComparison.Ordinal)), "SM diagnostics contain no allocation capacity or renewal requirement");
    }
}
