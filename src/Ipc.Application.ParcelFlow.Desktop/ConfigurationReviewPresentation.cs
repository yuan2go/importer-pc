using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

public sealed record ConfigurationChangeItem(string Category, string Object, string Label, string Before, string After, string Path, string TechnicalPath)
{
    public string Location => Category + " / " + Object;
    public override string ToString() => Label + " · " + Location;
    public string Activation { get; init; } = "重启后生效";
}

// A review projection only. It neither persists a second draft nor replaces
// Stage/Inspect, the original revision, runtime identity or submission guards.
public partial class ParcelViewModel
{
    string? reviewedConfigurationSignature;
    ParcelConfiguration? configurationReviewBaseline;
    ConfigurationChangeItem[] reviewedConfigurationChanges = [];
    string configurationSubmissionOutcome = "", configurationSubmissionDetail = "";
    public bool HasUnconfirmedConfigurationSubmission => unconfirmedSubmission is not null;
    public string ConfigurationSubmissionOutcome => configurationSubmissionOutcome;
    public string ConfigurationSubmissionDetail => configurationSubmissionDetail;
    public bool ConfigurationReviewIsCurrent => reviewedConfigurationSignature is not null && reviewedConfigurationSignature == CurrentReviewSignature();
    public bool CanReviewConfiguration => !Busy && (HasUnconfirmedConfigurationSubmission || HasUnsavedConfiguration);
    public string ConfigurationSaveActionText => HasUnconfirmedConfigurationSubmission ? "查看保存结果…" : "保存设置…";
    public bool CanSubmitReviewedConfiguration => CanConfigure && ConfigurationReviewIsCurrent && reviewedConfigurationChanges.Length > 0 && !HasUnconfirmedConfigurationSubmission;
    public ConfigurationChangeItem[] ReviewedConfigurationChanges => reviewedConfigurationChanges;
    public string ConfigurationDraftStatusText => HasUnconfirmedConfigurationSubmission ? "保存结果未确认" : HasUnsavedConfiguration ? "有未保存的更改" : "没有未保存的更改";
    public string ConfigurationActivationGuidance => Snapshot?.ApplicationId == "sportmaster" ? "仅修改条码规则时可直接生效；连接等其他设置需重启，以保存结果为准。" : "保存后需重启软件生效；重启前继续使用当前设置。";
    public string ConfigurationSavedStatusText => HasUnconfirmedConfigurationSubmission ? "请先查看上次保存结果" : Snapshot?.Configuration.Failure is not null ? ConfigurationActivationOutcome :
        Snapshot?.Configuration.Pending is not null ? "已保存 · 重启软件后生效" : configurationSubmissionOutcome == "Effective" ? "条码规则已保存并生效" : ConfigurationActivationGuidance;
    string CurrentReviewSignature() => configurationAuthority + "\n" + (configurationAuthority == "高级JSON" ? ConfigurationDraft : ConfigurationEditSignature());
    ParcelConfiguration CurrentReviewCandidate() => configurationAuthority switch
    {
        "高级JSON" => Wire.Decode<ParcelConfiguration>(ConfigurationDraft),
        "结构化表单" => BuildStructuredConfiguration(),
        _ => throw new InvalidOperationException("先读取当前设置；输入保留。")
    };
    public async Task<bool> PrepareConfigurationReviewAsync()
    {
        reviewedConfigurationSignature = null; NotifyConfigurationReview();
        if (HasUnconfirmedConfigurationSubmission) { Message = "上次保存结果未确认，请先查看保存结果。已填写的内容会保留。"; return false; }
        if (configurationAuthority == "结构化表单" && ParameterEditor?.ValidateFields() == false)
        { validationState = "有设置需要修正"; NotifyConfigurationEditorState(); Message = "请修正标出的设置后再保存；全部输入已保留。"; return false; }
        ParcelConfiguration candidate;
        try { candidate = CurrentReviewCandidate(); }
        catch (Exception error) when (error is InvalidOperationException or JsonException or System.IO.InvalidDataException or ArgumentException)
        {
            validationState = "有设置需要修正";
            if (configurationAuthority == "结构化表单") ParameterEditor?.Report(error.Message);
            NotifyConfigurationEditorState(); Message = "设置检查未通过，请修正标出的参数；输入已保留。"; return false;
        }
        var signature = CurrentReviewSignature();
        var validation = await ServiceAsync(() => application.ValidateConfigurationAsync(candidate));
        if (signature != CurrentReviewSignature())
        { validationState = "检查期间输入已改变"; NotifyConfigurationEditorState(); Message = "输入已改变，本次检查已过期。请重新保存以检查当前更改。"; return false; }
        validationState = validation.Status + " · " + validation.Reason;
        if (validation.Status != "Validated")
        { if (configurationAuthority == "结构化表单") ParameterEditor?.Report(validation.Reason); NotifyConfigurationEditorState(); Message = "设置检查未通过，请查看参数错误与诊断详情；输入已保留。"; return false; }
        var live = Snapshot is { } snapshot && ConfigurationActivation.RulesOnly(snapshot.ApplicationId, snapshot.Configuration.Effective, snapshot.Configuration.Pending, candidate);
        reviewedConfigurationChanges = BuildConfigurationChangeItems(configurationReviewBaseline ?? structuredBase ?? Wire.Decode<ParcelConfiguration>(savedCandidate), candidate)
            .Select(c => c with { Activation = live ? "保存后立即生效" : "重启后生效" }).ToArray();
        if (reviewedConfigurationChanges.Length == 0)
        {
            validationState = "没有配置差异";
            NotifyConfigurationEditorState(); NotifyConfigurationReview(); Message = "没有需要保存的修改。"; return false;
        }
        reviewedConfigurationSignature = signature; configurationSubmissionOutcome = "";
        NotifyConfigurationEditorState(); NotifyConfigurationReview(); Message = "检查通过，请核对修改并填写原因。"; return true;
    }
    public ConfigurationChangeItem[] GetConfigurationChangeItems()
    {
        var candidate = CurrentReviewCandidate();
        return BuildConfigurationChangeItems(configurationReviewBaseline ?? structuredBase ?? Wire.Decode<ParcelConfiguration>(savedCandidate), candidate);
    }
    void NotifyConfigurationReview()
    {
        foreach (var name in new[] { nameof(ConfigurationReviewIsCurrent), nameof(CanSubmitReviewedConfiguration), nameof(ReviewedConfigurationChanges), nameof(HasUnconfirmedConfigurationSubmission), nameof(ConfigurationSubmissionOutcome), nameof(ConfigurationSubmissionDetail), nameof(ConfigurationDraftStatusText), nameof(ConfigurationSavedStatusText) }) Changed(name);
    }
    ConfigurationChangeItem[] BuildConfigurationChangeItems(ParcelConfiguration original, ParcelConfiguration candidate)
    {
        using var before = JsonDocument.Parse(Wire.Encode(original)); using var after = JsonDocument.Parse(Wire.Encode(candidate));
        List<ConfigurationChangeItem> changes = [];
        var drafts = ReaderDrafts.Select(x => x.Capability).Concat(CapabilityDrafts).DistinctBy(x => x.InstanceId).ToDictionary(x => x.InstanceId);
        // DeviceBinding uses DeviceId whereas the editor uses its capability instance.
        // Resolve both identities without exposing JSON property paths to operators.
        foreach (var reader in ReaderDrafts) drafts.TryAdd(reader.Original.DeviceId, reader.Capability);
        static string? Identity(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object) return null;
            foreach (var key in new[] { "InstanceId", "DeviceId", "Id" }) if (value.TryGetProperty(key, out var id) && id.ValueKind == JsonValueKind.String) return id.GetString();
            return null;
        }
        static JsonElement Property(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) ? property : default;
        static string Read(JsonElement value) => value.ValueKind switch { JsonValueKind.Undefined => "未设置", JsonValueKind.Null => "未设置", JsonValueKind.String => value.GetString() ?? "", _ => value.GetRawText() };
        void Add(JsonElement left, JsonElement right, string path, string? instance, string? parameter)
        {
            var category = "高级设置"; var owner = "全部设置"; var label = "其他配置项"; var fieldPath = ""; var showValues = false;
            var translateChoice = false;
            if (instance is not null && drafts.TryGetValue(instance, out var draft))
            {
                owner = draft.Kind == "observation" ? draft.Role switch { "left" => "左相机", "right" => "右相机", _ => draft.Role } : ParameterOwnership.IsRcs(draft) ? "RCS 服务" : ParameterEditor?.ComponentLabel(draft) ?? draft.InstanceId;
                category = ParameterOwnership.CategoryName(parameter is null ? ParameterOwnership.ObjectCategory(draft) : ParameterOwnership.Category(draft, parameter));
                if (parameter is not null && draft.Parameters.FirstOrDefault(x => x.Field.Key == parameter) is { } p)
                { label = ChineseInterfaceText.ParameterName(parameter, p.EditorLabel); fieldPath = draft.InstanceId + "/" + parameter; showValues = !p.IsSensitive; translateChoice = p.Field.Type is "choice" or "boolean"; }
                else if (path.EndsWith(".Target", StringComparison.Ordinal)) { label = "连接目标"; fieldPath = draft.InstanceId + "/Target"; showValues = true; }
                else label = path.Split('.').Last() switch
                {
                    "Enabled" => "启用状态", "Implementation" or "Driver" => "连接方式", "Version" => "组件版本",
                    "Contract" => "服务协议", "InstanceId" or "DeviceId" => "设备与组件标识", "Package" => "组件安装来源",
                    "Module" => "组件模块", _ => "其他配置项"
                };
                if (category == "系统") owner = "供包台标识";
                else if (category == "业务模块") owner = "格口请求";
                else if (parameter is "left-bcr-code" or "right-bcr-code") owner = parameter.StartsWith("left", StringComparison.Ordinal) ? "左相机" : "右相机";
            }
            else
            {
                var policy = path switch
                {
                    "StationCode" => ("系统", "供包台标识", "供包台编号", "StationCode"),
                    "QueueCapacity" => ("业务模块", "接收与等待限制", "扫码消息缓冲上限", "QueueCapacity"),
                    "TimeoutMs" => ("业务模块", "接收与等待限制", "外部服务等待时间（毫秒）", "TimeoutMs"),
                    "MaximumOperations" => ("业务模块", "接收与等待限制", "待处理作业上限", "MaximumOperations"),
                    "DeferredConnections" => ("系统", "连接配置状态", "稍后配置的连接", ""),
                    "SessionLifetimeMs" => ("业务模块", "作业登录", "操作员会话时长（毫秒）", "SessionLifetimeMs"),
                    "Login" => ("业务模块", "作业登录", "操作员登录绑定", "OperatorLogin"),
                    "Retention.DiagnosticLogDays" => ("系统", "数据与记录", "诊断日志保留天数", "DiagnosticLogDays"),
                    "Retention.HistoryDays" => ("系统", "数据与记录", "已结数据保留天数", "HistoryDays"),
                    "Retention.ResidentHistory" => ("系统", "数据与记录", "常驻记录条数", "ResidentHistory"),
                    "Retention.MinimumFreeBytes" => ("系统", "数据与记录", "磁盘保留空间（字节）", "MinimumFreeMiB"),
                    "BarcodeFilter.Pattern" => ("业务模块", "条码筛选", "条码筛选规则", "BarcodePattern"),
                    "BarcodeFilter" => ("业务模块", "条码筛选", "条码处理规则", "BarcodePattern"),
                    "BarcodeFilter.ExclusionPattern" => ("业务模块", "条码筛选", "条码排除规则", "BarcodeExclusionPattern"),
                    "BarcodeFilter.RejectMultipleCodes" => ("业务模块", "条码筛选", "拒绝多码", "RejectMultipleCodes"),
                    "BarcodeFilter.MultiCodeSeparator" => ("业务模块", "条码筛选", "多条码分隔符", "MultiCodeSeparator"),
                    "WeightRange.Minimum" => ("业务模块", "业务策略", "包裹重量下限", "MinimumWeight"),
                    "WeightRange.Maximum" => ("业务模块", "业务策略", "包裹重量上限", "MaximumWeight"),
                    "Weight.Source" => ("业务模块", "业务策略", "重量来源", "WeightSource"),
                    "Weight.Unit" => ("业务模块", "业务策略", "重量单位", "Unit"),
                    "Weight.Precision" => ("业务模块", "业务策略", "重量精度", "Precision"),
                    "Weight.ConfiguredValue" => ("业务模块", "业务策略", "配置重量", "ConfiguredWeight"),
                    "Weight.MaximumAgeMs" => ("业务模块", "业务策略", "测量新鲜度（ms）", "MaximumAgeMs"),
                    "Weight.WaitTimeoutMs" => ("业务模块", "业务策略", "本件等待期限（ms）", "WaitTimeoutMs"),
                    "WeightValidity" => ("业务模块", "业务策略", "重量有效性时机", "Validity"),
                    "BackupSchedule.Directory" => ("系统", "自动备份", "备份目录", "BackupDirectory"),
                    "BackupSchedule.IntervalMinutes" => ("系统", "自动备份", "备份间隔（分钟）", "BackupIntervalMinutes"),
                    "BackupSchedule.MaximumSets" => ("系统", "自动备份", "备份份数上限", "BackupMaximumSets"),
                    _ => ((string, string, string, string)?)null
                };
                if (policy is { } known) { (category, owner, label, fieldPath) = known; showValues = true; }
            }
            var oldValue = showValues ? Read(left) : left.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? "未设置" : "已设置";
            var newValue = showValues ? Read(right) : right.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? "已移除" : "已更改（内容不显示）";
            if (translateChoice)
            {
                oldValue = ChineseInterfaceText.Choice(oldValue);
                newValue = ChineseInterfaceText.Choice(newValue);
            }
            // The JSON candidate may intentionally differ from cached form drafts.
            // Keep its exact technical path; never jump to an older form value.
            if (!CanEditStructuredConfiguration) fieldPath = "";
            changes.Add(new(category, owner, label, oldValue, newValue, fieldPath, path));
        }
        void Compare(JsonElement left, JsonElement right, string path, string? instance = null, string? parameter = null)
        {
            if (path == "DeferredConnections") { if (Read(left) != Read(right)) Add(left, right, path, null, null); return; }
            if (left.ValueKind == right.ValueKind && (left.ValueKind == JsonValueKind.Undefined || left.GetRawText() == right.GetRawText())) return;
            if (left.ValueKind == JsonValueKind.Object || right.ValueKind == JsonValueKind.Object)
            {
                instance = Identity(right) ?? Identity(left) ?? instance;
                var keys = (left.ValueKind == JsonValueKind.Object ? left.EnumerateObject().Select(x => x.Name) : []).Union(right.ValueKind == JsonValueKind.Object ? right.EnumerateObject().Select(x => x.Name) : []);
                foreach (var key in keys) Compare(Property(left, key), Property(right, key), path.Length == 0 ? key : path + "." + key, instance, path.EndsWith(".Parameters", StringComparison.Ordinal) ? key : parameter);
            }
            else if (left.ValueKind == JsonValueKind.Array && right.ValueKind == JsonValueKind.Array)
            {
                var a = left.EnumerateArray().ToArray(); var b = right.EnumerateArray().ToArray();
                var aKeys = a.Select(Identity).ToArray(); var bKeys = b.Select(Identity).ToArray();
                if (aKeys.All(x => x is not null) && bKeys.All(x => x is not null) && aKeys.Distinct().Count() == a.Length && bKeys.Distinct().Count() == b.Length)
                {
                    var aa = a.ToDictionary(x => Identity(x)!); var bb = b.ToDictionary(x => Identity(x)!);
                    foreach (var key in aa.Keys.Union(bb.Keys)) Compare(aa.GetValueOrDefault(key), bb.GetValueOrDefault(key), path + "[" + key + "]", instance);
                    if (a.Length == b.Length && aKeys.Order().SequenceEqual(bKeys.Order()) && !aKeys.SequenceEqual(bKeys)) Add(left, right, path + "（顺序）", null, null);
                }
                else for (var i = 0; i < Math.Max(a.Length, b.Length); i++) Compare(a.ElementAtOrDefault(i), b.ElementAtOrDefault(i), path + "[" + i + "]", instance);
            }
            else Add(left, right, path, instance, parameter);
        }
        if (original.Login?.InstanceId != candidate.Login?.InstanceId)
            changes.Add(new("业务模块", "作业登录", "操作员登录服务", original.Login?.InstanceId ?? "不绑定", candidate.Login?.InstanceId ?? "不绑定", CanEditStructuredConfiguration ? "OperatorLogin" : "", "Login"));
        Compare(before.RootElement, after.RootElement, "");
        return changes.DistinctBy(x => (x.Path.Length == 0 ? x.TechnicalPath : x.Path, x.Before, x.After)).ToArray();
    }
}
