extern alias Fixture;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;
using Ipc.Presentation.Wpf;
using ManualSupplyHarness = Fixture::ManualSupplyHarness;
using ManagementModeFixture = Fixture::ManagementModeFixture;

static partial class Program
{
    static async Task CaptureToteStation(string root, string evidence, List<string> lines)
    {
        await using var fixture = new ManualSupplyHarness(root, Path.Combine(evidence, "tote"), deferred: false);
        fixture.Peer.HeartbeatTransform = body => body.Replace("\"TASKING\"", "\"IDLE\"", StringComparison.Ordinal);
        fixture.Peer.ReplyWaveNo = "WAVE-001";
        fixture.Peer.ReplyQuantity = "1";
        fixture.Peer.ReplySlot = "CH-08";
        await fixture.StartAsync();
        foreach (var camera in fixture.Factory.Cameras.Values) camera.ConfirmConnected();

        var resources = System.Windows.Application.Current.Resources;
        var palette = new WorkstationThemeManager();
        var language = new WorkstationLocalizationManager();
        var window = new SupplyStationWindow(fixture.App, null, "standard", followEffectivePage: true);
        await using var owner = new NativeWindowOwner(window);
        var localResets = new List<ToteSessionSummary>();
        window.RecordToteSession = localResets.Add;
        window.Show();
        var model = window.ViewModel;
        await AwaitUi(() => model.Fresh && model.Left.CanSubmitBarcode);
        if (window.ActiveWorkstationPage != WorkstationPages.SupplyStation)
            throw new InvalidOperationException("Expected classic workstation view before the operations save.");
        var original = await fixture.App.SnapshotAsync();
        var blocker = new Window { Owner = window, Width = 300, Height = 150 };
        blocker.Show();
        try
        {
            await ManagementModeFixture.Authorize(fixture.App);
            var full = await fixture.App.SnapshotAsync();
            var saved = await fixture.App.StageConfigurationAsync(full.Configuration.Revision,
                full.Configuration.Effective with { WorkstationPage = WorkstationPages.ToteStation }, "WorkstationPage");
            if (saved.Status != "Effective") throw new InvalidOperationException(saved.Status + " " + saved.Reason);
            await fixture.App.ExitManagementModeAsync();
            await AwaitUi(() => model.Snapshot?.Configuration.Effective.WorkstationPage == WorkstationPages.ToteStation);
            if (window.ActiveWorkstationPage != WorkstationPages.SupplyStation)
                throw new InvalidOperationException("The workstation changed while an owned window was open.");
        }
        finally { blocker.Close(); }
        await AwaitUi(() => Visuals<ToteSupplyStationWindow>(window).Any() && model.Left.CanSubmitBarcode);
        var switched = await fixture.App.SnapshotAsync();
        if (window.ActiveWorkstationPage != WorkstationPages.ToteStation || !ReferenceEquals(model, window.ViewModel) ||
            switched.RuntimeId != original.RuntimeId || !Devices(switched).SequenceEqual(Devices(original)))
            throw new InvalidOperationException("Idle hot-swap replaced an application, view model, or device session.");
        var page = Visuals<ToteSupplyStationWindow>(window).Single();
        var container = Visuals<TextBox>(page).Single(box => AutomationProperties.GetAutomationId(box) == "Tote.ContainerInput");
        container.Text = "TC-2024-0091";
        var plan = (TextBox)page.FindName("PlanInput");
        plan.Text = "3";
        container.Focus();
        plan.Focus();
        var left = await fixture.App.SubmitParcelBarcodeAsync(await fixture.Command("left", "6950123456833"), default);
        if (left.OperationId is null) throw new InvalidOperationException(left.Detail);
        await AwaitUi(() => model.Left.ShowConfirmedTask &&
            ((TextBlock)page.FindName("CountText")).Text == "1");
        var before = await fixture.App.SnapshotAsync();

        void Apply(string code, UiThemeDescriptor theme)
        {
            language.Apply(resources, code);
            palette.Apply(resources, theme);
        }
        var light = WorkstationThemeManager.BuiltInThemes.Single(theme => theme.Id == WorkstationThemeManager.IndustrialLightId);
        var dark = WorkstationThemeManager.BuiltInThemes.Single(theme => theme.Id == WorkstationThemeManager.IndustrialDarkId);
        try
        {
            Apply("zh-CN", light);
            await Capture(window, 1366, 768, evidence, "tote-station-zh-light", lines);
            Apply("en-US", light);
            await Capture(window, 1366, 768, evidence, "tote-station-en-light", lines);

            fixture.Peer.Mode = "unpack";
            var right = await fixture.App.SubmitParcelBarcodeAsync(await fixture.Command("right", "6950123456826"), default);
            if (right.OperationId is null) throw new InvalidOperationException(right.Detail);
            await AwaitUi(() => Visuals<Button>(page).Any(button => button.IsVisible &&
                AutomationProperties.GetAutomationId(button) == "Tote.ConfirmUnpack"));
            await Capture(window, 1366, 768, evidence, "tote-station-unpack-card-en", lines);

            Apply("ru-RU", dark);
            foreach (var expander in Visuals<Expander>(page)) expander.IsExpanded = true;
            await Capture(window, 1366, 768, evidence, "tote-station-ru-dark", lines);
            if (before.RuntimeId != (await fixture.App.SnapshotAsync()).RuntimeId || !ReferenceEquals(page, Visuals<ToteSupplyStationWindow>(window).Single()))
                throw new InvalidOperationException("Theme or language replaced the workstation instance.");

            var end = Visuals<Button>(page).Single(button => AutomationProperties.GetAutomationId(button) == "Tote.EndSupply");
            Press(end);
            await AwaitUi(() => window.OwnedWindows.OfType<ToteEndSupplyConfirmWindow>().Any(dialog => dialog.IsVisible));
            var confirm = window.OwnedWindows.OfType<ToteEndSupplyConfirmWindow>().Single();
            var cancel = Visuals<Button>(confirm).Single(button => button.IsCancel);
            if (!cancel.IsDefault || !cancel.IsKeyboardFocused || localResets.Count != 0)
                throw new InvalidOperationException("Tote shortage confirmation must focus cancel and retain the session.");
            CaptureInternal(evidence, "tote-station-end-supply-modal-ru", (FrameworkElement)confirm.Content, lines);
            confirm.Close();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (localResets.Count != 0 || container.Text != "TC-2024-0091")
                throw new InvalidOperationException("Canceling shortage confirmation cleared the tote.");

            var beforeEnd = fixture.Peer.Requests.Count;
            Press(end);
            await AwaitUi(() => window.OwnedWindows.OfType<ToteEndSupplyConfirmWindow>().Any(dialog => dialog.IsVisible));
            confirm = window.OwnedWindows.OfType<ToteEndSupplyConfirmWindow>().Single();
            Press(Visuals<Button>(confirm).Single(button => AutomationProperties.GetAutomationId(button) == "Tote.ForceEnd"));
            await AwaitUi(() => localResets.Count == 1 && container.Text.Length == 0);
            if (localResets[0] != new ToteSessionSummary(1, 3) || fixture.Peer.Requests.Count != beforeEnd)
                throw new InvalidOperationException("Ending a local tote changed external requests or lost its count.");
            var removal = Visuals<Button>(page).Single(button => AutomationProperties.GetAutomationId(button) == "Tote.ConfirmUnpack" && button.IsVisible);
            if (!removal.IsEnabled) throw new InvalidOperationException("An actual NEED_UNPACK result did not permit removal confirmation.");
            Press(removal);
            await AwaitUi(() => model.Right.CurrentRound is null);
            if (fixture.Peer.Requests.Count != beforeEnd)
                throw new InvalidOperationException("Unpack removal issued a second destination request.");
        }
        finally { Apply("zh-CN", light); }
    }
}
