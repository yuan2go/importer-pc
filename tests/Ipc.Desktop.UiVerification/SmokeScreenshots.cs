extern alias Fixture;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;
using ManualSupplyHarness = Fixture::ManualSupplyHarness;
using ManagementModeFixture = Fixture::ManagementModeFixture;

static partial class Program
{
    static void Press(Button button) =>
        button.Dispatcher.BeginInvoke(new Action(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));

    static async Task SmokeScreenshots(string root, string evidence, List<string> lines)
    {
        await CaptureWorkstation(root, evidence, lines);
        await CaptureFirstConfiguration(evidence, lines);

        var build = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var operations = Path.Combine(root, "tests", "Ipc.Operations.Verification", "bin", build,
            "net10.0-windows", "Ipc.Operations.Verification.exe");
        var operationsEvidence = Path.Combine(evidence, "operations");
        var settings = new ProcessStartInfo(operations) { UseShellExecute = false };
        settings.ArgumentList.Add(root);
        settings.ArgumentList.Add(operationsEvidence);
        settings.ArgumentList.Add("--smoke-screenshots");
        using var process = Process.Start(settings) ?? throw new InvalidOperationException("Unable to start the isolated operations fixture.");
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2)); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        if (process.ExitCode != 0) throw new InvalidOperationException($"Operations screenshot process exited with {process.ExitCode}.");
        foreach (var png in Directory.EnumerateFiles(operationsEvidence, "operations-*.png"))
            File.Copy(png, Path.Combine(evidence, Path.GetFileName(png)));
    }

    static async Task CaptureWorkstation(string root, string evidence, List<string> lines)
    {
        const string password = "SYNTH-SETTINGS-12345";
        await using (var fixture = new ManualSupplyHarness(root, Path.Combine(evidence, "supply"), deferred: false))
        {
            fixture.Peer.HeartbeatTransform = body => body.Replace("\"TASKING\"", "\"IDLE\"", StringComparison.Ordinal);
            fixture.Peer.ReplyWaveNo = "WAVE-001";
            fixture.Peer.ReplyQuantity = "1";
            await fixture.StartAsync();
            foreach (var camera in fixture.Factory.Cameras.Values) camera.ConfirmConnected();
            await fixture.App.ConfigureManagementPasswordAsync(password);
            fixture.Peer.ReplySlot = "CH-08";
            var window = new SupplyStationWindow(fixture.App, null, "standard");
            await using var owner = new NativeWindowOwner(window);
            window.Show();
            var model = window.ViewModel;
            await AwaitUi(() => model.Fresh && model.Left.CanSubmitBarcode);
            var left = await fixture.App.SubmitParcelBarcodeAsync(await fixture.Command("left", "6950123456789"), default);
            if (left.OperationId is null) throw new InvalidOperationException(left.Detail);
            await AwaitUi(() => model.Left.ShowConfirmedTask && model.Left.Slot == "CH-08" && model.Right.CenteredStateTitle == "等待投包");
            await Capture(window, 1536, 816, evidence, "work-success-waiting", lines);
            AssertStatusDot(window, SupplyStationViewModel.StationStatusTone.Ready, "#FF147450");

            fixture.Peer.Mode = "reject";
            fixture.Peer.RejectionMessage = string.Concat(Enumerable.Repeat("当前包裹未取得可用格口，请保留包裹和本条记录，联系维护人员核对原结果。", 7));
            var right = await fixture.App.SubmitParcelBarcodeAsync(await fixture.Command("right", "6950123456796"), default);
            if (right.OperationId is null) throw new InvalidOperationException(right.Detail);
            await AwaitUi(() => model.Right.CenteredRcsRejection);
            await Capture(window, 1536, 816, evidence, "work-long-rejection", lines);

            Press(Visuals<Button>(window).Single(b => Equals(b.Content, "记录")));
            await AwaitUi(() => model.HistoryLoaded && !model.Busy);
            Press(Visuals<Button>(window).Single(b => Equals(b.Content, "今天")));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Press(Visuals<Button>(window).Single(b => AutomationProperties.GetAutomationId(b) == "History.Query"));
            await AwaitUi(() => !model.Busy && model.CanExportHistory);
            await Capture(window, 1536, 816, evidence, "records-jobs", lines);
            var roundTab = Visuals<TabItem>(window).Single(t => AutomationProperties.GetAutomationId(t) == "PositionHistory.ParcelRounds");
            roundTab.IsSelected = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var queryRounds = Visuals<Button>(window).Single(b => AutomationProperties.GetAutomationId(b) == "PositionHistory.QueryRounds");
            Press(queryRounds);
            await AwaitUi(() => queryRounds.IsEnabled && Visuals<DataGrid>(window).Any(grid => grid.IsVisible && grid.Items.Count > 0));
            await Capture(window, 1536, 816, evidence, "records-rounds", lines);

            Press((Button)window.FindName("StationSettingsEntry"));
            await AwaitUi(() => window.OwnedWindows.OfType<StationSettingsWindow>().Any(w => w.IsVisible));
            var station = window.OwnedWindows.OfType<StationSettingsWindow>().Single(w => w.IsVisible);
            await using var stationOwner = new NativeWindowOwner(station);
            await Capture(station, 1220, 800, evidence, "settings-readonly", lines);
            Press((Button)station.FindName("EditSettings"));
            ManagementModeWindow? login = null;
            await AwaitUi(() =>
            {
                login = System.Windows.Application.Current.Windows.OfType<ManagementModeWindow>().FirstOrDefault(w => w.IsVisible);
                return login is not null && Visuals<PasswordBox>(login).Any(p => p.IsVisible && p.IsEnabled);
            });
            Visuals<PasswordBox>(login!).Single(p => p.IsVisible).Password = password;
            var enter = Visuals<Button>(login!).Single(b => b.Name == "EnterManagementMode");
            await AwaitUi(() => enter.IsEnabled);
            Press(enter);
            await AwaitUi(() => !login!.IsVisible && ((ContentControl)station.FindName("EditorHost")).IsVisible && model.HasLoadedConfiguration);
            if ((await fixture.App.SnapshotAsync()).Paused)
                throw new InvalidOperationException("Modeless station settings paused the workstation.");
            var policy = model.StructuredPolicy!;
            var savedStationCode = policy.StationCode;
            policy.StationCode = "SYNTH-EDIT-DRAFT";
            try
            {
                await AwaitUi(() => Visuals<Button>(station).Any(b => b.Name == "SaveChanges" && b.IsEnabled));
                await Capture(station, 1220, 800, evidence, "settings-edit-unpaused", lines);
                var editor = Visuals<MaintenancePage>(station).Single();
                var reader = model.ReaderDrafts.Single(draft => draft.Capability.Role == "left").Capability;
                if (!editor.NavigateToParameter(reader.InstanceId + "/device.ip"))
                    throw new InvalidOperationException("The left camera settings could not be opened from station settings.");
                await Capture(station, 1220, 800, evidence, "settings-edit-cameras", lines);
                if ((await fixture.App.SnapshotAsync()).Paused)
                    throw new InvalidOperationException("Camera settings paused the workstation.");
            }
            finally { policy.StationCode = savedStationCode; }

            var snapshot = await fixture.App.SnapshotAsync();
            var rightControl = snapshot.Positions!.Single(position => position.Id == "right").SupplyControl!;
            await fixture.App.SetPositionEnabledAsync(new(Wire.Id(), snapshot.RuntimeId, "right", rightControl.Revision, false), default);
            await AwaitUi(() => model.Right.SupplyDisabled && model.OperatorStatusTone == SupplyStationViewModel.StationStatusTone.Warning);
            AssertStatusDot(window, SupplyStationViewModel.StationStatusTone.Warning, "#FF895509");

            fixture.Factory.RcsOffline = true;
            await AwaitUi(() => model.RcsState == SupplyStationViewModel.RcsConnectionState.Offline);
            AssertStatusDot(window, SupplyStationViewModel.StationStatusTone.Blocked, "#FFB52F38");
        }

        await using var unconfigured = new ManualSupplyHarness(root, Path.Combine(evidence, "empty"), deferred: true);
        unconfigured.Config.Wcs!.Parameters["left-bcr-code"] = "";
        unconfigured.Config.Wcs.Parameters["right-bcr-code"] = "";
        ExtensionStore.AtomicWrite(Path.Combine(unconfigured.Launch.ConfigurationDirectory, "business.json"), unconfigured.Config);
        await unconfigured.StartAsync(ready: false);
        var empty = new SupplyStationWindow(unconfigured.App, null, "standard");
        await using var emptyOwner = new NativeWindowOwner(empty);
        empty.Show();
        await AwaitUi(() => empty.ViewModel.Fresh);
        await Capture(empty, 1536, 816, evidence, "work-unconfigured", lines);
        AssertStatusDot(empty, SupplyStationViewModel.StationStatusTone.Blocked, "#FFB52F38");
    }

    static void AssertStatusDot(SupplyStationWindow window, SupplyStationViewModel.StationStatusTone expected, string color)
    {
        var dot = (System.Windows.Shapes.Ellipse)window.FindName("WorkStatusDot");
        if (window.ViewModel.OperatorStatusTone != expected || dot.Fill.ToString() != color)
        {
            var model = window.ViewModel;
            var reasons = string.Join(" / ", model.Snapshot?.Positions?.SelectMany(p => p.BlockingReasons) ?? []);
            throw new InvalidOperationException($"Station status: {model.OperatorStatus} · {dot.Fill} · {reasons}");
        }
    }

    static async Task CaptureFirstConfiguration(string evidence, List<string> lines)
    {
        var launch = new BusinessLaunch(Path.Combine(evidence, "first-config"), Path.Combine(evidence, "first-data"), null, null);
        ManagementModeFixture.AuthorizeRoot(launch.DataRoot);
        var type = Assembly.Load("Ipc.Desktop").GetType("Ipc.Desktop.StartupRepairWindow", throwOnError: true)!;
        var wizard = (Window)Activator.CreateInstance(type, [launch, null, true])!;
        type.GetProperty("ConfirmDiscardInputs", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(wizard, (Func<bool>)(() => true));
        await using var owner = new NativeWindowOwner(wizard);
        wizard.Show();
        ParameterSettingsControl? editor = null;
        await AwaitUi(() => (editor = Visuals<ParameterSettingsControl>(wizard).FirstOrDefault()) is { IsVisible: true, IsEnabled: true, Model: not null });
        await Capture(wizard, 1080, 700, evidence, "setup-step-1", lines);
        foreach (var label in new[] { "继续：左右相机", "继续：识读规则", "检查配置", "保存并进入" })
        {
            var actionId = label == "保存并进入" ? "Startup.SaveAndStart" : "Startup.Next";
            Button? primary = null;
            await AwaitUi(() => (primary = Visuals<Button>(wizard).FirstOrDefault(button =>
                button.IsVisible && button.IsEnabled && Equals(button.Content, label) &&
                AutomationProperties.GetAutomationId(button) == actionId)) is not null);
            var visible = ((Panel)primary!.Parent).Children.OfType<Button>().Where(button => button.IsVisible).ToArray();
            if (visible[^1] != primary)
                throw new InvalidOperationException($"Wizard primary action is not last: {label}");
            if (label != "保存并进入") Press(primary);
        }
        await Capture(wizard, 1080, 700, evidence, "setup-step-4", lines);
    }
}
