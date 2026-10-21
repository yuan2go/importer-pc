using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    public ConfigurationChangeAudit[] ConfigurationAudit()
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT body FROM parcel_configuration_audit WHERE json_extract(body,'$.Status') IN ('Prepared','PendingRestart') ORDER BY rowid";
        using var r = c.ExecuteReader(); List<ConfigurationChangeAudit> rows = [];
        while (r.Read()) rows.Add(Wire.Decode<ConfigurationChangeAudit>(r.GetString(0))); return rows.ToArray();
    }
}
public sealed partial class ParcelRuntime
{
    public async Task<BusinessReceipt> ValidateConfigurationAsync(ParcelConfiguration candidate)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Administrator.Require();
            if (stopped || configuration is null) return new("NotReady", "Application unavailable", repository?.Revision ?? 0);
            var errors = definition.Validate(candidate); if (errors.Length != 0) return new("Rejected", string.Join("; ", errors), repository!.Revision);
            RequireSameIdentity(Effective, candidate);
            if (Effective.SchemaVersion >= 3 && candidate.SchemaVersion < 3) throw new InvalidDataException("Topology schema flattening forbidden");
            candidate = factory.Normalize(candidate); factory.ValidateStructure(candidate);
            return new("Validated", "Complete candidate validated; no saved revision or activation", repository!.Revision);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException)
        { return new("Rejected", ex.Message, repository?.Revision ?? 0); }
        finally { gate.Release(); }
    }
    void CompleteConfigurationAudit(string outcome)
    {
        foreach (var item in repository!.ConfigurationAudit().Where(x => x.Status is "Prepared" or "PendingRestart"))
        {
            var current = configuration!.Current;
            var effective = Wire.Hash(Wire.Encode(current.Effective)); var pending = current.Pending is null ? null : Wire.Hash(Wire.Encode(current.Pending));
            var status = effective == item.CandidateHash ? outcome : pending == item.CandidateHash ? current.Status : "InterruptedNotApplied";
            var next = item with { Status = status, AfterRevision = current.Revision, Result = Wire.Encode(new { Detail = current.Failure ?? "Core identity and resource startup confirmed; durable envelope inspected", current.Activation }) };
            repository.Commit(null, "ConfigurationAuditRecovered", Wire.Encode(next), configurationAudit: next);
        }
    }
}
