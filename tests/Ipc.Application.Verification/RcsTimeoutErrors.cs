using System.Diagnostics;
using System.Text;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Assembly;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static partial class SportmasterRcs
{
    public static async Task TimeoutErrorChecks(string workspace, string root)
    {
        var schema = Wire.Decode<ParameterField[]>(File.ReadAllText(Path.Combine(workspace, "src/Ipc.Integrations.SportmasterRcs/parameters.json")));
        Check(ExtensionSchema.ValidateDefinition(schema).Length == 0 && schema.Single(p => p.Key == "request-timeout-ms") is
            { Default: "5000", Minimum: 100, Maximum: 30000, Unit: "毫秒" }, "request timeout schema has units, default and bounds");
        await CurrentRequestTimeoutChecks(workspace);
        await using var peer = new Peer();
        var binding = Binding(peer.Endpoint) with { Implementation = "sportmaster-rcs", Version = "1.0.0", Parameters = new(Binding(peer.Endpoint).Parameters) { ["request-timeout-ms"] = "300" } };
        foreach (var invalid in new[] { "0", "99", "30001", "1.5", "-1", "bad" })
        {
            try { await using var rejected = new SportmasterRcsTaskService(binding with { Parameters = new(binding.Parameters) { ["request-timeout-ms"] = invalid } }); throw new Exception("Invalid timeout accepted"); }
            catch (InvalidDataException) { Check(true, "invalid component timeout refused: " + invalid); }
        }
        await using (var service = new SportmasterRcsTaskService(binding))
        {
            service.ConfigureRuntime(HeartbeatConfiguration(workspace, binding)); await service.StartAsync(default);
            FrozenRequest Prepare(long sequence)
            { var original = Request(binding); return original with { IntegrationRequest = service.PrepareRequest(original, sequence) }; }
            peer.Mode = "timeout"; var before = peer.Requests.Count; var watch = Stopwatch.StartNew();
            try { await service.ResolveSlotAsync(Prepare(1), default); throw new Exception("Unbounded RCS request"); }
            catch (TimeoutException) { Check(watch.Elapsed < TimeSpan.FromSeconds(2) && peer.Requests.Count == before + 1, "component times out without a caller deadline and sends only once"); }
            using (var cancelled = new CancellationTokenSource(50))
            {
                try { await service.ResolveSlotAsync(Prepare(2), cancelled.Token); throw new Exception("Caller cancellation lost"); }
                catch (OperationCanceledException) { Check(true, "caller cancellation is not mistaken for component timeout"); }
            }
            peer.Mode = "body-timeout";
            var partial = await service.ResolveSlotAsync(Prepare(3), default);
            Check(partial.BusinessAccepted is null && partial.HttpResponse is { Complete: false } &&
                Encoding.UTF8.GetString(Convert.FromBase64String(partial.HttpResponse.BodyBase64)) == "{\"requestId\":" &&
                partial.Evidence.StartsWith("RCS_REQUEST_TIMEOUT:", StringComparison.Ordinal), "body-read timeout retains the original prefix and no assigned chute");
        }

        // Input custody and JIT work must fit the synthetic admission age. The
        // component deadline remains three times the generic service budget.
        const int genericRuntimeBudget = 500, componentRuntimeDeadline = 1500;
        var runtimeTimeoutBinding = binding with { Parameters = new(binding.Parameters) { ["request-timeout-ms"] = componentRuntimeDeadline.ToString(System.Globalization.CultureInfo.InvariantCulture) } };
        var template = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        async Task<ParcelRuntime> Open(string name, CapabilityBinding selected)
        {
            var directory = Path.Combine(root, name); Directory.CreateDirectory(directory);
            ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), template with { Wcs = selected, TimeoutMs = genericRuntimeBudget });
            var launch = new BusinessLaunch(directory, Path.Combine(directory, "data"), null, null);
            // Isolated Simulation only; production entry points retain the machine identity check.
            var app = new ParcelRuntime(launch, new SportmasterFlow(), new P0SimulationFactory(launch)) { RuntimeIdentityPreflight = () => { } };
            try { await app.StartAsync(default); return app; } catch { await app.DisposeAsync(); throw; }
        }
        peer.Mode = "timeout";
        await using (var app = await Open("timeout-presentation", runtimeTimeoutBinding))
        {
            var watch = Stopwatch.StartNew(); var before = peer.Requests.Count;
            app.EmitReferenceAt("left", "timeout-original", "SYNTH-0001", Wire.Id());
            var s = await Wait(app, x => x.Operations.Any(o => o.State == ParcelState.WcsUnknown)); var op = s.Operations.Single();
            Check(watch.ElapsedMilliseconds >= componentRuntimeDeadline - 50 && watch.Elapsed < TimeSpan.FromSeconds(3) && op.Wcs!.Attempts == 1 && peer.Requests.Count == before + 1,
                "configured RCS deadline overrides the shorter generic service budget and retains one attempt");
            var omitted = binding with { Parameters = new(binding.Parameters.Where(p => p.Key != "request-timeout-ms")) };
            ParcelOperation WithBinding(CapabilityBinding value) => op with { Wcs = op.Wcs! with { Request = op.Wcs.Request with { Binding = value } } };
            Check(new SportmasterFlow().RequestTimeoutMs(WithBinding(omitted), ParcelStage.Wcs) == 5000 &&
                new SportmasterFlow().RequestTimeoutMs(WithBinding(omitted), ParcelStage.Wcs) != genericRuntimeBudget,
                "omitted component timeout uses the displayed default independently of the generic service budget");
            var view = new SupplyStationViewModel(app); await view.RefreshAsync();
            Check(view.Left.AttentionReason == "RCS请求超时，请重新扫描。" && view.Left.OperatorInstruction.Contains("重新扫描", StringComparison.Ordinal) &&
                view.Left.NextStep.Contains("重新扫描", StringComparison.Ordinal) && view.Left.Slot == "未取得" && !view.Left.SlotAvailable,
                "timeout guidance is visible in the work area with no stale chute");
            await Task.Delay(2600);
            var afterLateResponse = (await app.SnapshotAsync()).Operations.Single();
            Check(peer.Requests.Count == before + 1 && afterLateResponse.State == ParcelState.WcsUnknown && afterLateResponse.Slot is null && afterLateResponse.Wcs!.Attempts == 1,
                "timeout does not repeat the POST and a late server response cannot assign a chute");
        }
        foreach (var mode in new[] { "assigned", "unpack", "reject", "missing-chute", "blank-chute", "malformed", "wrong-id" })
        {
            peer.Mode = mode;
            peer.ResponseTransform = mode is "reject" or "missing-chute" or "blank-chute" or "wrong-id"
                ? text => text.Replace("\"message\":\"SYNTH_OTHER_ERROR\"", "\"message\":\"请检查当前商品状态\"", StringComparison.Ordinal)
                    .Replace("\"message\":null", "\"message\":\"请检查当前商品状态\"", StringComparison.Ordinal) : null;
            var fastBinding = binding with { Parameters = new(binding.Parameters) { ["request-timeout-ms"] = "1500" } };
            await using var app = await Open("error-" + mode, fastBinding);
            app.EmitReferenceAt("left", "result-" + mode, "SYNTH-0001", Wire.Id());
            var s = await Wait(app, x => x.Operations.Any(o => o.Wcs?.State is StageState.Accepted or StageState.Rejected or StageState.Unknown));
            var view = new SupplyStationViewModel(app); await view.RefreshAsync(); var op = s.Operations.Single();
            if (mode == "assigned")
            {
                Check(view.Left.SlotAvailable && view.Left.Slot == "SYNTH-CHUTE", "valid assignment still displays its exact chute");
                peer.Mode = "reject";
                app.EmitReferenceAt("left", "rejected-after-assigned", "SYNTH-0001", Wire.Id());
                await Wait(app, x => x.Operations.Length == 2 && x.Operations.Any(o => o.State == ParcelState.WcsRejected));
                await view.RefreshAsync();
                Check(!view.Left.SlotAvailable && view.Left.Slot == "未取得" && view.Left.AttentionReason == "SYNTH_OTHER_ERROR",
                    "a rejected next parcel replaces the previous successful chute with its own message");
            }
            else
            {
                Check(!view.Left.SlotAvailable && view.Left.Slot == "未取得", mode + " never displays an assignment");
                Check(mode switch
                {
                    "unpack" => view.Left.AttentionReason.Contains("需要拆包", StringComparison.Ordinal) && op.State == ParcelState.UnpackRequired,
                    "reject" => view.Left.AttentionReason == "请检查当前商品状态" && op.State == ParcelState.WcsRejected,
                    "missing-chute" or "blank-chute" => view.Left.AttentionReason == "请检查当前商品状态" && op.State == ParcelState.WcsUnknown,
                    _ => view.Left.AttentionReason == "RCS未返回有效格口，请核对异常信息。" && op.State == ParcelState.WcsUnknown
                }, mode + " shows the matched exception/message with safe fallback for invalid or unmatched replies");
                if (mode != "reject") Check(!s.Positions!.Single(p => p.Id == "left").Admit, mode + " does not use the timeout-only rescan exception");
            }
        }
        peer.ResponseTransform = null;
        peer.Mode = "body-timeout";
        await using (var app = await Open("body-timeout-presentation", binding))
        {
            app.EmitReferenceAt("left", "partial-timeout-original", "SYNTH-0001", Wire.Id());
            var s = await Wait(app, x => x.Operations.Any(o => o.State == ParcelState.WcsUnknown)); var op = s.Operations.Single();
            var view = new SupplyStationViewModel(app); await view.RefreshAsync();
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "body-timeout-presentation", "data", "Simulation", template.StationId, "sportmaster", "sportmaster.db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            db.Open(); using var query = db.CreateCommand(); query.CommandText = "SELECT body FROM parcel_operations WHERE id=$id"; query.Parameters.AddWithValue("$id", op.Id);
            var persisted = Wire.Decode<ParcelOperation>((string)query.ExecuteScalar()!);
            Check(view.Left.AttentionReason == "RCS请求超时，请重新扫描。" && persisted.Wcs!.Replies.Single().HttpResponse is { Complete: false } &&
                Encoding.UTF8.GetString(Convert.FromBase64String(persisted.Wcs.Replies.Single().HttpResponse!.BodyBase64)) == "{\"requestId\":" &&
                op.Wcs!.Replies.Single().HttpResponse is null,
                "application/component deadline race preserves the response prefix and still shows rescan guidance");
        }
        await TimeoutRescanChecks(workspace, root);
        Console.WriteLine("PASS RCS TIMEOUT AND ERROR PRESENTATION COMPLETE");
    }

    static async Task CurrentRequestTimeoutChecks(string workspace)
    {
        await using var peer = new Peer { Mode = "delayed-assignment" };
        var binding = Binding(peer.Endpoint) with { Implementation = "sportmaster-rcs" };
        foreach (var version in new[] { "1.3.2", "1.3.3", "1.3.4" })
        {
            try { await using var rejected = new SportmasterRcsTaskService(binding with { Version = version }); throw new Exception("Old version accepted"); }
            catch (InvalidDataException) { Check(peer.Requests.Count == 0, "old component rejected before I/O: " + version); }
            Check(new Ipc.Application.Sportmaster.SportmasterFlow().Validate(HeartbeatConfiguration(workspace, binding with { Version = version })).Any(x => x.Contains("RCS组件版本")),
                "current configuration validation refuses old RCS before startup ledger/database writes: " + version);
        }
        foreach (var explicitTimeout in new[] { false, true })
        {
            var selected = explicitTimeout ? binding with { Parameters = new(binding.Parameters) { ["request-timeout-ms"] = "10000" } } : binding;
            await using var service = new SportmasterRcsTaskService(selected);
            service.ConfigureRuntime(HeartbeatConfiguration(workspace, selected)); await service.StartAsync(default);
            var request = Request(selected); request = request with { IntegrationRequest = service.PrepareRequest(request, 1) };
            using var caller = new CancellationTokenSource(10000);
            var before = peer.Requests.Count; var watch = Stopwatch.StartNew();
            if (explicitTimeout)
                Check((await service.ResolveSlotAsync(request, caller.Token)).BusinessAccepted == true && watch.ElapsedMilliseconds >= 5000,
                    "explicit ten-second budget accepts a valid response beyond five seconds");
            else
            {
                try { await service.ResolveSlotAsync(request, caller.Token); throw new Exception("Default budget extended by caller"); }
                catch (TimeoutException) { Check(watch.ElapsedMilliseconds >= 4500 && watch.ElapsedMilliseconds < 8000, "default five-second deadline overrides a ten-second caller budget"); }
            }
            Check(peer.Requests.Count == before + 1, "one request per frozen intent without retries");
        }
        peer.Mode = "assigned";
        var slowBinding = binding with { Parameters = new(binding.Parameters) { ["request-timeout-ms"] = "15000" } };
        int connects = 0;
        await using var slowConnect = new SportmasterRcsTaskService(slowBinding, async (context, token) =>
        {
            Interlocked.Increment(ref connects);
            await Task.Delay(10500, token);
            var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
            try { await socket.ConnectAsync(context.DnsEndPoint, token); return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); throw; }
        });
        slowConnect.ConfigureRuntime(HeartbeatConfiguration(workspace, slowBinding)); await slowConnect.StartAsync(default);
        var slowRequest = Request(slowBinding); slowRequest = slowRequest with { IntegrationRequest = slowConnect.PrepareRequest(slowRequest, 1) };
        var originalBytes = Wire.Encode(slowRequest); var sends = peer.Requests.Count; var slowWatch = Stopwatch.StartNew();
        Check((await slowConnect.RequestDestinationAsync(slowRequest, default)).BusinessAccepted == true && slowWatch.ElapsedMilliseconds >= 10000 &&
            connects == 1 && peer.Requests.Count == sends + 1 && Wire.Encode(slowRequest) == originalBytes,
            "controlled connection beyond ten seconds respects the configured total deadline and immutable once-send request");
    }
}
