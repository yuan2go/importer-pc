using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Application.DirectPlc;
using Ipc.Application.WcsToPlc;
using Ipc.Assembly;
using Ipc.Platform;

static class VersionCompatibility
{
    public static async Task Run(string workspace, string root)
    {
        void Pass(bool condition, string why) { if (!condition) throw new Exception("FAIL VC01: " + why); Console.WriteLine("PASS VC01: " + why); }
        void Reject(Action action, string why)
        { try { action(); } catch (InvalidDataException) { Pass(true, why); return; } throw new Exception("FAIL VC01 accepted: " + why); }
        FlowDefinition[] flows = [new SportmasterFlow(), new DirectPlcFlow(), new WcsToPlcFlow()];
        foreach (var flow in flows)
        {
            var assembly = flow.GetType().Assembly;
            var policy = BusinessCompatibility.Read(assembly);
            Pass(policy.ApplicationId == flow.Id && BusinessCompatibility.VersionOf(assembly) == "1.3.2", flow.Id + " compiled business version and embedded declaration");
            var configDirectory = Path.Combine(root, flow.Id, "config"); Directory.CreateDirectory(configDirectory);
            var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config", flow.Id, "business.json")));
            var launch = new BusinessLaunch(configDirectory, Path.Combine(root, flow.Id, "data"), null, null);
            var factory = new ParcelCapabilityFactory(launch, policy);
            factory.Validate(factory.Normalize(config));
            var optional = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/optional", flow.Id, "business.json")));
            factory.Validate(factory.Normalize(optional));
            Pass(policy.Require(factory.SelectedProviders(config)) == "builtin-reference", flow.Id + " default and all optional provider versions declared");
            if (flow.Id == "sportmaster")
            {
                var rcs = factory.SelectedProviders(config).Select(p => p.Kind == "integration" ? new ProviderVersion("integration","extension:sportmaster.rcs-task/sportmaster-rcs-task","1.1.4","SPORTMASTER-RCS-SORTER-v1") : p).ToArray();
                Pass(policy.Require(rcs)=="sportmaster-rcs-builtin-reference","RCS and original optional capabilities match the same business declaration");
                Reject(()=>policy.Require(rcs.Select(p=>p.Kind=="integration"?p with{Version="1.0.2"}:p).ToArray()),"old immutable RCS version is not silently relabelled for new host");
            }
            int constructed = 0; factory.Boundary = _ => constructed++;
            var capability = config.Wcs ?? config.Plc!;
            ParcelConfiguration Replace(CapabilityBinding selected) => config.Wcs is null ? config with { Plc = selected } : config with { Wcs = selected };
            Reject(() => factory.Create(Replace(capability with { Version = "9.0.0" })), "unknown provider version rejected before constructing any resource");
            Reject(() => factory.Validate(Replace(capability with { Contract = "UNKNOWN-v9" })), "wrong provider protocol rejected");
            Reject(() => factory.Validate(Replace(capability with { Implementation = "unapproved-provider" })), "unapproved implementation rejected");
            Pass(constructed == 0, "rejection did not open readers or create external ports");
            await factory.DisposeAsync();
            var a = new ProviderVersion("login", "a", "1.0.0", "C1");
            var b = new ProviderVersion("plc", "b", "1.0.0", "C1");
            var c = b with { Version = "2.0.1" };
            var d = a with { Version = "2.0.1" };
            var pairs = policy with { ProviderSets = [new("old", [a, b]), new("new", [d, c])] };
            Reject(() => pairs.Require([a, c]), "mixing members from distinct compatibility sets rejected");
            Pass(pairs.Require([d, c]) == "new", "whole selected set matches a single declaration");
            var directory = Path.Combine(root, flow.Id, "release"); Directory.CreateDirectory(directory);
            var binary = Path.GetFileName(assembly.Location); File.Copy(assembly.Location, Path.Combine(directory, binary));
            using (var resource = assembly.GetManifestResourceStream(BusinessCompatibility.ResourceName)!)
            using (var output = File.Create(Path.Combine(directory, "application-compatibility.json"))) resource.CopyTo(output);
            var presentation = new BusinessPresentation([flow.Id], ["standard"]);
            var release = new ApplicationRelease(2, flow.Id, new string('a', 40), presentation.Views, presentation.Layouts,
                new() { [binary] = Wire.FileHash(assembly.Location), ["application-compatibility.json"] = BusinessCompatibility.Fingerprint(assembly) },
                "1.3.2", binary, BusinessCompatibility.Fingerprint(assembly));
            void Write(ApplicationRelease value) => ExtensionStore.AtomicWrite(Path.Combine(directory, "application-release.json"), value);
            void Check() => ApplicationReleaseValidation.ValidateIfPublished(directory, flow.Id, presentation, assembly);
            Write(release); Check(); Pass(true, "schema2 binds actual compiled version, binary and declaration bytes");
            await using var maintenanceFactory = new ParcelCapabilityFactory(launch, policy, assembly);
            Pass(maintenanceFactory.ReadPublishedRelease(directory).BusinessVersion == "1.3.2", "protected maintenance accepts actual schema2 business release");
            Write(release with { BusinessVersion = "1.0.0" }); Reject(Check, "wrong declared business version rejected");
            Reject(() => maintenanceFactory.ReadPublishedRelease(directory), "protected maintenance rejects wrong business version");
            Write(release with { SchemaVersion = 1 }); Reject(Check, "new compiled business cannot downgrade to release schema1");
            Reject(() => maintenanceFactory.ReadPublishedRelease(directory), "protected maintenance rejects schema1 downgrade");
            Write(release with { BusinessAssembly = "Ipc.Application.Other.dll" }); Reject(Check, "cross-business binary rejected");
            var changed = policy with { ProviderSets = [new("fake", [a, b])] };
            ExtensionStore.AtomicWrite(Path.Combine(directory, "application-compatibility.json"), changed);
            var changedHash = Wire.FileHash(Path.Combine(directory, "application-compatibility.json"));
            Write(release with { CompatibilitySha256 = changedHash, Files = new(release.Files) { ["application-compatibility.json"] = changedHash } });
            Reject(Check, "changing external declaration and manifest together cannot widen compiled policy");
            Reject(() => maintenanceFactory.ReadPublishedRelease(directory), "protected maintenance rejects altered embedded declaration identity");
            ExtensionStore.AtomicWrite(Path.Combine(configDirectory, "business.json"), config);
            await using var runtime = new ParcelRuntime(launch, flow, new ParcelCapabilityFactory(launch, policy));
            await runtime.StartAsync(CancellationToken.None);
            var before = await runtime.SnapshotAsync();
            await runtime.CommandAsync(new(Wire.Id(), before.Revision, "Pause"));
            await runtime.AuthorizeMaintenanceAsync("maintenance", "SIMULATION_ONLY");
            before = await runtime.SnapshotAsync();
            var rejected = await runtime.StageConfigurationAsync(before.Configuration.Revision, Replace(capability with { Version = "9.0.0" }), "Verify incompatible provider version rejection");
            Pass(rejected.Status == "Rejected" && rejected.Reason.Contains("not declared compatible", StringComparison.Ordinal) && Wire.Encode((await runtime.SnapshotAsync()).Configuration) == Wire.Encode(before.Configuration), "actual maintenance reaches compatibility rejection without replacing Effective/Pending");
        }
        Console.WriteLine("PASS VC01 COMPLETE");
    }
}
