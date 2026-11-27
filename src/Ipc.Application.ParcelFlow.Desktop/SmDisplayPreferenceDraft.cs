using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ipc.Platform;
using Ipc.Presentation.Wpf;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed record PreferenceSaveItem(string Path, string Name, string State, string Detail, string? Diagnostic = null);

// Per-user display choices and the installed workstation startup shortcut.
public sealed class SmDisplayPreferenceDraft : INotifyPropertyChanged
{
    readonly string applicationId;
    readonly BusinessDesktopProfile profile;
    string? savedThemeId;
    string themeId;
    string? savedLanguage;
    string language = PresentationPreferences.DefaultLanguage;
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
    public SmDisplayPreferenceDraft() : this(BusinessDesktopCatalog.Station.ApplicationId) { }

    public SmDisplayPreferenceDraft(string applicationId)
    {
        this.applicationId = applicationId;
        profile = BusinessDesktopCatalog.For(applicationId);
        themeId = profile.DefaultThemeId;
        Read(nameof(ThemeId), () =>
        {
            var saved = ReadTheme(applicationId);
            savedThemeId = themeId = WorkstationThemeManager.Resolve(saved, profile.SupportedThemes, profile.DefaultThemeId).Id;
            if (saved != themeId) ThemeStatus = "已保存的主题不可用，当前使用默认主题。";
        });
        Read(nameof(Language), () => savedLanguage = language = PresentationPreferences.ReadLanguage(applicationId));
        if (!profile.UsesStationDisplayPreferences) return;

        Read("WindowOpeningMode", () => savedMode = mode = PresentationPreferences.ReadWindowMode(applicationId));
        Read("ShowPace", () => savedPace = pace = PresentationPreferences.Read(applicationId, "pace") ?? true);
        Read("CueVolume", () => savedVolume = volume = PresentationPreferences.ReadCueVolume(applicationId));
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
    public string ThemeId { get => themeId; set { if (themeId == value || !CanEdit(nameof(ThemeId))) return; themeId = value; Changed(); } }
    public string ThemeStatus { get; private set; } = "";
    public string Language { get => language; set { if (language == value || !CanEdit(nameof(Language))) return; language = value; Changed(); } }
    public bool LanguageChanged => savedLanguage is not null && language != savedLanguage || uncertain.Contains(nameof(Language));
    public bool ThemeChanged => savedThemeId is not null && themeId != savedThemeId || uncertain.Contains(nameof(ThemeId));
    string ReadTheme(string application) => PresentationPreferences.ReadTheme(application, profile.DefaultThemeId);
    string ThemeName(string id) => profile.SupportedThemes.FirstOrDefault(theme => theme.Id == id)?.DisplayName ?? id;

    bool AutoStartChanged => savedAutoStart.HasValue && autoStart != savedAutoStart || uncertain.Contains("AutoStart");
    public bool WindowChanged => savedMode.HasValue && mode != savedMode || uncertain.Contains("WindowOpeningMode");
    public bool HasChanges => WindowChanged || savedPace.HasValue && pace != savedPace || savedVolume.HasValue && volume != savedVolume || AutoStartChanged || ThemeChanged || LanguageChanged || uncertain.Count != 0;
    public string Signature => $"{mode}/{pace}/{volume}/{autoStart}/{themeId}/{language}";
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
        values["preference.theme"] = themeId;
        values["preference.language"] = language;
        if (!profile.UsesStationDisplayPreferences) return;
        values["preference.mode"] = mode.ToString(); values["preference.pace"] = pace.ToString(); values["preference.volume"] = volume.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (savedAutoStart.HasValue) values["preference.autostart"] = autoStart.ToString();
    }
    public void Restore(IReadOnlyDictionary<string, string> values)
    {
        if (values.GetValueOrDefault("preference.user") != UserIdentity)
            throw new System.IO.InvalidDataException("偏好草稿缺少当前Windows用户身份或属于另一用户；原草稿与当前输入保留。");
        var restoredTheme = values.GetValueOrDefault("preference.theme", themeId);
        if (!profile.SupportedThemes.Any(theme => theme.Id == restoredTheme))
        {
            restoredTheme = profile.DefaultThemeId;
            ThemeStatus = "草稿中的主题不可用，当前使用默认主题。";
        }
        if (!CanEdit(nameof(ThemeId)) && restoredTheme != themeId)
            throw new System.IO.InvalidDataException("主题偏好文件读取失败；请先核对访问权限，草稿与原件保留。");
        var restoredLanguage = values.GetValueOrDefault("preference.language", language);
        if (!PresentationPreferences.SupportedLanguages.Contains(restoredLanguage, StringComparer.Ordinal))
            restoredLanguage = PresentationPreferences.DefaultLanguage;
        if (!CanEdit(nameof(Language)) && restoredLanguage != language)
            throw new System.IO.InvalidDataException(UiText.Get("Common.LanguageUnreadable"));
        if (!profile.UsesStationDisplayPreferences) { ThemeId = restoredTheme; Language = restoredLanguage; return; }

        var restoredMode = Enum.Parse<PresentationPreferences.WindowOpeningMode>(values["preference.mode"]);
        var restoredPace = bool.Parse(values["preference.pace"]);
        var restoredVolume = int.Parse(values["preference.volume"], System.Globalization.CultureInfo.InvariantCulture);
        if ((!CanEdit("WindowOpeningMode") && restoredMode != mode) || (!CanEdit("ShowPace") && restoredPace != pace) || (!CanEdit("CueVolume") && restoredVolume != volume))
            throw new System.IO.InvalidDataException("原偏好文件读取失败；请先修复访问或文件问题，草稿与原件保留。");
        if (values.TryGetValue("preference.autostart", out var startup) && !CanEdit("AutoStart") && bool.Parse(startup) != autoStart)
            throw new InvalidOperationException("当前无法修改开机自启。请从已安装的供包台打开设置。");
        ThemeId = restoredTheme; Language = restoredLanguage; OpeningMode = restoredMode; ShowPace = restoredPace; CueVolume = restoredVolume;
        if (startup is not null) AutoStart = bool.Parse(startup);
    }
    public PreferenceSaveItem[] LastSave { get; private set; } = [];
    public bool LastSaveComplete => LastSave.All(x => x.State is "Saved" or "Unchanged");
    public string SaveSummary => string.Join("；", LastSave.Select(x => x.Name + "：" + x.Detail));
    public string? Validate() => !PresentationPreferences.SupportedLanguages.Contains(language, StringComparer.Ordinal) ? UiText.Get("Common.LanguageInvalid") : !profile.SupportedThemes.Any(theme => theme.Id == themeId) ? "请选择可用的界面主题。" : !Enum.IsDefined(mode) ? "请选择默认打开方式。" : volume is < 0 or > 100 ? "提示音音量须为0～100。" : null;
    public ConfigurationChangeItem[] Changes()
    {
        List<ConfigurationChangeItem> changes = [];
        void Add(string path, string name, string before, string after) => changes.Add(new("系统", "显示与声音", name, before, after, path, "") { Activation = "按当前Windows用户保存后立即生效" });
        static string Mode(PresentationPreferences.WindowOpeningMode value) => value == PresentationPreferences.WindowOpeningMode.FullScreen ? "最大化（全屏）" : "窗口";
        if (WindowChanged) Add("WindowOpeningMode", "默认打开方式", savedMode is { } before ? Mode(before) : "未核对", Mode(mode));
        if (savedPace.HasValue && pace != savedPace || uncertain.Contains("ShowPace")) Add("ShowPace", "效率显示", savedPace == true ? "显示" : "隐藏", pace ? "显示" : "隐藏");
        if (savedVolume.HasValue && volume != savedVolume || uncertain.Contains("CueVolume")) Add("CueVolume", "提示音音量", savedVolume + "%", volume + "%");
        if (ThemeChanged) Add(nameof(ThemeId), UiText.Get("Common.Theme"), ThemeName(savedThemeId ?? profile.DefaultThemeId), ThemeName(themeId));
        if (LanguageChanged) Add(nameof(Language), UiText.Get("Common.Language"), LanguageName(savedLanguage ?? PresentationPreferences.DefaultLanguage), LanguageName(language));
        if (AutoStartChanged) Add("AutoStart", "开机自动启动供包台", savedAutoStart == true ? "开启" : "关闭", autoStart ? "开启" : "关闭");
        return changes.ToArray();
    }
    static string LanguageName(string code) => code switch { "zh-CN" => "简体中文", "en-US" => "English", "ru-RU" => "Русский", _ => code };

    public PreferenceSaveItem[] Save()
    {
        if (Validate() is { } error) throw new InvalidOperationException(error);
        // Freeze this attempt. Notification callbacks cannot silently adopt newer edits.
        var selectedMode = mode; var selectedPace = pace; var selectedVolume = volume; var selectedAutoStart = autoStart; var selectedTheme = themeId; var selectedLanguage = language;
        List<PreferenceSaveItem> results = [];
        SaveOne("WindowOpeningMode", "打开方式", selectedMode, savedMode.HasValue, savedMode.GetValueOrDefault(), PresentationPreferences.ReadWindowMode,
            PresentationPreferences.SaveWindowMode, value => savedMode = value);
        SaveOne("ShowPace", "效率显示", selectedPace, savedPace.HasValue, savedPace.GetValueOrDefault(), app => PresentationPreferences.Read(app, "pace") ?? true,
            (app, value) => PresentationPreferences.Save(app, "pace", value), value => savedPace = value);
        SaveOne("CueVolume", "提示音", selectedVolume, savedVolume.HasValue, savedVolume.GetValueOrDefault(), PresentationPreferences.ReadCueVolume,
            PresentationPreferences.SaveCueVolume, value => savedVolume = value);
        SaveOne("AutoStart", "开机自启", selectedAutoStart, savedAutoStart.HasValue, savedAutoStart.GetValueOrDefault(), _ => ReadAutoStart(),
            (_, value) => WriteAutoStart(value), value => savedAutoStart = value);
        SaveOne(nameof(ThemeId), "界面主题", selectedTheme, savedThemeId is not null, savedThemeId ?? profile.DefaultThemeId, ReadTheme,
            PresentationPreferences.SaveTheme, value => { savedThemeId = value; ThemeStatus = ""; Changed(nameof(ThemeStatus)); });
        SaveOne(nameof(Language), UiText.Get("Common.Language"), selectedLanguage, savedLanguage is not null,
            savedLanguage ?? PresentationPreferences.DefaultLanguage, app => PresentationPreferences.ReadLanguage(app),
            PresentationPreferences.SaveLanguage, value => savedLanguage = value);
        LastSave = results.ToArray(); Changed(nameof(LastSave)); return LastSave;

        void SaveOne<T>(string path, string name, T target, bool hasOriginal, T original, Func<string, T> read, Action<string, T> write, Action<T> accepted)
        {
            if (!hasOriginal || EqualityComparer<T>.Default.Equals(target, original) && !uncertain.Contains(path)) return;
            try
            {
                if (!uncertain.Contains(path) || !EqualityComparer<T>.Default.Equals(read(applicationId), target)) write(applicationId, target);
                else if (path != "AutoStart") PresentationPreferences.RefreshSaved(applicationId, path switch { "WindowOpeningMode" => "window", "ShowPace" => "pace", nameof(ThemeId) => "theme", nameof(Language) => "language", _ => "audio" });
                accepted(target); uncertain.Remove(path); results.Add(new(path, name, "Saved", "已保存并生效"));
            }
            catch (Exception writeError) when (StorageError(writeError))
            {
                // An atomic write or its UI notification may have committed before
                // throwing. Inspect this exact target before declaring failure/retrying.
                try
                {
                    var actual = read(applicationId);
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
        PropertyChanged?.Invoke(this, new(nameof(WindowChanged))); PropertyChanged?.Invoke(this, new(nameof(ThemeChanged))); PropertyChanged?.Invoke(this, new(nameof(LanguageChanged))); PropertyChanged?.Invoke(this, new(nameof(SaveSummary)));
    }
}
