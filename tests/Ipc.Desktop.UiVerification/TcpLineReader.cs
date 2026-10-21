using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Ipc.Drivers;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyTcpLineReader(Action<bool, string> check)
    {
        async Task Until(Func<bool> condition) { for (var n = 0; n < 200 && !condition(); n++) await Task.Delay(10); check(condition(), "TCP bounded event wait completes"); }
        var first = new DeviceBinding("parcel-reader", "tcp-line", "synthetic.one", "1") { Parameters = new() { ["device.ip"] = "127.0.0.1", ["device.port"] = "80" } };
        var sameEndpoint = first with { DeviceId = "synthetic.two", Parameters = new(first.Parameters) { ["device.port"] = "00080" } };
        TcpLineObservationSource.Validate(first); TcpLineObservationSource.Validate(sameEndpoint);
        var leaseRoot = Path.Combine(Path.GetTempPath(), "ipc-tcp-lease-" + Wire.Id());
        using (var held = new ResourceLease(leaseRoot, ["reader:tcp:" + TcpLineObservationSource.EndpointIdentity(first)]))
        {
            var refused = false;
            try { using var duplicate = new ResourceLease(leaseRoot, ["reader:tcp:" + TcpLineObservationSource.EndpointIdentity(sameEndpoint)]); }
            catch (IOException) { refused = true; }
            check(refused, "equivalent numeric TCP ports with different reader names cannot acquire separate physical leases");
        }
        using (var after = new ResourceLease(leaseRoot, ["reader:tcp:" + TcpLineObservationSource.EndpointIdentity(sameEndpoint)]))
            check(after.Resources.Length == 1, "physical TCP endpoint can be acquired only after the original lease releases");
        foreach (var scenario in new[] { "framing", "observe", "overflow", "invalid-utf8", "partial-eof", "reject" })
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var binding = new DeviceBinding("parcel-reader", "tcp-line", "synthetic.camera", "1") { Parameters = new() { ["device.ip"] = "127.0.0.1", ["device.port"] = ((IPEndPoint)listener.LocalEndpoint).Port.ToString(), ["entity.mode"] = scenario == "observe" ? "observe-only" : "line" } };
            await using var source = new TcpLineObservationSource(binding);
            var observations = new ConcurrentQueue<Observation>(); var states = new ConcurrentQueue<DeviceHealth>();
            await source.StartControlledAsync("synthetic-epoch", o => { observations.Enqueue(o); return scenario != "reject"; }, h => states.Enqueue(h.Health), default);
            using var peer = await listener.AcceptTcpClientAsync(); var stream = peer.GetStream();
            check(states.Contains(DeviceHealth.Ready), "TCP client connects to the configured endpoint without sending commands");
            if (scenario == "framing")
            {
                var utf8 = Encoding.UTF8.GetBytes("条码");
                await stream.WriteAsync(utf8.AsMemory(0, 1)); await stream.WriteAsync(utf8.AsMemory(1));
                await stream.WriteAsync(Encoding.UTF8.GetBytes("\r\nSAME\nSAME\n")); await Until(() => observations.Count == 3);
                var rows = observations.ToArray();
                check(rows.Select(o => o.Codes.Single()).SequenceEqual(new[] { "条码", "SAME", "SAME" }), "TCP framing preserves split UTF8, CRLF and multiple coalesced lines");
                check(rows.Select(o => o.AdmissionId).Distinct().Count() == 3 && rows.All(o => o.Epoch == "synthetic-epoch"), "same-code lines retain separate entities and the application-registered epoch");
                peer.Close(); await Until(() => states.Contains(DeviceHealth.Faulted));
                check(source.LastFailure is not null && !listener.Pending(), "TCP disconnection faults the source without automatic reconnect or replay");
            }
            else if (scenario == "observe")
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("CODE\n")); await Until(() => observations.Count == 1);
                check(observations.Single().Kind == "UncorrelatedRead", "observe-only TCP cannot invent an entity admission boundary");
            }
            else
            {
                var bytes = scenario switch { "overflow" => Encoding.UTF8.GetBytes(new string('x', 4097)), "invalid-utf8" => new byte[] { 255, 10 }, "partial-eof" => Encoding.UTF8.GetBytes("PARTIAL"), _ => Encoding.UTF8.GetBytes("REJECT\nSECOND\n") };
                await stream.WriteAsync(bytes); if (scenario == "partial-eof") peer.Close();
                await Until(() => states.Contains(DeviceHealth.Faulted));
                check(source.LastFailure is not null && observations.Count == (scenario == "reject" ? 1 : 0), "TCP " + scenario + " is explicit failure, never a truncated or replacement barcode");
            }
            await source.StopAsync(default);
            check(states.Last() == DeviceHealth.Stopped, "TCP stop drains read and liveness tasks and closes the original socket");
        }
    }
}
