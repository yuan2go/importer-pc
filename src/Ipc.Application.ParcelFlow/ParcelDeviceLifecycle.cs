using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> deviceOperations = new();
    readonly HashSet<string> maintenanceOperations = [];
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> portEpochs = new();
    Task<T> PortCallAsync<T>(ICapabilityPort port, Func<Task<T>> action, CancellationToken token) => DeviceKey(port).StartsWith("service:", StringComparison.Ordinal) ? action() : DeviceCallAsync(DeviceKey(port), action, token);
    string DeviceKey(ICapabilityPort port) => ParcelBindings.Bound(Effective).FirstOrDefault(b => b.InstanceId == port.InstanceId && b.Kind is "camera" or "weight" or "plc")?.Target ?? "service:" + port.InstanceId;
    async Task<T> DeviceCallAsync<T>(string identity, Func<Task<T>> action, CancellationToken token)
    {
        var serial = deviceOperations.GetOrAdd(identity, _ => new(1, 1));
        await serial.WaitAsync(token).ConfigureAwait(false);
        try { token.ThrowIfCancellationRequested(); return await action().ConfigureAwait(false); }
        finally { serial.Release(); }
    }
    Task DeviceCallAsync(string identity, Func<Task> action, CancellationToken token) => DeviceCallAsync(identity, async () => { await action().ConfigureAwait(false); return true; }, token);
    async Task<BusinessReceipt> MaintainDevicesAsync(string? deviceId)
    {
        var key = deviceId ?? "*";
        await gate.WaitAsync().ConfigureAwait(false);
        var owns = false;
        try
        {
            if (stopped || stopping.IsCancellationRequested || ports is null || repository?.Paused != true || !MaintenanceAuthorized ||
                maintenanceOperations.Contains("*") || maintenanceOperations.Contains(key) || key == "*" && maintenanceOperations.Count > 0)
                return new("Rejected", "Pause, maintenance authorization and exclusive device maintenance are required", repository?.Revision ?? 0);
            var selectedReaders = readers.Values.Where(r => deviceId is null || r.Source.DeviceId == deviceId).ToArray();
            var selectedPorts = CapabilityPorts().Where(p => deviceId is null || p.InstanceId == deviceId || DeviceKey(p) == deviceId).ToArray();
            if (selectedReaders.Length == 0 && selectedPorts.Length == 0) return new("Rejected", "Unknown device/capability identity", repository.Revision);
            if (externalWaits != 0 && selectedPorts.Any(p => DeviceKey(p).StartsWith("service:", StringComparison.Ordinal)))
                return new("Rejected", "Service maintenance requires all current service calls to drain", repository.Revision);
            foreach (var port in selectedPorts) portEpochs.AddOrUpdate(port.InstanceId, 1, (_, value) => value + 1);
            maintenanceOperations.Add(key); owns = true;
            repository.Commit(null, "MaintenanceStarted", Wire.Encode(new { Device = key, Actor = maintenanceSession.Identity, TimeoutMs = Effective.TimeoutMs }));
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token); budget.CancelAfter(Effective.TimeoutMs);
            try
            {
                await ExternalAsync(async () =>
                {
                    foreach (var port in selectedPorts)
                    {
                        if (selectedReaders.Any(r => r.Source.DeviceId == DeviceKey(port))) continue; // Reader owns the shared physical capture session.
                        await DeviceCallAsync(DeviceKey(port), async () =>
                        {
                            if (port is IWeightSource scale)
                            {
                                scaleEpochs.TryRemove(scale.InstanceId, out _);
                                await scale.StopAsync(budget.Token).ConfigureAwait(false); await scale.StartAsync(budget.Token).ConfigureAwait(false);
                                await RegisterScaleAsync(scale, budget.Token).ConfigureAwait(false);
                            }
                            else await port.ReconnectAsync(budget.Token).ConfigureAwait(false);
                        }, budget.Token).ConfigureAwait(false);
                    }
                    foreach (var reader in selectedReaders)
                        await DeviceCallAsync(reader.Source.DeviceId, () => reader.ReconnectAsync(Enqueue, budget.Token), budget.Token).ConfigureAwait(false);
                    budget.Token.ThrowIfCancellationRequested();
                    return true;
                }).ConfigureAwait(false);
                repository.Commit(null, "DeviceReconnected", "Actor=" + maintenanceSession.Identity!.Subject + "; device=" + key + "; no original request resent");
                return new("Reconnected", "Selected lifecycle completed; admission remains paused", repository.Revision);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
            {
                foreach (var port in selectedPorts) { var now = DateTimeOffset.UtcNow; capabilityHealth[port.InstanceId] = new(port.InstanceId, false, now, now, "Maintenance incomplete: " + ex.GetType().Name); }
                repository.Commit(null, "MaintenanceIncomplete", "Device=" + key + "; " + ex.GetType().Name + "; ownership retained, no successful stop/reconnect asserted");
                return new("MaintenanceIncomplete", "Original resources retained; inspect and explicitly retry maintenance", repository.Revision);
            }
        }
        finally { if (owns) maintenanceOperations.Remove(key); gate.Release(); }
    }
}
