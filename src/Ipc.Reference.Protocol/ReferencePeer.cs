using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Reference.Protocol;

// Explicit synthetic, durable peer protocol. Not a vendor SDK, PLC address map or WCS
// contract. Acceptance is derived from the peer's persisted command ledger, not a stub.
public sealed class ReferencePeer(CapabilityBinding binding, string root, string stage) : IAsyncDisposable
{
    readonly SemaphoreSlim gate = new(1, 1);
    ResourceLease? lease;
    bool running, disposed;
    string DirectoryPath => Path.Combine(root, Wire.Hash(binding.Target));
    public Task StartAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (disposed) throw new ObjectDisposedException(nameof(ReferencePeer));
        if (running) return Task.CompletedTask;
        lease = new(DirectoryPath, ["reference-peer:" + binding.Target]); running = true; return Task.CompletedTask;
    }
    public Task<CapabilityHealth> HealthAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var now = DateTimeOffset.UtcNow;
        return Task.FromResult(new CapabilityHealth(binding.InstanceId, running, now, now.AddSeconds(3), running ? "SIMULATION: durable reference peer" : "Stopped"));
    }
    public async Task<StageReply> SubmitAsync(FrozenRequest request, CancellationToken token)
    {
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
            var reply = new StageReply(Wire.Id(), request.Id, request.PayloadHash, true, true, accepted,
                stage == "plc-submit" ? false : null, slot, "SIMULATION reference peer: durable " + (accepted ? "acceptance" : "rejection") + "; physical completion is independent", DateTimeOffset.UtcNow);
            ExtensionStore.AtomicWrite(path, new PeerEntry(decoded, reply, 1));
            if (binding.Parameters.GetValueOrDefault("reply-mode") == "lose-after-commit")
                throw new IOException("SIMULATION: reply lost after peer committed; reconcile original command");
            return Wire.Decode<StageReply>(Wire.Encode(reply));
        }
        finally { gate.Release(); }
    }
    public async Task<StageReply?> QueryAsync(FrozenRequest request, CancellationToken token)
    {
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
            return Wire.Decode<StageReply>(Wire.Encode(entry.Reply));
        }
        finally { gate.Release(); }
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
    public async ValueTask DisposeAsync() { if (disposed) return; await StopAsync(CancellationToken.None).ConfigureAwait(false); disposed = true; }
    public sealed record PeerEntry(FrozenRequest Request, StageReply Reply, int Executions);
}
