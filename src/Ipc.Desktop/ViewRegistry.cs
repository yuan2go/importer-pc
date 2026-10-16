using System.IO;
namespace Ipc.Desktop;

/// <summary>Explicit installed presentation mapping. No assembly scanning or page-owned runtime.</summary>
public static class ViewRegistry
{
    public static readonly IReadOnlyDictionary<string, string> Pages = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["workstation"] = "WorkstationPage", ["history"] = "HistoryPage", ["recovery"] = "RecoveryPage", ["alarms"] = "RecoveryPage", ["diagnostics"] = "DiagnosticsPage",
        ["maintenance"] = "MaintenancePage", ["engineering"] = "EngineeringPage"
    };
    public static string Resolve(string view) => Pages.TryGetValue(view, out var page) ? page : throw new InvalidDataException("未安装的页面：" + view);
}
