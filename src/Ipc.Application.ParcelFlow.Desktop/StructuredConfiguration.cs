using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

public sealed record CapabilityChoice(string Label, CapabilityDraft? Capability);
public sealed class CapabilityRoleDraft(string role, string kind, CapabilityChoice[] choices, CapabilityChoice selected, bool visible = true) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    CapabilityChoice selection = selected;
    public string Role { get; } = role;
    public string Kind { get; } = kind;
    public System.Windows.Visibility Visibility => visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public CapabilityChoice[] Choices { get; } = choices;
    public CapabilityChoice Selection { get => selection; set { selection = value; PropertyChanged?.Invoke(this, new(nameof(Selection))); } }
}
public sealed class PositionConfigurationDraft(DeliveryPosition original, CapabilityChoice[] scales, CapabilityChoice[] cameras) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    CapabilityChoice scale = scales.First(x => x.Capability?.InstanceId == original.ScaleInstanceId);
    CapabilityChoice camera = cameras.First(x => x.Capability?.InstanceId == original.CameraInstanceId);
    public DeliveryPosition Original { get; } = original;
    public string Id => Original.Id;
    public string ReaderDeviceId => Original.ReaderDeviceId;
    public CapabilityChoice[] Scales { get; } = scales;
    public CapabilityChoice[] Cameras { get; } = cameras;
    public CapabilityChoice Scale { get => scale; set { scale = value; PropertyChanged?.Invoke(this, new(nameof(Scale))); } }
    public CapabilityChoice Camera { get => camera; set { camera = value; PropertyChanged?.Invoke(this, new(nameof(Camera))); } }
    public DeliveryPosition Build() => Original with { ScaleInstanceId = Scale.Capability?.InstanceId, CameraInstanceId = Camera.Capability?.InstanceId };
}
public sealed record ReaderConfigurationDraft(DeviceBinding Original, CapabilityDraft Capability);
public sealed class ConfigurationPolicyDraft : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    readonly Dictionary<string, object?> values = [];
    T Get<T>([CallerMemberName] string key = "") => values.TryGetValue(key, out var v) && v is T typed ? typed : default!;
    void Set<T>(T value, [CallerMemberName] string key = "") { values[key] = value; PropertyChanged?.Invoke(this, new(key)); }
    public WeightSource WeightSource { get => Get<WeightSource>(); set => Set(value); }
    public WeightUnit Unit { get => Get<WeightUnit>(); set => Set(value); }
    public WeightValidity Validity { get => Get<WeightValidity>(); set => Set(value); }
    public UploadStrategy UploadStrategy { get => Get<UploadStrategy>(); set => Set(value); }
    public string Precision { get => Get<string>(); set => Set(value); }
    public string ConfiguredWeight { get => Get<string>(); set => Set(value); }
    public string MaximumAgeMs { get => Get<string>(); set => Set(value); }
    public string WaitTimeoutMs { get => Get<string>(); set => Set(value); }
    public string ImageBytes { get => Get<string>(); set => Set(value); }
    public string TotalBytes { get => Get<string>(); set => Set(value); }
    public string MaximumPending { get => Get<string>(); set => Set(value); }
    public bool BackupEnabled { get => Get<bool>(); set => Set(value); }
    public string BackupDirectory { get => Get<string>(); set => Set(value); }
    public string BackupIntervalMinutes { get => Get<string>(); set => Set(value); }
    public string BackupMaximumSets { get => Get<string>(); set => Set(value); }
    internal static ConfigurationPolicyDraft From(ParcelConfiguration c) => new()
    {
        WeightSource = c.Weight.Source, Unit = c.Weight.Unit, Validity = c.WeightValidity ?? WeightValidity.UntilDispatch,
        Precision = c.Weight.Precision.ToString(CultureInfo.InvariantCulture), ConfiguredWeight = c.Weight.ConfiguredValue?.ToString(CultureInfo.InvariantCulture) ?? "",
        MaximumAgeMs = c.Weight.MaximumAgeMs.ToString(CultureInfo.InvariantCulture), WaitTimeoutMs = c.Weight.WaitTimeoutMs.ToString(CultureInfo.InvariantCulture),
        BackupEnabled = c.BackupSchedule is not null, BackupDirectory = c.BackupSchedule?.Directory ?? "", BackupIntervalMinutes = c.BackupSchedule?.IntervalMinutes.ToString(CultureInfo.InvariantCulture) ?? "", BackupMaximumSets = c.BackupSchedule?.MaximumSets.ToString(CultureInfo.InvariantCulture) ?? "",
        UploadStrategy = c.Images?.Strategy ?? UploadStrategy.BeforeMain, ImageBytes = (c.Images?.MaximumBytes ?? 4194304).ToString(CultureInfo.InvariantCulture),
        TotalBytes = (c.Images?.TotalBytes ?? 33554432).ToString(CultureInfo.InvariantCulture), MaximumPending = (c.Images?.MaximumPending ?? 32).ToString(CultureInfo.InvariantCulture)
    };
    internal static int Integer(string text, string label, int minimum, int maximum = int.MaxValue) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= minimum && value <= maximum
        ? value : throw new InvalidOperationException(label + "须为" + minimum + "～" + maximum + "之间的整数");
    internal ParcelBackupSchedule? BuildBackup()
    {
        if (!BackupEnabled) return null;
        if (string.IsNullOrWhiteSpace(BackupDirectory) || !Path.IsPathFullyQualified(BackupDirectory)) throw new InvalidOperationException("明确选择计划备份的绝对目录；不使用当前工作目录作为默认");
        return new(Path.GetFullPath(BackupDirectory), Integer(BackupIntervalMinutes, "备份周期(分钟)", 15, 10080), Integer(BackupMaximumSets, "备份容量上限(份)", 1, 1000));
    }
    internal WeightPolicy BuildWeight()
    {
        decimal? configured = null;
        if (WeightSource == WeightSource.Configured)
        {
            if (!decimal.TryParse(ConfiguredWeight, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) || number <= 0 || number > 100000) throw new InvalidOperationException("配置重量须为正数；单位见所选kg/g");
            configured = number;
        }
        var precision = Integer(Precision, "重量精度", 0, 6);
        if (configured is { } value && decimal.Round(value, precision) != value) throw new InvalidOperationException("配置重量的小数位数超过所选精度");
        return new(WeightSource, Unit, precision, configured, Integer(MaximumAgeMs, "测量新鲜度(ms)", 100, 60000), Integer(WaitTimeoutMs, "本件等待期限(ms)", 100, 300000));
    }
}

public partial class ParcelViewModel
{
    ParcelConfiguration? structuredBase;
    CapabilityCatalog? installedCatalog;
    string configurationAuthority = "未加载", savedCandidate = "", validationState = "尚未校验";
    public bool HasLoadedConfiguration => configurationAuthority != "未加载";
    public Task EnsureMaintenanceLoadedAsync() => HasLoadedConfiguration ? Task.CompletedTask : LoadCapabilitiesAsync();
    public bool CanEditLegacyConfiguration => configurationAuthority == "旧版表单";
    public bool CanEditFormConfiguration => configurationAuthority is "结构化表单" or "旧版表单";
    public bool CanEditStructuredConfiguration => configurationAuthority == "结构化表单";
    public bool HasStructuredConfiguration => structuredBase is not null;
    public bool AdvancedConfigurationReadOnly => configurationAuthority != "高级JSON";
    public ObservableCollection<ReaderConfigurationDraft> ReaderDrafts { get; } = [];
    public ObservableCollection<PositionConfigurationDraft> PositionDrafts { get; } = [];
    public ObservableCollection<CapabilityRoleDraft> ConfigurationRoles { get; } = [];
    public ConfigurationPolicyDraft? StructuredPolicy { get; private set; }
    public PackageReference[] InstalledPackageReferences { get; private set; } = [];
    public PackageReference? SelectedInstalledPackage { get; set; }
    public string OriginalRecoveryOperationId { get; set; } = "";
    bool SupportsNewKind(string kind) => kind != "observation" && ((structuredBase?.ApplicationId ?? Snapshot?.ApplicationId) switch
    { "sportmaster" => kind is not ("plc" or "camera" or "upload"), "direct-plc" => kind != "integration", _ => true });
    public System.Windows.Visibility ImageSettingsVisibility => (structuredBase?.ApplicationId ?? Snapshot?.ApplicationId) != "sportmaster" || structuredBase?.Images is not null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public string[] NewCapabilityKinds => installedCatalog?.Providers.Where(x => SupportsNewKind(x.Kind)).Select(x => x.Kind).Distinct().ToArray() ?? [];
    public string NewCapabilityKind { get; set; } = "weight";
    public string ConfigurationEditorState
    {
        get
        {
            var state = "未加载";
            if (configurationAuthority is "高级JSON" or "旧版表单") state = !HasUnsavedConfiguration ? "已加载/已保存" : "已修改未保存";
            else if (configurationAuthority == "结构化表单")
                try { state = Wire.Encode(BuildStructuredConfiguration()) == savedCandidate ? "已加载/已保存" : "已修改未保存"; }
                catch (InvalidOperationException) { state = "已修改，局部字段未通过"; }
            return "编辑来源：" + configurationAuthority + " · " + state + " · " + validationState + "\n" + DraftRevisionState + "\n当前应用：" + (Snapshot?.Configuration.Status ?? "未读取") + " · Effective修订与Pending内容见运行输入页；保存不代表生效。";
        }
    }
    void EditorChanged() { validationState = "修改后需重新校验"; Changed(nameof(ConfigurationEditorState)); Changed(nameof(HasUnsavedConfiguration)); }
    void ApplyInstalledCatalog(CapabilityCatalog catalog)
    {
        installedCatalog = catalog;
        InstalledProviderParameters = Wire.Encode(catalog); Changed(nameof(InstalledProviderParameters));
        InstalledPackages = (catalog.InventoryDiagnostic is null ? "" : catalog.InventoryDiagnostic + "\n") + "已安装签名包（安装不代表绑定、激活或健康）：" + string.Join(" · ", catalog.InstalledPackages); Changed(nameof(InstalledPackages));
        InstalledPackageReferences = (catalog.Packages?.Select(x => x.Package) ?? catalog.Providers.Select(x => x.Package).OfType<PackageReference>()).Distinct().ToArray();
        if (SelectedInstalledPackage is null || !InstalledPackageReferences.Contains(SelectedInstalledPackage)) SelectedInstalledPackage = InstalledPackageReferences.FirstOrDefault();
        Changed(nameof(InstalledPackageReferences)); Changed(nameof(SelectedInstalledPackage)); Changed(nameof(NewCapabilityKinds));
    }
    void StartLegacyConfiguration(ParcelConfiguration c)
    {
        structuredBase = null; configurationAuthority = "旧版表单"; savedCandidate = Wire.Encode(c); validationState = "尚未校验";
        ReaderDrafts.Clear(); PositionDrafts.Clear(); ConfigurationRoles.Clear(); StructuredPolicy = null;
        NotifyConfigurationEditorState();
    }
    public void NotifyConfigurationEditorState()
    { Changed(nameof(HasUnsavedConfiguration)); Changed(nameof(HasLoadedConfiguration)); Changed(nameof(CanEditLegacyConfiguration)); Changed(nameof(CanEditFormConfiguration)); Changed(nameof(CanEditStructuredConfiguration)); Changed(nameof(HasStructuredConfiguration)); Changed(nameof(AdvancedConfigurationReadOnly)); Changed(nameof(StructuredPolicy)); Changed(nameof(ImageSettingsVisibility)); Changed(nameof(ConfigurationEditorState)); }
    void LoadStructuredConfiguration(ParcelConfiguration c, CapabilityCatalog catalog)
    {
        // Construct every reference and selection before changing the current editing
        // authority. A malformed JSON draft must remain intact and editable on failure.
        var drafts = (c.CapabilityInstances ?? []).Concat(ParcelBindings.All(c)).DistinctBy(x => x.InstanceId)
            .Select(b => new CapabilityDraft(b.Kind, catalog.Providers, b, false) { LegacyBindingEditable = false }).ToArray();
        CapabilityChoice[] LocalChoices(string kind, string empty = "不绑定") => [new(empty, null), .. drafts.Where(x => x.Kind == kind).Select(x => new CapabilityChoice(x.InstanceId, x))];
        CapabilityRoleDraft LocalRole(string role, string kind, CapabilityBinding? binding)
        {
            var choices = LocalChoices(kind);
            var visible = c.ApplicationId != "sportmaster" || kind is not ("plc" or "camera" or "upload") || binding is not null;
            return new(role, kind, choices, choices.Single(x => x.Capability?.InstanceId == binding?.InstanceId), visible);
        }
        var readers = new List<ReaderConfigurationDraft>(); var readerIndex = 0;
        foreach (var reader in ParcelBindings.Readers(c))
        {
            var instance = c.Extensions?.Instances.SingleOrDefault(x => reader.Driver == "extension:" + x.Id);
            var package = instance is null ? null : c.Extensions!.Packages.Single(x => x.Id == instance.PackageId);
            var descriptor = package is null ? null : catalog.Providers.SingleOrDefault(x => x.Package == package && x.Module == instance!.ModuleId);
            var binding = instance is null ? new CapabilityBinding("observation", "manual", "1.0.0", "reader." + (++readerIndex), reader.DeviceId, "CONTROLLED-OBSERVATION-v1", [])
                : new("observation", "extension:" + package!.Id + "/" + instance.ModuleId, package.Version, instance.Id, reader.DeviceId, descriptor?.Contract ?? "CONTROLLED-OBSERVATION-v1", instance.Parameters, package, instance.ModuleId, instance.Enabled);
            readers.Add(new(reader, new("observation", catalog.Providers, binding, true, reader.Role)));
        }
        CapabilityRoleDraft[] roles = [LocalRole("登录", "login", c.Login), LocalRole("维护认证", "login", c.MaintenanceLogin), LocalRole("默认秤", "weight", c.Scale),
            LocalRole("上游格口", "integration", c.Wcs), LocalRole("PLC", "plc", c.Plc), LocalRole("默认相机", "camera", c.Images?.Camera), LocalRole("图片上传", "upload", c.Images?.Uploader)];
        var positions = ParcelBindings.Positions(c).Select(position => new PositionConfigurationDraft(position, LocalChoices("weight", "沿用默认秤"), LocalChoices("camera", "沿用默认相机"))).ToArray();
        var policy = ConfigurationPolicyDraft.From(c);
        structuredBase = c; configurationAuthority = "结构化表单"; validationState = "尚未校验";
        CapabilityDrafts.Clear(); ReaderDrafts.Clear(); PositionDrafts.Clear(); ConfigurationRoles.Clear();
        foreach (var draft in drafts) { draft.PropertyChanged += (_, _) => EditorChanged(); CapabilityDrafts.Add(draft); }
        foreach (var reader in readers) { reader.Capability.PropertyChanged += (_, _) => EditorChanged(); ReaderDrafts.Add(reader); }
        foreach (var role in roles) { role.PropertyChanged += (_, _) => EditorChanged(); ConfigurationRoles.Add(role); }
        foreach (var position in positions) { position.PropertyChanged += (_, _) => EditorChanged(); PositionDrafts.Add(position); }
        StructuredPolicy = policy; StructuredPolicy.PropertyChanged += (_, _) => EditorChanged();
        try { savedCandidate = Wire.Encode(BuildStructuredConfiguration()); }
        catch (InvalidOperationException) { savedCandidate = Wire.Encode(c); }
        AcceptEditorBaseline(); NotifyConfigurationEditorState();
    }
    CapabilityChoice[] Choices(string kind, string empty = "不绑定") => [new(empty, null), .. CapabilityDrafts.Where(x => x.Kind == kind).Select(x => new CapabilityChoice(x.InstanceId, x))];
    void AddRole(string role, string kind, CapabilityBinding? binding)
    {
        var choices = Choices(kind); var selected = choices.Single(x => x.Capability?.InstanceId == binding?.InstanceId);
        var item = new CapabilityRoleDraft(role, kind, choices, selected); item.PropertyChanged += (_, _) => EditorChanged(); ConfigurationRoles.Add(item);
    }
    public static PackageReference[] ResolvePackageClosure(IEnumerable<PackageReference> roots, CapabilityPackage[] catalog)
    {
        var selected = new Dictionary<string, PackageReference>(StringComparer.Ordinal);
        var visited = new HashSet<PackageReference>(); var visiting = new HashSet<PackageReference>();
        var ordered = new List<PackageReference>();
        void Visit(PackageReference package)
        {
            if (!ExtensionSchema.IsId(package.Id) || !ExtensionSchema.IsVersion(package.Version) || !ExtensionSchema.IsHash(package.ManifestSha256)) throw new InvalidOperationException("扩展包精确身份格式不合法");
            if (selected.TryGetValue(package.Id, out var other) && other != package) throw new InvalidOperationException("依赖闭包中同ID存在冲突版本或清单SHA256：" + package.Id);
            selected[package.Id] = package;
            if (visited.Contains(package)) return;
            if (!visiting.Add(package)) throw new InvalidOperationException("已安装包目录存在循环依赖：" + package.Id);
            var matches = catalog.Where(x => x.Package == package).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("安装目录缺少唯一精确包/依赖记录：" + package.Id + " @ " + package.Version + " / " + package.ManifestSha256);
            foreach (var dependency in matches[0].Dependencies) Visit(dependency);
            visiting.Remove(package); visited.Add(package); ordered.Add(package);
        }
        foreach (var root in roots) Visit(root);
        return ordered.ToArray();
    }
    public ParcelConfiguration BuildStructuredConfiguration()
    {
        var c = structuredBase ?? throw new InvalidOperationException("先读取schema3完整拓扑");
        if (configurationAuthority != "结构化表单") throw new InvalidOperationException("高级JSON为当前编辑来源；显式加载JSON到表单后再保存表单");
        var bindings = CapabilityDrafts.Select(x => x.Build()).ToArray();
        if (bindings.Select(x => x.InstanceId).Distinct().Count() != bindings.Length) throw new InvalidOperationException("能力实例身份不能重复");
        CapabilityBinding? Role(string name) => ConfigurationRoles.Single(x => x.Role == name).Selection.Capability is { } draft ? bindings.Single(x => x.InstanceId == draft.InstanceId) : null;
        var readerBindings = ReaderDrafts.Select(x => x.Capability.Build()).ToArray();
        var readers = ReaderDrafts.Select((x, i) =>
        {
            var b = readerBindings[i];
            if (b.Target != x.Original.DeviceId || !b.Enabled) throw new InvalidOperationException("读码稳定DeviceId只读且必须启用；设备迁移需独立受控部署");
            return x.Original with { Driver = b.Package is null ? "manual" : "extension:" + b.InstanceId };
        }).ToArray();
        var oldReaderIds = ReaderDrafts.Select(x => x.Original.Driver).Where(x => x.StartsWith("extension:", StringComparison.Ordinal)).Select(x => x[10..]).ToHashSet();
        var instances = (c.Extensions?.Instances ?? []).Where(x => !oldReaderIds.Contains(x.Id)).Concat(readerBindings.Where(x => x.Package is not null).Select(x => new ExtensionInstance(x.InstanceId, x.Package!.Id, x.Module!, x.Target, x.Enabled, x.Parameters))).ToArray();
        var selectedRoots = bindings.Concat(readerBindings).Select(x => x.Package).OfType<PackageReference>().Distinct().ToArray();
        var closure = ResolvePackageClosure(selectedRoots, installedCatalog?.Packages ?? []);
        // Preserve the order of unchanged exact references; remove obsolete roots and
        // their now-unused dependencies without uninstalling the original store package.
        var packages = (c.Extensions?.Packages ?? []).Where(closure.Contains).Concat(closure).Distinct().ToArray();
        var extensions = c.Extensions is null && packages.Length == 0 ? null : new DeploymentExtensions(packages, instances, c.Extensions?.Services, c.Extensions?.Ports);
        var policy = StructuredPolicy ?? throw new InvalidOperationException("策略草稿未加载");
        var camera = Role("默认相机"); var uploader = Role("图片上传");
        if (camera is null && uploader is not null) throw new InvalidOperationException("图片上传绑定需要默认相机与图像策略");
        long total = 0;
        if (camera is not null && (!long.TryParse(policy.TotalBytes, NumberStyles.None, CultureInfo.InvariantCulture, out total) || total <= 0 || total > 268435456)) throw new InvalidOperationException("图片缓存总字节须为1～268435456之间的整数");
        var image = camera is null ? null : new ImagePolicy(camera, uploader, ConfigurationPolicyDraft.Integer(policy.ImageBytes, "单件最大字节", 1, 4194304), policy.UploadStrategy, total, ConfigurationPolicyDraft.Integer(policy.MaximumPending, "图片待处理件数", 1, 256));
        if (image is not null && image.TotalBytes < image.MaximumBytes) throw new InvalidOperationException("图片缓存总字节不能小于单件最大字节");
        var originalInventory = (c.CapabilityInstances ?? []).Select(x => x.InstanceId).ToHashSet();
        var originalAll = ParcelBindings.All(c).Select(x => x.InstanceId).ToHashSet();
        var roleIds = ConfigurationRoles.Select(x => x.Selection.Capability?.InstanceId).OfType<string>().ToHashSet();
        var inventory = bindings.Where(x => originalInventory.Contains(x.InstanceId) || !originalAll.Contains(x.InstanceId) || !roleIds.Contains(x.InstanceId)).ToArray();
        return c with
        {
            Reader = readers.Single(x => x.DeviceId == c.Reader.DeviceId), Topology = new(readers, PositionDrafts.Select(x => x.Build()).ToArray()),
            CapabilityInstances = inventory.Length == 0 && c.CapabilityInstances is null ? null : inventory, Login = Role("登录"), MaintenanceLogin = Role("维护认证"), Scale = Role("默认秤"), Wcs = Role("上游格口"), Plc = Role("PLC"),
            Weight = policy.BuildWeight(), WeightValidity = policy.Validity, Images = image, Extensions = extensions, BackupSchedule = policy.BuildBackup()
        };
    }
    public Task AddCapabilityInstanceAsync()
    {
        if (configurationAuthority != "结构化表单") throw new InvalidOperationException("先显式加载结构化表单");
        var catalog = installedCatalog ?? throw new InvalidOperationException("先读取安装清单");
        if (!NewCapabilityKinds.Contains(NewCapabilityKind)) throw new InvalidOperationException("选择已安装的能力类型");
        var number = 1; while (CapabilityDrafts.Any(x => x.InstanceId == NewCapabilityKind + "." + number)) number++;
        var draft = new CapabilityDraft(NewCapabilityKind, catalog.Providers, null, false) { LegacyBindingEditable = false, InstanceId = NewCapabilityKind + "." + number };
        draft.PropertyChanged += (_, _) => EditorChanged(); CapabilityDrafts.Add(draft); RebuildConfigurationSelections(); EditorChanged();
        Message = "已增加未绑定实例；填写目标与参数，在角色或本位绑定中选择，完整校验后保存。"; return Task.CompletedTask;
    }
    public Task RemoveCapabilityInstanceAsync(CapabilityDraft? draft)
    {
        if (configurationAuthority != "结构化表单" || draft is null) throw new InvalidOperationException("选择结构化库存中的实例");
        if (ConfigurationRoles.Any(x => x.Selection.Capability == draft) || PositionDrafts.Any(x => x.Scale.Capability == draft || x.Camera.Capability == draft)) throw new InvalidOperationException("先解除角色和本位绑定；不能直接移除仍被引用的实例");
        CapabilityDrafts.Remove(draft); RebuildConfigurationSelections(); EditorChanged(); Message = "已从候选库存移除未绑定实例；已安装原包和Effective不变。"; return Task.CompletedTask;
    }
    void RebuildConfigurationSelections()
    {
        var roles = ConfigurationRoles.Select(x => (x.Role, x.Kind, Binding: x.Selection.Capability)).ToArray();
        var positions = PositionDrafts.Select(x => x.Build()).ToArray(); ConfigurationRoles.Clear(); PositionDrafts.Clear();
        foreach (var item in roles)
        {
            var choices = Choices(item.Kind); var role = new CapabilityRoleDraft(item.Role, item.Kind, choices, choices.Single(x => x.Capability == item.Binding));
            role.PropertyChanged += (_, _) => EditorChanged(); ConfigurationRoles.Add(role);
        }
        foreach (var position in positions)
        {
            var item = new PositionConfigurationDraft(position, Choices("weight", "沿用默认秤"), Choices("camera", "沿用默认相机"));
            item.PropertyChanged += (_, _) => EditorChanged(); PositionDrafts.Add(item);
        }
    }
    public async Task ValidateStructuredConfigurationAsync()
    {
        var candidate = BuildStructuredConfiguration(); var receipt = await ServiceAsync(() => application.ValidateConfigurationAsync(candidate));
        validationState = receipt.Status + " · " + receipt.Reason; Changed(nameof(ConfigurationEditorState)); Message = "表单完整候选校验：" + validationState + "；未保存。";
    }
    public async Task SaveStructuredConfigurationAsync()
    {
        RequireFreshSnapshot(); var expected = RequireDraftRevision(); var candidate = BuildStructuredConfiguration(); var submittedSignature = ConfigurationEditSignature();
        var validation = await ServiceAsync(() => application.ValidateConfigurationAsync(candidate));
        if (validation.Status != "Validated") { validationState = validation.Status + " · " + validation.Reason; Changed(nameof(ConfigurationEditorState)); Message = validationState; return; }
        var reason = ConfigurationReason;
        var receipt = await ServiceAsync(() => application.StageConfigurationAsync(expected, candidate, reason));
        AdvanceOwnDraftRevision(expected, receipt);
        if (receipt.Status == "PendingRestart") { savedEditorSignature = submittedSignature; savedCandidate = Wire.Encode(candidate); ConfigurationDraft = savedCandidate; Changed(nameof(ConfigurationDraft)); }
        validationState = receipt.Status + " · " + receipt.Reason; Changed(nameof(ConfigurationEditorState)); Message = validationState;
    }
    public async Task LoadStructuredFromJsonAsync()
    {
        if (configurationAuthority != "高级JSON") throw new InvalidOperationException("先切换到高级JSON编辑来源");
        RequireDraftRevision(); var sourceJson = ConfigurationDraft; var c = Wire.Decode<ParcelConfiguration>(sourceJson);
        if (c.SchemaVersion < 3 || c.Topology is null) throw new InvalidOperationException("此入口需要schema3完整拓扑；旧schema1/2使用原表单");
        var catalog = installedCatalog ?? throw new InvalidOperationException("先读取安装清单");
        var validation = await ServiceAsync(() => application.ValidateConfigurationAsync(c));
        if (validation.Status != "Validated") { Message = "JSON未切换到表单：" + validation.Status + " · " + validation.Reason; return; }
        // The operator may continue typing during validation. Adopt only the exact
        // document inspected by this action; leave a newer JSON draft untouched.
        if (ConfigurationDraft != sourceJson) { Message = "JSON在校验期间已修改；保留当前编辑，请重新加载表单。"; return; }
        LoadStructuredConfiguration(c, catalog);
        savedEditorSignature = ""; savedCandidate = ""; EditorChanged(); Message = "已显式采用高级JSON作为表单草稿；未保存，原CAS保持。";
    }
    public Task SwitchToAdvancedConfigurationAsync()
    {
        if (configurationAuthority == "结构化表单") ConfigurationDraft = Wire.Encode(BuildStructuredConfiguration());
        else if (configurationAuthority == "旧版表单") ConfigurationDraft = Wire.Encode(BuildLegacyConfiguration());
        else if (configurationAuthority == "未加载") throw new InvalidOperationException("先读取当前配置，再切换编辑来源");
        configurationAuthority = "高级JSON"; Changed(nameof(ConfigurationDraft)); NotifyConfigurationEditorState(); Message = "高级JSON为当前编辑来源；表单保存已停用。返回表单须显式加载此JSON。"; return Task.CompletedTask;
    }
    public async Task ValidateAdvancedConfigurationAsync()
    { RequireAdvancedConfigurationAuthority(); await ValidateConfigurationDraftAsync(); }
    public async Task SaveAdvancedConfigurationAsync()
    {
        RequireAdvancedConfigurationAuthority(); RequireFreshSnapshot(); var expected = RequireDraftRevision();
        var submittedJson = ConfigurationDraft; var candidate = Wire.Decode<ParcelConfiguration>(submittedJson); var reason = ConfigurationReason;
        var receipt = await ServiceAsync(() => application.StageConfigurationAsync(expected, candidate, reason));
        AdvanceOwnDraftRevision(expected, receipt);
        if (receipt.Status == "PendingRestart") savedCandidate = submittedJson;
        validationState = receipt.Status + " · " + receipt.Reason; Changed(nameof(ConfigurationEditorState)); Message = validationState;
    }
    void RequireFormConfigurationAuthority()
    { if (!CanEditFormConfiguration) throw new InvalidOperationException("表单当前不是编辑来源；请显式加载JSON到表单，或重新读取当前配置"); }
    void RequireAdvancedConfigurationAuthority()
    { if (configurationAuthority != "高级JSON") throw new InvalidOperationException("高级JSON当前只读；先显式切换编辑来源，避免覆盖表单草稿"); }
    public async Task UninstallSelectedPackageAsync()
    {
        RequireFreshSnapshot(); var package = SelectedInstalledPackage ?? throw new InvalidOperationException("先选择已安装包的精确身份");
        if (!InstalledPackageReferences.Contains(package)) throw new InvalidOperationException("所选精确包不在本次安装清单；重新读取");
        var r = await ServiceAsync(() => application.ManageCapabilityAsync("extension-uninstall", Wire.Encode(package))); ApplyInstalledCatalog(await ServiceAsync(() => application.CapabilitiesAsync())); Message = r.Status + " · " + r.Reason;
    }
    public async Task RecoverOriginalInstallationAsync()
    {
        RequireFreshSnapshot(); if (string.IsNullOrWhiteSpace(OriginalRecoveryOperationId)) throw new InvalidOperationException("高级恢复必须从原安装日志复制确切操作ID；应用没有枚举该日志，不可猜测");
        var operationId = OriginalRecoveryOperationId;
        var r = await ServiceAsync(() => application.ManageCapabilityAsync("extension-recover", operationId)); ApplyInstalledCatalog(await ServiceAsync(() => application.CapabilitiesAsync())); Message = r.Status + " · " + r.Reason;
    }
}
