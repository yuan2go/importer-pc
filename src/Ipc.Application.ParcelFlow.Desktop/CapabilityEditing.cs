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
    public bool IsSensitive => Field.Sensitive || Field.Type == "secret-reference";
    public int EditorMaximumLength => IsList ? checked(Field.MaximumLength * Field.MaximumItems + 8 * Field.MaximumItems) : Field.MaximumLength;
    public string EditorLabel => IsList ? Field.Label.Replace("（每行一项）", "") + "（结构化数组）" : Field.Label;
    public string EditorHelp => IsList ? "结构化字符串数组，例如 [\"标签1\",\"标签2\"]；最多" + Field.MaximumItems + "项，每项最多" + Field.MaximumLength + "字符；保留空项和转义。" :
        Field.Description ?? (Field.Type switch
        {
            "integer" => "整数" + (Field.Minimum is { } min ? "，最小 " + min : "") + (Field.Maximum is { } max ? "，最大 " + max : ""),
            "ipv4" => "网络地址（四段数字）",
            "boolean" => "选择启用或关闭",
            "choice" => "选择已声明的可用值",
            _ => "最多 " + Field.MaximumLength + " 个字符"
        });
    public string Value { get => current; set { current = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
}
public sealed class CapabilityDraft : INotifyPropertyChanged
{
    internal static CapabilityBinding BuiltInReaderBinding(DeviceBinding reader, string instanceId, CapabilityCatalog catalog)
    {
        // DeviceBinding pins the built-in implementation, not a provider version.
        // Use this host's declarative inventory; extension bindings take a separate,
        // exact package/version/module path and must never be upgraded here.
        var contract = reader.Driver switch { "tcp-line" => "TCP-LINE-OBSERVATION-v1", "hikrobot-mvs" => "HIKROBOT-MVS-ACQUISITION-v1", _ => "CONTROLLED-OBSERVATION-v1" };
        var installed = catalog.Providers.SingleOrDefault(p => p.Kind == "observation" && p.Implementation == reader.Driver &&
            p.Contract == contract && p.Package is null && p.Module is null);
        // An unavailable provider remains unresolved and cannot be built/saved.
        return new("observation", reader.Driver, installed?.Version ?? "", instanceId, reader.DeviceId, contract, reader.Parameters ?? []);
    }

    readonly CapabilityBinding? originalBinding;
    public sealed record Saved(string Kind, string Role, CapabilityBinding? Original, string ProviderIdentity, string Instance, string Target, string Contract, bool Enabled, bool Bound, Dictionary<string, string> Parameters)
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public bool ConfigureLater { get; init; }
        public string? ProposedProviderIdentity { get; init; }
    }
    static string Identity(CapabilityDescriptor? p) => p is null ? "" : Wire.Encode(new { p.Kind, p.Implementation, p.Version, p.Contract, p.Package, p.Module });
    public Saved CaptureRaw() => new(Kind, Role, originalBinding, Identity(Provider), InstanceId, Target, Contract, Enabled, Bound, Parameters.ToDictionary(p => p.Field.Key, p => p.Value)) { ConfigureLater = ConfigureLater, ProposedProviderIdentity = proposedProvider is null ? null : Identity(proposedProvider) };
    public static CapabilityDraft RestoreRaw(Saved state, CapabilityDescriptor[] providers)
    {
        var draft = new CapabilityDraft(state.Kind, providers, state.Original, state.Bound, state.Role);
        if (state.ProviderIdentity.Length > 0)
        {
            var match = draft.Providers.SingleOrDefault(p => Identity(p) == state.ProviderIdentity) ?? throw new InvalidOperationException("草稿精确提供者缺失；原输入保留");
            if (draft.Provider != match) draft.Provider = match;
        }
        else draft.Provider = null;
        draft.InstanceId = state.Instance; draft.Target = state.Target; draft.Contract = state.Contract; draft.Enabled = state.Enabled;
        draft.ConfigureLater = state.ConfigureLater;
        foreach (var p in draft.Parameters) p.Value = state.Parameters.GetValueOrDefault(p.Field.Key, p.Value);
        if (state.ProposedProviderIdentity is { } proposed)
            draft.ProposeProvider(draft.Providers.SingleOrDefault(p => Identity(p) == proposed) ?? throw new InvalidOperationException("草稿中待切换的精确提供者缺失；原输入保留"));
        return draft;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    CapabilityDescriptor? provider;
    CapabilityDescriptor? proposedProvider;
    public CapabilityDescriptor? ProposedProvider => proposedProvider;
    public bool ProviderChangePending => proposedProvider is not null;
    public void ProposeProvider(CapabilityDescriptor selected)
    {
        if (!Providers.Contains(selected)) throw new InvalidOperationException("只能选择当前已安装的精确提供者");
        proposedProvider = selected == Provider ? null : selected;
        Notify(nameof(ProposedProvider)); Notify(nameof(ProviderChangePending));
    }
    public void CancelProviderChange() { proposedProvider = null; Notify(nameof(ProposedProvider)); Notify(nameof(ProviderChangePending)); }
    public void ApplyProviderChange()
    {
        var selected = proposedProvider ?? throw new InvalidOperationException("尚未选择新的连接方式");
        CancelProviderChange(); Provider = selected;
    }
    public CapabilityDescriptor[] Providers { get; }
    public string Kind { get; }
    public string Role { get; }
    bool enabled, bound, configureLater;
    public bool ConfigureLater { get => configureLater; set { configureLater = value; Notify(); } }
    string instanceId = "", target = "", contract = "";
    public bool Enabled { get => enabled; set { enabled = value; Notify(); } }
    public bool Bound { get => bound; set { bound = value; Notify(); } }
    public string InstanceId { get => instanceId; set { instanceId = value; Notify(); } }
    public bool IsTargetReadOnly => Kind == "observation";
    public string TargetBoundary => IsTargetReadOnly ? "稳定读码设备标识只读；更换须清偿原件并受控迁移部署" : ParameterOwnership.IsDriver(this) ? "驱动拥有的硬件协议目标；保存后待受控重启" : "组件拥有的外部服务目标；保存后待受控重启";
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
            proposedProvider = null;
            provider = value; Parameters.Clear(); originalParameters.Clear(); preserveOmittedDefaults = false;
            if (value is not null) foreach (var parameter in value.Parameters) { var item = new CapabilityParameter(parameter, parameter.Default ?? ""); item.PropertyChanged += (_, _) => Notify(nameof(Parameters)); Parameters.Add(item); }
            Contract = value?.Contract ?? "";
            Notify(nameof(Provider)); Notify(nameof(Contract)); Notify(nameof(ProviderIdentity));
        }
    }
    public CapabilityDraft(string kind, CapabilityDescriptor[] providers, CapabilityBinding? binding, bool bound, string? role = null)
    {
        originalBinding = binding;
        Kind = kind; Role = role ?? kind; Providers = providers.Where(x => x.Kind == kind).ToArray();
        InstanceId = binding?.InstanceId ?? kind + ".one"; Target = binding?.Target ?? "reference://" + kind + "/one"; Contract = binding?.Contract ?? "";
        Provider = binding is null ? Providers.FirstOrDefault() : Providers.SingleOrDefault(x => x.Implementation == binding.Implementation && x.Version == binding.Version && x.Package == binding.Package && x.Module == binding.Module);
        if (binding is not null) foreach (var p in Parameters) p.Value = binding.Parameters.GetValueOrDefault(p.Field.Key) ?? p.Field.Default ?? "";
        if (binding is not null) { preserveOmittedDefaults = true; OriginalProviderIdentity = binding.Implementation + " @ " + binding.Version + " · " + (binding.Package?.ManifestSha256 ?? "内置参考") + " · " + binding.Module; Contract = binding.Contract; foreach (var pair in binding.Parameters) originalParameters[pair.Key] = pair.Value; }
        Enabled = binding?.Enabled ?? false; Bound = bound;
    }
    public CapabilityBinding Build()
    {
        if (ProviderChangePending) throw new InvalidOperationException(InstanceId + "/Provider: 请确认切换并重置参数，或取消切换；当前输入保留");
        if (originalBinding is not null && InstanceId != originalBinding.InstanceId) throw new InvalidOperationException("稳定实例身份只读；不能通过普通参数修改身份：" + originalBinding.InstanceId);
        var p = Provider ?? throw new InvalidOperationException("选择已安装的能力提供者：" + Kind);
        // Keep original insertion order and explicitly omitted defaults. Reordering
        // unrelated parameter bytes would change the frozen configuration hash.
        var parameters = new Dictionary<string, string>(originalParameters);
        foreach (var item in Parameters)
        {
            if (!ParameterOwnership.Applicable(this, item.Field.Key)) { parameters.Remove(item.Field.Key); continue; }
            if (item.Value.Length == 0 && originalParameters.GetValueOrDefault(item.Field.Key) != "") { parameters.Remove(item.Field.Key); continue; }
            if (!preserveOmittedDefaults || originalParameters.ContainsKey(item.Field.Key) || item.Value != item.Field.Default) parameters[item.Field.Key] = item.Value;
        }
        if (!ConfigureLater && ParameterOwnership.StationConflict(this))
            throw new InvalidOperationException("供包台编号不一致；请在系统配置核对原值并填写共同编号");
        var errors = ConfigureLater ? [] : ExtensionSchema.Validate(p.Parameters, parameters);
        if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors));
        return new(Kind, p.Implementation, p.Version, InstanceId, Target, Contract, parameters, p.Package, p.Module, Enabled);
    }
    public CapabilityBinding BuildReader(string applicationId)
    {
        var binding = Build();
        // The confirmed Sportmaster flow is one camera scan per parcel. Generic
        // observation providers retain their own diagnostic/association policies.
        return applicationId == "sportmaster" && binding.Implementation == "tcp-line"
            ? binding with { Parameters = new(binding.Parameters) { ["entity.mode"] = "line" } } : binding;
    }
}

public partial class ParcelViewModel
{
    public ObservableCollection<CapabilityDraft> CapabilityDrafts { get; } = [];
    public string InstalledPackages { get; private set; } = "尚未读取安装清单";
    public string PackagePath { get; set; } = "";
    public string PackageSha256 { get; set; } = "";
    public string InspectionReason { get; set; } = "";
    public async Task LoadCapabilitiesAsync()
    {
        await RequireManagementModeAsync();
        var administration = (IWorkstationAdministration)application;
        var authority = await ServiceAsync(administration.ManagementModeStatusAsync);
        if (!authority.Authorized || string.IsNullOrWhiteSpace(authority.SessionId)) throw new UnauthorizedAccessException("请先进入管理模式；未保存输入保留。");
        // A concurrent refresh can supersede its caller before applying its
        // result. Never construct an editable candidate from that older UI
        // snapshot, which may still be the ordinary, redacted projection.
        var loaded = await ServiceAsync(() => application.SnapshotAsync());
        var c = loaded.Configuration.Pending ?? loaded.Configuration.Effective;
        var catalog = await ServiceAsync(() => application.CapabilitiesAsync());
        var baseline = await ReadEditorBaseline(loaded.Configuration.Revision);
        var currentAuthority = await ServiceAsync(administration.ManagementModeStatusAsync);
        if (!currentAuthority.Authorized || currentAuthority != authority) throw new UnauthorizedAccessException("管理授权已变化，请重新读取参数；现有输入保留。");
        if (c.SchemaVersion != 3 || c.Topology is null || c.MaintenanceLogin is not null)
            throw new InvalidOperationException("配置格式不受支持，保留原件；请使用当前完整拓扑配置。");
        SetConfigurationDraft(c, loaded.Configuration.Revision, loaded.RuntimeId);
        ApplyInstalledCatalog(catalog);
        CapabilityDrafts.Clear();
        LoadStructuredConfiguration(c, catalog);
        editorConfigurationHash = baseline.Hash; availableEditorDraft = baseline.Draft; savedEditorHash = baseline.Draft?.DraftHash;
        Message = "已读取完整拓扑与能力库存；保存后待受控重启生效。";
    }
    public async Task SaveCapabilitiesAsync()
    {
        RequireFreshSnapshot(); RequireFormConfigurationAuthority();
        if (Snapshot is not null) await SaveStructuredConfigurationAsync();
    }
    public async Task AuthorizeMaintenanceAsync(string proof) { RequireFreshSnapshot(); var r = await ServiceAsync(() => application.AuthorizeMaintenanceAsync(proof)); Message = r.Status + " · " + r.Reason; }
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
