namespace Ipc.Application.ParcelFlow.Desktop;

// Display/edit ownership only. Storage continues to use the component's declared
// schema. Each position has one camera code shared by requests and heartbeat.
// Frozen requests are never projected.
public static class ParameterOwnership
{
    public static bool IsRcs(CapabilityDraft draft) => draft.Kind == "integration" && draft.Contract == "SPORTMASTER-RCS-SORTER-v1";
    public static bool IsDriver(CapabilityDraft draft) => draft.Kind is "observation" or "weight" or "plc" or "camera";
    public static int ObjectCategory(CapabilityDraft draft) => IsDriver(draft) ? 2 : 3;
    public static int Category(CapabilityDraft draft, string key) => IsDriver(draft) ? 2 : !IsRcs(draft) ? 3 : key switch
    {
        "zone-id" or "project-id" => 0,
        "left-bcr-code" or "right-bcr-code" => 2,
        _ => 3
    };
    public static string CategoryName(int category) => new[] { "系统", "业务模块", "驱动", "组件" }[category];
}
