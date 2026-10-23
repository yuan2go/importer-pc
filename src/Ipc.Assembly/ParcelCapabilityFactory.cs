using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Adapters;
using Ipc.Drivers;
using Ipc.Components;
#if !DIRECTPLC
using Ipc.Integrations;
#endif
using Ipc.Platform;
namespace Ipc.Assembly;

public sealed record ParcelExtensionStoreConfiguration(string Root, string TrustFile);
public sealed class ParcelCapabilityFactory(BusinessLaunch launch, BusinessCompatibility? compatibility = null,
    System.Reflection.Assembly? businessAssembly = null, bool installer = false) : IParcelCapabilityFactory
{
    // Preserve the existing host constructor for already compiled callers.
    public ParcelCapabilityFactory(BusinessLaunch launch, BusinessCompatibility? compatibility, System.Reflection.Assembly? businessAssembly)
        : this(launch, compatibility, businessAssembly, false) { }
    ExtensionStore? store;
    ExtensionLoading? loading;
    PackageUseLease? use;
    ResourceLease? devices;
    readonly List<IAsyncDisposable> constructed = [];
    readonly List<FileStream> prerequisiteHolds = [];
    internal Action<string>? Boundary { get; set; }
    bool transferred;
    RunMode mode = RunMode.Simulation;
    ExtensionTrust? trustPolicy;
    string? trustFile;
    DeploymentQualification? qualification;
    public string? AdmissionFailure(ParcelConfiguration c) => c.Mode == RunMode.Production && (qualification is null || qualification.ExpiresAt <= DateTimeOffset.UtcNow)
        ? "Production NotReady: exact deployment qualification missing/expired" : null;
    public bool SimulationIdentity(CapabilityBinding b) => b.Package is null ? b.Implementation != "sportmaster-rcs" : Store().VerifyInstalled(b.Package).Manifest.Modules.Single(x => x.Id == b.Module).Assurance?.Simulation != false;
    public static void RegisterContracts() => ExtensionPublicContracts.Register(typeof(IPlcExtension).Assembly);
    ExtensionStore Store()
    {
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
        if (store is not null && mode != configuration.Mode) throw new InvalidDataException("Store mode belongs to the original runtime");
        mode = configuration.Mode;
        CapabilityBinding? Resolve(CapabilityBinding? b)
        {
            if (b is null || b.Implementation != "reference-" + b.Kind || b.Kind is "weight" or "login" or "camera" || b.Package is not null || b.Parameters.ContainsKey("endpoint")) return b;
            var parameters = new Dictionary<string, string>(b.Parameters);
            parameters.TryAdd("peer-root", Path.GetFullPath(Path.Combine(launch.DataRoot, "Simulation", "reference-peers")));
            return b with { Parameters = parameters };
        }
        return configuration with { CapabilityInstances = configuration.CapabilityInstances?.Select(x => Resolve(x)!).ToArray(), Wcs = Resolve(configuration.Wcs), Plc = Resolve(configuration.Plc), Images = configuration.Images is { } i ? i with { Uploader = Resolve(i.Uploader)! } : null };
    }
    public void Validate(ParcelConfiguration c) => _ = ValidateDeployment(c);
    public void ValidateStructure(ParcelConfiguration c) => _ = ValidateDeployment(c, false);
    DeploymentQualification? ValidateDeployment(ParcelConfiguration c, bool requireQualification = true)
    {
        var inventoryErrors = ParcelConfigurationRules.ValidateCapabilityInventory(c);
        if (inventoryErrors.Length != 0) throw new InvalidDataException(string.Join("; ", inventoryErrors));
        RegisterContracts();
        if (store is not null && mode != c.Mode) throw new InvalidDataException("Cannot change the extension store mode within a running owner");
        mode = c.Mode;
        if (requireQualification && c.Mode == RunMode.Production) ManagementModeBroker.RequireProduction(launch.DataRoot);
        if (requireQualification && c.Mode == RunMode.Production && (ParcelBindings.Readers(c).Any(r => r.Driver == "manual") || c.SchemaVersion != 3))
            throw new InvalidDataException("Production NotReady: real reader, provisioned software administrator and explicit schema3 topology policy are required");
        if (c.Extensions is { } malformed && (malformed.Packages is null || malformed.Instances is null || malformed.Packages.Any(x => x is null) || malformed.Instances.Any(x => x is null)))
            throw new InvalidDataException("Malformed application extension deployment");
        DeploymentQualification? candidateQualification = null;
        if (requireQualification && (c.Mode == RunMode.Production || c.Extensions?.Packages.Any(p => Store().VerifyInstalled(p).Manifest.Prerequisites.Length > 0) == true))
        {
            _ = Store();
            var hash = Wire.Hash(Wire.Encode(c));
            candidateQualification = DeploymentQualifications.Read(Path.Combine(Path.GetDirectoryName(trustFile!)!, "deployment-qualification." + hash + ".json"), trustPolicy!, mode, hash, Wire.FileHash(typeof(IParcelApplication).Assembly.Location));
        }
#if !DIRECTPLC
        if (c.Wcs?.Contract == Ipc.Integrations.SportmasterRcs.RcsConfigurationValidation.Contract)
        {
            _ = new Ipc.Integrations.SportmasterRcs.RcsTaskOptions(c.Wcs);
            _ = Ipc.Integrations.SportmasterRcs.RcsConfigurationValidation.HeartbeatMapping(c);
        }
#endif
        var bound = ParcelBindings.Bound(c).Where(b => b.InstanceId != c.MaintenanceLogin?.InstanceId).ToArray();
        var bindings = bound.Concat((c.CapabilityInstances ?? []).Where(b => b.InstanceId != c.MaintenanceLogin?.InstanceId)).DistinctBy(x => x.InstanceId).ToArray();
        if (compatibility is not null)
        {
            if (compatibility.ApplicationId != c.ApplicationId) throw new InvalidDataException("Business compatibility belongs to another Application");
            _ = compatibility.Require(SelectedProviders(c));
        }
        foreach (var r in ParcelBindings.Readers(c))
            if (r.Driver is not ("manual" or "tcp-line") && !r.Driver.StartsWith("extension:", StringComparison.Ordinal)) throw new InvalidDataException("No approved reader implementation: " + r.Driver);
        var tcpReaders = ParcelBindings.Readers(c).Where(r => r.Driver == "tcp-line").ToArray();
        foreach (var tcp in tcpReaders) Ipc.Drivers.TcpLineObservationSource.Validate(tcp);
        if (tcpReaders.GroupBy(r => Ipc.Drivers.TcpLineObservationSource.EndpointIdentity(r)).Any(g => g.Count() > 1))
            throw new InvalidDataException("device.ip: 左右相机不能连接同一个 IP 和端口");
        if (c.Extensions is not null)
        {
            if (c.Extensions.Packages is null || c.Extensions.Instances is null || c.Extensions.Packages.Any(x => x is null) || c.Extensions.Instances.Any(x => x is null) ||
                c.Extensions.Packages.Length > 64 || c.Extensions.Instances.Length > 64 || c.Extensions.Packages.Select(x => x.Id).Distinct().Count() != c.Extensions.Packages.Length)
                throw new InvalidDataException("Malformed or oversized application extension deployment");
            if (c.Extensions.Services is { Length: > 0 } || c.Extensions.Ports is { Length: > 0 }) throw new InvalidDataException("Parcel capabilities are owned by their typed bindings; duplicate service binding authority is forbidden");
            var readerBindings = ParcelBindings.Readers(c).Where(r => r.Driver.StartsWith("extension:", StringComparison.Ordinal)).ToArray();
            // Existing validator checks signatures/closure/reader role. New device kinds have
            // their own typed bindings below, never counterfeit observation DeviceIds.
            var observationInstances = c.Extensions.Instances.Where(x => readerBindings.Any(r => r.Driver == "extension:" + x.Id)).ToArray();
            var observationDeployment = c.Extensions with { Instances = observationInstances };
            var errors = ExtensionDeployment.Validate(observationDeployment, readerBindings, Store(), p => { if (requireQualification) DeploymentQualifications.RequirePrerequisites(candidateQualification, p); });
            if (errors.Length != 0) throw new InvalidDataException(string.Join("; ", errors));
            if (c.Extensions.Instances.Length != observationInstances.Length) throw new InvalidDataException("Unbound observation extension instance");
            if (observationInstances.Where(x => x.PackageId == "hikrobot.idmvs-reader").GroupBy(x => x.Parameters.GetValueOrDefault("device.ip")).Any(g => g.Count() > 1))
                throw new InvalidDataException("驱动/device.ip: 左右读码实例不能占用同一物理IPv4地址");
            foreach (var instance in observationInstances)
            {
                if (instance.PackageId == "hikrobot.idmvs-reader" && instance.Parameters.TryGetValue("device.ip", out var ip) &&
                    (!System.Net.IPAddress.TryParse(ip, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || address.ToString() != ip || ip is "0.0.0.0" or "255.255.255.255"))
                    throw new InvalidDataException("驱动/" + instance.Id + "/device.ip: 需要规范的设备IPv4地址");
            }
            foreach (var package in c.Extensions.Packages)
            {
                var verified = Store().VerifyInstalled(package);
                if (requireQualification) DeploymentQualifications.RequirePrerequisites(candidateQualification, verified);
                if (requireQualification && c.Mode == RunMode.Production)
                    foreach (var instance in observationInstances.Where(x => x.PackageId == package.Id))
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
            if (sharedReader.Driver == "tcp-line") throw new InvalidDataException("TCP条码连接不提供图像采集能力");
            if (sharedReader.Driver == "manual" && camera.Package is not null || sharedReader.Driver != "manual" &&
                c.Extensions!.Instances.Single(x => "extension:" + x.Id == sharedReader.Driver).PackageId != camera.Package?.Id)
                throw new InvalidDataException("Shared capture implementation must belong to the same exact reader package");
        }
        var deviceCapabilities = bound.Where(b => b.Kind is "weight" or "plc" or "camera").ToArray();
        if (deviceCapabilities.GroupBy(b => b.Target).Any(g => g.Count() > 1) || deviceCapabilities.Any(b => b.Kind != "camera" && ParcelBindings.Readers(c).Any(r => r.DeviceId == b.Target)))
            throw new InvalidDataException("Physical device cannot be occupied by incompatible capability roles");
        if (bound.Any(x => !x.Enabled)) throw new InvalidDataException("A bound provider must be enabled");
        foreach (var b in bindings.DistinctBy(x => x.InstanceId))
        {
#if !DIRECTPLC
            if (b.Package is null && b.Implementation == "sportmaster-rcs" && b.Version == "1.3.1" && b.Contract == Ipc.Integrations.SportmasterRcs.RcsConfigurationValidation.Contract)
            {
                _ = new Ipc.Integrations.SportmasterRcs.RcsTaskOptions(b);
                if (requireQualification && c.Mode == RunMode.Production && candidateQualification is null) throw new InvalidDataException("RCS生产部署资格尚未完成");
                continue;
            }
#endif
            if (requireQualification && c.Mode == RunMode.Production && b.Enabled && b.Package is null) throw new InvalidDataException("Production NotReady: reference capability " + b.InstanceId);
            if (b.Package is null && !(b.Kind == "camera" && ParcelBindings.Readers(c).Any(r => r.DeviceId == b.Target)) && (!Uri.TryCreate(b.Target, UriKind.Absolute, out var target) || target.Scheme != "reference")) throw new InvalidDataException("Reference capability target must use explicit reference:// identity");
            if (b.Package is null && b.Parameters.TryGetValue("endpoint", out var endpoint) &&
                (b.Kind is not ("integration" or "plc" or "upload") || !Uri.TryCreate(endpoint, UriKind.Absolute, out var peerUri) || peerUri.Scheme != "http" || peerUri.Host != "127.0.0.1" || peerUri.AbsolutePath != "/parcel-reference/" || peerUri.UserInfo.Length != 0 || peerUri.Query.Length != 0 || peerUri.Fragment.Length != 0))
                throw new InvalidDataException("Controlled reference HTTP peer must be literal loopback with an exact path");
            if (b.Package is null && b.Kind is ("login" or "camera" or "upload"))
            {
                if (b.Implementation != "reference-" + b.Kind || b.Version != "1.0.0" || b.Contract != "PARCEL-REFERENCE-v1")
                    throw new InvalidDataException("Optional capability only has an explicit reference implementation; signed real provider binding requires approved policy/contract");
                var optionalAllowed = b.Kind == "login" ? new[] { "subjects", "reply-mode" } : b.Kind == "camera" ? new[] { "decision", "reply-mode" } : new[] { "decision", "reply-mode", "peer-root", "endpoint" };
                if (b.Parameters.Keys.Any(x => !optionalAllowed.Contains(x)) ||
                    b.Kind == "login" && (b.Parameters.GetValueOrDefault("subjects") is not { Length: > 0 and <= 1024 } subjects || subjects.Split(',').Any(x => !ExtensionSchema.IsId(x)) || b.Parameters.GetValueOrDefault("reply-mode", "normal") is not ("normal" or "unavailable")) ||
                    b.Kind != "login" && (b.Parameters.GetValueOrDefault("decision", "accept") is not ("accept" or "reject") || b.Parameters.GetValueOrDefault("reply-mode", "normal") is not ("normal" or "lose-after-commit")) ||
                    b.Kind == "upload" && !b.Parameters.ContainsKey("endpoint") && (!b.Parameters.TryGetValue("peer-root", out var peer) || !Path.IsPathFullyQualified(peer)))
                    throw new InvalidDataException("Invalid bounded reference optional capability parameters");
                continue;
            }
            if (b.Package is not null)
            {
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
            else
            {
                if (b.Implementation != "reference-" + b.Kind || b.Version != "1.0.0" || b.Contract != "PARCEL-REFERENCE-v1") throw new InvalidDataException("No approved exact capability implementation/contract");
                var allowed = b.Kind == "weight" ? Array.Empty<string>() : b.Kind == "plc" ? ["decision", "reply-mode", "complete-on-query", "peer-root", "endpoint"] : ["decision", "reply-mode", "slot", "peer-root", "endpoint"];
                if (b.Kind != "weight" && b.Parameters.TryGetValue("peer-root", out var peerRoot) && !Path.IsPathFullyQualified(peerRoot)) throw new InvalidDataException("Reference peer location must be an explicit absolute frozen parameter");
                if (b.Parameters.Keys.Any(x => !allowed.Contains(x)) || b.Parameters.GetValueOrDefault("decision", "accept") is not ("accept" or "reject") ||
                    b.Parameters.GetValueOrDefault("reply-mode", "normal") is not ("normal" or "lose-after-commit") ||
                    b.Parameters.GetValueOrDefault("complete-on-query", "false") is not ("true" or "false") ||
                    b.Parameters.TryGetValue("slot", out var slot) && (string.IsNullOrWhiteSpace(slot) || slot.Length > 64 || slot.Any(char.IsControl)))
                    throw new InvalidDataException("Invalid reference protocol parameters");
            }
            if (b.Package is null)
            {
                var descriptor = ReferenceCapabilityCatalog.Providers.Single(x => x.Kind == b.Kind);
                var errors = ExtensionSchema.Validate(descriptor.Parameters, b.Parameters);
                if (errors.Length != 0) throw new InvalidDataException(string.Join("; ", errors));
                if (bound.Any(x => x.InstanceId == b.InstanceId)) ValidateRequirements(c, descriptor.Requirements, b.Kind);
            }
        }
        return candidateQualification;
    }
    public ProviderVersion[] SelectedProviders(ParcelConfiguration c)
    {
        var selected = ParcelBindings.All(c).Where(b => b.Enabled && b.InstanceId != c.MaintenanceLogin?.InstanceId).Select(b => new ProviderVersion(b.Kind, b.Implementation, b.Version, b.Contract)).ToList();
        foreach (var reader in ParcelBindings.Readers(c))
        {
            if (reader.Driver == "tcp-line") { selected.Add(new("observation", "tcp-line", "1.0.1", "TCP-LINE-OBSERVATION-v1")); continue; }
            if (reader.Driver == "manual") { selected.Add(new("observation", "manual", "1.0.0", "CONTROLLED-OBSERVATION-v1")); continue; }
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
        if (binding.Package is null && binding.Implementation == "sportmaster-rcs") return new(Query: false);
        return binding.Package is null ? ReferenceCapabilityCatalog.Providers.Single(x => x.Kind == binding.Kind).Requirements
            : Store().VerifyInstalled(binding.Package).Manifest.Modules.Single(x => x.Id == binding.Module).Requirements ?? new(Query: true);
    }
    public CapabilityCatalog Catalog()
    {
        RegisterContracts();
        try { return InstalledCatalog(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException)
        {
            // Inventory is an optional read model, never capability admission. Existing
            // exact bindings remain unresolved and Validate/Create still fail closed.
            return new(ReferenceProviders(), [], []) { InventoryDiagnostic = "扩展库存未配置或不可用：" + ex.Message + "。内置配置可读取；原扩展绑定仍须恢复同一可信包后才能保存或启动。" };
        }
    }
    static CapabilityDescriptor[] ReferenceProviders()
    {
        var providers = ReferenceCapabilityCatalog.Providers.Append(new("observation", "manual", "1.0.0", "CONTROLLED-OBSERVATION-v1", [], new())).Append(Ipc.Drivers.TcpLineObservationSource.Descriptor);
#if !DIRECTPLC
        using var stream = typeof(ParcelCapabilityFactory).Assembly.GetManifestResourceStream("Ipc.BuiltInRcs.Parameters.json") ?? throw new InvalidDataException("内置RCS参数描述缺失");
        using var reader = new StreamReader(stream);
        providers = providers.Append(new("integration", "sportmaster-rcs", "1.3.1", "SPORTMASTER-RCS-SORTER-v1", Wire.Decode<ParameterField[]>(reader.ReadToEnd()), new(Query: false)));
#endif
        return providers.ToArray();
    }
    CapabilityCatalog InstalledCatalog()
    {
        var settingsFile = Path.Combine(launch.ConfigurationDirectory, "extension-store.json");
        var installed = store is not null || OperatingSystem.IsWindows() || File.Exists(settingsFile) ? Store().Inventory().Where(x => x.Status == "Installed").ToArray() : [];
        // Verify signed declarative manifests only. Catalog reads never load provider code.
        var manifests = installed.Select(x => (x.Package, Manifest: Store().VerifyInstalled(x.Package).Manifest)).ToArray();
        var descriptors = manifests.SelectMany(x => x.Manifest.Modules.Select(m =>
            new CapabilityDescriptor(m.Kind, "extension:" + x.Package.Id + "/" + m.Id, x.Package.Version, m.ContractIdentity ?? "PARCEL-REFERENCE-v1", m.Parameters, m.Requirements ?? new(), x.Package, m.Id)));
        return new(ReferenceProviders().Concat(descriptors).ToArray(), installed.Select(x => x.Package.Id + "@" + x.Package.Version).ToArray(),
            manifests.Select(x => new CapabilityPackage(x.Package, x.Manifest.Dependencies)).ToArray());
    }
    internal ApplicationRelease ReadPublishedRelease(string appRoot)
    {
        var release = Wire.Decode<ApplicationRelease>(File.ReadAllText(Path.Combine(appRoot, "application-release.json")));
        ApplicationReleaseValidation.ValidateIfPublished(appRoot, release.ApplicationId, new(release.Views, release.Layouts), businessAssembly);
        return release;
    }
    public async Task<BusinessReceipt> InstallAsync(string file, string sha256)
    {
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
        var resourceRoot = MachineRuntimePolicy.ResolveLeaseRoot(OperatingSystem.IsWindows() ? null : Path.Combine(launch.DataRoot, "Simulation", "resource-leases"));
        var deviceIds = ParcelBindings.Readers(c).SelectMany(x => new[] { "device:" + x.DeviceId, "reader:" + x.DeviceId })
            .Concat(ParcelBindings.Bound(c).Where(x => x.Kind is "weight" or "plc" or "camera").SelectMany(x => new[] { "device:" + x.Target, x.Kind + ":" + x.Target })).Distinct();
        deviceIds = deviceIds.Concat(ParcelBindings.Readers(c).Where(r => r.Driver == "tcp-line").Select(r => "reader:tcp:" + Ipc.Drivers.TcpLineObservationSource.EndpointIdentity(r)));
        devices = new(resourceRoot, deviceIds);
        if (c.Extensions is { Packages.Length: > 0 } deployment)
        {
            Store().RegisterConsumer(Path.Combine(launch.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId, c.ApplicationId + ".db"));
            use = Store().AcquireUse(deployment.Packages); loading = new(Store(), qualification);
        }
        Dictionary<string, IControlledObservationSource> readers = [];
        foreach (var binding in ParcelBindings.Readers(c))
        {
            IControlledObservationSource source;
            if (binding.Driver == "tcp-line") source = new Ipc.Drivers.TcpLineObservationSource(binding);
            else if (binding.Driver == "manual") source = new ManualObservationSource(binding.DeviceId);
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
        T Own<T>(CapabilityBinding b, Func<T> builtIn, Func<ExtensionLoading, ExtensionModule, T> external) where T : class, ICapabilityPort
        {
            T value;
            if (b.Package is null) { value = builtIn(); constructed.Add(value); hashes.Add(b.InstanceId, BuiltInFingerprint(value.GetType())); }
            else
            {
                var module = Store().VerifyInstalled(b.Package).Manifest.Modules.Single(x => x.Id == b.Module);
                value = external(loading!, module); constructed.Add(value);
                hashes.Add(b.InstanceId, Wire.Hash(b.Package.ManifestSha256 + ":" + Wire.FileHash(typeof(IParcelApplication).Assembly.Location) + ":" + Wire.FileHash(typeof(ExtensionStore).Assembly.Location)));
            }
            if (value.InstanceId != b.InstanceId) throw new InvalidDataException("Capability returned a mismatched identity"); return value;
        }
        Dictionary<string, ICapabilityPort> devicePorts = [];
        foreach (var scaleBinding in ParcelBindings.Bound(c).Where(b => b.Kind == "weight"))
            devicePorts.Add(scaleBinding.InstanceId, Own<IWeightSource>(scaleBinding, () => new ReferenceWeightSource(scaleBinding), (l, m) => l.CreateFactory<IWeightExtension>(scaleBinding.Package!, m, "weight").Create(scaleBinding)));
        var weight = c.Scale is null ? null : (IWeightSource)devicePorts[c.Scale.InstanceId];
        #if DIRECTPLC
        IWcsPort? wcs = null;
        #else
        var wcs = c.Wcs is { } w ? Own<IWcsPort>(w, () => w.Implementation == "sportmaster-rcs" ? new Ipc.Integrations.SportmasterRcs.SportmasterRcsTaskService(w) : new ReferenceWcs(w, w.Parameters.GetValueOrDefault("peer-root", "")), (l, m) => l.CreateFactory<IWcsExtension>(w.Package!, m, "integration").Create(w)) : null;
        if (wcs is IWcsRuntimeConfiguration configuredWcs) configuredWcs.ConfigureRuntime(c);
        #endif
        var plc = c.Plc is { } p ? Own<IPlcPort>(p, () => new ReferencePlc(p, p.Parameters.GetValueOrDefault("peer-root", "")), (l, m) => l.CreateFactory<IPlcExtension>(p.Package!, m, "plc").Create(p)) : null;
        var login = c.Login is { } a ? Own<IOperatorAuthenticator>(a, () => new ReferenceOperatorAuthenticator(a), (l, m) => l.CreateFactory<ILoginExtension>(a.Package!, m, "login").Create(a)) : null;
        foreach (var cameraBinding in ParcelBindings.Bound(c).Where(b => b.Kind == "camera"))
        {
            var path = Path.Combine(launch.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId);
            var sameReader = readers.GetValueOrDefault(cameraBinding.Target);
            var imagePort = Own<IImageCamera>(cameraBinding,
                () => sameReader is not null ? BindReader(sameReader, cameraBinding, path) : new ReferenceImageCamera(cameraBinding, path),
                (l, m) => sameReader is not null ? BindReader(sameReader, cameraBinding, path) : l.CreateFactory<ICameraExtension>(cameraBinding.Package!, m, "camera").Create(cameraBinding, path));
            devicePorts.Add(cameraBinding.InstanceId, imagePort);
        }
        var camera = c.Images is null ? null : (IImageCamera)devicePorts[c.Images.Camera.InstanceId];
        var uploader = c.Images?.Uploader is { } u ? Own<IImageUploader>(u, () => new ReferenceImageUploader(u, u.Parameters.GetValueOrDefault("peer-root", "")), (l, m) => l.CreateFactory<IUploadExtension>(u.Package!, m, "upload").Create(u)) : null;
        // Legacy MaintenanceLogin remains in the original configuration for byte/identity history.
        // Software administration has its own credential store and never falls back to this port.
        IOperatorAuthenticator? maintenance = null;
        transferred = true; return new(reader, weight, wcs, plc, hashes, login, camera, uploader, maintenance, readers, devicePorts);
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
