using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    internal long NextIntegrationPreparationSequence(IntegrationRequestIdRange range)
    {
        if (range.First <= 0 || range.Last < range.First) throw new InvalidDataException("Invalid numeric request ID range");
        var floor = IntegrationRequestIdFloor();
        if (floor >= range.Last) throw new InvalidDataException($"External numeric request ID range exhausted/retired; durable floor {floor}; allocate a new disjoint range");
        return checked(Math.Max(range.First, checked(floor + 1)) - range.First + 1);
    }
    long IntegrationRequestIdFloor()
    {
        if (StorageVersion < 7) return lastIntegrationRequestId;
        using var c = db.CreateCommand();
        c.CommandText = "SELECT max(value) FROM (SELECT coalesce(max(id),0) value FROM parcel_integration_id_owners UNION ALL SELECT coalesce(max(ceiling),0) value FROM parcel_integration_id_restores)";
        return Math.Max(lastIntegrationRequestId, Convert.ToInt64(c.ExecuteScalar()));
    }
    internal void RegisterIntegrationRequestRanges(IWcsRequestPreparer preparer, IEnumerable<ParcelConfiguration> configurations, string? restoreIdentity)
    {
        if (StorageVersion < 7) throw new InvalidDataException("Numeric range retirement requires the current database reader");
        // Include old resident and archived bindings: a restored backup must never
        // reuse the unobserved tail that the lost source might already have sent.
        var bindings = configurations.Select(c => c.Wcs).OfType<CapabilityBinding>().ToList();
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
            c.CommandText = "INSERT OR IGNORE INTO parcel_integration_id_ranges VALUES($hash,$first,$last)";
            c.Parameters.AddWithValue("$hash", Wire.Hash(Wire.Encode(binding))); c.Parameters.AddWithValue("$first", range.First); c.Parameters.AddWithValue("$last", range.Last); c.ExecuteNonQuery();
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
        if (StorageVersion < 7 || operation.Wcs?.Request.IntegrationRequest is not { } numeric) return;
        var request = operation.Wcs.Request;
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
    void BackfillIntegrationRequestOwners()
    {
        if (StorageVersion < 7) return;
        using var tx = db.BeginTransaction();
        // SQL uniqueness remains permanent, including archived original owners.
        foreach (var operation in UpgradeOperations()) RegisterIntegrationRequestOwner(operation, tx);
        tx.Commit();
    }
}
