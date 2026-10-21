using System.Collections.ObjectModel;
using System.ComponentModel;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

public sealed record ParameterIssue(string Category, string Path, string Message);
// An editor over the existing complete draft, never a separately persisted configuration.
public sealed class ParameterEditorModel(ConfigurationPolicyDraft policy, string applicationId,
    CapabilityDraft[] readers, CapabilityDraft[] components, Func<BarcodeFilterPolicy, string[], Task<BarcodeFilterResult>> preview, string stationId = "", ParcelConfigurationLocations? locations = null,
    CapabilityRoleDraft? operatorLoginRole = null, CapabilityRoleDraft[]? componentRoles = null)
{
    public ConfigurationPolicyDraft Policy { get; } = policy;
    public string ApplicationId { get; } = applicationId;
    public string StationId { get; } = stationId;
    public ParcelConfigurationLocations? Locations { get; } = locations;
    public CapabilityDraft[] Readers { get; } = readers;
    public CapabilityDraft[] Components { get; } = components;
    // Daily schema-3 roles and startup bindings already own their respective candidate.
    // This is a read-only projection; never copy role state into the component inventory.
    public CapabilityRoleDraft? OperatorLoginRole { get; } = operatorLoginRole;
    public bool HasOperatorLogin => OperatorLoginRole is { } role ? role.Selection.Capability is not null
        : Components.Any(c => c.Role == "operator-login" && c.Bound);
    public IReadOnlyList<CapabilityRoleDraft> ComponentRoles { get; } = componentRoles ?? [];
    // Display existing role ownership without changing the inventory or bindings.
    public string ComponentLabel(CapabilityDraft component)
    {
        var roles = ComponentRoles.Where(r => ReferenceEquals(r.Selection.Capability, component)).Select(r => r.Role switch
        { "登录" => "操作员登录", "默认秤" => "默认称重", _ => r.Role }).ToArray();
        return roles.Length > 0 ? string.Join(" / ", roles) : component.Role switch
        { "maintenance-auth" => "维护认证", "operator-login" => "操作员登录", "default-scale" => "默认称重", _ => ChineseInterfaceText.CapabilityKind(component.Kind) + " · " + component.InstanceId };
    }
    public ObservableCollection<ParameterIssue> Issues { get; } = [];
    public Func<BarcodeFilterPolicy, string[], Task<BarcodeFilterResult>> Preview { get; } = preview;
    public event EventHandler? ValidationChanged;
    public void Report(string message)
    {
        if (message.Contains("BarcodeFilter.Pattern", StringComparison.Ordinal)) Issues.Add(new("业务模块", "BarcodePattern", message));
        else if (message.Contains("StationCode", StringComparison.Ordinal) || message.Contains("station code", StringComparison.OrdinalIgnoreCase)) Issues.Add(new("系统", "StationCode", message));
        else
        {
            var matching = Readers.Concat(Components).SelectMany(d => d.Parameters.Select(p => (Draft: d, Field: p.Field))).Where(x => message.Contains(x.Field.Key, StringComparison.Ordinal)).ToArray();
            if (matching.Length == 0) Issues.Add(new("完整候选", "", message));
            else foreach (var item in matching) Issues.Add(new(ParameterOwnership.CategoryName(ParameterOwnership.Category(item.Draft, item.Field.Key)), item.Draft.InstanceId + "/" + item.Field.Key, message));
        }
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }
    public bool ValidateFields(int? category = null)
    {
        Issues.Clear();
        void Issue(string category, string key, string message) => Issues.Add(new(category, key, message));
        void Number(string name, string value, int min, int max)
        { try { _ = ConfigurationPolicyDraft.Integer(value, name, min, max); } catch (InvalidOperationException ex) { Issue(name is "QueueCapacity" or "TimeoutMs" or "MaximumOperations" ? "业务模块" : "系统", name, ex.Message); } }
        Number(nameof(Policy.QueueCapacity), Policy.QueueCapacity, 1, 4096); Number(nameof(Policy.TimeoutMs), Policy.TimeoutMs, 100, 30000);
        Number(nameof(Policy.MaximumOperations), Policy.MaximumOperations, 1, 100000); Number(nameof(Policy.ResidentHistory), Policy.ResidentHistory, 1, 10000);
        Number(nameof(Policy.SessionLifetimeMs), Policy.SessionLifetimeMs, 100, 86400000);
        if (!decimal.TryParse(Policy.MinimumFreeMiB, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var reserve) || reserve < 1 || reserve > 1048576)
            Issue("系统", nameof(Policy.MinimumFreeMiB), "磁盘保留须为1～1048576 兆字节");
        if (Policy.StationCode.Length > 256 || Policy.StationCode.Any(char.IsControl) || string.IsNullOrWhiteSpace(Policy.StationCode))
            Issue("系统", nameof(Policy.StationCode), "工位外部编码须为1～256个非控制字符");
        if (Policy.BackupEnabled)
        {
            Number(nameof(Policy.BackupIntervalMinutes), Policy.BackupIntervalMinutes, 15, 10080);
            Number(nameof(Policy.BackupMaximumSets), Policy.BackupMaximumSets, 1, 1000);
            try
            {
                if (string.IsNullOrWhiteSpace(Policy.BackupDirectory) || !System.IO.Path.IsPathFullyQualified(Policy.BackupDirectory))
                    throw new ArgumentException("计划备份须指定绝对目录");
                _ = System.IO.Path.GetFullPath(Policy.BackupDirectory);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
            { Issue("系统", nameof(Policy.BackupDirectory), ex.Message); }
        }
        foreach (var (draft, fieldCategory) in Readers.Select(x => (x, "驱动")).Concat(Components.Where(x => x.Bound || x.Enabled).Select(x => (x, "组件"))))
        {
            if (draft.Provider is null) { Issue(fieldCategory, draft.InstanceId, draft.ProviderIdentity); continue; }
            var values = draft.Parameters.Where(p => p.Value.Length > 0 && ParameterOwnership.Applicable(draft, p.Field.Key)).ToDictionary(p => p.Field.Key, p => p.Value);
            foreach (var error in ExtensionSchema.Validate(draft.Provider.Parameters, values))
                Issue(ParameterOwnership.CategoryName(ParameterOwnership.Category(draft, error.Split(':')[0])), draft.InstanceId + "/" + error.Split(':')[0], draft.Role + " · " + error);
            // Mirror the installed Hikrobot driver's existing address-format rule
            // before review. This is local feedback, not a connection probe or a
            // replacement for Application/driver validation at the real boundary.
            if (draft.Provider.Implementation == "extension:hikrobot.idmvs-reader/idmvs-reader" && values.TryGetValue("device.ip", out var ip) &&
                (!System.Net.IPAddress.TryParse(ip, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || address.ToString() != ip || ip is "0.0.0.0" or "255.255.255.255"))
                Issue("驱动", draft.InstanceId + "/device.ip", "请填写完整网络地址：四段 0～255 的数字，以点分隔；不能使用全零或广播地址。");
            if (ParameterOwnership.StationConflict(draft))
                Issue("系统", draft.InstanceId + "/left-station-code", "左右取格口站码不一致；请核对原值并明确填写共同站码。");
            if (ParameterOwnership.IsRcs(draft) && values.GetValueOrDefault("specified-chute") == "true" && !values.ContainsKey("chute-code"))
                Issue("业务模块", draft.InstanceId + "/chute-code", "启用指定格口时须填写格口编码。");
            if (ParameterOwnership.IsRcs(draft))
            {
                if (!Uri.TryCreate(draft.Target, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https") ||
                    endpoint.AbsolutePath != "/rcs/sorter/api" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.UserInfo.Length != 0)
                    Issue("组件", draft.InstanceId + "/Target", "调度服务地址须使用网页传输协议，接口路径为 /rcs/sorter/api，不能含凭据、查询串或片段。");
                if (long.TryParse(values.GetValueOrDefault("request-id-base"), out var start) && long.TryParse(values.GetValueOrDefault("request-id-count"), out var count) &&
                    (start < 0 || count < 1 || start > long.MaxValue - count))
                    Issue("组件", draft.InstanceId + "/request-id-count", "请求编号起点与数量合计超出协议整数范围。");
            }
        }
        if (Components.Any(c => c.Bound && ParameterOwnership.IsRcs(c)))
            foreach (var duplicate in Readers.Select(d => (Draft: d, Code: d.Parameters.FirstOrDefault(p => p.Field.Key == "bcr-code")?.Value))
                .Where(x => !string.IsNullOrWhiteSpace(x.Code)).GroupBy(x => x.Code, StringComparer.Ordinal).Where(g => g.Count() > 1).SelectMany(g => g))
                Issue("驱动", duplicate.Draft.InstanceId + "/bcr-code", "左右相机的心跳编码 必须独立，不能重复。");
        if (category is not null)
            foreach (var issue in Issues.Where(i => i.Category != ParameterOwnership.CategoryName(category.Value)).ToArray()) Issues.Remove(issue);
        ValidationChanged?.Invoke(this, EventArgs.Empty); return Issues.Count == 0;
    }
}
