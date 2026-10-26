// Remove only the newly introduced derived schema when constructing an explicitly
// synthetic prior-format fixture. Historical databases and migration checksums stay untouched.
static class SyntheticPriorStorage
{
    // Test-only immutable old-format input. Product constructors never open a historical writer.
    internal static string Create(string directory,string application,int version,Ipc.Application.Contracts.ParcelOperation? operation=null)
    {
        if(version is <1 or >8)throw new ArgumentOutOfRangeException(nameof(version));
        Directory.CreateDirectory(directory);var file=Path.Combine(directory,application+".db");
        if(File.Exists(file))throw new IOException("Synthetic fixture cannot replace an existing database");
        var migrations=(Ipc.Platform.Storage.DatabaseMigration[])typeof(Ipc.Application.ParcelFlow.ParcelRepository)
            .GetField("migrations",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        using var db=Ipc.Platform.Storage.Database.Open(file,true);
        Ipc.Platform.Storage.Database.Migrate(db,application,migrations.Take(version).ToArray());
        if(operation is not null)
        {
            using var command=db.CreateCommand();command.CommandText="INSERT INTO parcel_operations VALUES($id,$admission,$observation,$body)";
            command.Parameters.AddWithValue("$id",operation.Id);command.Parameters.AddWithValue("$admission",operation.AdmissionId);
            command.Parameters.AddWithValue("$observation",operation.ObservationId);command.Parameters.AddWithValue("$body",Ipc.Platform.Wire.Encode(operation));command.ExecuteNonQuery();
        }
        return file;
    }
    internal const string RemoveNine = "DROP TRIGGER IF EXISTS parcel_operations_projection_insert; DROP TRIGGER IF EXISTS parcel_operations_projection_update; DROP TRIGGER IF EXISTS parcel_archives_projection_insert; DROP TRIGGER IF EXISTS parcel_archives_projection_update; DROP TABLE IF EXISTS parcel_history_projection; DROP TABLE IF EXISTS parcel_ingress; DROP TABLE IF EXISTS parcel_retired_operations; DROP TABLE IF EXISTS parcel_retired_measurements; DROP TABLE IF EXISTS parcel_retired_inputs; DROP TABLE IF EXISTS parcel_ingress_audits; DROP TABLE IF EXISTS parcel_retention_runs; DROP INDEX IF EXISTS parcel_audit_owner_at; DROP INDEX IF EXISTS parcel_audit_input_operation; DROP INDEX IF EXISTS parcel_commands_pending; DROP INDEX IF EXISTS parcel_operations_recovery; DROP INDEX IF EXISTS parcel_archives_recovery; ";
}
