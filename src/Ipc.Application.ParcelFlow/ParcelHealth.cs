using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    void ReaderHealthChanged(DeviceHealthConfirmation h) => Diagnostic("相机通信", "相机连接状态变化",
        h.Health == DeviceHealth.Faulted ? "警告" : "信息",
        Wire.Encode(new { Device = h.DeviceId, Position = positions.Values.SingleOrDefault(p => p.ReaderDeviceId == h.DeviceId)?.Id,
            h.Epoch, h.Health, h.ConfirmedAt, h.ValidUntil, Evidence = h.Source }));
    async Task MonitorAsync(ICapabilityPort port)
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                await Task.Delay(100, stopping.Token).ConfigureAwait(false);
                // Test scheduling seam before reservation; production always follows the same fence.
                if (BeforeMonitorProbe is { } schedule) await schedule(port.InstanceId, stopping.Token).ConfigureAwait(false);
                await gate.WaitAsync(stopping.Token).ConfigureAwait(false);
                try
                {
                    if (!PortMaintenanceClear(port)) continue;
                    var epoch = portEpochs.GetValueOrDefault(port.InstanceId);
                    var health = await ExternalPortAsync(port, () => ProbeHealthAsync(port, stopping.Token)).ConfigureAwait(false);
                    if (epoch == portEpochs.GetValueOrDefault(port.InstanceId))
                    {
                        if (!capabilityHealth.TryGetValue(port.InstanceId, out var previous) || previous.Ready != health.Ready)
                            Diagnostic("设备通信", health.Ready ? "设备或服务已就绪" : "设备或服务不可用", health.Ready ? "信息" : "警告", port.InstanceId + ": " + health.Detail);
                        capabilityHealth[port.InstanceId] = health;
                    }
                }
                finally { gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception ex) { Diagnostic("异常", "设备监测异常", "错误", ex.ToString()); failure = "Capability monitor failed: " + port.InstanceId + ": " + ex.Message; Interlocked.Exchange(ref accepting, 0); }
    }
    // Only this boundary knows both the configured budget and the lifecycle token.
    // A port receives one token and must not guess why that token was cancelled.
    async Task<CapabilityHealth> ProbeHealthAsync(ICapabilityPort port, CancellationToken caller)
    {
        var epoch = portEpochs.GetValueOrDefault(port.InstanceId);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(caller, stopping.Token);
        budget.CancelAfter(Effective.TimeoutMs);
        try
        {
            var result = await PortCallAsync(port, () => port.HealthAsync(budget.Token), budget.Token).ConfigureAwait(false);
            caller.ThrowIfCancellationRequested(); stopping.Token.ThrowIfCancellationRequested();
            // A provider can return a cached success after its request budget elapsed.
            if (epoch != portEpochs.GetValueOrDefault(port.InstanceId)) return Unavailable("Old maintenance epoch health discarded");
            if (budget.IsCancellationRequested) return Unavailable("Health check timed out");
            var now = DateTimeOffset.UtcNow;
            if (result.InstanceId != port.InstanceId || result.ConfirmedAt > now || result.ValidUntil <= result.ConfirmedAt)
                return Unavailable("Health identity/time invalid for original port " + port.InstanceId);
            return result;
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested && !stopping.IsCancellationRequested && budget.IsCancellationRequested)
        { return Unavailable("Health check timed out"); }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            caller.ThrowIfCancellationRequested(); stopping.Token.ThrowIfCancellationRequested();
            return Unavailable("Health unavailable: " + ex.GetType().Name);
        }
        CapabilityHealth Unavailable(string reason)
        { var now = DateTimeOffset.UtcNow; return new(port.InstanceId, false, now, now, reason); }
    }
}
