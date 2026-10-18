using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    public IdentityConflict[] LoadConflicts()
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT body FROM parcel_identity_conflicts WHERE acknowledged=0";
        using var r = c.ExecuteReader(); List<IdentityConflict> result = [];
        while (r.Read()) result.Add(Wire.Decode<IdentityConflict>(r.GetString(0))); return result.ToArray();
    }
}
public sealed partial class ParcelRuntime
{
    readonly Dictionary<string, IdentityConflict> identityConflicts = [];
    IdentityConflict[] IdentityConflicts() => identityConflicts.Values.ToArray();
    bool HasIdentityConflict(string position) => identityConflicts.Values.Any(c => c.Positions.Contains(position));
    void RecordIdentityConflict(ParcelOperation original, string position, string detail)
    {
        var fact = new IdentityConflict(Wire.Id(), original.Id, new[] { original.PositionId ?? "main", position }.Distinct().ToArray(), detail, DateTimeOffset.UtcNow);
        repository!.Commit(null, "IdentityConflict", Wire.Encode(fact), identityConflict: fact);
        identityConflicts.Add(fact.Id, fact);
    }
}
