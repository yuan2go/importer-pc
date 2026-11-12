using System.Net;
using System.Net.Sockets;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Platform;

static partial class SportmasterRcs
{
    internal static async Task ConnectionEvidenceChecks(string workspace, string root)
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start(); var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        var binding = Binding($"http://127.0.0.1:{port}/rcs/sorter/api") with { Implementation = "sportmaster-rcs" };
        // Windows TCP refusal can arrive after the production 1s budget. This
        // evidence test waits for the actual refused-connect result, not timeout.
        binding.Parameters["request-timeout-ms"] = "10000";
        await using (var service = new SportmasterRcsTaskService(binding))
        {
            service.ConfigureRuntime(HeartbeatConfiguration(workspace, binding)); await service.StartAsync(default);
            var request = Request(binding); request = request with { IntegrationRequest = service.PrepareRequest(request, 1) };
            var reply = await service.ResolveSlotAsync(request, default);
            Check(reply is
            {
                SenderDelivery: SenderDeliveryEvidence.ConnectionRefusedBeforeBody, TransportSucceeded: false,
                ProtocolAcknowledged: null, BusinessAccepted: null, PhysicalCompleted: null, HttpResponse: null
            } && reply.RequestId == request.Id && reply.PayloadHash == request.PayloadHash,
                "real loopback connection refusal produces only typed sender no-body evidence, no fabricated HTTP/business reply");
        }
        await using (var h = new ManualSupplyHarness(workspace, Path.Combine(root, "arrival-refused"), deferred: true))
        {
            h.Config.Wcs!.Parameters["request-timeout-ms"] = "10000";
            ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config);
            await h.StartAsync();
            var vehicle = (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Arrival!.Generation;
            var originalPort = new Uri(h.Peer.Endpoint).Port;
            await h.Peer.DisposeAsync();
            var submitted = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-0001"), default);
            await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(submitted.OperationId!))?.Wcs?.State == StageState.Unknown);
            var original = (await h.App.OperationAsync(submitted.OperationId!))!;
            var frozen = Wire.Encode(original.Wcs);
            Check(original.Wcs is { Attempts: 1, Replies: [{ SenderDelivery: SenderDeliveryEvidence.ConnectionRefusedBeforeBody }] },
                "actual Application records the unique refused-connect attempt without an invented HTTP reply");
            await SmRoundFixture.ConfirmRemovedAsync(h.App, "left");
            await CameraRoundHarness.Until(async () => !(await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Arrival!.Submitted);
            await using var restored = new CameraRoundHarness.RoundPeer(originalPort);
            await h.App.CheckRcsHeartbeatAsync(default);
            var afterRemoval = (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left");
            Check(afterRemoval.Arrival!.Generation == vehicle && afterRemoval.BarcodeInput!.Manual && afterRemoval.Arrival.TargetOperation is null,
                "actual call drain plus explicit removal releases only the proven-undelivered same-car claim without toggling supply");
            var next = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-0001"), default);
            await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(next.OperationId!))?.State == ParcelState.SlotResolved);
            Check(restored.Requests.Count == 1 && next.OperationId != original.Id && Wire.Encode((await h.App.OperationAsync(original.Id))!.Wcs) == frozen,
                "same-code next parcel sends one distinct request on the same car, preserving original Unknown/no-body evidence");
            await h.App.DisposeAsync(); await h.StartAsync();
            Check(restored.Requests.Count == 1 && Wire.Encode((await h.App.OperationAsync(original.Id))!.Wcs) == frozen,
                "restart preserves original refused-connect evidence and never replays either request");
        }
        foreach (var mode in new[] { "http-error", "malformed" })
        {
            await using var errors = new Peer { Mode = mode };
            var b = Binding(errors.Endpoint) with { Implementation = "sportmaster-rcs" };
            await using var service = new SportmasterRcsTaskService(b); service.ConfigureRuntime(HeartbeatConfiguration(workspace, b)); await service.StartAsync(default);
            var r = Request(b); r = r with { IntegrationRequest = service.PrepareRequest(r, 1) };
            var reply = await service.ResolveSlotAsync(r, default);
            Check(reply.SenderDelivery == SenderDeliveryEvidence.Unknown && reply.HttpResponse is not null && reply.BusinessAccepted is null,
                mode + " retains real original response and cannot claim a no-delivery exception");
        }
        var dropped = new TcpListener(IPAddress.Loopback, 0); dropped.Start(); var droppedPort = ((IPEndPoint)dropped.LocalEndpoint).Port;
        var received = Task.Run(async () =>
        {
            // The service owns an independent real heartbeat connection. Answer
            // that protocol before selecting the destination body to drop.
            using var receiveBudget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                using var socket = await dropped.AcceptTcpClientAsync(receiveBudget.Token); using var stream = socket.GetStream();
                var header = new List<byte>(); var one = new byte[1];
                while (!System.Text.Encoding.ASCII.GetString(header.ToArray()).EndsWith("\r\n\r\n", StringComparison.Ordinal))
                { if (await stream.ReadAsync(one) != 1 || header.Count > 16384) throw new IOException("Incomplete synthetic header"); header.Add(one[0]); }
                var text = System.Text.Encoding.ASCII.GetString(header.ToArray());
                var length = int.Parse(text.Split("\r\n").Single(s => s.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Split(':')[1].Trim());
                var body = new byte[length]; await stream.ReadExactlyAsync(body);
                var bodyText = System.Text.Encoding.UTF8.GetString(body);
                if (bodyText.Contains("sorter.task.station_heartbeat", StringComparison.Ordinal))
                {
                    var response = System.Text.Encoding.UTF8.GetBytes(Response(bodyText, "assigned"));
                    await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {response.Length}\r\nConnection: close\r\n\r\n"));
                    await stream.WriteAsync(response); continue;
                }
                socket.Client.LingerState = new(true, 0); return bodyText;
            }
        });
        try
        {
            var b = Binding($"http://127.0.0.1:{droppedPort}/rcs/sorter/api") with { Implementation = "sportmaster-rcs" };
            await using var service = new SportmasterRcsTaskService(b); service.ConfigureRuntime(HeartbeatConfiguration(workspace, b)); await service.StartAsync(default);
            var r = Request(b); r = r with { IntegrationRequest = service.PrepareRequest(r, 1) };
            try { var reply = await service.ResolveSlotAsync(r, default); Check(reply.SenderDelivery == SenderDeliveryEvidence.Unknown && reply.BusinessAccepted is null, "response lost after body cannot produce no-delivery evidence"); }
            catch (IOException) { Check(true, "connection reset after body remains delivery/outcome unknown"); }
            Check(await received.WaitAsync(TimeSpan.FromSeconds(3)) == r.IntegrationRequest.Body, "lost-response peer received exact original frozen body once");
        }
        finally { dropped.Stop(); }
    }
}
