using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

// A limited offline editor preserves topology, identities, provider versions and omitted fields.
public sealed class StartupConfigurationDraft
{
    readonly ParcelConfiguration original;
    readonly Dictionary<string, CapabilityBinding> bindings;
    public ConfigurationPolicyDraft Policy { get; }
    public CapabilityDraft[] Capabilities { get; }
    public string QueueCapacity { get; set; }
    public string TimeoutMs { get; set; }
    public string MaximumOperations { get; set; }
    public StartupConfigurationDraft(ParcelConfiguration configuration, CapabilityCatalog catalog)
    {
        original = configuration; Policy = ConfigurationPolicyDraft.From(configuration);
        var c = configuration;
        var all = (c.CapabilityInstances ?? []).Concat(new[] { c.Login, c.MaintenanceLogin, c.Scale, c.Wcs, c.Plc, c.Images?.Camera, c.Images?.Uploader }.OfType<CapabilityBinding>());
        var readers = c.Extensions?.Instances.Select(instance =>
        {
            var package = c.Extensions.Packages.Single(p => p.Id == instance.PackageId);
            return new CapabilityBinding("observation", "extension:" + package.Id + "/" + instance.ModuleId, package.Version, instance.Id,
                instance.DeviceId, "CONTROLLED-OBSERVATION-v1", instance.Parameters, package, instance.ModuleId, instance.Enabled);
        }) ?? [];
        bindings = all.Concat(readers).DistinctBy(b => b.InstanceId).ToDictionary(b => b.InstanceId);
        Capabilities = bindings.Values.Select(b => new CapabilityDraft(b.Kind, catalog.Providers, b, true)).ToArray();
        QueueCapacity = c.QueueCapacity.ToString(); TimeoutMs = c.TimeoutMs.ToString(); MaximumOperations = c.MaximumOperations.ToString();
    }
    public ParcelConfiguration Build()
    {
        // Missing inventory remains visible and the exact binding is preserved for application validation.
        var replacements = Capabilities.ToDictionary(d => d.InstanceId, d => d.Provider is null ? bindings[d.InstanceId] : d.Build());
        CapabilityBinding? Map(CapabilityBinding? b) => b is null ? null : replacements[b.InstanceId];
        var c = original;
        return c with
        {
            Weight = Policy.BuildWeight(), WeightValidity = c.SchemaVersion >= 2 ? Policy.Validity : null,
            QueueCapacity = ConfigurationPolicyDraft.Integer(QueueCapacity, "输入队列容量", 1, 4096),
            TimeoutMs = ConfigurationPolicyDraft.Integer(TimeoutMs, "调用超时(ms)", 100, 30000),
            MaximumOperations = ConfigurationPolicyDraft.Integer(MaximumOperations, "作业容量", 1, 100000),
            Login = Map(c.Login), MaintenanceLogin = Map(c.MaintenanceLogin), Scale = Map(c.Scale), Wcs = Map(c.Wcs), Plc = Map(c.Plc),
            CapabilityInstances = c.CapabilityInstances?.Select(b => Map(b)!).ToArray(),
            Images = c.Images is { } image ? image with { Camera = Map(image.Camera)!, Uploader = Map(image.Uploader)! } : null,
            Extensions = c.Extensions is { } extensions ? extensions with { Instances = extensions.Instances.Select(i => i with { Parameters = replacements[i.Id].Parameters }).ToArray() } : null
        };
    }
}
