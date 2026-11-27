using Ipc.Presentation.Wpf;
using System.ComponentModel;
using System.Windows;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

// Owns a view and a temporary grant only. The station keeps its one complete
// draft/page so invalid text and unconfirmed save identities survive this window.
public partial class StationSettingsWindow : Window
{
    readonly SupplyStationViewModel model;
    readonly MaintenancePage editor;
    readonly IWorkstationAdministration authority;
    sealed record DisplayField(string Label, string Value, string AutomationId);
    string? session;
    bool authenticating, closing, closeReady;
    public StationSettingsWindow(SupplyStationViewModel model, MaintenancePage editor)
    {
        InitializeComponent(); UiTypography.Apply(this);
        editor.SetStationSettingsLayout(EditActions);
        this.model = model; this.editor = editor; authority = (IWorkstationAdministration)model.Application;
        DataContext = model;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width); Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        model.PropertyChanged += Changed;
        Loaded += (_, _) => UpdateState();
        IsVisibleChanged += (_, _) => { if (IsVisible) RefreshSavedPreferences(); };
        Closing += ClosingSettings;
        Closed += (_, _) => { model.PropertyChanged -= Changed; editor.SetStationSettingsLayout(null); EditorHost.Content = null; if (model.OwnedManagementSession == session) model.OwnedManagementSession = null; };
    }
    void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null) RefreshSavedPreferences();
        if (e.PropertyName is null or nameof(model.Snapshot) or nameof(model.IsManagementMode) or nameof(model.ManagementStatus) or nameof(model.Fresh)) UpdateState();
    }
    void UpdateState()
    {
        var granted = ReferenceEquals(EditorHost.Content, editor) && session is not null && model.IsManagementMode && model.ManagementStatus.SessionId == session;
        EditorHost.IsEnabled = granted && model.Fresh;
        EditorHost.Visibility = granted ? Visibility.Visible : Visibility.Collapsed;
        ReadOnlyBody.Visibility = granted ? Visibility.Collapsed : Visibility.Visible;
        EditSettings.Visibility = granted ? Visibility.Collapsed : Visibility.Visible;
        EditActions.Visibility = SettingsDraft.Visibility = granted ? Visibility.Visible : Visibility.Collapsed;
        ReturnToWorkstation.Content = granted ? UiText.Get("Settings.DisplayWork") : UiText.Get("Settings.ReturnWork");
        ReturnToWorkstation.ToolTip = granted ? UiText.Get("Settings.HideDraft") : UiText.Get("Settings.HideAndReturn");
        CloseSettings.Content = granted ? UiText.Get("Settings.CloseEdit") : UiText.Get("Common.Close");
        CloseSettings.ToolTip = granted ? UiText.Get("Settings.CloseEndGrant") : UiText.Get("Settings.CloseWindow");
        AuthorizationText.Text = granted ? UiText.Format("Settings.GrantUntil", model.ManagementStatus.ExpiresAt?.ToLocalTime().ToString("HH:mm")) :
            session is null ? UiText.Get("Settings.ReadOnly") : "授权待核对 · 草稿保留";
        EffectiveSettingsCards.Visibility = model.Fresh && model.Snapshot is not null ? Visibility.Visible : Visibility.Collapsed;
        if (!model.Fresh || model.Snapshot is not { } snapshot)
        {
            ReadOnlySettings.Text = UiText.Get("Settings.AwaitEffective");
            return;
        }

        var configuration = snapshot.Configuration.Effective;
        var rcs = configuration.Wcs;
        var host = rcs is not null && Uri.TryCreate(rcs.Target, UriKind.Absolute, out var uri)
            ? new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri.GetLeftPart(UriPartial.Authority) : UiText.Get("Common.ConfigurePending");
        var revision = snapshot.Configuration.Revision.ToString();
        ReadOnlySettings.Text = snapshot.HasPendingConfiguration == true
            ? UiText.Format("Settings.PendingRevision", revision)
            : UiText.Format("Settings.EffectiveRevision", revision);
        UpdateFields(StationFields,
        [
            new("本站", configuration.StationId, "StationSettings.StationId"),
            new("供包台编号", string.IsNullOrWhiteSpace(configuration.StationCode) ? UiText.Get("Common.ConfigurePending") : configuration.StationCode, "StationSettings.StationCode"),
            new("RCS 主机", host, "StationSettings.RcsHost"),
            new("仓 ID", rcs?.Parameters.GetValueOrDefault("zone-id") ?? UiText.Get("Settings.NotSavedSeparately"), "StationSettings.Zone"),
            new("生产线 ID", rcs?.Parameters.GetValueOrDefault("project-id") ?? UiText.Get("Settings.NotSavedSeparately"), "StationSettings.Project")
        ]);
        UpdateFields(LeftCameraFields, CameraFields("left"));
        UpdateFields(RightCameraFields, CameraFields("right"));

        DisplayField[] CameraFields(string position)
        {
            var topology = configuration.Topology;
            var lane = topology?.Positions.SingleOrDefault(p => p.Id == position);
            var reader = lane is null ? null : topology!.Readers.Single(r => r.DeviceId == lane.ReaderDeviceId);
            var prefix = "StationSettings.Camera." + position;
            return
            [
                new("IP", reader?.Parameters?.GetValueOrDefault("device.ip") is { Length: > 0 } ip ? ip : UiText.Get("Common.NotConfigured"), prefix + ".Ip"),
                new("物理序列号", reader?.Parameters?.GetValueOrDefault("device.serial") is { Length: > 0 } serial ? serial : UiText.Get("Common.NotConfigured"), prefix + ".Serial"),
                new("RCS 相机编码", rcs?.Parameters.GetValueOrDefault(position + "-bcr-code") is { Length: > 0 } code ? code : UiText.Get("Settings.LaneDisabled"), prefix + ".Code")
            ];
        }
    }
    static void UpdateFields(System.Windows.Controls.ItemsControl target, DisplayField[] fields)
    {
        fields = fields.Select(field => field with { Label = UiText.Authored(field.Label) }).ToArray();
        if (target.ItemsSource is not DisplayField[] current || !current.SequenceEqual(fields))
            target.ItemsSource = fields;
    }
    void RefreshSavedPreferences()
    {
        // Read the saved per-user files independently of the edit session and its draft.
        var saved = new SmDisplayPreferenceDraft();
        PreferenceFields.ItemsSource = new DisplayField[]
        {
            new(UiText.Get("Settings.WindowMode"), Value("WindowOpeningMode", saved.OpeningMode == PresentationPreferences.WindowOpeningMode.FullScreen ? UiText.Get("Settings.FullScreen") : UiText.Get("Settings.Windowed")), "StationSettings.WindowMode"),
            new(UiText.Get("Settings.Pace"), Value("ShowPace", saved.ShowPace ? UiText.Get("Settings.Show") : UiText.Get("Settings.Hide")), "StationSettings.Pace"),
            new(UiText.Get("Settings.AudioVolume"), Value("CueVolume", saved.CueVolume == 0 ? UiText.Get("Settings.Mute") : $"{saved.CueVolume}%"), "StationSettings.CueVolume"),
            new(UiText.Get("Settings.Theme"), saved.ReadFailure(nameof(saved.ThemeId)) is { Length: > 0 } themeError ? themeError : UiText.Get(saved.ThemeId == WorkstationThemeManager.IndustrialDarkId ? "Settings.Dark" : "Settings.Light"), "StationSettings.Theme.ReadOnly"),
            new(UiText.Get("Settings.Language"), saved.ReadFailure(nameof(saved.Language)) is { Length: > 0 } languageError ? languageError : WorkstationLocalizationManager.Languages.Single(item => item.Code == saved.Language).NativeName, "StationSettings.Language.ReadOnly"),
            new(UiText.Get("Setup.AutoStart"), saved.CanEdit("AutoStart") ? saved.AutoStart ? UiText.Get("Common.Enabled") : UiText.Get("Common.DisabledSetting") : saved.ReadFailure("AutoStart"), "StationSettings.AutoStart")
        };
        string Value(string path, string value)
        {
            var key = path switch { "WindowOpeningMode" => "window", "ShowPace" => "pace", _ => "audio" };
            var name = path switch { "WindowOpeningMode" => UiText.Get("Settings.OpenMode"), "ShowPace" => UiText.Get("Settings.Pace"), _ => UiText.Get("Settings.Cues") };
            model.SetPreferenceNotice(key, saved.CanEdit(path) ? null : name + UiText.Get("Settings.PrefReadFailure"));
            return saved.CanEdit(path) ? value : UiText.Get("Settings.Unreadable");
        }
    }
    async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (authenticating || closing) return;
        authenticating = true; EditSettings.IsEnabled = false;
        try
        {
            var password = new ManagementModeWindow(authority, UiText.Get("Settings.Edit"), bindSession: true) { Owner = this };
            password.ShowDialog();
            // A grant may exist even when its follow-up observation failed and
            // the password window was canceled. Retain its identity for exit.
            if (password.EnteredSessionId is { Length: > 0 } grantedId) { session = grantedId; model.OwnedManagementSession = grantedId; }
            if (!password.Entered) return;
            await model.RefreshManagementModeAsync();
            if (!model.IsManagementMode || password.EnteredSessionId is not { Length: > 0 } id || model.ManagementStatus.SessionId != id)
                throw new UnauthorizedAccessException(UiText.Get("Settings.GrantRecheck"));
            session = id;
            await model.RunAsync(model.EnsureMaintenanceLoadedAsync, UiText.Get("Settings.LoadDraft"));
            if (!model.HasLoadedConfiguration) throw new InvalidOperationException(UiText.Get("Settings.LoadFailed"));
            EditorHost.Content = editor; UpdateState();
        }
        catch (Exception error) { model.Message = ChineseInterfaceText.ParameterMessage(error.Message, UiText.Get("Settings.EditNotOpened")); }
        finally { authenticating = false; EditSettings.IsEnabled = true; }
    }
    async void ClosingSettings(object? sender, CancelEventArgs e)
    {
        if (closeReady) return;
        e.Cancel = true; if (closing || authenticating) return;
        closing = true; IsEnabled = false;
        try
        {
            await System.Windows.Threading.Dispatcher.Yield();
            await CloseAfterDrainAsync();
        }
        catch (Exception error) { model.Message = UiText.Get("Settings.CloseIncomplete") + ChineseInterfaceText.ParameterMessage(error.Message, UiText.Get("Settings.CloseRetry")); }
        finally { closing = false; IsEnabled = true; }
    }
    internal async Task CloseForOwnerStopAsync()
    {
        if (closing || authenticating) throw new InvalidOperationException(UiText.Get("Settings.CloseWait"));
        closing = true; IsEnabled = false;
        try { await CloseAfterDrainAsync(); }
        finally { closing = false; IsEnabled = true; }
    }
    async Task CloseAfterDrainAsync()
    {
        ConfigurationCloseGuard.CommitAndFindInvalid(editor);
        await model.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        if (session is not null) await model.ServiceAsync(() => authority.ExitManagementModeAsync(session));
        await model.RefreshManagementModeAsync();
        closeReady = true; Close();
    }
    void Close_Click(object sender, RoutedEventArgs e) => Close();
    void Return_Click(object sender, RoutedEventArgs e) => (Owner as SupplyStationWindow)?.ReturnToWorkstation();
}
