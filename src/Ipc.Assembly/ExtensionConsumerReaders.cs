using Ipc.Application.ParcelFlow;
using Ipc.Platform;
using Ipc.Supply;
using Microsoft.Data.Sqlite;
namespace Ipc.Assembly;

// Product composition selects the existing application-owned read-only reader.
// Neither the registry nor either Application interprets the other's schema.
public static class ExtensionConsumerReaders
{
    public static PackageReference[] Inspect(string database)
    {
        database = Path.GetFullPath(database); RecoveryFiles.RequireOrdinaryAncestors(database);
        if (!File.Exists(database)) throw new InvalidDataException("Registered consumer unavailable; retain packages");
        bool parcel, supply;
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('platform_migrations','schema_history')";
            using var reader = command.ExecuteReader(); HashSet<string> markers = [];
            while (reader.Read()) markers.Add(reader.GetString(0));
            parcel = markers.Contains("platform_migrations"); supply = markers.Contains("schema_history");
        }
        if (parcel == supply) throw new InvalidDataException("Unknown/ambiguous registered consumer format; retain packages and use matching release");
        return parcel ? ParcelRepository.InspectRequiredPackages(database) : SupplyStore.InspectRequiredExtensionPackages(database);
    }
}
