using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
using Ipc.Product;
namespace Ipc.Assembly;

static class ParcelCli
{
    public static bool Management(string[] args)
    {
        if (!args.Contains("--action")) return false;
        ParcelCapabilityFactory.RegisterContracts();
        var map = args.Chunk(2).ToDictionary(x => x[0], x => x.Length == 2 ? x[1] : throw new ArgumentException("Missing value"));
        if (map.Keys.Any(x => x is not ("--action" or "--mode" or "--file" or "--extension-trust" or "--dependency-files" or "--extension-root" or "--sha256" or "--destination"))) throw new ArgumentException("Unknown extension option");
        var mode = Enum.Parse<RunMode>(map.GetValueOrDefault("--mode", "Simulation"));
        if (mode != RunMode.Simulation) throw new InvalidDataException("Production reference management is blocked");
        if (map["--action"] == "restore")
        {
            ParcelRecovery.Restore(map["--file"], map["--destination"]);
            Console.WriteLine(Wire.Encode(new { Status = "RestoredIsolated", Admission = "Reconcile original external stages before resume" })); return true;
        }
        if (map["--action"] == "extension-check")
        {
            var dependencies = map.TryGetValue("--dependency-files", out var file) ? Wire.Decode<string[]>(File.ReadAllText(file)) : [];
            Console.WriteLine(Wire.Encode(ExtensionBuildCheck.Inspect(map["--file"], map["--extension-trust"], mode, dependencies))); return true;
        }
        var root = map["--extension-root"];
        var store = new ExtensionStore(root, Wire.Decode<ExtensionTrust>(File.ReadAllText(map["--extension-trust"])), mode, Path.Combine(root, "reference-leases"));
        if (map["--action"] == "extension-install") Console.WriteLine(Wire.Encode(store.Install(map["--file"], map["--sha256"])));
        else if (map["--action"] == "extension-inspect") Console.WriteLine(Wire.Encode(ExtensionPackages.Inspect(map["--file"], store.Trust, mode)));
        else throw new ArgumentException("Unsupported parcel extension action");
        return true;
    }
    public static async Task<string> Command(IParcelApplication app, string line)
    {
        if (line == "snapshot") return Wire.Encode(await app.SnapshotAsync().ConfigureAwait(false));
        var c = Wire.Decode<BusinessCommand>(line);
        var result = c.Kind switch
        {
            "EmitReference" => new BusinessReceipt(app.EmitReference(c.Owner!, c.Value!, c.Id) ? "Enqueued" : "Rejected", "Explicit simulation camera observation", c.ExpectedRevision),
            "EmitWeight" => new BusinessReceipt(app.EmitReferenceWeight(Wire.Decode<WeightMeasurement>(c.Value!)) ? "Enqueued" : "Rejected", "Explicit simulation scale observation", c.ExpectedRevision),
            "Reconcile" => await app.ReconcileAsync(c.Owner!).ConfigureAwait(false),
            "Reconnect" => await app.ReconnectAsync().ConfigureAwait(false),
            "StageConfiguration" => await app.StageConfigurationAsync(c.ExpectedRevision, Wire.Decode<ParcelConfiguration>(c.Value!)).ConfigureAwait(false),
            "Backup" => await app.BackupAsync(c.Value!).ConfigureAwait(false),
            _ => await app.CommandAsync(c).ConfigureAwait(false)
        };
        return Wire.Encode(result);
    }
}
