using Ipc.Application.Contracts;
using Ipc.Platform;
using Ipc.Reference.Protocol;
namespace Ipc.Components;

public sealed class ReferenceImageUploader(CapabilityBinding binding, string peerRoot) : IImageUploader
{
    readonly ReferencePeer peer = new(binding, peerRoot, "image-upload");
    public string InstanceId => binding.InstanceId;
    public Task StartAsync(CancellationToken token) => peer.StartAsync(token);
    public Task<CapabilityHealth> HealthAsync(CancellationToken token) => peer.HealthAsync(token);
    public async Task ReconnectAsync(CancellationToken token) { await peer.StopAsync(token).ConfigureAwait(false); await peer.StartAsync(token).ConfigureAwait(false); }
    public Task<StageReply> UploadAsync(FrozenRequest request, ReadOnlyMemory<byte> content, CancellationToken token) => peer.SubmitAsync(request, token, content);
    public Task<StageReply?> QueryAsync(FrozenRequest original, CancellationToken token) => peer.QueryAsync(original, token);
    public Task StopAsync(CancellationToken token) => peer.StopAsync(token);
    public ValueTask DisposeAsync() => peer.DisposeAsync();
}
// Public test identities/proof only. Never a substitute for a production identity provider.
public sealed class ReferenceOperatorAuthenticator(CapabilityBinding binding) : IOperatorAuthenticator
{
    bool running;
    public string InstanceId => binding.InstanceId;
    public Task StartAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); running = true; return Task.CompletedTask; }
    public Task<CapabilityHealth> HealthAsync(CancellationToken token)
    { token.ThrowIfCancellationRequested(); var now = DateTimeOffset.UtcNow; return Task.FromResult(new CapabilityHealth(InstanceId, running, now, now.AddSeconds(3), "SIMULATION identity provider")); }
    public Task<OperatorIdentity?> AuthenticateAsync(string subject, string proof, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (!running) throw new IOException("Reference authenticator is stopped");
        if (binding.Parameters.GetValueOrDefault("reply-mode") == "unavailable") throw new IOException("SIMULATION authentication unavailable");
        return Task.FromResult<OperatorIdentity?>(proof == "SIMULATION_ONLY" && binding.Parameters["subjects"].Split(',').Contains(subject)
            ? new(subject, subject, binding.Target, Wire.Id(), DateTimeOffset.UtcNow, true, DateTimeOffset.UtcNow.AddHours(8), subject == "maintenance" ? ["maintenance"] : []) : null);
    }
    public Task ReconnectAsync(CancellationToken token) => StartAsync(token);
    public Task StopAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); running = false; return Task.CompletedTask; }
    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None).ConfigureAwait(false);
}
