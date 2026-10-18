using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

public sealed class CapabilityParameter(ParameterField field, string value)
{
    public ParameterField Field { get; } = field;
    public string Value { get; set; } = value;
}
public sealed class CapabilityDraft : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    CapabilityDescriptor? provider;
    public CapabilityDescriptor[] Providers { get; }
    public string Kind { get; }
    public string Role { get; }
    public bool Enabled { get; set; }
    public bool Bound { get; set; }
    public string InstanceId { get; set; }
    public bool IsTargetReadOnly => Kind == "observation";
    public string TargetBoundary => IsTargetReadOnly ? "稳定读码DeviceId只读；更换须清偿原件并受控迁移部署" : "组件拥有的协议目标；保存后待受控重启";
    public string Target { get; set; }
    public string Contract { get; set; }
    public ObservableCollection<CapabilityParameter> Parameters { get; } = [];
    public CapabilityDescriptor? Provider
    {
        get => provider;
        set
        {
            if (ReferenceEquals(provider, value)) return;
            provider = value; Parameters.Clear();
            if (value is not null) foreach (var parameter in value.Parameters) Parameters.Add(new(parameter, parameter.Default ?? ""));
            Contract = value?.Contract ?? "";
            PropertyChanged?.Invoke(this, new(nameof(Provider))); PropertyChanged?.Invoke(this, new(nameof(Contract)));
        }
    }
    public CapabilityDraft(string kind, CapabilityDescriptor[] providers, CapabilityBinding? binding, bool bound, string? role = null)
    {
        Kind = kind; Role = role ?? kind; Providers = providers.Where(x => x.Kind == kind).ToArray();
        InstanceId = binding?.InstanceId ?? kind + ".one"; Target = binding?.Target ?? "reference://" + kind + "/one"; Contract = binding?.Contract ?? "";
        Provider = Providers.FirstOrDefault(x => x.Implementation == binding?.Implementation && x.Version == binding.Version) ?? Providers.FirstOrDefault();
        if (binding is not null) foreach (var p in Parameters) p.Value = binding.Parameters.GetValueOrDefault(p.Field.Key) ?? p.Field.Default ?? "";
        Enabled = binding?.Enabled ?? false; Bound = bound;
    }
    public CapabilityBinding Build()
    {
        var p = Provider ?? throw new InvalidOperationException("选择已安装的能力提供者：" + Kind);
        var parameters = Parameters.Where(x => x.Value.Length > 0).ToDictionary(x => x.Field.Key, x => x.Value);
        var errors = ExtensionSchema.Validate(p.Parameters, parameters);
        if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors));
        return new(Kind, p.Implementation, p.Version, InstanceId, Target, Contract, parameters, p.Package, p.Module, Enabled);
    }
}

public partial class ParcelViewModel
{
    public ObservableCollection<CapabilityDraft> CapabilityDrafts { get; } = [];
    public string InstalledPackages { get; private set; } = "尚未读取安装清单";
    public WeightSource[] WeightSources => Enum.GetValues<WeightSource>();
    public WeightUnit[] WeightUnits => Enum.GetValues<WeightUnit>();
    public WeightValidity[] WeightValidities => Enum.GetValues<WeightValidity>();
    public WeightValidity DraftWeightValidity { get; set; }
    public UploadStrategy[] UploadStrategies => Enum.GetValues<UploadStrategy>();
    public WeightSource DraftWeightSource { get; set; }
    public WeightUnit DraftUnit { get; set; }
    public int DraftPrecision { get; set; }
    public decimal? DraftConfiguredWeight { get; set; }
    public int DraftWaitMs { get; set; }
    public int DraftFreshMs { get; set; }
    public UploadStrategy DraftUploadStrategy { get; set; }
    public int DraftImageBytes { get; set; }
    public long DraftTotalBytes { get; set; }
    public int DraftPending { get; set; }
    public string PackagePath { get; set; } = "";
    public string PackageSha256 { get; set; } = "";
    public string MaintenanceSubject { get; set; } = "maintenance";
    public string InspectionReason { get; set; } = "";
    public async Task LoadCapabilitiesAsync()
    {
        if (Snapshot is null) await RefreshAsync();
        var c = Snapshot!.Configuration.Pending ?? Snapshot.Configuration.Effective;
        var catalog = await application.CapabilitiesAsync();
        InstalledPackages = "已安装签名包：" + string.Join(" · ", catalog.InstalledPackages); Changed(nameof(InstalledPackages));
        CapabilityDrafts.Clear();
        if (c.SchemaVersion >= 3 || c.Topology is not null)
        {
            Message = "schema3设备拓扑为只读；请通过版本化完整配置及应用CAS接口维护，旧单Reader表单禁止保存";
            return;
        }
        var bound = new[] { c.Login, c.MaintenanceLogin, c.Scale, c.Wcs, c.Plc, c.Images?.Camera, c.Images?.Uploader }.OfType<CapabilityBinding>().ToArray();
        var instances = c.CapabilityInstances ?? bound;
        CapabilityBinding readerBinding;
        var observation = c.Extensions?.Instances.SingleOrDefault(x => c.Reader.Driver == "extension:" + x.Id);
        if (observation is not null)
        {
            var package = c.Extensions!.Packages.Single(x => x.Id == observation.PackageId);
            readerBinding = new("observation", "extension:" + package.Id + "/" + observation.ModuleId, package.Version, observation.Id, c.Reader.DeviceId, "CONTROLLED-OBSERVATION-v1", observation.Parameters, package, observation.ModuleId, observation.Enabled);
        }
        else readerBinding = new("observation", "manual", "1.0.0", "parcel.reader", c.Reader.DeviceId, "CONTROLLED-OBSERVATION-v1", []);
        CapabilityDrafts.Add(new("observation", catalog.Providers, readerBinding, true));
        foreach (var b in instances) CapabilityDrafts.Add(new(b.Kind, catalog.Providers, b, bound.Any(x => x.InstanceId == b.InstanceId), b.InstanceId == c.MaintenanceLogin?.InstanceId ? "maintenance-auth" : b.Kind));
        foreach (var kind in new[] { "login", "weight", "camera", "upload", "integration", "plc" }.Where(k => !CapabilityDrafts.Any(x => x.Role == k))) CapabilityDrafts.Add(new(kind, catalog.Providers, null, false));
        DraftWeightValidity = c.WeightValidity ?? WeightValidity.UntilDispatch; Changed(nameof(DraftWeightValidity));
        DraftWeightSource = c.Weight.Source; DraftUnit = c.Weight.Unit; DraftPrecision = c.Weight.Precision;
        DraftConfiguredWeight = c.Weight.ConfiguredValue; DraftWaitMs = c.Weight.WaitTimeoutMs; DraftFreshMs = c.Weight.MaximumAgeMs;
        DraftUploadStrategy = c.Images?.Strategy ?? UploadStrategy.BeforeMain; DraftImageBytes = c.Images?.MaximumBytes ?? 4194304;
        DraftTotalBytes = c.Images?.TotalBytes ?? 33554432; DraftPending = c.Images?.MaximumPending ?? 32;
        foreach (var name in new[] { nameof(DraftWeightSource), nameof(DraftUnit), nameof(DraftPrecision), nameof(DraftConfiguredWeight), nameof(DraftWaitMs), nameof(DraftFreshMs), nameof(DraftUploadStrategy), nameof(DraftImageBytes), nameof(DraftTotalBytes), nameof(DraftPending) }) Changed(name);
    }
    public async Task SaveCapabilitiesAsync()
    {
        if (Snapshot is null) return;
        if (Snapshot.Configuration.Effective.SchemaVersion >= 3 || Snapshot.Configuration.Pending?.SchemaVersion >= 3 || Snapshot.Configuration.Effective.Topology is not null)
        {
            Message = "拒绝保存：此表单不能编辑完整schema3拓扑，原配置及设备绑定保持不变";
            return;
        }
        var instances = CapabilityDrafts.Where(x => x.Kind != "observation").Select(x => x.Build()).ToArray();
        CapabilityBinding? Selected(string kind) => CapabilityDrafts.Where(x => x.Role == kind && x.Bound).Select(x => x.Build()).SingleOrDefault();
        var reader = Selected("observation") ?? throw new InvalidOperationException("读码来源必须绑定");
        if (reader.Target != Snapshot.Configuration.Effective.Reader.DeviceId) throw new InvalidOperationException("稳定读码DeviceId不属于普通参数修改；清偿原件后使用受控迁移部署");
        if (!reader.Enabled) throw new InvalidOperationException("读码来源必须启用");
        var camera = Selected("camera"); var upload = Selected("upload");
        if ((camera is null) != (upload is null)) throw new InvalidOperationException("采图和上传须同时绑定");
        var c = (Snapshot.Configuration.Pending ?? Snapshot.Configuration.Effective) with
        {
            SchemaVersion = 2, WeightValidity = DraftWeightValidity,
            Login = Selected("login"), MaintenanceLogin = Selected("maintenance-auth"), Scale = Selected("weight"), Wcs = Selected("integration"), Plc = Selected("plc"),
            Weight = new(DraftWeightSource, DraftUnit, DraftPrecision, DraftWeightSource == WeightSource.Configured ? DraftConfiguredWeight : null, DraftFreshMs, DraftWaitMs),
            Images = camera is null ? null : new(camera, upload!, DraftImageBytes, DraftUploadStrategy, DraftTotalBytes, DraftPending),
            Reader = Snapshot.Configuration.Effective.Reader with { Driver = reader.Package is null ? "manual" : "extension:" + reader.InstanceId },
            CapabilityInstances = instances,
            Extensions = instances.Append(reader).Any(x => x.Package is not null) ? new(instances.Append(reader).Where(x => x.Package is not null).Select(x => x.Package!).Distinct().ToArray(),
                reader.Package is null ? [] : [new(reader.InstanceId, reader.Package.Id, reader.Module!, Snapshot.Configuration.Effective.Reader.DeviceId, true, reader.Parameters)]) : null
        };
        var receipt = await application.StageConfigurationAsync(Snapshot.Configuration.Revision, c);
        Message = receipt.Status + " · " + receipt.Reason;
    }
    public async Task AuthorizeMaintenanceAsync(string proof) { var r = await application.AuthorizeMaintenanceAsync(MaintenanceSubject, proof); Message = r.Status + " · " + r.Reason; }
    public async Task InstallAsync() { var r = await application.InstallCapabilityAsync(PackagePath, PackageSha256); Message = r.Status + " · " + r.Reason; await LoadCapabilitiesAsync(); }
    public async Task ContinueUploadAsync() { var op = Selected ?? Current ?? throw new InvalidOperationException("选择原作业"); await CommandAsync("ContinueUpload", op.Id); }
    public async Task AbandonAsync()
    {
        var op = Selected ?? Current ?? throw new InvalidOperationException("选择原作业"); var s = await application.SnapshotAsync();
        var r = await application.CommandAsync(new(Wire.Id(), s.Revision, "AbandonUnsubmitted", op.Id, InspectionReason)); Message = r.Status + " · " + r.Reason;
    }
}
