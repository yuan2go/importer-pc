using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

public sealed class CapabilityParameter(ParameterField field, string value) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    string current = value;
    public ParameterField Field { get; } = field;
    public bool IsList => Field.Type == "string-list";
    public int EditorMaximumLength => IsList ? checked(Field.MaximumLength * Field.MaximumItems + 8 * Field.MaximumItems) : Field.MaximumLength;
    public string EditorLabel => IsList ? Field.Label.Replace("（每行一项）", "") + "（JSON 数组）" : Field.Label;
    public string EditorHelp => IsList ? "JSON字符串数组；最多" + Field.MaximumItems + "项，每项最多" + Field.MaximumLength + "字符；保留空项和转义。" : Field.Description ?? Field.Type;
    public string Value { get => current; set { current = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
}
public sealed class CapabilityDraft : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    CapabilityDescriptor? provider;
    public CapabilityDescriptor[] Providers { get; }
    public string Kind { get; }
    public string Role { get; }
    bool enabled, bound;
    string instanceId = "", target = "", contract = "";
    public bool Enabled { get => enabled; set { enabled = value; Notify(); } }
    public bool Bound { get => bound; set { bound = value; Notify(); } }
    public string InstanceId { get => instanceId; set { instanceId = value; Notify(); } }
    public bool IsTargetReadOnly => Kind == "observation";
    public bool LegacyBindingEditable { get; set; } = true;
    public string TargetBoundary => IsTargetReadOnly ? "稳定读码DeviceId只读；更换须清偿原件并受控迁移部署" : "组件拥有的协议目标；保存后待受控重启";
    public string Target { get => target; set { target = value; Notify(); } }
    public string Contract { get => contract; set { contract = value; Notify(); } }
    public string ProviderIdentity => Provider is { } p ? p.Implementation + " @ " + p.Version + " · " + (p.Package?.ManifestSha256 ?? "内置参考") + " · " + p.Module : "原提供者未安装或精确身份不匹配；保存被阻断：" + OriginalProviderIdentity;
    readonly Dictionary<string, string> originalParameters = [];
    bool preserveOmittedDefaults;
    public string OriginalProviderIdentity { get; private set; } = "新增实例";
    void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public ObservableCollection<CapabilityParameter> Parameters { get; } = [];
    public CapabilityDescriptor? Provider
    {
        get => provider;
        set
        {
            if (ReferenceEquals(provider, value)) return;
            provider = value; Parameters.Clear(); originalParameters.Clear(); preserveOmittedDefaults = false;
            if (value is not null) foreach (var parameter in value.Parameters) { var item = new CapabilityParameter(parameter, parameter.Default ?? ""); item.PropertyChanged += (_, _) => Notify(nameof(Parameters)); Parameters.Add(item); }
            Contract = value?.Contract ?? "";
            Notify(nameof(Provider)); Notify(nameof(Contract)); Notify(nameof(ProviderIdentity));
        }
    }
    public CapabilityDraft(string kind, CapabilityDescriptor[] providers, CapabilityBinding? binding, bool bound, string? role = null)
    {
        Kind = kind; Role = role ?? kind; Providers = providers.Where(x => x.Kind == kind).ToArray();
        InstanceId = binding?.InstanceId ?? kind + ".one"; Target = binding?.Target ?? "reference://" + kind + "/one"; Contract = binding?.Contract ?? "";
        Provider = binding is null ? Providers.FirstOrDefault() : Providers.SingleOrDefault(x => x.Implementation == binding.Implementation && x.Version == binding.Version && x.Package == binding.Package && x.Module == binding.Module);
        if (binding is not null) foreach (var p in Parameters) p.Value = binding.Parameters.GetValueOrDefault(p.Field.Key) ?? p.Field.Default ?? "";
        if (binding is not null) { preserveOmittedDefaults = true; OriginalProviderIdentity = binding.Implementation + " @ " + binding.Version + " · " + (binding.Package?.ManifestSha256 ?? "内置参考") + " · " + binding.Module; Contract = binding.Contract; foreach (var pair in binding.Parameters) originalParameters[pair.Key] = pair.Value; }
        Enabled = binding?.Enabled ?? false; Bound = bound;
    }
    public CapabilityBinding Build()
    {
        var p = Provider ?? throw new InvalidOperationException("选择已安装的能力提供者：" + Kind);
        // Keep original insertion order and explicitly omitted defaults. Reordering
        // unrelated parameter bytes would change the frozen configuration hash.
        var parameters = new Dictionary<string, string>(originalParameters);
        foreach (var item in Parameters)
        {
            if (item.Value.Length == 0 && originalParameters.GetValueOrDefault(item.Field.Key) != "") { parameters.Remove(item.Field.Key); continue; }
            if (!preserveOmittedDefaults || originalParameters.ContainsKey(item.Field.Key) || item.Value != item.Field.Default) parameters[item.Field.Key] = item.Value;
        }
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
        var loaded = Snapshot!;
        var c = loaded.Configuration.Pending ?? loaded.Configuration.Effective;
        var catalog = await ServiceAsync(() => application.CapabilitiesAsync());
        SetConfigurationDraft(c, loaded.Configuration.Revision, loaded.RuntimeId);
        ApplyInstalledCatalog(catalog);
        CapabilityDrafts.Clear();
        if (c.SchemaVersion >= 3 || c.Topology is not null)
        {
            LoadStructuredConfiguration(c, catalog);
            Message = "已读取完整拓扑与能力库存；表单为当前编辑来源。保存为Pending，受控重启生效。";
            return;
        }
        StartLegacyConfiguration(c);
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
        AcceptEditorBaseline();
    }
    public async Task SaveCapabilitiesAsync()
    {
        RequireFreshSnapshot();
        RequireFormConfigurationAuthority();
        if (Snapshot is null) return;
        if (Snapshot.Configuration.Effective.SchemaVersion >= 3 || Snapshot.Configuration.Pending?.SchemaVersion >= 3 || Snapshot.Configuration.Effective.Topology is not null)
        {
            await SaveStructuredConfigurationAsync();
            return;
        }
        var c = BuildLegacyConfiguration(); var submittedSignature = ConfigurationEditSignature();
        var expected = RequireDraftRevision();
        var reason = ConfigurationReason;
        var receipt = await ServiceAsync(() => application.StageConfigurationAsync(expected, c, reason));
        AdvanceOwnDraftRevision(expected, receipt);
        if (receipt.Status == "PendingRestart") { savedEditorSignature = submittedSignature; savedCandidate = Wire.Encode(c); }
        Message = receipt.Status + " · " + receipt.Reason;
    }
    ParcelConfiguration BuildLegacyConfiguration()
    {
        var loaded = Wire.Decode<ParcelConfiguration>(savedCandidate);
        var instances = CapabilityDrafts.Where(x => x.Kind != "observation").Select(x => x.Build()).ToArray();
        CapabilityBinding? Selected(string kind) => CapabilityDrafts.Where(x => x.Role == kind && x.Bound).Select(x => x.Build()).SingleOrDefault();
        var reader = Selected("observation") ?? throw new InvalidOperationException("读码来源必须绑定");
        if (reader.Target != loaded.Reader.DeviceId) throw new InvalidOperationException("稳定读码DeviceId不属于普通参数修改；清偿原件后使用受控迁移部署");
        if (!reader.Enabled) throw new InvalidOperationException("读码来源必须启用");
        var camera = Selected("camera"); var upload = Selected("upload");
        if ((camera is null) != (upload is null)) throw new InvalidOperationException("采图和上传须同时绑定");
        return loaded with
        {
            SchemaVersion = 2, WeightValidity = DraftWeightValidity,
            Login = Selected("login"), MaintenanceLogin = Selected("maintenance-auth"), Scale = Selected("weight"), Wcs = Selected("integration"), Plc = Selected("plc"),
            Weight = new(DraftWeightSource, DraftUnit, DraftPrecision, DraftWeightSource == WeightSource.Configured ? DraftConfiguredWeight : null, DraftFreshMs, DraftWaitMs),
            Images = camera is null ? null : new(camera, upload!, DraftImageBytes, DraftUploadStrategy, DraftTotalBytes, DraftPending),
            Reader = loaded.Reader with { Driver = reader.Package is null ? "manual" : "extension:" + reader.InstanceId },
            CapabilityInstances = instances,
            Extensions = instances.Append(reader).Any(x => x.Package is not null) ? new(instances.Append(reader).Where(x => x.Package is not null).Select(x => x.Package!).Distinct().ToArray(),
                reader.Package is null ? [] : [new(reader.InstanceId, reader.Package.Id, reader.Module!, loaded.Reader.DeviceId, true, reader.Parameters)]) : null
        };
    }
    public async Task AuthorizeMaintenanceAsync(string proof) { RequireFreshSnapshot(); var subject = MaintenanceSubject; var r = await ServiceAsync(() => application.AuthorizeMaintenanceAsync(subject, proof)); Message = r.Status + " · " + r.Reason; }
    public async Task InstallAsync()
    {
        RequireFreshSnapshot();
        if (string.IsNullOrWhiteSpace(PackagePath) || !System.IO.Path.IsPathFullyQualified(PackagePath)) throw new InvalidOperationException("填写离线签名包的完整路径");
        if (!ExtensionSchema.IsHash(PackageSha256)) throw new InvalidOperationException("原包 SHA256 必须为64位十六进制；安装前核对原包");
        var path = PackagePath; var sha = PackageSha256;
        var r = await ServiceAsync(() => application.InstallCapabilityAsync(path, sha));
        ApplyInstalledCatalog(await ServiceAsync(() => application.CapabilitiesAsync()));
        Message = r.Status + " · " + r.Reason + "；安装清单已刷新，当前草稿保留。需要新提供者时显式重新读取配置。";
    }
    public async Task ContinueUploadAsync() { var op = Selected ?? throw new InvalidOperationException("选择原作业"); await CommandAsync("ContinueUpload", op.Id); }
    public async Task AbandonAsync()
    { RequireFreshSnapshot();
        var op = Selected ?? throw new InvalidOperationException("选择原作业"); var s = Snapshot ?? throw new InvalidOperationException("刷新后选择原作业");
        var command = new BusinessCommand(Wire.Id(), s.Revision, "AbandonUnsubmitted", op.Id, InspectionReason);
        var r = await ServiceAsync(() => application.CommandAsync(command)); Message = r.Status + " · " + r.Reason;
    }
}
