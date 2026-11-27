using Ipc.Adapters;
using Ipc.Application.Contracts;
using Ipc.Components;
using Ipc.Drivers;
using Ipc.Platform;

namespace Ipc.Assembly;

internal static class BuiltInReferenceProviders
{
    internal static IEnumerable<IBuiltInProvider> Create(Func<CapabilityBinding, IWcsPort>? createWcs)
    {
        foreach (var descriptor in ReferenceCapabilityCatalog.Providers)
            yield return descriptor.Kind switch
            {
                "weight" => new DeviceProvider(descriptor, (b, _) => new ReferenceWeightSource(b)),
                "login" => new DeviceProvider(descriptor, (b, _) => new ReferenceOperatorAuthenticator(b)),
                "camera" => new DeviceProvider(descriptor, (b, directory) => new ReferenceImageCamera(b, directory!)),
                "upload" => new DeviceProvider(descriptor, (b, _) => new ReferenceImageUploader(b, PeerDirectory(b))),
                "plc" => new DeviceProvider(descriptor, (b, _) => new ReferencePlc(b, PeerDirectory(b))),
                // Direct PLC keeps the existing catalog declaration for unused inventory.
                "integration" => createWcs is null ? new BindingProvider(descriptor) : new WcsProvider(descriptor, createWcs),
                _ => throw new InvalidOperationException("Unregistered reference capability: " + descriptor.Kind)
            };
        yield return new ManualReader();
        yield return new TcpLineReader();
    }

    internal static string PeerDirectory(CapabilityBinding binding) => binding.Parameters.GetValueOrDefault("peer-root", "");

    sealed class ManualReader : IBuiltInReaderProvider
    {
        public string DriverId => "manual";
        public bool IsSimulationOnly => true;
        public bool SupportsLiveReload => false;
        public bool AllowedInBuiltInOnlyMode => false;
        public CapabilityDescriptor Descriptor => new("observation", DriverId, "1.0.0", "CONTROLLED-OBSERVATION-v1", [], new());
        public void ValidateBinding(ParcelConfiguration config, DeviceBinding binding) { }
        public IControlledObservationSource Create(DeviceBinding binding) => new ManualObservationSource(binding.DeviceId);
    }

    sealed class TcpLineReader : IBuiltInReaderProvider
    {
        public string DriverId => "tcp-line";
        public bool IsSimulationOnly => false;
        public bool SupportsLiveReload => false;
        public bool AllowedInBuiltInOnlyMode => false;
        public bool SupportsSharedImageCapture => false;
        public CapabilityDescriptor Descriptor => TcpLineObservationSource.Descriptor;
        public void ValidateBinding(ParcelConfiguration config, DeviceBinding binding) => TcpLineObservationSource.Validate(binding);
        public void ValidateGroup(ParcelConfiguration config, IReadOnlyList<DeviceBinding> activeBindings)
        {
            if (activeBindings.GroupBy(TcpLineObservationSource.EndpointIdentity).Any(group => group.Count() > 1))
                throw new InvalidDataException("device.ip: 左右相机不能连接同一个 IP 和端口");
        }
        public IReadOnlyList<string> ResourceLeaseKeys(DeviceBinding binding) => ["reader:tcp:" + TcpLineObservationSource.EndpointIdentity(binding)];
        public IControlledObservationSource Create(DeviceBinding binding) => new TcpLineObservationSource(binding);
    }

    class BindingProvider(CapabilityDescriptor descriptor) : IBuiltInBindingProvider
    {
        public CapabilityDescriptor Descriptor => descriptor;
        public bool IsSimulation => true;

        public CapabilityBinding Normalize(BusinessLaunch launch, ParcelConfiguration config, CapabilityBinding binding)
        {
            if (binding.Kind != Descriptor.Kind || binding.Kind is "weight" or "login" or "camera" ||
                binding.Parameters.ContainsKey("endpoint")) return binding;
            var parameters = new Dictionary<string, string>(binding.Parameters);
            parameters.TryAdd("peer-root", Path.GetFullPath(Path.Combine(launch.DataRoot, "Simulation", "reference-peers")));
            return binding with { Parameters = parameters };
        }

        public void Validate(ParcelConfiguration config, CapabilityBinding binding, bool requireActiveQualification, DeploymentQualification? qualification)
        {
            if (requireActiveQualification && config.Mode == RunMode.Production && binding.Enabled)
                throw new InvalidDataException("Production NotReady: reference capability " + binding.InstanceId);
            if (!(binding.Kind == "camera" && ParcelBindings.Readers(config).Any(r => r.DeviceId == binding.Target)) &&
                (!Uri.TryCreate(binding.Target, UriKind.Absolute, out var target) || target.Scheme != "reference"))
                throw new InvalidDataException("Reference capability target must use explicit reference:// identity");
            if (binding.Parameters.TryGetValue("endpoint", out var endpoint) &&
                (binding.Kind is not ("integration" or "plc" or "upload") || !Uri.TryCreate(endpoint, UriKind.Absolute, out var peerUri) ||
                peerUri.Scheme != "http" || peerUri.Host != "127.0.0.1" || peerUri.AbsolutePath != "/parcel-reference/" ||
                peerUri.UserInfo.Length != 0 || peerUri.Query.Length != 0 || peerUri.Fragment.Length != 0))
                throw new InvalidDataException("Controlled reference HTTP peer must be literal loopback with an exact path");

            if (binding.Kind is "login" or "camera" or "upload")
            {
                var allowed = binding.Kind == "login" ? new[] { "subjects", "reply-mode" } : binding.Kind == "camera" ?
                    new[] { "decision", "reply-mode" } : new[] { "decision", "reply-mode", "peer-root", "endpoint" };
                if (binding.Parameters.Keys.Any(x => !allowed.Contains(x)) ||
                    binding.Kind == "login" && (binding.Parameters.GetValueOrDefault("subjects") is not { Length: > 0 and <= 1024 } subjects ||
                        subjects.Split(',').Any(x => !ExtensionSchema.IsId(x)) || binding.Parameters.GetValueOrDefault("reply-mode", "normal") is not ("normal" or "unavailable")) ||
                    binding.Kind != "login" && (binding.Parameters.GetValueOrDefault("decision", "accept") is not ("accept" or "reject") ||
                        binding.Parameters.GetValueOrDefault("reply-mode", "normal") is not ("normal" or "lose-after-commit")) ||
                    binding.Kind == "upload" && !binding.Parameters.ContainsKey("endpoint") &&
                        (!binding.Parameters.TryGetValue("peer-root", out var peer) || !Path.IsPathFullyQualified(peer)))
                    throw new InvalidDataException("Invalid bounded reference optional capability parameters");
                return;
            }

            var keys = Descriptor.Parameters.Select(p => p.Key);
            if (binding.Kind != "weight" && binding.Parameters.TryGetValue("peer-root", out var peerRoot) && !Path.IsPathFullyQualified(peerRoot))
                throw new InvalidDataException("Reference peer location must be an explicit absolute frozen parameter");
            if (binding.Parameters.Keys.Except(keys, StringComparer.Ordinal).Any() ||
                binding.Parameters.GetValueOrDefault("decision", "accept") is not ("accept" or "reject") ||
                binding.Parameters.GetValueOrDefault("reply-mode", "normal") is not ("normal" or "lose-after-commit") ||
                binding.Parameters.GetValueOrDefault("complete-on-query", "false") is not ("true" or "false") ||
                binding.Parameters.TryGetValue("slot", out var slot) && (string.IsNullOrWhiteSpace(slot) || slot.Length > 64 || slot.Any(char.IsControl)))
                throw new InvalidDataException("Invalid reference protocol parameters");
            var issues = ExtensionSchema.Validate(Descriptor.Parameters, binding.Parameters);
            if (issues.Length != 0) throw new InvalidDataException(string.Join("; ", issues));
        }
    }

    sealed class DeviceProvider(CapabilityDescriptor descriptor, Func<CapabilityBinding, string?, ICapabilityPort> create)
        : BindingProvider(descriptor), IBuiltInDeviceProvider
    {
        public ICapabilityPort Create(CapabilityBinding binding, string? dataDirectory = null) => create(binding, dataDirectory);
    }

    sealed class WcsProvider(CapabilityDescriptor descriptor, Func<CapabilityBinding, IWcsPort> create)
        : BindingProvider(descriptor), IBuiltInIntegrationProvider
    {
        public bool SupportsLiveReload => false;
        public IWcsPort Create(ParcelConfiguration config, CapabilityBinding binding) => create(binding);
    }
}
