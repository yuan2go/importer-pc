using System.Windows;
using System.Windows.Controls;
using Ipc.Presentation.Wpf;
namespace Ipc.Desktop;

// A real startup surface precedes synchronous configuration/SQLite work. Its rows
// describe completed calls, never guessed device health or simulated percentages.
sealed class ParcelStartupWindow : Window
{
    readonly TextBlock title = new() { FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis, Text = UiText.Get("Startup.Starting") };
    readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12), Text = UiText.Get("Startup.Preparing") };
    readonly TextBox diagnostic = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 120 };
    readonly TextBox buildIdentity = new() { Name = "StartupBuildIdentity", IsReadOnly = true, FontSize = UiTypography.Label, Margin = new Thickness(0, 0, 0, 12) };
    readonly WrapPanel actions = new() { HorizontalAlignment = HorizontalAlignment.Right };
    readonly List<string> completed = [];
    Func<Task>? closeAction;
    bool closing;
    public bool AllowClose { get; set; }
    public ParcelStartupWindow()
    {
        Ipc.Application.ParcelFlow.Desktop.ProductBrand.Apply(this); UiTypography.Apply(this);
        Title = UiText.Get("Startup.Product"); Width = 760; Height = 520; MinWidth = 500; MinHeight = 360;
        Ipc.Application.ParcelFlow.Desktop.StartupAppearance.FitToWorkArea(this);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // Read the running binary, including the frozen SourceRevisionId used by packaging;
        // deployment machines do not need Git or a source checkout.
        var identity = Ipc.Platform.WindowsDeploymentSetup.RunningBuild(typeof(ParcelStartupWindow).Assembly);
        buildIdentity.Text = UiText.Format("Startup.BuildLabel", identity.BusinessVersion, identity.SourceCommit);
        buildIdentity.SetResourceReference(StyleProperty, "ReadonlyValue");
        buildIdentity.SetResourceReference(ForegroundProperty, "Muted");
        System.Windows.Automation.AutomationProperties.SetName(buildIdentity, UiText.Get("Startup.BuildIdentity"));
        var layout = new DockPanel { Margin = new Thickness(24) };
        DockPanel.SetDock(actions, Dock.Bottom); layout.Children.Add(actions);
        var body = new StackPanel(); body.Children.Add(title); body.Children.Add(detail); body.Children.Add(buildIdentity);
        diagnostic.MaxHeight = 400; body.Children.Add(diagnostic);
        layout.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = layout;
        System.Windows.Input.Keyboard.AddKeyDownHandler(this, (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; _ = RequestCloseAsync(); } });
        Closing += (_, e) =>
        {
            if (AllowClose) return;
            e.Cancel = true;
            // Leave WPF's Closing callback before the shared exit can close this window.
            Dispatcher.BeginInvoke(new Action(async () => await RequestCloseAsync()));
        };
    }
    async Task RequestCloseAsync()
    {
        if (AllowClose || closing || !actions.IsEnabled || closeAction is null) return;
        closing = true; actions.IsEnabled = false;
        try { await closeAction(); }
        catch (Exception error) { diagnostic.Text += UiText.Get("Startup.StopIncomplete") + error; }
        finally { closing = false; actions.IsEnabled = true; }
    }
    public void Phase(string heading, string description, string? completedCall = null)
    {
        title.Text = heading; detail.Text = description;
        if (completedCall is not null) completed.Add(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + " · " + completedCall);
        diagnostic.Text = string.Join("\n", completed);
    }
    public void BeginAttempt() { closeAction = null; actions.Children.Clear(); completed.Clear(); diagnostic.Visibility = Visibility.Visible; MinHeight = 360; Height = 520; }
    // After offline maintenance or history cleanup the station stays stopped until asked to start;
    // this is the only start/exit choice. Normal launches, installs and first configuration start directly.
    public void AfterMaintenance(Func<Task> close, Func<Task> start)
    {
        closeAction = close;
        title.Text = UiText.Get("Startup.MaintenanceDone");
        detail.Text = UiText.Get("Startup.StartOrExit");
        diagnostic.Visibility = Visibility.Collapsed; actions.Children.Clear(); Height = 280; MinHeight = 260;
        foreach (var (label, run, primary) in new[] { ("退出", close, false), (UiText.Get("Startup.Launch"), start, true) })
        {
            var button = new Button { Content = label, IsDefault = primary, MinHeight = 44, Margin = new(0, 8, 12, 0) };
            if (primary) button.SetResourceReference(StyleProperty, "Primary");
            button.Click += async (_, _) =>
            {
                actions.IsEnabled = false;
                try { await run(); }
                catch (Exception error)
                {
                    diagnostic.Visibility = Visibility.Visible; MinHeight = 360; Height = 520;
                    detail.Text = UiText.Get("Startup.ActionFailed"); diagnostic.Text = error.ToString();
                }
                finally { actions.IsEnabled = true; }
            };
            actions.Children.Add(button);
        }
    }
    public void UninstallFailed(string reason, Func<Task> close, Func<Task> retry)
    {
        closeAction = close; actions.Children.Clear(); diagnostic.Visibility = Visibility.Visible;
        title.Text = UiText.Get("Startup.UninstallNotOpened"); detail.Text = UiText.Get("Startup.UninstallRetry");
        diagnostic.Text = reason;
        var exit = new Button { Content = UiText.Get("Common.Close"), MinHeight = 44, IsCancel = true };
        System.Windows.Automation.AutomationProperties.SetAutomationId(exit, "UninstallHost.Close");
        exit.Click += async (_, _) => await RequestCloseAsync(); actions.Children.Add(exit);
        var again = new Button { Content = UiText.Get("Startup.RetryUninstaller"), MinHeight = 44, IsDefault = true };
        System.Windows.Automation.AutomationProperties.SetAutomationId(again, "UninstallHost.Retry");
        again.SetResourceReference(StyleProperty, "Primary");
        again.Click += async (_, _) =>
        {
            actions.IsEnabled = false;
            try { await retry(); }
            catch (Exception error) { diagnostic.Text += UiText.Get("Startup.UninstallOpenFailure") + error; }
            finally { actions.IsEnabled = true; }
        };
        actions.Children.Add(again);
    }
    public void Fail(string reason, Func<Task> close, Func<Task> retry, Func<Task> repair, Func<Task> setup)
    {
        closeAction = close;
        actions.Children.Clear(); diagnostic.Visibility = Visibility.Visible; MinHeight = 360; Height = 520;
        title.Text = UiText.Get("Startup.Failed"); detail.Text = UiText.Get("Startup.FailureActions");
        diagnostic.Text = string.Join("\n", completed) + "\n\n" + reason;
        var copy = new Button { Content = UiText.Get("Startup.CopyDiagnostics"), MinHeight = 44 };
        copy.Click += (_, _) => { try { Clipboard.SetText(buildIdentity.Text + "\n\n" + diagnostic.Text); } catch (Exception error) { detail.Text = UiText.Get("Startup.CopyFailed") + error.Message; } };
        var exit = new Button { Content = UiText.Get("Startup.StopExit"), MinHeight = 44 };
        exit.Click += async (_, _) => await RequestCloseAsync();
        actions.Children.Add(copy);
        void Add(string label, Func<Task> action, bool primary = false)
        {
            var button = new Button { Content = label, MinHeight = 44, IsDefault = primary };
            if (primary) button.SetResourceReference(StyleProperty, "Primary");
            button.Click += async (_, _) =>
            {
                actions.IsEnabled = false;
                try { await action(); }
                catch (Exception error) { diagnostic.Text += UiText.Get("Startup.ActionIncomplete") + error; }
                finally { actions.IsEnabled = true; }
            };
            actions.Children.Add(button);
        }
        Add(UiText.Get("Startup.Retry"), retry, primary: true); Add(UiText.Get("Startup.RepairSettings"), repair); Add("安装 / 修复", setup); actions.Children.Add(exit);
    }
}
