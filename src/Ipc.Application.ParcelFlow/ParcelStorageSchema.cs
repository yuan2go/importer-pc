using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    static string[] StorageSchema(SqliteConnection db, SqliteTransaction? tx = null)
    {
        using var command = db.CreateCommand(); command.Transaction = tx;
        // Only the engine's two optimizer tables are optional. A LIKE prefix
        // would treat '_' as a wildcard and conceal user-defined lookalikes.
        command.CommandText = "SELECT type,name,tbl_name,sql FROM sqlite_schema WHERE sql IS NOT NULL AND NOT(type='table' AND name IN ('sqlite_stat1','sqlite_stat4') AND tbl_name=name) ORDER BY type,name";
        using var rows = command.ExecuteReader(); var result = new List<string>();
        while (rows.Read()) result.Add(Wire.Encode(new[] { rows.GetString(0), rows.GetString(1), rows.GetString(2), rows.GetString(3).Replace("\r\n", "\n", StringComparison.Ordinal) }));
        return result.ToArray();
    }
    static void RequireSchema(SqliteConnection db, string application, SqliteTransaction? tx = null)
    {
        using var expected = new SqliteConnection("Data Source=:memory:;Pooling=False"); expected.Open();
        Database.CreateBaseline(expected, application, CurrentStorageVersion, Baseline);
        if (!StorageSchema(db, tx).SequenceEqual(StorageSchema(expected), StringComparer.Ordinal))
            throw new InvalidDataException("数据库实际结构与获准格式不同；原文件保留，拒绝仅改版本号");
    }
}
