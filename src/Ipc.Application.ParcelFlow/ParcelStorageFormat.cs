using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    // Historical DB14 storage identity allowlist used by unscoped offline readers.
    internal const string LegacyStationStorageId = "sportmaster";
    internal static IReadOnlyList<string> LegacyStorageIds { get; } =
        Array.AsReadOnly(new[] { LegacyStationStorageId, "direct-plc", "wcs-to-plc" });

    const string UnsupportedStorage = "仅支持当前数据格式，原文件已保留；请用匹配版本核对原记录和永久请求编号，勿清空或覆盖原数据。";
    public static int ReadStorageVersion(string database, string application)
        => ReadStorageVersion(database, application, immutable: false);
    internal static int ReadStorageVersion(string database, FlowDefinition flow, bool immutable = false)
        => ReadStorageVersion(database, flow.Id, immutable, flow);

    internal static int ReadStorageVersion(string database, string application, bool immutable, FlowDefinition? flow = null)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = immutable ? new Uri(Path.GetFullPath(database)).AbsoluteUri + "?immutable=1" : database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
        RequireCurrentStorage(db, application, flow: flow);
        return CurrentStorageVersion;
    }
    static string RequireCurrentStorage(SqliteConnection db, string? application = null, SqliteTransaction? tx = null, FlowDefinition? flow = null)
    {
        try
        {
            using var c = db.CreateCommand(); c.Transaction = tx;
            c.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='platform_migrations'";
            if (Convert.ToInt64(c.ExecuteScalar()) != 1) throw new InvalidDataException(UnsupportedStorage);
            c.CommandText = "SELECT application,version,sha FROM platform_migrations";
            using var r = c.ExecuteReader();
            if (!r.Read()) throw new InvalidDataException(UnsupportedStorage);
            var actual = r.GetString(0);
            var registered = LegacyStorageIds.Contains(actual, StringComparer.Ordinal) ||
                flow is not null && actual == flow.Id && Ipc.Platform.ExtensionSchema.IsId(actual);
            if (!registered || application is not null && actual != application ||
                r.GetInt64(1) != CurrentStorageVersion || r.GetString(2) != Database.BaselineHash(Baseline) || r.Read())
                throw new InvalidDataException(UnsupportedStorage);
            r.Close(); RequireSchema(db, actual, tx);
            ValidateRoundHistory(db, tx);
            return actual;
        }
        catch (Exception ex) when (ex is SqliteException or InvalidCastException or InvalidOperationException or FormatException or System.Text.Json.JsonException)
        { throw new InvalidDataException(UnsupportedStorage, ex); }
    }
}
