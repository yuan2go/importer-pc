using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    const string UnsupportedStorage = "仅支持当前Parcel 10数据库；原文件保留，请使用全新数据目录。";
    public static int ReadStorageVersion(string database, string application)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
        RequireCurrentStorage(db, application);
        return CurrentStorageVersion;
    }
    static string RequireCurrentStorage(SqliteConnection db, string? application = null, SqliteTransaction? tx = null)
    {
        using var c = db.CreateCommand(); c.Transaction = tx;
        c.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='platform_migrations'";
        if (Convert.ToInt64(c.ExecuteScalar()) != 1) throw new InvalidDataException(UnsupportedStorage);
        c.CommandText = "SELECT application,version,sha FROM platform_migrations";
        using var r = c.ExecuteReader();
        if (!r.Read()) throw new InvalidDataException(UnsupportedStorage);
        var actual = r.GetString(0);
        if (actual is not ("sportmaster" or "direct-plc" or "wcs-to-plc") || application is not null && actual != application ||
            r.GetInt64(1) != CurrentStorageVersion || r.GetString(2) != Database.BaselineHash(Baseline) || r.Read())
            throw new InvalidDataException(UnsupportedStorage);
        return actual;
    }
}
