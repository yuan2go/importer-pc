using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    readonly HashSet<string> obsoleteSmOriginals = new(StringComparer.Ordinal);

    void ClassifyIncompatibleOriginals()
    {
        foreach (var op in operations.Values)
        {
            if (IsSettled(op) || !op.ImplementationHashes.Any(original =>
                ports!.ImplementationHashes.GetValueOrDefault(original.Key) != original.Value)) continue;

            if (Id == "sportmaster" && op.FrozenConfiguration is { Images: null, Plc: null } &&
                op.Capture is null && op.Upload is null && op.Plc is null &&
                (op.Wcs?.Request.Binding ?? op.FrozenConfiguration.Wcs) is
                    { Package: null, Implementation: "sportmaster-rcs", Contract: RcsRequestPolicy.Contract })
            {
                // Locally recovered final results still belong to history. An
                // obsolete unfinished request can neither be sent nor queried.
                if (!MainTerminated(op) && op.Wcs?.State is not (StageState.Accepted or StageState.Rejected or StageState.Cancelled))
                    obsoleteSmOriginals.Add(op.Id);
                continue;
            }

            if (RequiresOriginalExecution(op))
                throw new InvalidDataException("OriginalCapabilityUnavailable: settle unresolved original using the matching release before changing host/implementation; no source started: " + op.Id);
        }

        if (obsoleteSmOriginals.Count == 0) return;
        WriteRuntimeLog("供包状态", "新版启动已识别无法继续的旧取格口请求", new
        {
            OperationIds = obsoleteSmOriginals.ToArray(),
            Policy = "NoReplay; retire after placement ends"
        }, "警告");
        RetireObsoleteSmOriginals();
    }

    void RetireObsoleteSmOriginals()
    {
        if (obsoleteSmOriginals.Count == 0) return;
        var ready = obsoleteSmOriginals.Where(id => releasedPlacements.Contains(id) || IsDebugRequest(operations[id])).ToArray();
        if (ready.Length == 0) return;
        repository!.RetireIncompatibleSmOriginals(ready.Select(id => operations[id]).ToArray());
        foreach (var id in ready)
        {
            admissionPositions.TryRemove(operations[id].AdmissionId, out _);
            operations.Remove(id);
            obsoleteSmOriginals.Remove(id);
        }
        WriteRuntimeLog("供包状态", "已清理新版无法处理的旧取格口请求", new { OperationIds = ready }, "警告");
    }
}

public sealed partial class ParcelRepository
{
    const string IncompatibleSmOriginalRun = "sm-incompatible-rcs-original";

    internal void RetireIncompatibleSmOriginals(ParcelOperation[] originals)
    {
        using (var tx = db.BeginTransaction())
        {
            foreach (var op in originals)
            {
                RetainOperationStatistics(op, tx);
                using var c = db.CreateCommand();
                c.Transaction = tx;
                c.Parameters.AddWithValue("$id", op.Id);
                c.Parameters.AddWithValue("$hash", Wire.Hash(Wire.Encode(op)));
                c.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
                c.Parameters.AddWithValue("$run", IncompatibleSmOriginalRun);
                // Keep identity and numeric request ownership fences. Retire
                // receipt spools before deleting files so startup can resume cleanup.
                c.CommandText = """
                    INSERT INTO parcel_retired_operations VALUES($id,$hash,$at,$run);
                    UPDATE parcel_receipts SET state='Retired' WHERE operation=$id;
                    DELETE FROM parcel_archive_versions WHERE operation=$id;
                    DELETE FROM parcel_audit WHERE owner=$id;
                    DELETE FROM parcel_history_projection WHERE id=$id;
                    DELETE FROM parcel_operations WHERE id=$id;
                    DELETE FROM parcel_archives WHERE id=$id;
                    INSERT INTO parcel_audit(at,event,owner,detail) VALUES($at,'IncompatibleSmOriginalRemoved','runtime',$id);
                    """;
                c.ExecuteNonQuery();
            }
            using var revision = db.CreateCommand();
            revision.Transaction = tx;
            revision.CommandText = "UPDATE parcel_meta SET revision=revision+1 WHERE id=1";
            revision.ExecuteNonQuery();
            tx.Commit();
            Revision++;
        }
        CleanupIncompatibleSmReceipts();
    }

    internal void CleanupIncompatibleSmReceipts()
    {
        List<(string Id, string? Hash)> retired = [];
        using (var select = db.CreateCommand())
        {
            select.CommandText = """
                SELECT r.id,r.raw_hash FROM parcel_receipts r
                JOIN parcel_retired_operations t ON t.id=r.operation WHERE r.state='Retired' AND t.run=$run
                """;
            select.Parameters.AddWithValue("$run", IncompatibleSmOriginalRun);
            using var rows = select.ExecuteReader();
            while (rows.Read()) retired.Add((rows.GetString(0), rows.IsDBNull(1) ? null : rows.GetString(1)));
        }
        foreach (var (id, hash) in retired)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid retired receipt identity");
            var file = Path.Combine(rawRoot, id + ".json");
            if (File.Exists(file))
            {
                RecoveryFiles.RequireOrdinaryAncestors(file);
                if (hash is not null && Wire.FileHash(file) != hash) throw new InvalidDataException("Retired receipt bytes changed");
                File.Delete(file);
            }
            using var tx = db.BeginTransaction();
            using var c = db.CreateCommand();
            c.Transaction = tx;
            c.Parameters.AddWithValue("$id", id);
            c.CommandText = """
                DELETE FROM parcel_receipt_custody WHERE occurrence=$id;
                DELETE FROM parcel_archived_receipts WHERE id=$id;
                DELETE FROM parcel_receipts WHERE id=$id AND state='Retired';
                """;
            c.ExecuteNonQuery();
            tx.Commit();
        }
    }
}
