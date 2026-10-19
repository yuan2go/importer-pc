using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    public void RecoverLegacyConflicts(string[] positions)
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT seq,at,detail FROM parcel_audit WHERE event='IdentityConflict' ORDER BY seq";
        List<(long Seq, string At, string Detail)> legacy = [];
        using (var r = c.ExecuteReader()) while (r.Read()) legacy.Add((r.GetInt64(0),r.GetString(1),r.GetString(2)));
        foreach (var row in legacy)
        {
            string? recordedId = null;
            try { using var json = System.Text.Json.JsonDocument.Parse(row.Detail); if (json.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && json.RootElement.TryGetProperty("Id", out var id)) recordedId = id.GetString(); }
            catch (System.Text.Json.JsonException) { }
            var legacyId = Wire.Hash("legacy-identity-conflict:" + row.Seq + ":" + row.At + ":" + row.Detail)[..32].ToLowerInvariant();
            using var exists = db.CreateCommand(); exists.CommandText = "SELECT count(*) FROM parcel_identity_conflicts WHERE id=$id OR id=$legacy";
            exists.Parameters.AddWithValue("$id", recordedId ?? ""); exists.Parameters.AddWithValue("$legacy", legacyId);
            if ((long)exists.ExecuteScalar()! != 0)
            {
                // Earlier readers used declaration roles (left/right) even when the
                // persisted legacy configuration actually runs main. Preserve the ID
                // and old body in the same transaction that expands its protection.
                using var read = db.CreateCommand(); read.CommandText = "SELECT body FROM parcel_identity_conflicts WHERE id=$id AND acknowledged=0";
                read.Parameters.AddWithValue("$id",legacyId);
                if (read.ExecuteScalar() is string body)
                {
                    var prior = Wire.Decode<IdentityConflict>(body);
                    var scope = prior.Positions.Concat(positions).Distinct(StringComparer.Ordinal).ToArray();
                    if (!scope.SequenceEqual(prior.Positions))
                    {
                        var expanded = prior with { Positions = scope };
                        Commit(null,"LegacyIdentityConflictScopeExpanded",Wire.Encode(new { OriginalBody = body, Original = prior, Responsibility = expanded }),
                            paused:true,identityConflict:expanded,priorConflictBody:body);
                    }
                }
                continue; // Acknowledged rows retain identity and never re-arm.
            }
            var fact = new IdentityConflict(legacyId, "legacy-audit:" + row.Seq, positions,
                "Legacy conflict scope/entity was not recorded; inspect all possible positions. Original=" + row.Detail, DateTimeOffset.Parse(row.At, System.Globalization.CultureInfo.InvariantCulture));
            Commit(null, "LegacyIdentityConflictRecovered", Wire.Encode(new { OriginalSequence = row.Seq, row.At, row.Detail, Responsibility = fact }), paused: true, identityConflict: fact);
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
    void RecordIdentityConflict(ParcelOperation original, string position, string detail)
        => RecordIdentityConflict(original, new[] { original.PositionId ?? "main", position }.Distinct().ToArray(), detail);
    void RecordIdentityConflict(ParcelOperation original, string[] affectedPositions, string detail)
    {
        var fact = new IdentityConflict(Wire.Id(), original.Id, affectedPositions, detail, DateTimeOffset.UtcNow);
        repository!.Commit(null, "IdentityConflict", Wire.Encode(fact), identityConflict: fact);
        identityConflicts.Add(fact.Id, fact);
    }
}
