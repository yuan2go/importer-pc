using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

// A limited offline editor preserves topology, identities, provider versions and omitted fields.
public sealed class StartupConfigurationDraft
{
    static string ProviderKey(CapabilityDescriptor p) => Wire.Encode(new { p.Kind, p.Implementation, p.Version, p.Contract, p.Package, p.Module });
    readonly ParcelConfiguration original;
    readonly Dictionary<string, CapabilityBinding> bindings;
    readonly CapabilityCatalog catalog;
    public CapabilityDraft[] Readers { get; }
    public string ResidentHistory { get; set; }
    public string MinimumFreeMiB { get; set; }
    public ConfigurationPolicyDraft Policy { get; }
    public CapabilityDraft[] Capabilities { get; }
    public string QueueCapacity { get; set; }
    public string TimeoutMs { get; set; }
    public string MaximumOperations { get; set; }
    public StartupConfigurationDraft(ParcelConfiguration configuration, CapabilityCatalog catalog)
    {
        original = configuration; this.catalog = catalog; Policy = ConfigurationPolicyDraft.From(configuration);
        var c = configuration;
        var all = (c.CapabilityInstances ?? []).Concat(new[] { c.Login, c.MaintenanceLogin, c.Scale, c.Wcs, c.Plc, c.Images?.Camera, c.Images?.Uploader }.OfType<CapabilityBinding>());
        var readers = c.Extensions?.Instances.Select(instance =>
        {
            var package = c.Extensions.Packages.Single(p => p.Id == instance.PackageId);
            return new CapabilityBinding("observation", "extension:" + package.Id + "/" + instance.ModuleId, package.Version, instance.Id,
                instance.DeviceId, "CONTROLLED-OBSERVATION-v1", instance.Parameters, package, instance.ModuleId, instance.Enabled);
        }) ?? [];
        bindings = all.DistinctBy(b => b.InstanceId).ToDictionary(b => b.InstanceId);
        var capabilities = bindings.Values.Select(b => new CapabilityDraft(b.Kind, catalog.Providers, b, true,
            b.InstanceId == c.Login?.InstanceId ? "operator-login" : b.InstanceId == c.MaintenanceLogin?.InstanceId ? "maintenance-auth" : b.InstanceId == c.Scale?.InstanceId ? "default-scale" : b.Kind)).ToList();
        if (c.Login is null) capabilities.Add(new("login", catalog.Providers, null, false, "operator-login") { InstanceId = "operator.login", Enabled = true });
        if (c.Scale is null) capabilities.Add(new("weight", catalog.Providers, null, false, "default-scale") { InstanceId = "default.scale", Enabled = true });
        Capabilities = capabilities.ToArray();
        Readers = ParcelBindings.Readers(c).Select((reader, index) =>
        {
            var binding = readers.SingleOrDefault(b => "extension:" + b.InstanceId == reader.Driver) ??
                new CapabilityBinding("observation", "manual", "1.0.0", "reader." + index, reader.DeviceId, "CONTROLLED-OBSERVATION-v1", []);
            return new CapabilityDraft("observation", catalog.Providers, binding, true, ParcelBindings.Positions(c).Single(p => p.ReaderDeviceId == reader.DeviceId).Id);
        }).ToArray();
        QueueCapacity = c.QueueCapacity.ToString(); TimeoutMs = c.TimeoutMs.ToString(); MaximumOperations = c.MaximumOperations.ToString();
        ResidentHistory = (c.Retention?.ResidentHistory ?? 200).ToString(); MinimumFreeMiB = ((c.Retention?.MinimumFreeBytes ?? 67108864) / 1048576m).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    public ParcelConfiguration Build()
    {
        // Missing inventory remains visible and the exact binding is preserved for application validation.
        var replacements = Capabilities.Where(d => bindings.ContainsKey(d.InstanceId) || d.Bound).ToDictionary(d => d.InstanceId, d => d.Provider is null ? bindings[d.InstanceId] : d.Build());
        CapabilityBinding? Map(CapabilityBinding? b) => b is null ? null : replacements[b.InstanceId];
        var c = original;
        var readerBindings = Readers.Select(d => d.Build()).ToArray();
        var devices = ParcelBindings.Readers(c).Select((r, i) => r with { DeviceId = readerBindings[i].Target,
            Driver = readerBindings[i].Package is null ? "manual" : "extension:" + readerBindings[i].InstanceId }).ToArray();
        var instances = readerBindings.Where(b => b.Package is not null).Select(b => new ExtensionInstance(b.InstanceId, b.Package!.Id, b.Module!, b.Target, b.Enabled, b.Parameters)).ToArray();
        var roots = replacements.Values.Concat(readerBindings).Select(b => b.Package).OfType<PackageReference>().Distinct().ToArray();
        var packages = roots.Length == 0 ? [] : ParcelViewModel.ResolvePackageClosure(roots, catalog.Packages ?? []);
        var extensions = c.Extensions is null && packages.Length == 0 ? null : new DeploymentExtensions(packages, instances, c.Extensions?.Services, c.Extensions?.Ports);
        if (!decimal.TryParse(MinimumFreeMiB, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var reserve) || reserve < 1 || reserve > 1048576)
            throw new InvalidOperationException("磁盘保留空间须为1～1048576 MiB");
        var retention = new ParcelRetentionPolicy(ConfigurationPolicyDraft.Integer(ResidentHistory, "常驻历史作业", 1, 10000), checked((long)(reserve * 1048576)));
        return c with
        {
            Reader = devices[0], Topology = c.Topology is null ? null : c.Topology with { Readers = devices, Positions = c.Topology.Positions.Select((p, i) => p with { ReaderDeviceId = devices[Array.FindIndex(ParcelBindings.Readers(c), r => r.DeviceId == p.ReaderDeviceId)].DeviceId }).ToArray() },
            Retention = c.Retention is null && retention == new ParcelRetentionPolicy() ? null : retention, BackupSchedule = Policy.BuildBackup(),
            Weight = Policy.BuildWeight(), WeightValidity = c.SchemaVersion >= 2 ? Policy.Validity : null,
            QueueCapacity = ConfigurationPolicyDraft.Integer(QueueCapacity, "输入队列容量", 1, 4096),
            TimeoutMs = ConfigurationPolicyDraft.Integer(TimeoutMs, "调用超时(ms)", 100, 30000),
            MaximumOperations = ConfigurationPolicyDraft.Integer(MaximumOperations, "作业容量", 1, 100000),
            Login = Capabilities.Single(d => d.Role == "operator-login") is { Bound: true } login ? replacements[login.InstanceId] : null,
            Scale = Policy.WeightSource == WeightSource.Measured && Capabilities.Single(d => d.Role == "default-scale") is { Bound: true } scale ? replacements[scale.InstanceId] : null,
            MaintenanceLogin = Map(c.MaintenanceLogin), Wcs = Map(c.Wcs), Plc = Map(c.Plc),
            CapabilityInstances = c.CapabilityInstances?.Select(b => Map(b)!).ToArray(),
            Images = c.Images is { } image ? image with { Camera = Map(image.Camera)!, Uploader = Map(image.Uploader)! } : null,
            Extensions = extensions
        };
    }
    public StartupEditorState Capture()
    {
        Dictionary<string, string> values = new() { ["queue"] = QueueCapacity, ["timeout"] = TimeoutMs, ["operations"] = MaximumOperations, ["resident"] = ResidentHistory, ["reserveMiB"] = MinimumFreeMiB };
        foreach (var property in typeof(ConfigurationPolicyDraft).GetProperties().Where(p => p.CanRead && p.CanWrite)) values["policy." + property.Name] = property.GetValue(Policy)?.ToString() ?? "";
        foreach (var (draft, index) in Readers.Concat(Capabilities).Select((d, i) => (d, i)))
        {
            var prefix = "cap." + index + ".";
            values[prefix + "provider"] = draft.Provider is null ? "" : ProviderKey(draft.Provider);
            values[prefix + "target"] = draft.Target; values[prefix + "enabled"] = draft.Enabled.ToString(); values[prefix + "bound"] = draft.Bound.ToString();
            foreach (var parameter in draft.Parameters) values[prefix + "parameter." + parameter.Field.Key] = parameter.Value;
        }
        return new(values);
    }
    public void Restore(StartupEditorState state)
    {
        var v = state.Values;
        QueueCapacity = v.GetValueOrDefault("queue", QueueCapacity); TimeoutMs = v.GetValueOrDefault("timeout", TimeoutMs); MaximumOperations = v.GetValueOrDefault("operations", MaximumOperations);
        ResidentHistory = v.GetValueOrDefault("resident", ResidentHistory); MinimumFreeMiB = v.GetValueOrDefault("reserveMiB", MinimumFreeMiB);
        foreach (var property in typeof(ConfigurationPolicyDraft).GetProperties().Where(p => p.CanRead && p.CanWrite))
            if (v.TryGetValue("policy." + property.Name, out var value)) property.SetValue(Policy, property.PropertyType.IsEnum ? Enum.Parse(property.PropertyType, value) : property.PropertyType == typeof(bool) ? bool.Parse(value) : value);
        foreach (var (draft, index) in Readers.Concat(Capabilities).Select((d, i) => (d, i)))
        {
            var prefix = "cap." + index + ".";
            if (v.GetValueOrDefault(prefix + "provider") is { Length: > 0 } provider)
            {
                var match = draft.Providers.SingleOrDefault(p => ProviderKey(p) == provider);
                if (match is null) throw new InvalidOperationException("草稿精确提供者未安装或声明已变化，先恢复相同组件；当前输入保留");
                draft.Provider = match;
            }
            draft.Target = v.GetValueOrDefault(prefix + "target", draft.Target);
            if (v.TryGetValue(prefix + "enabled", out var enabled)) draft.Enabled = bool.Parse(enabled);
            if (v.TryGetValue(prefix + "bound", out var bound)) draft.Bound = bool.Parse(bound);
            foreach (var parameter in draft.Parameters) parameter.Value = v.GetValueOrDefault(prefix + "parameter." + parameter.Field.Key, parameter.Value);
        }
    }
}
