using Ipc.Inspection;
using Ipc.Platform;
using Ipc.Product;
using Microsoft.Extensions.Logging.Abstractions;

static class RuntimeEntryBoundaryVerification
{
    public static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ipc-identity-boundary-" + Wire.Id());
        foreach (var business in new[] { "supply", "inspection" })
        {
            var directory = Path.Combine(root, business); Directory.CreateDirectory(directory);
            var names = business == "supply" ? new[] { "product.json", "station.json", "presentation.json" } : new[] { "business.json" };
            foreach (var name in names) File.WriteAllText(Path.Combine(directory, name), "unsupported original input");
            var originals = names.ToDictionary(n => n, n => File.ReadAllBytes(Path.Combine(directory, n)));
            var data = Path.Combine(directory, "data");
            var denial = new UnauthorizedAccessException("Synthetic identity refusal before any configuration read");
            if (business == "supply")
            {
                var options = new BootOptions(Path.Combine(directory, names[0]), Path.Combine(directory, names[1]), Path.Combine(directory, names[2]), data);
                await using var runtime = new ProductRuntime(options, NullLogger<ProductRuntime>.Instance,
                    (_, _) => throw new Exception("Denied Supply identity reached a device factory"));
                RequireDefault(runtime.RuntimeIdentityPreflight);
                await using var denied = new ProductRuntime(options, NullLogger<ProductRuntime>.Instance,
                    (_, _) => throw new Exception("Denied Supply identity reached a device factory")) { RuntimeIdentityPreflight = () => throw denial };
                await denied.StartAsync(default);
                if (denied.Application is not null || denied.Configuration is not null || denied.DataDirectory is not null || denied.HeldResources.Length != 0 ||
                    !denied.Diagnostics.Contains("Startup NotReady: UnauthorizedAccessException: " + denial.Message))
                    throw new Exception("Supply did not preserve its identity refusal as NotReady before initialization");
            }
            else
            {
                await using var runtime = new InspectionRuntime(new(directory, data, null, null));
                RequireDefault(runtime.RuntimeIdentityPreflight);
                await using var denied = new InspectionRuntime(new(directory, data, null, null)) { RuntimeIdentityPreflight = () => throw denial };
                try { await denied.StartAsync(default); throw new Exception("Denied Inspection identity started"); }
                catch (UnauthorizedAccessException error) when (ReferenceEquals(error, denial)) { }
            }
            if (Directory.Exists(data) || Directory.GetFileSystemEntries(directory).Length != originals.Count ||
                originals.Any(p => !File.ReadAllBytes(Path.Combine(directory, p.Key)).SequenceEqual(p.Value)))
                throw new Exception("Identity denial changed original input or created runtime files");
            Console.WriteLine("PASS IDENTITY-ENTRY " + business + ": actual StartAsync denial precedes malformed input, storage, leases and devices; originals retained after disposal");
        }
    }

    static void RequireDefault(Action check)
    {
        if (check != (Action)MachineRuntimePolicy.RequireRuntimeIdentity)
            throw new Exception("Production runtime identity policy was replaced");
    }
}
