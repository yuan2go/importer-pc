using System.Security.AccessControl;
using System.Security.Principal;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;

static class InstallationConfigurationChecks
{
    public static void Run(string root)
    {
        void Check(bool value, string text) { if (!value) throw new Exception(text); Console.WriteLine("PASS INSTALL-SEED " + text); }
        void Reject(Action action, string text)
        { try { action(); } catch (InvalidDataException) { Check(true, text); return; } throw new Exception(text); }
        Directory.CreateDirectory(root);
        var initial = new SportmasterFlow().InitialConfiguration();
        var config = Path.Combine(root, "missing-machine-configuration");
        var data = Path.Combine(root, "original-data");
        var installed = new DeploymentLocation("sportmaster", new string('a', 40), Path.Combine(root, "old-release"), config, data, "synthetic-account");
        var effective = initial with { StationId = "original-station-17", Wcs = initial.Wcs! with { Parameters = new(initial.Wcs.Parameters) { ["no-dest-to-exp"] = "false" } } };
        var area = Path.Combine(data, effective.Mode.ToString(), effective.StationId, effective.ApplicationId); Directory.CreateDirectory(area);
        var ledger = Path.Combine(area, "configuration.json");
        var pending = effective with { Wcs = effective.Wcs! with { Parameters = new(effective.Wcs.Parameters) { ["no-dest-to-exp"] = "true" } } };
        File.WriteAllText(ledger, Wire.Encode(CurrentConfigurationFixture.Create(19, effective, pending, "Pending", (string?)null)));
        var database = Path.Combine(area, "sportmaster.db"); File.WriteAllText(database, "SYNTHETIC unknown original request; deliberately not a SQLite database");
        var ledgerHash = Wire.FileHash(ledger); var databaseHash = Wire.FileHash(database);
        var prepared = InstallationConfiguration.Prepare(initial, installed, Path.Combine(root, "recovered-seed"));
        var recovered = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(prepared, "business.json")));
        Check(Wire.Encode(recovered) == Wire.Encode(effective), "missing registered configuration reconstructs the exact saved effective configuration");
        Check(recovered.StationId == effective.StationId && recovered.Mode == effective.Mode, "reinstall retains the nondefault original station and mode");
        Check(Wire.Encode(recovered) != Wire.Encode(pending), "reconstructing a launch seed never promotes pending configuration");
        Check(Wire.FileHash(ledger) == ledgerHash && Wire.FileHash(database) == databaseHash, "preparation never opens the business database or changes the saved ledger");
        Check(!Directory.Exists(config), "read-only preparation does not recreate the protected machine directory before authorization");
        if (OperatingSystem.IsWindows())
        {
            var acl = new DirectoryInfo(prepared).GetAccessControl();
            var privateAcl = acl.AreAccessRulesProtected;
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.IdentityReference.Value is "S-1-1-0" or "S-1-5-11" or "S-1-5-32-545") privateAcl = false;
            Check(privateAcl, "temporary configuration has a private Windows ACL");
        }
        var withoutRegistration = InstallationConfiguration.Prepare(initial, null, Path.Combine(root, "no-registration"), data);
        Check(Wire.Encode(Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(withoutRegistration, "business.json")))) == Wire.Encode(effective), "lost machine registration still discovers the original site and nondefault RCS settings in the selected data root");
        Check(Wire.FileHash(ledger) == ledgerHash && Wire.FileHash(database) == databaseHash, "registration recovery leaves pending configuration and original requests unchanged");
        Directory.CreateDirectory(config); File.WriteAllText(Path.Combine(config, "retained-other.json"), "SYNTHETIC retained settings");
        _ = InstallationConfiguration.Prepare(initial, installed, Path.Combine(root, "missing-business-file"));
        Check(!File.Exists(Path.Combine(config, "business.json")) && File.ReadAllText(Path.Combine(config, "retained-other.json")).Contains("retained settings"), "a missing business seed in an existing folder leaves every original file untouched");
        File.WriteAllText(Path.Combine(config, "business.json"), Wire.Encode(effective));
        var untouched = Path.Combine(root, "not-created");
        Check(InstallationConfiguration.Prepare(initial, installed, untouched) == config && !Directory.Exists(untouched), "an existing seed is reused without a temporary copy or configuration change");
        File.Delete(Path.Combine(config, "business.json")); File.Delete(ledger);
        var noLedger = InstallationConfiguration.Prepare(initial, installed, Path.Combine(root, "missing-ledger"));
        Check(Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(noLedger, "business.json"))).StationId == effective.StationId && Wire.FileHash(database) == databaseHash, "a missing ledger cannot redirect installation to a new station or overwrite the old database");
        File.WriteAllText(ledger, "{ malformed original configuration"); var malformedHash = Wire.FileHash(ledger);
        Reject(() => InstallationConfiguration.Prepare(initial, installed, Path.Combine(root, "malformed-output")), "an unreadable saved configuration is preserved instead of silently replaced with defaults");
        Check(Wire.FileHash(ledger) == malformedHash && !Directory.Exists(Path.Combine(root, "malformed-output")), "failed preparation preserves the complete original and creates no replacement");
        File.WriteAllText(ledger, Wire.Encode(CurrentConfigurationFixture.Create(19, initial, (ParcelConfiguration?)null, "Effective", (string?)null)));
        Reject(() => InstallationConfiguration.Prepare(initial, installed, Path.Combine(root, "wrong-identity")), "saved configuration with another station identity cannot be copied into this station");
        Directory.CreateDirectory(Path.Combine(data, "Simulation", "second-station", "sportmaster"));
        Reject(() => InstallationConfiguration.Prepare(initial, installed, Path.Combine(root, "ambiguous")), "multiple existing stations cannot be replaced by a guessed default identity");
        var empty = installed with { DataRoot = Path.Combine(root, "no-original-data") };
        var fresh = InstallationConfiguration.Prepare(initial, empty, Path.Combine(root, "fresh-seed"));
        Check(Wire.Encode(Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(fresh, "business.json")))) == Wire.Encode(initial), "a stale registration without retained site data accepts the ordinary first-configuration seed");
    }
}
