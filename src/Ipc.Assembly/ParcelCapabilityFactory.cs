using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
namespace Ipc.Assembly;

public sealed record ParcelExtensionStoreConfiguration(string Root, string TrustFile);
public sealed class ParcelCapabilityFactory : IParcelCapabilityFactory
{
    readonly BusinessLaunch launch;
    readonly BusinessCompatibility? compatibility;
    readonly System.Reflection.Assembly? businessAssembly;
    readonly bool installer;
    readonly BuiltInCapabilityRegistry registry;
    readonly ProductCapabilityPolicy policy;

    public ParcelCapabilityFactory(BusinessLaunch launch, BusinessCompatibility? compatibility = null,
        System.Reflection.Assembly? businessAssembly = null, bool installer = false)
        : this(launch, ParcelAssembly.CreateCapabilityRegistry(), ParcelAssembly.CreateCapabilityPolicy(), compatibility, businessAssembly, installer)
    { }

    public ParcelCapabilityFactory(BusinessLaunch launch, BuiltInCapabilityRegistry registry, ProductCapabilityPolicy policy,
        BusinessCompatibility? compatibility = null, System.Reflection.Assembly? businessAssembly = null, bool installer = false)
    {
        this.launch = launch;
        this.registry = registry;
        this.policy = policy;
        this.compatibility = compatibility;
        this.businessAssembly = businessAssembly;
        this.installer = installer;
    }

    ExtensionStore? store;
    ExtensionLoading? loading;
    PackageUseLease? use;
    ResourceLease? devices;
    readonly List<IAsyncDisposable> constructed = [];
    readonly List<IAsyncDisposable> failedLiveCleanup = [];
    readonly List<FileStream> prerequisiteHolds = [];
    internal Action<string>? Boundary { get; set; }
    internal Action<string> RequireProductionManagement { get; init; } = ManagementModeBroker.RequireProduction;
    bool transferred;
    RunMode mode = RunMode.Simulation;
    public DiscoveredCamera[] DiscoverCameras(string readerEngine) =>
        registry.FindCameraDiscovery(readerEngine).Discover(readerEngine);

    public string ReadCameraSerial(string ip, string readerEngine) =>
        registry.FindCameraDiscovery(readerEngine).ReadSerial(ip, readerEngine);

    ExtensionTrust? trustPolicy;
    string? trustFile;
    DeploymentQualification? qualification;
    bool UsesBuiltInOnly(ParcelConfiguration c) => policy.BuiltInOnly && c.ApplicationId == policy.ApplicationId;

    void RequireProduct(ParcelConfiguration c)
    {
        if (c.ApplicationId != policy.ApplicationId && !policy.AllowsMixedVerificationProducts)
            throw new InvalidDataException("Capability policy belongs to another Application");
    }

    public string? AdmissionFailure(ParcelConfiguration c)
    {
        RequireProduct(c);
        return c.Mode == RunMode.Production && !UsesBuiltInOnly(c) &&
            (qualification is null || qualification.ExpiresAt <= DateTimeOffset.UtcNow)
            ? "Production NotReady: exact deployment qualification missing/expired" : null;
    }

    // Read-only semantics also apply to historical bindings of the same implementation.
    // Construction and configuration admission still require the exact current tuple.
    IBuiltInBindingProvider BindingMetadata(CapabilityBinding b) =>
        registry.FindBinding(b.Implementation) is { } provider && provider.Descriptor.Kind == b.Kind ? provider :
            throw new InvalidDataException("No approved capability implementation: " + b.Implementation);

    public bool SimulationIdentity(CapabilityBinding b) => b.Package is null ? BindingMetadata(b).IsSimulation :
        Store().VerifyInstalled(b.Package).Manifest.Modules.Single(x => x.Id == b.Module).Assurance?.Simulation != false;

    public static void RegisterContracts() => ExtensionPublicContracts.Register(typeof(IPlcExtension).Assembly);

    void RequireExternalExtensions()
    {
        if (!policy.SupportsExternalExtensions) throw new NotSupportedException("SM当前全部使用内置驱动和组件，外置扩展已停用。");
    }
    ExtensionStore Store()
    {
        RequireExternalExtensions();
        if (store is not null) return store;
        var settingsFile = Path.Combine(launch.ConfigurationDirectory, "extension-store.json");
        (string Root, string TrustFile) paths;
        if (OperatingSystem.IsWindows())
        {
            if (File.Exists(settingsFile)) throw new InvalidDataException("Windows reference extension store/trust overrides are forbidden; use provisioned machine policy");
            paths = MachineRuntimePolicy.ExtensionPaths(mode);
        }
        else
        {
            var settings = Wire.Decode<ParcelExtensionStoreConfiguration>(File.ReadAllText(settingsFile));
            if (!Path.IsPathFullyQualified(settings.Root) || !Path.IsPathFullyQualified(settings.TrustFile)) throw new InvalidDataException("Explicit independent reference store/trust absolute paths are required");
            paths = MachineRuntimePolicy.ExtensionPaths(mode, settings.Root, settings.TrustFile);
        }
        if (new FileInfo(paths.TrustFile).Length > ExtensionPackages.MaximumManifestBytes) throw new InvalidDataException("Trust policy quota exceeded");
        var trust = Wire.Decode<ExtensionTrust>(File.ReadAllText(paths.TrustFile));
        var leases = installer && OperatingSystem.IsWindows() ? MachineRuntimePolicy.InstallerLeaseRoot("") :
            MachineRuntimePolicy.ResolveLeaseRoot(OperatingSystem.IsWindows() ? null : Path.Combine(launch.DataRoot, "Simulation", "resource-leases"));
        trustPolicy = trust; trustFile = paths.TrustFile;
        return store = new(paths.Root, trust, mode, leases);
    }
    public ParcelConfiguration Normalize(ParcelConfiguration configuration)
    {
        RequireProduct(configuration);
        if (store is not null && mode != configuration.Mode) throw new InvalidDataException("Store mode belongs to the original runtime");
        mode = configuration.Mode;
        var builtInOnly = UsesBuiltInOnly(configuration);
        CapabilityBinding? Resolve(CapabilityBinding? binding)
        {
            if (binding is null) return null;
            // Protocol defaults also normalize an old candidate before exact
            // provider admission. Frozen requests never enter this path.
            if (builtInOnly && registry.FindIntegrationForProtocol(binding) is { } integration)
                binding = integration.NormalizeProtocol(configuration, binding);
            return binding.Package is null && registry.FindBinding(binding.Implementation) is { } provider && provider.Contract == binding.Contract ?
                provider.Normalize(launch, configuration, binding) : binding;
        }
        return configuration with { CapabilityInstances = configuration.CapabilityInstances?.Select(x => Resolve(x)!).ToArray(), Wcs = Resolve(configuration.Wcs), Plc = Resolve(configuration.Plc), Images = configuration.Images is { } i ? i with { Uploader = Resolve(i.Uploader)! } : null };
    }

    bool ReaderAllowedInBuiltInOnlyMode(ParcelConfiguration c, IBuiltInReaderProvider provider) =>
        provider.AllowedInBuiltInOnlyMode || c.Mode == RunMode.Simulation && policy.AllowedSimulationReaderDrivers.Contains(provider.DriverId);

    public void Validate(ParcelConfiguration c) => _ = ValidateDeployment(c);
    public void ValidateStructure(ParcelConfiguration c) => _ = ValidateDeployment(c, false);
    DeploymentQualification? ValidateDeployment(ParcelConfiguration c, bool requireQualification = true)
    {
        RequireProduct(c);
        var builtInOnly = UsesBuiltInOnly(c);
        var readers = ParcelBindings.Readers(c);
        if (builtInOnly)
        {
            if (c.Extensions is not null || readers.Any(r => r.Driver.StartsWith("extension:", StringComparison.Ordinal)) ||
                ParcelBindings.All(c).Any(b => b.Package is not null || b.Module is not null || b.Implementation.StartsWith("extension:", StringComparison.Ordinal)))
                throw new InvalidDataException("SM当前仅使用随软件安装的内置驱动和组件，不支持外置扩展配置；原配置保留。");
            foreach (var reader in readers)
                if (registry.FindReader(reader.Driver) is not { } provider || !ReaderAllowedInBuiltInOnlyMode(c, provider))
                    throw new InvalidDataException("SM当前只支持内置MVS采图与软件识别；不支持原相机配置，原件保留。");
        }
        var setupErrors = ParcelConnectionSetup.Validate(c);
        if (setupErrors.Length != 0) throw new InvalidDataException(string.Join("; ", setupErrors));
        var requiresActiveQualification = requireQualification && ParcelConnectionSetup.HasActiveConnections(c) && !builtInOnly;
        var inventoryErrors = ParcelConfigurationRules.ValidateCapabilityInventory(c);
        if (inventoryErrors.Length != 0) throw new InvalidDataException(string.Join("; ", inventoryErrors));
        RegisterContracts();
        if (store is not null && mode != c.Mode) throw new InvalidDataException("Cannot change the extension store mode within a running owner");
        mode = c.Mode;
        if (requireQualification && c.Mode == RunMode.Production) RequireProductionManagement(launch.DataRoot);
        if (requireQualification && c.Mode == RunMode.Production && readers.Any(r => registry.FindReader(r.Driver)?.IsSimulationOnly == true))
            throw new InvalidDataException("Production NotReady: a real reader is required");
        DeploymentQualification? candidateQualification = null;
        if (requiresActiveQualification && (c.Mode == RunMode.Production || c.Extensions?.Packages.Any(p => Store().VerifyInstalled(p).Manifest.Prerequisites.Length > 0) == true))
        {
            _ = Store();
            var hash = Wire.Hash(Wire.Encode(c));
            candidateQualification = DeploymentQualifications.Read(Path.Combine(Path.GetDirectoryName(trustFile!)!, "deployment-qualification." + hash + ".json"), trustPolicy!, mode, hash, Wire.FileHash(typeof(IParcelApplication).Assembly.Location));
        }
        // Preflight the selected protocol first for precise diagnostics. Provider
        // admission and construction still require the exact registered binding.
        if (c.Wcs is { } selected && !ParcelConnectionSetup.IsDeferred(c, selected.InstanceId))
            registry.FindIntegrationForProtocol(selected)?.ValidateSelectedProtocol(c, selected);
        var bound = ParcelBindings.Bound(c).ToArray();
        var bindings = bound.Concat((c.CapabilityInstances ?? [])).DistinctBy(x => x.InstanceId).ToArray();
        if (compatibility is not null)
        {
            if (compatibility.ApplicationId != c.ApplicationId) throw new InvalidDataException("Business compatibility belongs to another Application");
            _ = compatibility.Require(SelectedProviders(c));
        }
        var activeReaders = readers.Where(r => !ParcelConnectionSetup.IsDeferred(c, r.DeviceId)).ToArray();
        foreach (var reader in readers)
        {
            if (reader.Driver.StartsWith("extension:", StringComparison.Ordinal)) continue;
            var provider = registry.RequireReader(reader.Driver);
            if (!ParcelConnectionSetup.IsDeferred(c, reader.DeviceId)) provider.ValidateBinding(c, reader);
        }
        registry.ValidateReaderGroups(c, activeReaders);
        if (c.Extensions is not null)
        {
            var readerBindings = ParcelBindings.Readers(c).Where(r => r.Driver.StartsWith("extension:", StringComparison.Ordinal)).ToArray();
            // Existing validator checks signatures/closure/reader role. New device kinds have
            // their own typed bindings below, never counterfeit observation DeviceIds.
            var observationInstances = c.Extensions.Instances.Where(x => readerBindings.Any(r => r.Driver == "extension:" + x.Id)).ToArray();
            var observationDeployment = c.Extensions with { Instances = observationInstances };
            var deferredDevices = (c.DeferredConnections ?? []).ToHashSet(StringComparer.Ordinal);
            var activePackages = new HashSet<string>(StringComparer.Ordinal);
            void IncludeActive(string id)
            {
                if (!activePackages.Add(id)) return;
                foreach (var dependency in Store().VerifyInstalled(c.Extensions.Packages.Single(p => p.Id == id)).Manifest.Dependencies) IncludeActive(dependency.Id);
            }
            foreach (var id in observationInstances.Where(i => !deferredDevices.Contains(i.DeviceId)).Select(i => i.PackageId)
                .Concat(bindings.Where(b => b.Package is not null && !deferredDevices.Contains(b.InstanceId)).Select(b => b.Package!.Id))) IncludeActive(id);
            bool ActivePackage(string id) => activePackages.Contains(id);
            var errors = ExtensionDeployment.Validate(observationDeployment, readerBindings, Store(), p => { if (requiresActiveQualification && ActivePackage(p.Manifest.Id)) DeploymentQualifications.RequirePrerequisites(candidateQualification, p); }, deferredDevices);
            if (errors.Length != 0) throw new InvalidDataException(string.Join("; ", errors));
            if (c.Extensions.Instances.Length != observationInstances.Length) throw new InvalidDataException("Unbound observation extension instance");
            foreach (var package in c.Extensions.Packages)
            {
                var verified = Store().VerifyInstalled(package);
                if (requiresActiveQualification && ActivePackage(package.Id)) DeploymentQualifications.RequirePrerequisites(candidateQualification, verified);
                if (requireQualification && c.Mode == RunMode.Production)
                    foreach (var instance in observationInstances.Where(x => x.PackageId == package.Id && !deferredDevices.Contains(x.DeviceId)))
                        RequireReal(verified.Manifest.Modules.Single(x => x.Id == instance.ModuleId), candidateQualification);
            }
            foreach (var package in c.Extensions.Packages)
                if (!bindings.Any(x => x.Package == package) && !observationInstances.Any(x => x.PackageId == package.Id) &&
                    !c.Extensions.Packages.Any(x => Store().VerifyInstalled(x).Manifest.Dependencies.Contains(package)))
                    throw new InvalidDataException("Unbound package in application deployment");
        }
        else if (ParcelBindings.Readers(c).Any(r => r.Driver.StartsWith("extension:", StringComparison.Ordinal)) || bindings.Any(x => x.Package is not null)) throw new InvalidDataException("Extension binding requires an exact deployment");
        foreach (var camera in bound.Where(b => b.Kind == "camera"))
        {
            var position = ParcelBindings.Positions(c).SingleOrDefault(p => ParcelBindings.Camera(c, p.Id)?.InstanceId == camera.InstanceId);
            var sharedReader = ParcelBindings.Readers(c).SingleOrDefault(r => r.DeviceId == camera.Target);
            if (sharedReader is null) continue;
            if (position?.ReaderDeviceId != sharedReader.DeviceId) throw new InvalidDataException("Capture cannot borrow another position's physical reader");
            if (registry.FindReader(sharedReader.Driver)?.SupportsSharedImageCapture == false) throw new InvalidDataException("TCP条码连接不提供图像采集能力");
            if (!sharedReader.Driver.StartsWith("extension:", StringComparison.Ordinal) ? camera.Package is not null :
                c.Extensions!.Instances.Single(x => "extension:" + x.Id == sharedReader.Driver).PackageId != camera.Package?.Id)
                throw new InvalidDataException("Shared capture implementation must belong to the same exact reader package");
        }
        var deviceCapabilities = bound.Where(b => b.Kind is "weight" or "plc" or "camera").ToArray();
        if (deviceCapabilities.GroupBy(b => b.Target).Any(g => g.Count() > 1) || deviceCapabilities.Any(b => b.Kind != "camera" && ParcelBindings.Readers(c).Any(r => r.DeviceId == b.Target)))
            throw new InvalidDataException("Physical device cannot be occupied by incompatible capability roles");
        if (bound.Any(x => !x.Enabled)) throw new InvalidDataException("A bound provider must be enabled");
        foreach (var b in bindings)
        {
            if (b.Package is null)
            {
                var provider = registry.RequireBinding(b);
                if (ParcelConnectionSetup.IsDeferred(c, b.InstanceId))
                {
                    if (provider is not IBuiltInIntegrationProvider integration)
                        throw new InvalidDataException("延后配置的RCS仍须使用当前已支持的精确实现和合同。");
                    integration.ValidateDeferred(b);
                }
                else
                {
                    provider.Validate(c, b, requireQualification && (provider.IsSimulation || !builtInOnly), candidateQualification);
                    if (bound.Any(x => x.InstanceId == b.InstanceId)) ValidateRequirements(c, provider.Requirements, b.Kind);
                }
                continue;
            }
            if (ParcelConnectionSetup.IsDeferred(c, b.InstanceId))
                throw new InvalidDataException("延后配置的RCS仍须使用当前已支持的精确实现和合同。");
            if (c.Extensions?.Packages.Contains(b.Package) != true) throw new InvalidDataException("Capability package absent from exact deployment");
            if (b.Version != b.Package.Version || b.Implementation != "extension:" + b.Package.Id + "/" + b.Module) throw new InvalidDataException("Capability implementation/version must name its exact installed package/module");
            var manifest = Store().VerifyInstalled(b.Package).Manifest;
            var module = manifest.Modules.SingleOrDefault(x => x.Id == b.Module && x.Kind == b.Kind) ?? throw new InvalidDataException("Typed capability/module missing");
            var errors = ExtensionSchema.Validate(module.Parameters, b.Parameters);
            if (requireQualification && c.Mode == RunMode.Production && b.Enabled) RequireReal(module, candidateQualification);
            if (requireQualification) DeploymentQualifications.RequirePrerequisites(candidateQualification, Store().VerifyInstalled(b.Package));
            if (module.ContractIdentity is not null && module.ContractIdentity != b.Contract) throw new InvalidDataException("Selected binding contract differs from signed provider contract");
            if (bound.Any(x => x.InstanceId == b.InstanceId)) ValidateRequirements(c, module.Requirements ?? new(), b.Kind);
            if (errors.Length != 0) throw new InvalidDataException("Capability schema/native SDK prerequisite evidence is unavailable: " + string.Join("; ", errors));
            if (bindings.Count(x => x.Package == b.Package && x.Module == b.Module) > module.MaximumInstances) throw new InvalidDataException("Capability instance quota exceeded");
        }
        return candidateQualification;
    }
    public ProviderVersion[] SelectedProviders(ParcelConfiguration c)
    {
        var selected = ParcelBindings.All(c).Where(b => b.Enabled).Select(b => new ProviderVersion(b.Kind, b.Implementation, b.Version, b.Contract)).ToList();
        foreach (var reader in ParcelBindings.Readers(c))
        {
            if (registry.FindReader(reader.Driver) is { } provider)
            {
                var descriptor = provider.Descriptor ?? throw new InvalidDataException("Missing selected reader declaration: " + reader.Driver);
                selected.Add(new(descriptor.Kind, descriptor.Implementation, descriptor.Version, descriptor.Contract));
                continue;
            }
            if (!reader.Driver.StartsWith("extension:", StringComparison.Ordinal)) _ = registry.RequireReader(reader.Driver);
            var instance = c.Extensions?.Instances.SingleOrDefault(x => reader.Driver == "extension:" + x.Id) ?? throw new InvalidDataException("Missing selected reader instance");
            var package = c.Extensions!.Packages.Single(x => x.Id == instance.PackageId);
            var module = Store().VerifyInstalled(package).Manifest.Modules.Single(x => x.Id == instance.ModuleId);
            selected.Add(new("observation", "extension:" + package.Id + "/" + module.Id, package.Version, module.ContractIdentity ?? "CONTROLLED-OBSERVATION-v1"));
        }
        return selected.Distinct().ToArray();
    }
    internal static void RequireReal(ExtensionModule module, DeploymentQualification? qualification)
    {
        if (module.Assurance is not { SchemaVersion: 1, Simulation: false } || module.Requirements is null || string.IsNullOrWhiteSpace(module.ContractIdentity) || !ExtensionSchema.IsHash(module.Assurance.ProtocolEvidence) || qualification?.ApprovalEvidence.Any(x => string.Equals(x.Sha256, module.Assurance.ProtocolEvidence, StringComparison.OrdinalIgnoreCase)) != true)
            throw new InvalidDataException("Production NotReady: provider must explicitly declare real identity, contract, capability semantics and protocol evidence: " + module.Id);
    }
    internal static void ValidateRequirements(ParcelConfiguration c, CapabilityRequirements requirements, string kind)
    {
        if (kind is "integration" or "plc" or "camera" or "upload" && !requirements.BusinessAcceptance)
            throw new InvalidDataException("Application delivery requires authoritative business acceptance for " + kind);
        if (kind == "plc" && (!requirements.PhysicalConfirmation || !requirements.Query && !requirements.CompletesOnSubmit))
            throw new InvalidDataException("Application delivery requires physical completion on submit or authoritative original query");
        if (kind is "login" or "weight" or "camera" && (requirements.MeasuredWeight || requirements.ImageAddress) || kind == "upload" && requirements.ImageAddress)
            throw new InvalidDataException("Declared inputs create an impossible pre-capture/authentication or self-upload dependency");
        if (requirements.MeasuredWeight && c.Weight.Source != WeightSource.Measured)
            throw new InvalidDataException("Selected protocol requires an actual same-parcel measurement");
        if (requirements.ImageAddress && (c.Images?.Uploader is null || c.Images.Strategy != UploadStrategy.BeforeMain))
            throw new InvalidDataException("Selected protocol requires a confirmed image address before main flow");
    }
    public CapabilityRequirements Requirements(CapabilityBinding binding)
    {
        RegisterContracts();
        return binding.Package is null ? BindingMetadata(binding).Requirements :
            Store().VerifyInstalled(binding.Package).Manifest.Modules.Single(x => x.Id == binding.Module).Requirements ?? new(Query: true);
    }

    public CapabilityCatalog Catalog()
    {
        RegisterContracts();
        if (!policy.SupportsExternalExtensions) return new(registry.Catalog(policy), [], []);
        try { return InstalledCatalog(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException)
        {
            // Inventory is an optional read model, never capability admission. Existing
            // exact bindings remain unresolved and Validate/Create still fail closed.
            return new(registry.Catalog(policy), [], []) { InventoryDiagnostic = "扩展库存未配置或不可用：" + ex.Message + "。内置配置可读取；原扩展绑定仍须恢复同一可信包后才能保存或启动。" };
        }
    }
    CapabilityCatalog InstalledCatalog()
    {
        var settingsFile = Path.Combine(launch.ConfigurationDirectory, "extension-store.json");
        var installed = store is not null || OperatingSystem.IsWindows() || File.Exists(settingsFile) ? Store().Inventory().Where(x => x.Status == "Installed").ToArray() : [];
        // Verify signed declarative manifests only. Catalog reads never load provider code.
        var manifests = installed.Select(x => (x.Package, Manifest: Store().VerifyInstalled(x.Package).Manifest)).ToArray();
        var descriptors = manifests.SelectMany(x => x.Manifest.Modules.Select(m =>
            new CapabilityDescriptor(m.Kind, "extension:" + x.Package.Id + "/" + m.Id, x.Package.Version, m.ContractIdentity ?? "PARCEL-REFERENCE-v1", m.Parameters, m.Requirements ?? new(), x.Package, m.Id)));
        return new(registry.Catalog(policy).Concat(descriptors).ToArray(), installed.Select(x => x.Package.Id + "@" + x.Package.Version).ToArray(),
            manifests.Select(x => new CapabilityPackage(x.Package, x.Manifest.Dependencies)).ToArray());
    }
    internal ApplicationRelease ReadPublishedRelease(string appRoot)
    {
        var release = Wire.Decode<ApplicationRelease>(File.ReadAllText(Path.Combine(appRoot, "application-release.json")));
        ApplicationReleaseValidation.ValidateIfPublished(appRoot, release.ApplicationId, new(release.Views, release.Layouts), businessAssembly ?? throw new InvalidDataException("Published verification requires the actual business assembly"));
        return release;
    }
    public async Task<BusinessReceipt> InstallAsync(string file, string sha256)
    {
        RequireExternalExtensions();
        if (OperatingSystem.IsWindows())
        {
            var appRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "app"));
            var release = ReadPublishedRelease(appRoot);
            MachineRuntimePolicy.RequireProtectedApplicationInstaller(appRoot, release.Files.Keys.Append("application-release.json").ToArray());
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(appRoot, "Ipc.Assembly.exe")) { UseShellExecute = true, Verb = "runas", WorkingDirectory = appRoot };
            foreach (var value in new[] { "--action", "extension-install", "--mode", mode.ToString(), "--file", Path.GetFullPath(file), "--sha256", sha256 }) start.ArgumentList.Add(value);
            using var process = System.Diagnostics.Process.Start(start) ?? throw new IOException("Protected installation helper did not start");
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("Installation helper failed; preserve original installation journal");
            var entry = Store().Inventory().SingleOrDefault(x => x.PackageSha256 == sha256 && x.Status == "Installed") ?? throw new IOException("Installation was not confirmed by the original store registration");
            return new(entry.Status, entry.Reason, 0);
        }
        RegisterContracts(); var installed = Store().Install(file, sha256);
        return new(installed.Status, installed.Reason, 0);
    }
    public IDisposable? ProtectConfiguration(ParcelConfiguration c)
    {
        if (c.Extensions is not { Packages.Length: > 0 }) return null;
        Store().RegisterConsumer(Path.Combine(launch.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId, c.ApplicationId + ".db"));
        return Store().AcquireUse(c.Extensions.Packages);
    }
    public void RegisterBackup(string database, ParcelConfiguration c)
    {
        if (store is not null || c.Extensions is { Packages.Length: > 0 }) Store().RegisterConsumer(database);
    }
    public async Task<BusinessReceipt> ManageAsync(string action, string identity)
    {
        RequireExternalExtensions();
        if (action is not ("extension-recover" or "extension-uninstall")) throw new InvalidDataException("Unsupported controlled extension maintenance");
        if (OperatingSystem.IsWindows())
        {
            var appRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "app"));
            var release = ReadPublishedRelease(appRoot);
            MachineRuntimePolicy.RequireProtectedApplicationInstaller(appRoot, release.Files.Keys.Append("application-release.json").ToArray());
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(appRoot, "Ipc.Assembly.exe")) { UseShellExecute = true, Verb = "runas", WorkingDirectory = appRoot };
            foreach (var arg in new[] { "--action", action, "--mode", mode.ToString(), action == "extension-recover" ? "--operation" : "--package", identity }) start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start) ?? throw new IOException("Protected maintenance helper did not start");
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("Protected maintenance failed; preserve original operation journal");
            if (action == "extension-recover")
            {
                var op = Store().Operations().Single(x => x.Id == identity);
                return new(op.State, op.Error ?? "Original operation read back", 0);
            }
            var package = Wire.Decode<PackageReference>(identity);
            var result = Store().Inventory().Single(x => x.Package == package);
            if (result.Status != "Uninstalled") throw new IOException("Uninstall was not confirmed by original registration");
            return new(result.Status, result.Reason, 0);
        }
        return await Task.Run(() =>
        {
            var manager = Store();
            if (action == "extension-recover")
            {
                var result = manager.Recover(identity, refreshReferences: () => manager.InspectConsumers(Ipc.Assembly.ExtensionConsumerReaders.Inspect));
                return new BusinessReceipt(result.Status, result.Reason, 0);
            }
            var package = Wire.Decode<PackageReference>(identity);
            manager.Uninstall(package, () => manager.InspectConsumers(Ipc.Assembly.ExtensionConsumerReaders.Inspect));
            return new BusinessReceipt("Uninstalled", "Exact package retired; immutable identity retained", 0);
        }).ConfigureAwait(false);
    }
    public ParcelPorts Create(ParcelConfiguration c)
    {
        qualification = ValidateDeployment(c);
        if (qualification is { } decision)
            foreach (var evidence in decision.ApprovalEvidence.Concat(decision.Prerequisites.SelectMany(x => x.Files)).DistinctBy(x => x.Path))
            {
                prerequisiteHolds.Add(new FileStream(evidence.Path, FileMode.Open, FileAccess.Read, FileShare.Read));
                if (!string.Equals(DeploymentQualifications.FileHash(evidence.Path), evidence.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Qualified installed prerequisite changed during acquisition");
            }
        var deviceIds = ParcelBindings.Readers(c).Where(r => !ParcelConnectionSetup.IsDeferred(c, r.DeviceId)).SelectMany(x => new[] { "device:" + x.DeviceId, "reader:" + x.DeviceId })
            .Concat(ParcelBindings.Bound(c).Where(x => x.Kind is "weight" or "plc" or "camera").SelectMany(x => new[] { "device:" + x.Target, x.Kind + ":" + x.Target })).Distinct();
        deviceIds = deviceIds.Concat(ParcelBindings.Readers(c).Where(r => !ParcelConnectionSetup.IsDeferred(c, r.DeviceId))
            .SelectMany(r => registry.FindReader(r.Driver)?.ResourceLeaseKeys(r) ?? []));
        var resources = deviceIds.ToArray();
        if (resources.Length > 0)
        {
            var resourceRoot = MachineRuntimePolicy.ResolveLeaseRoot(OperatingSystem.IsWindows() ? null : Path.Combine(launch.DataRoot, "Simulation", "resource-leases"));
            devices = new(resourceRoot, resources);
        }
        if (c.Extensions is { Packages.Length: > 0 } deployment)
        {
            Store().RegisterConsumer(Path.Combine(launch.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId, c.ApplicationId + ".db"));
            use = Store().AcquireUse(deployment.Packages); loading = new(Store(), qualification);
        }
        Dictionary<string, IControlledObservationSource> readers = [];
        foreach (var binding in ParcelBindings.Readers(c))
        {
            IControlledObservationSource source;
            if (ParcelConnectionSetup.IsDeferred(c, binding.DeviceId)) source = new DeferredObservationSource(binding.DeviceId);
            else if (registry.FindReader(binding.Driver) is { } provider) source = provider.Create(binding);
            else
            {
                var instance = c.Extensions!.Instances.Single(x => "extension:" + x.Id == binding.Driver);
                var package = c.Extensions.Packages.Single(x => x.Id == instance.PackageId);
                source = loading!.Create(package, Store().VerifyInstalled(package).Manifest.Modules.Single(x => x.Id == instance.ModuleId), instance);
            }
            constructed.Add(source); Boundary?.Invoke("ReaderConstructed:" + binding.DeviceId);
            if (source.DeviceId != binding.DeviceId) throw new InvalidDataException("Reader returned the wrong stable identity");
            readers.Add(binding.DeviceId, source);
        }
        var reader = readers[c.Reader.DeviceId];
        Dictionary<string, string> hashes = [];
        T Own<T>(CapabilityBinding b, Func<ICapabilityPort> builtIn, Func<ExtensionLoading, ExtensionModule, T> external) where T : class, ICapabilityPort
        {
            ICapabilityPort value;
            if (b.Package is null) { value = builtIn(); constructed.Add(value); hashes.Add(b.InstanceId, BuiltInFingerprint(value.GetType())); }
            else
            {
                var module = Store().VerifyInstalled(b.Package).Manifest.Modules.Single(x => x.Id == b.Module);
                value = external(loading!, module); constructed.Add(value);
                hashes.Add(b.InstanceId, Wire.Hash(b.Package.ManifestSha256 + ":" + Wire.FileHash(typeof(IParcelApplication).Assembly.Location) + ":" + Wire.FileHash(typeof(ExtensionStore).Assembly.Location)));
            }
            if (value.InstanceId != b.InstanceId || value is not T typed)
                throw new InvalidDataException("Capability returned a mismatched type or identity");
            return typed;
        }
        ICapabilityPort CreateDevice(CapabilityBinding binding, string? dataDirectory = null) =>
            registry.RequireBinding(binding) is IBuiltInDeviceProvider device ? device.Create(binding, dataDirectory) :
                throw new InvalidDataException("No registered device constructor for " + binding.Implementation);

        Dictionary<string, ICapabilityPort> devicePorts = [];
        foreach (var scaleBinding in ParcelBindings.Bound(c).Where(b => b.Kind == "weight"))
            devicePorts.Add(scaleBinding.InstanceId, Own<IControlledWeightSource>(scaleBinding, () => CreateDevice(scaleBinding), (l, m) => l.CreateFactory<IWeightExtension>(scaleBinding.Package!, m, "weight").Create(scaleBinding)));
        var weight = c.Scale is null ? null : (IControlledWeightSource)devicePorts[c.Scale.InstanceId];
        var wcs = c.Wcs is { } w ? Own<IWcsPort>(w,
            () => ParcelConnectionSetup.IsDeferred(c, w.InstanceId) ? new DeferredWcsPort(w.InstanceId) :
                registry.RequireBinding(w) is IBuiltInIntegrationProvider provider ? provider.Create(c, w) :
                throw new InvalidDataException("No registered WCS constructor for " + w.Implementation),
            (l, m) => l.CreateFactory<IWcsExtension>(w.Package!, m, "integration").Create(w)) : null;
        if (wcs is IWcsRuntimeConfiguration configuredWcs) configuredWcs.ConfigureRuntime(c);
        var plc = c.Plc is { } p ? Own<IPlcPort>(p, () => CreateDevice(p), (l, m) => l.CreateFactory<IPlcExtension>(p.Package!, m, "plc").Create(p)) : null;
        var login = c.Login is { } a ? Own<IOperatorAuthenticator>(a, () => CreateDevice(a), (l, m) => l.CreateFactory<ILoginExtension>(a.Package!, m, "login").Create(a)) : null;
        foreach (var cameraBinding in ParcelBindings.Bound(c).Where(b => b.Kind == "camera"))
        {
            var path = Path.Combine(launch.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId);
            var sameReader = readers.GetValueOrDefault(cameraBinding.Target);
            var imagePort = Own<IImageCamera>(cameraBinding,
                () => sameReader is not null ? BindReader(sameReader, cameraBinding, path) : CreateDevice(cameraBinding, path),
                (l, m) => sameReader is not null ? BindReader(sameReader, cameraBinding, path) : l.CreateFactory<ICameraExtension>(cameraBinding.Package!, m, "camera").Create(cameraBinding, path));
            devicePorts.Add(cameraBinding.InstanceId, imagePort);
        }
        var camera = c.Images is null ? null : (IImageCamera)devicePorts[c.Images.Camera.InstanceId];
        var uploader = c.Images?.Uploader is { } u ? Own<IImageUploader>(u, () => CreateDevice(u), (l, m) => l.CreateFactory<IUploadExtension>(u.Package!, m, "upload").Create(u)) : null;
        // Software administration has its own credential store and never falls back to this port.
        transferred = true; return new(reader, weight, wcs, plc, hashes, login, camera, uploader, readers, devicePorts);
    }
    // SM: a saved camera or RCS change rebuilds only that built-in port; everything else keeps running.
    // The runtime owns what is returned. Anything this cannot rebuild exactly (extensions, a qualified
    // deployment, an unconfigured or non-built-in device) returns null and the caller stages a restart.
    public ParcelLiveDevices? CreateLive(ParcelConfiguration c, IReadOnlyCollection<string> readerIds, bool integration)
    {
        if (!transferred || c.ApplicationId != policy.ApplicationId || !policy.SupportsLiveReload ||
            c.Extensions is not null || qualification is not null) return null;
        Dictionary<string, IControlledObservationSource> readers = [];
        Dictionary<string, string> hashes = [];
        IWcsPort? liveWcs = null;
        try
        {
            foreach (var id in readerIds)
            {
                var binding = ParcelBindings.Readers(c).SingleOrDefault(r => r.DeviceId == id);
                if (binding is null || ParcelConnectionSetup.IsDeferred(c, id) ||
                    registry.FindReader(binding.Driver) is not { SupportsLiveReload: true } provider ||
                    (UsesBuiltInOnly(c) && !ReaderAllowedInBuiltInOnlyMode(c, provider))) return Release();
                if (readers.ContainsKey(id)) throw new InvalidDataException("Duplicate live reader identity");
                var source = provider.Create(binding);
                readers.Add(id, source);
                if (source.DeviceId != id) throw new InvalidDataException("Reader returned the wrong stable identity");
            }
            if (integration)
            {
                if (c.Wcs is not { Package: null, Module: null } w || ParcelConnectionSetup.IsDeferred(c, w.InstanceId) ||
                    registry.FindExactBinding(w) is not IBuiltInIntegrationProvider { SupportsLiveReload: true } provider) return Release();
                liveWcs = provider.Create(c, w);
                if (liveWcs.InstanceId != w.InstanceId) throw new InvalidDataException("Capability returned a mismatched identity");
                if (liveWcs is IWcsRuntimeConfiguration configuration) configuration.ConfigureRuntime(c);
                hashes.Add(w.InstanceId, BuiltInFingerprint(liveWcs.GetType()));
            }
            return new(readers, liveWcs, hashes);
        }
        catch (Exception original)
        {
            try { Release(); }
            catch (Exception cleanup) { throw new AggregateException("Live device construction and cleanup failed; factory retains failed owners", original, cleanup); }
            throw;
        }
        ParcelLiveDevices? Release()
        {
            List<Exception> failures = [];
            foreach (var resource in readers.Values.Cast<IAsyncDisposable>().Concat(liveWcs is null ? [] : new IAsyncDisposable[] { liveWcs }))
                try { resource.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                catch (Exception error) { failedLiveCleanup.Add(resource); failures.Add(error); }
            readers.Clear(); liveWcs = null;
            if (failures.Count != 0) throw new AggregateException("Live candidate cleanup failed; factory retains owners", failures);
            return null;
        }
    }
    static IImageCamera BindReader(IControlledObservationSource source, CapabilityBinding binding, string directory) => source is IReaderImageCapture provider
        ? provider.BindCapture(binding, directory) : throw new InvalidDataException("Reader does not expose controlled original-acquisition capture; a second connection is forbidden");
    static string BuiltInFingerprint(Type implementation)
    {
        Dictionary<string, string> closure = [];
        void Visit(System.Reflection.Assembly assembly)
        {
            var name = assembly.GetName().Name!;
            if (closure.ContainsKey(name)) return;
            closure.Add(name, Wire.FileHash(assembly.Location));
            foreach (var dependency in assembly.GetReferencedAssemblies().Where(x => x.Name!.StartsWith("Ipc.", StringComparison.Ordinal)))
                Visit(System.Reflection.Assembly.Load(dependency));
        }
        Visit(implementation.Assembly);
        return Wire.Hash(string.Join("\n", closure.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + ":" + x.Value)));
    }
    public async ValueTask DisposeAsync()
    {
        List<Exception> liveFailures = [];
        foreach (var resource in failedLiveCleanup.ToArray())
            try { await resource.DisposeAsync().ConfigureAwait(false); failedLiveCleanup.Remove(resource); }
            catch (Exception error) { liveFailures.Add(error); }
        if (liveFailures.Count != 0) throw new AggregateException("Live candidate release remains incomplete", liveFailures);
        // Discovery stays factory-owned after port transfer. Failed release keeps enclosing leases.
        await registry.DisposeAsync().ConfigureAwait(false);
        if (!transferred)
        {
            List<Exception> failures = [];
            foreach (var resource in constructed.ToArray().Reverse())
                try { await resource.DisposeAsync().ConfigureAwait(false); constructed.Remove(resource); } catch (Exception ex) { failures.Add(ex); }
            if (failures.Count != 0) throw new AggregateException("Partial construction cleanup failed; device/package leases retained", failures);
        }
        loading?.Dispose(); use?.Dispose(); devices?.Dispose();
        foreach (var hold in prerequisiteHolds) hold.Dispose(); prerequisiteHolds.Clear();
    }
}
