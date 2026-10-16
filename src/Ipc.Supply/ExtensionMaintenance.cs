using Ipc.Platform;

namespace Ipc.Supply;

public sealed partial class SupplyStore
{
    // Pending external responsibility conservatively pins all retained deployment packages.
    // No UI pagination limit is allowed in a dependency/ownership guard.
    public PackageReference[] RequiredExtensionPackages() => Read<ConfigRevision>(PendingCount() != 0
        ? "SELECT json FROM configs" : "SELECT json FROM configs WHERE status IN ('Effective','PendingActivation')")
        .SelectMany(x => x.Content.Extensions?.Packages ?? []).Distinct().ToArray();
    public static PackageReference[] InspectRequiredExtensionPackages(string path)
    {
        using var connection = OpenMaintenance(path, Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly);
        _ = InspectConnection(connection, path);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT count(*) FROM requests WHERE status IN ('Prepared','Dispatching','Unknown')";
        var pending = Convert.ToInt64(command.ExecuteScalar()) != 0;
        command.CommandText = pending ? "SELECT json FROM configs" : "SELECT json FROM configs WHERE status IN ('Effective','PendingActivation')";
        using var reader = command.ExecuteReader(); List<PackageReference> packages = [];
        while (reader.Read()) packages.AddRange(Wire.Decode<ConfigRevision>(reader.GetString(0)).Content.Extensions?.Packages ?? []);
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
