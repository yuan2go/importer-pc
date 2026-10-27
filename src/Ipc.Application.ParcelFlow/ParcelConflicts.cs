using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    public void ValidateConflictHistory(string[]? positions = null)
    {
        using var command = db.CreateCommand(); command.CommandText = "SELECT detail FROM parcel_audit WHERE event='IdentityConflict' ORDER BY seq";
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            IdentityConflict fact;
            try { fact = Wire.Decode<IdentityConflict>(rows.GetString(0)); }
            catch (Exception error) when (error is System.Text.Json.JsonException or InvalidDataException)
            { throw new InvalidDataException("Conflict audit lacks current original identity; preserve database without migration", error); }
            if (fact is null || !Guid.TryParseExact(fact.Id, "N", out _) || fact.Positions is not { Length: > 0 } ||
                fact.Positions.Any(string.IsNullOrWhiteSpace) || positions is not null && fact.Positions.Except(positions).Any())
                throw new InvalidDataException("Conflict identity/scope is incomplete; preserve original responsibility");
            using var original = db.CreateCommand(); original.CommandText = "SELECT body FROM parcel_identity_conflicts WHERE id=$id"; original.Parameters.AddWithValue("$id", fact.Id);
            if (original.ExecuteScalar() is not string body || Wire.Encode(Wire.Decode<IdentityConflict>(body)) != Wire.Encode(fact))
                throw new InvalidDataException("Original conflict fact is missing or changed; cannot synthesize responsibility from audit text");
        }
    }
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
    void RecordRetiredConflict(string original, string[] affectedPositions, string detail)
    {
        var fact = new IdentityConflict(Wire.Id(), original, affectedPositions.Distinct(StringComparer.Ordinal).ToArray(), detail, DateTimeOffset.UtcNow);
        repository!.Commit(null, "IdentityConflict", Wire.Encode(fact), identityConflict: fact); identityConflicts.Add(fact.Id, fact);
    }
    void RecordIdentityConflict(ParcelOperation original, string position, string detail)
        => RecordIdentityConflict(original, new[] { original.PositionId ?? "main", position }.Distinct().ToArray(), detail);
    void RecordIdentityConflict(ParcelOperation original, string[] affectedPositions, string detail)
    {
        var fact = new IdentityConflict(Wire.Id(), original.Id, affectedPositions, detail, DateTimeOffset.UtcNow);
        repository!.Commit(null, "IdentityConflict", Wire.Encode(fact), identityConflict: fact);
        identityConflicts.Add(fact.Id, fact);
    }
}
