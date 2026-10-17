using Ipc.Platform;

namespace Ipc.Supply;

public sealed partial class SupplyStore
{
    // Pending external responsibility conservatively pins all retained deployment packages.
    // No UI pagination limit is allowed in a dependency/ownership guard.
    public PackageReference[] RequiredExtensionPackages() => Read<ConfigRevision>(PendingCount() != 0 || Directory.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "receipt-ingress")) && Directory.EnumerateFiles(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "receipt-ingress")).Any()
        ? "SELECT json FROM configs" : "SELECT json FROM configs WHERE status IN ('Effective','PendingActivation')")
        .SelectMany(x => x.Content.Extensions?.Packages ?? []).Concat(PendingRequests().Where(x => x.Implementation?.Package is not null).Select(x => x.Implementation!.Package!)).Distinct().ToArray();
    public static PackageReference[] InspectRequiredExtensionPackages(string path)
    {
        using var connection = OpenMaintenance(path, Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly);
        var inspection = InspectConnection(connection, path);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT count(*) FROM requests WHERE status IN ('Prepared','Dispatching','Unknown')" +
            (inspection.FormatVersion >= 4 ? " OR id IN (SELECT request FROM response_conflicts) OR id IN (SELECT json_extract(b.value,'$.Source') FROM state,json_each(state.json,'$.Blocks') b WHERE json_extract(b.value,'$.Code')='ReceiptPersistenceUncertain')" : "");
        var pending = Convert.ToInt64(command.ExecuteScalar()) != 0 || inspection.UpgradeBlockers.Contains("ReceiptPersistenceRecoveryRequired");
        command.CommandText = pending ? "SELECT json FROM configs" : "SELECT json FROM configs WHERE status IN ('Effective','PendingActivation')";
        using var reader = command.ExecuteReader(); List<PackageReference> packages = [];
        while (reader.Read()) packages.AddRange(Wire.Decode<ConfigRevision>(reader.GetString(0)).Content.Extensions?.Packages ?? []);
        reader.Close();
        command.CommandText = "SELECT json FROM requests WHERE status IN ('Prepared','Dispatching','Unknown')" +
            (inspection.FormatVersion >= 4 ? " OR id IN (SELECT request FROM response_conflicts) OR id IN (SELECT json_extract(b.value,'$.Source') FROM state,json_each(state.json,'$.Blocks') b WHERE json_extract(b.value,'$.Code')='ReceiptPersistenceUncertain')" : "");
        using var requests = command.ExecuteReader();
        while (requests.Read()) if (Wire.Decode<BusinessRequest>(requests.GetString(0)).Implementation?.Package is { } package) packages.Add(package);
        return packages.Distinct().ToArray();
    }
}

public sealed partial class StationApplication
{
    public Task RecordDeploymentPhaseAsync(string revisionId, string evidence) => owner.Invoke(async () =>
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { store.Commit(state, "DeploymentPhase", revisionId, "runtime", evidence); }
        finally { gate.Release(); }
    }, priority: false, waitForCapacity: true);
    public Task<PackageReference[]> RequiredExtensionPackagesAsync() => owner.Invoke(async () =>
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { RequireIdentity(state); Maintenance(state); return store.RequiredExtensionPackages(); }
        finally { gate.Release(); }
    }, priority: false, waitForCapacity: false);
}
