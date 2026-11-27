using Ipc.Application.Contracts;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

namespace Ipc.Assembly;

internal sealed class BuiltInHikrobotReaderProvider(CameraIdentityLookup? lookup = null) : IBuiltInReaderProvider
{
    public string DriverId => BundledMvs.ReaderImplementation;
    public bool IsSimulationOnly => false;
    public bool SupportsLiveReload => true;
    public bool AllowedInBuiltInOnlyMode => true;
    public CapabilityDescriptor Descriptor => BundledMvs.ReaderDescriptor;
    public ICameraDiscoveryProvider Discovery { get; } = new DiscoveryProvider(lookup ?? new());

    public void ValidateBinding(ParcelConfiguration config, DeviceBinding binding)
    {
        var parameters = binding.Parameters ?? [];
        var issues = ExtensionSchema.Validate(Descriptor.Parameters, parameters);
        if (issues.Length != 0) throw new InvalidDataException("驱动/" + binding.DeviceId + ": " + string.Join("; ", issues));
        BundledMvs.ValidateReaderParameters(parameters);
        InstallationResourceBoundary.RequireCamera(parameters.GetValueOrDefault("device.ip", ""), parameters.GetValueOrDefault("device.serial"));
    }

    public void ValidateGroup(ParcelConfiguration config, IReadOnlyList<DeviceBinding> activeBindings) => HikrobotReaderBindings.Validate(config);
    public IControlledObservationSource Create(DeviceBinding binding) => BundledMvs.CreateReader(binding);

    sealed class DiscoveryProvider(CameraIdentityLookup lookup) : ICameraDiscoveryProvider
    {
        public string DriverId => BundledMvs.ReaderImplementation;
        public bool SupportsEngine(string readerEngine) => readerEngine is "mvid-native" or "zxing-cpp";
        public DiscoveredCamera[] Discover(string readerEngine) => lookup.Discover(readerEngine)
            .Select(d => new DiscoveredCamera(d.Ip, d.Serial, d.Model, d.Firmware, d.HasIpConflict)).ToArray();
        public string ReadSerial(string ip, string readerEngine) => lookup.Read(ip, readerEngine);
        public ValueTask DisposeAsync() => lookup.DisposeAsync();
    }
}
