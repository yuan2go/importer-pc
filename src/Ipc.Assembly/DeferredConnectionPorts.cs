using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Assembly;

// Unconfigured ports own no socket/SDK and can never produce observations or replies.
sealed class DeferredObservationSource(string deviceId) : IControlledObservationSource
{
    public string DeviceId => deviceId;
    public string Epoch { get; private set; } = "";
    public string ConfirmationSource => "configuration-pending";
    public Task StartControlledAsync(string registeredEpoch, Func<Observation, bool> receive, Action<DeviceHealthConfirmation> health, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Epoch = registeredEpoch;
        health(new(DeviceId, Epoch, DeviceHealth.Unknown, DateTimeOffset.UtcNow, null, ConfirmationSource));
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken token) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class DeferredWcsPort(string instanceId) : IWcsPort
{
    public string InstanceId => instanceId;
    public Task StartAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public Task<CapabilityHealth> HealthAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var now = DateTimeOffset.UtcNow;
        return Task.FromResult(new CapabilityHealth(InstanceId, false, now, now.AddSeconds(2), "RCS待配置：请在参数设置完成连接后保存并重启。"));
    }
    public Task ReconnectAsync(CancellationToken token) => throw new InvalidOperationException("RCS尚未配置；请完成参数并受控重启，重连不能启用待配置连接。");
    public Task<StageReply> ResolveSlotAsync(FrozenRequest request, CancellationToken token) => throw new InvalidOperationException("RCS待配置，拒绝发送格口请求。");
    public Task<StageReply?> QueryAsync(FrozenRequest original, CancellationToken token) => throw new InvalidOperationException("RCS待配置，不能核对原请求；请恢复匹配原请求的实现和目标。");
    public Task StopAsync(CancellationToken token) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
