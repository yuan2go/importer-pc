using System.Net;
using System.Net.Sockets;
using System.Text;
using Ipc.Platform;

namespace Ipc.Drivers;

// Explicit client/LF framing requested by the user. No commands or reconnect replay.
public sealed class TcpLineObservationSource : IControlledObservationSource
{
    public static CapabilityDescriptor Descriptor => new("observation", "tcp-line", "1.0.0", "TCP-LINE-OBSERVATION-v1",
    [new("device.ip", "连接", "相机 IP 地址", "string", true, MaximumLength: 15),
     new("device.port", "连接", "相机端口", "integer", true, Minimum: 1, Maximum: 65535),
     new("bcr-code", "相机编码", "相机编码（心跳）", "string", MaximumLength: 256),
     new("entity.mode", "实体关联", "包裹关联方式", "choice", Default: "observe-only", Choices: ["observe-only", "line"], Description: "仅观察不创建作业；每行一个包裹须由现场确认，重复行不会按条码合并。")], new());
    readonly DeviceBinding binding;
    readonly SemaphoreSlim lifecycle = new(1, 1);
    readonly object gate = new();
    TcpClient? client;
    CancellationTokenSource? stop;
    Task? reading, heartbeat;
    Action<DeviceHealthConfirmation>? health;
    bool failed, disposed;
    long sequence;
    public string? LastFailure { get; private set; }
    public string DeviceId => binding.DeviceId;
    public string Epoch { get; private set; } = "not-started";
    public string ConfirmationSource => "TCP:client-LF:" + DeviceId;
    public TcpLineObservationSource(DeviceBinding binding) { Validate(binding); this.binding = binding; }
    public static void Validate(DeviceBinding binding)
    {
        var p = binding.Parameters ?? [];
        var errors = ExtensionSchema.Validate(Descriptor.Parameters, p);
        if (errors.Length > 0) throw new InvalidDataException(string.Join("; ", errors));
        if (!IPAddress.TryParse(p.GetValueOrDefault("device.ip"), out var address) || address.AddressFamily != AddressFamily.InterNetwork || address.ToString() != p["device.ip"] || address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast))
            throw new InvalidDataException("device.ip: 填写规范的相机 IPv4 地址");
    }
    // Numerically identical ports must share the same physical resource key.
    public static string EndpointIdentity(DeviceBinding binding) => binding.Parameters!["device.ip"] + ":" +
        int.Parse(binding.Parameters["device.port"], System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture);
    public Task StartAsync(Func<Observation, bool> receive, Action<string, string, DeviceHealth> onHealth, CancellationToken token) =>
        throw new InvalidOperationException("Use the application-registered connection epoch");
    public async Task StartControlledAsync(string registeredEpoch, Func<Observation, bool> receive, Action<DeviceHealthConfirmation> onHealth, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registeredEpoch);
        await lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (client is not null) throw new InvalidOperationException("Stop the original connection before reconnecting");
            Epoch = registeredEpoch; sequence = 0; failed = false; LastFailure = null; health = onHealth;
            client = new TcpClient(AddressFamily.InterNetwork); stop = new(); Confirm(DeviceHealth.Connecting);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await client.ConnectAsync(IPAddress.Parse(binding.Parameters!["device.ip"]), int.Parse(binding.Parameters["device.port"], System.Globalization.CultureInfo.InvariantCulture), budget.Token).ConfigureAwait(false);
                Confirm(DeviceHealth.Ready);
                reading = Read(receive, stop.Token); heartbeat = Monitor(stop.Token);
            }
            catch { client.Dispose(); client = null; stop.Dispose(); stop = null; Confirm(DeviceHealth.Faulted); throw; }
        }
        finally { lifecycle.Release(); }
    }
    async Task Read(Func<Observation, bool> receive, CancellationToken token)
    {
        var buffer = new byte[1024]; var line = new List<byte>(4096);
        try
        {
            var stream = client!.GetStream();
            while (true)
            {
                var count = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException(line.Count == 0 ? "Camera closed the connection" : "Incomplete barcode line; retained as a connection failure");
                for (var i = 0; i < count; i++)
                {
                    var b = buffer[i];
                    if (b != 10) { if (line.Count == 4096) throw new InvalidDataException("Camera line exceeds 4096 bytes"); line.Add(b); continue; }
                    if (line.Count > 0 && line[^1] == 13) line.RemoveAt(line.Count - 1);
                    var code = new UTF8Encoding(false, true).GetString(line.ToArray()); line.Clear();
                    if (code.Length == 0 || code.Any(char.IsControl)) throw new InvalidDataException("Camera line is empty or contains control characters");
                    lock (gate)
                    {
                        if (failed || token.IsCancellationRequested) return;
                        var n = ++sequence; var source = Epoch + "." + n;
                        var observation = new Observation(Wire.Id(), DeviceId, Epoch, source, n, "tcp." + Wire.Hash(source)[..40].ToLowerInvariant(),
                            binding.Parameters!.GetValueOrDefault("entity.mode", "observe-only") == "line" ? "Read" : "UncorrelatedRead", [code], null, DateTimeOffset.UtcNow);
                        if (!receive(observation)) throw new IOException("Application rejected camera ingress; connection fenced");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or SocketException or DecoderFallbackException or InvalidDataException) { Fault(error); }
    }
    async Task Monitor(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                lock (gate)
                {
                    if (failed) return;
                    if (client!.Client.Poll(0, SelectMode.SelectRead) && client.Available == 0) { Fault(new EndOfStreamException("Camera closed the TCP connection")); return; }
                    Confirm(DeviceHealth.Ready);
                }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is SocketException or ObjectDisposedException) { Fault(error); }
    }
    void Confirm(DeviceHealth state)
    {
        var at = DateTimeOffset.UtcNow;
        health?.Invoke(new(DeviceId, Epoch, state, at, state == DeviceHealth.Ready ? at.AddSeconds(3) : null, ConfirmationSource));
    }
    void Fault(Exception error) { lock (gate) { if (failed) return; LastFailure = error.Message; failed = true; stop?.Cancel(); client?.Dispose(); Confirm(DeviceHealth.Faulted); } }
    public async Task StopAsync(CancellationToken token)
    {
        await lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            lock (gate) { stop?.Cancel(); client?.Dispose(); }
            if (reading is not null) await reading.ConfigureAwait(false);
            if (heartbeat is not null) await heartbeat.ConfigureAwait(false);
            client = null; reading = heartbeat = null; stop?.Dispose(); stop = null;
            if (health is not null) Confirm(DeviceHealth.Stopped); health = null;
        }
        finally { lifecycle.Release(); }
    }
    public async ValueTask DisposeAsync() { await StopAsync(default).ConfigureAwait(false); disposed = true; }
}
