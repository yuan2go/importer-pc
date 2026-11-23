using System.Windows;
using System.ComponentModel;
using System.Windows.Input;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class StationSettingsWindow : Window
{
    readonly Func<StationSettingsWindow, Task> edit;
    readonly SupplyStationViewModel model;
    MaintenancePage? editor;
    bool workspaceReleased;
    bool? editorShown;
    internal bool ReturnToWork { get; private set; }
    internal bool IsEditing => editor is not null;

    internal StationSettingsWindow(SupplyStationViewModel viewModel, Func<StationSettingsWindow, Task> edit)
    {
        InitializeComponent();
        DataContext = model = viewModel;
        this.edit = edit;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        Activated += (_, _) => { if (!IsEditing) ReadLocalPreferences(); };
        model.PropertyChanged += ModelChanged;
        Closed += (_, _) => model.PropertyChanged -= ModelChanged;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }

    void ReadLocalPreferences()
    {
        // Read the saved values separately from the existing editor's unsaved draft.
        var preferences = new SmDisplayPreferenceDraft();
        string Read(string key, string label, string value)
        {
            var failure = preferences.ReadFailure(key);
            model.SetPreferenceNotice(key switch { "WindowOpeningMode" => "window", "ShowPace" => "pace", _ => "audio" },
                failure.Length == 0 ? null : label + "未能读取，原文件保留；请检查本机设置文件与访问权限。");
            if (failure.Length == 0) return value;
            return "未能读取";
        }

        OpeningModeValue.Text = Read("WindowOpeningMode", "默认打开方式",
            preferences.OpeningMode == PresentationPreferences.WindowOpeningMode.FullScreen ? "全屏" : "窗口");
        ShowPaceValue.Text = Read("ShowPace", "效率显示", preferences.ShowPace ? "显示" : "隐藏");
        CueVolumeValue.Text = Read("CueVolume", "提示音音量", $"{preferences.CueVolume}%");
    }

    internal void StartEditing(MaintenancePage page)
    {
        editor = page;
        EditHost.Content = page;
        ReadOnlyOverview.Visibility = Visibility.Collapsed;
        AuthorizeEdit.Content = "验证管理密码";
        UpdateAuthorization();
    }

    internal void DetachEditor() => EditHost.Content = null;
    internal void CancelReturn() => ReturnToWork = false;
    internal void LockAfterRelease() { workspaceReleased = true; editorShown = null; UpdateAuthorization(); }

    internal void UpdateAuthorization()
    {
        if (!IsEditing) return;
        bool authorized = model.IsManagementMode && !workspaceReleased;
        if (editorShown == authorized) return;
        editorShown = authorized;
        HeaderStatus.Text = authorized ? "编辑模式 · 已暂停供包" : "编辑已锁定";
        AuthorizeEdit.Visibility = authorized || workspaceReleased ? Visibility.Collapsed : Visibility.Visible;
        EditSurface.Visibility = authorized ? Visibility.Visible : Visibility.Collapsed;
        ReviewSettings.Visibility = SaveSettings.Visibility = authorized ? Visibility.Visible : Visibility.Collapsed;
        FooterStatus.Text = authorized ? DraftStatus() : workspaceReleased ? "设置已锁定，正在结束授权" : "需要验证管理密码，草稿已保留";
    }

    string DraftStatus()
    {
        if (model.HasUnconfirmedConfigurationSubmission || !model.HasUnsavedConfiguration && !model.HasUnsavedDisplayPreferences)
            return model.ConfigurationDraftStatusText;
        try
        {
            var changes = model.GetConfigurationChangeItems().Length;
            return changes > 0 ? $"有 {changes} 项未保存更改" : model.ConfigurationDraftStatusText;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.IO.InvalidDataException or System.Text.Json.JsonException)
        { return model.ConfigurationDraftStatusText; }
    }

    void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (editorShown == true && e.PropertyName is nameof(model.ConfigurationDraftStatusText) or nameof(model.HasUnsavedConfiguration) or nameof(model.HasUnsavedDisplayPreferences))
            FooterStatus.Text = DraftStatus();
    }

    internal void ShowAuthorizationFailure(string message = "暂时无法进入编辑，请重试或检查管理授权服务。")
    { AuthorizationFeedbackText.Text = message; AuthorizationFeedback.Visibility = Visibility.Visible; }

    async void AuthorizeEdit_Click(object sender, RoutedEventArgs e)
    {
        AuthorizationFeedback.Visibility = Visibility.Collapsed;
        AuthorizeEdit.IsEnabled = false;
        try { await edit(this); }
        finally { AuthorizeEdit.IsEnabled = true; }
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
    void Return_Click(object sender, RoutedEventArgs e) { ReturnToWork = true; Close(); }
    void Review_Click(object sender, RoutedEventArgs e) => editor?.ReviewChanges();
    async void Save_Click(object sender, RoutedEventArgs e) { if (editor is { } page) await page.SaveChangesAsync(); }
}
