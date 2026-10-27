using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    public static int ReadStorageVersion(string database, string application)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
        using var c = db.CreateCommand(); c.CommandText = "SELECT application,version,sha FROM platform_migrations ORDER BY version";
        using var r = c.ExecuteReader(); var version = 0;
        while (r.Read())
        {
            version++;
            if (r.GetString(0) != application || version > migrations.Length || r.GetInt64(1) != version || r.GetString(2) != Wire.Hash(migrations[version - 1].Sql))
                throw new InvalidDataException("Unknown/changed original database; matching maintenance reader required");
        }
        if (version is < 2 or > 9) throw new InvalidDataException("This maintenance reader supports exact Parcel DB2/3/4/5/6/7/8/9 only");
        return version;
    }
    public static int RepairLegacyMain(string database, string expectedHash, string backup, FlowDefinition definition, string reason,
        Action<string, ParcelConfiguration>? registerBackup = null) =>
        throw new InvalidDataException("旧格式修复不受支持；保留原数据库、配置和责任原件。");
}
