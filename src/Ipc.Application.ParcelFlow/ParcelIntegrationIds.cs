using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

public sealed record IntegrationIdBudget(long First, long Last, long PermanentHighWater, long Remaining, string State);

public sealed partial class ParcelRuntime
{
    IntegrationIdBudget? integrationBudget;
    void RefreshIntegrationBudget()
    {
        integrationBudget = ports?.Wcs is IWcsRequestPreparer preparer && Effective.Wcs is { } binding && preparer.GetRequestIdRange(binding) is { } range
            ? repository!.IntegrationBudget(range) : null;
    }
}

public sealed partial class ParcelRepository
{
    public IntegrationIdBudget IntegrationBudget(IntegrationRequestIdRange range)
    {
        if (range.First <= 0 || range.Last < range.First) throw new InvalidDataException("Invalid numeric request ID range");
        var floor = IntegrationRequestIdFloor();
        var remaining = Math.Max(0, range.Last - Math.Max(floor, range.First - 1));
        var total = range.Last - range.First + 1;
        return new(range.First, range.Last, floor, remaining, remaining == 0 ? "Exhausted" : remaining <= Math.Max(1, total / 10) ? "RenewalRequired" : "Available");
    }
    internal long NextIntegrationPreparationSequence(IntegrationRequestIdRange range)
    {
        if (range.First <= 0 || range.Last < range.First) throw new InvalidDataException("Invalid numeric request ID range");
        var floor = IntegrationRequestIdFloor();
        if (floor >= range.Last) throw new InvalidDataException($"External numeric request ID range exhausted/retired; durable floor {floor}; allocate a new disjoint range");
        return checked(Math.Max(range.First, checked(floor + 1)) - range.First + 1);
    }
    long IntegrationRequestIdFloor()
    {
        using var c = db.CreateCommand();
        c.CommandText = "SELECT max(value) FROM (SELECT coalesce(max(id),0) value FROM parcel_integration_id_owners UNION ALL SELECT coalesce(max(ceiling),0) value FROM parcel_integration_id_restores)";
        return Math.Max(lastIntegrationRequestId, Convert.ToInt64(c.ExecuteScalar()));
    }
    internal void RegisterIntegrationRequestRanges(IWcsRequestPreparer preparer, IEnumerable<ParcelConfiguration> configurations, string? restoreIdentity)
    {
        // Include old resident and archived bindings: a restored backup must never
        // reuse the unobserved tail that the lost source might already have sent.
        // A deferred configuration is an unfinished candidate, not an allocated
        // request range. Its missing parameters must not block later activation.
        // Durable ranges and every resident/archived original below remain intact.
        var bindings = configurations.Where(c => c.Wcs is { } wcs && !ParcelConnectionSetup.IsDeferred(c, wcs.InstanceId))
            .Select(c => c.Wcs).OfType<CapabilityBinding>().ToList();
        using (var c = db.CreateCommand())
        {
            c.CommandText = "SELECT DISTINCT json_extract(body,'$.Wcs.Request.Binding') FROM parcel_operations WHERE json_extract(body,'$.Wcs.Request.IntegrationRequest') IS NOT NULL UNION SELECT DISTINCT json_extract(body,'$.Wcs.Request.Binding') FROM parcel_archives WHERE json_extract(body,'$.Wcs.Request.IntegrationRequest') IS NOT NULL";
            using var r = c.ExecuteReader(); while (r.Read()) bindings.Add(Wire.Decode<CapabilityBinding>(r.GetString(0)));
        }
        using var tx = db.BeginTransaction();
        foreach (var binding in bindings.DistinctBy(b => Wire.Hash(Wire.Encode(b))))
        {
            var range = preparer.GetRequestIdRange(binding);
            if (range is null) continue;
            if (range.First <= 0 || range.Last < range.First) throw new InvalidDataException("Invalid numeric request ID range");
            using var c = db.CreateCommand(); c.Transaction = tx;
            c.CommandText = "SELECT NOT EXISTS(SELECT 1 FROM parcel_integration_id_ranges WHERE first_id=$first AND last_id=$last) AND EXISTS(SELECT 1 FROM parcel_integration_id_ranges WHERE first_id<=$last AND last_id>=$first)";
            c.Parameters.AddWithValue("$first", range.First); c.Parameters.AddWithValue("$last", range.Last);
            if ((long)c.ExecuteScalar()! != 0) throw new InvalidDataException("A newly sized numeric range overlaps a previously registered allocation; obtain a disjoint range, never resize or reuse its tail");
            c.CommandText = "INSERT OR IGNORE INTO parcel_integration_id_ranges VALUES($hash,$first,$last)";
            c.Parameters.AddWithValue("$hash", Wire.Hash(Wire.Encode(binding))); c.ExecuteNonQuery();
            c.CommandText = "SELECT count(*) FROM parcel_integration_id_ranges WHERE binding_hash=$hash AND first_id=$first AND last_id=$last";
            if ((long)c.ExecuteScalar()! != 1) throw new InvalidDataException("Original configured numeric range cannot change meaning");
        }
        if (restoreIdentity is not null)
        {
            using var c = db.CreateCommand(); c.Transaction = tx;
            c.CommandText = "INSERT OR IGNORE INTO parcel_integration_id_restores SELECT $identity,max(value) FROM (SELECT coalesce(max(last_id),0) value FROM parcel_integration_id_ranges UNION ALL SELECT coalesce(max(id),0) value FROM parcel_integration_id_owners UNION ALL SELECT coalesce(max(ceiling),0) value FROM parcel_integration_id_restores)";
            c.Parameters.AddWithValue("$identity", restoreIdentity); c.ExecuteNonQuery();
        }
        tx.Commit();
    }
    void RegisterIntegrationRequestOwner(ParcelOperation operation, SqliteTransaction transaction)
    {
        if (operation.Wcs?.Request.IntegrationRequest is not { } numeric) return;
        var request = operation.Wcs.Request;
        if (numeric.RequestId <= 0 || string.IsNullOrWhiteSpace(request.Id))
            throw new InvalidDataException("Invalid original numeric request ID or request owner; retain original intent");
        using var c = db.CreateCommand(); c.Transaction = transaction;
        c.CommandText = "SELECT operation,request,request_hash FROM parcel_integration_id_owners WHERE id=$id"; c.Parameters.AddWithValue("$id", numeric.RequestId);
        using (var r = c.ExecuteReader())
        {
            if (r.Read())
            {
                if (r.GetString(0) != operation.Id || r.GetString(1) != request.Id || r.GetString(2) != Wire.Hash(Wire.Encode(request)))
                    throw new InvalidDataException("Numeric request ID already belongs to another immutable original intent");
                return;
            }
        }
        c.CommandText = "INSERT INTO parcel_integration_id_owners VALUES($id,$op,$request,$hash)";
        c.Parameters.AddWithValue("$op", operation.Id); c.Parameters.AddWithValue("$request", request.Id); c.Parameters.AddWithValue("$hash", Wire.Hash(Wire.Encode(request))); c.ExecuteNonQuery();
    }
    void ValidateIntegrationRequestOwners()
    {
        long maximum = 0;
        using var command = db.CreateCommand();
        command.CommandText = "SELECT operation,request,request_hash FROM parcel_integration_id_owners WHERE id=$id";
        var id = command.Parameters.Add("$id", SqliteType.Integer);
        foreach (var operation in OriginalOperations())
        {
            if (operation.Wcs?.Request.IntegrationRequest is not { } numeric) continue;
            var request = operation.Wcs.Request;
            if (numeric.RequestId <= 0 || string.IsNullOrWhiteSpace(request.Id))
                throw new InvalidDataException("Invalid original numeric request ID or request owner; retain original intent");
            id.Value = numeric.RequestId;
            using var row = command.ExecuteReader();
            if (!row.Read() || row.GetString(0) != operation.Id || row.GetString(1) != request.Id || row.GetString(2) != Wire.Hash(Wire.Encode(request)))
                throw new InvalidDataException("Original numeric request ownership missing or changed; retain database without backfill");
            maximum = Math.Max(maximum, numeric.RequestId);
        }
        lastIntegrationRequestId = maximum;
    }
}
