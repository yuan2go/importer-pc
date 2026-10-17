namespace Ipc.Supply;

// Supply declares its own view identities. Product chooses the shipped set; Desktop maps controls.
public static class SupplyViews
{
    public const string Workstation = "workstation";
    public const string History = "history";
    public const string Alarms = "alarms";
    public const string Diagnostics = "diagnostics";
    public const string Maintenance = "maintenance";
    public const string Engineering = "engineering";
    public const string Extensions = "extensions";
    public const string Recovery = "recovery";
    public static IReadOnlyList<string> Installed { get; } = Array.AsReadOnly(new[] { Workstation, History, Alarms, Diagnostics, Maintenance, Engineering, Extensions });
}
