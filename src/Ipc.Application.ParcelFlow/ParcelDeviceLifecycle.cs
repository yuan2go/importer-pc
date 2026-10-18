using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> deviceOperations = new();
    readonly HashSet<string> maintenanceOperations = [];
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> maintainedResources = new();
    readonly Dictionary<string, int> resourceCalls = [];
    bool PortMaintenanceClear(ICapabilityPort port) => !maintainedResources.ContainsKey(DeviceKey(port));
    bool PositionMaintenanceClear(string position) => !maintainedResources.ContainsKey(positions[position].ReaderDeviceId) &&
        new[] { Effective.Wcs, Effective.Plc, Effective.Login, ParcelBindings.Scale(Effective, position), ParcelBindings.Camera(Effective, position),
            Effective.Images?.Strategy == UploadStrategy.Attached ? null : Effective.Images?.Uploader }
        .OfType<CapabilityBinding>().All(b => !maintainedResources.ContainsKey(DeviceKey(Capability(b.InstanceId))));
    async Task<T> ExternalPortAsync<T>(ICapabilityPort port, Func<Task<T>> action)
    {
        // Reserve under the aggregate gate before releasing it. Service maintenance
        // cannot pass a drain check between this reservation and the external call.
        var resource = DeviceKey(port);
        if (!PortMaintenanceClear(port)) throw new InvalidOperationException("Selected capability is under maintenance");
        resourceCalls[resource] = resourceCalls.GetValueOrDefault(resource) + 1;
        try { return await ExternalAsync(action).ConfigureAwait(false); }
        finally { if (--resourceCalls[resource] == 0) resourceCalls.Remove(resource); }
    }
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
        string[] resources = [];
        try
        {
            if (stopped || stopping.IsCancellationRequested || ports is null || repository?.Paused != true || !MaintenanceAuthorized ||
                maintenanceOperations.Contains("*") || maintenanceOperations.Contains(key) || key == "*" && maintenanceOperations.Count > 0)
                return new("Rejected", "Pause, maintenance authorization and exclusive device maintenance are required", repository?.Revision ?? 0);
            var selectedReaders = readers.Values.Where(r => deviceId is null || r.Source.DeviceId == deviceId).ToArray();
            var selectedPorts = CapabilityPorts().Where(p => deviceId is null || p.InstanceId == deviceId || DeviceKey(p) == deviceId).ToArray();
            if (selectedReaders.Length == 0 && selectedPorts.Length == 0) return new("Rejected", "Unknown device/capability identity", repository.Revision);
            resources = selectedReaders.Select(r => r.Source.DeviceId).Concat(selectedPorts.Select(DeviceKey)).Distinct().ToArray();
            if (resources.Any(maintainedResources.ContainsKey)) return new("Rejected", "Selected physical device/service is already under maintenance", repository.Revision);
            if (selectedPorts.Any(p => DeviceKey(p).StartsWith("service:", StringComparison.Ordinal) && resourceCalls.GetValueOrDefault(DeviceKey(p)) != 0))
                return new("Rejected", "Selected service calls must drain before maintenance", repository.Revision);
            foreach (var port in selectedPorts) portEpochs.AddOrUpdate(port.InstanceId, 1, (_, value) => value + 1);
            maintenanceOperations.Add(key); owns = true;
            foreach (var resource in resources) maintainedResources.TryAdd(resource, 0);
            foreach (var port in selectedPorts)
                if (capabilityHealth.TryGetValue(port.InstanceId, out var original)) capabilityHealth[port.InstanceId] = original with { Ready = false, Detail = "Maintenance in progress; original confirmation retained" };
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
                return new("Reconnected", "Selected lifecycle completed; software paused=" + repository.Paused + "; current health is required before dependent work", repository.Revision);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
            {
                foreach (var port in selectedPorts) { var now = DateTimeOffset.UtcNow; capabilityHealth[port.InstanceId] = new(port.InstanceId, false, now, now, "Maintenance incomplete: " + ex.GetType().Name); }
                repository.Commit(null, "MaintenanceIncomplete", "Device=" + key + "; " + ex.GetType().Name + "; ownership retained, no successful stop/reconnect asserted");
                return new("MaintenanceIncomplete", "Original resources retained; inspect and explicitly retry maintenance", repository.Revision);
            }
        }
        finally
        {
            if (owns) { foreach (var resource in resources) maintainedResources.TryRemove(resource, out _); maintenanceOperations.Remove(key); }
            gate.Release();
        }
    }
}
