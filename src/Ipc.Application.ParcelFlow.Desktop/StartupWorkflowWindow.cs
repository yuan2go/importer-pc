using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
using Microsoft.Win32;

namespace Ipc.Desktop;

public abstract partial class StartupWorkflowWindow : Window
{
    protected readonly StackPanel Body = new();
    protected readonly ContentControl WorkflowBody = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    protected readonly WrapPanel Actions = new() { HorizontalAlignment = HorizontalAlignment.Right };
    protected ScrollViewer? InstallationOptionsScroll;
    protected readonly DockPanel FooterHost = new();
    protected readonly WrapPanel SessionActions = new() { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(12, 0, 0, 0) };
    protected readonly StackPanel AuthorizationGate = new() { Visibility = Visibility.Collapsed, Margin = new(8, 16, 8, 16), MaxWidth = 570, HorizontalAlignment = HorizontalAlignment.Left };
    protected readonly TextBlock WorkflowHeading = new() { FontSize = UiTypography.Position, FontWeight = UiTypography.Emphasis };
    protected readonly TextBlock WorkflowBusinessName = new TextBlock() { FontSize = UiTypography.Label, Margin = new(0, 4, 0, 0), Visibility = Visibility.Collapsed }.Themed(TextBlock.ForegroundProperty, "Muted");
    protected StackPanel Form;
    protected readonly TextBlock Status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 12) };
    protected readonly TextBlock InstallationHeading = new() { Text = UiText.Get("Setup.WorkflowTitle"), FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis };
    protected readonly TextBlock VersionLabel = new() { FontSize = UiTypography.Body, Margin = new(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    protected readonly TextBox BuildIdentityDetails = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    protected void ShowBuildIdentity(DeploymentLocation? registered, DeploymentMedia? target = null)
    {
        var running = WindowsDeploymentSetup.RunningBuild((System.Reflection.Assembly.GetEntryAssembly() ?? typeof(StartupWorkflowWindow).Assembly));
        var current = registered is null ? null : WindowsDeploymentSetup.RegisteredBuild(registered);
        var targetBuild = target is null ? null : WindowsDeploymentSetup.MediaBuild(target);
        BuildIdentityDetails.Text = UiText.Get("Setup.HostVersion") + running.BusinessVersion + "\n" + running.SourceCommit +
            UiText.Get("Setup.CurrentDeployment") + (current is null ? UiText.Get("Setup.Unregistered") : current.BusinessVersion + "\n" + current.SourceCommit + "\n" + current.Detail) +
            (targetBuild is null ? "" : UiText.Get("Setup.TargetMedia") + targetBuild.BusinessVersion + "\n" + targetBuild.SourceCommit + "\n" + targetBuild.Detail + UiText.Get("Setup.BuildTransition") + (current?.SourceCommit ?? UiText.Get("Setup.Unregistered")) + " → " + targetBuild.SourceCommit);
    }
    readonly TextBlock[] installationSteps = new TextBlock[3];
    protected void InstallationStep(int step)
    {
        for (var i = 0; i < installationSteps.Length; i++)
        {
            installationSteps[i].SetResourceReference(TextBlock.ForegroundProperty, i == step ? "Accent" : "Muted");
            installationSteps[i].FontWeight = i == step ? UiTypography.Emphasis : FontWeights.Normal;
            if (i == step) installationSteps[i].SetResourceReference(TextBlock.BackgroundProperty, "AccentBackground"); else installationSteps[i].Background = Brushes.Transparent;
            installationSteps[i].Padding = new(8, 12, 8, 12);
            installationSteps[i].FontSize = UiTypography.Label;
        }
    }
    protected bool Busy;
    protected bool UnknownOutcome;
    protected bool CloseAfterCancellation;
    protected StartupWorkflowWindow(string title, bool installation = false, string? applicationId = null)
    {
        applicationId ??= BusinessDesktopCatalog.Station.ApplicationId;
        Form = Body;
        ProductBrand.Apply(this); UiTypography.Apply(this);
        VersionLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Title = title; FontSize = UiTypography.Body; Width = 960; Height = 760; MinWidth = 680; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Background"); SetResourceReference(ForegroundProperty, "Ink");

        System.Windows.Automation.AutomationProperties.SetAutomationId(BuildIdentityDetails, "Maintenance.BuildIdentity");
        Loaded += (_, _) =>
        {
            if (BuildIdentityDetails.Text.Length != 0) return;
            try { ShowBuildIdentity(WindowsDeploymentSetup.Current(applicationId)); }
            catch (Exception error) { BuildIdentityDetails.Text = UiText.Get("Setup.HostVersion") + WindowsDeploymentSetup.RunningBuild((System.Reflection.Assembly.GetEntryAssembly() ?? typeof(StartupWorkflowWindow).Assembly)) + UiText.Get("Setup.CouldNotReadDeployment") + error.Message; }
        };
        if (installation)
        {
            var buildIdentity = new Expander { Header = UiText.Get("Setup.CopyVersion"), Content = BuildIdentityDetails, Margin = new(0, 4, 0, 4) };
            Width = 880; Height = 560; MinWidth = 600; MinHeight = 380; StartupAppearance.Apply(this);
            var layout = new Grid(); layout.ColumnDefinitions.Add(new() { Width = new(176) }); layout.ColumnDefinitions.Add(new());
            var rail = new StackPanel { Margin = new(16, 28, 16, 24) };
            rail.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Ipc.Desktop;component/Assets/cainiao.png")), Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Left });
            rail.Children.Add(new TextBlock { Text = UiText.Get("Startup.Product"), FontSize = UiTypography.Body, FontWeight = UiTypography.Emphasis, Margin = new(0, 16, 0, 16) });
            var stepLabels = BusinessDesktopCatalog.For(applicationId).UsesWorkstationSettingsFlow
                ? new[] { (UiText.Get("Setup.StepWelcome"), 0), (UiText.Get("Setup.StepInstall"), 1), (UiText.Get("Setup.StepFinished"), 2) }
                : new[] { (UiText.Get("Setup.StepOptions"), 0), (UiText.Get("Setup.StepConfirm"), 1), (UiText.Get("Setup.StepFinished"), 2) };
            foreach (var (label, index) in stepLabels)
            { installationSteps[index] = new TextBlock { Text = label, Margin = new(0, 12, 0, 12), TextWrapping = TextWrapping.Wrap }; rail.Children.Add(installationSteps[index]); }
            var sidebar = new Border { Child = rail }.Themed(Border.BackgroundProperty, "Soft"); layout.Children.Add(sidebar);
            var content = new Grid { Margin = new(28, 24, 28, 20) }; Grid.SetColumn(content, 1);
            content.RowDefinitions.Add(new() { Height = GridLength.Auto }); content.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); content.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var header = new StackPanel(); header.Children.Add(InstallationHeading); header.Children.Add(VersionLabel); header.Children.Add(buildIdentity); header.Children.Add(new ScrollViewer { Content = Status, MaxHeight = 48, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); header.Children.Add(CreateMaintenancePanel(compact: true));
            var options = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(options, 1);
            InstallationOptionsScroll = options;
            Actions.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetRow(Actions, 2);
            content.Children.Add(header); content.Children.Add(options); content.Children.Add(Actions); layout.Children.Add(content); Content = layout;
            SizeChanged += (_, _) => { layout.ColumnDefinitions[0].Width = new(ActualWidth < 760 ? 136 : 176); content.Margin = new(ActualWidth < 760 ? 20 : 28, 24, ActualWidth < 760 ? 20 : 28, 20); };
            Closing += (_, e) => { if (Busy) e.Cancel = true; }; return;
        }
        var grid = new Grid().Themed(Panel.BackgroundProperty, "Background");
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel(); WorkflowHeading.Text = title.Split(" · ")[0]; heading.Children.Add(WorkflowHeading);
        heading.Children.Add(WorkflowBusinessName);
        heading.Children.Add(new ScrollViewer { Content = Status, MaxHeight = 64, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var diagnostic = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        diagnostic.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source = Status });
        heading.Children.Add(CreateMaintenancePanel());
        // Full diagnostics remain selectable through the named tools menu.
        Status.ToolTip = diagnostic;
        WorkflowBody.Content = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var bodyHost = new Grid { Margin = new(20, 0, 20, 0) }; bodyHost.Children.Add(WorkflowBody); bodyHost.Children.Add(AuthorizationGate); Grid.SetRow(bodyHost, 1);
        FooterHost.Children.Add(Actions); Grid.SetRow(FooterHost, 2);
        FooterHost.Margin = new(20, 0, 20, 8);
        FooterHost.Themed(DockPanel.BackgroundProperty, "Surface");
        var identityButton = new ToggleButton { Content = UiText.Get("Setup.VersionInformation"), Margin = new(8, 0, 0, 0) };
        var identityCard = new Border { Child = BuildIdentityDetails, Width = 420, Margin = new(8) }.Themed(StyleProperty, "WorkstationCard");
        var identityPopup = new Popup { Child = identityCard, PlacementTarget = identityButton, Placement = PlacementMode.Bottom,
            HorizontalOffset = -300, StaysOpen = false, AllowsTransparency = true };
        identityPopup.SetBinding(Popup.IsOpenProperty, new Binding(nameof(ToggleButton.IsChecked)) { Source = identityButton, Mode = BindingMode.TwoWay });
        var identity = new Grid { VerticalAlignment = VerticalAlignment.Top };
        identity.Children.Add(identityButton);
        identity.Children.Add(identityPopup);
        var sessionHeader = new DockPanel { MinHeight = 56, Margin = new(20, 8, 20, 4) };
        DockPanel.SetDock(identity, Dock.Right);
        DockPanel.SetDock(SessionActions, Dock.Right);
        sessionHeader.Children.Add(identity);
        sessionHeader.Children.Add(SessionActions);
        sessionHeader.Children.Add(heading);
        grid.Children.Add(sessionHeader); grid.Children.Add(bodyHost); grid.Children.Add(FooterHost); Content = grid;
        Closing += (_, e) => { if (Busy) e.Cancel = true; };
    }
    protected Button Action(string label, Func<Task> action, bool readOnly = false)
    {
        var button = new Button { Content = label, MinHeight = 44, MinWidth = 100, Margin = new(0, 12, 10, 0), Padding = new(12, 6, 12, 6) };
        button.Click += async (_, _) =>
        {
            if (UnknownOutcome && !readOnly) { Message(UiText.Get("Setup.MaintenanceUnknown")); return; }
            await Run(action);
        }; Actions.Children.Add(button); return button;
    }
    protected async Task Run(Func<Task> action)
    {
        if (Busy) return; Busy = true; FooterHost.IsEnabled = false; SessionActions.IsEnabled = false; Actions.IsEnabled = false; Body.IsEnabled = false; WorkflowBody.IsEnabled = false;
        try { await action(); }
        catch (OperationCanceledException) when (CloseAfterCancellation) { Busy = false; Close(); }
        catch (Exception ex) { OperationFailed(ex); }
        finally { Busy = false; FooterHost.IsEnabled = true; SessionActions.IsEnabled = true; Actions.IsEnabled = true; Body.IsEnabled = true; WorkflowBody.IsEnabled = true; OperationCompleted(); }
    }
    protected virtual void OperationCompleted() { }
    protected virtual void OperationFailed(Exception error)
    {
        if (error is MaintenanceCancelledException) { Message(error.Message); return; }
        UnknownOutcome |= error is InstallationOutcomeUnknownException;
        Status.Text = UnknownOutcome ? UiText.Get("Setup.MaintenancePastUnknown") + ChineseInterfaceText.ParameterMessage(error.Message)
            : UiText.Get("Common.IncompletePrefix") + ChineseInterfaceText.ParameterMessage(error.Message) + UiText.Get("Setup.CheckReceipt");
        Status.SetResourceReference(TextBlock.ForegroundProperty, UnknownOutcome ? "Warning" : "Danger");
    }
    protected void Message(string text) { Status.Text = text; Status.Foreground = Foreground; }
    protected void Note(string text) => Form.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 8) });
    protected TextBox Field(string label, string value, Action<string> changed, bool readOnly = false)
    {
        Note(label); var box = new TextBox { Text = value, MinHeight = 44, IsReadOnly = readOnly, MaxLength = 65536, Margin = new(0, 0, 0, 4) };
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        box.TextChanged += (_, _) => changed(box.Text); Form.Children.Add(box); return box;
    }
    protected TextBox Folder(string label, string value)
    {
        var box = Field(label, value, _ => { });
        var browse = new Button { Content = UiText.Get("Setup.ChooseFolder"), MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        browse.Click += (_, _) => { var picker = new OpenFolderDialog { Title = label }; if (picker.ShowDialog(this) == true) box.Text = picker.FolderName; };
        Form.Children.Add(browse); return box;
    }
    protected void Choice<T>(string label, T selected, Action<T> changed) where T : struct, Enum
    {
        Note(label); var box = new ComboBox { ItemsSource = Enum.GetValues<T>(), ItemTemplate = ChineseChoiceTextConverter.CreateTemplate(), SelectedItem = selected, MinHeight = 36 };
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is T value) changed(value); }; Form.Children.Add(box);
    }
    protected static string? FilePick(Window owner, string title, string filter = "所有文件|*.*")
    { var picker = new OpenFileDialog { Title = title, Filter = filter }; return picker.ShowDialog(owner) == true ? picker.FileName : null; }
    protected void CompleteDialog() { Busy = false; DialogResult = true; }
}
