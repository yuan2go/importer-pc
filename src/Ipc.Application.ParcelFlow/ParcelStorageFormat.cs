using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    const string UnsupportedStorage = "运行要求Parcel 11数据库；已核验的格式10须经安装程序完整备份后升级。其他格式原文件保留，请使用全新数据目录。";
    public static int ReadStorageVersion(string database, string application)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
        RequireCurrentStorage(db, application);
        return CurrentStorageVersion;
    }
    // Only offline upgrade/backup inspection may read the one approved predecessor.
    // Normal runtime and restore still require the current format.
    internal static int ReadMaintenanceStorageVersion(string database, string application)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        db.Open();
        try
        {
            if (application is not ("sportmaster" or "direct-plc" or "wcs-to-plc")) throw new InvalidDataException(UnsupportedStorage);
            if (IsStorage10(db, application)) return 10;
            RequireCurrentStorage(db, application);
            return CurrentStorageVersion;
        }
        catch (Exception ex) when (ex is SqliteException or InvalidCastException or InvalidOperationException or FormatException)
        { throw new InvalidDataException(UnsupportedStorage, ex); }
    }
    static string RequireCurrentStorage(SqliteConnection db, string? application = null, SqliteTransaction? tx = null)
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
            if (actual is not ("sportmaster" or "direct-plc" or "wcs-to-plc") || application is not null && actual != application ||
                r.GetInt64(1) != CurrentStorageVersion || r.GetString(2) != Database.BaselineHash(Baseline) || r.Read())
                throw new InvalidDataException(UnsupportedStorage);
            return actual;
        }
        catch (Exception ex) when (ex is SqliteException or InvalidCastException or InvalidOperationException or FormatException)
        { throw new InvalidDataException(UnsupportedStorage, ex); }
    }
}
