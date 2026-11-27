using System.Collections.Frozen;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Assembly;

public interface IBuiltInProvider
{
    CapabilityDescriptor? Descriptor { get; }
}

public interface IBuiltInReaderProvider : IBuiltInProvider
{
    string DriverId { get; }
    bool IsSimulationOnly { get; }
    bool SupportsLiveReload { get; }
    bool AllowedInBuiltInOnlyMode { get; }
    bool SupportsSharedImageCapture => true;
    void ValidateBinding(ParcelConfiguration config, DeviceBinding binding);
    void ValidateGroup(ParcelConfiguration config, IReadOnlyList<DeviceBinding> activeBindings) { }
    IReadOnlyList<string> ResourceLeaseKeys(DeviceBinding binding) => [];
    IControlledObservationSource Create(DeviceBinding binding);
    ICameraDiscoveryProvider? Discovery => null;
}

public interface IBuiltInBindingProvider : IBuiltInProvider
{
    new CapabilityDescriptor Descriptor { get; }
    CapabilityDescriptor? IBuiltInProvider.Descriptor => Descriptor;
    string ImplementationId => Descriptor.Implementation;
    string Version => Descriptor.Version;
    string Contract => Descriptor.Contract;
    CapabilityRequirements Requirements => Descriptor.Requirements;
    bool IsSimulation { get; }
    CapabilityBinding Normalize(BusinessLaunch launch, ParcelConfiguration config, CapabilityBinding binding) => binding;
    void Validate(ParcelConfiguration config, CapabilityBinding binding, bool requireActiveQualification, DeploymentQualification? qualification);
}

public interface IBuiltInIntegrationProvider : IBuiltInBindingProvider
{
    bool SupportsLiveReload { get; }
    CapabilityBinding NormalizeProtocol(ParcelConfiguration config, CapabilityBinding binding) => binding;
    void ValidateSelectedProtocol(ParcelConfiguration config, CapabilityBinding binding) { }
    void ValidateDeferred(CapabilityBinding binding) =>
        throw new InvalidDataException("延后配置的RCS仍须使用当前已支持的精确实现和合同。");
    IWcsPort Create(ParcelConfiguration config, CapabilityBinding binding);
}

// Typed adapters for the existing PLC, scale, image and login roles, and for future hardware ports.
public interface IBuiltInDeviceProvider : IBuiltInBindingProvider
{
    ICapabilityPort Create(CapabilityBinding binding, string? dataDirectory = null);
}

public sealed class ProductCapabilityPolicy
{
    public ProductCapabilityPolicy(string applicationId, bool builtInOnly, bool supportsExternalExtensions,
        bool supportsLiveReload, IEnumerable<string>? allowedSimulationReaderDrivers = null,
        IEnumerable<string>? excludedReferenceCatalogProviders = null)
    {
        if (builtInOnly && supportsExternalExtensions)
            throw new ArgumentException("A built-in-only product cannot enable external extensions.");
        ApplicationId = applicationId;
        BuiltInOnly = builtInOnly;
        SupportsExternalExtensions = supportsExternalExtensions;
        SupportsLiveReload = supportsLiveReload;
        AllowedSimulationReaderDrivers = (allowedSimulationReaderDrivers ?? []).ToFrozenSet(StringComparer.Ordinal);
        ExcludedReferenceCatalogProviders = (excludedReferenceCatalogProviders ?? []).ToFrozenSet(StringComparer.Ordinal);
    }

    public string ApplicationId { get; }
    public bool BuiltInOnly { get; }
    public bool SupportsExternalExtensions { get; }
    public bool SupportsLiveReload { get; }
    public IReadOnlySet<string> AllowedSimulationReaderDrivers { get; }
    public IReadOnlySet<string> ExcludedReferenceCatalogProviders { get; }
    // The source-linked verifier exercises all three flows in one process.
    internal bool AllowsMixedVerificationProducts { get; init; }
}

public sealed class BuiltInCapabilityRegistry : IAsyncDisposable
{
    readonly IBuiltInProvider[] ordered;
    readonly FrozenDictionary<string, IBuiltInReaderProvider> readers;
    readonly FrozenDictionary<string, IBuiltInBindingProvider> bindings;
    readonly List<ICameraDiscoveryProvider> discovery;
    readonly Action<ParcelConfiguration>? validateExternalReaders;
    bool disposing;

    public BuiltInCapabilityRegistry(IEnumerable<IBuiltInProvider> providers) : this(providers, null) { }

    internal BuiltInCapabilityRegistry(IEnumerable<IBuiltInProvider> providers, Action<ParcelConfiguration>? validateExternalReaders)
    {
        ordered = providers.ToArray();
        if (ordered.Any(provider => provider is not (IBuiltInReaderProvider or IBuiltInBindingProvider)))
            throw new ArgumentException("A provider must declare a reader or a binding implementation.", nameof(providers));
        readers = ordered.OfType<IBuiltInReaderProvider>().ToDictionary(p => p.DriverId, StringComparer.Ordinal).ToFrozenDictionary(StringComparer.Ordinal);
        bindings = ordered.OfType<IBuiltInBindingProvider>().ToDictionary(p => p.ImplementationId, StringComparer.Ordinal).ToFrozenDictionary(StringComparer.Ordinal);
        this.validateExternalReaders = validateExternalReaders;
        discovery = [];
        foreach (var reader in ordered.OfType<IBuiltInReaderProvider>())
            if (reader.Discovery is { } provider)
            {
                if (provider.DriverId != reader.DriverId) throw new InvalidDataException("Camera discovery driver identity differs from its registration.");
                discovery.Add(provider);
            }
    }

    public IBuiltInReaderProvider? FindReader(string driverId) => readers.GetValueOrDefault(driverId);
    public IBuiltInReaderProvider RequireReader(string driverId) => FindReader(driverId) ??
        throw new InvalidDataException("No approved reader implementation: " + driverId);
    public IBuiltInBindingProvider? FindBinding(string implementationId) => bindings.GetValueOrDefault(implementationId);
    internal IBuiltInBindingProvider? FindExactBinding(CapabilityBinding binding) =>
        FindBinding(binding.Implementation) is { } provider && provider.Descriptor.Kind == binding.Kind &&
        provider.Version == binding.Version && provider.Contract == binding.Contract ? provider : null;
    public IBuiltInBindingProvider RequireBinding(CapabilityBinding binding) => FindExactBinding(binding) ??
        throw new InvalidDataException("No approved exact capability implementation/contract: " + binding.Implementation);

    internal IBuiltInIntegrationProvider? FindIntegrationForProtocol(CapabilityBinding binding) =>
        FindBinding(binding.Implementation) is IBuiltInIntegrationProvider provider && provider.Contract == binding.Contract ? provider :
            ordered.OfType<IBuiltInIntegrationProvider>().FirstOrDefault(p => p.Contract == binding.Contract);

    public ICameraDiscoveryProvider FindCameraDiscovery(string readerEngine)
    {
        ObjectDisposedException.ThrowIf(disposing, this);
        ICameraDiscoveryProvider? match = null;
        foreach (var provider in discovery)
            if (provider.SupportsEngine(readerEngine))
            {
                if (match is not null) throw new InvalidDataException("识码方式对应多个已注册相机发现驱动：" + readerEngine);
                match = provider;
            }
        return match ?? throw new NotSupportedException("当前装配无此识码方式的相机发现驱动：" + readerEngine);
    }

    public CapabilityDescriptor[] Catalog(ProductCapabilityPolicy policy) => ordered.Select(p => p.Descriptor)
        .OfType<CapabilityDescriptor>().Where(d => !policy.ExcludedReferenceCatalogProviders.Contains(d.Implementation)).ToArray();

    internal void ValidateReaderGroups(ParcelConfiguration config, DeviceBinding[] activeReaders)
    {
        foreach (var provider in ordered.OfType<IBuiltInReaderProvider>())
            provider.ValidateGroup(config, activeReaders.Where(binding => binding.Driver == provider.DriverId).ToArray());
        validateExternalReaders?.Invoke(config);
    }

    public async ValueTask DisposeAsync()
    {
        disposing = true;
        List<Exception> failures = [];
        foreach (var provider in discovery.ToArray())
            try
            {
                await provider.DisposeAsync().ConfigureAwait(false);
                discovery.Remove(provider);
            }
            catch (Exception error) { failures.Add(error); }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failures[0]);
        if (failures.Count != 0) throw new AggregateException("相机发现资源释放未完成，保留原所有者。", failures);
    }
}
