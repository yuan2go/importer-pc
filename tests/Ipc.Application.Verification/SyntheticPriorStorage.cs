// Remove only the newly introduced derived schema when constructing an explicitly
// synthetic prior-format fixture. Historical databases and migration checksums stay untouched.
static class SyntheticPriorStorage
{
    internal const string RemoveNine = "DROP TRIGGER IF EXISTS parcel_operations_projection_insert; DROP TRIGGER IF EXISTS parcel_operations_projection_update; DROP TRIGGER IF EXISTS parcel_archives_projection_insert; DROP TRIGGER IF EXISTS parcel_archives_projection_update; DROP TABLE IF EXISTS parcel_history_projection; DROP TABLE IF EXISTS parcel_ingress; DROP TABLE IF EXISTS parcel_retired_operations; DROP TABLE IF EXISTS parcel_retired_measurements; DROP TABLE IF EXISTS parcel_retired_inputs; DROP TABLE IF EXISTS parcel_ingress_audits; DROP TABLE IF EXISTS parcel_retention_runs; DROP INDEX IF EXISTS parcel_audit_owner_at; DROP INDEX IF EXISTS parcel_audit_input_operation; DROP INDEX IF EXISTS parcel_commands_pending; DROP INDEX IF EXISTS parcel_operations_recovery; DROP INDEX IF EXISTS parcel_archives_recovery; ";
}
