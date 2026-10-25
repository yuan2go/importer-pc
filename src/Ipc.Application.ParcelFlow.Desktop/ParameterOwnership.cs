using System.ComponentModel;
namespace Ipc.Application.ParcelFlow.Desktop;

// Display/edit ownership only. Storage continues to use the component's declared
// schema, including both legacy position keys. Frozen requests are never projected.
public static class ParameterOwnership
{
    public static bool IsRcs(CapabilityDraft draft) => draft.Kind == "integration" && draft.Contract == "SPORTMASTER-RCS-SORTER-v1";
    public static bool IsDriver(CapabilityDraft draft) => draft.Kind is "observation" or "weight" or "plc" or "camera";
    public static int ObjectCategory(CapabilityDraft draft) => IsDriver(draft) ? 2 : 3;
    public static int Category(CapabilityDraft draft, string key) => IsDriver(draft) ? 2 : !IsRcs(draft) ? 3 : key switch
    {
        "zone-id" or "project-id" or "left-station-code" or "right-station-code" => 0,
        "specified-chute" or "chute-code" => 1,
        "left-bcr-code" or "right-bcr-code" => 2,
        _ => 3
    };
    public static string CategoryName(int category) => new[] { "系统", "业务模块", "驱动", "组件" }[category];
    public static bool Applicable(CapabilityDraft draft, string key) => !IsRcs(draft) || key != "chute-code" ||
        draft.Parameters.FirstOrDefault(p => p.Field.Key == "specified-chute")?.Value != "false";
    public static bool StationConflict(CapabilityDraft draft) => IsRcs(draft) &&
        draft.Parameters.FirstOrDefault(p => p.Field.Key == "left-station-code")?.Value != draft.Parameters.FirstOrDefault(p => p.Field.Key == "right-station-code")?.Value;
}

// No second persisted draft: an intentional edit writes both original parameter
// drafts. A conflicting old pair starts blank, never chooses one side implicitly.
public sealed class CommonStationParameter : INotifyPropertyChanged, IDisposable
{
    readonly CapabilityParameter left, right;
    readonly ConfigurationPolicyDraft? policy;
    bool editing;
    public CommonStationParameter(CapabilityParameter left, CapabilityParameter right, ConfigurationPolicyDraft? policy = null)
    {
        this.left = left; this.right = right; this.policy = policy;
        OriginalValues = "读取时左取格口编号：" + left.Value + "；右取格口编号：" + right.Value + (policy is null ? "" : "；心跳编号：" + policy.StationCode);
        left.PropertyChanged += UnderlyingChanged; right.PropertyChanged += UnderlyingChanged;
        if (policy is not null) policy.PropertyChanged += UnderlyingChanged;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string OriginalValues { get; }
    void UnderlyingChanged(object? sender, PropertyChangedEventArgs e)
    { if (!editing && e.PropertyName is nameof(CapabilityParameter.Value) or nameof(ConfigurationPolicyDraft.StationCode)) PropertyChanged?.Invoke(this, new(nameof(Value))); }
    public void Dispose() { left.PropertyChanged -= UnderlyingChanged; right.PropertyChanged -= UnderlyingChanged; if (policy is not null) policy.PropertyChanged -= UnderlyingChanged; }
    public string Value
    {
        get => left.Value == right.Value && (policy is null || left.Value == policy.StationCode) ? left.Value : "";
        set
        {
            editing = true;
            try { left.Value = value; right.Value = value; if (policy is not null) policy.StationCode = value; }
            finally { editing = false; }
            PropertyChanged?.Invoke(this, new(nameof(Value)));
        }
    }
}
