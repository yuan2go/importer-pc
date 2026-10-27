using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.DirectPlc;
using Ipc.Platform;
using System.IO.Compression;

static partial class Audit03
{
    public static async Task Maintenance(string source, string evidence)
    {
        workspace = source; root = evidence;
        var standard = Config("direct-plc"); var scale = Scale("maintenance.scale");
        var c = standard with { Scale = scale, Weight = new(WeightSource.Measured, WeightUnit.Kg, 3, MaximumAgeMs: 60000, WaitTimeoutMs: 300000) };
        var run = "maintenance"; var directory = Path.Combine(root, run, "data", "Simulation", c.StationId, c.ApplicationId); var database = Path.Combine(directory, c.ApplicationId + ".db");
        await using (var app = await Open(c, run))
        {
            app.EmitReference("original-entity", "ORIGINAL", "original-event"); await Wait(app, s => s.Operations.FirstOrDefault()?.State == ParcelState.WaitingWeight);
            try { _ = ParcelRepository.InspectMaintenance(database, new DirectPlcFlow()); Check(false, "offline inspection must acquire original owner"); }
            catch (IOException) { Check(true, "offline maintenance cannot race a running application owner"); }
        }
        var hash = Wire.FileHash(database);
        var before = ParcelRepository.InspectMaintenance(database, new DirectPlcFlow());
        Check(!before.ResponsibilitiesSettled && before.Unsettled == 1 && before.DatabaseVersion == 10 && before.DatabaseSha256 == hash && Wire.FileHash(database) == hash,
            "read-only maintenance reports original WaitingWeight and preserves exact database without upgrade or source startup");
        var diagnostic = Path.Combine(root, "diagnostic.zip");
        _ = ParcelRepository.ExportDiagnostics(database, diagnostic, new DirectPlcFlow());
        using (var zip = ZipFile.OpenRead(diagnostic))
        {
            Check(zip.Entries.Single().FullName == "maintenance.json", "diagnostic export contains explicit source metadata, not a hidden database/credential dump");
            using var reader = new StreamReader(zip.Entries.Single().Open());
            var data = Wire.Decode<ParcelMaintenanceInspection>(reader.ReadToEnd());
            Check(data.DatabaseSha256 == hash && data.ConfigurationSha256 == before.ConfigurationSha256, "diagnostic binds original database/configuration fingerprints");
        }
        var registrations = new List<string>(); var backup = Path.Combine(root, "offline.backup.db");
        ParcelRepository.BackupOffline(database, backup, new DirectPlcFlow(), (path, _) => registrations.Add(path));
        Check(registrations.Single() == backup && Wire.FileHash(database) == hash && File.Exists(backup + ".recovery/COMPLETED.json"), "offline backup retains unclosed originals and registers custody before completion, without changing source");
        var restored = Path.Combine(root, "restored", "Simulation", c.StationId, c.ApplicationId);
        ParcelRecovery.Restore(backup, restored);
        var after = ParcelRepository.InspectMaintenance(Path.Combine(restored, c.ApplicationId + ".db"), new DirectPlcFlow());
        Check(after.Unsettled == before.Unsettled && after.Revision == before.Revision && File.Exists(Path.Combine(restored, "RESTORED-ISOLATED.json")), "actual restore preserves the unresolved original and starts isolated");
        try { ParcelRepository.ExportDiagnostics(database, diagnostic, new DirectPlcFlow()); Check(false, "diagnostic output must be immutable"); }
        catch (IOException) { Check(true, "existing diagnostic output cannot be overwritten"); }
        var missing = Path.Combine(root, "missing-root", "direct-plc.db");
        try { _ = ParcelRepository.InspectMaintenance(missing, new DirectPlcFlow()); Check(false, "missing input must not create root"); }
        catch (FileNotFoundException) { Check(!Directory.Exists(Path.GetDirectoryName(missing)), "missing database leaves no alternate empty runtime root"); }
        if (failures != 0) throw new Exception("Audit03 maintenance failures: " + failures);
        Console.WriteLine("PASS A03 MAINTENANCE COMPLETE");
    }
}
