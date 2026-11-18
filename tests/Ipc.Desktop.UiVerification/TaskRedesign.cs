using System.Windows.Threading;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyTaskRedesign(string root, string evidence, List<string> lines, bool interactive)
    {
        // The current SM navigation lives in its owned management window. Verify
        // the mounted page, not the retired main-window settings surface.
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); lines.Add("PASS UIR " + reason); }
        var folder = Path.Combine(evidence, "draft-config"); Directory.CreateDirectory(folder);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        ExtensionStore.AtomicWrite(Path.Combine(folder, "business.json"), config);
        var launch = new BusinessLaunch(folder, Path.Combine(evidence, "draft-data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize(runtime);
        var window = new OperationsViewFixture(runtime); await using var owner = new NativeWindowOwner(window); window.Show();
        var vm = window.ViewModel;
        async Task Until(Func<bool> ready) { for (var n = 0; n < 200; n++) { await vm.RefreshAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (ready()) return; await Task.Delay(20); } throw new TimeoutException("Current management draft did not converge"); }
        await Until(() => vm.IsManagementMode);
        window.Navigate("maintenance");
        await Until(() => vm.HasLoadedConfiguration && window.IsVisible);
        var center = window;
        var host = center.Page; var page = (MaintenancePage)host.Content;
        var editor = (ParameterSettingsControl)page.FindName("MainParameterEditor"); var draft = editor.Model;
        Check(editor.NavigateTo("ReadTimeoutMs"), "current station setting is reachable");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var station = Descendants(editor).OfType<TextBox>().Single(box => AutomationProperties.GetAutomationId(box) == "ReadTimeoutMs");
        station.Text = "1700"; await Dispatcher.Yield(DispatcherPriority.DataBind);
        var revision = (await runtime.SnapshotAsync()).Configuration.Revision;
        foreach (var destination in new[] { "logs", "diagnostics" })
        {
            window.Navigate(destination);
            await Until(() => vm.SelectedPage == destination && !ReferenceEquals(host.Content, page));
            window.Navigate("maintenance");
            await Until(() => vm.SelectedPage == "maintenance" && host.Content is MaintenancePage && !vm.Busy);
            var current = (MaintenancePage)host.Content; var currentEditor = (ParameterSettingsControl)current.FindName("MainParameterEditor");
            Check(ReferenceEquals(current, page) && ReferenceEquals(currentEditor, editor) && ReferenceEquals(currentEditor.Model, draft), destination + " round trip retains the mounted page/editor/full draft identities");
            Check(currentEditor.NavigateTo("ReadTimeoutMs"), "mounted station field remains reachable"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var currentStation = Descendants(currentEditor).OfType<TextBox>().Single(box => AutomationProperties.GetAutomationId(box) == "ReadTimeoutMs");
            Check(ReferenceEquals(currentStation, station) && currentStation.Text == "1700", destination + " round trip retains the visible unsaved field, not a detached old control");
        }
        Check((await runtime.SnapshotAsync()).Configuration.Revision == revision && (await runtime.SnapshotAsync()).Operations.Length == 0, "navigation does not save a revision or issue any parcel request");
        CaptureInternal(evidence, "current-management-draft", center, lines);
        // The external-extension editor fixture below remains paused with that
        // mechanism; SM uses the built-in provider and the current full draft above.
    }
    static async Task VerifyRcsEditor(string root, ParcelRuntime runtime, ParcelConfiguration config, Window window, Action<string, FrameworkElement> capture, Action<bool, string> check)
    {
        var package = new PackageReference("hikrobot.idmvs-reader", "1.0.3", new string('a', 64));
        var readerFields = Wire.Decode<ParameterField[]>(File.ReadAllText(Path.Combine(root, "src/Ipc.Drivers.Hikrobot/parameters.json")));
        var rcsFields = Wire.Decode<ParameterField[]>(File.ReadAllText(Path.Combine(root, "src/Ipc.Integrations.SportmasterRcs/parameters.json")));
        var reader = new CapabilityDescriptor("observation", "extension:hikrobot.idmvs-reader/idmvs-reader", "1.0.3", "HIKROBOT-IDMVS-OBSERVATION-v1", readerFields, new(), package, "idmvs-reader");
        var rcs = new CapabilityDescriptor("integration", "sportmaster-rcs-task", "1.3.0", "SPORTMASTER-RCS-SORTER-v1", rcsFields, new());
        var binding = new CapabilityBinding("integration", rcs.Implementation, rcs.Version, "rcs.one", "http://192.0.2.10/rcs/sorter/api", rcs.Contract, new()
        {
            ["zone-id"] = "71",
            ["project-id"] = "83",
            [RcsRequestPolicy.DestinationMode] = "false",
            [RcsRequestPolicy.DestinationChuteCode] = "OLD-CHUTE",
            ["request-id-base"] = "810000000",
            ["request-id-count"] = "1000",
            ["left-bcr-code"] = "TAKE-L",
            ["right-bcr-code"] = "TAKE-R"
        });
        var devices = config.Topology!.Readers.Select((r, i) => r with { Driver = "extension:reader." + i }).ToArray();
        var draft = new StartupConfigurationDraft(config with
        {
            Wcs = binding,
            Reader = devices[0],
            Topology = config.Topology with { Readers = devices },
            Extensions = new([package], devices.Select((r, i) => new ExtensionInstance("reader." + i, package.Id, reader.Module!, r.DeviceId, true, new() { ["device.ip"] = "192.0.2." + (21 + i) })).ToArray())
        },
            new([reader, rcs, .. (await runtime.CapabilitiesAsync()).Providers.Where(p => p.Kind is not ("observation" or "integration"))], [package.Id], Packages: [new(package, [])]));
        var model = draft.CreateParameterEditor(runtime.TestBarcodeFilterAsync); var control = new ParameterSettingsControl { Model = model };
        window.Content = control; window.Width = 1366; window.Height = 768; window.UpdateLayout();
        control.NavigateTo("StationCode"); window.UpdateLayout();
        var common = Descendants(control).OfType<TextBox>().Single(t => AutomationProperties.GetAutomationId(t) == "StationCode");
        check(common.Text == draft.Policy.StationCode, "station input uses the one complete-configuration value"); capture("parameters-single-station", control);
        common.Text = "SYNTHETIC-COMMON";
        var rcsDraft = draft.Capabilities.Single(c => c.InstanceId == "rcs.one"); var built = rcsDraft.Build();
        check(draft.Build().StationCode == "SYNTHETIC-COMMON" && built.Parameters.All(p => !p.Key.EndsWith("-station-code")) && !built.Parameters.ContainsKey(RcsRequestPolicy.DestinationChuteCode), "one station edit creates no per-position station parameters and automatic assignment omits inactive chute");
        check(rcsDraft.Parameters.Single(p => p.Field.Key == RcsRequestPolicy.DestinationChuteCode).Value == "OLD-CHUTE", "inactive chute input remains in the raw draft");
        check(model.ValidateFields(), "corrected synthetic provider candidate passes field checks before normal-form captures");
        control.NavigateTo("rcs.one/quantity"); window.UpdateLayout(); capture("parameters-business-rules", control);
        check(control.NavigateTo("reader.1"), "right camera object remains a valid navigation target after the SDK-only transition"); window.UpdateLayout(); capture("parameters-right-driver", control);
        check(Descendants(control).OfType<TextBox>().Count(t => t.Text == "TAKE-R") == 1 &&
            draft.Readers[1].Parameters.All(p => p.Field.Key != "bcr-code") &&
            !Descendants(control).OfType<FrameworkElement>().Any(t => AutomationProperties.GetAutomationId(t) == "reader.1/bcr-code"),
            "right camera displays one shared request/heartbeat code and no driver code copy");
        control.NavigateTo("rcs.one/Target"); window.UpdateLayout(); capture("parameters-rcs-component", control);
        check(!Descendants(control).OfType<TextBox>().Any(t => !t.IsReadOnly && t.Text == "71"), "moved system values are read-only references on the RCS object");
        var template = StartupSiteTemplates.Export("sportmaster", draft.Capture());
        check(!template.Defaults.Values.Keys.Any(k => k == "policy.StationCode" || k.Contains("parameter.") || k.StartsWith("policy.Backup")), "safe template excludes heart station code, instance parameters and backup settings");
        var localBefore = draft.Capture();
        var restored = draft.WithTemplate(StartupSiteTemplates.Import("sportmaster", template));
        check(localBefore.Values.All(x => restored.Capture().Values.GetValueOrDefault(x.Key) == x.Value), "same-provider safe template preserves every local parameter and identity");
        var invalidProviderTemplate = new StartupEditorState(new(template.Defaults.Values) { ["cap.0.provider"] = "missing-provider" });
        try { draft.WithTemplate(invalidProviderTemplate); throw new InvalidOperationException("missing template provider unexpectedly accepted"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("精确提供者")) { }
        check(localBefore.Values.All(x => draft.Capture().Values.GetValueOrDefault(x.Key) == x.Value), "failed template import leaves the entire current draft untouched");
        rcsDraft.Target = "http://192.0.2.10/other";
        rcsDraft.Parameters.Single(p => p.Field.Key == "request-id-base").Value = (long.MaxValue - 1).ToString();
        rcsDraft.Parameters.Single(p => p.Field.Key == "right-bcr-code").Value = rcsDraft.Parameters.Single(p => p.Field.Key == "left-bcr-code").Value;
        check(!model.ValidateFields() && model.Issues.Any(i => i.Path == "rcs.one/Target") &&
            model.Issues.Any(i => i.Path == "rcs.one/request-id-count") && model.Issues.Count(i => i.Path.EndsWith("-bcr-code")) == 2,
            "endpoint, overflowing request range and duplicated shared camera codes locate exact editable fields");
        control.NavigateTo("rcs.one/Target"); capture("parameters-errors", control);
        await System.Windows.Threading.Dispatcher.Yield();
    }
}
