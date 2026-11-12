using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    static void ValidateRoundHistory(SqliteConnection db, SqliteTransaction? tx = null)
    {
        using var q = db.CreateCommand(); q.Transaction = tx; q.CommandText = "SELECT id,position,active,operation,body,hash FROM sm_parcel_rounds";
        using var rows = q.ExecuteReader();
        while (rows.Read())
        {
            var body = rows.GetString(4); var round = Wire.Decode<SmParcelRound>(body);
            if (Wire.Hash(body) != rows.GetString(5) || round.Id != rows.GetString(0) || round.PositionId != rows.GetString(1) ||
                round.Version < 1 || (round.EndedAt is null) != (rows.GetInt32(2) == 1) ||
                round.OperationId != (rows.IsDBNull(3) ? null : rows.GetString(3)) || round.ReadTimeoutMs is < 100 or > 60000 ||
                !Enum.IsDefined(round.State) || (round.EndedAt is null) == (round.State == SmRoundState.Ended))
                throw new InvalidDataException("Round original identity/hash/state differs; original retained");
        }
        rows.Close(); q.CommandText = "SELECT position,body,hash FROM sm_position_cycles";
        using var cycles = q.ExecuteReader();
        while (cycles.Read())
        {
            var body = cycles.GetString(1); var cycle = Wire.Decode<SmPositionCycle>(body);
            if (Wire.Hash(body) != cycles.GetString(2) || cycle.PositionId != cycles.GetString(0) || !Enum.IsDefined(cycle.Presence) || !Guid.TryParseExact(cycle.Generation, "N", out _) || !Guid.TryParseExact(cycle.RecoveryToken, "N", out _) ||
                cycle.PlacementGeneration is { } placement && !Guid.TryParseExact(placement, "N", out _) ||
                cycle.VehicleNo is { } vehicle && (string.IsNullOrWhiteSpace(vehicle) || vehicle.Length > 256 || vehicle.Any(char.IsControl)) ||
                (cycle.RecoveryCause & ~(SmRemoteRecoveryCause.RequestMayBind | SmRemoteRecoveryCause.MembershipMayApply)) != 0 ||
                cycle.SubmittedOperation is not null && cycle.VehicleNo is null || cycle.HeartbeatPending is <= 0 ||
                cycle.ArrivalAt is not null && (cycle.VehicleNo is null || cycle.ArrivalEvidence is null))
                throw new InvalidDataException("Physical cycle original differs; original retained");
        }
    }
    void WriteRound(SqliteTransaction tx, SmParcelRound? round, SmPositionCycle? cycle)
    {
        if (round is not null)
        {
            using var q = db.CreateCommand(); q.Transaction = tx;
            q.CommandText = "INSERT INTO sm_parcel_rounds VALUES($id,$position,$active,$operation,$at,$body,$hash) ON CONFLICT(id) DO UPDATE SET active=excluded.active,operation=excluded.operation,body=excluded.body,hash=excluded.hash WHERE sm_parcel_rounds.position=excluded.position AND json_extract(sm_parcel_rounds.body,'$.Version')=$prior";
            var body = Wire.Encode(round);
            q.Parameters.AddWithValue("$id", round.Id); q.Parameters.AddWithValue("$position", round.PositionId);
            q.Parameters.AddWithValue("$active", round.EndedAt is null ? 1 : 0); q.Parameters.AddWithValue("$operation", (object?)round.OperationId ?? DBNull.Value);
            q.Parameters.AddWithValue("$at", round.StartedAt.ToString("O")); q.Parameters.AddWithValue("$body", body); q.Parameters.AddWithValue("$hash", Wire.Hash(body));
            q.Parameters.AddWithValue("$prior", round.Version - 1);
            if (q.ExecuteNonQuery() != 1) throw new InvalidDataException("Round revision or position changed");
        }
        if (cycle is not null)
        {
            using var q = db.CreateCommand(); q.Transaction = tx;
            q.CommandText = "INSERT INTO sm_position_cycles VALUES($position,$body,$hash) ON CONFLICT(position) DO UPDATE SET body=excluded.body,hash=excluded.hash";
            var body = Wire.Encode(cycle);
            q.Parameters.AddWithValue("$position", cycle.PositionId); q.Parameters.AddWithValue("$body", body); q.Parameters.AddWithValue("$hash", Wire.Hash(body)); q.ExecuteNonQuery();
        }
    }
    public SmParcelRound[] Rounds(bool currentOnly = false, string? position = null, int limit = 100, string? search = null, DateTimeOffset? from = null, DateTimeOffset? to = null)
        => ReadRounds(db, currentOnly, position, limit, search, from, to);
    internal static SmParcelRound[] ReadRoundHistory(string database, string? position, int limit, string? search, DateTimeOffset? from, DateTimeOffset? to, CancellationToken token)
    {
        RecoveryFiles.RequireOrdinaryAncestors(database);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open(); token.ThrowIfCancellationRequested();
        RequireCurrentStorage(connection);
        SQLitePCL.raw.sqlite3_progress_handler(connection.Handle, 1000, _ => token.IsCancellationRequested ? 1 : 0, null);
        return ReadRounds(connection, false, position, limit, search, from, to);
    }
    static SmParcelRound[] ReadRounds(SqliteConnection db, bool currentOnly, string? position, int limit, string? search, DateTimeOffset? from, DateTimeOffset? to)
    {
        using var q = db.CreateCommand();
        var conditions = new List<string>();
        if (currentOnly) conditions.Add("active=1");
        if (position is not null) { conditions.Add("position=$position"); q.Parameters.AddWithValue("$position", position); }
        if (!string.IsNullOrEmpty(search))
        {
            conditions.Add("(id=$search OR json_extract(body,'$.SelectedCode')=$search OR json_extract(body,'$.RequestId')=$search OR json_extract(body,'$.ProtocolRequestId')=$number OR json_extract(body,'$.RcsOutcome')=$search OR json_extract(body,'$.EndReason')=$search OR json_extract(body,'$.ReadOutcome')=$search OR ($search='Unknown' AND json_extract(body,'$.RcsOutcome')='Conflict') OR EXISTS(SELECT 1 FROM json_each(sm_parcel_rounds.body,'$.FormalCodes') WHERE value=$search))");
            q.Parameters.AddWithValue("$search", search); q.Parameters.AddWithValue("$number", long.TryParse(search, out var number) ? number : -1);
        }
        if (from is not null) { conditions.Add("started>=$from"); q.Parameters.AddWithValue("$from", from.Value.ToUniversalTime().ToString("O")); }
        if (to is not null) { conditions.Add("started<$to"); q.Parameters.AddWithValue("$to", to.Value.ToUniversalTime().ToString("O")); }
        q.CommandText = "SELECT id,position,body,hash FROM sm_parcel_rounds" + (conditions.Count == 0 ? "" : " WHERE " + string.Join(" AND ", conditions)) + " ORDER BY started DESC,id DESC LIMIT $limit";
        q.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
        using var r = q.ExecuteReader(); List<SmParcelRound> result = [];
        while (r.Read())
        {
            var body = r.GetString(2); var round = Wire.Decode<SmParcelRound>(body);
            if (Wire.Hash(body) != r.GetString(3) || round.Id != r.GetString(0) || round.PositionId != r.GetString(1) || round.Version < 1)
                throw new InvalidDataException("Round identity/hash differs; original retained");
            result.Add(round);
        }
        return result.ToArray();
    }
    public SmParcelRound? Round(string id)
    {
        using var q = db.CreateCommand(); q.CommandText = "SELECT body,hash FROM sm_parcel_rounds WHERE id=$id"; q.Parameters.AddWithValue("$id", id);
        using var r = q.ExecuteReader(); if (!r.Read()) return null;
        var body = r.GetString(0); var round = Wire.Decode<SmParcelRound>(body);
        if (Wire.Hash(body) != r.GetString(1) || round.Id != id) throw new InvalidDataException("Round original differs");
        return round;
    }
    public SmPositionCycle? Cycle(string position)
    {
        using var q = db.CreateCommand(); q.CommandText = "SELECT body,hash FROM sm_position_cycles WHERE position=$position"; q.Parameters.AddWithValue("$position", position);
        using var r = q.ExecuteReader(); if (!r.Read()) return null;
        var body = r.GetString(0); var cycle = Wire.Decode<SmPositionCycle>(body);
        if (Wire.Hash(body) != r.GetString(1) || cycle.PositionId != position) throw new InvalidDataException("Physical cycle original differs");
        return cycle;
    }
    internal SmParcelRound[] PendingRoundImages()
    {
        using var q = db.CreateCommand(); q.CommandText = "SELECT body FROM sm_parcel_rounds WHERE json_extract(body,'$.OriginalFrame.State')='Pending' LIMIT 64";
        using var r = q.ExecuteReader(); List<SmParcelRound> rounds = []; while (r.Read()) rounds.Add(Wire.Decode<SmParcelRound>(r.GetString(0))); return rounds.ToArray();
    }
    // Only stopped retention calls this after the complete backup is verified.
    // Keep identities/outcomes; retire large evidence only after original custody.
    void RetireSmRoundEvidence(DateTimeOffset cutoff, Action authority)
    {
        while (true)
        {
            authority(); List<string> ids = [];
            using (var q = db.CreateCommand())
            {
                q.CommandText = "SELECT id FROM sm_parcel_rounds WHERE active=0 AND julianday(json_extract(body,'$.EndedAt'))<julianday($cutoff) AND (operation IS NULL OR EXISTS(SELECT 1 FROM parcel_retired_operations t WHERE t.id=operation)) AND (json_extract(body,'$.Observation') IS NOT NULL OR json_extract(body,'$.OriginalFrame.State') IN ('Saved','Failed','Interrupted')) LIMIT 128";
                q.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));
                using var r = q.ExecuteReader(); while (r.Read()) ids.Add(r.GetString(0));
            }
            if (ids.Count == 0) return;
            foreach (var id in ids)
            {
                authority(); var round = Round(id)!;
                var compact = round with { Version = round.Version + 1, Observation = null,
                    OriginalFrame = round.OriginalFrame is { } frame ? frame with { State = "Retired", Failure = "Evidence retained in verified complete retention backup" } : null };
                Commit(null, "RoundEvidenceRetired", Wire.Encode(new { RoundId = id, OriginalHash = Wire.Hash(Wire.Encode(round)) }), round: compact);
            }
        }
    }
}
