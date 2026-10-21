using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    string? editorConfigurationHash, savedEditorHash;
    StartupSavedDraft? availableEditorDraft;
    async Task ReadEditorBaseline(long revision)
    {
        if (application is not IParcelConfigurationDraftApplication access) return;
        editorConfigurationHash = await ServiceAsync(() => access.ConfigurationEditorHashAsync(revision));
        availableEditorDraft = await ServiceAsync(access.ReadEditorDraftAsync); savedEditorHash = availableEditorDraft?.DraftHash;
    }
    public async Task SaveIncompleteEditorAsync()
    {
        if (application is not IParcelConfigurationDraftApplication access || editorConfigurationHash is null || structuredBase is null || StructuredPolicy is null)
            throw new InvalidOperationException("先读取完整拓扑；旧版配置可使用高级JSON候选或独立首配草稿");
        Dictionary<string, string> values = new() { ["editor"] = "live-v1", ["base"] = Wire.Encode(structuredBase), ["authority"] = configurationAuthority, ["json"] = ConfigurationDraft, ["reason"] = ConfigurationReason };
        // Optional keys keep older encrypted drafts readable; retaining input never applies a preference.
        values["display.identity"] = displayIdentity ?? "";
        values["display.name"] = DisplayNameDraft; values["display.target"] = TargetEfficiencyDraft;
        values["capabilities"] = Wire.Encode(CapabilityDrafts.Select(d => d.CaptureRaw()).ToArray());
        values["readers"] = Wire.Encode(ReaderDrafts.Select(d => d.Capability.CaptureRaw()).ToArray());
        foreach (var p in typeof(ConfigurationPolicyDraft).GetProperties().Where(p => p.CanRead && p.CanWrite)) values["policy." + p.Name] = p.GetValue(StructuredPolicy)?.ToString() ?? "";
        foreach (var role in ConfigurationRoles) values["role." + role.Role] = role.Selection.Capability?.InstanceId ?? "";
        foreach (var position in PositionDrafts) { values["position." + position.Id + ".scale"] = position.Scale.Capability?.InstanceId ?? ""; values["position." + position.Id + ".camera"] = position.Camera.Capability?.InstanceId ?? ""; }
        availableEditorDraft = await ServiceAsync(() => access.SaveEditorDraftAsync(editorConfigurationHash, savedEditorHash, new(values)));
        savedEditorHash = availableEditorDraft.DraftHash;
        Message = "未完成设置已加密保存（含尚无效的输入）；不是Pending，不生效、不重连、不发请求。";
    }
    public async Task ContinueIncompleteEditorAsync()
    {
        if (application is not IParcelConfigurationDraftApplication access || installedCatalog is null) throw new InvalidOperationException("先读取配置和精确目录");
        var saved = await ServiceAsync(access.ReadEditorDraftAsync) ?? throw new InvalidOperationException("没有已保存草稿");
        savedEditorHash = saved.DraftHash;
        if (saved.BaseChanged || saved.BaseConfigurationHash != editorConfigurationHash) throw new InvalidOperationException("草稿原基线/来源已变化；保留当前输入和加密草稿，先核对原版本，不覆盖新账");
        var v = saved.Editor.Values;
        if (v.GetValueOrDefault("editor") != "live-v1") throw new InvalidOperationException("这是独立首配草稿；从启动修复入口继续，当前设置输入保留");
        if (v.TryGetValue("display.identity", out var identity) && identity != displayIdentity)
            throw new InvalidOperationException("显示草稿站点身份已变化；原草稿和当前输入保留，不移用到另一站点");
        // Reconstruct and validate in a temporary model before replacing this editor.
        var temporary = new ParcelViewModel(application, Title, Flow, PresentationId) { installedCatalog = installedCatalog };
        temporary.LoadStructuredConfiguration(Wire.Decode<ParcelConfiguration>(v["base"]), installedCatalog);
        var originalReaders = temporary.ReaderDrafts.Select(r => r.Original).ToArray();
        var savedReaders = Wire.Decode<CapabilityDraft.Saved[]>(v["readers"]);
        if (originalReaders.Length != savedReaders.Length) throw new InvalidOperationException("读码拓扑结构已变化；保留原草稿");
        temporary.ReaderDrafts.Clear();
        for (int n = 0; n < originalReaders.Length; n++) temporary.ReaderDrafts.Add(new(originalReaders[n], CapabilityDraft.RestoreRaw(savedReaders[n], installedCatalog.Providers)));
        temporary.CapabilityDrafts.Clear(); foreach (var draft in Wire.Decode<CapabilityDraft.Saved[]>(v["capabilities"])) temporary.CapabilityDrafts.Add(CapabilityDraft.RestoreRaw(draft, installedCatalog.Providers));
        foreach (var p in typeof(ConfigurationPolicyDraft).GetProperties().Where(p => p.CanRead && p.CanWrite))
            if (v.TryGetValue("policy." + p.Name, out var value)) p.SetValue(temporary.StructuredPolicy, p.PropertyType.IsEnum ? Enum.Parse(p.PropertyType, value) : p.PropertyType == typeof(bool) ? bool.Parse(value) : value);
        CapabilityChoice[] Choices(string kind) => [new("不绑定/沿用默认", null), .. temporary.CapabilityDrafts.Where(c => c.Kind == kind).Select(c => new CapabilityChoice(c.InstanceId, c))];
        var roles = temporary.ConfigurationRoles.ToArray(); temporary.ConfigurationRoles.Clear();
        foreach (var role in roles) { var choices = Choices(role.Kind); temporary.ConfigurationRoles.Add(new(role.Role, role.Kind, choices, choices.Single(c => (c.Capability?.InstanceId ?? "") == v["role." + role.Role]), role.Visibility == System.Windows.Visibility.Visible)); }
        var positions = temporary.PositionDrafts.ToArray(); temporary.PositionDrafts.Clear();
        foreach (var p in positions) { var restored = new PositionConfigurationDraft(p.Original, Choices("weight"), Choices("camera")); restored.Scale = restored.Scales.Single(c => (c.Capability?.InstanceId ?? "") == v["position." + p.Id + ".scale"]); restored.Camera = restored.Cameras.Single(c => (c.Capability?.InstanceId ?? "") == v["position." + p.Id + ".camera"]); temporary.PositionDrafts.Add(restored); }
        structuredBase = temporary.structuredBase; StructuredPolicy = temporary.StructuredPolicy;
        ReaderDrafts.Clear(); foreach (var r in temporary.ReaderDrafts) ReaderDrafts.Add(r);
        CapabilityDrafts.Clear(); foreach (var c in temporary.CapabilityDrafts) CapabilityDrafts.Add(c);
        ConfigurationRoles.Clear(); foreach (var r in temporary.ConfigurationRoles) ConfigurationRoles.Add(r);
        PositionDrafts.Clear(); foreach (var p in temporary.PositionDrafts) PositionDrafts.Add(p);
        StructuredPolicy!.PropertyChanged += (_, _) => EditorChanged();
        foreach (var c in CapabilityDrafts.Concat(ReaderDrafts.Select(r => r.Capability))) c.PropertyChanged += (_, _) => EditorChanged();
        foreach (var r in ConfigurationRoles) r.PropertyChanged += (_, _) => EditorChanged(); foreach (var p in PositionDrafts) p.PropertyChanged += (_, _) => EditorChanged();
        configurationAuthority = v["authority"]; ConfigurationDraft = v["json"]; ConfigurationReason = v["reason"];
        if (v.TryGetValue("display.name", out var name)) DisplayNameDraft = name;
        if (v.TryGetValue("display.target", out var target)) TargetEfficiencyDraft = target;
        RebuildParameterEditor(); NotifyConfigurationEditorState(); Message = "已继续加密草稿；当前编辑来源和无效输入保留。校验后才能保存Pending。";
    }
}
