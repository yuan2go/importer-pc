using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using Ipc.Platform;

namespace Ipc.Drivers;

// One bounded client/LF framer for both profiles. No automatic reconnect or replay.
public sealed class TcpLineObservationSource : IControlledObservationSource
{
    public static CapabilityDescriptor Descriptor => new("observation", "tcp-line", "1.0.0", "TCP-LINE-OBSERVATION-v1",
    [new("device.ip", "连接", "生产者 IPv4 地址", "string", true, MaximumLength: 15),
     new("device.port", "连接", "生产者 TCP 端口", "integer", true, Minimum: 1, Maximum: 65535),
     new("keepalive-time-seconds", "连接存活", "TCP空闲探测时间（秒）", "integer", Default: "60", Minimum: 1, Maximum: 7200),
     new("keepalive-interval-seconds", "连接存活", "TCP探测间隔（秒）", "integer", Default: "10", Minimum: 1, Maximum: 600),
     new("keepalive-retry-count", "连接存活", "TCP探测次数", "integer", Default: "3", Minimum: 1, Maximum: 10),
     new("liveness-timeout-seconds", "连接存活", "最长无入站数据时间（秒）", "integer", Default: "300", Minimum: 1, Maximum: 86400,
         Description: "超过后关闭连接并告警；无来件也会计时，TCP应答不等于相机业务存活，须与现场确认。"),
     .. BarcodeProducerConfiguration.Fields], new());
    readonly DeviceBinding binding;
    readonly TimeSpan livenessTimeout, handshakeTimeout;
    readonly bool sequenced;
    readonly Func<string, IDisposable> acquireLease;
    IDisposable? physicalLease;
    SequencedBarcodeProtocol? protocol;
    long connectedTicks, connectionAttempts, connections, disconnections, heartbeats;
    double maximumHeartbeatDelayMs;
    readonly Dictionary<string, long> failures = new(StringComparer.Ordinal);
    public TcpProducerDiagnostics Diagnostics
    {
        get { lock (gate) return new(failed ? "Faulted" : phase, connectionAttempts, connections, disconnections, heartbeats,
            protocol?.IsReady == true ? Stopwatch.GetElapsedTime(confirmedTicks).TotalMilliseconds : 0,
            maximumHeartbeatDelayMs, new Dictionary<string, long>(failures)); }
    }
    long confirmedTicks;
    DateTimeOffset confirmedAt;
    readonly SemaphoreSlim lifecycle = new(1, 1);
    readonly object gate = new();
    TcpClient? client;
    CancellationTokenSource? stop;
    Task? reading, liveness;
    Action<DeviceHealthConfirmation>? health;
    bool failed, disposed, connectionOpen;
    string? priorRegistration;
    long sequence;
    string? localEndpoint, remoteEndpoint, failureCategory, socketError;
    string phase = "NotStarted";
    DateTimeOffset? receivedAt, validObservationAt;
    public string? LastFailure { get; private set; }
    public string DeviceId => binding.DeviceId;
    public string Epoch { get; private set; } = "not-started";
    public string ConfirmationSource => "TCP:client-LF:" + DeviceId;
    public TcpLineObservationSource(DeviceBinding binding) : this(binding, null) { }
    internal TcpLineObservationSource(DeviceBinding binding, Func<string, IDisposable>? leaseFactory)
    {
        Validate(binding);
        this.binding = binding with { Parameters = new(binding.Parameters!) };
        sequenced = BarcodeProducerConfiguration.Sequenced(binding);
        livenessTimeout = TimeSpan.FromSeconds(sequenced ? Setting("heartbeat-timeout-seconds", 5) : Setting("liveness-timeout-seconds", 300));
        handshakeTimeout = TimeSpan.FromSeconds(Setting("handshake-timeout-seconds", 5));
        acquireLease = leaseFactory ?? (key => new ResourceLease(MachineRuntimePolicy.ResolveLeaseRoot(null), [key]));
    }
    int Setting(string key, int fallback) => binding.Parameters!.TryGetValue(key, out var value)
        ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : fallback;
    void ConfigureKeepalive(Socket socket)
    {
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, Setting("keepalive-time-seconds", 60));
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, Setting("keepalive-interval-seconds", 10));
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, Setting("keepalive-retry-count", 3));
    }
    bool Current(string registration)
    { lock (gate) return !failed && Epoch == registration && stop?.IsCancellationRequested == false; }
    void ConfirmAlive()
    {
        confirmedAt = DateTimeOffset.UtcNow; confirmedTicks = Stopwatch.GetTimestamp();
        Confirm(DeviceHealth.Ready);
    }
    public static void Validate(DeviceBinding binding)
    {
        var p = binding.Parameters ?? [];
        var errors = ExtensionSchema.Validate(Descriptor.Parameters, p);
        if (errors.Length > 0) throw new InvalidDataException(string.Join("; ", errors));
        BarcodeProducerConfiguration.Validate(binding);
        if (!IPAddress.TryParse(p.GetValueOrDefault("device.ip"), out var address) || address.AddressFamily != AddressFamily.InterNetwork || address.ToString() != p["device.ip"] || address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast))
            throw new InvalidDataException("device.ip: 填写规范的生产者 IPv4 地址");
    }
    // Numerically identical ports must share the same physical resource key.
    public static string EndpointIdentity(DeviceBinding binding) => binding.Parameters!["device.ip"] + ":" +
        int.Parse(binding.Parameters["device.port"], System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture);
    public async Task StartControlledAsync(string registeredEpoch, Func<Observation, bool> receive, Action<DeviceHealthConfirmation> onHealth, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registeredEpoch);
        if (sequenced && !SequencedBarcodeProtocol.Token(registeredEpoch)) throw new ArgumentException("Sequenced epoch must be a new canonical token", nameof(registeredEpoch));
        await lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (client is not null || reading is not null || physicalLease is not null) throw new InvalidOperationException("Stop the original connection before reconnecting");
            if (registeredEpoch == priorRegistration) throw new InvalidOperationException("Reconnect requires a new registered epoch");
            priorRegistration = registeredEpoch;
            lock (gate)
            {
                Epoch = registeredEpoch; sequence = 0; failed = false; LastFailure = null; health = onHealth;
                protocol = sequenced ? new(binding.Parameters!, registeredEpoch) : null;
                connectionAttempts++;
                localEndpoint = remoteEndpoint = failureCategory = socketError = null;
                receivedAt = validObservationAt = null; phase = "Connect";
                client = new TcpClient(AddressFamily.InterNetwork); stop = new(); Confirm(DeviceHealth.Connecting);
            }
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(5));
            var connecting = false;
            try
            {
                if (BarcodeProducerConfiguration.CameraLeaseKey(binding) is { } leaseKey) physicalLease = acquireLease(leaseKey);
                connecting = true;
                ConfigureKeepalive(client.Client);
                await client.ConnectAsync(IPAddress.Parse(binding.Parameters!["device.ip"]), int.Parse(binding.Parameters["device.port"], System.Globalization.CultureInfo.InvariantCulture), budget.Token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                lock (gate)
                {
                    localEndpoint = client.Client.LocalEndPoint?.ToString(); remoteEndpoint = client.Client.RemoteEndPoint?.ToString();
                    connections++; connectionOpen = true; connectedTicks = Stopwatch.GetTimestamp();
                    phase = sequenced ? "AwaitHello" : "Receive";
                    if (!sequenced) ConfirmAlive();
                }
                var stream = client.GetStream(); var receiveToken = stop.Token;
                if (protocol is not null) await Send(stream, protocol.Open, budget.Token).ConfigureAwait(false);
                reading = Task.Run(() => Read(stream, receive, registeredEpoch, receiveToken), CancellationToken.None);
                liveness = Monitor(registeredEpoch, receiveToken);
            }
            catch (Exception error)
            {
                Fault(error, !connecting ? "LeaseFailure" : error is OperationCanceledException ? token.IsCancellationRequested ? "ConnectCancelled" : "ConnectTimeout" : "ConnectFailure");
                client?.Dispose(); client = null; stop?.Dispose(); stop = null;
                if (!connecting) throw;
                if (error is OperationCanceledException && !token.IsCancellationRequested)
                    throw new ObservationConnectionException("ConnectTimeout", error);
                if (error is SocketException or IOException)
                    throw new ObservationConnectionException("ConnectFailure", error);
                throw;
            }
        }
        finally { lifecycle.Release(); }
    }
    async Task Read(NetworkStream stream, Func<Observation, bool> receive, string registration, CancellationToken token)
    {
        var buffer = new byte[1024]; var line = new List<byte>(4097);
        var category = "ReceiveFailure";
        try
        {
            while (Current(registration))
            {
                category = "ReceiveFailure";
                var count = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                if (!Current(registration)) return;
                if (count == 0)
                {
                    category = line.Count == 0 ? "RemoteClosed" : "IncompleteFrame";
                    throw new EndOfStreamException();
                }
                lock (gate)
                {
                    if (failed || Epoch != registration || token.IsCancellationRequested) return;
                    receivedAt = DateTimeOffset.UtcNow;
                    if (!sequenced) ConfirmAlive();
                }
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
                    Observation? observation = null;
                    string? reply = null;
                    lock (gate)
                    {
                        if (failed || Epoch != registration || token.IsCancellationRequested) return;
                        if (protocol is not null)
                        {
                            CheckDeadline();
                            var message = protocol.Accept(code);
                            sequence = protocol.Sequence; reply = message.Reply;
                            if (reply is not null) phase = "AwaitReady";
                            if (message.Ready) { phase = "Streaming"; ConfirmAlive(); }
                            if (message.Heartbeat)
                            {
                                maximumHeartbeatDelayMs = Math.Max(maximumHeartbeatDelayMs, Stopwatch.GetElapsedTime(confirmedTicks).TotalMilliseconds);
                                heartbeats++; ConfirmAlive();
                            }
                            if (message.Codes is not null)
                            {
                                validObservationAt = DateTimeOffset.UtcNow;
                                var source = DeviceId + "." + protocol.BootId + "." + protocol.Sequence;
                                var entity = Wire.Encode(new { DeviceId, protocol.BootId, protocol.Trigger });
                                observation = new(Wire.Id(), DeviceId, registration, source, sequence,
                                    "tcp." + Wire.Hash(entity)[..40].ToLowerInvariant(),
                                    BarcodeProducerConfiguration.BusinessEnabled(binding) ? "Read" : "UncorrelatedRead",
                                    message.Codes, null, validObservationAt.Value);
                            }
                        }
                        else
                        {
                            if (code.Contains('\uFEFF')) { category = "InvalidUtf8"; throw new InvalidDataException(); }
                            validObservationAt = DateTimeOffset.UtcNow;
                            var n = ++sequence; var source = registration + "." + n;
                            observation = new(Wire.Id(), DeviceId, registration, source, n, "tcp." + Wire.Hash(source)[..40].ToLowerInvariant(),
                                BarcodeProducerConfiguration.BusinessEnabled(binding) ? "Read" : "UncorrelatedRead", [code], null, validObservationAt.Value);
                        }
                    }
                    if (reply is not null)
                    {
                        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
                        budget.CancelAfter(handshakeTimeout);
                        await Send(stream, reply, budget.Token).ConfigureAwait(false);
                    }
                    if (observation is null) continue;
                    // No driver lock across application code. Stop can fence the
                    // socket and honor cancellation while retaining an in-flight reader.
                    category = "IngressCallbackFailure";
                    if (!Current(registration)) return;
                    if (!receive(observation)) { category = "IngressRejected"; throw new IOException(); }
                    lock (gate) { if (!failed && Epoch == registration && !token.IsCancellationRequested) Confirm(DeviceHealth.Ready); }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
        catch (IOException) when (token.IsCancellationRequested) { }
        catch (ProducerProtocolException error) { Fault(error, error.Category, registration); }
        catch (Exception error) { Fault(error, category, registration); }
    }
    async Task Monitor(string registration, CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                lock (gate)
                {
                    if (failed || Epoch != registration || token.IsCancellationRequested) return;
                    // Only connect/received bytes confirm transport activity. A
                    // local timer cannot manufacture a new remote confirmation.
                    try { CheckDeadline(); }
                    catch (ProducerProtocolException error) { Fault(error, error.Category, registration); return; }
                }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { Fault(error, "LocalMonitorFailure", registration); }
    }
    static Task Send(NetworkStream stream, string line, CancellationToken token)
        => stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), token).AsTask();
    void CheckDeadline()
    {
        if (sequenced && protocol?.IsReady != true)
        {
            if (Stopwatch.GetElapsedTime(connectedTicks) >= handshakeTimeout) throw new ProducerProtocolException("HandshakeTimeout");
        }
        else if (Stopwatch.GetElapsedTime(confirmedTicks) >= livenessTimeout)
            throw new ProducerProtocolException(sequenced ? "HeartbeatTimeout" : "LivenessExpired");
    }
    void Confirm(DeviceHealth state, string? publishedEpoch = null)
    {
        var at = state == DeviceHealth.Ready ? confirmedAt : DateTimeOffset.UtcNow;
        var evidence = ConfirmationSource + "; " + Wire.Encode(new
        {
            Phase = phase, Category = failureCategory, SocketError = socketError, Sequence = sequence,
            ConfiguredRemote = EndpointIdentity(binding), Local = localEndpoint, Remote = remoteEndpoint,
            LastReceivedAt = receivedAt, LastValidObservationAt = validObservationAt,
            LastConfirmedAt = confirmedAt, LivenessTimeoutSeconds = livenessTimeout.TotalSeconds,
            CurrentConnectionEpoch = Epoch, Profile = sequenced ? "sequenced" : "plain",
            HandshakePassed = protocol?.IsReady == true,
            BusinessModeDeclared = BarcodeProducerConfiguration.BusinessEnabled(binding),
            Counters = Diagnostics,
            Confirmation = sequenced ? "Validated handshake and producer heartbeat; business admission is separate" :
                "Plain: loss is undetectable / 丢失不可检测; connect/received-byte evidence only; TCP keepalive is not application heartbeat; business admission is separate"
        });
        health?.Invoke(new(DeviceId, publishedEpoch ?? Epoch, state, at, state == DeviceHealth.Ready ? at.Add(livenessTimeout) : null, evidence));
    }
    void Fault(Exception error, string category, string? registration = null)
    {
        lock (gate)
        {
            if (failed || registration is not null && Epoch != registration) return;
            failures[category] = failures.GetValueOrDefault(category) + 1;
            if (sequenced && protocol?.IsReady != true) failures["HandshakeFailure"] = failures.GetValueOrDefault("HandshakeFailure") + 1;
            if (connectionOpen) { disconnections++; connectionOpen = false; }
            failureCategory = category;
            phase = category.StartsWith("Ingress", StringComparison.Ordinal) ? "Ingress" :
                category is "InvalidUtf8" or "EmptyFrame" or "ControlCharacter" or "FrameTooLong" or "IncompleteFrame" ? "Protocol" : phase;
            socketError = (error as SocketException ?? error.InnerException as SocketException)?.SocketErrorCode.ToString();
            // Do not retain callback messages or raw barcode/protocol bytes.
            LastFailure = category + (socketError is null ? "" : ": " + socketError);
            var retired = Epoch;
            failed = true; Epoch = Wire.Id(); stop?.Cancel(); client?.Dispose();
            // Publish one final fault to the application's original registration,
            // while retiring every producer tied to this physical connection.
            Confirm(DeviceHealth.Faulted, retired);
        }
    }
    async Task StopCore(CancellationToken token)
    {
        lock (gate) { phase = "Stopping"; if (connectionOpen) { disconnections++; connectionOpen = false; } stop?.Cancel(); client?.Dispose(); }
        // A cancelled wait retains tasks/CTS/client for a later explicit drain.
        if (reading is not null) await reading.WaitAsync(token).ConfigureAwait(false);
        if (liveness is not null) await liveness.WaitAsync(token).ConfigureAwait(false);
        lock (gate)
        {
            client = null; reading = liveness = null; stop?.Dispose(); stop = null;
            physicalLease?.Dispose(); physicalLease = null; phase = "Stopped";
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
