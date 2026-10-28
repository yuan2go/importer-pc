using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static partial class Program
{
    static void VerifyReaderCatalogBindings(string root, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS READER-CATALOG " + text); }
        var initial = new SportmasterFlow().InitialConfiguration();
        var catalog = new Ipc.Assembly.ParcelCapabilityFactory(new(Path.Combine(root, "config/sportmaster"), Path.GetTempPath(), null, null)).Catalog();
        var sdk = catalog.Providers.Single(p => p.Kind == "observation" && p.Implementation == "hikrobot-mvs-reader");
        var devices = initial.Topology!.Readers.Select((r, i) => r with { Parameters = new(r.Parameters!)
        { ["device.ip"] = "192.0.2." + (11 + i), ["device.serial"] = "SYNTH-" + i, ["entity.mode"] = "observe-only" } }).ToArray();
        var candidate = initial with { Reader = devices[0], Topology = initial.Topology with { Readers = devices } };
        var original = Wire.Encode(candidate);

        CapabilityDraft[][] Editors(ParcelConfiguration c, CapabilityCatalog inventory)
        {
            var startup = new StartupConfigurationDraft(c, inventory);
            // Exercise the actual runtime parameter model without starting runtime,
            // constructing ports, authorizing writes or changing a configuration ledger.
            var service = DispatchProxy.Create<IManagementParcelFixture, ProjectionProbe>();
            var live = new ParcelViewModel(service, "Sportmaster", "合成目录验证");
            typeof(ParcelViewModel).GetMethod("LoadStructuredConfiguration", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(live, [c, inventory]);
            return [startup.Readers, live.ReaderDrafts.Select(r => r.Capability).ToArray()];
        }
        foreach (var readers in Editors(candidate, catalog))
        {
            Check(readers.Length == 2 && readers.All(r => r.Provider?.Implementation == sdk.Implementation && r.Provider.Version == sdk.Version && r.Provider.Package is null),
                "startup and runtime resolve the actual current built-in SDK descriptor without a package or version substitution");
            for (var i = 0; i < readers.Length; i++)
            {
                var binding = readers[i].Build();
                Check(readers[i].Role == (i == 0 ? "left" : "right") && binding.Target == devices[i].DeviceId &&
                    binding.Parameters["device.ip"] == devices[i].Parameters!["device.ip"] && binding.Parameters["device.serial"] == devices[i].Parameters!["device.serial"] &&
                    binding.Parameters["entity.mode"] == "read-result" && binding.Parameters["acquisition.trigger"] == "off" && !binding.Parameters.ContainsKey("device.port"),
                    "SDK device, position and physical identity survive resolution while current fixed reporting defaults apply only to the candidate for " + readers[i].Role);
            }
        }
        foreach (var inventory in new[]
        {
            catalog with { Providers = catalog.Providers.Where(p => p != sdk).ToArray() },
            catalog with { Providers = catalog.Providers.Select(p => p == sdk ? p with { Contract = "UNSUPPORTED" } : p).ToArray() }
        })
            foreach (var readers in Editors(candidate, inventory))
                foreach (var reader in readers)
                {
                    Check(reader.Provider is null, "missing or incompatible SDK provider stays unresolved");
                    var blocked = false; try { reader.Build(); } catch (InvalidOperationException) { blocked = true; }
                    Check(blocked, "unresolved reader cannot build a savable binding");
                }
        var manual = candidate with { Reader = devices[0] with { Driver = "manual", Parameters = null },
            Topology = candidate.Topology with { Readers = devices.Select(r => r with { Driver = "manual", Parameters = null }).ToArray() } };
        Check(Editors(manual, catalog).All(rs => rs.All(r => r.Provider?.Implementation == "manual" && r.Parameters.Count == 0)), "manual readers retain their field-free contract");

        var package = new PackageReference("synthetic.reader", "1.2.3", new string('a', 64));
        var extension = new CapabilityDescriptor("observation", "extension:" + package.Id + "/reader", package.Version, "CONTROLLED-OBSERVATION-v1", [], new(), package, "reader");
        var extDevices = devices.Select((r, i) => r with { Driver = "extension:synthetic." + i, Parameters = null }).ToArray();
        var ext = candidate with { Reader = extDevices[0], Topology = candidate.Topology with { Readers = extDevices },
            Extensions = new([package], devices.Select((r, i) => new ExtensionInstance("synthetic." + i, package.Id, "reader", r.DeviceId, true, [])).ToArray()) };
        var exact = catalog with { Providers = [.. catalog.Providers, extension], Packages = [new(package, [])] };
        Check(Editors(ext, exact).All(rs => rs.All(r => r.Provider?.Package == package)), "extension readers resolve their exact pinned package");
        var changed = exact with { Providers = [.. catalog.Providers, extension with { Version = "1.2.4", Package = package with { Version = "1.2.4" } }] };
        Check(Editors(ext, changed).All(rs => rs.All(r => r.Provider is null)), "another extension version cannot replace the pinned reader");
        Check(Wire.Encode(candidate) == original, "model construction never mutates original configuration bytes");
    }
}
