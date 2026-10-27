using System.Collections.ObjectModel;
using System.ComponentModel;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

public sealed record ParameterIssue(string Category, string Path, string Message, string? Rule = null);
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
        { "operator-login" => "操作员登录", "default-scale" => "默认称重", _ => (component.Kind == "login" ? "操作员登录（未绑定）" : ChineseInterfaceText.CapabilityKind(component.Kind)) + (Components.Count(x => x.Kind == component.Kind) > 1 ? "（" + (Array.IndexOf(Components, component) + 1) + "）" : "") };
    }
    public string ComponentName(CapabilityDraft component)
    {
        var name = ParameterOwnership.IsRcs(component) ? "RCS 服务" : component.Provider?.Implementation switch
        {
            "reference-integration" => "RCS 服务", "reference-login" => "本机模拟认证", "reference-weight" => "模拟称重",
            _ => ChineseInterfaceText.CapabilityKind(component.Kind)
        };
        var peers = Components.Where(c => c.Kind == component.Kind).ToArray();
        return peers.Length > 1 ? name + "（" + (Array.IndexOf(peers, component) + 1) + "）" : name;
    }
    public ObservableCollection<ParameterIssue> Issues { get; } = [];
    public Func<BarcodeFilterPolicy, string[], Task<BarcodeFilterResult>> Preview { get; } = preview;
    public event EventHandler? ValidationChanged;
    bool validationRequested;
    int? validationScope;
    IReadOnlySet<string>? validationPaths;
    public void RevalidateAfterEdit()
    {
        if (validationRequested) ValidateFields(validationScope, validationPaths);
    }
    static string WeightRangeField(string message) => message.Contains("WeightRange.MinimumWeight", StringComparison.Ordinal) ? "MinimumWeight" : "MaximumWeight";
    public void Report(string message, bool preserveScope = false)
    {
        validationRequested = true;
        if (!preserveScope) { validationScope = null; validationPaths = null; }
        if (message.Contains("BarcodeFilter.ExclusionPattern", StringComparison.Ordinal)) Issues.Add(new("业务模块", "BarcodeExclusionPattern", message));
        else if (message.Contains("BarcodeFilter.Pattern", StringComparison.Ordinal)) Issues.Add(new("业务模块", "BarcodePattern", message));
        else if (message.Contains("DiagnosticLogDays", StringComparison.Ordinal)) Issues.Add(new("系统", "DiagnosticLogDays", message));
        else if (message.Contains("HistoryDays", StringComparison.Ordinal)) Issues.Add(new("系统", "HistoryDays", message));
        else if (message.Contains("WeightRange", StringComparison.Ordinal)) Issues.Add(new("业务模块", WeightRangeField(message), message));
        else if (message.Contains("StationCode", StringComparison.Ordinal) || message.Contains("station code", StringComparison.OrdinalIgnoreCase)) Issues.Add(new("系统", "StationCode", message));
        else
        {
            var matching = Readers.Concat(Components).SelectMany(d => d.Parameters.Select(p => (Draft: d, Field: p.Field))).Where(x => message.Contains(x.Field.Key, StringComparison.Ordinal)).ToArray();
            var specific = matching.Where(x => message.StartsWith(x.Draft.Role + " · ", StringComparison.Ordinal) ||
                message.StartsWith(x.Draft.InstanceId + ":", StringComparison.Ordinal) || message.StartsWith(x.Draft.InstanceId + "/", StringComparison.Ordinal)).ToArray();
            if (specific.Length > 0) matching = specific;
            // An unqualified shared field name cannot identify the left or right
            // instance. Keep it in full-candidate diagnostics rather than falsely
            // attaching the same failure to every camera.
            if (matching.Length != 1) Issues.Add(new("完整候选", "", message));
            else foreach (var item in matching) Issues.Add(new(ParameterOwnership.CategoryName(ParameterOwnership.Category(item.Draft, item.Field.Key)), item.Draft.InstanceId + "/" + item.Field.Key, message));
        }
        Deduplicate();
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }
    public bool ValidateFields(int? category = null, IReadOnlySet<string>? paths = null)
    {
        validationRequested = true; validationScope = category; validationPaths = paths;
        Issues.Clear();
        void Issue(string category, string key, string message) => Issues.Add(new(category, key, message));
        void Number(string name, string value, int min, int max)
        { try { _ = ConfigurationPolicyDraft.Integer(value, name, min, max); } catch (InvalidOperationException ex) { Issue(name is "QueueCapacity" or "TimeoutMs" or "MaximumOperations" or "SessionLifetimeMs" ? "业务模块" : "系统", name, ex.Message); } }
        Number(nameof(Policy.QueueCapacity), Policy.QueueCapacity, 1, 4096); Number(nameof(Policy.TimeoutMs), Policy.TimeoutMs, 100, 30000);
        Number(nameof(Policy.MaximumOperations), Policy.MaximumOperations, 1, 100000); Number(nameof(Policy.ResidentHistory), Policy.ResidentHistory, 1, 10000);
        Number(nameof(Policy.SessionLifetimeMs), Policy.SessionLifetimeMs, 100, 86400000);
        if (Policy.DiagnosticLogDays.Length > 0) Number(nameof(Policy.DiagnosticLogDays), Policy.DiagnosticLogDays, 1, 3650);
        if (Policy.HistoryDays.Length > 0) Number(nameof(Policy.HistoryDays), Policy.HistoryDays, 7, 30);
        if (Policy.WeightRangeEnabled)
            try { _ = Policy.BuildWeightRange(); } catch (InvalidOperationException ex) { Issue("业务模块", WeightRangeField(ex.Message), ex.Message); }
        if (!decimal.TryParse(Policy.MinimumFreeMiB, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var reserve) || reserve < 1 || reserve > 1048576)
            Issue("系统", nameof(Policy.MinimumFreeMiB), "磁盘保留须为1～1048576 MiB（二进制兆字节）");
        if (Policy.StationCode.Length > 256 || Policy.StationCode.Any(char.IsControl) || string.IsNullOrWhiteSpace(Policy.StationCode))
            Issue("系统", nameof(Policy.StationCode), "供包台编号须为1～256个非控制字符");
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
        foreach (var draft in Readers.Concat(Components.Where(x => x.Bound || x.Enabled)))
        {
            var fieldCategory = ParameterOwnership.CategoryName(ParameterOwnership.ObjectCategory(draft));
            if (draft.ProviderChangePending) Issue(fieldCategory, draft.InstanceId + "/Provider", "请确认切换并重置参数，或取消切换；当前输入保留。");
            if (draft.Provider is null) { Issue(fieldCategory, draft.InstanceId, draft.ProviderIdentity); continue; }
            if (draft.ConfigureLater) continue;
            var values = draft.Parameters.Where(p => p.Value.Length > 0 && ParameterOwnership.Applicable(draft, p.Field.Key)).ToDictionary(p => p.Field.Key, p => p.Value);
            foreach (var error in ExtensionSchema.Validate(draft.Provider.Parameters, values))
                Issue(ParameterOwnership.CategoryName(ParameterOwnership.Category(draft, error.Split(':')[0])), draft.InstanceId + "/" + error.Split(':')[0], draft.Role + " · " + error);
            // Mirror the installed Hikrobot driver's existing address-format rule
            // before review. This is local feedback, not a connection probe or a
            // replacement for Application/driver validation at the real boundary.
            if (draft.Provider.Implementation is "extension:hikrobot.idmvs-reader/idmvs-reader" or "extension:hikrobot.mvs-acquisition/mvs-acquisition" or "hikrobot-mvs" or "tcp-line" && values.TryGetValue("device.ip", out var ip))
            {
                if (!System.Net.IPAddress.TryParse(ip, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || address.ToString() != ip)
                    Issues.Add(new("驱动", draft.InstanceId + "/device.ip", "请填写完整的 IPv4 地址，使用四段 0～255 的数字。", "ipv4-format"));
                else if (ip is "0.0.0.0" or "255.255.255.255")
                    Issues.Add(new("驱动", draft.InstanceId + "/device.ip", "此地址不能用于相机连接，请填写具体相机地址。", "ipv4-address"));
            }
            if (ParameterOwnership.StationConflict(draft))
                Issue("系统", draft.InstanceId + "/left-station-code", "供包台编号不一致；请核对原值后填写本站共同使用的编号。");
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
        foreach (var rcs in Components.Where(ParameterOwnership.IsRcs))
            if (rcs.Parameters.Where(p => p.Field.Key is "left-station-code" or "right-station-code").Any(p => p.Value != Policy.StationCode))
                Issue("系统", "StationCode", "取格口和心跳的供包台编号尚未统一；请核对原值后填写同一个供包台编号。");
        foreach (var rcs in Components.Where(c => c.Bound && !c.ConfigureLater && ParameterOwnership.IsRcs(c)))
        {
            var codes = Readers.Where(r => !r.ConfigureLater).Select(r =>
                (Path: rcs.InstanceId + "/" + r.Role + "-bcr-code", Code: rcs.Parameters.SingleOrDefault(p => p.Field.Key == r.Role + "-bcr-code")?.Value)).ToArray();
            foreach (var item in codes.Where(c => string.IsNullOrWhiteSpace(c.Code)))
                Issue("驱动", item.Path, "请填写相机编码；本相机取格口与心跳共用这一编码。");
            foreach (var duplicate in codes.Where(c => !string.IsNullOrWhiteSpace(c.Code)).GroupBy(c => c.Code, StringComparer.Ordinal).Where(g => g.Count() > 1).SelectMany(g => g))
                Issue("驱动", duplicate.Path, "左右相机编码不能重复；取格口与心跳共用各自的编码。");
        }
        if (category is not null)
            foreach (var issue in Issues.Where(i => i.Category != ParameterOwnership.CategoryName(category.Value)).ToArray()) Issues.Remove(issue);
        if (paths is not null)
            foreach (var issue in Issues.Where(i => !paths.Contains(i.Path)).ToArray()) Issues.Remove(issue);
        Deduplicate();
        ValidationChanged?.Invoke(this, EventArgs.Empty); return Issues.Count == 0;
    }
    public async Task<bool> ValidatePageAsync(IReadOnlySet<string> paths)
    {
        var valid = ValidateFields(paths: paths);
        if (!paths.Contains("BarcodePattern") && !paths.Contains("BarcodeExclusionPattern")) return valid;
        var pattern = Policy.BarcodePattern; var exclusion = Policy.BarcodeExclusionPattern;
        if (string.IsNullOrWhiteSpace(pattern) && string.IsNullOrWhiteSpace(exclusion)) return valid;
        var result = await Task.Run(() => Preview(new(pattern, exclusion.Length == 0 ? null : exclusion), ["__format_check__"]));
        if (pattern != Policy.BarcodePattern || exclusion != Policy.BarcodeExclusionPattern) return false;
        if (result.Outcome is "InvalidPattern" or "FilterTimeout") { Report(result.Detail, preserveScope: true); return false; }
        return valid;
    }
    public void ReportField(string category, string path, string message)
    {
        Issues.Add(new(category, path, message)); Deduplicate(); ValidationChanged?.Invoke(this, EventArgs.Empty);
    }
    void Deduplicate()
    {
        var identities = new HashSet<(string Category, string Path, string Rule)>();
        foreach (var issue in Issues.ToArray())
        {
            var rule = issue.Rule ?? (issue.Message.EndsWith(": required", StringComparison.Ordinal) ? "required" :
                issue.Message.Contains(": invalid value", StringComparison.Ordinal) ? "schema-value" : issue.Message);
            if (!identities.Add((issue.Category, issue.Path, rule))) Issues.Remove(issue);
        }
    }
}
