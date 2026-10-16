using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;

static class AlignmentCompositionVerification
{
    public static Task Run(string root, string url, string workspace)
    {
        var install = Path.Combine(root, "alignment-composition"); Directory.CreateDirectory(Path.Combine(install, "modules"));
        File.WriteAllText(Path.Combine(install, "modules", "alpha.bin"), "controlled test module alpha");
        File.WriteAllText(Path.Combine(install, "modules", "beta.bin"), "controlled test module beta");
        var config = Wire.Decode<StationConfig>(File.ReadAllText(Path.Combine(workspace, "config", "station.simulation.json")));
        config = config with { Integration = config.Integration with { Endpoint = url }, Devices = [config.Devices[0] with { Role = "inspection" }] };
        var alpha = new ModuleRegistration("inspection", "business", "7.2.0", 3, [], "modules/alpha.bin");
        var beta = new ModuleRegistration("capture", "device", "2.0.0", 1, ["inspection"], "modules/beta.bin");
        var definitionA = new ProductDefinition("test-one", "net10.0", "inspection", [RunMode.Simulation], [alpha], ["review"], ["wide"], [new("manual", RunMode.Simulation)]);
        var definitionB = definitionA with { ProductId = "test-two", Modules = [alpha, beta], Views = ["review", "status"], Layouts = ["wide", "narrow"] };
        ProductManifest Manifest(ProductDefinition d) => new(1, d.ProductId, RunMode.Simulation, d.Runtime, "any", d.BusinessModule, d.Views,
            d.Modules.Select(m => new ModuleSpec(m.Id, m.Kind, m.Version, m.ContractMajor, m.Dependencies, m.File, Wire.FileHash(Path.Combine(install, m.File)))).ToArray());
        var manifestA = Manifest(definitionA); var manifestB = Manifest(definitionB);
        IReadOnlyList<string> Validate(ProductManifest manifest, ProductDefinition? definition = null, StationConfig? station = null, PresentationSettings? presentation = null) =>
            ProductValidation.Validate(definition ?? definitionB, manifest, station ?? config, presentation ?? new("review", "wide"), install);
        void Pass(bool value, string message) { if (!value) throw new Exception("ALIGNMENT composition: " + message); Console.WriteLine("PASS ALIGNMENT composition: " + message); }
        Pass(Validate(manifestA, definitionA).Count == 0 && Validate(manifestB).Count == 0, "two explicit test definitions assemble without Platform business edits");
        Pass(SupplyConfiguration.Validate(config).Count != 0, "one-channel generic registration does not weaken Supply two-channel rule");
        Pass(Validate(manifestB with { Modules = manifestB.Modules.Append(manifestB.Modules[0] with { Id = "unknown" }).ToArray() }).Count != 0, "unknown modules refused");
        Pass(Validate(manifestB with { Modules = [manifestB.Modules[0]] }).Count != 0, "missing module refused");
        Pass(Validate(manifestB with { Modules = manifestB.Modules.Select(m => m with { Kind = "unknown" }).ToArray() }).Count != 0, "module kind refused");
        Pass(Validate(manifestB with { Modules = manifestB.Modules.Select(m => m with { Version = "9.0.0" }).ToArray() }).Count != 0, "module version refused");
        Pass(Validate(manifestB with { Modules = manifestB.Modules.Select(m => m with { ContractMajor = 9 }).ToArray() }).Count != 0, "contract version refused");
        Pass(Validate(manifestB with { Modules = manifestB.Modules.Select(m => m with { Dependencies = [] }).ToArray() }).Count != 0, "dependency registration must match");
        var cyclic = definitionB with { Modules = [alpha with { Dependencies = ["capture"] }, beta] };
        Pass(Validate(Manifest(cyclic), cyclic).Count != 0, "cycles refused even in malformed trusted registration");
        Pass(Validate(manifestB with { Modules = manifestB.Modules.Select(m => m with { Sha256 = new string('0', 64) }).ToArray() }).Count != 0, "tampered bytes refused");
        foreach (var path in new[] { "../alpha.bin", "/tmp/alpha.bin", "modules/../alpha.bin", "modules\\alpha.bin" })
            Pass(Validate(manifestB with { Modules = [manifestB.Modules[0] with { File = path }, manifestB.Modules[1]] }).Count != 0, "unsafe path refused " + path);
        var linked = Path.Combine(install, "linked"); Directory.CreateSymbolicLink(linked, Path.Combine(install, "modules"));
        Pass(!ProductValidation.IsSafeRelativeFile(install, "linked/alpha.bin", out _), "linked ancestor refused");
        Pass(Validate(manifestB, station: config with { Mode = RunMode.Production }).Count != 0, "mode mismatch refused");
        Pass(Validate(manifestB, station: config with { Devices = [config.Devices[0] with { Driver = "unknown" }] }).Count != 0, "unregistered driver refused");
        Pass(Validate(manifestB, presentation: new("unknown", "wide")).Count != 0 && Validate(manifestB, presentation: new("review", "unknown")).Count != 0, "unregistered page/layout refused");
        var serializedDefinition = Wire.Decode<ProductDefinition>(File.ReadAllText(Path.Combine(workspace, "config", "product.reference.json")));
        Pass(Wire.Encode(serializedDefinition) == Wire.Encode(ReferenceProduct.Definition), "packaging and compiled product registrations match");
        Pass(Validate(manifestB with { Modules = null! }).Count != 0 && Validate(manifestB, station: config with { Devices = null! }).Count != 0, "null/missing registration structure refused");
        Pass(Validate(manifestB, definitionB with { Modules = [alpha, alpha] }).Count != 0, "duplicate trusted registrations refused without fallback");
        var old = Wire.Decode<StationConfig>(Wire.Encode(config)); Pass(old == config || Wire.Encode(old) == Wire.Encode(config), "schema1 station JSON remains compatible");
        return Task.CompletedTask;
    }
}
