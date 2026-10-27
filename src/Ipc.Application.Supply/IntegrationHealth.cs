using Ipc.Platform;

namespace Ipc.Supply;

public sealed partial class StationApplication
{
    public Task SetIntegrationHealthAsync(IntegrationHealthFact[] facts) => owner.Invoke(async () =>
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var next = Clone(state); bool changed = false, guardChanged = false, transition = false;
            foreach (var fact in facts)
            {
                var binding = config.Extensions?.Ports?.SingleOrDefault(x => x.Port == fact.Port);
                if (fact.DeploymentVersion != config.Version || !Enum.IsDefined(fact.Status) || fact.ConfirmedAt > Now || fact.ValidUntil <= fact.ConfirmedAt ||
                    fact.ConfigurationSha256.Length != 64 || string.IsNullOrWhiteSpace(fact.Epoch) || fact.Sequence <= 0 ||
                    (binding is null ? fact.Port != "requests" || fact.Target != config.Integration || config.Extensions?.Services?.Any(x => x.Enabled) == true : binding.InstanceId != fact.InstanceId || binding.Target != fact.Target)) continue;
                var prior = next.ServiceHealth.GetValueOrDefault(fact.Port);
                if (prior is not null && (prior.Epoch != fact.Epoch || prior.ConfigurationSha256 != fact.ConfigurationSha256 || fact.Sequence <= prior.Sequence || fact.ConfirmedAt <= prior.ConfirmedAt)) continue;
                changed = true;
                guardChanged |= prior?.Status != fact.Status || (prior?.ValidUntil > Now) != (fact.ValidUntil > Now);
                transition |= prior is null || prior.Status != fact.Status || prior.Failure != fact.Failure ||
                    (prior.ValidUntil > Now) != (fact.ValidUntil > Now);
                next.ServiceHealth[fact.Port] = fact;
                // requests is Supply's actual dependency. Other ports have no declared
                // business consumer; their failures remain independently visible diagnostics.
                if (fact.Port != "requests") continue;
                // Builtin and extension services publish the same current
                // authority. Do not reclassify a service as a legacy device.
                next.IntegrationHealth = null;
                next.IntegrationServiceHealth = new(fact.Target.Id, fact.Target.Revision, fact.Status, fact.ConfirmedAt, fact.ValidUntil, "integration:" + fact.InstanceId);
                next.Blocks.RemoveAll(x => x.Code == "WcsUnavailable" && x.Source == "transport");
                if (fact.Status != ServiceStatus.Ready) SetBlock(next, "WcsUnavailable", "station", "transport", fact.Target.Revision, "transport-health");
            }
            if (!changed) return;
            if (guardChanged) next.Revision++;
            if (transition) store.Commit(next, "IntegrationInstanceHealth", config.StationId, "integration", Wire.Encode(facts));
            // All accepted sequences and confirmation times still reach the projection.
            // Unchanged renewals are volatile authority, invalidated at every startup.
            Adopt(next); SyncAlarms();
        }
        catch (Exception ex) when (IsStorage(ex)) { Latch("StorageFailure", ex.Message); }
        finally { gate.Release(); }
    }, waitForCapacity: true);
}
