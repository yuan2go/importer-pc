using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;
using Ipc.Presentation.Wpf;

static partial class Program
{
    static async Task VerifyTaskHierarchy(string root, string evidence, List<string> lines, bool baseline)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); lines.Add("PASS HIERARCHY " + reason); }
        var folder = Path.Combine(evidence, "authorization-config"); Directory.CreateDirectory(folder);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        ExtensionStore.AtomicWrite(Path.Combine(folder, "business.json"), config);
        var launch = new BusinessLaunch(folder, Path.Combine(evidence, "authorization-data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize(runtime);
        var window = new OperationsViewFixture(runtime); await using var owner = new NativeWindowOwner(window); window.Show(); var vm = window.ViewModel;
        async Task Until(Func<bool> ready) { for (var n = 0; n < 200; n++) { await vm.RefreshAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (ready()) return; await Task.Delay(20); } throw new TimeoutException("Management revocation did not converge"); }
        await Until(() => vm.IsManagementMode);
        window.Navigate("maintenance");
        await Until(() => vm.HasLoadedConfiguration && window.IsVisible);
        var center = window; var host = center.Page;
        var page = (MaintenancePage)host.Content; var protectedBody = page;
        var draft = vm.StructuredPolicy!; draft.ReadTimeoutMs = "1700";
        Check(vm.IsManagementMode && protectedBody.IsEnabled && host.Content is MaintenancePage, "the actual protected settings page is open and authorized immediately before revocation");
        await runtime.ExitManagementModeAsync(); await vm.RefreshManagementModeAsync();
        await Until(() => !vm.IsManagementMode && !protectedBody.IsEnabled);
        Check(center.IsVisible && ReferenceEquals(host.Content, page) && ReferenceEquals(draft, vm.StructuredPolicy) && draft.ReadTimeoutMs == "1700", "revocation disables the previously mounted protected content while retaining the same unsaved draft and closable owner");
        var before = (await runtime.SnapshotAsync()).Configuration.Revision; var denied = false;
        try { await vm.SaveStructuredConfigurationAsync(); } catch (UnauthorizedAccessException) { denied = true; }
        Check(denied && (await runtime.SnapshotAsync()).Configuration.Revision == before, "direct save after revocation is denied before any configuration revision is written");
        CaptureInternal(evidence, "current-management-revoked", center, lines);
    }

}

public class HierarchyProjectionProbe : ProjectionProbe
{
    public Func<BusinessCommand, Task<BusinessReceipt>>? Command;
    public bool FailSnapshots;
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method!.Name == nameof(IParcelApplication.SnapshotAsync) && FailSnapshots) return Task.FromException<ParcelSnapshot>(new IOException("合成状态读取失败"));
        if (method.Name == nameof(IBusinessApplication.CommandAsync) && Command is not null) return Command((BusinessCommand)arguments![0]!);
        if (method.Name == nameof(IWorkstationAdministration.QueryLogsAsync))
        {
            var rows = Enumerable.Range(1, 12).Select(i => new SoftwareLogEntry(new("synthetic", i, "fixture", "synthetic.jsonl"),
                "合成运行日志", new DateTimeOffset(2026, 10, 24, 8, 0, i, TimeSpan.FromHours(8)), "信息", "合成记录 " + i + "：运行状态已读取。")).ToArray();
            return Task.FromResult(new SoftwareLogPage("synthetic-fixed-selection", rows, false, []));
        }
        return base.Invoke(method, arguments);
    }
}
