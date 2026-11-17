using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyManagementEntryOrdering(string root, string evidence, List<string> lines)
    {
        var launch = new BusinessLaunch(Path.Combine(evidence, "config"), Path.Combine(evidence, "data"), null, null);
        Directory.CreateDirectory(launch.ConfigurationDirectory);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        ExtensionStore.AtomicWrite(Path.Combine(launch.ConfigurationDirectory, "business.json"), config);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch))
        { RuntimeIdentityPreflight = () => { if (config.Mode != RunMode.Simulation) throw new InvalidOperationException("Synthetic management entry fixture only"); } };
        await runtime.StartAsync(default);
        await VerifyManagementStatusOrdering(runtime, (valid, reason) =>
        {
            if (!valid) throw new InvalidOperationException(reason);
            lines.Add("PASS MANAGEMENT-ENTRY " + reason);
        });
    }
    static async Task VerifyManagementStatusOrdering(IParcelApplication runtime, Action<bool, string> check)
    {
        var service = DispatchProxy.Create<IManagementStatusUiFixture, ManagementStatusUiProbe>();
        var probe = (ManagementStatusUiProbe)(object)service; probe.Inner = runtime;
        var model = new ParcelViewModel(service, "Management status ordering", "Synthetic UI");
        var authorized = new ManagementModeStatus(true, true, DateTimeOffset.UtcNow.AddMinutes(15)) { SessionId = Wire.Id() };
        var shared = new TaskCompletionSource<ManagementModeStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.StatusRead = () => shared.Task;
        var foreground = model.RefreshManagementModeAsync();
        var background = model.RefreshManagementModeAsync();
        var appliedBeforeReturn = false;
        var observation = foreground.ContinueWith(_ => appliedBeforeReturn = model.IsManagementMode,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        shared.SetResult(authorized);
        await Task.WhenAll(foreground, background, observation).WaitAsync(TimeSpan.FromSeconds(5));
        check(appliedBeforeReturn, "foreground password handoff cannot return before a coalesced authorized status is applied");
        var older = new TaskCompletionSource<ManagementModeStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newer = new TaskCompletionSource<ManagementModeStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.StatusRead = () => older.Task;
        foreground = model.RefreshManagementModeAsync();
        probe.StatusRead = () => newer.Task;
        background = model.RefreshManagementModeAsync();
        older.SetResult(authorized);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var returnedBeforeLatest = foreground.IsCompleted;
        newer.SetResult(new(true, false, null));
        await Task.WhenAll(foreground, background).WaitAsync(TimeSpan.FromSeconds(5));
        check(!returnedBeforeLatest && !model.IsManagementMode, "superseded foreground reads await the newer status and cannot authorize after revocation");
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "IsWindowEnabled")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool ManagementOwnerEnabled(nint handle);
}

public interface IManagementStatusUiFixture : IParcelApplication, IWorkstationAdministration, IManagementWorkspace { }
public class ManagementStatusUiProbe : ProjectionProbe
{
    public Func<Task<ManagementModeStatus>>? StatusRead;
    public Func<Task>? AfterOpen;
    public bool FailCloseOnce;
    public readonly List<string> ClosingOwners = [];
    protected override object? Invoke(MethodInfo? method, object?[]? arguments) =>
        method!.Name == nameof(IWorkstationAdministration.ManagementModeStatusAsync) && StatusRead is { } read ? read() :
        method.Name == nameof(IManagementWorkspace.SetManagementWorkspaceAsync) ? Workspace(method, arguments!) : base.Invoke(method, arguments);
    async Task<BusinessReceipt> Workspace(MethodInfo method, object?[] arguments)
    {
        if (arguments[1] is false)
        {
            ClosingOwners.Add((string)arguments[0]!);
            if (FailCloseOnce) { FailCloseOnce = false; throw new IOException("Synthetic retained workspace resource"); }
        }
        var receipt = await (Task<BusinessReceipt>)method.Invoke(Inner, arguments)!;
        if (arguments[1] is true && AfterOpen is { } opened) await opened();
        return receipt;
    }
}
