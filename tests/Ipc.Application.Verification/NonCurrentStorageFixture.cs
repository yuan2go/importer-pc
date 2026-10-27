using Microsoft.Data.Sqlite;
using Ipc.Platform;
static class NonCurrentStorageFixture
{
    internal static string Create(string directory, string application, int version, Ipc.Application.Contracts.ParcelOperation? operation = null)
    {
        Directory.CreateDirectory(directory); var file = Path.Combine(directory, application + ".db");
        if (File.Exists(file)) throw new IOException("Fresh synthetic input required");
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString()); db.Open();
        using var c = db.CreateCommand();
        c.CommandText = "CREATE TABLE platform_migrations(application TEXT,version INTEGER,sha TEXT); INSERT INTO platform_migrations VALUES($app,$v,'synthetic-noncurrent'); CREATE TABLE parcel_operations(id TEXT,admission TEXT,observation TEXT,body TEXT);";
        c.Parameters.AddWithValue("$app", application); c.Parameters.AddWithValue("$v", version); c.ExecuteNonQuery();
        if (operation is not null)
        {
            c.CommandText = "INSERT INTO parcel_operations VALUES($id,$a,$o,$b)"; c.Parameters.Clear();
            c.Parameters.AddWithValue("$id", operation.Id); c.Parameters.AddWithValue("$a", operation.AdmissionId);
            c.Parameters.AddWithValue("$o", operation.ObservationId); c.Parameters.AddWithValue("$b", Wire.Encode(operation)); c.ExecuteNonQuery();
        }
        return file;
    }
    internal static long Version(string file)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
        using var c = db.CreateCommand(); c.CommandText = "SELECT max(version) FROM platform_migrations"; return Convert.ToInt64(c.ExecuteScalar());
    }
}
