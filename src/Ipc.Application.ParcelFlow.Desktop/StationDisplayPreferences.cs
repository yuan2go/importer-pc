using System.Globalization;
using System.IO;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    string? displayIdentity;
    string displayNameDraft = "", targetEfficiencyDraft = "", displayStatus = "等待站点配置";
    PresentationPreferences.StationDisplay stationDisplay = new(1, "", null);
    public string DisplayNameDraft { get => displayNameDraft; set { displayNameDraft = value; DisplayPreferenceStatus = "本机显示有未应用输入"; Changed(); Changed(nameof(HasUnsavedDisplayPreferences)); } }
    public string TargetEfficiencyDraft { get => targetEfficiencyDraft; set { targetEfficiencyDraft = value; DisplayPreferenceStatus = "本机显示有未应用输入"; Changed(); Changed(nameof(HasUnsavedDisplayPreferences)); } }
    public bool HasUnsavedDisplayPreferences => displayNameDraft != stationDisplay.Name ||
        targetEfficiencyDraft != (stationDisplay.TargetPerHour?.ToString(CultureInfo.InvariantCulture) ?? "");
    public string DisplayPreferenceStatus { get => displayStatus; private set { displayStatus = value; Changed(); } }
    public string StationDisplaySummary => (stationDisplay.Name.Length > 0 ? stationDisplay.Name + " · " : "") +
        (Snapshot?.Configuration.Effective.StationId ?? "等待站位配置") +
        (stationDisplay.TargetPerHour is { } target ? " · 目标 " + target.ToString(CultureInfo.InvariantCulture) + " 件/小时" : "");
    void LoadStationDisplay()
    {
        if (Snapshot?.Configuration.Effective is not { } c) return;
        var key = c.ApplicationId + "\n" + c.StationId;
        if (displayIdentity == key) return; // Snapshot refresh never replaces active input.
        displayIdentity = key;
        try
        {
            stationDisplay = PresentationPreferences.ReadStation(c.ApplicationId, c.StationId);
            displayNameDraft = stationDisplay.Name; targetEfficiencyDraft = stationDisplay.TargetPerHour?.ToString(CultureInfo.InvariantCulture) ?? "";
            DisplayPreferenceStatus = "本机显示已读取";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { stationDisplay = new(1, "", null); DisplayPreferenceStatus = "显示偏好未加载，原文件保留：" + ex.Message; }
        Changed(nameof(DisplayNameDraft)); Changed(nameof(TargetEfficiencyDraft)); Changed(nameof(StationDisplaySummary)); Changed(nameof(Title)); Changed(nameof(HasUnsavedDisplayPreferences));
    }
    public bool ApplyStationDisplay()
    {
        if (!IsAdministrator) { DisplayPreferenceStatus = "请先登录软件管理员；输入保留。"; return false; }
        if (Snapshot?.Configuration.Effective is not { } c) { DisplayPreferenceStatus = "尚无站点身份，输入保留"; return false; }
        try
        {
            int? target = targetEfficiencyDraft.Length == 0 ? null : ConfigurationPolicyDraft.Integer(targetEfficiencyDraft, "目标效率（件/小时）", 1, 100000);
            var next = new PresentationPreferences.StationDisplay(1, displayNameDraft, target);
            PresentationPreferences.SaveStation(c.ApplicationId, c.StationId, next); stationDisplay = next;
            targetEfficiencyDraft = target?.ToString(CultureInfo.InvariantCulture) ?? "";
            DisplayPreferenceStatus = "本机显示已应用；运行配置修订未改变";
            Changed(nameof(TargetEfficiencyDraft)); Changed(nameof(HasUnsavedDisplayPreferences)); Changed(nameof(StationDisplaySummary)); Changed(nameof(Title)); return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        { DisplayPreferenceStatus = "显示未应用，输入保留：" + ex.Message; return false; }
    }
}
