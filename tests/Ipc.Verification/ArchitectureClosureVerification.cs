using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;
using Microsoft.Extensions.Logging.Abstractions;

static class ArchitectureClosureVerification
{
    static void Assert(bool value, string reason) { if (!value) throw new Exception("E13: " + reason); Console.WriteLine("PASS E13 " + reason); }
    static void Refuse(Action action, string reason)
    {
        try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or CryptographicException)
        { Console.WriteLine("PASS E13 refused " + reason + " / " + ex.GetType().Name); return; }
        throw new Exception("E13 accepted: " + reason);
    }
    static async Task Wait(Func<Task<bool>> predicate)
    {
        var watch = Stopwatch.StartNew();
        while (!await predicate()) { if (watch.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("E13 predicate"); await Task.Delay(30); }
    }
    static string Dotnet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Environment.ProcessPath!;
    static async Task RunProcess(string workspace, string log, params string[] arguments)
    {
        var info = new ProcessStartInfo(Dotnet) { WorkingDirectory = workspace, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var child = Process.Start(info)!; var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync(); await child.WaitForExitAsync();
        var text = await output + await error; File.WriteAllText(log, text);
        if (child.ExitCode != 0) throw new Exception("E13 command failed: " + string.Join(' ', arguments) + "\n" + text);
    }
    public static async Task<string> Build(string workspace, string area, string example)
    {
        var isolated = Path.Combine(area, example); Directory.CreateDirectory(isolated);
        foreach (var name in new[] { "global.json", "Directory.Build.props", "NuGet.Config" }) File.Copy(Path.Combine(workspace, name), Path.Combine(area, name), true);
        foreach (var file in Directory.GetFiles(Path.Combine(workspace, "examples", example)).Where(x => Path.GetExtension(x) is ".cs" or ".csproj" or ".json")) File.Copy(file, Path.Combine(isolated, Path.GetFileName(file)), true);
        var contract = typeof(IObservationExtension).Assembly.Location;
        await RunProcess(workspace, Path.Combine(area, example + ".build.log"), "build", Path.Combine(isolated, example + ".csproj"), "-p:RestoreLockedMode=true", "-c", "Release", "-m:1", "-nr:false",
            "-p:IpcContractPath=" + contract, "-p:IpcContractDirectory=" + Path.GetDirectoryName(contract), "-o", Path.Combine(isolated, "output"));
        return Path.Combine(isolated, "output", "Ipc.Reference." + example + ".dll");
    }
    public static string Package(string area, string name, ExtensionManifest manifest, RSA key, params string[] files)
    {
        var path = Path.Combine(area, name + ".ipcx");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, byte[] bytes) { using var stream = zip.CreateEntry(name).Open(); stream.Write(bytes); }
        var bytes = Encoding.UTF8.GetBytes(Wire.Encode(manifest)); Add("manifest.json", bytes); Add("manifest.sig", key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        foreach (var file in files) Add(Path.GetFileName(file), File.ReadAllBytes(file));
        return path;
    }
    static ExtensionFile FileIdentity(string file) => new(Path.GetFileName(file), new FileInfo(file).Length, Wire.FileHash(file));
    public static ExtensionManifest Manifest(string id, string file, string entry, string kind, string source) => new(1, id, "1.0.0", "reference-test", "net10.0", "any", 1, RunMode.Simulation,
        source, [], [FileIdentity(file)], [new(kind == "observation" ? "barcode-source" : "reference-http", Path.GetFileName(file), entry, kind, 8,
            kind == "observation" ? [new("health.interval-ms", "Health", "Heartbeat", "integer", Default: "100", Minimum: 100, Maximum: 2000), new("input.prefix", "Input", "Prefix", "string", Default: "")]
            : [new("metadata.label", "Service", "Label", "string", Default: ""), new("fault.fail-create", "Fault", "Constructor", "boolean", Default: "false"), new("fault.stop-delay-ms", "Fault", "Stop delay", "integer", Default: "0", Minimum: 0, Maximum: 30000), new("fault.dispose-delay-ms", "Fault", "Dispose delay", "integer", Default: "0", Minimum: 0, Maximum: 30000)])], [], Wire.FileHash(typeof(IObservationExtension).Assembly.Location),
        kind == "integration" ? new() { ["Ipc.Supply.Contracts"] = Wire.FileHash(typeof(IIntegrationExtension).Assembly.Location) } : null);
    public static async Task Run(string root, string url, string workspace)
    {
        ExtensionDraftVerification.Run();
        var area = Path.Combine(root, "architecture-13"); Directory.CreateDirectory(area);
        var hostDirectory = Path.GetDirectoryName(typeof(ProductRuntime).Assembly.Location)!;
        Dictionary<string, string> Host() => Directory.GetFiles(hostDirectory, "Ipc.*.dll").ToDictionary(x => Path.GetFileName(x)!, Wire.FileHash);
        var frozen = Wire.Encode(Host());
        var observation = await Build(workspace, area, "BarcodeExtension");
        var integration = await Build(workspace, area, "IntegrationExtension");
        Assert(Wire.Encode(Host()) == frozen, "two independent plugins build against frozen public contracts without changing host hashes");
        using var key = RSA.Create(3072);
        var trust = new ExtensionTrust(1, [new("reference-test", key.ExportSubjectPublicKeyInfoPem(), true)]);
        var trustFile = Path.Combine(area, "trust.json"); File.WriteAllText(trustFile, Wire.Encode(trust));
        string Source(string directory)
        {
            var info = new ProcessStartInfo("git") { WorkingDirectory = workspace, RedirectStandardOutput = true };
            foreach (var argument in new[] { "log", "-1", "--format=%H", "--", "examples/" + directory }) info.ArgumentList.Add(argument);
            using var child = Process.Start(info)!; var output = child.StandardOutput.ReadToEnd().Trim(); child.WaitForExit(); return output;
        }
        var om = Manifest("reference.barcode", observation, "Ipc.Reference.BarcodeExtension.BarcodeModule", "observation", Source("BarcodeExtension"));
        var im = Manifest("reference.integration", integration, "Ipc.Reference.IntegrationExtension.IntegrationModule", "integration", Source("IntegrationExtension"));
        var archive = Package(area, "observation", om, key, observation);
        var integrationArchive = Package(area, "integration", im, key, integration);
        var leases = Path.Combine(area, "leases");
        var manager = new ExtensionStore(Path.Combine(area, "store"), trust, RunMode.Simulation, leases);
        var op = manager.Install(archive, Wire.FileHash(archive)); var ip = manager.Install(integrationArchive, Wire.FileHash(integrationArchive));
        Assert(manager.Inventory().Length == 2, "both independent kinds install without executing factories");
        var changed = Package(area, "changed-same-version", om with { Modules = [om.Modules[0] with { MaximumInstances = 7 }] }, key, observation);
        manager.Uninstall(op.Package, []);
        Refuse(() => manager.Plan(changed), "P2-01 different content after uninstall");
        Refuse(() => manager.Install(changed, Wire.FileHash(changed)), "P2-01 different content cannot bypass installation");
        var reinstalled = manager.Install(archive, Wire.FileHash(archive));
        Assert(reinstalled.Package == op.Package && manager.Operations().Any(x => x.Kind == "Uninstall"), "P2-01 exact original can reinstall with retired bytes/audit retained");
        var interrupted = new ExtensionStore(Path.Combine(area, "identity-interrupted"), trust, RunMode.Simulation, leases);
        interrupted.PhaseBoundary = phase => { if (phase == "Prepared") throw new IOException("Synthetic prepared interruption"); };
        Refuse(() => interrupted.Install(archive, Wire.FileHash(archive)), "P2-01 prepared interruption");
        var restarted = new ExtensionStore(interrupted.Root, trust, RunMode.Simulation, leases);
        Refuse(() => restarted.Plan(changed), "P2-01 interrupted identity reservation");
        Assert(restarted.Recover(restarted.Operations().Single().Id).Package == op.Package, "P2-01 recovery preserves original operation identity");
        var concurrent = new ExtensionStore(Path.Combine(area, "identity-concurrent"), trust, RunMode.Simulation, leases);
        var results = await Task.WhenAll(new[] { archive, changed }.Select(file => Task.Run(() =>
        { try { return concurrent.Install(file, Wire.FileHash(file)).Package.ManifestSha256; } catch (Exception ex) when (ex is IOException or InvalidDataException) { return "refused"; } })));
        Assert(results.Count(x => x != "refused") == 1 && concurrent.Inventory().Length == 1, "P2-01 concurrent different-content install has one immutable winner");
        await ManagedReferences(workspace, area, om, archive, key, manager, observation);
        await IntegrationJourney(area, url, manager, trustFile, op.Package, ip.Package, integrationArchive, key, im, integration);
        await AssemblyJourney(workspace, area, url);
        Assert(Wire.Encode(Host()) == frozen, "host hashes remain unchanged after install, execution, recovery and stop");
        File.WriteAllText(Path.Combine(area, "closure-13.json"), Wire.Encode(new { Result = "PASS_REFERENCE", HostHashes = Host(), ObservationSource = om.SourceCommit, IntegrationSource = im.SourceCommit,
            Packages = manager.Inventory(), Operations = manager.Operations(), Windows = "WINDOWS_NOT_RUN", ActualWpfScreenshots = 0 }));
    }
    static async Task ManagedReferences(string workspace, string area, ExtensionManifest om, string observationArchive, RSA key, ExtensionStore manager, string observation)
    {
        async Task<string> Compile(string name, string code, params string[] references)
        {
            var directory = Path.Combine(area, "compile-" + name); Directory.CreateDirectory(directory);
            var items = string.Concat(references.Select(x => "<Reference Include=\"" + Path.GetFileNameWithoutExtension(x) + "\"><HintPath>" + System.Security.SecurityElement.Escape(x) + "</HintPath><Private>false</Private></Reference>"));
            File.WriteAllText(Path.Combine(directory, "probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>" + name + "</AssemblyName><GenerateAssemblyVersionAttribute>false</GenerateAssemblyVersionAttribute></PropertyGroup><ItemGroup>" + items + "</ItemGroup></Project>");
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), code);
            await RunProcess(workspace, directory + ".log", "build", Path.Combine(directory, "probe.csproj"), "-m:1", "-nr:false", "-o", Path.Combine(directory, "output"));
            return Path.Combine(directory, "output", name + ".dll");
        }
        var contract = typeof(IObservationExtension).Assembly.Location;
        var marker = Path.Combine(area, "preflight-executed.txt");
        string Code(string body) => "using Ipc.Platform; namespace P2Probe; public sealed class Factory : IObservationExtension { public Factory() { System.IO.File.WriteAllText(" + System.Text.Json.JsonSerializer.Serialize(marker) + ", \"executed\"); } public IControlledObservationSource Create(ExtensionContext c) { " + body + " return new Ipc.Reference.BarcodeExtension.BarcodeModule().Create(c); } }";
        var legal = await Compile("Ipc.Reference.DependencyProbe", Code(""), contract, observation);
        var lm = Manifest("reference.dependency", legal, "P2Probe.Factory", "observation", om.SourceCommit) with { Modules = [new("probe", Path.GetFileName(legal), "P2Probe.Factory", "observation", 2, om.Modules[0].Parameters)] };
        var missing = Package(area, "missing-actual-reference", lm, key, legal);
        Refuse(() => manager.Plan(missing), "P2-02 actual undeclared managed reference absent from signed closure");
        Assert(!File.Exists(marker), "P2-02 inspection and rejected installation never execute plugin constructors");
        var exact = om is not null ? manager.Inventory().Single(x => x.Package.Id == om.Id).Package : throw new Exception();
        var validFile = Package(area, "legal-actual-reference", lm with { Dependencies = [exact] }, key, legal);
        var installed = manager.Install(validFile, Wire.FileHash(validFile));
        using (manager.AcquireUse([installed.Package, exact]))
        using (var loading = new ExtensionLoading(manager))
        {
            var source = loading.Create(installed.Package, lm.Modules[0], new("dependency-probe", lm.Id, "probe", "SIM-P2-PROBE", true, []));
            await source.StartControlledAsync(Wire.Id(), _ => true, _ => { }, CancellationToken.None);
            await source.StopAsync(CancellationToken.None); await source.DisposeAsync();
        }
        Assert(File.Exists(marker), "P2-02 legal exact dependency enters actual runtime resolution only after install and lease");
        File.Delete(marker);
        var faux = await Compile("System.Unapproved", "namespace System.Unapproved; public static class Api { public static string Read() => \"synthetic\"; }");
        var root = await Compile("Ipc.Reference.FalseFramework", Code("_ = System.Unapproved.Api.Read();"), contract, observation, faux);
        var fm = Manifest("reference.falseframework", root, "P2Probe.Factory", "observation", om.SourceCommit) with { Dependencies = [exact], Modules = [new("probe", Path.GetFileName(root), "P2Probe.Factory", "observation", 2, om.Modules[0].Parameters)] };
        Refuse(() => manager.Plan(Package(area, "false-system-prefix", fm, key, root)), "P2-02 System.* is not a framework allowlist");
        var libraryManifest = Manifest("reference.library", faux, "", "observation", om.SourceCommit) with { Modules = [] };
        var libraryFile = Package(area, "library-only", libraryManifest, key, faux);
        var library = manager.Install(libraryFile, Wire.FileHash(libraryFile));
        var transitiveFile = Package(area, "exact-library-dependency", fm with { Dependencies = [exact, library.Package] }, key, root);
        var transitive = manager.Install(transitiveFile, Wire.FileHash(transitiveFile));
        Assert(manager.VerifyInstalled(transitive.Package).Reference == transitive.Package, "P2-02 library-only exact signed dependency is accepted without a fake entry module");
        Refuse(() => manager.Uninstall(library.Package, []), "actual managed library dependency protects uninstall");
        manager.Uninstall(transitive.Package, []); manager.Uninstall(library.Package, []);
        var complete = fm with { Id = "reference.privateframework", Files = [FileIdentity(root), FileIdentity(faux)] };
        var legalPrivate = Package(area, "legal-private-system-name", complete, key, root, faux);
        Assert(manager.Plan(legalPrivate).Manifest.Files.Length == 2, "P2-02 signed private System-named assembly requires exact bytes rather than blanket denial or allow");
        var mismatched = await Compile("System.Unapproved", "[assembly:System.Reflection.AssemblyVersion(\"2.0.0.0\")] namespace System.Unapproved; public static class Api { public static string Read() => \"version2\"; }");
        // Compile overwrites only a fixture directory; retain v1 bytes in its own custody.
        // The previously packaged v1 archive remains immutable and is used for the mismatch.
        var v1 = Path.Combine(area, "v1-System.Unapproved.dll");
        using (var zip = ZipFile.OpenRead(legalPrivate)) { using var input = zip.GetEntry("System.Unapproved.dll")!.Open(); using var output = File.Create(v1); input.CopyTo(output); }
        var versionRoot = await Compile("Ipc.Reference.VersionProbe", Code("_ = System.Unapproved.Api.Read();"), contract, observation, mismatched);
        var renamed = Path.Combine(area, "version1/System.Unapproved.dll"); Directory.CreateDirectory(Path.GetDirectoryName(renamed)!); File.Copy(v1, renamed);
        var vm = Manifest("reference.versionconflict", versionRoot, "P2Probe.Factory", "observation", om.SourceCommit) with { Dependencies = [exact], Modules = [new("probe", Path.GetFileName(versionRoot), "P2Probe.Factory", "observation", 2, om.Modules[0].Parameters)], Files = [FileIdentity(versionRoot), FileIdentity(renamed)] };
        Refuse(() => manager.Plan(Package(area, "version-conflict", vm, key, versionRoot, renamed)), "P2-02 managed AssemblyRef exact version conflict");
        var ambiguous = lm with { Dependencies = [exact], Files = [FileIdentity(legal), FileIdentity(observation)] };
        Refuse(() => manager.Plan(Package(area, "ambiguous-closure", ambiguous, key, legal, observation)), "P2-02 local and dependency assembly ambiguity");
        Assert(!File.Exists(marker), "P2-02 all negative static preflights leave constructor marker absent");
        manager.Uninstall(installed.Package, []);
    }
    static BootOptions Boot(string area, StationConfig config, ExtensionStore store, string trust)
    {
        var install = Path.GetDirectoryName(typeof(ProductRuntime).Assembly.Location)!; var definition = ReferenceProduct.Definition;
        var manifest = new ProductManifest(1, definition.ProductId, config.Mode, definition.Runtime, "any", definition.BusinessModule, definition.Views,
            definition.Modules.Select(x => new ModuleSpec(x.Id, x.Kind, x.Version, x.ContractMajor, x.Dependencies, x.File, Wire.FileHash(Path.Combine(install, x.File)))).ToArray());
        var directory = Path.Combine(area, "config"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "product.json"), Wire.Encode(manifest)); File.WriteAllText(Path.Combine(directory, "station.json"), Wire.Encode(config)); File.WriteAllText(Path.Combine(directory, "presentation.json"), Wire.Encode(new PresentationSettings()));
        return new(Path.Combine(directory, "product.json"), Path.Combine(directory, "station.json"), Path.Combine(directory, "presentation.json"), Path.Combine(area, "data"), install, store.DeviceLeaseRoot, ExtensionRoot: store.Root, ExtensionTrustFile: trust);
    }
    static async Task<CommandReceipt> Command(ProductRuntime runtime, string kind, string? value = null, string? owner = null)
    {
        var snapshot = await runtime.Application!.SnapshotAsync(); var receipt = await runtime.CommandAsync(new(Wire.Id(), snapshot.State.Revision, kind, owner, value));
        Assert(receipt.Status == "Persisted", "integration journey " + kind + " / " + receipt.Status + ": " + receipt.Reason); return receipt;
    }
    static async Task IntegrationJourney(string area, string url, ExtensionStore manager, string trustFile, PackageReference observation, PackageReference integration,
        string archive, RSA key, ExtensionManifest manifest, string binary)
    {
        var configuration = Rig.Config(url) with { Version = "E13-DEPLOY-1", Extensions = new([observation, integration],
            [new("reader-a", observation.Id, "barcode-source", "SIM-E13-A", true, new() { ["input.prefix"] = "000" }),
             new("reader-b", observation.Id, "barcode-source", "SIM-E13-B", true, new() { ["input.prefix"] = "111" })],
            [new("receiver-a", integration.Id, "reference-http", true, new() { ["metadata.label"] = "primary" }),
             new("receiver-b", integration.Id, "reference-http", true, new() { ["metadata.label"] = "secondary" })],
            [new("requests", "receiver-a", new("reference-target-a", "1", "SIMULATION-v1", url)),
             new("reference-query", "receiver-b", new("reference-target-b", "2", "SIMULATION-v1", url))]) };
        configuration = configuration with { Devices = [new("left", "extension:reader-a", "SIM-E13-A", "1"), new("right", "extension:reader-b", "SIM-E13-B", "1")], Integration = configuration.Extensions!.Ports![0].Target };
        Assert(ExtensionDeployment.Validate(configuration.Extensions, configuration.Devices, manager).Length == 0, "two device and two service instances have separate identity/binding/parameter validation");
        var bad = configuration.Extensions with { Ports = [configuration.Extensions.Ports![0], configuration.Extensions.Ports[1] with { Port = "requests" }] };
        Assert(ExtensionDeployment.Validate(bad, configuration.Devices, manager).Length > 0, "duplicate service ports are rejected by real management validation");
        var boot = Boot(Path.Combine(area, "journey"), configuration, manager, trustFile);
        BusinessRequest unknown;
        var runtime = new ProductRuntime(boot, NullLogger<ProductRuntime>.Instance);
        await runtime.StartAsync(CancellationToken.None);
        Assert(runtime.Diagnostics.Count == 0 && runtime.Application is not null, "both installed extension kinds start actual Product / " + string.Join(';', runtime.Diagnostics));
        await Wait(async () => (await runtime.Application!.SnapshotAsync()).State.IntegrationServiceHealth?.IsValid(DateTimeOffset.UtcNow) == true);
        Assert((await runtime.Application!.SnapshotAsync()).State.IntegrationHealth is null, "typed service health does not invent a device identity");
        Assert(runtime.IntegrationDiagnostics.Select(x => x.InstanceId).Order().SequenceEqual(new[] { "receiver-a", "receiver-b" }), "two real integration instances participate in health and diagnostics");
        await Command(runtime, "Login", "maintenance"); await Command(runtime, "OpenContext", "000001");
        await Wait(async () => (await runtime.Application!.SnapshotAsync()).State.Session?.Status == SessionStatus.Active);
        var context = (await runtime.Application!.SnapshotAsync()).Requests.Single();
        Assert(context.Implementation is { Driver: "extension", InstanceId: "receiver-a" } && context.Implementation.Package == integration && context.Implementation.Parameters["metadata.label"] == "primary", "first T1 persist freezes actual package, instance, parameters, contract and target");
        await runtime.EmitAsync("left", "Enter"); await Wait(async () => (await runtime.Application.SnapshotAsync()).State.Lanes.ContainsKey("left"));
        await runtime.EmitAsync("left", "Read", ["000123"], 2); await Wait(async () => (await runtime.Application.SnapshotAsync()).State.Lanes["left"].Status == OperationStatus.AwaitingAction);
        await runtime.EmitAsync("right", "Enter"); await Wait(async () => (await runtime.Application.SnapshotAsync()).State.Lanes.ContainsKey("right"));
        await runtime.EmitAsync("right", "Read", ["000123"], 2); await Wait(async () => (await runtime.Application.SnapshotAsync()).State.Lanes["right"].Status == OperationStatus.ValidationRejected);
        Assert((await runtime.Application.SnapshotAsync()).State.Lanes["right"].CurrentInput!.Codes.Length == 0, "device parameters isolate actual business streams");
        var operation = (await runtime.Application.SnapshotAsync()).State.Lanes["left"];
        await Command(runtime, "SetFixtureFault", "Drop"); await Command(runtime, "Submit", owner: operation.Id);
        await Wait(async () => (await runtime.Application.SnapshotAsync()).Requests.Any(x => x.Kind == RequestKind.Submit && x.Status == RequestStatus.Unknown));
        unknown = (await runtime.Application.SnapshotAsync()).Requests.Single(x => x.Kind == RequestKind.Submit);
        Refuse(() => manager.Uninstall(integration, () => SupplyStore.InspectRequiredExtensionPackages(Path.Combine(runtime.DataDirectory!, "station.db"))), "original Unknown and actual service lease protect integration uninstall");
        await runtime.DisposeAsync();
        runtime = new ProductRuntime(boot, NullLogger<ProductRuntime>.Instance); await runtime.StartAsync(CancellationToken.None);
        await Command(runtime, "Login", "maintenance"); await runtime.ReconcileAsync(unknown.Id);
        var detail = (await runtime.Application!.DetailsAsync(unknown.Id)).Single();
        Assert(detail.Request == unknown with { Status = RequestStatus.Accepted } && detail.Attempts.Length == 1 && (await runtime.Application.SnapshotAsync()).AcceptedQuantity == 2,
            "restart queries original integration identity and unique receiver acceptance, without new ID/bytes/attempt");
        // New deployment edits cannot reinterpret a recorded request or response validator.
        var frozenIdentity = Wire.Encode(unknown.Implementation);
        var tampered = unknown with { Implementation = unknown.Implementation! with { Parameters = new() { ["metadata.label"] = "substituted" } } };
        var isolatedRouter = new IntegrationRuntime(configuration, manager); isolatedRouter.Initialize();
        await ThrowsData(() => isolatedRouter.QueryAsync(tampered, CancellationToken.None));
        await isolatedRouter.DisposeAsync();
        Assert(Wire.Encode(unknown.Implementation) == frozenIdentity, "tampered original parameter fingerprint blocks routing without mutating persisted identity");
        File.WriteAllText(Path.Combine(area, "integration-journey.json"), Wire.Encode(new { Original = unknown, Resolved = detail, Diagnostics = runtime.IntegrationDiagnostics, Accepted = (await runtime.Application.SnapshotAsync()).AcceptedQuantity }));
        await runtime.DisposeAsync();
        await FrozenRoutingAndCancellation(area, configuration, manager, unknown, archive, key, manifest, binary);
        var failedConfiguration = configuration with { StationId = "SIM-E13-FAIL", Version = "E13-CLEAN", Devices = configuration.Devices.Select(x => x with { DeviceId = x.DeviceId + "-fail" }).ToArray(), Extensions = configuration.Extensions with { Instances = configuration.Extensions.Instances.Select(x => x with { DeviceId = x.DeviceId + "-fail" }).ToArray() } };
        var failureBoot = Boot(Path.Combine(area, "activation"), failedConfiguration, manager, trustFile);
        string candidateId;
        await using (var clean = new ProductRuntime(failureBoot, NullLogger<ProductRuntime>.Instance))
        {
            await clean.StartAsync(CancellationToken.None); Assert(clean.Diagnostics.Count == 0, "integration upgrade base starts");
            await Command(clean, "Login", "maintenance");
            var draft = failedConfiguration with { Version = "E13-FAILED", Extensions = failedConfiguration.Extensions! with { Services = failedConfiguration.Extensions.Services!.Select((x, index) => x with { Parameters = new(x.Parameters) { ["fault.fail-create"] = index == 1 ? "true" : "false" } }).ToArray() } };
            var revision = (await clean.Application!.SnapshotAsync()).State.Revision;
            await Command(clean, "SaveConfig", Wire.Encode(draft), failedConfiguration.Version);
            var competing = await clean.CommandAsync(new(Wire.Id(), revision, "SaveConfig", failedConfiguration.Version, Wire.Encode(draft with { Version = "E13-RACE" })));
            Assert(competing.Status == "Conflict", "service parameters and target bindings use the same deployment CAS");
            var candidate = (await clean.Application.ConfigsAsync()).Single(x => x.Content.Version == draft.Version); candidateId = candidate.Id;
            await Command(clean, "ActivateConfig", owner: candidate.Id);
        }
        await using (var failed = new ProductRuntime(failureBoot, NullLogger<ProductRuntime>.Instance))
        { await failed.StartAsync(CancellationToken.None); Assert(failed.Diagnostics.Any(x => x.Contains("Startup NotReady")), "integration constructor failure cannot become Effective or fallback to simulation success"); }
        await using (var fallback = new ProductRuntime(failureBoot, NullLogger<ProductRuntime>.Instance))
        {
            await fallback.StartAsync(CancellationToken.None); Assert(fallback.Diagnostics.Count == 0 && fallback.Configuration!.Version == failedConfiguration.Version, "next controlled startup retains prior Effective after failed integration activation");
            await Command(fallback, "Login", "maintenance"); Assert((await fallback.Application!.ConfigsAsync()).Any(x => x.Id == candidateId && x.Status == "ActivationFailed"), "failed integration activation receipt is retained");
        }
        await SlowIntegration(area, configuration, manager, trustFile, integration);
    }
    static async Task ThrowsData(Func<Task> action)
    {
        try { await action(); } catch (InvalidDataException) { return; } throw new Exception("E13 expected exact identity refusal");
    }
    static async Task FrozenRoutingAndCancellation(string area, StationConfig configuration, ExtensionStore manager, BusinessRequest original,
        string archive, RSA key, ExtensionManifest manifest, string binary)
    {
        var upgradedManifest = manifest with { Version = "1.1.0" };
        var upgradedFile = Package(area, "integration-upgrade", upgradedManifest, key, binary);
        var upgraded = manager.Install(upgradedFile, Wire.FileHash(upgradedFile));
        var newTarget = new IntegrationBinding("new-unavailable-target", "new-revision", "SIMULATION-v1", "http://127.0.0.1:1/");
        var changed = configuration with { Version = "E13-NEW-IMPLEMENTATION", Integration = newTarget,
            Extensions = configuration.Extensions! with {
                Packages = configuration.Extensions.Packages.Select(x => x.Id == upgraded.Package.Id ? upgraded.Package : x).ToArray(),
                Services = configuration.Extensions.Services!.Select(x => x with { Parameters = new(x.Parameters) { ["metadata.label"] = "new-parameters" } }).ToArray(),
                Ports = configuration.Extensions.Ports!.Select(x => x with { Target = newTarget }).ToArray() } };
        var router = new IntegrationRuntime(changed, manager); router.Initialize();
        var missingIdentity = original with { Implementation = null }; var frozenMissing = Wire.Encode(missingIdentity);
        var entriesBefore = router.Diagnostics.Length;
        foreach (var queryOnly in new[] { false, true })
        {
            var refused = false;
            try
            {
                if (queryOnly) await router.QueryAsync(missingIdentity, CancellationToken.None);
                else await router.ExecuteAsync(missingIdentity, CancellationToken.None);
            }
            catch (InvalidDataException error) when (error.Message.Contains("implementation identity is missing", StringComparison.Ordinal)) { refused = true; }
            Assert(refused && router.Diagnostics.Length == entriesBefore && Wire.Encode(missingIdentity) == frozenMissing,
                "missing frozen implementation refuses before constructing a replacement route or executing/querying any port; original bytes retained");
        }
        var queried = await router.QueryAsync(original, CancellationToken.None);
        Assert(queried?.Result == "Accepted", "old query uses original installed implementation/parameters/receiver despite changed active target and package");
        router.ValidateResponse(original, queried!);
        Refuse(() => manager.Uninstall(original.Implementation!.Package!, []), "historical query pins the original package lease independently of the new active package");
        await router.DisposeAsync();
        var oldDirectory = manager.PackageDirectory(original.Implementation!.Package!); var retained = oldDirectory + ".synthetic-missing";
        Directory.Move(oldDirectory, retained);
        try
        {
            router = new IntegrationRuntime(changed, manager); router.Initialize();
            bool refused = false;
            try { await router.QueryAsync(original, CancellationToken.None); } catch (Exception ex) when (ex is IOException or InvalidDataException) { refused = true; }
            Assert(refused, "missing exact historical capability blocks query instead of selecting new target or default implementation");
            await router.DisposeAsync();
        }
        finally { Directory.Move(retained, oldDirectory); }
        router = new IntegrationRuntime(configuration with { TimeoutMs = 5000 }, manager); router.Initialize();
        var actor = new Actor("SIM-transport-verifier", SimulationRole.Maintenance, DateTimeOffset.UtcNow.AddMinutes(5));
        var session = Wire.Id();
        var intent = new RequestIntent(configuration.StationId + "-transport", session, session, 1, RequestKind.Context, "000001", null, null, null, actor);
        var payload = router.Encode(intent, "Delay");
        var request = new BusinessRequest(Wire.Id(), session, session, RequestKind.Context, 1, session + ":Context:1", payload, Wire.Hash(payload), configuration.Integration, RequestStatus.Dispatching, DateTimeOffset.UtcNow, router.Freeze());
        File.WriteAllText(Path.Combine(area, "transport-delay-request.json"), Wire.Encode(request));
        using var cancellation = new CancellationTokenSource();
        var sending = router.ExecuteAsync(request, cancellation.Token);
        await Wait(async () => (await router.QueryAsync(request, CancellationToken.None))?.Result == "Accepted");
        cancellation.Cancel();
        bool cancelled = false;
        try { await sending; } catch (OperationCanceledException) { cancelled = true; }
        Assert(cancelled, "typed integration forwards caller cancellation across actual in-flight HTTP delivery");
        var receipt = await router.QueryAsync(request, CancellationToken.None);
        Assert(receipt?.Result == "Accepted" && receipt.RequestId == request.Id, "cancelled transport may already be accepted; original query resolves without resend");
        router.ValidateResponse(request, receipt!);
        await router.DisposeAsync();
        manager.Uninstall(upgraded.Package, []);
    }
    static async Task SlowIntegration(string area, StationConfig configuration, ExtensionStore manager, string trust, PackageReference package)
    {
        foreach (var failure in new[] { "stop", "dispose" })
        {
        var slow = configuration with { StationId = "SIM-E13-SLOW-" + failure, Version = "E13-SLOW-" + failure, Devices = configuration.Devices.Select(x => x with { DeviceId = x.DeviceId + "-slow-" + failure }).ToArray(),
            Extensions = configuration.Extensions! with { Instances = configuration.Extensions.Instances.Select(x => x with { DeviceId = x.DeviceId + "-slow-" + failure }).ToArray(),
                Services = configuration.Extensions.Services!.Select((x, i) => x with { Parameters = new(x.Parameters) { ["fault." + failure + "-delay-ms"] = i == 0 ? "16000" : "0" } }).ToArray() } };
        var boot = Boot(Path.Combine(area, "integration-" + failure), slow, manager, trust);
        var runtime = new ProductRuntime(boot, NullLogger<ProductRuntime>.Instance); await runtime.StartAsync(CancellationToken.None);
        Assert(runtime.Diagnostics.Count == 0, "delayed integration " + failure + " starts real typed instance");
        Exception? timeoutFailure = null; try { await runtime.DisposeAsync(); } catch (AggregateException ex) { timeoutFailure = ex; }
        Assert(timeoutFailure is not null && runtime.HeldResources.Length > 0, "integration " + failure + " timeout retains data/station/device ownership");
        Refuse(() => manager.Uninstall(package, []), "integration Stop not terminated retains package/dependency lease");
        Refuse(() => { using var duplicate = new ResourceLease(runtime.DataDirectory!, ["database"]); }, "integration Stop not terminated retains actual database lease");
        await Task.Delay(11500); await runtime.DisposeAsync();
        Assert(runtime.HeldResources.Length == 0, "late integration " + failure + " releases exact resources once after termination");
        }
    }
    static async Task AssemblyJourney(string workspace, string area, string url)
    {
        var output = Path.Combine(area, "inspection-composition");
        await RunProcess(workspace, Path.Combine(area, "inspection-publish.log"), "publish", Path.Combine(workspace, "src/Ipc.Assembly/Ipc.Assembly.csproj"),
            "-p:BusinessModule=Inspection", "-p:NuGetLockFilePath=packages.inspection.lock.json", "-c", "Release", "-m:1", "-nr:false", "--self-contained", "false", "-o", output);
        Assert(File.Exists(Path.Combine(output, "Ipc.Inspection.dll")) && !Directory.GetFiles(output, "Ipc.Supply*.dll").Any() && !File.Exists(Path.Combine(output, "Ipc.Adapters.dll")), "non-Supply build physically selects a real business closure without Supply/adapters binaries");
        var config = Path.Combine(workspace, "config/inspection"); var data = Path.Combine(area, "inspection-data");
        async Task<string> ConsoleRun(string commands)
        {
            var info = new ProcessStartInfo(Dotnet) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { Path.Combine(output, "Ipc.Assembly.dll"), "--config", config, "--data-root", data, "--view", "inspection-history", "--layout", "compact" }) info.ArgumentList.Add(argument);
            using var child = Process.Start(info)!; var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            await child.StandardInput.WriteAsync(commands); child.StandardInput.Close(); await child.WaitForExitAsync();
            var text = await stdout + await stderr; Assert(child.ExitCode == 0, "inspection actual core process starts, commands/queries and stops"); return text;
        }
        var beginId = Wire.Id(); var sampleId = Wire.Id();
        var commands = new[] { "snapshot", Wire.Encode(new BusinessCommand(beginId, 0, "BeginBatch", Value: "batch-one")),
            Wire.Encode(new BusinessCommand(sampleId, 1, "RecordSample", Value: "synthetic-inspection")),
            Wire.Encode(new BusinessCommand(sampleId, 1, "RecordSample", Value: "synthetic-inspection")),
            Wire.Encode(new BusinessCommand(Wire.Id(), 1, "RecordSample", Value: "stale")),
            Wire.Encode(new BusinessCommand(Wire.Id(), 2, "CloseBatch")), "snapshot", "quit" };
        // CLI is one JSON document per line; Wire's indented formatter is intentionally normalized.
        var first = await ConsoleRun(string.Join('\n', commands.Select(x => x.Replace("\r", "").Replace("\n", ""))) + "\n");
        File.WriteAllText(Path.Combine(area, "inspection-console.log"), first);
        Assert(first.Contains("\"Conflict\"") && first.Contains("\"Samples\": \"1\"") && first.Contains("\"CurrentState\": \"Closed\""), "inspection commands persist one sample, reject stale CAS and close the batch");
        var restarted = await ConsoleRun("snapshot\nquit\n"); File.WriteAllText(Path.Combine(area, "inspection-restart.log"), restarted);
        Assert(restarted.Contains("\"Samples\": \"1\"") && restarted.Contains("\"CurrentState\": \"Closed\""), "inspection restart reads durable business facts");
        var supplyOutput = Path.Combine(area, "supply-composition");
        await RunProcess(workspace, Path.Combine(area, "supply-assembly-build.log"), "publish", Path.Combine(workspace, "src/Ipc.Assembly/Ipc.Assembly.csproj"),
            "-p:BusinessModule=Supply", "-p:NuGetLockFilePath=packages.supply.lock.json", "-c", "Release", "-m:1", "-nr:false", "--self-contained", "false", "-o", supplyOutput);
        Assert(File.Exists(Path.Combine(supplyOutput, "Ipc.Supply.dll")) && !File.Exists(Path.Combine(supplyOutput, "Ipc.Inspection.dll")), "Supply composition selects its business and page registration with no inspection implementation");
        // The Product itself is also exercised above; use the neutral assembly to prove its startup/command/query/stop bridge.
        var seal = new ProcessStartInfo("python3") { WorkingDirectory = workspace, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "scripts/seal.py", "--bin", supplyOutput }) seal.ArgumentList.Add(argument);
        using (var child = Process.Start(seal)!) { var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync(); await child.WaitForExitAsync(); Assert(child.ExitCode == 0, "Supply assembly seals exact build modules / " + await stdout + await stderr); }
        var station = Wire.Decode<StationConfig>(File.ReadAllText(Path.Combine(supplyOutput, "config/station.json"))) with { Integration = new("reference-target", "1", "SIMULATION-v1", url), StationId = "SIM-ASSEMBLY-13" };
        File.WriteAllText(Path.Combine(supplyOutput, "config/station.json"), Wire.Encode(station));
        var processInfo = new ProcessStartInfo(Dotnet) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { Path.Combine(supplyOutput, "Ipc.Assembly.dll"), "--config", Path.Combine(supplyOutput, "config"), "--data-root", Path.Combine(area, "supply-assembly-data"), "--view", "history", "--layout", "compact" }) processInfo.ArgumentList.Add(argument);
        // Supply startup needs Mac's protected-policy test override just as the existing verifier does.
        using (var child = Process.Start(processInfo)!)
        {
            var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync(); await child.StandardInput.WriteAsync("snapshot\nquit\n"); child.StandardInput.Close(); await child.WaitForExitAsync();
            var log = await stdout + await stderr; File.WriteAllText(Path.Combine(area, "supply-assembly-console.log"), log); Assert(child.ExitCode == 0 && log.Contains("\"Business\": \"supply\"") && log.Contains("\"Admission\""), "Supply neutral assembly actual startup/query/stop uses the one Product owner");
        }
    }
}
