using System.Net;
using System.Net.Sockets;
using System.Text;
using Ipc.Platform;

namespace Ipc.Drivers;

// Explicit client/LF framing requested by the user. No commands or reconnect replay.
public sealed class TcpLineObservationSource : IControlledObservationSource
{
    public static CapabilityDescriptor Descriptor => new("observation", "tcp-line", "1.0.1", "TCP-LINE-OBSERVATION-v1",
    [new("device.ip", "连接", "相机 IP 地址", "string", true, MaximumLength: 15),
     new("device.port", "连接", "相机端口", "integer", true, Minimum: 1, Maximum: 65535),
     new("bcr-code", "相机编码", "相机编码（心跳）", "string", MaximumLength: 256),
     new("entity.mode", "实体关联", "包裹关联方式", "choice", Default: "observe-only", Choices: ["observe-only", "line"], Description: "仅观察不创建作业；每行一个包裹须由现场确认，重复行不会按条码合并。")], new());
    readonly DeviceBinding binding;
    readonly SemaphoreSlim lifecycle = new(1, 1);
    readonly object gate = new();
    TcpClient? client;
    CancellationTokenSource? stop;
    Task? reading, liveness;
    Action<DeviceHealthConfirmation>? health;
    bool failed, disposed;
    long sequence;
    string? localEndpoint, remoteEndpoint, failureCategory, socketError;
    string phase = "NotStarted";
    DateTimeOffset? receivedAt, validObservationAt;
    public string? LastFailure { get; private set; }
    public string DeviceId => binding.DeviceId;
    public string Epoch { get; private set; } = "not-started";
    public string ConfirmationSource => "TCP:client-LF:" + DeviceId;
    public TcpLineObservationSource(DeviceBinding binding)
    {
        Validate(binding);
        this.binding = binding with { Parameters = new(binding.Parameters!) };
    }
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
            if (client is not null || reading is not null) throw new InvalidOperationException("Stop the original connection before reconnecting");
            lock (gate)
            {
                Epoch = registeredEpoch; sequence = 0; failed = false; LastFailure = null; health = onHealth;
                localEndpoint = remoteEndpoint = failureCategory = socketError = null;
                receivedAt = validObservationAt = null; phase = "Connect";
                client = new TcpClient(AddressFamily.InterNetwork); stop = new(); Confirm(DeviceHealth.Connecting);
            }
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await client.ConnectAsync(IPAddress.Parse(binding.Parameters!["device.ip"]), int.Parse(binding.Parameters["device.port"], System.Globalization.CultureInfo.InvariantCulture), budget.Token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                lock (gate)
                {
                    localEndpoint = client.Client.LocalEndPoint?.ToString(); remoteEndpoint = client.Client.RemoteEndPoint?.ToString();
                    phase = "Receive"; Confirm(DeviceHealth.Ready);
                }
                var stream = client.GetStream(); var receiveToken = stop.Token;
                reading = Task.Run(() => Read(stream, receive, receiveToken), CancellationToken.None);
                liveness = Monitor(receiveToken);
            }
            catch (Exception error)
            {
                Fault(error, error is OperationCanceledException ? token.IsCancellationRequested ? "ConnectCancelled" : "ConnectTimeout" : "ConnectFailure");
                client?.Dispose(); client = null; stop?.Dispose(); stop = null;
                if (error is OperationCanceledException && !token.IsCancellationRequested)
                    throw new ObservationConnectionException("ConnectTimeout", error);
                if (error is SocketException or IOException)
                    throw new ObservationConnectionException("ConnectFailure", error);
                throw;
            }
        }
        finally { lifecycle.Release(); }
    }
    async Task Read(NetworkStream stream, Func<Observation, bool> receive, CancellationToken token)
    {
        var buffer = new byte[1024]; var line = new List<byte>(4097);
        var category = "ReceiveFailure";
        try
        {
            while (true)
            {
                category = "ReceiveFailure";
                var count = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                if (count == 0)
                {
                    category = line.Count == 0 ? "RemoteClosed" : "IncompleteFrame";
                    throw new EndOfStreamException();
                }
                lock (gate) receivedAt = DateTimeOffset.UtcNow;
                for (var i = 0; i < count; i++)
                {
                    var b = buffer[i];
                    if (b != 10)
                    {
                        // 4096 payload bytes, with an optional CR belonging to CRLF.
                        if (line.Count >= 4096 && !(line.Count == 4096 && b == 13))
                        { category = "FrameTooLong"; throw new InvalidDataException(); }
                        line.Add(b); continue;
                    }
                    if (line.Count > 0 && line[^1] == 13) line.RemoveAt(line.Count - 1);
                    category = "InvalidUtf8";
                    var code = new UTF8Encoding(false, true).GetString(line.ToArray()); line.Clear();
                    category = code.Length == 0 ? "EmptyFrame" : "ControlCharacter";
                    if (code.Length == 0 || code.Any(char.IsControl)) throw new InvalidDataException();
                    Observation observation;
                    lock (gate)
                    {
                        if (failed || token.IsCancellationRequested) return;
                        validObservationAt = DateTimeOffset.UtcNow;
                        var n = ++sequence; var source = Epoch + "." + n;
                        observation = new(Wire.Id(), DeviceId, Epoch, source, n, "tcp." + Wire.Hash(source)[..40].ToLowerInvariant(),
                            binding.Parameters!.GetValueOrDefault("entity.mode", "observe-only") == "line" ? "Read" : "UncorrelatedRead", [code], null, validObservationAt.Value);
                    }
                    // No driver lock across application code. Stop can fence the
                    // socket and honor cancellation while retaining an in-flight reader.
                    category = "IngressCallbackFailure";
                    if (!receive(observation)) { category = "IngressRejected"; throw new IOException(); }
                    lock (gate) { if (!failed && !token.IsCancellationRequested) Confirm(DeviceHealth.Ready); }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
        catch (IOException) when (token.IsCancellationRequested) { }
        catch (Exception error) { Fault(error, category); }
    }
    async Task Monitor(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                lock (gate)
                {
                    if (failed || token.IsCancellationRequested) return;
                    // Read alone owns EOF/errors. Poll + Available races with Read.
                    // This renews LOCAL transport evidence, never a remote heartbeat.
                    Confirm(DeviceHealth.Ready);
                }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { Fault(error, "LocalMonitorFailure"); }
    }
    void Confirm(DeviceHealth state)
    {
        var at = DateTimeOffset.UtcNow;
        var evidence = ConfirmationSource + "; " + Wire.Encode(new
        {
            Phase = phase, Category = failureCategory, SocketError = socketError, Sequence = sequence,
            ConfiguredRemote = EndpointIdentity(binding), Local = localEndpoint, Remote = remoteEndpoint,
            LastReceivedAt = receivedAt, LastValidObservationAt = validObservationAt,
            Confirmation = "LocalTransportOnly; no application heartbeat; business admission is separate"
        });
        health?.Invoke(new(DeviceId, Epoch, state, at, state == DeviceHealth.Ready ? at.AddSeconds(3) : null, evidence));
    }
    void Fault(Exception error, string category)
    {
        lock (gate)
        {
            if (failed) return;
            failureCategory = category;
            phase = category.StartsWith("Ingress", StringComparison.Ordinal) ? "Ingress" :
                category is "InvalidUtf8" or "EmptyFrame" or "ControlCharacter" or "FrameTooLong" or "IncompleteFrame" ? "Protocol" : phase;
            socketError = (error as SocketException ?? error.InnerException as SocketException)?.SocketErrorCode.ToString();
            // Do not retain callback messages or raw barcode/protocol bytes.
            LastFailure = category + (socketError is null ? "" : ": " + socketError);
            failed = true; stop?.Cancel(); client?.Dispose(); Confirm(DeviceHealth.Faulted);
        }
    }
    async Task StopCore(CancellationToken token)
    {
        lock (gate) { phase = "Stop"; stop?.Cancel(); client?.Dispose(); }
        // A cancelled wait retains tasks/CTS/client for a later explicit drain.
        if (reading is not null) await reading.WaitAsync(token).ConfigureAwait(false);
        if (liveness is not null) await liveness.WaitAsync(token).ConfigureAwait(false);
        lock (gate)
        {
            client = null; reading = liveness = null; stop?.Dispose(); stop = null;
            if (health is not null) Confirm(DeviceHealth.Stopped); health = null;
        }
    }
    public async Task StopAsync(CancellationToken token)
    {
        await lifecycle.WaitAsync(token).ConfigureAwait(false);
        try { await StopCore(token).ConfigureAwait(false); }
        finally { lifecycle.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try { if (!disposed) { await StopCore(default).ConfigureAwait(false); disposed = true; } }
        finally { lifecycle.Release(); }
    }
}
