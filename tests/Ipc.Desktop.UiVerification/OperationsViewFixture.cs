using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;

// Hosts the actual operations presentation over the existing controlled Application.
// Password HWND, identity/lease and crash semantics are exercised in Operations.Verification;
// these fixtures explicitly adopt an already authorized synthetic test session for image assertions.
internal sealed class OperationsViewFixture : Window
{
    internal readonly OperationsWorkspaceControl Workspace;
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly Grid client = new();
    bool ready, closing;
    readonly bool guest;
    Task adoption = Task.CompletedTask;
    public Exception? CloseFailure { get; private set; }
    public SupplyStationViewModel ViewModel { get; }
    public Func<int, bool>? ConfirmDiscardDrafts { get; set; } = _ => true;
    static readonly BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;
    internal T Field<T>(string name) => (T)typeof(OperationsWorkspaceControl).GetField(name, Fields)!.GetValue(Workspace)!;
    void Assign(string name, object? value) => typeof(OperationsWorkspaceControl).GetField(name, Fields)!.SetValue(Workspace, value);
    internal UserControl? Debug => Field<UserControl?>("debug");
    internal ContentControl Page => Field<ContentControl>("page");
    internal MaintenancePage Parameters => Field<MaintenancePage>("parameters");
    internal void StopPolling() { clock.Stop(); Field<DispatcherTimer>("timer").Stop(); }
    internal async Task EndDebugAsync() => await (Task)typeof(OperationsWorkspaceControl).GetMethod("EndDebug", Fields)!.Invoke(Workspace, null)!;
    internal void UseFieldParameters()
    {
        ViewModel.ParameterAudience = SmParameterAudience.Field;
        Parameters.Audience = SmParameterAudience.Field;
    }
    public OperationsViewFixture(IParcelApplication application, OperationsViewFixture? owner = null)
    {
        Width = 1380; Height = 820; MinWidth = 640; MinHeight = 420;
        Workspace = new(application, () => Task.CompletedTask, () => true, () => Task.CompletedTask);
        Workspace.NavigationRequested += (_, target) => Navigate(target switch { "parameters" => "maintenance", "debug" => "diagnostics", _ => target });
        ViewModel = Field<SupplyStationViewModel>("model"); DataContext = ViewModel;
        NameScope.SetNameScope(this, new NameScope()); RegisterName("ClientArea", client); 
        client.Children.Add(Workspace); Content = client;
        // Round-CAS tests mount a second view inside the first fixture's
        // explicit scope. That view owns neither its workspace nor its grant.
        guest = owner is not null;
        if (guest) { Assign("window", owner!.Field<string>("window")); Field<DispatcherTimer>("timer").Stop(); }
        Loaded += async (_, _) => { adoption = AdoptGrant(); await adoption; await ViewModel.RefreshAsync(); };
        clock.Tick += (_, _) => { if (!closing && adoption.IsCompleted) adoption = AdoptGrant(); }; clock.Start();
        Closing += OnClosing;
    }
    async Task AdoptGrant()
    {
        var status = await ((IWorkstationAdministration)ViewModel.Application).ManagementModeStatusAsync();
        if (status.Authorized) { Assign("grant", guest ? null : status.SessionId); ViewModel.OwnedManagementSession = status.SessionId; }
        await ViewModel.RefreshManagementModeAsync();
    }
    internal Task AdoptFixtureGrantAsync() => AdoptGrant();
    public async void ShowPositionDebug(SupplyStationPositionViewModel position)
    {
        if (Field<bool>("workspace") && ViewModel.IsManagementMode)
        {
            typeof(OperationsWorkspaceControl).GetMethod("ShowDebugView", Fields)!.Invoke(Workspace, null);
            Debug!.GetType().GetMethod("SelectPosition")!.Invoke(Debug, [position.PositionId]); SetPage("debug"); return;
        }
        await AdoptGrant(); if (!ViewModel.IsManagementMode) return;
        var window = Field<string>("window");
        if (!guest && !Field<bool>("workspace"))
        {
            var receipt = await ((IManagementWorkspace)ViewModel.Application).SetManagementWorkspaceAsync(window, true, default);
            if (receipt.Status is not ("Paused" or "Opened" or "PausedWithCameraWarning")) throw new InvalidOperationException(receipt.Reason);
            Assign("workspace", true);
        }
        typeof(OperationsWorkspaceControl).GetMethod("ShowDebugView", Fields)!.Invoke(Workspace, null);
        Debug!.GetType().GetMethod("SelectPosition")!.Invoke(Debug, [position.PositionId]);
        SetPage("debug");
    }
    public void ShowPositionDiagnostics(SupplyStationPositionViewModel position) => SetPage("diagnostics");
    internal void Navigate(string page) => SetPage(page);
    void SetPage(string page)
    {
        ViewModel.SelectedPage = page;
        Workspace.ShowPage(page switch { "maintenance" => "parameters", "diagnostics" => "debug", "workstation" => "parameters", _ => page });
        if (page == "diagnostics") typeof(OperationsWorkspaceControl).GetMethod("ShowDiagnosticsView", Fields)!.Invoke(Workspace, null);
        if (page is "maintenance" or "workstation") _ = ViewModel.EnsureMaintenanceLoadedAsync();
    }
    async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (ready) return; e.Cancel = true; if (closing) return; closing = true; IsEnabled = false; clock.Stop();
        try
        {
            var answer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            answer.Tick += (_, _) =>
            {
                var dialog = FindWindow("#32770", "核对整站退出影响");
                if (dialog != 0 && GetWindow(dialog, 4) == new System.Windows.Interop.WindowInteropHelper(this).Handle)
                    SendMessage(dialog, 0x111, ConfirmDiscardDrafts?.Invoke(1) == true ? 1 : 2, 0);
            };
            answer.Start();
            try
            {
                await adoption;
                if (guest && Debug is { } view) await (Task)view.GetType().GetMethod("CloseAsync")!.Invoke(view, null)!;
                await Workspace.CloseAsync();
            }
            catch (OperationCanceledException) { return; }
            finally { answer.Stop(); }
            ready = true; clock.Stop(); await Dispatcher.InvokeAsync(Close);
        }
        catch (Exception error) { CloseFailure = error; Console.Error.WriteLine("Operations fixture close failed: " + error); }
        finally { closing = false; IsEnabled = true; if (!ready) clock.Start(); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern nint FindWindow(string kind, string title);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern nint GetWindow(nint handle, uint command);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern nint SendMessage(nint handle, uint message, nint value, nint data);
}
