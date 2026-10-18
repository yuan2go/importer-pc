using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Reference.Protocol;

// Explicit synthetic, durable peer protocol. Not a vendor SDK, PLC address map or WCS
// contract. Acceptance is derived from the peer's persisted command ledger, not a stub.
public sealed class ReferencePeer(CapabilityBinding binding, string root, string stage, bool serveLocally = false) : IAsyncDisposable
{
    readonly HttpClient? remote = !serveLocally && binding.Parameters.TryGetValue("endpoint", out var endpoint)
        ? new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = new Uri(endpoint), Timeout = Timeout.InfiniteTimeSpan } : null;
    readonly SemaphoreSlim gate = new(1, 1);
    ResourceLease? lease;
    bool running, disposed;
    string DirectoryPath => Path.Combine(root, Wire.Hash(binding.Target));
    public Task StartAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (disposed) throw new ObjectDisposedException(nameof(ReferencePeer));
        if (running) return Task.CompletedTask;
        if (remote is null)
        {
            lease = new(DirectoryPath, ["reference-peer:" + binding.Target]);
            if (Directory.EnumerateFiles(DirectoryPath, "*.new").Any()) throw new InvalidDataException("Interrupted reference custody write; preserve original files");
        }
        running = true; return Task.CompletedTask;
    }
    public async Task<CapabilityHealth> HealthAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var now = DateTimeOffset.UtcNow;
        var ready = running;
        if (ready && remote is not null)
        {
            try
            {
                // Header and body share the caller's budget; never buffer an unbounded
                // response. Cancellation belongs to the caller that owns that budget.
                using var response = await remote.GetAsync("health", HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                ready = false;
                var marker = "PARCEL-REFERENCE-v1"u8.ToArray();
                if (response.IsSuccessStatusCode && (response.Content.Headers.ContentLength is null || response.Content.Headers.ContentLength <= marker.Length))
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                    var bytes = new byte[marker.Length + 1]; var length = 0;
                    while (length < bytes.Length)
                    {
                        var read = await stream.ReadAsync(bytes.AsMemory(length), token).ConfigureAwait(false);
                        if (read == 0) break;
                        length += read;
                    }
                    ready = bytes.AsSpan(0, length).SequenceEqual(marker);
                }
            }
            catch (HttpRequestException) { ready = false; }
            catch (IOException) { ready = false; }
        }
        token.ThrowIfCancellationRequested(); now = DateTimeOffset.UtcNow;
        return new CapabilityHealth(binding.InstanceId, ready, now, now.AddSeconds(3), ready ? "SIMULATION: durable reference peer" : "Stopped/unavailable");
    }
    public async Task<StageReply> SubmitAsync(FrozenRequest request, CancellationToken token, ReadOnlyMemory<byte> content = default)
    {
        if (remote is not null) return await ExchangeAsync("submit", request, content, token).ConfigureAwait(false) ?? throw new IOException("Missing reference submit reply");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Require(request); var path = Path.Combine(DirectoryPath, request.Id + ".json");
            var wire = Wire.Encode(request); // Same encode/decode path as an external protocol boundary.
            var decoded = Wire.Decode<FrozenRequest>(wire);
            var payload = Wire.Decode<ParcelPayload>(decoded.Payload);
            if (payload.AdmissionId != request.AdmissionId || string.IsNullOrWhiteSpace(payload.ParcelCode) ||
                payload.Weight is { Value: <= 0 } || stage == "plc-submit" && request.ApplicationId == "wcs-to-plc" &&
                (string.IsNullOrWhiteSpace(payload.Slot) || string.IsNullOrWhiteSpace(payload.WcsRequestId))) throw new InvalidDataException("Reference protocol payload is invalid");
            if (File.Exists(path))
            {
                var prior = Wire.Decode<PeerEntry>(File.ReadAllText(path));
                if (Wire.Encode(prior.Request) != wire) throw new InvalidDataException("Reference peer rejects reused identity with changed frozen content");
                return prior.Reply;
            }
            var accepted = binding.Parameters.GetValueOrDefault("decision", "accept") == "accept";
            var slot = stage == "wcs-slot" && accepted ? binding.Parameters.GetValueOrDefault("slot", "REFERENCE-07") : payload.Slot;
            ParcelImage? image = null;
            if (stage == "image-capture" && accepted)
            {
                if (payload.Acquisition is not { } acquisition || acquisition.AdmissionId != request.AdmissionId || acquisition.ObservedAt > request.PreparedAt || string.IsNullOrWhiteSpace(acquisition.SourceEventId))
                    throw new InvalidDataException("Capture requires original source acquisition identity; current/latest frame is forbidden");
                // A valid one-pixel PNG from an explicit synthetic camera, never a site image.
                var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAACklEQVR4nGP4DwABAQEAsTj2FAAAAABJRU5ErkJggg==");
                WriteImage(request.Id, bytes);
                image = new(request.Id, request.AdmissionId, "images/" + Wire.Hash(binding.Target) + "/" + request.Id + ".png", "image/png", bytes.Length,
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
            }
            if (stage == "image-upload" && accepted)
            {
                var original = payload.Image ?? throw new InvalidDataException("Upload requires an original captured image");
                if (original.AdmissionId != request.AdmissionId || content.Length != original.Length || content.Length > 4194304 ||
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content.Span)) != original.Sha256) throw new InvalidDataException("Upload bytes do not match the original image");
                WriteImage(request.Id, content);
                image = original with { UploadedUri = "reference://images/" + request.Id };
            }
            var reply = new StageReply(Wire.Id(), request.Id, request.PayloadHash, true, true, accepted,
                stage == "plc-submit" ? false : null, slot, "SIMULATION reference peer: durable " + (accepted ? "acceptance" : "rejection") + "; physical completion is independent", DateTimeOffset.UtcNow, image);
            ExtensionStore.AtomicWrite(path, new PeerEntry(decoded, reply, 1));
            if (binding.Parameters.GetValueOrDefault("reply-mode") == "lose-after-commit")
                throw new IOException("SIMULATION: reply lost after peer committed; reconcile original command");
            return Wire.Decode<StageReply>(Wire.Encode(reply));
        }
        finally { gate.Release(); }
    }
    public async Task<StageReply?> QueryAsync(FrozenRequest request, CancellationToken token)
    {
        if (remote is not null) return await ExchangeAsync("query", request, default, token).ConfigureAwait(false);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Require(request); var path = Path.Combine(DirectoryPath, request.Id + ".json");
            if (!File.Exists(path)) return null;
            var entry = Wire.Decode<PeerEntry>(File.ReadAllText(path));
            if (Wire.Encode(entry.Request) != Wire.Encode(request)) throw new InvalidDataException("Original reference command identity differs");
            if (stage == "plc-submit" && entry.Reply.BusinessAccepted == true && binding.Parameters.GetValueOrDefault("complete-on-query") == "true" && entry.Reply.PhysicalCompleted != true)
            {
                entry = entry with { Reply = entry.Reply with { Id = Wire.Id(), PhysicalCompleted = true, At = DateTimeOffset.UtcNow,
                    Evidence = "SIMULATION: reference peer reports explicit physical completion; no real equipment operated" } };
                ExtensionStore.AtomicWrite(path, entry);
            }
            if (entry.Reply.Image is { } image && (!File.Exists(Path.Combine(DirectoryPath, request.Id + ".png")) || Wire.FileHash(Path.Combine(DirectoryPath, request.Id + ".png")) != image.Sha256))
                throw new InvalidDataException("Reference image custody missing or changed");
            return Wire.Decode<StageReply>(Wire.Encode(entry.Reply));
        }
        finally { gate.Release(); }
    }
    async Task<StageReply?> ExchangeAsync(string action, FrozenRequest request, ReadOnlyMemory<byte> content, CancellationToken token)
    {
        try { return await ExchangeCoreAsync(action, request, content, token).ConfigureAwait(false); }
        catch (HttpRequestException ex) { throw new IOException("Reference HTTP delivery/outcome unknown", ex); }
    }
    async Task<StageReply?> ExchangeCoreAsync(string action, FrozenRequest request, ReadOnlyMemory<byte> content, CancellationToken token)
    {
        Require(request);
        using var body = new StringContent(Wire.Encode(new ReferenceExchange(request, content.IsEmpty ? null : Convert.ToBase64String(content.Span))), System.Text.Encoding.UTF8, "application/json");
        using var response = await remote!.PostAsync(action, body, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new IOException("Reference HTTP outcome unavailable: " + (int)response.StatusCode);
        if (response.Content.Headers.ContentLength > 65536) throw new InvalidDataException("Reference reply quota exceeded");
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var limited = new MemoryStream(); var buffer = new byte[4096]; int read;
        while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        { if (limited.Length + read > 65536) throw new InvalidDataException("Reference reply quota exceeded"); await limited.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false); }
        var json = System.Text.Encoding.UTF8.GetString(limited.ToArray());
        return json.Trim() == "null" ? null : Wire.Decode<StageReply>(json);
    }
    void WriteImage(string id, ReadOnlyMemory<byte> bytes)
    {
        var path = Path.Combine(DirectoryPath, id + ".png");
        if (File.Exists(path))
        {
            if (Wire.FileHash(path) != Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.Span))) throw new InvalidDataException("Original reference image differs");
            return;
        }
        using (var file = new FileStream(path + ".new", FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { file.Write(bytes.Span); file.Flush(true); }
        File.Move(path + ".new", path);
    }
    void Require(FrozenRequest request)
    {
        if (!running) throw new IOException("Reference peer stopped");
        if (!Guid.TryParseExact(request.Id, "N", out _) || request.Stage != stage || request.PayloadHash != Wire.Hash(request.Payload) ||
            request.Binding.Target != binding.Target || request.Binding.Contract != binding.Contract ||
            Wire.Encode(request.Binding) != Wire.Encode(binding)) throw new InvalidDataException("Reference protocol original target/contract/payload mismatch");
    }
    public async Task StopAsync(CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { running = false; lease?.Dispose(); lease = null; } finally { gate.Release(); }
    }
    public async ValueTask DisposeAsync() { if (disposed) return; await StopAsync(CancellationToken.None).ConfigureAwait(false); remote?.Dispose(); disposed = true; }
    public sealed record PeerEntry(FrozenRequest Request, StageReply Reply, int Executions);
}

public sealed record ReferenceExchange(FrozenRequest Request, string? Content);
