using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

// All identifiers and replies below are synthetic protocol fixtures. The listener
// is a local peer, independent of the application database and its receipt journal.
static partial class SportmasterRcs
{
    static void Check(bool value, string message)
    { if (!value) throw new Exception("FAIL RCS20: " + message); Console.WriteLine("PASS RCS20: " + message); }
    static Exchange[] NonHeartbeatRequests(Peer peer) => peer.Requests.Where(post =>
    {
        using var json = JsonDocument.Parse(post.Body);
        var data = json.RootElement.GetProperty("data");
        return data.GetArrayLength() == 0 || data.EnumerateArray().Any(item => item.GetProperty("command").GetString() != "sorter.task.station_heartbeat");
    }).ToArray();
    static CapabilityBinding Binding(string endpoint) => new("integration", "sportmaster-rcs-task", "1.0.0", "sportmaster.integration", endpoint, "SPORTMASTER-RCS-SORTER-v1", new()
    {
        ["zone-id"] = "71",
        ["project-id"] = "83",
        ["heartbeat-interval-ms"] = "3600000",
        ["left-bcr-code"] = "TEST-LEFT-READER",
        ["right-bcr-code"] = "TEST-RIGHT-READER"
    });
    static FrozenRequest Request(CapabilityBinding binding, string position = "left", string barcode = "SYNTH-0001")
    {
        var reader = new DeviceBinding("parcel-reader", "manual", "synthetic.reader." + position, "1");
        var acquisition = new AcquisitionIdentity(reader.DeviceId, "synthetic-epoch", "synthetic-event", "synthetic-admission", DateTimeOffset.UtcNow);
        var payload = Wire.Encode(new ParcelPayload(barcode, "synthetic-admission", null, null, null, Acquisition: acquisition, PositionId: position, ReaderBinding: reader) { StationCode = "SYNTH-STATION" });
        return new(Wire.Id(), "sportmaster", Wire.Id(), "synthetic-admission", "wcs-slot", binding, Wire.Hash("synthetic-provider"), Wire.Hash("synthetic-configuration"), payload, Wire.Hash(payload), DateTimeOffset.UtcNow);
    }
    static string Response(string request, string mode, IReadOnlyDictionary<string, string>? vehicles = null)
    {
        using var j = JsonDocument.Parse(request); var id = j.RootElement.GetProperty("requestId").GetInt64();
        var heartbeat = j.RootElement.GetProperty("data").GetArrayLength() == 0 || j.RootElement.GetProperty("data")[0].GetProperty("command").GetString() == "sorter.task.station_heartbeat";
        if (heartbeat) return JsonSerializer.Serialize(new
        {
            requestId = id,
            result = j.RootElement.GetProperty("data").EnumerateArray().Reverse().Select(item => new
            {
                index = item.GetProperty("index").GetInt32(),
                success = true,
                command = "sorter.task.station_heartbeat",
                data = new
                {
                    stationCode = item.GetProperty("params").GetProperty("stationCode").GetString(),
                    bcrCode = item.GetProperty("params").GetProperty("bcrCode").GetString(),
                    agvNo = vehicles?.GetValueOrDefault(item.GetProperty("params").GetProperty("bcrCode").GetString()!) ?? "SYNTH-AGV-" + (item.GetProperty("params").GetProperty("bcrCode").GetString()!.Contains("RIGHT", StringComparison.OrdinalIgnoreCase) ? 2 : 1),
                    status = item.GetProperty("index").GetInt32() == 1 ? "IDLE" : "TASKING"
                }
            }).ToArray()
        });
        if (mode.StartsWith("no-destination", StringComparison.Ordinal))
        {
            var result = new Dictionary<string, object?> { ["index"] = 1, ["success"] = false, ["command"] = "sorter.task.dest_request", ["msg"] = "合成异常：未找到可用格口，请人工核对包裹。" };
            if (mode != "no-destination-without-data") result["data"] = new { chuteCode = mode == "no-destination-null" ? null : "" };
            return JsonSerializer.Serialize(new { requestId = id, result = new[] { result } });
        }
        if (mode == "malformed") return "{not-json";
        if (mode == "oversize") return new string('x', SportmasterRcsTaskService.MaximumResponseBytes + 500);
        var data = mode switch
        {
            "unpack" => "{\"barcode\":\"SYNTH-0001\",\"skuId\":\"SYNTH-SKU\",\"scanQty\":1,\"chutes\":[{\"chuteNo\":\"SYNTH-CHUTE\",\"orderNo\":\"SYNTH-ORDER\",\"remaining\":0,\"phase\":\"ASSIGNED\"}]}",
            "reject" => "null",
            _ => "{\"chuteCode\":\"SYNTH-CHUTE\",\"length\":null,\"width\":null,\"height\":null,\"weight\":null,\"taskId\":\"SYNTH-TASK\",\"skuId\":\"SYNTH-SKU\"}"
        };
        var code = mode == "unpack" ? "\"NEED_UNPACK\"" : mode == "reject" ? "\"SYNTH_OTHER_ERROR\"" : "null";
        var response = "{\"requestId\":" + (mode == "wrong-id" ? id + 1 : id) + ",\"result\":[{\"index\":" + (mode == "wrong-index" ? 2 : 1) + ",\"success\":" + (mode is "unpack" or "reject" ? "false" : "true") + ",\"code\":" + code + ",\"message\":" + code + ",\"args\":null,\"command\":\"" + (mode == "wrong-command" ? "synthetic.other" : "sorter.task.dest_request") + "\",\"data\":" + data + "}]}";
        return mode switch
        {
            "task-fields" => response.Replace("\"taskId\":", "\"waveNo\":\"0007-WAVE\",\"quantity\":\"0002\",\"taskId\":", StringComparison.Ordinal),
            "numeric-response-quantity" => response.Replace("\"taskId\":", "\"quantity\":2,\"taskId\":", StringComparison.Ordinal),
            "fractional-response-quantity" => response.Replace("\"taskId\":", "\"quantity\":1.5,\"taskId\":", StringComparison.Ordinal),
            "invalid-measurement" => response.Replace("\"length\":null", "\"length\":\"invalid\"", StringComparison.Ordinal),
            "numeric-wave" => response.Replace("\"taskId\":", "\"waveNo\":7,\"taskId\":", StringComparison.Ordinal),
            "duplicate" => response.Replace("\"requestId\":", "\"requestId\":0,\"requestId\":", StringComparison.Ordinal),
            "missing-chute" => response.Replace("\"chuteCode\":\"SYNTH-CHUTE\",", "", StringComparison.Ordinal),
            "blank-chute" => response.Replace("\"chuteCode\":\"SYNTH-CHUTE\"", "\"chuteCode\":\" \"", StringComparison.Ordinal),
            "measured" => response.Replace("\"length\":null,\"width\":null,\"height\":null,\"weight\":null", "\"length\":12.34,\"width\":5.6,\"height\":7,\"weight\":0.89", StringComparison.Ordinal).Replace("\"args\":null", "\"args\":{\"synthetic\":[1,\"alpha\"]}", StringComparison.Ordinal),
            _ => response
        };
    }
    public static async Task Run(string workspace, string root)
    {
        await using var peer = new Peer(); var binding = Binding(peer.Endpoint);
        await using (var service = new SportmasterRcsTaskService(binding))
        {
            service.ConfigureRuntime(HeartbeatConfiguration(workspace, binding));
            await service.StartAsync(CancellationToken.None);
            Check((await service.HealthAsync(CancellationToken.None)).Ready && NonHeartbeatRequests(peer).Length == 0, "local lifecycle readiness creates no destination request");
            foreach (var mode in new[] { "assigned", "task-fields", "numeric-response-quantity", "fractional-response-quantity", "invalid-measurement", "numeric-wave", "unpack", "reject", "malformed", "wrong-id", "wrong-index", "wrong-command", "http-error", "redirect", "duplicate", "missing-chute", "blank-chute", "measured" })
            {
                peer.Mode = mode; var postCount = NonHeartbeatRequests(peer).Length; var original = Request(binding); var frozen = original with { IntegrationRequest = service.PrepareRequest(original, 7) };
                var reply = await service.RequestDestinationAsync(frozen, CancellationToken.None);
                if (mode == "assigned") await service.SendHeartbeatAsync(default);
                var actual = NonHeartbeatRequests(peer).Skip(postCount).Single();
                Check(actual.Body == frozen.IntegrationRequest.Body && Wire.Hash(actual.Body) == frozen.IntegrationRequest.BodyHash && actual.Method == "POST" && actual.ContentType.StartsWith("application/json", StringComparison.Ordinal), mode + " sends exact pre-frozen JSON bytes once");
                using var body = JsonDocument.Parse(actual.Body); var envelope = body.RootElement; var item = envelope.GetProperty("data").EnumerateArray().Single(); var p = item.GetProperty("params");
                Check(envelope.GetProperty("source").GetString() == "BAR" && envelope.GetProperty("version").GetInt32() == 1 && envelope.GetProperty("locale").GetString() == "zh_CN" &&
                    envelope.GetProperty("requestId").GetInt64() == 7 && item.GetProperty("index").GetInt32() == 1 && item.GetProperty("command").GetString() == "sorter.task.dest_request" &&
                    p.GetProperty("zoneId").GetInt32() == 71 && p.GetProperty("projectId").GetInt32() == 83 && p.GetProperty("loadCode").GetString() == "SYNTH-0001" &&
                    p.GetProperty("stationCode").GetString() == "SYNTH-STATION" && p.GetProperty("bcrCode").GetString() == "TEST-LEFT-READER" && !p.GetProperty("specifiedChute").GetBoolean() &&
                    p.GetProperty("chuteCode").ValueKind == JsonValueKind.Null && p.GetProperty("quantity").GetInt32() == 1, mode + " preserves documented field casing, numbers, booleans, null and configured mapping");
                var bytes = Convert.FromBase64String(reply.HttpResponse!.BodyBase64);
                Check(bytes.SequenceEqual(Encoding.UTF8.GetBytes(actual.Response)) && reply.HttpResponse.BodyHash == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) && reply.HttpResponse.StatusCode == actual.StatusCode,
                    mode + " preserves original HTTP status and exact response bytes/hash");
                Check(reply.RequestId == frozen.Id && reply.PayloadHash == frozen.PayloadHash && reply.PhysicalCompleted != true, mode + " retains original local correlation without physical completion");
                switch (mode)
                {
                    case "task-fields":
                        Check(reply.BusinessAccepted == true && reply.Destination is { WaveNo: "0007-WAVE", Quantity: "0002" }, "optional response wave/quantity preserve strings including leading zeros independently of request quantity");
                        Check(Wire.Decode<StageReply>(Wire.Encode(reply)).Destination == reply.Destination, "new task fields survive persisted StageReply round trip");
                        break;
                    case "numeric-response-quantity":
                        Check(reply.BusinessAccepted == true && reply.Slot == "SYNTH-CHUTE" && reply.Destination?.Quantity == "2",
                            "numeric returned quantity is accepted and shown independently of request quantity");
                        break;
                    case "measured": Check(reply.BusinessAccepted == true && reply.Destination is { Length: 12.34m, Width: 5.6m, Height: 7m, Weight: 0.89m } && reply.Destination.ArgsJson == "{\"synthetic\":[1,\"alpha\"]}", "non-null args and positive measurements preserve exact values without inferred units/conversion"); break;
                    case "assigned":
                        Check(reply.BusinessAccepted == true && reply.Slot == "SYNTH-CHUTE" && reply.Destination is { Outcome: WcsDestinationOutcome.Assigned, TaskId: "SYNTH-TASK", SkuId: "SYNTH-SKU", Length: null, Width: null, Height: null, Weight: null, WaveNo: null, Quantity: null }, "older assigned reply preserves task/SKU and leaves added response fields absent");
                        var legacy = Wire.Encode(new { reply.Destination!.Outcome, reply.Destination.Code, reply.Destination.Message, reply.Destination.ArgsJson, reply.Destination.TaskId, reply.Destination.SkuId, reply.Destination.Length, reply.Destination.Width, reply.Destination.Height, reply.Destination.Weight, reply.Destination.Unpack });
                        Check(Wire.Encode(reply.Destination) == legacy && Wire.Encode(Wire.Decode<WcsDestinationResult>(legacy)) == legacy, "old destination bytes and identity hash are unchanged when new fields are absent");
                        break;
                    case "unpack": Check(reply.BusinessAccepted != true && reply.Destination is { Outcome: WcsDestinationOutcome.UnpackRequired, Code: "NEED_UNPACK", Unpack: { Barcode: "SYNTH-0001", SkuId: "SYNTH-SKU", ScanQty: 1 } } && reply.Destination.Unpack.Chutes.Single() == new WcsUnpackChute("SYNTH-CHUTE", "SYNTH-ORDER", 0, "ASSIGNED"), "NEED_UNPACK preserves all typed split-package details"); break;
                    case "reject": Check(reply.BusinessAccepted == false && reply.Destination is { Outcome: WcsDestinationOutcome.Rejected, Code: "SYNTH_OTHER_ERROR", Message: "SYNTH_OTHER_ERROR" }, "other explicit errors remain business rejections with original codes/messages"); break;
                    default: Check(reply.BusinessAccepted is null && reply.ProtocolAcknowledged != true && reply.Slot is null, mode + " remains unknown and cannot manufacture acceptance"); break;
                }
            }
            peer.Mode = "oversize";
            var oversized = Request(binding); oversized = oversized with { IntegrationRequest = service.PrepareRequest(oversized, 9) };
            var oversizedReply = await service.RequestDestinationAsync(oversized, CancellationToken.None);
            Check(oversizedReply.BusinessAccepted is null && oversizedReply.HttpResponse is { Complete: false } && Convert.FromBase64String(oversizedReply.HttpResponse.BodyBase64).Length == SportmasterRcsTaskService.MaximumResponseBytes + 1, "oversized response retains bounded prefix with Complete=false and unknown outcome");
            await PreparationGuards(service, binding, peer);
            var right = Request(binding, "right"); var rightWire = service.PrepareRequest(right, 8);
            Check(rightWire.RequestId != service.PrepareRequest(Request(binding), 7).RequestId && rightWire.Body.Contains("TEST-RIGHT-READER", StringComparison.Ordinal), "durable entity sequence allocates distinct numeric request IDs and right mapping");
            var before = NonHeartbeatRequests(peer).Length; Check(await service.QueryAsync(right, CancellationToken.None) is null && NonHeartbeatRequests(peer).Length == before, "unsupported original query never resubmits a destination request");
        }
        await ConfirmedScanFlow(workspace, root, peer);
        await RuntimeChecks(workspace, root, peer);
        MigrationChecks(workspace, root);
        await NumericIdentityHistoryChecks(workspace, root, peer);
        Console.WriteLine("PASS RCS20 COMPLETE: synthetic HTTP protocol, exact custody, independent positions, unpack guards, restart unknown and current custody transitions");
    }
    static async Task ConfirmedScanFlow(string workspace, string root, Peer peer)
    {
        peer.Mode = "assigned";
        var current = Binding(peer.Endpoint);
        await using (var service = new SportmasterRcsTaskService(current))
        {
            service.ConfigureRuntime(HeartbeatConfiguration(workspace, current)); await service.StartAsync(default);
            var request = Request(current); request = request with { IntegrationRequest = service.PrepareRequest(request, 1) };
            using var body = JsonDocument.Parse(request.IntegrationRequest.Body);
            Check(body.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("quantity").GetInt32() == 1 &&
                (await service.RequestDestinationAsync(request, default)).BusinessAccepted == true, "current parcel request sends exactly quantity one");
            var oldBody = request.IntegrationRequest.Body.Replace("\"quantity\":1", "\"quantity\":7", StringComparison.Ordinal);
            var old = request with { IntegrationRequest = request.IntegrationRequest with { Body = oldBody, BodyHash = Wire.Hash(oldBody) } };
            var frozen = Wire.Encode(old); var count = NonHeartbeatRequests(peer).Length;
            try { await service.RequestDestinationAsync(old, default); throw new Exception("Old quantity intent delivered"); }
            catch (InvalidDataException) { Check(Wire.Encode(old) == frozen && NonHeartbeatRequests(peer).Length == count, "unsupported quantity preserves frozen original and produces no destination I/O"); }
        }
        try { await using var unsupported = new SportmasterRcsTaskService(current with { Parameters = new(current.Parameters) { ["quantity"] = "7" } }); throw new Exception("Old quantity option accepted"); }
        catch (InvalidDataException) { Check(true, "obsolete quantity configuration explicitly rejected"); }
        // TCP framing is verified by DualCameraCommunication.Protocols. Current
        // SM camera submissions must carry a real acquisition/arrival boundary.
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "confirmed-camera-flow"), rcsEndpoint: peer.Endpoint, heartbeatSeconds: 3600);
        await h.StartAsync();
        var app = h.App;
        uint frame = 0;
        async Task Read(string code)
        {
            var number = ++frame;
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode(code), number);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[h.Left].Timings.Snapshot().RecentFrames.Any(f => f.Number == number && f.Delivered)));
        }
        var before = NonHeartbeatRequests(peer).Length;
        foreach (var barcode in new[] { "000123", "000123", "000456" })
        {
            var count = (await app.SnapshotAsync()).Operations.Length;
            if (count != 0) await SmRoundFixture.NextVehicleAsync(app, "left");
            await h.TrayAsync(h.Left, TrayPresence.Present);
            await Read(barcode);
            await Wait(app, s => s.Operations.Length == count + 1 && s.Operations.All(o => o.State == ParcelState.SlotResolved));
        }
        var snapshot = await app.SnapshotAsync(); var posts = NonHeartbeatRequests(peer).Skip(before).ToArray();
        var payloads = posts.Select(post =>
        {
            using var json = JsonDocument.Parse(post.Body); var values = json.RootElement.GetProperty("data")[0].GetProperty("params");
            return (Code: values.GetProperty("loadCode").GetString(), Quantity: values.GetProperty("quantity").GetInt32());
        }).ToArray();
        Check(snapshot.Operations.Length == 3 && snapshot.Operations.Select(o => o.AdmissionId).Distinct().Count() == 3 &&
            snapshot.Operations.Select(o => o.ArrivalGeneration).Distinct().Count() == 3 && posts.Length == 3 &&
            payloads.Select(p => p.Code).SequenceEqual(new[] { "000123", "000123", "000456" }) && payloads.All(p => p.Quantity == 1) &&
            snapshot.Operations.All(o => o.Wcs is { Attempts: 1 } wcs && posts.Count(p => p.Body == wcs.Request.IntegrationRequest!.Body) == 1),
            "three trusted camera arrivals create distinct original requests once with exact frozen bytes, original barcodes and quantity one");
        var original = snapshot.Operations.Single(o => o.ParcelCode == "000456");
        var originalStage = Wire.Encode(original.Wcs); var resting = NonHeartbeatRequests(peer).Length;
        for (var i = 0; i < 5; i++) await Read("000456");
        await h.TrayAsync(h.Left, TrayPresence.Absent);
        await Task.Delay(600);
        await h.TrayAsync(h.Left, TrayPresence.Present);
        await Read("000456");
        snapshot = await app.SnapshotAsync();
        Check(NonHeartbeatRequests(peer).Length == resting && snapshot.Operations.Length == 3 &&
            Wire.Encode((await app.OperationAsync(original.Id))!.Wcs) == originalStage,
            "continuous reads and lifting/replacing on the same vehicle retain the original request without another send");
        await SmRoundFixture.NextVehicleAsync(app, "left");
        await h.TrayAsync(h.Left, TrayPresence.Present);
        await Read("000456");
        snapshot = await Wait(app, s => s.Operations.Length == 4 && s.Operations.All(o => o.State == ParcelState.SlotResolved));
        Check(NonHeartbeatRequests(peer).Length == resting + 1 && snapshot.Operations.Select(o => o.ArrivalGeneration).Distinct().Count() == 4,
            "only a confirmed new vehicle allows the same barcode to request a new chute once");
    }
    static async Task NumericIdentityHistoryChecks(string workspace, string root, Peer peer)
    {
        var name = "rcs-numeric-history"; var configPath = Path.Combine(root, name, "config"); Directory.CreateDirectory(configPath);
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json"))) with { Wcs = Binding(peer.Endpoint), TimeoutMs = 1500 };
        ExtensionStore.AtomicWrite(Path.Combine(configPath, "business.json"), c);
        var data = Path.Combine(root, name, "data"); var directory = Path.Combine(data, "Simulation", c.StationId, c.ApplicationId);
        async Task<ParcelRuntime> Open(string dataRoot, bool waitForArrival = true)
        {
            var launch = new BusinessLaunch(configPath, dataRoot, null, null); var app = new ParcelRuntime(launch, new SportmasterFlow(), new Factory(new ParcelCapabilityFactory(launch)));
            try
            {
                await app.StartAsync(default); SmRoundFixture.Register(app, launch, c); await ManagementModeFixture.Authorize(app);
                if (waitForArrival) await Wait(app, s => s.Positions!.All(p => p.Arrival?.CameraBoundaryReady == true));
                return app;
            }
            catch { await app.DisposeAsync(); throw; }
        }
        async Task<BusinessReceipt> Command(ParcelRuntime app, string kind, string? owner = null, string? value = null)
        { var s = await app.SnapshotAsync(); return await app.CommandAsync(new(Wire.Id(), s.Revision, kind, owner, value)); }
        async Task Maintenance(ParcelRuntime app)
        { await Command(app, "Pause"); Check((await ManagementModeFixture.Authorize(app)).Status == "Authorized", "numeric-ID fixture explicitly authenticates maintenance"); await Wait(app, s => s.Availability!.Configure); }
        peer.Mode = "assigned"; string original; long allocated; var backup = Path.Combine(root, "rcs-numeric-history.backup.db");
        await using (var app = await Open(data))
        {
            app.EmitReferenceAt("right", "numeric-original", "SYNTH-0001", Wire.Id()); var s = await Wait(app, x => x.Operations.SingleOrDefault()?.State == ParcelState.SlotResolved);
            original = s.Operations.Single().Id; allocated = s.Operations.Single().Wcs!.Request.IntegrationRequest!.RequestId;
            await Maintenance(app); s = await app.SnapshotAsync(); var low = s.Configuration.Effective with { Wcs = s.Configuration.Effective.Wcs! with { Parameters = new(s.Configuration.Effective.Wcs!.Parameters) { ["request-id-base"] = "1" } } };
            Check((await app.StageConfigurationAsync(s.Configuration.Revision, low, "Synthetic obsolete numeric range, no remote action allowed")).Status == "Unchanged", "retired numeric fields are removed before candidate comparison without changing effective configuration");
            // Leaving configuration maintenance schedules a real heartbeat. Drain
            // that refresh before taking the revision for the archive command.
            Check(await app.CheckRcsHeartbeatAsync(default) == RcsHeartbeatResult.Received, "numeric history waits for the configuration heartbeat to complete");
            await Wait(app, x => x.Availability!.Configure);
            var archive = await Command(app, "ArchiveHistory", value: "Synthetic numeric history must retain allocation identity");
            Check(archive.Status == "Persisted", "numeric archive command is durably accepted: " + Wire.Encode(archive));
            using (var repo = new ParcelRepository(directory, c.ApplicationId)) Check(repo.History(long.MaxValue, 20).Items.Any(i => i.Operation.Id == original && i.Archived), "numeric allocation authority survives real transaction history archival");
            Check((await app.BackupAsync(backup)).Status == "BackupCreated", "numeric history uses the actual complete backup path");
        }
        var posts = NonHeartbeatRequests(peer).Length;
        var restored = Path.Combine(root, "rcs-numeric-restored"); ParcelRecovery.Restore(backup, Path.Combine(restored, "Simulation", c.StationId, c.ApplicationId));
        await using (var app = await Open(restored, waitForArrival: false))
        {
            var resume = await Command(app, "Resume"); var s = await app.SnapshotAsync();
            Check(resume.Status == "Rejected" && s.Paused && s.BlockingReasons.Any(r => r.StartsWith("RestoreHandoffRequired", StringComparison.Ordinal)) &&
                NonHeartbeatRequests(peer).Length == posts,
                "restored numeric history remains isolated before handoff and cannot resume or send a destination request");
            using var repository = new ParcelRepository(Path.Combine(restored, "Simulation", c.StationId, c.ApplicationId), c.ApplicationId);
            Check(repository.NextAutomaticIntegrationRequestId() > allocated && repository.History(long.MaxValue, 20).Items.Any(i => i.Operation.Id == original && i.Archived && i.Operation.Wcs!.Request.IntegrationRequest!.RequestId == allocated),
                "automatic allocation advances after restore while archived original wire IDs remain fixed");

        }
        using (var retired = new ParcelRepository(Path.Combine(restored, "Simulation", c.StationId, c.ApplicationId), c.ApplicationId))
        {
            var before = retired.IntegrationBudget(new(1, 900000000));
            retired.ExpireHistoryCore(new Ipc.Application.Sportmaster.SportmasterFlow(), 7, DateTimeOffset.UtcNow.AddDays(8), Wire.FileHash(backup), () => { });
            Check(retired.ReadOperation(original) is null && retired.IntegrationBudget(new(1, 900000000)).PermanentHighWater == before.PermanentHighWater &&
                retired.RetiredIdentity("numeric-original", "") is not null, "expiry removes settled RCS detail while retaining permanent numeric ownership, restored retirement floor and entity fence");
        }
    }
    static async Task PreparationGuards(SportmasterRcsTaskService service, CapabilityBinding binding, Peer peer)
    {
        var signedStyle = binding with { Implementation = "extension:sportmaster.rcs-task/sportmaster-rcs-task" };
        Check(service.GetRequestIdRange(signedStyle) is null && service.UsesAutomaticRequestIds, "SM preparation uses automatic persisted IDs without a configured range");
        var count = NonHeartbeatRequests(peer).Length; var original = Request(binding); var frozen = original with { IntegrationRequest = service.PrepareRequest(original, 11) };
        var alteredBody = frozen.IntegrationRequest!.Body + " ";
        try { await service.RequestDestinationAsync(frozen with { IntegrationRequest = frozen.IntegrationRequest with { Body = alteredBody, BodyHash = Wire.Hash(alteredBody) } }, CancellationToken.None); throw new Exception("Changed wire body accepted"); }
        catch (InvalidDataException) { Check(NonHeartbeatRequests(peer).Length == count, "tampered wire body fails before destination HTTP even when attacker recomputes its body hash"); }
        foreach (var sequence in new long[] { 0, -1 })
        {
            try { service.PrepareRequest(original, sequence); throw new Exception("Out-of-range sequence accepted"); }
            catch (InvalidDataException) { Check(NonHeartbeatRequests(peer).Length == count, "request-ID range rejects invalid durable sequence " + sequence + " without an external destination call"); }
        }
        try { service.PrepareRequest(Request(binding, "unknown"), 1); throw new Exception("Unknown position accepted"); }
        catch (InvalidDataException) { Check(NonHeartbeatRequests(peer).Length == count, "unknown position cannot guess a configured reader mapping"); }
        var invalid = new[]
        {
            binding with { Target = "http://127.0.0.1/synthetic-other-api" },
            binding with { Contract = "UNKNOWN-CONTRACT" },
            binding with { Parameters = new(binding.Parameters) { ["unexpected"] = "true" } },
            binding with { Parameters = new(binding.Parameters) { ["zone-id"] = "0" } },
            binding with { Parameters = new(binding.Parameters) { ["project-id"] = "invalid" } }
        };
        foreach (var value in invalid)
        {
            try { await using var rejected = new SportmasterRcsTaskService(value); throw new Exception("Invalid RCS binding accepted"); }
            catch (InvalidDataException) { Check(NonHeartbeatRequests(peer).Length == count, "invalid endpoint/contract/mapping configuration rejects before an external destination call"); }
        }
        var missingCameraCode = binding with { Parameters = new(binding.Parameters.Where(p => p.Key != "left-bcr-code")) };
        await using (var missingService = new SportmasterRcsTaskService(missingCameraCode))
        {
            try { missingService.PrepareRequest(Request(missingCameraCode), 1); throw new Exception("Missing camera code sent"); }
            catch (InvalidDataException) { Check(NonHeartbeatRequests(peer).Length == count, "missing active camera mapping refuses preparation before destination HTTP"); }
        }
        var specified = binding with { Parameters = new(binding.Parameters) { ["specified-chute"] = "true", ["chute-code"] = "SYNTH-EXPLICIT" } };
        await using var explicitService = new SportmasterRcsTaskService(specified); var explicitRequest = Request(specified);
        using var parsed = JsonDocument.Parse(explicitService.PrepareRequest(explicitRequest, 1).Body); var parameters = parsed.RootElement.GetProperty("data")[0].GetProperty("params");
        Check(!parameters.GetProperty("specifiedChute").GetBoolean() && parameters.GetProperty("chuteCode").ValueKind == JsonValueKind.Null, "retired fixed-chute settings cannot affect new ordinary destination requests");
    }
    static async Task RuntimeChecks(string workspace, string root, Peer peer)
    {
        var template = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        var rcs = Binding(peer.Endpoint);
        var config = template with { Wcs = rcs with { Parameters = new(rcs.Parameters) { ["request-timeout-ms"] = "1000" } }, TimeoutMs = 1500 };
        async Task<ParcelRuntime> Open(string run)
        {
            var directory = Path.Combine(root, run, "config"); Directory.CreateDirectory(directory); ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), config);
            var launch = new BusinessLaunch(directory, Path.Combine(root, run, "data"), null, null);
            var app = new ParcelRuntime(launch, new SportmasterFlow(), new Factory(new ParcelCapabilityFactory(launch)));
            var responseMode = peer.Mode; peer.Mode = "assigned";
            try
            {
                await app.StartAsync(CancellationToken.None); SmRoundFixture.Register(app, launch, config); await ManagementModeFixture.Authorize(app);
                await Wait(app, s => s.Positions!.All(p => p.Arrival?.CameraBoundaryReady == true));
                peer.Mode = responseMode; return app;
            }
            catch { peer.Mode = responseMode; await app.DisposeAsync(); throw; }
        }
        peer.Mode = "by-position";
        await using (var app = await Open("rcs-dual"))
        {
            app.EmitReferenceAt("left", "synthetic-left", "SYNTH-0001", Wire.Id()); app.EmitReferenceAt("right", "synthetic-right", "SYNTH-0001", Wire.Id());
            var s = await Wait(app, x => x.Operations.Length == 2 && x.Operations.Any(o => o.State == ParcelState.UnpackRequired) && x.Operations.Any(o => o.State == ParcelState.SlotResolved));
            var left = s.Operations.Single(o => o.PositionId == "left"); var right = s.Operations.Single(o => o.PositionId == "right");
            await SportmasterRcsPresentation.Verify(app, right, left);
            Check(left.State == ParcelState.UnpackRequired && right.State == ParcelState.SlotResolved && left.Wcs!.Attempts == 1 && right.Wcs!.Attempts == 1 &&
                left.Wcs.Request.IntegrationRequest!.RequestId != right.Wcs.Request.IntegrationRequest!.RequestId, "same barcode on independent positions has distinct frozen numeric IDs and separate outcomes");
            Check((await app.CommandAsync(new(Wire.Id(), s.Revision, "AbandonUnsubmitted", left.Id, "Synthetic operator inspection"))).Status == "Rejected", "unpack position cannot be released while running");
            var originalStage = left.Wcs!;
            var originalWire = originalStage.Request.IntegrationRequest!;
            var sent = NonHeartbeatRequests(peer).Length;
            peer.Mode = "assigned";
            await SmRoundFixture.NextVehicleAsync(app, "left");
            Check(app.EmitReferenceAt("left", "synthetic-left-followup", "SYNTH-0002", Wire.Id()), "new observation enters the current left reader session");
            var changed = await Wait(app, x => x.Operations.Any(o => o.AdmissionId == "synthetic-left-followup" && o.State == ParcelState.SlotResolved));
            var next = changed.Operations.Single(o => o.AdmissionId == "synthetic-left-followup");
            var retained = await app.OperationAsync(left.Id);
            Check(changed.Positions!.Single(p => p.Id == "left").CurrentOperation == next.Id &&
                next.ParcelCode == "SYNTH-0002" && next.Slot == "SYNTH-CHUTE" &&
                next.Wcs is { State: StageState.Accepted, Attempts: 1, Request.IntegrationRequest: { } nextWire } &&
                nextWire.RequestId != originalWire.RequestId &&
                NonHeartbeatRequests(peer).Length == sent + 1 && peer.Requests.Count(post => post.Body == nextWire.Body) == 1,
                "confirmed new vehicle admits a new placement with its own chute and one request");
            Check(retained is { State: ParcelState.UnpackRequired, Slot: null } &&
                Wire.Encode(retained.Wcs) == Wire.Encode(originalStage) &&
                peer.Requests.Count(post => post.Body == originalWire.Body) == 1,
                "confirmed new vehicle preserves the original unpack result, request and raw reply without replay");
            s = await app.SnapshotAsync(); await app.CommandAsync(new(Wire.Id(), s.Revision, "Pause")); s = await app.SnapshotAsync();
            Check((await app.CommandAsync(new(Wire.Id(), s.Revision, "AbandonUnsubmitted", left.Id))).Status == "Rejected", "paused unpack release still requires explicit entity inspection reason");
            s = await app.SnapshotAsync(); Check((await app.CommandAsync(new(Wire.Id(), s.Revision, "AbandonUnsubmitted", left.Id, "Synthetic entity inspected and removed"))).Status == "Persisted", "paused explicit inspection releases unpack without retransmission or physical completion");
            var settled = (await app.SnapshotAsync()).Operations.Single(o => o.Id == left.Id);
            Check(settled.State == ParcelState.Abandoned && settled.Wcs!.Attempts == 1 && settled.Wcs.Replies.Single().Destination!.Outcome == WcsDestinationOutcome.UnpackRequired, "local unpack disposition preserves original reply, request and one attempt");
        }
        VerifyActualStorage(Path.Combine(root, "rcs-dual", "data", "Simulation", config.StationId, "sportmaster"));
        peer.Mode = "timeout"; ParcelOperation original; var before = NonHeartbeatRequests(peer).Length;
        await using (var app = await Open("rcs-unknown"))
        {
            app.EmitReferenceAt("left", "synthetic-unknown", "SYNTH-0001", Wire.Id()); var s = await Wait(app, x => x.Operations.FirstOrDefault()?.State == ParcelState.WcsUnknown); original = s.Operations.Single();
            Check(original.Wcs is { Attempts: 1, Request.IntegrationRequest: not null }, "HTTP timeout leaves one durable original wire intent in unknown state");
        }
        peer.Mode = "assigned";
        await using (var app = await Open("rcs-unknown"))
        {
            var recovered = (await app.SnapshotAsync()).Operations.Single();
            Check(recovered.State == ParcelState.WcsUnknown && Wire.Encode(recovered.Wcs!.Request) == Wire.Encode(original.Wcs!.Request), "restart preserves exact numeric ID, body, target, contract and all original bindings");
            await app.ReconcileAsync(recovered.Id); await app.ReconnectAsync(); await Task.Delay(100);
            var current = (await app.SnapshotAsync()).Operations.Single();
            Check(current.State == ParcelState.WcsUnknown && current.Wcs!.Attempts == 1 && NonHeartbeatRequests(peer).Length == before + 1, "restart, unsupported reconciliation and reconnect cannot resend an unknown HTTP request");
        }
        peer.Mode = "malformed";
        await using (var app = await Open("rcs-raw"))
        {
            app.EmitReferenceAt("left", "synthetic-malformed", "SYNTH-0001", Wire.Id()); var s = await Wait(app, x => x.Operations.FirstOrDefault()?.State == ParcelState.WcsUnknown);
            Check(s.Operations.Single().Wcs!.Replies.Single().HttpResponse is { BodyBase64: not null }, "malformed remote bytes are journaled in the actual application stage reply");
        }
        await using (var app = await Open("rcs-raw"))
        {
            var reply = (await app.SnapshotAsync()).Operations.Single().Wcs!.Replies.Single();
            Check(Encoding.UTF8.GetString(Convert.FromBase64String(reply.HttpResponse!.BodyBase64)) == "{not-json", "raw malformed reply bytes survive application restart without reparsing into success");
        }
        CapabilityBinding Camera(string position) => new("camera", "reference-camera", "1.0.0", "rcs.camera." + position, config.Topology!.Readers.Single(x => x.DeviceId.EndsWith("." + position, StringComparison.Ordinal)).DeviceId, "PARCEL-REFERENCE-v1", []);
        var leftCamera = Camera("left"); var rightCamera = Camera("right");
        var uploader = new CapabilityBinding("upload", "reference-upload", "1.0.0", "rcs.upload", "reference://upload/rcs-attached", "PARCEL-REFERENCE-v1", []);
        config = config with
        {
            Images = new(leftCamera, uploader, Strategy: UploadStrategy.Attached),
            CapabilityInstances = [config.Wcs!, leftCamera, rightCamera, uploader],
            Topology = config.Topology! with { Positions = [config.Topology.Positions[0], config.Topology.Positions[1] with { CameraInstanceId = rightCamera.InstanceId }] }
        };
        peer.Mode = "unpack"; ParcelOperation unpackOriginal;
        await using (var app = await Open("rcs-unpack-projection-recovery"))
        {
            app.EmitReferenceAt("left", "synthetic-unpack-attached", "SYNTH-0001", Wire.Id());
            unpackOriginal = (await Wait(app, x => x.Operations.SingleOrDefault() is { State: ParcelState.UnpackRequired, Upload.State: StageState.Accepted })).Operations.Single();
        }
        var projectionDirectory = Path.Combine(root, "rcs-unpack-projection-recovery", "data", "Simulation", config.StationId, "sportmaster");
        using (var db = new SqliteConnection("Data Source=" + Path.Combine(projectionDirectory, "sportmaster.db")))
        {
            db.Open(); using var command = db.CreateCommand(); command.CommandText = "UPDATE parcel_operations SET body=json_set(body,'$.State','Blocked','$.Reason','Synthetic prior attached receipt projection') WHERE id=$id"; command.Parameters.AddWithValue("$id", unpackOriginal.Id); Check(command.ExecuteNonQuery() == 1, "synthetic prior attached projection changes only the persisted overall state/reason");
        }
        var postsBeforeRecovery = NonHeartbeatRequests(peer).Length;
        await using (var app = await Open("rcs-unpack-projection-recovery"))
        {
            var recovered = (await app.SnapshotAsync()).Operations.Single();
            Check(recovered.State == ParcelState.UnpackRequired && Wire.Encode(recovered.Wcs) == Wire.Encode(unpackOriginal.Wcs) && NonHeartbeatRequests(peer).Length == postsBeforeRecovery &&
                (await app.SnapshotAsync()).Positions!.Single(x => x.Id == "left").Admit == false,
                "main receipt projection recovery restores NEED_UNPACK as a held position with exact original WCS request/raw instead of flattening it to generic rejection");
        }
    }
    static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> ready)
    {
        for (var i = 0; i < 200; i++) { var s = await app.SnapshotAsync(); if (ready(s)) return s; await Task.Delay(20); }
        throw new Exception("RCS application timeout: " + Wire.Encode(await app.SnapshotAsync()));
    }
    static void VerifyActualStorage(string directory)
    {
        var file = Path.Combine(directory, "sportmaster.db");
        using (var db = new SqliteConnection("Data Source=" + file))
        {
            db.Open(); using var command = db.CreateCommand(); command.CommandText = "SELECT max(version) FROM platform_migrations";
            Check((long)command.ExecuteScalar()! == ParcelRepository.CurrentStorageVersion, "actual ParcelRuntime startup retains numeric ownership with the current storage format");
        }

    }
    static void MigrationChecks(string workspace, string root)
    {
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        var request = Request(config.Wcs!);
        var reply = new StageReply(Wire.Id(), request.Id, request.PayloadHash, true, null, null, null, null, "Synthetic original unknown", DateTimeOffset.UtcNow);
        var stage = new StageFact(request, StageState.Unknown, 1, [reply], "Original outcome unknown");
        var operation = new ParcelOperation(request.OperationId, request.AdmissionId, "SYNTH-0001", Wire.Id(), request.PreparedAt, request.ConfigurationHash, config,
            new() { [config.Wcs!.InstanceId] = request.ImplementationHash }, config.Weight, null, ParcelState.WcsUnknown, null, stage, null, "Original awaiting reconciliation", PositionId: "left");
        var current = Path.Combine(root, "current-custody"); string durable, awaiting;
        using (var repo = new ParcelRepository(current, "sportmaster"))
        {
            repo.Commit(operation, "SyntheticOriginal", "Current frozen unknown");
            durable = repo.BeginReceipt(operation, stage, "Submit"); repo.StoreRaw(durable, reply);
            awaiting = repo.BeginReceipt(operation, stage, "Query");
        }
        var raw = File.ReadAllBytes(Path.Combine(current, "receipts", durable + ".json"));
        using (var reopened = new ParcelRepository(current, "sportmaster"))
            Check(reopened.StorageVersion == ParcelRepository.CurrentStorageVersion && Wire.Encode(reopened.Load().Single()) == Wire.Encode(operation) &&
                reopened.PendingReceipts().Length == 2 && reopened.ReceiptById(durable) is { State: "RawDurable" } &&
                reopened.ReceiptById(awaiting) is { State: "Awaiting", RawHash: null } && File.ReadAllBytes(Path.Combine(current, "receipts", durable + ".json")).SequenceEqual(raw),
                "current format retains frozen Unknown, independent custody, raw bytes and one send after restart");
        foreach (var version in Enumerable.Range(1, ParcelRepository.CurrentStorageVersion - 1))
        {
            var directory = Path.Combine(root, "unsupported-format" + version);
            var file = NonCurrentStorageFixture.Create(directory, "sportmaster", version, operation);
            var original = Wire.FileHash(file); var receipt = Path.Combine(directory, "original-receipt.json"); File.WriteAllBytes(receipt, raw);
            try { using var denied = new ParcelRepository(directory, "sportmaster"); throw new Exception("Historical SQL accepted"); }
            catch (InvalidDataException)
            {
                Check(Wire.FileHash(file) == original && File.ReadAllBytes(receipt).SequenceEqual(raw) && NonCurrentStorageFixture.Version(file) == version && !Directory.Exists(Path.Combine(directory, "receipts")),
                "SQL" + version + " refuses before writes/receipt directories; original request and raw remain exact");
            }
        }
        foreach (var damage in new[] { "future", "foreign", "checksum" })
        {
            var directory = Path.Combine(root, "current-db-" + damage); Directory.CreateDirectory(directory); var file = Path.Combine(directory, "sportmaster.db");
            File.Copy(Path.Combine(current, "sportmaster.db"), file);
            using (var db = new SqliteConnection("Data Source=" + file + ";Pooling=False"))
            {
                db.Open(); using var command = db.CreateCommand(); command.CommandText = damage switch
                {
                    "future" => $"INSERT INTO platform_migrations VALUES('sportmaster',{ParcelRepository.CurrentStorageVersion + 1},'future')",
                    "foreign" => "UPDATE platform_migrations SET application='another-business'",
                    _ => $"UPDATE platform_migrations SET sha='changed' WHERE version={ParcelRepository.CurrentStorageVersion}"
                };
                if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Synthetic storage damage did not change the expected row");
            }
            var hash = Wire.FileHash(file);
            try { using var denied = new ParcelRepository(directory, "sportmaster"); throw new Exception("Damaged identity accepted"); }
            catch (InvalidDataException) { Check(Wire.FileHash(file) == hash, "future/foreign/changed migration identity is refused read-only: " + damage); }
        }
    }
    sealed class Factory(ParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        static ParcelConfiguration Reference(ParcelConfiguration c)
        {
            var reference = c.Wcs! with { Implementation = "reference-integration", Version = "1.0.0", Target = "reference://integration/synthetic-rcs-verification", Contract = "PARCEL-REFERENCE-v1", Parameters = new() };
            return c with { Wcs = reference, CapabilityInstances = c.CapabilityInstances?.Select(x => x.InstanceId == c.Wcs.InstanceId ? reference : x).ToArray() };
        }
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) { inner.Validate(inner.Normalize(Reference(c))); using var _ = new DisposableBindingCheck(c.Wcs!); }
        public CapabilityRequirements Requirements(CapabilityBinding b) => new(BusinessAcceptance: true, Query: false);
        public ParcelPorts Create(ParcelConfiguration c)
        {
            var p = inner.Create(inner.Normalize(Reference(c))); var service = new SportmasterRcsTaskService(c.Wcs!);
            service.ConfigureRuntime(c);
            var hashes = new Dictionary<string, string>(p.ImplementationHashes) { [c.Wcs!.InstanceId] = Wire.FileHash(typeof(SportmasterRcsTaskService).Assembly.Location) };
            // The unused reference integration never starts; dispose its owned resources.
            p.Wcs!.DisposeAsync().AsTask().GetAwaiter().GetResult(); return p with { Wcs = service, ImplementationHashes = hashes };
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class DisposableBindingCheck(CapabilityBinding binding) : IDisposable
    {
        readonly SportmasterRcsTaskService service = new(binding);
        public void Dispose() => service.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
    sealed record Exchange(string Method, string ContentType, string Body, int StatusCode, string Response);
    sealed class Peer : IAsyncDisposable
    {
        public readonly ConcurrentDictionary<string, string> Vehicles = new();
        readonly HttpListener listener = new(); readonly CancellationTokenSource stop = new(); readonly Task loop;
        readonly ConcurrentBag<Task> pending = [];
        public ConcurrentQueue<Exchange> Requests { get; } = new();
        public string Mode = "assigned";
        public Func<string, string>? ResponseTransform;
        public TaskCompletionSource? ResponseHold;
        public string Endpoint { get; }
        public Peer()
        {
            var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
            Endpoint = "http://127.0.0.1:" + port + "/rcs/sorter/api"; listener.Prefixes.Add("http://127.0.0.1:" + port + "/"); listener.Start(); SmRoundFixture.VehiclePeers[Endpoint] = (code, vehicle) => Vehicles[code] = vehicle; loop = Loop();
        }
        async Task Loop()
        {
            try { while (!stop.IsCancellationRequested) { var context = await listener.GetContextAsync().WaitAsync(stop.Token); pending.Add(Handle(context)); } }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (HttpListenerException) when (stop.IsCancellationRequested) { }
        }
        async Task Handle(HttpListenerContext context)
        {
            try { await HandleCore(context); }
            catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
            catch (HttpListenerException) when (stop.IsCancellationRequested) { }
        }
        async Task HandleCore(HttpListenerContext context)
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8); var body = await reader.ReadToEndAsync(); var mode = Mode;
            if (mode is "by-position" or "timeout-left")
            {
                using var json = JsonDocument.Parse(body); var data = json.RootElement.GetProperty("data");
                var leftDestination = data.GetArrayLength() == 1 && data[0].GetProperty("command").GetString() == RcsDestinationProtocol.Command &&
                    data[0].GetProperty("params").GetProperty("bcrCode").GetString() == "TEST-LEFT-READER";
                mode = leftDestination ? mode == "by-position" ? "unpack" : "timeout" : "assigned";
            }
            var response = Response(body, mode, Vehicles); response = ResponseTransform?.Invoke(response) ?? response;
            var status = mode == "http-error" ? 503 : mode == "redirect" ? 302 : 200;
            Requests.Enqueue(new(context.Request.HttpMethod, context.Request.ContentType ?? "", body, status, response));
            try
            {
                if (ResponseHold is { } hold) await hold.Task.WaitAsync(stop.Token);
                if (mode == "body-timeout")
                {
                    context.Response.SendChunked = true; context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("{\"requestId\":"), stop.Token);
                    await context.Response.OutputStream.FlushAsync(stop.Token);
                    await Task.Delay(2500, stop.Token); return;
                }
                if (mode == "timeout") await Task.Delay(3500, stop.Token);
                // Deterministic deadline fixture: no success can race the client
                // timeout. Dispose owns cancellation and drains this handler.
                if (mode == "withhold-response") await Task.Delay(Timeout.Infinite, stop.Token);
                if (mode == "delayed-assignment") await Task.Delay(5500, stop.Token);
                var bytes = Encoding.UTF8.GetBytes(response); context.Response.StatusCode = status; if (mode == "redirect") context.Response.RedirectLocation = Endpoint + "/synthetic-followed"; context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, stop.Token);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (HttpListenerException) { } // The timeout fixture intentionally closes the client.
            catch (IOException) { }
            finally
            {
                try { context.Response.Close(); }
                catch (HttpListenerException) { } // A timed-out client may already have closed the connection.
                catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
            }
        }
        public async ValueTask DisposeAsync()
        { SmRoundFixture.VehiclePeers.TryRemove(Endpoint, out _); stop.Cancel(); listener.Close(); await loop; await Task.WhenAll(pending); stop.Dispose(); }
    }
}
