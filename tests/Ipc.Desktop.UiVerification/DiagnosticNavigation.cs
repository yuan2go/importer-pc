using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyDiagnosticNavigation(OperationsViewFixture window, CameraRoundHarness harness,
        CameraSettingsFeedbackProbe probe, Func<Func<bool>, Task> until, Action<bool, string> check)
    {
        var vm = window.ViewModel;
        window.StopPolling();
        await window.Field<Task>("refresh");
        await vm.EnsureMaintenanceLoadedAsync();
        window.Navigate("maintenance");
        var editor = (ParameterSettingsControl)window.Parameters.FindName("MainParameterEditor");
        check(editor.NavigateTo("HistoryDays"), "the professional retention input exists");
        window.UpdateLayout();
        var input = Descendants(editor).OfType<TextBox>().Single(t => AutomationProperties.GetAutomationId(t) == "HistoryDays");
        var original = input.Text;
        input.Text = original == "19" ? "20" : "19";
        var edited = input.Text;
        var draft = vm.ParameterEditor;
        var left = vm.ReaderDrafts.Single(r => r.Original.DeviceId == harness.Left).Capability.InstanceId;
        var revision = (await harness.App.SnapshotAsync()).Configuration.Revision;

        foreach (var (cardId, detail, path) in new[]
        {
            ("shared", false, harness.Config.Wcs!.InstanceId + "/heartbeat-interval-ms"),
            (harness.Left, true, left + "/reader.engine"),
            ("storage", false, "HistoryDays")
        })
        {
            window.Navigate("diagnostics");
            await vm.RefreshAsync();
            window.UpdateLayout();
            var card = Descendants(window).OfType<Border>().Single(b => AutomationProperties.GetAutomationId(b) == "DiagnosticCard." + cardId);
            if (detail) Descendants(card).OfType<Expander>().Single().IsExpanded = true;
            window.UpdateLayout();
            var button = Descendants(card).OfType<Button>().Single(b => cardId == "storage" ? Equals(b.Content, "数据与记录设置") :
                detail ? Equals(b.Content, "专业参数") && AutomationProperties.GetName(b) == "" : AutomationProperties.GetName(b) == "RCS：专业参数");
            check(button.IsVisible && button.IsEnabled, "diagnostic action available for " + path);
            var release = new TaskCompletionSource<ManagementModeStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reading = false;
            probe.StatusRead = () => { reading = true; return release.Task; };
            probe.NavigationCalls = new();
            vm.Message = "";
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(reading, "the click reached the held authorization read for " + path);
            if (detail)
                release.SetException(new IOException("Synthetic control connection interrupted"));
            else
            {
                await ManagementModeFixture.Authorize(harness.App); // Replace the originating window's grant.
                release.SetResult(await harness.App.ManagementModeStatusAsync());
            }
            var reason = detail ? "连接" : "重新验证";
            await until(() => vm.Message.Contains(reason));
            check(window.IsVisible && card.IsVisible && !window.Parameters.IsVisible && !vm.Message.Contains("已打开") &&
                ReferenceEquals(vm.ParameterEditor, draft) && input.Text == edited && vm.StructuredPolicy!.HistoryDays == edited &&
                vm.HasUnsavedConfiguration && !string.IsNullOrWhiteSpace(window.Field<TextBox>("technicalFailure").Text),
                "failed navigation stays in diagnostics with its error, original draft and input for " + path);
            var calls = probe.NavigationCalls;
            probe.NavigationCalls = null;
            probe.StatusRead = null;
            var reconnect = Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "连接并验证管理密码…"));
            check(reconnect.IsVisible, "the failed action exposes the reconnect and authorization button");
            _ = window.Dispatcher.BeginInvoke(new Action(() => reconnect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            await until(() => window.OwnedWindows.OfType<ManagementModeWindow>().Any(w => w.IsVisible));
            var login = window.OwnedWindows.OfType<ManagementModeWindow>().Single();
            var password = Descendants(login).OfType<PasswordBox>().Single(p => p.Name == "ManagementPassword");
            await until(() => password.IsVisible && password.IsEnabled);
            password.Password = ManagementModeFixture.NativeInputPassword;
            Descendants(login).OfType<Button>().Single(b => b.Name == "EnterManagementMode").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await until(() => !login.IsVisible && vm.IsManagementMode && !window.Field<bool>("acting"));
            window.StopPolling();
            // Authentication restarts polling. Drain an already dispatched refresh before tracing the click again.
            await window.Field<Task>("refresh");
            probe.NavigationCalls = calls;
            check(button.IsEnabled, "the same window can retry after reauthorization");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await until(() => vm.Message.StartsWith("已打开") && window.Parameters.IsVisible);
            window.UpdateLayout();
            var target = Descendants(editor).OfType<Control>().Single(c => AutomationProperties.GetAutomationId(c) == path);
            check(editor.Audience == SmParameterAudience.Technical && (cardId == "storage" || vm.Message.Contains("工作站")) &&
                target.IsVisible && target.IsKeyboardFocusWithin && ReferenceEquals(vm.ParameterEditor, draft) &&
                vm.StructuredPolicy!.HistoryDays == edited && input.Text == edited, "successful retry focuses the exact field and retains the edit for " + path);
            probe.NavigationCalls = null;
            var snapshot = await harness.App.SnapshotAsync();
            check(calls.All(c => c is nameof(IWorkstationAdministration.ManagementModeStatusAsync) or nameof(IParcelApplication.SnapshotAsync)) &&
                !snapshot.Paused && snapshot.Configuration.Revision == revision, "navigation uses reads only and preserves running production: " + string.Join(", ", calls.Distinct()));
        }
        input.Text = original;
        window.ShowPositionDiagnostics(vm.Left);
        window.UpdateLayout();
        window.Field<DispatcherTimer>("timer").Start();
    }
}
