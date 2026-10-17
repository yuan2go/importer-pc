using Ipc.Platform;
namespace Ipc.Supply;

public static class ConfigurationActivation
{
    public static Guard Evaluate(StationState state, ConfigRevision? selected, string effectiveVersion, bool unresolved, DateTimeOffset now)
    {
        List<string> reasons = [];
        if (state.Actor is not { Role: SimulationRole.Maintenance } actor || actor.ExpiresAt <= now) reasons.Add("UnexpiredMaintenanceIdentityRequired");
        if (selected is null) reasons.Add("SelectConfigurationRevision");
        else if (selected.Status != "Validated" || selected.BaseVersion != effectiveVersion) reasons.Add("StaleOrInvalidConfigurationRevision");
        if (state.Stopping) reasons.Add("Stopping");
        if (unresolved) reasons.Add("PendingExternalResponsibility");
        if (state.Session is { Status: not (SessionStatus.Closed or SessionStatus.Rejected) }) reasons.Add("CloseCurrentBoxBeforeActivation");
        if (state.Quarantined || state.Lanes.Values.Any(x => x.Occupancy != Occupancy.Clear) || state.Blocks.Any(x => x.Code is "LocalDispositionRequired" or "ConflictingEvidence")) reasons.Add("EntityOrRecoveryResponsibility");
        return new(reasons.Count == 0, reasons.ToArray());
    }
}
