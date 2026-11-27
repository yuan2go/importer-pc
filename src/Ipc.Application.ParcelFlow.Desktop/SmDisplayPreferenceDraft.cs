using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed record PreferenceSaveItem(string Path, string Name, string State, string Detail, string? Diagnostic = null);

// Per-user display choices and the installed workstation startup shortcut.
public sealed class SmDisplayPreferenceDraft : INotifyPropertyChanged
{
    PresentationPreferences.WindowOpeningMode? savedMode;
    bool? savedPace;
    int? savedVolume;
    PresentationPreferences.WindowOpeningMode mode;
    bool pace = true, autoStart;
    bool? savedAutoStart;
    int volume;
    readonly HashSet<string> uncertain = [];
    readonly Dictionary<string, string> readFailures = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    public SmDisplayPreferenceDraft()
    {
        Read("WindowOpeningMode", () => savedMode = mode = PresentationPreferences.ReadWindowMode("sportmaster"));
        Read("ShowPace", () => savedPace = pace = PresentationPreferences.Read("sportmaster", "pace") ?? true);
        Read("CueVolume", () => savedVolume = volume = PresentationPreferences.ReadCueVolume("sportmaster"));
        try { savedAutoStart = autoStart = ReadAutoStart(); }
        catch (Exception error) when (StorageError(error))
        {
            readFailures["AutoStart"] = error is InvalidOperationException ? error.Message :
                "无法读取当前用户的启动快捷方式。原文件保留，请检查启动目录和访问权限。";
        }
    }
    static bool ReadAutoStart() => OperatingSystem.IsWindows() ? DeploymentShortcuts.ReadCurrentUserAutoStart() :
        throw new InvalidOperationException("开机自启只适用于已安装的 Windows 供包台。");
    static void WriteAutoStart(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("开机自启只适用于已安装的 Windows 供包台。");
        DeploymentShortcuts.SaveCurrentUserAutoStart(enabled);
    }
    void Read(string path, Action read)
    {
        try { read(); }
        catch (Exception e) when (StorageError(e)) { readFailures[path] = "当前值未能读取，原文件保留；请检查本机偏好文件与访问权限后重新打开。诊断：" + DiagnosticText.Protect(e.Message); }
    }
    static bool StorageError(Exception e) => e is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException;
    public bool CanEdit(string path) => !readFailures.ContainsKey(path);
    public string ReadFailure(string path) => readFailures.GetValueOrDefault(path, "");
    public PresentationPreferences.WindowOpeningMode OpeningMode { get => mode; set { if (mode == value) return; mode = value; Changed(); } }
    public bool ShowPace { get => pace; set { if (pace == value) return; pace = value; Changed(); } }
    public int CueVolume { get => volume; set { if (volume == value) return; volume = value; Changed(); } }
    public bool AutoStart { get => autoStart; set { if (autoStart == value || !CanEdit("AutoStart")) return; autoStart = value; Changed(); } }
    bool AutoStartChanged => savedAutoStart.HasValue && autoStart != savedAutoStart || uncertain.Contains("AutoStart");
    public bool WindowChanged => savedMode.HasValue && mode != savedMode || uncertain.Contains("WindowOpeningMode");
    public bool HasChanges => WindowChanged || savedPace.HasValue && pace != savedPace || savedVolume.HasValue && volume != savedVolume || AutoStartChanged || uncertain.Count != 0;
    public string Signature => $"{mode}/{pace}/{volume}/{autoStart}";
    static string UserIdentity
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return Environment.UserName;
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return identity.User!.Value;
        }
    }
    public void Capture(IDictionary<string, string> values)
    {
        values["preference.user"] = UserIdentity;
        values["preference.mode"] = mode.ToString(); values["preference.pace"] = pace.ToString(); values["preference.volume"] = volume.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (savedAutoStart.HasValue) values["preference.autostart"] = autoStart.ToString();
    }
    public void Restore(IReadOnlyDictionary<string, string> values)
    {
        if (values.GetValueOrDefault("preference.user") != UserIdentity)
            throw new System.IO.InvalidDataException("偏好草稿缺少当前Windows用户身份或属于另一用户；原草稿与当前输入保留。");
        var restoredMode = Enum.Parse<PresentationPreferences.WindowOpeningMode>(values["preference.mode"]);
        var restoredPace = bool.Parse(values["preference.pace"]);
        var restoredVolume = int.Parse(values["preference.volume"], System.Globalization.CultureInfo.InvariantCulture);
        if ((!CanEdit("WindowOpeningMode") && restoredMode != mode) || (!CanEdit("ShowPace") && restoredPace != pace) || (!CanEdit("CueVolume") && restoredVolume != volume))
            throw new System.IO.InvalidDataException("原偏好文件读取失败；请先修复访问或文件问题，草稿与原件保留。");
        if (values.TryGetValue("preference.autostart", out var startup) && !CanEdit("AutoStart") && bool.Parse(startup) != autoStart)
            throw new InvalidOperationException("当前无法修改开机自启。请从已安装的供包台打开设置。");
        OpeningMode = restoredMode; ShowPace = restoredPace; CueVolume = restoredVolume;
        if (startup is not null) AutoStart = bool.Parse(startup);
    }
    public PreferenceSaveItem[] LastSave { get; private set; } = [];
    public bool LastSaveComplete => LastSave.All(x => x.State is "Saved" or "Unchanged");
    public string SaveSummary => string.Join("；", LastSave.Select(x => x.Name + "：" + x.Detail));
    public string? Validate() => !Enum.IsDefined(mode) ? "请选择默认打开方式。" : volume is < 0 or > 100 ? "提示音音量须为0～100。" : null;
    public ConfigurationChangeItem[] Changes()
    {
        List<ConfigurationChangeItem> changes = [];
        void Add(string path, string name, string before, string after) => changes.Add(new("系统", "显示与声音", name, before, after, path, "") { Activation = "按当前Windows用户保存后立即生效" });
        static string Mode(PresentationPreferences.WindowOpeningMode value) => value == PresentationPreferences.WindowOpeningMode.FullScreen ? "最大化（全屏）" : "窗口";
        if (WindowChanged) Add("WindowOpeningMode", "默认打开方式", savedMode is { } before ? Mode(before) : "未核对", Mode(mode));
        if (savedPace.HasValue && pace != savedPace || uncertain.Contains("ShowPace")) Add("ShowPace", "效率显示", savedPace == true ? "显示" : "隐藏", pace ? "显示" : "隐藏");
        if (savedVolume.HasValue && volume != savedVolume || uncertain.Contains("CueVolume")) Add("CueVolume", "提示音音量", savedVolume + "%", volume + "%");
        if (AutoStartChanged) Add("AutoStart", "开机自动启动供包台", savedAutoStart == true ? "开启" : "关闭", autoStart ? "开启" : "关闭");
        return changes.ToArray();
    }
    public PreferenceSaveItem[] Save()
    {
        if (Validate() is { } error) throw new InvalidOperationException(error);
        // Freeze this attempt. Notification callbacks cannot silently adopt newer edits.
        var selectedMode = mode; var selectedPace = pace; var selectedVolume = volume; var selectedAutoStart = autoStart;
        List<PreferenceSaveItem> results = [];
        SaveOne("WindowOpeningMode", "打开方式", selectedMode, savedMode, PresentationPreferences.ReadWindowMode,
            PresentationPreferences.SaveWindowMode, value => savedMode = value);
        SaveOne("ShowPace", "效率显示", selectedPace, savedPace, app => PresentationPreferences.Read(app, "pace") ?? true,
            (app, value) => PresentationPreferences.Save(app, "pace", value), value => savedPace = value);
        SaveOne("CueVolume", "提示音", selectedVolume, savedVolume, PresentationPreferences.ReadCueVolume,
            PresentationPreferences.SaveCueVolume, value => savedVolume = value);
        SaveOne("AutoStart", "开机自启", selectedAutoStart, savedAutoStart, _ => ReadAutoStart(),
            (_, value) => WriteAutoStart(value), value => savedAutoStart = value);
        LastSave = results.ToArray(); Changed(nameof(LastSave)); return LastSave;

        void SaveOne<T>(string path, string name, T target, T? original, Func<string, T> read, Action<string, T> write, Action<T> accepted) where T : struct
        {
            if (original is null || EqualityComparer<T>.Default.Equals(target, original.Value) && !uncertain.Contains(path)) return;
            try
            {
                if (!uncertain.Contains(path) || !EqualityComparer<T>.Default.Equals(read("sportmaster"), target)) write("sportmaster", target);
                else if (path != "AutoStart") PresentationPreferences.RefreshSaved("sportmaster", path switch { "WindowOpeningMode" => "window", "ShowPace" => "pace", _ => "audio" });
                accepted(target); uncertain.Remove(path); results.Add(new(path, name, "Saved", "已保存并生效"));
            }
            catch (Exception writeError) when (StorageError(writeError))
            {
                // An atomic write or its UI notification may have committed before
                // throwing. Inspect this exact target before declaring failure/retrying.
                try
                {
                    var actual = read("sportmaster");
                    if (EqualityComparer<T>.Default.Equals(actual, target))
                    {
                        accepted(target); uncertain.Remove(path);
                        results.Add(new(path, name, "Saved", "已核对保存；界面刷新未完成，请重新打开核对", DiagnosticText.Protect(writeError.ToString())));
                    }
                    else
                    {
                        uncertain.Remove(path);
                        results.Add(new(path, name, "NotSaved", "未保存，草稿保留；检查本机偏好文件与权限后重试", DiagnosticText.Protect(writeError.ToString())));
                    }
                }
                catch (Exception inspectError) when (StorageError(inspectError))
                {
                    uncertain.Add(path);
                    results.Add(new(path, name, "Unknown", "保存结果未确认，草稿保留；重试先核对原值", DiagnosticText.Protect(new AggregateException(writeError, inspectError).ToString())));
                }
            }
        }
    }
    void Changed([CallerMemberName] string name = "")
    {
        PropertyChanged?.Invoke(this, new(name)); PropertyChanged?.Invoke(this, new(nameof(HasChanges)));
        PropertyChanged?.Invoke(this, new(nameof(WindowChanged))); PropertyChanged?.Invoke(this, new(nameof(SaveSummary)));
    }
}
