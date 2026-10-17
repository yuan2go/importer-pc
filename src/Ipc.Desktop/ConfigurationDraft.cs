using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Ipc.Platform;

namespace Ipc.Desktop;

// One editable source per window, shared by both pages. Effective is never edited.
public sealed class ConfigurationDraft : INotifyPropertyChanged
{
    StationConfig? basis;
    DeploymentExtensions? extensions;
    readonly Dictionary<string, string> fields = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    public long Generation { get; private set; }
    public ObservableCollection<DeviceDraft> Devices { get; } = [];
    public string this[string key]
    {
        get => fields.GetValueOrDefault(key, "");
        set { if (this[key] == value) return; fields[key] = value; Generation++; PropertyChanged?.Invoke(this, new("Item[]")); }
    }
    public DeploymentExtensions Extensions
    {
        get
        {
            var value = extensions ?? new([], []);
            if (value.Ports is not { } ports) return value;
            var target = new IntegrationBinding(this["TargetId"].Trim(), this["BindingVersion"].Trim(), this["Contract"].Trim(), this["Endpoint"].Trim());
            return value with { Ports = ports.Select(x => x.Port == "requests" ? x with { Target = target } : x).ToArray() };
        }
        set
        {
            var old = Extensions.Ports?.SingleOrDefault(x => x.Port == "requests")?.Target;
            extensions = value; Generation++;
            var target = value.Ports?.SingleOrDefault(x => x.Port == "requests")?.Target;
            if (target is not null && old != target) SetTarget(target);
        }
    }
    void SetTarget(IntegrationBinding target)
    { this["TargetId"] = target.Id; this["Endpoint"] = target.Endpoint; this["BindingVersion"] = target.Revision; this["Contract"] = target.Contract; }
    public void Import(string json, Func<StationConfig, IReadOnlyList<string>> validate)
    {
        if (json.Length > 262144) throw new InvalidDataException("配置导入超过大小限制");
        var candidate = Wire.Decode<StationConfig>(json);
        var errors = validate(candidate);
        if (errors.Count != 0) throw new InvalidDataException(string.Join("；", errors));
        Replace(candidate);
    }
    public void Replace(StationConfig config)
    {
        var copy = Wire.Decode<StationConfig>(Wire.Encode(config)); // prepare before replacing anything
        basis = copy; extensions = copy.Extensions;
        this["StationId"] = copy.StationId; this["Version"] = copy.Version; SetTarget(copy.Integration);
        this["Timeout"] = copy.TimeoutMs.ToString(CultureInfo.InvariantCulture);
        this["MinimumFree"] = copy.MinimumFreeMb.ToString(CultureInfo.InvariantCulture); this["FixtureDemand"] = copy.FixtureDemand.ToString(CultureInfo.InvariantCulture);
        this["MaximumPackage"] = copy.MaximumPackage.ToString(CultureInfo.InvariantCulture); this["Policy"] = copy.Policy;
        this["EventCapacity"] = copy.EventCapacity.ToString(CultureInfo.InvariantCulture); this["CommandCapacity"] = copy.CommandCapacity.ToString(CultureInfo.InvariantCulture); this["OutboxCapacity"] = copy.OutboxCapacity.ToString(CultureInfo.InvariantCulture);
        Devices.Clear(); foreach (var device in copy.Devices) Devices.Add(new(device)); Generation++;
    }
    // Editing defaults must not parse unrelated temporary numeric fields.
    public IntegrationBinding IntegrationTarget => new(this["TargetId"].Trim(), this["BindingVersion"].Trim(), this["Contract"].Trim(), this["Endpoint"].Trim());
    public StationConfig Build()
    {
        var source = basis ?? throw new InvalidOperationException("No configuration to edit");
        int Number(string key) => int.TryParse(this[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : throw new InvalidDataException(key + "必须是整数");
        var integration = new IntegrationBinding(this["TargetId"].Trim(), this["BindingVersion"].Trim(), this["Contract"].Trim(), this["Endpoint"].Trim());
        var deployment = extensions;
        if (deployment?.Ports is { } ports) deployment = deployment with { Ports = ports.Select(x => x.Port == "requests" ? x with { Target = integration } : x).ToArray() };
        return source with { StationId = this["StationId"].Trim(), Version = this["Version"].Trim(), Integration = integration,
            TimeoutMs = Number("Timeout"), MinimumFreeMb = Number("MinimumFree"), FixtureDemand = Number("FixtureDemand"), MaximumPackage = Number("MaximumPackage"), Policy = this["Policy"],
            EventCapacity = Number("EventCapacity"), CommandCapacity = Number("CommandCapacity"), OutboxCapacity = Number("OutboxCapacity"), Devices = Devices.Select(x => x.Binding()).ToArray(), Extensions = deployment };
    }
}
public sealed class DeviceDraft(DeviceBinding source)
{
    public string Role { get; set; } = source.Role;
    public string Driver { get; set; } = source.Driver;
    public string DeviceId { get; set; } = source.DeviceId;
    public string Revision { get; set; } = source.Revision;
    public string? ReplayFile { get; set; } = source.ReplayFile;
    public DeviceBinding Binding() => new(Role, Driver, DeviceId, Revision, string.IsNullOrWhiteSpace(ReplayFile) ? null : ReplayFile);
}
