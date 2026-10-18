using Ipc.Application.Contracts;
using Ipc.Reference.Protocol;
namespace Ipc.Drivers;

public sealed class ReferenceImageCamera(CapabilityBinding binding, string applicationDirectory) : IImageCamera
{
    readonly ReferencePeer peer = new(binding, Path.Combine(applicationDirectory, "images"), "image-capture");
    public string InstanceId => binding.InstanceId;
    public Task StartAsync(CancellationToken token) => peer.StartAsync(token);
    public Task<CapabilityHealth> HealthAsync(CancellationToken token) => peer.HealthAsync(token);
    public async Task ReconnectAsync(CancellationToken token) { await peer.StopAsync(token).ConfigureAwait(false); await peer.StartAsync(token).ConfigureAwait(false); }
    public Task<StageReply> CaptureAsync(FrozenRequest request, CancellationToken token) => peer.SubmitAsync(request, token);
    public Task<StageReply?> QueryAsync(FrozenRequest original, CancellationToken token) => peer.QueryAsync(original, token);
    public Task StopAsync(CancellationToken token) => peer.StopAsync(token);
    public ValueTask DisposeAsync() => peer.DisposeAsync();
}
