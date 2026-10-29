using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;

static partial class SportmasterRcs
{
    public static async Task BuiltInOnly(string workspace, string root, string[] args)
    {
        var business = typeof(SportmasterFlow).Assembly;
        var launch = new BusinessLaunch(Path.Combine(root, "config"), Path.Combine(root, "data"), null, null);
        await using var factory = new ParcelCapabilityFactory(launch, BusinessCompatibility.Read(business), business);
        var catalog = factory.Catalog();
        Check(string.IsNullOrEmpty(catalog.InventoryDiagnostic) && catalog.Providers.All(p => p.Package is null),
            "built-in catalog does not access external store/trust or return external providers");
        Check(catalog.Providers.Where(p => p.Kind == "observation").Select(p => p.Implementation).Order().SequenceEqual(new[] { "hikrobot-mvs-reader", "manual" }),
            "SM catalog contains only the current MVS reader and explicit manual simulation source");
        var seed = new SportmasterFlow().InitialConfiguration();
        var readers = seed.Topology!.Readers.Select((r, i) => r with { Parameters = i == 0
            ? new() { ["device.ip"] = "192.0.2.11", ["device.serial"] = "SYNTH-MVS-LEFT", ["entity.mode"] = "read-result", ["acquisition.trigger"] = "off" }
            : r.Parameters }).ToArray();
        var binding = Binding("http://127.0.0.1:18848/rcs/sorter/api") with { Implementation = "sportmaster-rcs",
            Parameters = new(Binding("http://127.0.0.1:18848/rcs/sorter/api").Parameters)
            { ["left-station-code"] = "SYNTH-STATION", ["right-station-code"] = "SYNTH-STATION" } };
        var configured = seed with { Reader = readers[0], Topology = seed.Topology with { Readers = readers }, Wcs = binding,
            StationCode = "SYNTH-STATION", DeferredConnections = [readers[1].DeviceId] };
        Check(new SportmasterFlow().Validate(configured).Length == 0, "single-camera built-in candidate satisfies business fields");
        factory.ValidateStructure(configured);
        factory.Validate(configured with { Mode = RunMode.Simulation });
        Check(factory.AdmissionFailure(configured) is not null, "unconstructed Production candidate cannot inherit admission from a structural check");
        var originalFile = Path.Combine(root, "unsupported-reader.json");
        var constructed = 0; factory.Boundary = _ => constructed++;
        foreach (var driver in new[] { "hikrobot-idmvs", "hikrobot-mvs", "tcp-line" })
        foreach (var mode in new[] { RunMode.Production, RunMode.Simulation })
        {
            // Loopback TCP remains a shared transport fixture, never an SM production driver.
            if (driver == "tcp-line" && mode == RunMode.Simulation) continue;
            var replaced = readers.Select(r => r with { Driver = driver }).ToArray();
            var old = configured with { Mode = mode, Reader = replaced[0], Topology = configured.Topology! with { Readers = replaced } };
            ExtensionStore.AtomicWrite(originalFile, old);
            var bytes = File.ReadAllBytes(originalFile);
            try { factory.Create(old); throw new Exception("Unsupported SM reader accepted: " + driver); }
            catch (InvalidDataException e) when (e.Message.Contains("不支持原相机配置", StringComparison.Ordinal))
            { Check(constructed == 0 && File.ReadAllBytes(originalFile).SequenceEqual(bytes), driver + " is rejected before resource construction without changing the input in " + mode); }
        }
        var external = configured with { Wcs = binding with { Implementation = "extension:synthetic.rcs/rcs", Module = "rcs" } };
        try { factory.ValidateStructure(external); throw new Exception("External binding accepted"); }
        catch (InvalidDataException e) when (e.Message.Contains("不支持外置扩展配置", StringComparison.Ordinal))
        { Check(true, "external binding is explicitly rejected before any store or native load"); }
        try { await factory.InstallAsync(Path.Combine(root, "absent.ipcx"), new string('0', 64)); throw new Exception("External installer was entered"); }
        catch (NotSupportedException) { Check(true, "external installation refuses before opening a file or elevating a helper"); }
        try { await factory.ManageAsync("extension-recover", "synthetic"); throw new Exception("External maintenance was entered"); }
        catch (NotSupportedException) { Check(true, "external maintenance refuses before reading machine extension paths"); }
        var badReaders = readers.Select((r, i) => i == 0 ? r with { Parameters = new(r.Parameters!) { ["device.serial"] = "" } } : r).ToArray();
        try { factory.ValidateStructure(configured with { Reader = badReaders[0], Topology = configured.Topology! with { Readers = badReaders } }); throw new Exception("Missing physical serial accepted"); }
        catch (InvalidDataException) { Check(true, "Production physical camera identity remains mandatory"); }
        if (args.Contains("--installed-configuration-file"))
        {
            string Value(string key) => args[Array.IndexOf(args, key) + 1];
            var file = Value("--installed-configuration-file");
            var bytes = File.ReadAllBytes(file);
            var envelope = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(System.Text.Encoding.UTF8.GetString(bytes));
            var candidate = envelope.Pending ?? throw new InvalidOperationException("Expected saved installed candidate");
            var installedLaunch = new BusinessLaunch(Value("--installed-configuration-directory"), Value("--installed-data-root"), null, null);
            await using var installed = new ParcelCapabilityFactory(installedLaunch, BusinessCompatibility.Read(business), business);
            installed.Validate(candidate);
            Check(File.ReadAllBytes(file).SequenceEqual(bytes), "actual saved Production candidate passes full deployment validation without rewriting the ledger or opening a camera");
        }
    }
}
