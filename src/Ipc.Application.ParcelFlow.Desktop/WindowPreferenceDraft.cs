using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    string? windowPreferenceApplication;
    PresentationPreferences.WindowOpeningMode savedWindowMode, windowModeDraft;
    public string WindowPreferenceStatus { get; private set; } = "";
    public bool HasUnsavedWindowPreference => windowPreferenceApplication is not null && windowModeDraft != savedWindowMode;
    public PresentationPreferences.WindowOpeningMode WindowModeDraft
    {
        get => windowModeDraft;
        set
        {
            if (windowModeDraft == value) return;
            windowModeDraft = value;
            WindowPreferenceStatus = HasUnsavedWindowPreference ? "打开方式已修改，请点击底部“保存设置”。" : "";
            WindowPreferenceChanged();
        }
    }
    public void LoadWindowPreference(string application)
    {
        if (windowPreferenceApplication == application) return;
        var mode = PresentationPreferences.ReadWindowMode(application);
        savedWindowMode = windowModeDraft = mode;
        windowPreferenceApplication = application;
        WindowPreferenceChanged();
    }
    public async Task SaveWindowPreferenceAsync()
    {
        if (!HasUnsavedWindowPreference) return;
        var mode = windowModeDraft;
        var application = windowPreferenceApplication!;
        await RequireManagementModeAsync();
        try
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
        WindowPreferenceChanged();
    }
    void WindowPreferenceChanged()
    {
        Changed(nameof(WindowModeDraft)); Changed(nameof(WindowPreferenceStatus));
        Changed(nameof(HasUnsavedWindowPreference)); Changed(nameof(HasUnsavedDisplayPreferences));
        Changed(nameof(ParameterConfigurationSummary));
    }
    ConfigurationChangeItem[] WindowPreferenceChanges() => HasUnsavedWindowPreference
        ? [new("系统", "显示偏好", "默认打开方式", WindowModeLabel(savedWindowMode), WindowModeLabel(windowModeDraft), "WindowOpeningMode", "") { Activation = "保存后立即生效" }]
        : [];
    static string WindowModeLabel(PresentationPreferences.WindowOpeningMode mode) => mode == PresentationPreferences.WindowOpeningMode.FullScreen ? "最大化（全屏）" : "窗口";
}
