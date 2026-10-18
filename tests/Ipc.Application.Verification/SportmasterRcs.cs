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
static class SportmasterRcs
{
    static void Check(bool value, string message)
    { if (!value) throw new Exception("FAIL RCS20: " + message); Console.WriteLine("PASS RCS20: " + message); }
    static CapabilityBinding Binding(string endpoint) => new("integration", "sportmaster-rcs-task", "1.0.0", "sportmaster.integration", endpoint, "SPORTMASTER-RCS-SORTER-v1", new()
    {
        ["source"] = "TEST_READER", ["locale"] = "zh_CN", ["zone-id"] = "71", ["project-id"] = "83", ["quantity"] = "1",
        ["specified-chute"] = "false", ["request-id-base"] = "810000000", ["request-id-count"] = "100000",
        ["left-station-code"] = "TEST-LEFT", ["left-bcr-code"] = "TEST-LEFT-READER", ["right-station-code"] = "TEST-RIGHT", ["right-bcr-code"] = "TEST-RIGHT-READER"
    });
    static FrozenRequest Request(CapabilityBinding binding, string position = "left", string barcode = "SYNTH-0001")
    {
        var reader = new DeviceBinding("parcel-reader", "manual", "synthetic.reader." + position, "1");
        var acquisition = new AcquisitionIdentity(reader.DeviceId, "synthetic-epoch", "synthetic-event", "synthetic-admission", DateTimeOffset.UtcNow);
        var payload = Wire.Encode(new ParcelPayload(barcode, "synthetic-admission", null, null, null, Acquisition: acquisition, PositionId: position, ReaderBinding: reader));
        return new(Wire.Id(), "sportmaster", Wire.Id(), "synthetic-admission", "wcs-slot", binding, Wire.Hash("synthetic-provider"), Wire.Hash("synthetic-configuration"), payload, Wire.Hash(payload), DateTimeOffset.UtcNow);
    }
    static string Response(string request, string mode)
    {
        using var j = JsonDocument.Parse(request); var id = j.RootElement.GetProperty("requestId").GetInt64();
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
            await service.StartAsync(CancellationToken.None);
            Check((await service.HealthAsync(CancellationToken.None)).Ready && peer.Requests.Count == 0, "lifecycle readiness sends no fabricated remote health request");
            foreach (var mode in new[] { "assigned", "unpack", "reject", "malformed", "wrong-id", "wrong-index", "wrong-command", "http-error", "redirect", "duplicate", "missing-chute", "blank-chute", "measured" })
            {
                peer.Mode = mode; var postCount = peer.Requests.Count; var original = Request(binding); var frozen = original with { IntegrationRequest = service.PrepareRequest(original, 7) };
                var reply = await service.RequestDestinationAsync(frozen, CancellationToken.None);
                var actual = peer.Requests.Last();
                Check(peer.Requests.Count == postCount + 1 && actual.Body == frozen.IntegrationRequest.Body && Wire.Hash(actual.Body) == frozen.IntegrationRequest.BodyHash && actual.Method == "POST" && actual.ContentType.StartsWith("application/json", StringComparison.Ordinal), mode + " sends exact pre-frozen JSON bytes once");
                using var body = JsonDocument.Parse(actual.Body); var envelope = body.RootElement; var item = envelope.GetProperty("data").EnumerateArray().Single(); var p = item.GetProperty("params");
                Check(envelope.GetProperty("source").GetString() == "TEST_READER" && envelope.GetProperty("version").GetInt32() == 1 && envelope.GetProperty("locale").GetString() == "zh_CN" &&
                    envelope.GetProperty("requestId").GetInt64() == 810000007 && item.GetProperty("index").GetInt32() == 1 && item.GetProperty("command").GetString() == "sorter.task.dest_request" &&
                    p.GetProperty("zoneId").GetInt32() == 71 && p.GetProperty("projectId").GetInt32() == 83 && p.GetProperty("loadCode").GetString() == "SYNTH-0001" &&
                    p.GetProperty("stationCode").GetString() == "TEST-LEFT" && p.GetProperty("bcrCode").GetString() == "TEST-LEFT-READER" && !p.GetProperty("specifiedChute").GetBoolean() &&
                    p.GetProperty("chuteCode").ValueKind == JsonValueKind.Null && p.GetProperty("quantity").GetInt32() == 1, mode + " preserves documented field casing, numbers, booleans, null and configured mapping");
                var bytes = Convert.FromBase64String(reply.HttpResponse!.BodyBase64);
                Check(bytes.SequenceEqual(Encoding.UTF8.GetBytes(actual.Response)) && reply.HttpResponse.BodyHash == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) && reply.HttpResponse.StatusCode == actual.StatusCode,
                    mode + " preserves original HTTP status and exact response bytes/hash");
                Check(reply.RequestId == frozen.Id && reply.PayloadHash == frozen.PayloadHash && reply.PhysicalCompleted != true, mode + " retains original local correlation without physical completion");
                switch (mode)
                {
                    case "measured": Check(reply.BusinessAccepted == true && reply.Destination is { Length: 12.34m, Width: 5.6m, Height: 7m, Weight: 0.89m } && reply.Destination.ArgsJson == "{\"synthetic\":[1,\"alpha\"]}", "non-null args and positive measurements preserve exact values without inferred units/conversion"); break;
                    case "assigned": Check(reply.BusinessAccepted == true && reply.Slot == "SYNTH-CHUTE" && reply.Destination is { Outcome: WcsDestinationOutcome.Assigned, TaskId: "SYNTH-TASK", SkuId: "SYNTH-SKU", Length: null, Width: null, Height: null, Weight: null }, "assigned reply preserves task/SKU and nullable measurements"); break;
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
            var before = peer.Requests.Count; Check(await service.QueryAsync(right, CancellationToken.None) is null && peer.Requests.Count == before, "unsupported original query sends no request and never resubmits");
        }
        await RuntimeChecks(workspace, root, peer);
        MigrationChecks(root);
        Console.WriteLine("PASS RCS20 COMPLETE: synthetic HTTP protocol, exact custody, independent positions, unpack guards, restart unknown and SQL5 barriers");
    }
    static async Task PreparationGuards(SportmasterRcsTaskService service, CapabilityBinding binding, Peer peer)
    {
        var count = peer.Requests.Count; var original = Request(binding); var frozen = original with { IntegrationRequest = service.PrepareRequest(original, 11) };
        var alteredBody = frozen.IntegrationRequest!.Body + " ";
        try { await service.RequestDestinationAsync(frozen with { IntegrationRequest = frozen.IntegrationRequest with { Body = alteredBody, BodyHash = Wire.Hash(alteredBody) } }, CancellationToken.None); throw new Exception("Changed wire body accepted"); }
        catch (InvalidDataException) { Check(peer.Requests.Count == count, "tampered wire body fails before HTTP even when attacker recomputes its body hash"); }
        foreach (var sequence in new long[] { 0, 100001 })
        {
            try { service.PrepareRequest(original, sequence); throw new Exception("Out-of-range sequence accepted"); }
            catch (InvalidDataException) { Check(peer.Requests.Count == count, "request-ID range rejects invalid durable sequence " + sequence + " without an external call"); }
        }
        try { service.PrepareRequest(Request(binding, "unknown"), 1); throw new Exception("Unknown position accepted"); }
        catch (InvalidDataException) { Check(peer.Requests.Count == count, "unknown position cannot guess a configured reader mapping"); }
        var invalid = new[]
        {
            binding with { Target = "http://127.0.0.1/synthetic-other-api" },
            binding with { Contract = "UNKNOWN-CONTRACT" },
            binding with { Parameters = new(binding.Parameters.Where(x => x.Key != "left-bcr-code")) },
            binding with { Parameters = new(binding.Parameters) { ["specified-chute"] = "true" } },
            binding with { Parameters = new(binding.Parameters) { ["request-id-base"] = long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture) } },
            binding with { Parameters = new(binding.Parameters) { ["request-id-count"] = "0" } }
        };
        foreach (var value in invalid)
        {
            try { await using var rejected = new SportmasterRcsTaskService(value); throw new Exception("Invalid RCS binding accepted"); }
            catch (InvalidDataException) { Check(peer.Requests.Count == count, "invalid endpoint/contract/mapping/chute/range configuration rejects before an external call"); }
        }
        var specified = binding with { Parameters = new(binding.Parameters) { ["specified-chute"] = "true", ["chute-code"] = "SYNTH-EXPLICIT" } };
        await using var explicitService = new SportmasterRcsTaskService(specified); var explicitRequest = Request(specified);
        using var parsed = JsonDocument.Parse(explicitService.PrepareRequest(explicitRequest, 1).Body); var parameters = parsed.RootElement.GetProperty("data")[0].GetProperty("params");
        Check(parameters.GetProperty("specifiedChute").GetBoolean() && parameters.GetProperty("chuteCode").GetString() == "SYNTH-EXPLICIT", "explicit configured chute preserves its documented boolean and code");
    }
    public static async Task SignedChecks(string workspace, string root, string packageFile, string trustFile)
    {
        await using var peer = new Peer();
        ParcelCapabilityFactory.RegisterContracts();
        var store = new ExtensionStore(Path.Combine(root, "rcs-signed-store"), Wire.Decode<ExtensionTrust>(File.ReadAllText(trustFile)), RunMode.Simulation, Path.Combine(root, "rcs-signed-leases"));
        var installed = store.Install(packageFile, Wire.FileHash(packageFile)); var verified = store.VerifyInstalled(installed.Package);
        var module = verified.Manifest.Modules.Single(x => x.Id == "sportmaster-rcs-task");
        Check(module.Kind == "integration" && module.ContractIdentity == SportmasterRcsTaskService.Contract && module.Requirements is { Query: false, BusinessAcceptance: true }, "signed package verifies exact integration contract and explicitly declares no original-query capability");
        var binding = Binding(peer.Endpoint) with { Implementation = "extension:" + installed.Package.Id + "/" + module.Id, Version = installed.Package.Version, Package = installed.Package, Module = module.Id };
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json"))) with { Wcs = binding, Extensions = new([installed.Package], []), TimeoutMs = 1500 };
        async Task<ParcelRuntime> Open(string run)
        {
            var directory = Path.Combine(root, run, "config"); Directory.CreateDirectory(directory);
            ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), config);
            ExtensionStore.AtomicWrite(Path.Combine(directory, "extension-store.json"), new ParcelExtensionStoreConfiguration(store.Root, trustFile));
            var launch = new BusinessLaunch(directory, Path.Combine(root, run, "data"), null, null);
            var factory = new ParcelCapabilityFactory(launch);
            Check(factory.Catalog().Providers.Single(x => x.Package == installed.Package && x.Module == module.Id).Requirements.Query == false, "installed exact signed module is discoverable with Query=false");
            var app = SportmasterApplication.Create(launch, factory);
            try { await app.StartAsync(CancellationToken.None); return app; } catch { await app.DisposeAsync(); throw; }
        }
        peer.Mode = "by-position";
        await using (var app = await Open("rcs-signed-dual"))
        {
            app.EmitReferenceAt("left", "signed-left", "SYNTH-0001", Wire.Id()); app.EmitReferenceAt("right", "signed-right", "SYNTH-0001", Wire.Id());
            var s = await Wait(app, x => x.Operations.Length == 2 && x.Operations.Any(o => o.State == ParcelState.UnpackRequired) && x.Operations.Any(o => o.State == ParcelState.SlotResolved));
            Check(s.Operations.All(o => o.Wcs is { Attempts: 1, Request.Binding.Package: not null, Request.IntegrationRequest: not null } && o.Wcs.Request.Binding.Package == installed.Package && o.Wcs.Replies.Single().HttpResponse is not null), "plain SportmasterApplication and ParcelCapabilityFactory load signed provider and execute both HTTP positions with durable original wire/response custody");
            var assigned = s.Operations.Single(o => o.State == ParcelState.SlotResolved);
            Check(assigned.Wcs!.Replies.Single().Destination is { TaskId: "SYNTH-TASK", SkuId: "SYNTH-SKU" } && s.Operations.Single(o => o.State == ParcelState.UnpackRequired).Wcs!.Replies.Single().Destination!.Unpack!.Chutes.Length == 1, "signed runtime exposes assigned task/SKU and original unpack details separately");
        }
        peer.Mode = "reject";
        await using (var app = await Open("rcs-signed-rejection"))
        {
            app.EmitReferenceAt("left", "signed-rejected", "SYNTH-0001", Wire.Id()); var s = await Wait(app, x => x.Operations.FirstOrDefault()?.State == ParcelState.WcsRejected);
            Check(s.Operations.Single().Wcs!.Replies.Single().Destination is { Outcome: WcsDestinationOutcome.Rejected, Code: "SYNTH_OTHER_ERROR" }, "actual signed extension path preserves ordinary RCS rejection");
        }
        peer.Mode = "timeout"; ParcelOperation original; var count = peer.Requests.Count;
        await using (var app = await Open("rcs-signed-unknown"))
        {
            app.EmitReferenceAt("left", "signed-unknown", "SYNTH-0001", Wire.Id()); var s = await Wait(app, x => x.Operations.FirstOrDefault()?.State == ParcelState.WcsUnknown); original = s.Operations.Single();
        }
        peer.Mode = "assigned";
        await using (var app = await Open("rcs-signed-unknown"))
        {
            var recovered = (await app.SnapshotAsync()).Operations.Single(); await app.ReconcileAsync(recovered.Id); await app.ReconnectAsync();
            var current = (await app.SnapshotAsync()).Operations.Single();
            Check(current.State == ParcelState.WcsUnknown && current.Wcs!.Attempts == 1 && Wire.Encode(current.Wcs.Request) == Wire.Encode(original.Wcs!.Request) && peer.Requests.Count == count + 1,
                "signed provider restart, reconcile and reconnect retain exact original HTTP intention without a repeated POST");
        }
        Console.WriteLine("PASS RCS20 SIGNED COMPLETE: verified installed extension, plain factory application, dual positions, unpack/rejection and restart no resend; Simulation fixture only");
    }
    static async Task RuntimeChecks(string workspace, string root, Peer peer)
    {
        var template = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        var config = template with { Wcs = Binding(peer.Endpoint), TimeoutMs = 1500 };
        async Task<ParcelRuntime> Open(string run)
        {
            var directory = Path.Combine(root, run, "config"); Directory.CreateDirectory(directory); ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), config);
            var launch = new BusinessLaunch(directory, Path.Combine(root, run, "data"), null, null);
            var app = new ParcelRuntime(launch, new SportmasterFlow(), new Factory(new ParcelCapabilityFactory(launch)));
            try { await app.StartAsync(CancellationToken.None); return app; } catch { await app.DisposeAsync(); throw; }
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
            app.EmitReferenceAt("left", "synthetic-left-followup", "SYNTH-0002", Wire.Id()); await Task.Delay(75);
            Check((await app.SnapshotAsync()).Operations.Length == 2, "unpack holds its original position against new entity admission");
            s = await app.SnapshotAsync(); await app.CommandAsync(new(Wire.Id(), s.Revision, "Pause")); s = await app.SnapshotAsync();
            Check((await app.CommandAsync(new(Wire.Id(), s.Revision, "AbandonUnsubmitted", left.Id))).Status == "Rejected", "paused unpack release still requires explicit entity inspection reason");
            s = await app.SnapshotAsync(); Check((await app.CommandAsync(new(Wire.Id(), s.Revision, "AbandonUnsubmitted", left.Id, "Synthetic entity inspected and removed"))).Status == "Persisted", "paused explicit inspection releases unpack without retransmission or physical completion");
            var settled = (await app.SnapshotAsync()).Operations.Single(o => o.Id == left.Id);
            Check(settled.State == ParcelState.Abandoned && settled.Wcs!.Attempts == 1 && settled.Wcs.Replies.Single().Destination!.Outcome == WcsDestinationOutcome.UnpackRequired, "local unpack disposition preserves original reply, request and one attempt");
        }
        VerifyActualStorage(Path.Combine(root, "rcs-dual", "data", "Simulation", config.StationId, "sportmaster"));
        peer.Mode = "timeout"; ParcelOperation original; var before = peer.Requests.Count;
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
            Check(current.State == ParcelState.WcsUnknown && current.Wcs!.Attempts == 1 && peer.Requests.Count == before + 1, "restart, unsupported reconciliation and reconnect cannot resend an unknown HTTP request");
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
        peer.Mode = "assigned"; config = config with { Wcs = config.Wcs! with { Parameters = new(config.Wcs.Parameters) { ["request-id-count"] = "1" } } };
        await using (var app = await Open("rcs-range-exhausted"))
        {
            app.EmitReferenceAt("left", "synthetic-range-first", "SYNTH-0001", Wire.Id()); await Wait(app, x => x.Operations.FirstOrDefault()?.State == ParcelState.SlotResolved);
            var count = peer.Requests.Count;
            app.EmitReferenceAt("right", "synthetic-range-second", "SYNTH-0001", Wire.Id());
            var s = await Wait(app, x => x.Operations.Any(o => o.State == ParcelState.Blocked)); var blocked = s.Operations.Single(o => o.State == ParcelState.Blocked);
            Check(blocked.Wcs is null && blocked.Reason?.Contains("IntegrationPreparationBlocked", StringComparison.Ordinal) == true && peer.Requests.Count == count && !s.BlockingReasons.Any(x => x.Contains("worker failed", StringComparison.OrdinalIgnoreCase)), "exhausted durable numeric-ID range blocks only the unsent entity without POST or global worker failure");
            Check((await app.CommandAsync(new(Wire.Id(), s.Revision, "Pause"))).Status == "Persisted", "range exhaustion leaves runtime available for software pause");
            s = await app.SnapshotAsync(); Check((await app.CommandAsync(new(Wire.Id(), s.Revision, "AbandonUnsubmitted", blocked.Id, "Synthetic entity inspected after exhausted namespace"))).Status == "Persisted", "range-exhausted unsent entity supports guarded inspection and disposition");
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
            Check((long)command.ExecuteScalar()! == 5, "actual ParcelRuntime startup records semantic SQL5");
        }
        try { using var oldReader = new ParcelRepository(directory, "sportmaster", 4); throw new Exception("Old reader accepted actual SQL5 runtime database"); }
        catch (InvalidDataException) { Check(true, "old SQL4 reader rejects actual application SQL5 database"); }
    }
    static void MigrationChecks(string root)
    {
        var directory = Path.Combine(root, "rcs-sql4"); using (var repo = new ParcelRepository(directory, "sportmaster", 4)) { }
        var file = Path.Combine(directory, "sportmaster.db");
        void Sql(string sql) { using var db = new SqliteConnection("Data Source=" + file); db.Open(); using var c = db.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
        long Version() { using var db = new SqliteConnection("Data Source=" + file); db.Open(); using var c = db.CreateCommand(); c.CommandText = "SELECT max(version) FROM platform_migrations"; return (long)c.ExecuteScalar()!; }
        Sql("INSERT INTO parcel_receipts VALUES('synthetic-pending','synthetic-operation','wcs-slot','original','Submit','Awaiting',NULL)");
        try { using var rejected = new ParcelRepository(directory, "sportmaster"); throw new Exception("SQL4 unsettled upgrade accepted"); }
        catch (InvalidDataException e) { Check(e.Message.Contains("LegacyUnsettled", StringComparison.Ordinal) && Version() == 4, "SQL4 unresolved receipt blocks SQL5 before original migration history changes"); }
        Sql("UPDATE parcel_receipts SET state='NoReply'"); using (var upgraded = new ParcelRepository(directory, "sportmaster")) { }
        Check(Version() == 5, "settled SQL4 appends semantic SQL5");
        try { using var rejected = new ParcelRepository(directory, "sportmaster", 4); throw new Exception("Old reader accepted SQL5"); }
        catch (InvalidDataException) { Check(Version() == 5, "old SQL4 reader rejects future SQL5 without modifying the original database"); }
    }
    sealed class Factory(ParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        static ParcelConfiguration Reference(ParcelConfiguration c) => c with { Wcs = c.Wcs! with { Implementation = "reference-integration", Target = "reference://integration/synthetic-rcs-verification", Contract = "PARCEL-REFERENCE-v1", Parameters = new() } };
        public ParcelConfiguration Normalize(ParcelConfiguration c) => c;
        public void Validate(ParcelConfiguration c) { inner.Validate(inner.Normalize(Reference(c))); using var _ = new DisposableBindingCheck(c.Wcs!); }
        public CapabilityRequirements Requirements(CapabilityBinding b) => new(BusinessAcceptance: true, Query: false);
        public ParcelPorts Create(ParcelConfiguration c)
        {
            var p = inner.Create(inner.Normalize(Reference(c))); var service = new SportmasterRcsTaskService(c.Wcs!);
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
        readonly HttpListener listener = new(); readonly CancellationTokenSource stop = new(); readonly Task loop;
        readonly ConcurrentBag<Task> pending = [];
        public ConcurrentQueue<Exchange> Requests { get; } = new();
        public string Mode = "assigned";
        public string Endpoint { get; }
        public Peer()
        {
            var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
            Endpoint = "http://127.0.0.1:" + port + "/rcs/sorter/api"; listener.Prefixes.Add("http://127.0.0.1:" + port + "/"); listener.Start(); loop = Loop();
        }
        async Task Loop()
        {
            try { while (!stop.IsCancellationRequested) { var context = await listener.GetContextAsync().WaitAsync(stop.Token); pending.Add(Handle(context)); } }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (HttpListenerException) when (stop.IsCancellationRequested) { }
        }
        async Task Handle(HttpListenerContext context)
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8); var body = await reader.ReadToEndAsync(); var mode = Mode;
            if (mode == "by-position") { using var j = JsonDocument.Parse(body); mode = j.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("stationCode").GetString() == "TEST-LEFT" ? "unpack" : "assigned"; }
            var response = Response(body, mode); var status = mode == "http-error" ? 503 : mode == "redirect" ? 302 : 200;
            Requests.Enqueue(new(context.Request.HttpMethod, context.Request.ContentType ?? "", body, status, response));
            try
            {
                if (mode == "timeout") await Task.Delay(2500, stop.Token);
                var bytes = Encoding.UTF8.GetBytes(response); context.Response.StatusCode = status; if (mode == "redirect") context.Response.RedirectLocation = Endpoint + "/synthetic-followed"; context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, stop.Token);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (HttpListenerException) { } // The timeout fixture intentionally closes the client.
            catch (IOException) { }
            finally { context.Response.Close(); }
        }
        public async ValueTask DisposeAsync()
        { stop.Cancel(); listener.Close(); await loop; await Task.WhenAll(pending); stop.Dispose(); }
    }
}
