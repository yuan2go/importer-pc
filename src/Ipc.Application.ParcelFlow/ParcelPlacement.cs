using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    internal string[] ReadReleasedPlacements()
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT DISTINCT o.id FROM parcel_audit a, json_each(a.detail,'$.OperationIds') i JOIN parcel_operations o ON o.id=i.value WHERE a.event='StationPlacementReleased' UNION SELECT operation FROM sm_parcel_rounds WHERE active=0 AND operation IS NOT NULL";
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read()) ids.Add(reader.GetString(0));
        return ids.ToArray();
    }

    internal bool PlacementWasReleased(string operation)
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM parcel_audit a, json_each(a.detail,'$.OperationIds') i WHERE a.event='StationPlacementReleased' AND i.value=$operation) OR EXISTS(SELECT 1 FROM sm_parcel_rounds WHERE operation=$operation AND active=0)";
        command.Parameters.AddWithValue("$operation", operation);
        return (long)command.ExecuteScalar()! != 0;
    }
}

// Physical confirmation ends current-round ownership; original execution is separate.
public sealed partial class ParcelRuntime
{
    readonly Dictionary<string, DateTimeOffset> confirmedEmptyTrays = [];
    readonly HashSet<string> releasedPlacements = new(StringComparer.Ordinal);
    string? PlacementRemovalOperation(string position, ParcelOperation? latest, ParcelOperation? displayed)
    {
        var round = CurrentRound(position);
        return Id == "sportmaster" && SessionValid && round is not null && !ManagementWorkspaceOpen && !configurationSaving && !stopping.IsCancellationRequested &&
            (round.RequireRemoval || !SmVisualReliable(position)) ? round.Id + ":" + round.Version : null;
    }
    bool ConfirmPlacementRemoval(BusinessCommand command)
    {
        if (Id != "sportmaster" || CurrentRound(command.Owner ?? "") is not { } round ||
            PlacementRemovalOperation(round.PositionId, null, null) != command.Value) return false;
        EndSmRound(round, "OperatorConfirmedRemoval", physicallyRemoved: true, command: command); return true;
    }
}
