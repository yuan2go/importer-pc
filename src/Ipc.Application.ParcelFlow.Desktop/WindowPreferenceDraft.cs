using Ipc.Presentation.Wpf;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    string? windowPreferenceApplication;
    PresentationPreferences.WindowOpeningMode savedWindowMode, windowModeDraft;
    SmDisplayPreferenceDraft? smPreferences;
    public SmDisplayPreferenceDraft SmPreferences
    {
        get
        {
            if (smPreferences is not null) return smPreferences;
            smPreferences = new(windowPreferenceApplication ?? structuredBase?.ApplicationId ?? Snapshot?.ApplicationId ?? BusinessDesktopCatalog.Station.ApplicationId);
            smPreferences.PropertyChanged += (_, _) => WindowPreferenceChanged();
            return smPreferences;
        }
    }
    public string WindowPreferenceStatus { get; private set; } = "";
    public bool HasUnsavedWindowPreference => BusinessDesktopCatalog.For(windowPreferenceApplication).UsesStationDisplayPreferences ? SmPreferences.WindowChanged : windowPreferenceApplication is not null && windowModeDraft != savedWindowMode;
    public PresentationPreferences.WindowOpeningMode WindowModeDraft
    {
        get => BusinessDesktopCatalog.For(windowPreferenceApplication).UsesStationDisplayPreferences ? SmPreferences.OpeningMode : windowModeDraft;
        set
        {
            if (BusinessDesktopCatalog.For(windowPreferenceApplication).UsesStationDisplayPreferences) { SmPreferences.OpeningMode = value; return; }
            if (windowModeDraft == value) return;
            windowModeDraft = value;
            WindowPreferenceStatus = HasUnsavedWindowPreference ? "打开方式已修改，请点击底部“保存设置”。" : "";
            WindowPreferenceChanged();
        }
    }
    public void LoadWindowPreference(string application)
    {
        if (windowPreferenceApplication == application) return;
        if (BusinessDesktopCatalog.For(application).UsesStationDisplayPreferences)
        {
            windowPreferenceApplication = application;
            _ = SmPreferences; WindowPreferenceChanged(); return;
        }
        var mode = PresentationPreferences.ReadWindowMode(application);
        savedWindowMode = windowModeDraft = mode;
        windowPreferenceApplication = application;
        WindowPreferenceChanged();
    }
    public async Task SaveWindowPreferenceAsync()
    {
        if (BusinessDesktopCatalog.For(windowPreferenceApplication).UsesStationDisplayPreferences)
        {
            await SaveSmPreferencesAsync(); return;
        }
        var saveWindow = HasUnsavedWindowPreference;
        var saveDisplay = smPreferences is { } preferences && (preferences.ThemeChanged || preferences.LanguageChanged);
        if (!saveWindow && !saveDisplay) return;
        var mode = windowModeDraft;
        var application = windowPreferenceApplication!;
        await RequireManagementModeAsync();
        if (saveWindow) try
        {
            PresentationPreferences.SaveWindowMode(application, mode);
            savedWindowMode = mode;
            WindowPreferenceStatus = "打开方式已保存并生效，下次打开软件仍使用此方式。";
            Message = WindowPreferenceStatus;
        }
        catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            WindowPreferenceStatus = "打开方式未保存；输入已保留，请检查本机设置文件与访问权限后重试。";
            Message = WindowPreferenceStatus;
        }
        if (saveDisplay)
        {
            SmPreferences.Save();
            WindowPreferenceStatus = (saveWindow ? WindowPreferenceStatus + " " : "") + SmPreferences.SaveSummary;
            Message = WindowPreferenceStatus;
        }
        WindowPreferenceChanged();
    }
    public async Task SaveSmPreferencesAsync()
    {
        await RequireManagementModeAsync();
        SmPreferences.Save();
        WindowPreferenceStatus = SmPreferences.SaveSummary;
        Message = UiText.Get("Settings.SavedSeparately") + WindowPreferenceStatus;
        WindowPreferenceChanged();
    }
    void WindowPreferenceChanged()
    {
        Changed(nameof(WindowModeDraft)); Changed(nameof(WindowPreferenceStatus));
        Changed(nameof(HasUnsavedWindowPreference)); Changed(nameof(HasUnsavedDisplayPreferences));
        Changed(nameof(ParameterConfigurationSummary));
        Changed(nameof(CanReviewConfiguration)); Changed(nameof(HasPreferenceSaveFailure));
        NotifyConfigurationReview();
    }
    ConfigurationChangeItem[] WindowPreferenceChanges()
    {
        if (BusinessDesktopCatalog.For(windowPreferenceApplication).UsesStationDisplayPreferences || DesktopProfile.UsesStationDisplayPreferences)
            return SmPreferences.Changes();
        ConfigurationChangeItem[] changes = smPreferences?.Changes() ?? [];
        return HasUnsavedWindowPreference
            ? [.. changes, new("系统", "显示偏好", "默认打开方式", WindowModeLabel(savedWindowMode), WindowModeLabel(windowModeDraft), "WindowOpeningMode", "") { Activation = "保存后立即生效" }]
            : changes;
    }
    static string WindowModeLabel(PresentationPreferences.WindowOpeningMode mode) => mode == PresentationPreferences.WindowOpeningMode.FullScreen ? "最大化（全屏）" : "窗口";
}
