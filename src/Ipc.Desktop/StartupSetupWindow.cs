using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Assembly;
using Ipc.Platform;
using Microsoft.Win32;

namespace Ipc.Desktop;

public sealed record InstallationPreparationProgress(string Stage, string File, double Percent);

public sealed class StartupSetupWindow : StartupWorkflowWindow
{
    readonly ProductDeploymentProfile deploymentProfile;
    internal Func<string, DeploymentLocation?> CurrentInstallation { get; set; } = WindowsDeploymentSetup.Current;
    internal Func<DeploymentLocation, uint> LaunchDesktop { get; set; } = location => OrdinaryRuntimeProcess.LaunchForCurrentUser(
        Path.Combine(location.ReleaseRoot, "desktop", InstallationIdentity.DesktopExecutable), ["--after-install"], location.ReleaseRoot);
    internal Func<DeploymentMedia, string, string, string, string?, string[], bool, string?, CancellationToken, WindowsDeploymentRequest> Review { get; set; } = ParcelStartupServices.Review;
    internal Func<WindowsDeploymentRequest, IProgress<WindowsInstallationStage>, IProgress<MaintenanceProgress>, CancellationToken, Task<DeploymentLocation>> Install { get; set; } = ParcelStartupServices.InstallWithPreflightAsync;
    internal Func<WindowsDeploymentRequest, InstallationInspection> InspectInstallation { get; set; } = ParcelStartupServices.InspectInstallation;
    internal Func<string, string, InstallationContinuation?> DiscoverInstallation { get; set; } = WindowsDeploymentSetup.DiscoverInstallation;
    internal Func<WindowsDeploymentRequest, IProgress<MaintenanceProgress>, CancellationToken, Task<InstallationInspection>> ConcludeInstallation { get; set; }
    internal Func<WindowsDeploymentRequest, IProgress<MaintenanceProgress>, CancellationToken, Task> RepairRegistration { get; set; } = (request, progress, token) =>
        ParcelStartupServices.RepairRegistrationAsync(Wire.FileHash(WindowsDeploymentSetup.Registration(request.ApplicationId)), request, progress, token);
    readonly Func<IProgress<InstallationPreparationProgress>, CancellationToken, Task<string>>? prepareMedia;
    CancellationTokenSource? preparing;
    readonly ProgressBar preparationProgress = new ProgressBar { Height = 6, Margin = new(0, 20, 0, 12) }.Themed(Control.ForegroundProperty, "Accent");
    readonly TextBlock preparationFile = new() { TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label };
    readonly TextBlock installationStage = new() { FontSize = UiTypography.Body, TextWrapping = TextWrapping.Wrap, Margin = new(0, 24, 0, 14) };
    readonly List<string> installationDetails = [];
    TextBox? installationDetailView;
    DeploymentMedia? media;
    WindowsDeploymentRequest? reviewed;
    DeploymentLocation? reviewedLocation;
    DeploymentComponentReview[] components = [];
    DeploymentLocation? installed;
    public DeploymentLocation? Installed => installed;
    public bool Launched { get; private set; }
    // An independent elevated helper may still need the prepared media after a lost receipt.
    public bool CanCleanPreparation => !installing && !outcomeUnknown;
    TextBox data = null!, program = null!;
    CheckBox startMenu = null!, desktopIcon = null!, runAfter = null!;
    CheckBox? autoStart;
    bool autoStartInitialized;
    string? autoStartReadIssue;
    bool installing, failed, outcomeUnknown;
    bool? needsFirstConfiguration;
    string configurationMessage = "", failureMessage = "", preflightError = "";
    string newStation = "", selectedProgram = "";
    string? enteredData;
    string? installationRecordDirectory;
    string? preparedConfigurationDirectory;
    readonly TextBlock recordFeedback = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label, Margin = new(0, 10, 0, 0) }.Themed(TextBlock.ForegroundProperty, "Danger");
    bool selectedStartMenu = true, selectedDesktop = true, selectedAutoStart, advancedExpanded;
    int step;
    string installationKind = UiText.Get("Setup.Install");
    double optionsHeight = 560;
    readonly Button next, back, records, cancel, inspect, resumeRemoval, repairCommitted, concludeUncommitted, reviewAgain;
    readonly string? initialLanguage;
    bool languageApplied;
    string? languageWarning;
    bool reopenedInstallation;
    InstallationInspection? originalInspection;
    public StartupSetupWindow(BusinessLaunch launch, Func<IProgress<InstallationPreparationProgress>, CancellationToken, Task<string>>? prepareMedia = null, string? businessVersion = null, string? initialLanguage = null) : base(UiText.Get("Setup.TitlePrefix") + InstallationIdentity.DisplayName, installation: true, applicationId: Ipc.Assembly.ParcelStartupServices.ApplicationId)
    {
        deploymentProfile = ProductDeploymentCatalog.Require(ApplicationId);
        this.prepareMedia = prepareMedia;
        this.initialLanguage = initialLanguage;
        ConcludeInstallation = (request, progress, token) => ParcelStartupServices.ContinueInstallationAsync(media ?? throw new InvalidOperationException(UiText.Get("Setup.MediaRequired")), request, false, progress, token);
        if (deploymentProfile.UsesInstallationContinuation) RepairRegistration = async (request, progress, token) =>
        {
            var result = await ParcelStartupServices.ContinueInstallationAsync(media ?? throw new InvalidOperationException(UiText.Get("Setup.MediaRequired")), request, true, progress, token);
            if (result.Status != "Completed") throw new InstallationOutcomeUnknownException(result.Detail);
        };
        if (businessVersion is not null) { VersionLabel.Text = UiText.Get("Setup.VersionPrefix") + BusinessDesktopCatalog.For(ApplicationId).InstallerVersionPrefix + "V" + businessVersion + UiText.Get("Setup.VersionSuffix"); VersionLabel.Visibility = Visibility.Visible; }
        back = Action(UiText.Get("Common.Back"), () =>
        {
            CaptureInputs();
            if (InlineReviewReady) { reviewed = null; reviewedLocation = null; }
            else if (step > 0) step--;
            Render(); return Task.CompletedTask;
        });
        next = Action(UiText.Get("Setup.CheckMedia"), Next); StartupAppearance.Primary(next);
        records = Action(UiText.Get("Setup.OpenRecords"), OpenRecordDirectory, readOnly: true);
        inspect = Action(UiText.Get("Setup.ViewResult"), InspectOriginal, readOnly: true);
        repairCommitted = Action(UiText.Get("Setup.RepairRegistration"), async () =>
        {
            if (reviewed is null || originalInspection?.Status != "CommittedNeedsSystemRepair") return;
            var current = await Task.Run(() => InspectInstallation(reviewed));
            if (current.Status != "CommittedNeedsSystemRepair" || current.Location is null) throw new InvalidOperationException(UiText.Get("Setup.RegistrationChanged"));
            ResolveKnownMaintenanceOutcome(); // The read-back above resolved the original committed identity.
            await RunMaintenance(UiText.Get("Setup.RepairRegistrationDetail") + reviewed.OperationId + UiText.Get("Setup.RepairKeepsData"), (progress, token) => RepairRegistration(reviewed, progress, token));
            installed = current.Location; outcomeUnknown = false; step = 3; Render();
        }, readOnly: true);
        concludeUncommitted = Action(UiText.Get("Setup.CloseOriginal"), async () =>
        {
            if (reviewed is null || originalInspection?.Status != "NotCommitted") throw new InvalidOperationException(UiText.Get("Setup.CheckOriginalFirst"));
            var result = await RunMaintenance(UiText.Get("Setup.CloseOriginalDetail") + reviewed.OperationId,
                (progress, token) => ConcludeInstallation(reviewed, progress, token));
            if (result.Status != "Failed") throw new InstallationOutcomeUnknownException(UiText.Get("Setup.OriginalStillOpen"));
            ResolveKnownMaintenanceOutcome(); outcomeUnknown = false; failed = false; reviewed = null; originalInspection = null; step = 1;
            Render(); Message(UiText.Get("Setup.OriginalClosed"));
        }, readOnly: true);
        AutomationProperties.SetAutomationId(concludeUncommitted, "Installation.ConcludeOriginal");
        AutomationProperties.SetAutomationId(repairCommitted, "Installation.RepairOriginal");
        AutomationProperties.SetAutomationId(inspect, "Installation.InspectOriginal");
        reviewAgain = Action(UiText.Get("Setup.RecheckOptions"), () =>
        {
            reopenedInstallation = false; reviewed = null; installed = null; originalInspection = null; step = 1;
            Render(); return Task.CompletedTask;
        });
        AutomationProperties.SetAutomationId(reviewAgain, "Installation.ReviewAgain");
        resumeRemoval = Action(UiText.Get("Setup.UninstallAction"), async () =>
        {
            var current = CurrentInstallation(ParcelStartupServices.ApplicationId);
            if (current is not null && TrustedProgramRoots.Contains(current.ReleaseRoot, AppContext.BaseDirectory))
            {
                await Task.Run(() => WindowsDeploymentSetup.LaunchUninstaller(ParcelStartupServices.ApplicationId));
                Launched = true; Busy = false; Close();
            }
            else { new ProductMaintenanceWindow(media!) { Owner = this }.ShowDialog(); Render(); }
        }, readOnly: true);
        cancel = Action(UiText.Get("Common.Cancel"), () => { Busy = false; Close(); return Task.CompletedTask; }, readOnly: true);
        next.IsDefault = false; cancel.IsCancel = true;
        AutomationProperties.SetLiveSetting(recordFeedback, AutomationLiveSetting.Polite);
        cancel.Click += (_, _) =>
        {
            if (preparing is null) return;
            CloseAfterCancellation = true; preparing.Cancel(); cancel.IsEnabled = false;
            Message(UiText.Get("Setup.CancelPreparing"));
        };
        foreach (var button in Actions.Children.OfType<Button>()) { button.MinHeight = System.Globalization.CultureInfo.CurrentUICulture.Name == PresentationPreferences.DefaultLanguage ? 40 : 44; button.Margin = new(10, 16, 0, 0); }
        Render(); Loaded += async (_, _) => await Run(Next);
        Closed += (_, _) =>
        {
            if (CanCleanPreparation && preparedConfigurationDirectory is { } directory && Directory.Exists(directory))
            {
                try { File.Delete(Path.Combine(directory, "business.json")); Directory.Delete(directory); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { System.Diagnostics.Trace.TraceError("Installation preparation cleanup incomplete; retained {0}: {1}", directory, error); }
            }
        };
        Closing += (_, _) => { if (preparing is not null) { CloseAfterCancellation = true; preparing.Cancel(); Message(UiText.Get("Setup.CancelPreparing")); } };
    }
    string ApplicationId => ParcelStartupServices.ApplicationId;
    bool InlineReviewReady => deploymentProfile.UsesInlineInstallationReview && step == 1 && reviewed is not null;
    void Section(string label)
    {
        Body.Children.Add(new TextBlock { Text = label, FontSize = UiTypography.Group, FontWeight = UiTypography.Emphasis, Margin = new(0, 20, 0, 10) });
        Body.Children.Add(new Border { Height = 1, Margin = new(0, 0, 0, 6) }.Themed(Border.BackgroundProperty, "Line"));
    }
    Grid Row(string label, UIElement value, UIElement? end = null)
    {
        var row = new Grid { Margin = new(0, 6, 0, 6) };
        row.ColumnDefinitions.Add(new() { Width = new(System.Globalization.CultureInfo.CurrentUICulture.Name == PresentationPreferences.DefaultLanguage ? 82 : 134) }); row.ColumnDefinitions.Add(new());
        var heading = new Label { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, Padding = new(0, 6, 8, 6), VerticalAlignment = VerticalAlignment.Top }.Themed(Control.ForegroundProperty, "Muted");
        if (value is Control control) heading.Target = control;
        row.Children.Add(heading); Grid.SetColumn(value, 1); row.Children.Add(value);
        if (end is not null) { row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); Grid.SetColumn(end, 2); row.Children.Add(end); }
        Body.Children.Add(row); return row;
    }
    TextBox InstallationFolder(string label, string value, bool retainRegistered = false, bool reviewFrozen = false)
    {
        var box = new TextBox { Text = value, IsReadOnly = retainRegistered || reviewFrozen, MinWidth = 0, FontSize = UiTypography.Label, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetName(box, label);
        if (retainRegistered) AutomationProperties.SetHelpText(box, UiText.Get("Setup.RegisteredPathHelp"));
        else if (reviewFrozen) AutomationProperties.SetHelpText(box, UiText.Get("Setup.FrozenPathHelp"));
        box.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(TextBox.Text)) { Source = box });
        var browse = new Button { Content = UiText.Get("Setup.Browse"), IsEnabled = !retainRegistered && !reviewFrozen, MinWidth = 72, Margin = new(10, 0, 0, 0) };
        AutomationProperties.SetName(browse, UiText.Get("Setup.BrowseName") + label);
        browse.Click += (_, _) => { var picker = new OpenFolderDialog { Title = label }; if (picker.ShowDialog(this) == true) box.Text = picker.FolderName; };
        Row(label, box, browse); return box;
    }
    void Summary(string label, string value, bool bounded = false)
    {
        var box = new TextBox { Text = value, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 0, Padding = new(0, 6, 0, 6), BorderThickness = new(0), Background = Brushes.Transparent };
        AutomationProperties.SetName(box, label); Row(label, box);
        if (bounded) { box.MaxHeight = 120; box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
    }
    void CaptureInputs()
    {
        if (step != 1 || data is null) return;
        enteredData = data.Text; selectedProgram = program.Text;
        selectedStartMenu = startMenu.IsChecked == true; selectedDesktop = desktopIcon.IsChecked == true;
        selectedAutoStart = autoStart?.IsChecked == true;
    }
    void Render()
    {
        if (step == 3 && installed is not null && initialLanguage is { } language && !languageApplied)
        {
            languageApplied = true;
            try { PresentationPreferences.SaveLanguage(ApplicationId, language); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                languageWarning = UiText.Get("Common.LanguageSaveFailed");
                System.Diagnostics.Trace.TraceWarning("Installation language preference: " + DiagnosticText.Protect(error.Message));
            }
        }
        Body.Children.Clear(); Form = Body; Body.Margin = new(0);
        if (runAfter is not null) Actions.Children.Remove(runAfter);
        InstallationStep(step == 3 ? 2 : step >= 2 ? 1 : 0);
        InstallationHeading.Text = outcomeUnknown ? UiText.Get("Setup.PendingResult") : failed ? UiText.Get("Setup.Incomplete") : installing ? UiText.Get("Setup.Installing") : step switch { 0 => UiText.Get("Setup.Preparing"), 1 => UiText.Get("Setup.Options"), 2 => UiText.Get("Setup.Confirm"), _ => UiText.Get("Setup.Complete") };
        back.Content = InlineReviewReady ? UiText.Get("Setup.EditOptions") : UiText.Get("Common.Back");
        back.Visibility = (step == 2 || InlineReviewReady) && !installing && !failed && !outcomeUnknown ? Visibility.Visible : Visibility.Collapsed;
        cancel.Visibility = installing || step == 3 ? Visibility.Collapsed : Visibility.Visible;
        cancel.Content = failed || outcomeUnknown ? UiText.Get("Common.Close") : UiText.Get("Common.Cancel");
        next.Visibility = installing || outcomeUnknown ? Visibility.Collapsed : Visibility.Visible;
        records.Visibility = outcomeUnknown || failed && Directory.Exists(installationRecordDirectory) ? Visibility.Visible : Visibility.Collapsed;
        records.SetResourceReference(StyleProperty, typeof(Button));
        inspect.SetResourceReference(StyleProperty, outcomeUnknown && originalInspection?.Status is not ("NotCommitted" or "CommittedNeedsSystemRepair") ? "StartupPrimary" : typeof(Button));
        repairCommitted.SetResourceReference(StyleProperty, "StartupPrimary");
        concludeUncommitted.SetResourceReference(StyleProperty, "StartupPrimary");
        records.IsDefault = false; next.IsDefault = step < 2 && !InlineReviewReady && !outcomeUnknown;
        inspect.Visibility = outcomeUnknown ? Visibility.Visible : Visibility.Collapsed;
        repairCommitted.Visibility = outcomeUnknown && originalInspection?.Status == "CommittedNeedsSystemRepair" ? Visibility.Visible : Visibility.Collapsed;
        concludeUncommitted.Visibility = outcomeUnknown && originalInspection?.Status == "NotCommitted" ? Visibility.Visible : Visibility.Collapsed;
        reviewAgain.Visibility = step == 3 && reopenedInstallation ? Visibility.Visible : Visibility.Collapsed;
        inspect.IsDefault = outcomeUnknown && originalInspection?.Status is not ("NotCommitted" or "CommittedNeedsSystemRepair");
        resumeRemoval.Visibility = step == 1 && !InlineReviewReady && CurrentInstallation(ApplicationId) is { LifecycleState: not "Installed" } ? Visibility.Visible : Visibility.Collapsed;
        recordFeedback.Text = "";
        recordFeedback.ToolTip = null;
        if (outcomeUnknown)
        {
            Height = Math.Min(520, MaxHeight);
            Message(originalInspection?.Status == "NotCommitted" ? UiText.Get("Setup.NotCommitted") : originalInspection?.Status == "CommittedNeedsSystemRepair"
                ? UiText.Get("Setup.RepairOutcomeUnknown")
                : UiText.Get("Setup.OutcomeUnknown"));
            Note(UiText.Get("Setup.CheckNoReplay"));
            var caution = new TextBlock { Text = UiText.Get("Setup.NoRepeat"), TextWrapping = TextWrapping.Wrap }.Themed(TextBlock.ForegroundProperty, "Warning");
            AutomationProperties.SetLiveSetting(caution, AutomationLiveSetting.Assertive);
            Body.Children.Add(new Border { CornerRadius = new(4), Padding = new(14, 11, 14, 11), Margin = new(0, 16, 0, 18), Child = caution }.Themed(Border.BackgroundProperty, "WarningBackground"));
            Details(historical: true);
            Body.Children.Add(new TextBlock { Text = UiText.Get("Setup.CloseKeepsRunning"), TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label, Margin = new(0, 16, 0, 0) }.Themed(TextBlock.ForegroundProperty, "Muted"));
            Body.Children.Add(recordFeedback);
            return;
        }
        if (failed)
        {
            Height = Math.Min(optionsHeight, MaxHeight);
            Message(ChineseInterfaceText.InstallationFailure(failureMessage)); Status.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
            Note(UiText.Get("Setup.FailedStage") + installationStage.Text + UiText.Get("Setup.PartialFailure"));
            Details(historical: true); next.Content = UiText.Get("Setup.ReturnOptions"); Body.Children.Add(recordFeedback); return;
        }
        if (installing)
        {
            Height = Math.Min(optionsHeight, MaxHeight);
            Message(UiText.Get("Setup.WaitCompletion"));
            Body.Children.Add(installationStage); Body.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 10, Margin = new(0, 12, 0, 12) }.Themed(Control.ForegroundProperty, "Accent"));
            Details();
            Note(UiText.Get("Setup.CancellationNotice")); return;
        }
        Message(step switch { 0 => UiText.Get("Setup.CheckingMedia"), 1 => InlineReviewReady ? UiText.Get("Setup.ReviewReady") : UiText.Get("Setup.ChooseOptions"), 2 => UiText.Get("Setup.ActionLabel") + installationKind + UiText.Get("Setup.ConfirmOptions"), _ => UiText.Get("Setup.Installed") });
        if (step == 0)
        {
            preparationProgress.IsIndeterminate = prepareMedia is null;
            Body.Children.Add(preparationProgress); Body.Children.Add(new Expander { Header = UiText.Get("Setup.MediaDetails"), Content = preparationFile });
            Note(UiText.Get("Setup.ReadingMedia")); next.Content = UiText.Get("Setup.RetryCheck");
        }
        else if (step == 1)
        {
            Height = Math.Min(optionsHeight, MaxHeight);
            var current = InlineReviewReady ? reviewedLocation : CurrentInstallation(ApplicationId);
            var retainRegistered = deploymentProfile.PreservesInstallationConfiguration && current is not null;
            if (retainRegistered)
            {
                selectedProgram = Directory.GetParent(Directory.GetParent(current!.ReleaseRoot)!.FullName)!.FullName;
                enteredData = current.DataRoot;
            }
            if (InlineReviewReady)
            {
                selectedProgram = reviewed!.ProgramRoot ?? selectedProgram;
                enteredData = reviewed.DataRoot;
            }
            ShowBuildIdentity(current, media);
            installationKind = current is null ? UiText.Get("Setup.Install") : current.LifecycleState != "Installed" ? UiText.Get("Setup.Reinstall") : current.SourceCommit == media?.SourceCommit ? UiText.Get("Setup.Repair") : UiText.Get("Setup.Update");
            if (InlineReviewReady)
            {
                Section(UiText.Get("Setup.Impact"));
                Note(current?.LifecycleState == "Purged" ? UiText.Get("Setup.DataPurged")
                    : UiText.Get("Setup.ActionLabel") + installationKind + UiText.Get("Setup.KeepStation"));
                Note(UiText.Get("Setup.RequiresWindowsAdmin"));
                Summary(UiText.Get("Setup.AutoStartShort"), reviewed!.Shortcuts!.AutoStart ? UiText.Get("Common.Enabled") : UiText.Get("Common.Close"));
                foreach (var component in components.Where(c => c.Prerequisites.Length > 0)) Note(UiText.Get("Setup.Dependencies") + string.Join("；", component.Prerequisites));
            }
            Section(UiText.Get("Setup.Location"));
            // New SM stations use independent D-drive locations. Existing deployments
            // and explicitly reviewed/user-entered paths retain their original authority.
            program = InstallationFolder(UiText.Get("Setup.ProgramDirectory"), selectedProgram.Length > 0 ? selectedProgram : reviewed?.ProgramRoot ?? (current is null ? (deploymentProfile.UsesStrictWindowsStorage ? InstallationIdentity.DefaultProgramRoot : WindowsStoragePaths.ProgramRoot("CainiaoSupplyStation")) : Directory.GetParent(Directory.GetParent(current.ReleaseRoot)!.FullName)!.FullName), retainRegistered, InlineReviewReady);
            data = InstallationFolder(UiText.Get("Setup.DataDirectory"), enteredData ?? reviewed?.DataRoot ?? current?.DataRoot ?? (deploymentProfile.UsesStrictWindowsStorage ? InstallationIdentity.DefaultDataRoot : Path.Combine(WindowsDeploymentSetup.MachineRoot, "data")), retainRegistered, InlineReviewReady);
            if (retainRegistered) Note(UiText.Get("Setup.MaintainLocation"));
            startMenu = new CheckBox { Content = UiText.Get("Setup.StartMenuShortcut"), IsChecked = selectedStartMenu };
            desktopIcon = new CheckBox { Content = UiText.Get("Setup.DesktopShortcut"), IsChecked = selectedDesktop };
            var shortcuts = new StackPanel(); shortcuts.Children.Add(startMenu); shortcuts.Children.Add(desktopIcon);
            if (deploymentProfile.SupportsAutoStart)
            {
                if (!autoStartInitialized)
                {
                    autoStartInitialized = true;
                    if (current?.LifecycleState == "Installed")
                    {
                        try { selectedAutoStart = DeploymentShortcuts.ReadAutoStart(current, DeploymentShortcuts.CurrentUserStartupDirectory()); }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or System.Runtime.InteropServices.COMException)
                        {
                            selectedAutoStart = current.Shortcuts?.AutoStart == true;
                            autoStartReadIssue = UiText.Get("Setup.AutostartUnavailable");
                            System.Diagnostics.Trace.TraceWarning("Cannot read startup shortcut: {0}", error);
                        }
                    }
                }
                startMenu.IsEnabled = desktopIcon.IsEnabled = !InlineReviewReady;
                startMenu.MinHeight = desktopIcon.MinHeight = 44;
                autoStart = new CheckBox { Content = UiText.Get("Setup.AutoStart"), IsChecked = selectedAutoStart, MinHeight = 44,
                    IsEnabled = !InlineReviewReady, ToolTip = UiText.Get("Setup.AutoStartHelp") };
                AutomationProperties.SetAutomationId(autoStart, "Install.AutoStart");
                shortcuts.Children.Add(autoStart);
                if (autoStartReadIssue is { } issue)
                    shortcuts.Children.Add(new TextBlock { Text = issue, TextWrapping = TextWrapping.Wrap }.Themed(TextBlock.ForegroundProperty, "Warning"));
                var advanced = new Expander
                {
                    Header = new Border
                    {
                        MinHeight = 44,
                        Background = Brushes.Transparent,
                        Child = new TextBlock { Text = UiText.Get("Setup.AdvancedShortcuts"), VerticalAlignment = VerticalAlignment.Center }
                    },
                    IsExpanded = advancedExpanded,
                    Content = shortcuts,
                    MinHeight = 44,
                    Margin = new(0, 16, 0, 8)
                };
                AutomationProperties.SetName(advanced, UiText.Get("Setup.Advanced"));
                advanced.Expanded += (_, _) => advancedExpanded = true;
                advanced.Collapsed += (_, _) => advancedExpanded = false;
                Body.Children.Add(advanced);
            }
            else { Section(UiText.Get("Setup.Settings")); Row(UiText.Get("Setup.Shortcuts"), shortcuts); }
            if (deploymentProfile.RequiresBundledCameraDependencies)
                Note(UiText.Get("Setup.BundledDrivers"));
            if (current is not null && !InlineReviewReady)
            {
                if (current.LifecycleState == "Purged")
                {
                    Message(UiText.Get("Setup.PurgedReinstall"));
                    Note(UiText.Get("Setup.PurgedNotice"));
                }
                else
                {
                    Message(UiText.Get("Setup.ActionLabel") + installationKind + UiText.Get("Setup.KeepExisting"));
                    Note(UiText.Get("Setup.ThisOperation") + installationKind + UiText.Get("Setup.ReleaseBeforeInstall") +
                        (current.LifecycleState != "Installed" ? UiText.Get("Setup.ReinstallKeepsRecords") : ""));
                }
            }
            if (preflightError.Length != 0)
            {
                var detail = new TextBox
                {
                    Text = preflightError,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 140,
                    Margin = new(0, 8, 0, 0)
                };
                AutomationProperties.SetName(detail, UiText.Get("Setup.Preflight"));
                Body.Children.Add(new Expander { Header = UiText.Get("Setup.ThisPreflight"), IsExpanded = true, Content = detail, Margin = new(0, 12, 0, 0) });
            }
            next.Content = InlineReviewReady ? UiText.Get("Setup.Install") : UiText.Get("Common.Next");
        }
        else if (step == 2)
        {
            var r = reviewed!;
            Section(UiText.Get("Setup.Location")); Summary(UiText.Get("Setup.ProgramDirectory"), r.ProgramRoot!); Summary(UiText.Get("Setup.DataDirectory"), r.DataRoot);
            Section(UiText.Get("Setup.Settings"));
            Summary(UiText.Get("Setup.StartMenu"), r.Shortcuts!.StartMenu ? UiText.Get("Setup.CreateShortcut") : UiText.Get("Setup.NoShortcut")); Summary(UiText.Get("Setup.Desktop"), r.Shortcuts.Desktop ? UiText.Get("Setup.CreateShortcut") : UiText.Get("Setup.NoShortcut"));
            if (deploymentProfile.SupportsAutoStart) Summary(UiText.Get("Setup.AutoStartShort"), r.Shortcuts.AutoStart ? UiText.Get("Common.Enabled") : UiText.Get("Common.Close"));
            Note(CurrentInstallation(ApplicationId)?.LifecycleState == "Purged" ? UiText.Get("Setup.PurgedData") : UiText.Get("Setup.CloseFirst"));
            foreach (var component in components.Where(c => c.Prerequisites.Length > 0)) Note(UiText.Get("Setup.Dependencies") + string.Join("；", component.Prerequisites));
            Note(UiText.Get("Setup.Authority")); next.Content = UiText.Get("Setup.Install");
        }
        else
        {
            Height = Math.Min(optionsHeight, MaxHeight);
            if (needsFirstConfiguration == true)
            { Section(UiText.Get("Setup.FirstUse")); Note(BusinessDesktopCatalog.For(ApplicationId).FirstUseGuidance); }
            else Note(needsFirstConfiguration == false ? UiText.Get("Setup.FoundConfig") : configurationMessage.Length > 0 ? configurationMessage : UiText.Get("Setup.FirstConfiguration"));
            var canRun = ParcelStartupServices.IsRuntimeAccount(installed!);
            if (languageWarning is not null) Note(languageWarning);
            runAfter = new CheckBox { Content = UiText.Get("Setup.OpenAfter"), IsChecked = canRun && !reopenedInstallation, IsEnabled = canRun, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 16, 12, 0) };
            Actions.Children.Insert(0, runAfter); if (!canRun) Note(UiText.Get("Setup.UseOriginalAccount")); next.Content = UiText.Get("Common.Finish");
        }
    }
    void Details(bool historical = false)
    {
        var content = new StackPanel();
        var original = installationDetails.Count == 0 ? UiText.Get("Setup.NoStageMessage") : string.Join("\n", installationDetails);
        if (historical)
        {
            content.Children.Add(new TextBlock { Text = UiText.Get("Setup.PreviousMessages"), FontSize = UiTypography.Label, FontWeight = UiTypography.Emphasis, Margin = new(0, 0, 0, 6) });
            content.Children.Add(new TextBlock { Text = original.Replace(UiText.Get("Setup.WindowsConfirm"), UiText.Get("Setup.WaitForAuthorization"), StringComparison.Ordinal).Replace(UiText.Get("Setup.Authorizing"), UiText.Get("Setup.WaitForAuthorization"), StringComparison.Ordinal), FontSize = UiTypography.Label, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Text = UiText.Get("Setup.PastAuthorization"), TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label, Margin = new(0, 8, 0, 12) });
            content.Children.Add(new TextBlock { Text = UiText.Get("Setup.Diagnostics"), FontSize = UiTypography.Label, FontWeight = UiTypography.Emphasis, Margin = new(0, 0, 0, 6) });
            content.Children.Add(new TextBox { Text = failureMessage, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label, MinHeight = 0, MaxHeight = 100, BorderThickness = new(0), Background = Brushes.Transparent, Padding = new(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            content.Children.Add(new Expander { Header = UiText.Get("Setup.RawMessage"), FontSize = UiTypography.Label, Margin = new(0, 12, 0, 0), Content = new TextBox { Text = original + "\n" + failureMessage, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label, MinHeight = 0, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            Body.Children.Add(new Expander { Header = UiText.Get("Setup.Details"), Content = new Border { CornerRadius = new(4), Padding = new(12), Margin = new(0, 8, 0, 0), Child = content }.Themed(Border.BackgroundProperty, "Soft"), IsExpanded = false });
            return;
        }
        installationDetailView = new TextBox { Text = original, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 0, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        content.Children.Add(installationDetailView);
        content.Children.Add(new TextBlock { Text = failureMessage, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        Body.Children.Add(new Expander { Header = UiText.Get("Setup.ViewDetails"), Content = content, IsExpanded = failed || outcomeUnknown, Margin = new(0, 12, 0, 0) });
    }
    Task OpenRecordDirectory()
    {
        try
        {
            var directory = installationRecordDirectory ?? throw new IOException(UiText.Get("Setup.RecordsUnavailable"));
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(UiText.Get("Setup.RecordsError") + directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            recordFeedback.ToolTip = error.Message;
            recordFeedback.Text = (error is DirectoryNotFoundException ? UiText.Get("Setup.RecordsMissing") : UiText.Get("Setup.OpenRecordsFailed") + ChineseInterfaceText.ParameterMessage(error.Message))
                + (outcomeUnknown ? UiText.Get("Setup.OutcomeStillUnknown") : UiText.Get("Setup.ViewFailureDetails"));
        }
        return Task.CompletedTask;
    }
    void Stage(WindowsInstallationStage value)
    {
        if (!installing) return;
        installationStage.Text = value switch
        {
            WindowsInstallationStage.WaitingForAuthorization => UiText.Get("Setup.Authorizing"),
            WindowsInstallationStage.CheckingInstallation => UiText.Get("Setup.CheckingClosed"),
            WindowsInstallationStage.CopyingFiles => UiText.Get("Setup.Copying"),
            WindowsInstallationStage.InstallingCameraRuntime => UiText.Get("Setup.CameraRuntime"),
            WindowsInstallationStage.PreparingConfiguration => UiText.Get("Setup.PreparingData"),
            WindowsInstallationStage.InstallingComponents => UiText.Get("Setup.InstallingComponents"),
            WindowsInstallationStage.RegisteringManagementService => UiText.Get("Setup.ManagementService"),
            WindowsInstallationStage.CommittingInstallation => UiText.Get("Setup.Committing"),
            WindowsInstallationStage.CreatingShortcuts => UiText.Get("Setup.CreatingShortcuts"),
            WindowsInstallationStage.RegisteringApplication => UiText.Get("Setup.Registering"),
            _ => UiText.Get("Setup.CheckingResult")
        };
        var authorizing = value == WindowsInstallationStage.WaitingForAuthorization;
        installationStage.SetResourceReference(TextBlock.ForegroundProperty, authorizing ? "Warning" : "Ink");
        installationStage.SetResourceReference(TextBlock.BackgroundProperty, authorizing ? "WarningBackground" : "Surface");
        installationStage.Padding = authorizing ? new(12) : new(0);
        AutomationProperties.SetLiveSetting(installationStage, authorizing ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
        installationDetails.Add(installationStage.Text);
        if (installationDetailView is not null) installationDetailView.Text = string.Join("\n", installationDetails);
    }
    protected override void OperationFailed(Exception error)
    {
        if (error is MaintenanceCancelledException)
        {
            if (outcomeUnknown) { Render(); Message(error.Message); return; }
            installing = false; failed = false; reviewed = null; step = media is null ? 0 : 1;
            Render(); Message(error.Message); return;
        }
        if (outcomeUnknown && originalInspection?.Status == "CommittedNeedsSystemRepair")
        {
            installing = false; failed = false; failureMessage = error.Message;
            Render(); return;
        }
        if (outcomeUnknown)
        {
            installing = false; Render();
            Message(UiText.Get("Setup.ResumeFailed") + ChineseInterfaceText.ParameterMessage(error.Message) + UiText.Get("Setup.KeepOriginal"));
            return;
        }
        if (step == 3 && installed is not null)
        {
            string details;
            try { details = DiagnosticText.Protect(error.Message); }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { details = UiText.Get("Setup.StartDiagnosticsUnavailable"); }
            Message(UiText.Get("Setup.OpenFailed") + details);
            Status.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
            return;
        }
        if (step == 2 || installing)
        {
            // Only inspection of the original operation can resolve an unknown installation result.
            installing = false; outcomeUnknown = outcomeUnknown || error is InstallationOutcomeUnknownException; failed = !outcomeUnknown;
            failureMessage = error.Message; Render();
        }
        else
        {
            if (step == 1)
            {
                CaptureInputs();
                try { preflightError = DiagnosticText.Protect(error.Message); }
                catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { preflightError = UiText.Get("Setup.PreflightUnavailable"); }
                Render(); Message(UiText.Get("Setup.PreflightFailed")); Status.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
                return;
            }
            if (step == 0) { next.Visibility = Visibility.Visible; next.Content = UiText.Get("Setup.RetryCheck"); cancel.Content = UiText.Get("Common.Cancel"); cancel.IsEnabled = true; }
            Message(UiText.Get("Common.IncompletePrefix") + ChineseInterfaceText.ParameterMessage(error.Message)); Status.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        }
    }
    protected override void OperationCompleted()
    {
        if (outcomeUnknown && IsVisible) inspect.Focus();
        else if (InlineReviewReady && IsVisible)
        {
            InstallationOptionsScroll?.ScrollToHome();
            back.Focus();
        }
    }
    async Task Next()
    {
        if (outcomeUnknown) return; // Never replay a transaction whose terminal receipt is untrusted.
        if (failed) { failed = false; step = 1; reviewed = null; Render(); return; }
        if (step == 0)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(UiText.Get("Setup.WindowsRequired"));
            var environment = WindowsClientCompatibility.Capture(); if (!environment.Assessment.CanStart) throw new PlatformNotSupportedException(environment.Assessment.Reason);
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
            if (prepareMedia is not null)
            {
                using var cancellation = new CancellationTokenSource(); preparing = cancellation;
                next.Visibility = Visibility.Collapsed; cancel.Content = UiText.Get("Setup.CancelPreparation"); Actions.IsEnabled = true; cancel.IsEnabled = true;
                var progress = new Progress<InstallationPreparationProgress>(value => { Message(value.Stage); preparationProgress.Value = value.Percent; preparationFile.Text = value.File; });
                try { root = await prepareMedia(progress, cancellation.Token); } finally { preparing = null; Actions.IsEnabled = false; cancel.IsEnabled = true; }
            }
            media = await Task.Run(() => DeploymentMedia.Inspect(root, ApplicationId));
            VersionLabel.Text = UiText.Get("Setup.VersionPrefix") + BusinessDesktopCatalog.For(ApplicationId).InstallerVersionPrefix + "V" + Wire.Decode<ApplicationRelease>(File.ReadAllText(Path.Combine(media.Root, "desktop", "application-release.json"))).BusinessVersion + UiText.Get("Setup.VersionSuffix");
            VersionLabel.Visibility = Visibility.Visible; newStation = SystemParameterDefaults.InitialStationId; step = 1;
            if (deploymentProfile.UsesInstallationContinuation)
            {
                var original = await Task.Run(() => DiscoverInstallation(ApplicationId, media.SourceCommit));
                if (original is not null) ApplyOriginal(original);
            }
            Render();
        }
        else if (step == 1 && !InlineReviewReady)
        {
            reopenedInstallation = false;
            CaptureInputs(); preflightError = ""; Render(); var current = CurrentInstallation(ApplicationId);
            var selectedAccount = current?.RuntimeAccountSid ?? MachineRuntimePolicy.SharedUsersSid;
            var configPath = current?.ConfigurationDirectory ?? Path.Combine(media!.Root, "app", "config");
            if (deploymentProfile.PreservesInstallationConfiguration)
            {
                if (preparedConfigurationDirectory is { } previous && Directory.Exists(previous)) { File.Delete(Path.Combine(previous, "business.json")); Directory.Delete(previous); }
                preparedConfigurationDirectory = Path.Combine(Path.GetTempPath(), "ipc-install-seed-" + Wire.Id());
                configPath = await Task.Run(() => ParcelStartupServices.PrepareInstallationConfiguration(current, preparedConfigurationDirectory, enteredData));
            }
            else if (current is null) { var seedDirectory = Path.Combine(Path.GetTempPath(), "ipc-new-site-" + Wire.Id()); await Task.Run(() => ParcelStartupServices.CreateSiteSeed(media!, seedDirectory, newStation, RunMode.Production)); configPath = seedDirectory; }
            var selected = media!.Files.Keys.Where(p => p.StartsWith("offline-extensions/", StringComparison.Ordinal) && p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).Select(p => Path.Combine(media.Root, p)).ToArray();
            var preview = await RunMaintenance(UiText.Get("Setup.CheckOptionsDetails") + media!.Root + UiText.Get("Setup.ProgramDetails") + selectedProgram + UiText.Get("Setup.DataDetails") + enteredData + UiText.Get("Setup.PreflightCancelDetail"), (progress, token) =>
            {
                try
                {
                    progress.Report(new(MaintenanceStage.Verifying, null, null, true));
                    var candidate = Review(media!, selectedAccount, enteredData!, configPath, null, selected, false, selectedProgram, token) with { Shortcuts = new(selectedStartMenu, selectedDesktop, selectedAutoStart) };
                    var review = DeploymentComponentReviewing.Inspect(candidate, token); token.ThrowIfCancellationRequested(); return Task.FromResult((candidate, review));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw new MaintenanceCancelledException(UiText.Get("Setup.PreflightStopped")); }
            }, readOnly: true);
            var reviewedCandidate = preview.candidate;
            var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(reviewedCandidate.ConfigurationSource, "business.json")));
            // Publish UI readiness only after every preview/seed check succeeds.
            // The execution path still rechecks the original registration CAS and owners.
            if (deploymentProfile.UsesInlineInstallationReview) HideCompletedMaintenanceProgress();
            reviewedLocation = current;
            reviewed = reviewedCandidate;
            components = preview.review;
            step = deploymentProfile.UsesInlineInstallationReview ? 1 : 2;
            Render();
        }
        else if (step == 2 || InlineReviewReady)
        {
            step = 2;
            optionsHeight = Height;
            installationRecordDirectory = Path.GetDirectoryName(WindowsDeploymentSetup.Registration(reviewed!.ApplicationId));
            installing = true; installationDetails.Clear(); installationStage.Text = UiText.Get("Setup.PreparingRequest"); Render();
            var stages = new Progress<WindowsInstallationStage>(StageOnDispatcher);
            installed = await RunMaintenance(UiText.Get("Setup.DeployDetails") + reviewed!.MediaRoot + UiText.Get("Setup.ProgramDetails") + (reviewed.ProgramRoot ?? TrustedProgramRoots.Default) + UiText.Get("Uninstall.DataPath") + reviewed.DataRoot,
                (progress, token) => Install(reviewed, stages, progress, token));
            installing = false;
            try { var classification = await ParcelStartupServices.Maintenance(new(installed.ConfigurationDirectory, installed.DataRoot, null, null)).ClassifyAsync(); needsFirstConfiguration = classification.Readable ? !classification.ExistingConfiguration : null; if (!classification.Readable) configurationMessage = UiText.Get("Setup.ConfigurationUnknown"); }
            catch (Exception error) { configurationMessage = UiText.Get("Setup.ConfigurationFailure") + error.Message + UiText.Get("Setup.DevicesStopped"); }
            step = 3; Render();
        }
        else
        {
            if (runAfter.IsChecked == true)
            {
                if (!ParcelStartupServices.IsRuntimeAccount(installed!)) throw new InvalidOperationException(UiText.Get("Setup.SignInRuntime"));
                _ = LaunchDesktop(installed!); Launched = true;
            }
            Busy = false; Close();
        }
    }
    async Task InspectOriginal()
    {
        if (!outcomeUnknown) return;
        if (reviewed is null)
        {
            var original = await Task.Run(() => DiscoverInstallation(ApplicationId, media!.SourceCommit));
            if (original is not null) { ApplyOriginal(original); Render(); }
            else Message(UiText.Get("Setup.MissingOriginalIdentity"));
            return;
        }
        var result = await Task.Run(() => InspectInstallation(reviewed));
        originalInspection = result;
        if (result.Status == "Completed" && result.Location is { } location)
        { ResolveKnownMaintenanceOutcome(); installed = location; outcomeUnknown = false; installing = false; step = 3; Render(); }
        else if (result.Status == "NotCommitted") { ResolveKnownMaintenanceOutcome(); Render(); Message(result.Detail); }
        else if (result.Status is "Failed" or "Superseded")
        { ResolveKnownMaintenanceOutcome(); outcomeUnknown = false; reviewed = null; failed = false; step = 1; Render(); Message(result.Detail + UiText.Get("Setup.VerifyCurrentOptions")); }
        else { Render(); Message(result.Detail + UiText.Get("Setup.LastOperation") + reviewed.OperationId); }
    }
    void ApplyOriginal(InstallationContinuation original)
    {
        reopenedInstallation = true; reviewed = original.Request; originalInspection = original.Inspection;
        installationRecordDirectory = Path.GetDirectoryName(original.OperationFile);
        installationDetails.Clear();
        installationDetails.Add(original.Inspection.Detail);
        if (reviewed is not null)
            installationDetails.Add(UiText.Get("Setup.OriginalOperation") + reviewed.OperationId + UiText.Get("Setup.SourceCommit") + reviewed.SourceCommit + UiText.Get("Setup.ProgramFolderDetails") + reviewed.ProgramRoot + UiText.Get("Setup.DataFolderDetails") + reviewed.DataRoot);
        if (original.Inspection is { Status: "Completed", Location: { } location })
        { installed = location; outcomeUnknown = false; step = 3; }
        else { outcomeUnknown = true; step = 1; }
    }
    void StageOnDispatcher(WindowsInstallationStage value) => Dispatcher.BeginInvoke(() => Stage(value));
}
