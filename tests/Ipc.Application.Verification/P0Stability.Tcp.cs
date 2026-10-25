using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Ipc.Drivers;
using Ipc.Platform;

static class P0Tcp
{
    public static async Task Run(string root)
    {
        var errors = new List<string>();
        foreach (var scenario in new[] { "FIN", "RST", "silent-forwarder" })
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var p = new Dictionary<string, string> { ["device.ip"] = "127.0.0.1", ["device.port"] = ((IPEndPoint)listener.LocalEndpoint).Port.ToString(), ["entity.mode"] = "line" };
            var configured = TcpLineObservationSource.Descriptor.Parameters.Any(x => x.Key == "liveness-timeout-seconds");
            if (configured)
            {
                p["keepalive-time-seconds"] = "1"; p["keepalive-interval-seconds"] = "1";
                p["keepalive-retry-count"] = "2"; p["liveness-timeout-seconds"] = "2";
            }
            var binding = new DeviceBinding("parcel-reader", "tcp-line", "synthetic." + scenario, "1") { Parameters = p };
            await using var source = new TcpLineObservationSource(binding);
            var observations = new ConcurrentQueue<Observation>(); var states = new ConcurrentQueue<DeviceHealthConfirmation>();
            var watch = Stopwatch.StartNew();
            await source.StartControlledAsync("registered-" + scenario, o => { observations.Enqueue(o); return true; }, states.Enqueue, default);
            using var peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
            var socket = ((TcpClient)typeof(TcpLineObservationSource).GetField("client", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!).Client;
            var keepalive = (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)!;
            var actual = new Dictionary<string, object> { ["KeepAlive"] = keepalive, ["ConfigurableDeadline"] = configured };
            // Enum numeric aliases must not label TCP options as BlockSource/DontRoute.
            foreach (var (name, option) in new[] { ("TcpKeepAliveTime", SocketOptionName.TcpKeepAliveTime), ("TcpKeepAliveInterval", SocketOptionName.TcpKeepAliveInterval), ("TcpKeepAliveRetryCount", SocketOptionName.TcpKeepAliveRetryCount) })
                actual[name] = socket.GetSocketOption(SocketOptionLevel.Tcp, option)!;
            await peer.GetStream().WriteAsync(Encoding.UTF8.GetBytes("000123\n"));
            await Until(() => observations.Count == 1, 3000);
            var confirmed = states.Last(x => x.Health == DeviceHealth.Ready); var epoch = source.Epoch;
            if (scenario == "FIN") peer.Close();
            if (scenario == "RST") { peer.Client.LingerState = new(true, 0); peer.Close(); }
            if (scenario == "silent-forwarder")
            {
                // TCP endpoint remains connected and can ACK keepalive; it stops
                // forwarding application data. This is not a packet-drop test.
                await Task.Delay(1200);
                actual["LocalTimerRenewedReady"] = states.Last(x => x.Health == DeviceHealth.Ready).ConfirmedAt != confirmed.ConfirmedAt;
            }
            var detected = await Until(() => states.Any(x => x.Health == DeviceHealth.Faulted), 3000);
            actual["FaultDetected"] = detected; actual["ElapsedMs"] = watch.Elapsed.TotalMilliseconds;
            actual["EpochRetired"] = source.Epoch != epoch; actual["LastFailure"] = source.LastFailure ?? "none";
            if (detected && scenario == "silent-forwarder")
                try { await peer.GetStream().WriteAsync(Encoding.UTF8.GetBytes("STALE\n")); } catch (IOException) { }
            await Task.Delay(120);
            actual["ObservationCount"] = observations.Count; actual["AutoReconnected"] = listener.Pending();
            File.WriteAllText(Path.Combine(root, "tcp-" + scenario + ".json"), Wire.Encode(actual));
            Console.WriteLine("ST06 " + scenario + " " + Wire.Encode(actual));
            if (keepalive == 0 || !configured || Convert.ToInt32(actual["TcpKeepAliveTime"]) != 1 || Convert.ToInt32(actual["TcpKeepAliveInterval"]) != 1 ||
                Convert.ToInt32(actual["TcpKeepAliveRetryCount"]) != 2 || !detected || watch.Elapsed > TimeSpan.FromSeconds(4) || source.Epoch == epoch || listener.Pending() || observations.Count != 1 ||
                actual.GetValueOrDefault("LocalTimerRenewedReady") is true) errors.Add(scenario);
            await source.StopAsync(default);
        }
        if (errors.Count != 0) throw new Exception("FAIL ST06 keepalive/liveness/epoch: " + string.Join(",", errors));
        Console.WriteLine("PASS ST06 FIN/RST/silent-forwarder, kernel socket options, no timer renewal or replay");
    }
    public static async Task ExistingComponents()
    {
        foreach (var name in new[] { "Protocols", "StopOwnership", "ConfirmationEvidence" })
        {
            var method = typeof(DualCameraCommunication).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(null, null)!;
        }
        Console.WriteLine("PASS ST06 existing protocol, stop ownership and confirmation assertions unchanged");
    }
    static async Task<bool> Until(Func<bool> condition, int milliseconds)
    { var watch = Stopwatch.StartNew(); while (!condition() && watch.ElapsedMilliseconds < milliseconds) await Task.Delay(10); return condition(); }
}
