using Ipc.Platform;

namespace Ipc.Supply;

public sealed partial class StationApplication
{
    readonly Queue<OperatorNotification> notifications = new();
    long notificationSequence, notificationOverflow;

    static bool MatchesReview(BlockReason block, ObservationReviewSelection selected) =>
        block.Code == selected.Code && block.Scope == selected.Scope && block.Source == selected.Source &&
        block.SourceEpoch == selected.SourceEpoch && block.EvidenceVersion == selected.EvidenceVersion;

    // Called only after a committed business transition. Current process notifications are
    // not reconstructed from old success records at startup or when another view opens.
    void Adopt(StationState next)
    {
        foreach (var operation in next.Lanes.Values)
        {
            var previous = state.Lanes.Values.FirstOrDefault(x => x.Id == operation.Id);
            var changed = previous is null || previous.Status != operation.Status ||
                operation.Status is OperationStatus.ValidationRejected or OperationStatus.Rejected && previous.Revision != operation.Revision;
            if (!changed || operation.Status is not (OperationStatus.Accepted or OperationStatus.Rejected or OperationStatus.ValidationRejected or OperationStatus.SplitRequired)) continue;
            var cue = new OperatorNotification(++notificationSequence, operation.Id, operation.Lane, operation.Status, operation.Revision);
            if (notifications.Count < 64) notifications.Enqueue(cue);
            else
            {
                notificationOverflow++;
                // Never silently discard an actionable cue. Its business fact is already in
                // SQLite; block new work and require diagnosis instead of claiming playback.
                Latch("OperatorNotificationBacklog", $"Cue backlog exceeded; unplayed={notificationOverflow}; operation={operation.Id}");
            }
        }
        state = next;
    }
    public Task<OperatorNotification?> PeekNotificationAsync() => owner.Invoke(() =>
        Task.FromResult(notifications.TryPeek(out var cue) ? cue : null), waitForCapacity: false);
    public Task AcknowledgeNotificationAsync(long sequence) => owner.Invoke(() =>
    {
        if (!notifications.TryPeek(out var cue) || cue.Sequence != sequence) throw new GuardException("Notification owner/sequence changed");
        notifications.Dequeue(); return Task.CompletedTask;
    }, waitForCapacity: true);

    public Task RegisterDeviceEpochAsync(string deviceId, string expectedPreviousEpoch, string epoch, string source) =>
        owner.Invoke(() => RegisterDeviceEpochCore(deviceId, expectedPreviousEpoch, epoch, source), waitForCapacity: true);
    async Task RegisterDeviceEpochCore(string deviceId, string expectedPreviousEpoch, string epoch, string source)
    {
        await gate.WaitAsync();
        try
        {
            var binding = config.Devices.Single(x => x.DeviceId == deviceId);
            var previous = state.Devices[binding.Role];
            Require(previous.Epoch == expectedPreviousEpoch && !string.IsNullOrWhiteSpace(epoch) && epoch != previous.Epoch && !string.IsNullOrWhiteSpace(source), "Connection epoch registration conflict");
            var next = Clone(state);
            next.Devices[binding.Role] = new(deviceId, epoch, 0, DeviceHealth.Connecting, Now, Source: source, Invalidity: "AwaitingConfirmation");
            next.Revision++;
            store.Commit(next, "RegisterDeviceEpoch", deviceId, "runtime", $"previous={previous.Epoch}; epoch={epoch}; source={source}");
            Adopt(next); SyncAlarms();
        }
        catch (Exception ex) when (IsStorage(ex)) { Latch("StorageFailure", ex.Message); throw; }
        finally { gate.Release(); }
    }
    public Task SetDeviceHealthAsync(DeviceHealthConfirmation confirmation) =>
        owner.Invoke(() => SetDeviceHealthCore(confirmation), waitForCapacity: true);
    async Task SetDeviceHealthCore(DeviceHealthConfirmation confirmation)
    {
        await gate.WaitAsync();
        try
        {
            var binding = config.Devices.Single(x => x.DeviceId == confirmation.DeviceId);
            var previous = state.Devices[binding.Role];
            if (previous.Epoch != confirmation.Epoch)
            {
                store.Commit(state, "StaleHealthIgnored", confirmation.DeviceId, "driver", "Unregistered/old epoch ignored; no authority changed"); return;
            }
            if (!Enum.IsDefined(confirmation.Health) || confirmation.Source != previous.Source)
            {
                store.Commit(state, "InvalidHealthSourceIgnored", confirmation.DeviceId, "driver", "Source/state does not match the registered connection; no authority changed"); return;
            }
            if (confirmation.ConfirmedAt > Now || confirmation.ValidUntil is { } until && until <= confirmation.ConfirmedAt)
            {
                store.Commit(state, "InvalidHealthTimeIgnored", confirmation.DeviceId, "driver", "Future confirmation or invalid validity interval; no authority changed"); return;
            }
            if (previous.ConfirmedAt is { } last && confirmation.ConfirmedAt <= last)
            {
                // Replaying one confirmation cannot extend freshness or change health.
                // An exact duplicate is harmless; changed equal-time facts remain rejected.
                if (confirmation.ConfirmedAt != last || confirmation.Health != previous.Health || confirmation.ValidUntil != previous.ValidUntil)
                    store.Commit(state, "StaleHealthIgnored", confirmation.DeviceId, "driver", "Non-newer confirmation cannot replace health/validity; no authority changed");
                return;
            }
            var next = Clone(state);
            var current = previous with { Health = confirmation.Health, UpdatedAt = previous.Health == confirmation.Health ? previous.UpdatedAt : confirmation.ConfirmedAt,
                ConfirmedAt = confirmation.ConfirmedAt, ValidUntil = confirmation.ValidUntil, Source = confirmation.Source, Invalidity = null };
            if (current == previous) return;
            next.Devices[binding.Role] = current;
            if (previous.Health != current.Health || previous.IsValid(Now) != current.IsValid(Now)) next.Revision++;
            store.Commit(next, "DeviceHealthConfirmation", confirmation.DeviceId, "driver", Wire.Encode(confirmation));
            Adopt(next); SyncAlarms();
        }
        catch (Exception ex) when (IsStorage(ex)) { Latch("StorageFailure", ex.Message); throw; }
        finally { gate.Release(); }
    }
    // Compatibility for old synthetic callers only. New runtime adapters must register
    // their epoch explicitly and provide source confirmation, never borrow projection time.
    public async Task SetDeviceAsync(string deviceId, string epoch, DeviceHealth health)
    {
        Require(config.Mode == RunMode.Simulation, "Legacy device health is SIMULATION only");
        await owner.Invoke(async () =>
        {
            var binding = config.Devices.Single(x => x.DeviceId == deviceId);
            if (state.Devices[binding.Role].Epoch == "not-connected")
                await RegisterDeviceEpochCore(deviceId, "not-connected", epoch, "SIMULATION:legacy-health");
            await SetDeviceHealthCore(new(deviceId, epoch, health, Now, Now.AddSeconds(5), "SIMULATION:legacy-health"));
        }, waitForCapacity: true);
    }
    public Task SetWcsHealthAsync(bool available) => owner.Invoke(() => SetWcsHealthCore(available), waitForCapacity: true);
    async Task SetWcsHealthCore(bool available)
    {
        await gate.WaitAsync();
        try
        {
            var next = Clone(state); var previous = state.IntegrationHealth; var service = state.IntegrationServiceHealth;
            var typed = config.Extensions?.Ports?.Any(x => x.Port == "requests") == true;
            var wasValid = typed ? service?.IsValid(Now) == true : previous?.IsValid(Now) == true;
            if (typed)
            {
                var status = available ? ServiceStatus.Ready : ServiceStatus.Unavailable;
                if (service is not null && service.Status == status && service.ConfirmedAt > Now.AddSeconds(-1) && wasValid == available) return;
                next.IntegrationHealth = null;
                next.IntegrationServiceHealth = new(config.Integration.Id, config.Integration.Revision, status, Now, Now.AddMilliseconds(config.TimeoutMs + 3000), "integration:" + config.Integration.Id);
            }
            else
            {
                var health = available ? DeviceHealth.Ready : DeviceHealth.Disconnected;
                if (previous is not null && previous.Health == health && previous.ConfirmedAt > Now.AddSeconds(-1) && wasValid == available) return;
                next.IntegrationServiceHealth = null;
                next.IntegrationHealth = new(config.Integration.Id, config.Integration.Revision, 0, health,
                    previous is not null && previous.Health == health ? previous.UpdatedAt : Now, Now, Now.AddMilliseconds(config.TimeoutMs + 3000), "SIMULATION:loopback-health");
            }
            var had = next.Blocks.Any(x => x.Code == "WcsUnavailable" && x.Source == "transport");
            next.Blocks.RemoveAll(x => x.Code == "WcsUnavailable" && x.Source == "transport");
            if (!available) SetBlock(next, "WcsUnavailable", "station", "transport", config.Integration.Revision, "transport-health");
            if (had == available || wasValid != available) next.Revision++;
            store.Commit(next, "IntegrationHealth", config.StationId, "transport", available.ToString());
            Adopt(next); SyncAlarms();
        }
        catch (Exception ex) when (IsStorage(ex)) { Latch("StorageFailure", ex.Message); }
        finally { gate.Release(); }
    }
    public Task<HistoryPage> QueryHistoryAsync(HistoryQuery query) => owner.Invoke(async () =>
    {
        await gate.WaitAsync();
        try { RequireIdentity(state); return store.QueryHistory(query); }
        finally { gate.Release(); }
    }, waitForCapacity: false);
    public Task<SessionHistoryDetail> SessionDetailsAsync(string sessionId) => owner.Invoke(async () =>
    {
        await gate.WaitAsync();
        try { RequireIdentity(state); return store.SessionHistory(sessionId); }
        finally { gate.Release(); }
    }, waitForCapacity: false);
}
