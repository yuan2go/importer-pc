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
        var template = HeartbeatConfiguration(workspace, binding);
        var directory = Path.Combine(root, "config"); Directory.CreateDirectory(directory);
        ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), template);
        var launch = new BusinessLaunch(directory, Path.Combine(root, "data"), null, null);
        async Task<ParcelRuntime> Open()
        {
            var app = new ParcelRuntime(launch, new SportmasterFlow(), new P0SimulationFactory(launch)) { RuntimeIdentityPreflight = () => { } };
            await app.StartAsync(default); return app;
        }
        string frozen; string originalId;
        await using (var app = await Open())
        {
            app.EmitReferenceAt("left", "synth-refused-1", "SYNTH-0001", Wire.Id());
            var snapshot = await Wait(app, s => s.Operations.Any(o => o.State == ParcelState.WcsUnknown));
            var original = snapshot.Operations.Single(); originalId = original.Id; frozen = Wire.Encode(original.Wcs);
            Check(original.Wcs is { State: StageState.Unknown, Attempts: 1 } && original.Wcs.Replies.Single().SenderDelivery == SenderDeliveryEvidence.ConnectionRefusedBeforeBody && snapshot.Positions!.Single(p => p.Id == "left").Admit,
                "no-body refusal remains Unknown in durable original stage but permits a new independent parcel");
        }
        await using var peer = new CameraRoundHarness.RoundPeer(port);
        await using (var app = await Open())
        {
            await Task.Delay(150);
            Check(peer.Requests.IsEmpty && Wire.Encode((await app.OperationAsync(originalId))!.Wcs) == frozen,
                "restart and connection restoration preserve original refusal bytes/attempt and never replay its request");
            app.EmitReferenceAt("left", "synth-refused-2", "SYNTH-0001", Wire.Id());
            var after = await Wait(app, s => s.Operations.Any(o => o.State == ParcelState.SlotResolved));
            Check(peer.Requests.Count == 1 && after.Operations.Single(o => o.Id != originalId).Wcs!.Request.Id != after.Operations.Single(o => o.Id == originalId).Wcs!.Request.Id,
                "fresh scan after restoration creates exactly one new frozen request while retaining old responsibility");
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
            using var socket = await dropped.AcceptTcpClientAsync(); using var stream = socket.GetStream();
            var header = new List<byte>(); var one = new byte[1];
            while (!System.Text.Encoding.ASCII.GetString(header.ToArray()).EndsWith("\r\n\r\n", StringComparison.Ordinal))
            { if (await stream.ReadAsync(one) != 1 || header.Count > 16384) throw new IOException("Incomplete synthetic header"); header.Add(one[0]); }
            var text = System.Text.Encoding.ASCII.GetString(header.ToArray());
            var length = int.Parse(text.Split("\r\n").Single(s => s.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Split(':')[1].Trim());
            var body = new byte[length]; await stream.ReadExactlyAsync(body);
            socket.Client.LingerState = new(true, 0); return System.Text.Encoding.UTF8.GetString(body);
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
