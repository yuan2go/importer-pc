using Ipc.Application.Contracts;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Platform;

namespace Ipc.Assembly;

internal sealed class BuiltInSportmasterRcsProvider : IBuiltInIntegrationProvider
{
    static readonly CapabilityDescriptor descriptor = ReadDescriptor();
    public CapabilityDescriptor Descriptor => descriptor;
    public bool IsSimulation => false;
    public bool SupportsLiveReload => true;

    public CapabilityBinding NormalizeProtocol(ParcelConfiguration config, CapabilityBinding binding) =>
        RcsRequestPolicy.NormalizeHeartbeat(binding with
        {
            Parameters = new(binding.Parameters.Where(p => !RcsTaskOptions.RetiredKeys.Contains(p.Key, StringComparer.Ordinal)))
        });

    public void ValidateSelectedProtocol(ParcelConfiguration config, CapabilityBinding binding)
    {
        _ = new RcsTaskOptions(binding);
        _ = RcsConfigurationValidation.HeartbeatMapping(config);
    }

    public void Validate(ParcelConfiguration config, CapabilityBinding binding, bool requireActiveQualification, DeploymentQualification? qualification)
    {
        if (binding.InstanceId != config.Wcs?.InstanceId) _ = new RcsTaskOptions(binding);
        if (requireActiveQualification && config.Mode == RunMode.Production && qualification is null)
            throw new InvalidDataException("RCS生产部署资格尚未完成");
    }

    public void ValidateDeferred(CapabilityBinding binding) => _ = RcsRequestPolicy.TimeoutMs(binding);

    // Runtime configuration is applied by the caller after it owns this port.
    public IWcsPort Create(ParcelConfiguration config, CapabilityBinding binding) => new SportmasterRcsTaskService(binding);

    static CapabilityDescriptor ReadDescriptor()
    {
        using var stream = typeof(SportmasterRcsTaskService).Assembly.GetManifestResourceStream("Ipc.BuiltInRcs.Parameters.json")
            ?? throw new InvalidDataException("内置RCS参数描述缺失");
        using var reader = new StreamReader(stream);
        return new("integration", "sportmaster-rcs", RcsRequestPolicy.ComponentVersion, RcsRequestPolicy.Contract,
            Wire.Decode<ParameterField[]>(reader.ReadToEnd()), new(Query: false));
    }
}
