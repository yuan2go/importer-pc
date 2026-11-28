using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Assembly;
using Ipc.Platform;

namespace Ipc.Desktop;

sealed class ProductMaintenanceWindow : StartupWorkflowWindow
{
    internal Func<string> RegistrationIdentity { get; set; } = () => Wire.FileHash(WindowsDeploymentSetup.Registration(ParcelStartupServices.ApplicationId));
    internal Func<string> RegistrationScope { get; set; } = () =>
    {
        var current = WindowsDeploymentSetup.Current(ParcelStartupServices.ApplicationId) ?? throw new InvalidDataException(UiText.Get("Uninstall.Missing"));
        return Wire.Hash(Wire.Encode(current with { LifecycleState = "", MaintenanceOperation = null }));
    };
    internal Func<string, bool, IProgress<MaintenanceProgress>?, CancellationToken, Task<ProgramUninstallResult>> ExecuteUninstall { get; set; } = ParcelStartupServices.UninstallAsync;
    internal Func<ProgramUninstallResult> InspectUninstall { get; set; } = ParcelStartupServices.InspectUninstall;
    readonly RadioButton keepData = new() { Content = UiText.Get("Uninstall.KeepData"), GroupName = "StationData", IsChecked = true, MinHeight = 44 };
    readonly RadioButton deleteData = new() { Content = UiText.Get("Uninstall.DeleteData"), GroupName = "StationData", MinHeight = 44 };
    string? originalRegistration;
    string? originalScope;

    // Installer cleanup uses the protected installation inventory, not new media.
    internal ProductMaintenanceWindow(DeploymentMedia media) : this()
        => ExecuteUninstall = (hash, delete, progress, token) => ParcelStartupServices.UninstallAsync(hash, delete, progress, token, media);
    public ProductMaintenanceWindow() : base(UiText.Get("Uninstall.WindowTitle"), applicationId: Ipc.Assembly.ParcelStartupServices.ApplicationId)
    {
        Width = 680; Height = 420; MinHeight = 380;
        RenderOptions();
        Loaded += (_, _) =>
        {
            try { originalScope ??= RegistrationScope(); originalRegistration ??= RegistrationIdentity(); }
            catch (Exception error) { OperationFailed(error); }
        };
    }

    void RenderOptions()
    {
        Title = UiText.Get("Uninstall.WindowTitle"); WorkflowHeading.Text = UiText.Get("Uninstall.Heading");
        Body.Children.Clear(); Actions.Children.Clear(); SessionActions.Children.Clear();
        Body.Children.Add(keepData);
        var retained = new TextBlock { Text = UiText.Get("Uninstall.KeepDetail"), FontSize = UiTypography.Label, TextWrapping = TextWrapping.Wrap, Margin = new(24, 0, 0, 16) };
        retained.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); Body.Children.Add(retained);
        Body.Children.Add(deleteData);
        var warning = new TextBlock { Text = UiText.Get("Uninstall.DeleteDetail"), FontSize = UiTypography.Label, TextWrapping = TextWrapping.Wrap, Margin = new(24, 0, 0, 8) };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "Warning"); Body.Children.Add(warning);
        // An unknown outcome routes this same action to inspection below. Only
        // RunMaintenance may start an actual uninstall after that guard clears.
        var uninstall = Action(UiText.Get("Uninstall.Action"), Uninstall, readOnly: true);
        AutomationProperties.SetAutomationId(uninstall, "Uninstall.Execute");
        CloseAction(UiText.Get("Common.Cancel"));
        Message(UiText.Get("Uninstall.CloseFirst"));
    }

    async Task Uninstall()
    {
        if (UnknownOutcome) { await InspectResult(); return; }
        RequireOriginalScope();
        var hash = RegistrationIdentity();
        if (originalRegistration is null || originalRegistration != hash)
            throw new InvalidOperationException(UiText.Get("Uninstall.RecordChanged"));
        var deletionRequested = deleteData.IsChecked == true;
        var context = deletionRequested ? UiText.Get("Uninstall.DeletePreview") : UiText.Get("Uninstall.KeepPreview");
        var result = await RunMaintenance(context, (progress, token) => ExecuteUninstall(hash, deletionRequested, progress, token));
        // The operation itself changes registration. A verified result may retry
        // that retired installation; an unconfirmed result must first be inspected.
        RefreshOriginalRegistration();
        ShowResult(result);
    }

    async Task InspectResult()
    {
        RequireOriginalScope();
        var result = await Task.Run(InspectUninstall);
        RefreshOriginalRegistration();
        if (result.Removal?.Status is "Installed" or "NotCommitted" or "NotCommittedNeedsClosure")
        {
            ResolveKnownMaintenanceOutcome(); RenderOptions();
            Message(UiText.Get("Uninstall.StillInstalled")); return;
        }
        if (result.Removal?.Status == "Unknown") throw new InstallationOutcomeUnknownException(UiText.Get("Uninstall.PreviousUnknown"));
        ResolveKnownMaintenanceOutcome(); ShowResult(result with { DeleteData = result.DeleteData || deleteData.IsChecked == true });
    }

    void RequireOriginalScope()
    {
        if (originalScope is null || RegistrationScope() != originalScope)
            throw new InvalidOperationException(UiText.Get("Uninstall.RecordChanged"));
    }

    void RefreshOriginalRegistration()
    {
        RequireOriginalScope();
        var hash = RegistrationIdentity();
        RequireOriginalScope();
        originalRegistration = hash;
    }

    void ShowResult(ProgramUninstallResult result)
    {
        Title = UiText.Get("Uninstall.ResultTitle"); WorkflowHeading.Text = UiText.Get("Uninstall.Result");
        HideCompletedMaintenanceProgress(); // The receipt below owns the result; a finished call is not proof that cleanup completed.
        Body.Children.Clear(); Actions.Children.Clear(); SessionActions.Children.Clear();
        var removal = result.Removal;
        var deleted = result.DataDeletion?.Status == "Purged";
        var baseComplete = deleted || removal?.Status == "RemovedDataRetained" && !result.DeleteData;
        var complete = baseComplete && result.Assets?.Status == "Completed";
        var dataState = deleted ? UiText.Get("Uninstall.DataDeleted") : UiText.Get("Uninstall.DataKept");
        var text = baseComplete && result.Assets?.Status == "RestartRequired" ? dataState + UiText.Get("Uninstall.MaintenanceRestart")
            : baseComplete && !complete ? dataState + UiText.Get("Uninstall.FilesKept")
            : deleted ? UiText.Get("Uninstall.CompleteWithPurge") : result.DataDeletion is not null ? UiText.Get("Uninstall.DataBusy")
            : removal?.Status == "RestartRequired" ? UiText.Get("Uninstall.Restart")
            : removal?.Status == "RemovedDataRetained" ? result.DeleteData ? UiText.Get("Uninstall.PurgePending") : UiText.Get("Uninstall.CompleteKept")
            : UiText.Get("Uninstall.FilesBusy");
        Message(text);
        var details = new TextBox
        {
            Text = (removal is null ? "" : UiText.Get("Uninstall.ProgramPath") + removal.ReleaseRoot + (deleted ? "" : UiText.Get("Uninstall.DataPath") + string.Join("；", removal.Retained)) + UiText.Get("Uninstall.ReceiptPath") + removal.OperationFile + "\n") +
                (result.DataDeletion is null ? "" : UiText.Get("Uninstall.PurgeReceipt") + result.DataDeletion.OperationFile + "\n") +
                (result.Assets is null ? "" : UiText.Get("Uninstall.CacheReceipt") + result.Assets.OperationFile + "\n") +
                UiText.Get("Uninstall.Remaining") + string.Join("\n", (result.DataDeletion?.Remaining ?? []).Concat(removal?.RemainingFiles ?? []).Concat(result.Assets?.Remaining ?? [])) +
                UiText.Get("Uninstall.Unverified") + string.Join("\n", (result.Assets?.Retained ?? []).Select(x => x.Root + "：" + x.Reason)),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 160
        };
        Body.Children.Add(new Expander { Header = UiText.Get("Uninstall.Details"), Content = details });
        if (result.Assets is { DetailsTruncated: true } assets)
            Note(UiText.Get("Uninstall.RemainingPrefix") + assets.TotalRemaining + UiText.Get("Uninstall.FolderCount") + assets.TotalRetained + UiText.Get("Uninstall.RetainedSummary"));
        Note(result.DeleteData ? UiText.Get("Uninstall.ScopePurge") : UiText.Get("Uninstall.ScopeKeep"));
        if (!complete && (!baseComplete || result.Assets?.Status != "RestartRequired"))
        {
            deleteData.IsChecked = result.DeleteData; keepData.IsChecked = !result.DeleteData;
            Action(UiText.Get("Uninstall.Retry"), Uninstall, readOnly: true);
        }
        CloseAction(UiText.Get("Common.Close"));
    }

    void CloseAction(string caption)
    {
        var close = new Button { Content = caption, IsCancel = true, MinHeight = 44, MinWidth = 100, Margin = new(0, 12, 10, 0), Padding = new(12, 6, 12, 6) };
        close.Click += (_, _) => Close(); Actions.Children.Add(close);
    }

    protected override void OperationFailed(Exception error)
    {
        base.OperationFailed(error);
        if (UnknownOutcome)
            Status.Text = UiText.Get("Uninstall.UnknownNoReplay") + ChineseInterfaceText.ParameterMessage(error.Message);
    }
}
