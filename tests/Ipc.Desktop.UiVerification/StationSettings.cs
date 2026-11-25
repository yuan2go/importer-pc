using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyCameraSettingsFeedback(string root, string evidence, List<string> lines)
    {
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "camera-settings-feedback"), nativeResults: true, stationCode: "SYNTHETIC-STATION");
        await h.StartAsync(authorizeManagement: false);
        await ManagementModeFixture.Authorize(h.App);
        var reserve = (h.Config.Retention ?? new ParcelRetentionPolicy()).MinimumFreeBytes;
        long probeBytes = reserve * 8;
        h.App.StorageAvailableBytesForVerification = () => Volatile.Read(ref probeBytes) is >= 0 and var bytes
            ? bytes : throw new IOException("Synthetic storage probe failure");
        var service = DispatchProxy.Create<IManagementStatusUiFixture, CameraSettingsFeedbackProbe>();
        var probe = (CameraSettingsFeedbackProbe)(object)service;
        probe.Inner = h.App;
        probe.Project = s => s with { Devices = s.Devices!.Select(d => d.DeviceId == h.Left ? d with { Health = DeviceHealth.Disconnected } : d).ToArray() };
        var window = new OperationsViewFixture(service);
        window.Show();
        var vm = window.ViewModel;
        async Task Until(Func<bool> ready) => await CameraRoundHarness.Until(async () =>
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            return ready();
        });
        void Check(bool ok, string text)
        {
            if (!ok) throw new InvalidOperationException(text);
            lines.Add("PASS CAMERA-SETTINGS " + text);
        }
        await vm.RefreshAsync();
        await Until(() => vm.IsManagementMode && vm.Fresh && vm.Left.ReaderId == h.Left);
        window.ShowPositionDiagnostics(vm.Left);
        await Until(() => window.IsVisible);
        var center = window;
        var storageCard = Descendants(center).OfType<Border>()
            .Single(card => AutomationProperties.GetAutomationId(card) == "DiagnosticCard.storage");
        var storage = (StackPanel)storageCard.Child;
        var pill = (Border)((DockPanel)storage.Children[0]).Children[0];
        var badge = (TextBlock)pill.Child;
        var reading = (TextBlock)storage.Children[1];
        bool UsesInk(string key) => Equals(badge.Foreground, badge.FindResource(key));
        await Until(() => badge.Text == "正常" && reading.Text.StartsWith("剩余 "));
        Check(Equals(pill.Background, pill.FindResource("SuccessBackground")) &&
            pill.CornerRadius == (CornerRadius)pill.FindResource("RadiusBadge") &&
            ((Button)storage.Children[2]).Margin.Bottom == 10, "healthy storage uses the shared pill and separates its action from details");
        Check(UsesInk("Success"), "verified storage reading uses the success theme resource");
        Volatile.Write(ref probeBytes, -1);
        await Until(() => badge.Text == "作业存储异常");
        Check(UsesInk("Danger") && reading.Text.StartsWith("剩余空间未确认") &&
            vm.Snapshot?.Capacity is { AdmissionBlock: ParcelInputBlockReason.StorageUnavailable, FreeStorageBytes: null },
            "a failed probe shows the storage error and withdraws the previous numeric reading");
        CaptureInternal(evidence, "camera-settings-storage-unavailable", center, lines);
        Volatile.Write(ref probeBytes, reserve - 1);
        await Until(() => vm.Snapshot?.Capacity?.FreeStorageBytes == reserve - 1);
        Check(badge.Text == "作业存储异常" && UsesInk("Danger") && reading.Text.StartsWith("剩余 "),
            "confirmed low space stays an error and shows its actual numeric reading");
        Volatile.Write(ref probeBytes, reserve * 8);
        await Until(() => badge.Text == "正常");
        Check(UsesInk("Success") && vm.Snapshot?.Capacity?.FreeStorageBytes == reserve * 8,
            "successful probe restores the healthy status and the confirmed reading");
        await VerifyDiagnosticNavigation(window, h, probe, Until, Check);
        var reconnect = Descendants(center).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "左相机：重新连接");
        Check(!reconnect.IsEnabled, "passive diagnostics cannot mutate a device before explicit debug entry");
        window.ShowPositionDebug(vm.Left);
        await Until(() => window.Debug is not null);
        window.ShowPositionDiagnostics(vm.Left);
        await Until(() => reconnect.IsEnabled);
        reconnect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => !vm.Busy && vm.Message.Contains("重新连接未完成"));
        Check(probe.ReconnectedDevice == h.Left && vm.Message.Contains("供电、网线") &&
            Descendants(center).OfType<TextBox>().Any(t => t.Text == vm.Message) &&
            !vm.Message.Contains("MaintenanceIncomplete"), "the actual left reconnect button shows a Chinese result for the incomplete receipt");

        await window.EndDebugAsync();
        window.UseFieldParameters();
        Click(center, "设置");
        await Until(() => vm.HasLoadedConfiguration && !vm.Busy);
        var leftSerial = vm.ReaderDrafts.Single(r => r.Capability.Role == "left").Capability.Parameters.Single(p => p.Field.Key == "device.serial");
        var rightDraft = vm.ReaderDrafts.Single(r => r.Capability.Role == "right").Capability;
        var rightSerial = rightDraft.Parameters.Single(p => p.Field.Key == "device.serial");
        var editor = Descendants(center).OfType<ParameterSettingsControl>().Single();
        editor.NavigateTo(rightDraft.InstanceId + "/device.ip"); center.UpdateLayout();
        var serialInput = Descendants(editor).OfType<TextBox>().Single(t => AutomationProperties.GetAutomationId(t) == rightDraft.InstanceId + "/device.serial");
        Check(serialInput.IsReadOnly, "camera serial is read-only; device identity can only be obtained through the IP lookup boundary");
        var rightIp = Descendants(editor).OfType<TextBox>().Single(t => AutomationProperties.GetAutomationId(t) == rightDraft.InstanceId + "/device.ip");
        var lookupCalls = 0; var discoveredSerial = leftSerial.Value;
        vm.ParameterEditor!.CameraSerialReader = (ip, engine) =>
        {
            if (ip != "192.0.2.99" || engine != "mvid-native") throw new InvalidOperationException("Unexpected synthetic discovery identity");
            lookupCalls++; return Task.FromResult(discoveredSerial);
        };
        rightIp.Text = "192.0.2.99";
        Check(rightSerial.Value == "" && !await vm.PrepareConfigurationReviewAsync() && lookupCalls == 1 &&
            vm.ParameterEditor.Issues.Count(i => i.Path.EndsWith("/device.serial") && i.Message.Contains("同一序列号")) == 2 &&
            probe.SaveCalls == 0, "different IPs returning the same SDK serial flag both camera identities and refuse saving");
        var factoryRejected = false;
        try { h.Factory.ValidateStructure(vm.BuildStructuredConfiguration()); }
        catch (InvalidDataException error) { factoryRejected = error.Message.Contains("同一物理序列号"); }
        Check(factoryRejected, "Application composition rejects duplicate physical identities independently of the read-only editor");
        discoveredSerial = "SYNTH-REPLACEMENT";
        Descendants(editor).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == rightDraft.InstanceId + "/ReadCameraSerial")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => rightSerial.Value == discoveredSerial && lookupCalls == 2);
        var before = (await h.App.SnapshotAsync()).Configuration.Revision;
        var left = h.Api(h.Left);
        h.Factory.Expect(vm.BuildStructuredConfiguration());
        h.Factory.DiscoverReplacements = false;
        var reviewed = await vm.PrepareConfigurationReviewAsync();
        Check(reviewed, reviewed ? "right camera identity discovered from its IP is ready to save" :
            vm.Message + " " + string.Join(" | ", vm.ParameterEditor?.Issues.Select(i => i.Message) ?? []));
        var review = new ConfigurationSaveReviewWindow(center, vm);
        review.Show();
        var save = Descendants(review).OfType<Button>().Single(b => b.Name == "ConfirmSaveChanges");
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => !vm.Busy && vm.ConfigurationSubmissionOutcome.Length > 0);
        var actual = (await h.App.SnapshotAsync()).Configuration;
        Check(probe.SaveCalls == 1 && actual.Revision == before + 1 && actual.Pending is null &&
            actual.Effective.Topology!.Readers.Single(r => r.DeviceId == h.Right).Parameters!["device.serial"] == "SYNTH-REPLACEMENT" &&
            !vm.HasUnsavedConfiguration && !vm.HasUnconfirmedConfigurationSubmission,
            "a cancelled save response reads back the exact committed serial and never submits it twice. Outcome: " + vm.ConfigurationSubmissionOutcome);
        Check(h.Factory.ReaderBuilds == 1 && ReferenceEquals(left, h.Api(h.Left)) && left.Stops == 0,
            "adopting the right camera's discovered identity does not add hidden defaults or reconnect the left camera");
        Check(!save.IsVisible && Descendants(review).OfType<Button>().Any(b => Equals(b.Content, "完成") && b.IsEnabled) &&
            Descendants(review).OfType<TextBlock>().Any(t => t.Text.StartsWith("设置已保存并生效。") && t.IsVisible) &&
            !Descendants(review).OfType<Expander>().Any(e => Equals(e.Header, "保存详情")) &&
            !vm.Message.Contains("TaskCanceledException"), "the native save review shows completion beside the buttons and omits technical details");
        CaptureInternal(evidence, "camera-settings-saved", review, lines);
        Click(review, "完成");
        center.Close();
        await Until(() => !center.IsVisible);
        window.Close();
        await VerifyStartupIdentityCleanup(root, evidence, lines);
    }

    static async Task VerifyStartupIdentityCleanup(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS CAMERA-SETTINGS " + text); }
        var desktop = Assembly.LoadFrom(Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP") ?? throw new InvalidOperationException("Set same-build desktop assembly"));
        var type = desktop.GetType("Ipc.Desktop.StartupRepairWindow", true)!;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var busy = type.BaseType!.GetField("Busy", flags)!;
        var launch = new BusinessLaunch(Path.Combine(evidence, "identity-startup-config"), Path.Combine(evidence, "identity-startup-data"), null, null);
        ManagementModeFixture.AuthorizeRoot(launch.DataRoot);
        var first = (Window)Activator.CreateInstance(type, [launch, null, true])!;
        type.GetProperty("ConfirmDiscardInputs", flags)!.SetValue(first, (Func<bool>)(() => true));
        first.Show(); await FirstConfigurationReady(first, busy, Check);
        var field = type.GetField("maintenance", flags)!;
        var startup = (IStartupMaintenance)field.GetValue(first)!;
        var reachedLookup = false;
        try { await startup.ReadCameraSerialAsync("invalid-ip", "zxing-cpp"); }
        catch (InvalidDataException) { reachedLookup = true; }
        Check(reachedLookup, "first-configuration service forwards identity lookup without loading an SDK for invalid input");
        await startup.DisposeAsync();
        var document = (StartupMaintenanceDocument)type.GetField("document", flags)!.GetValue(first)!;
        var retained = new StartupMaintenanceProbe(document) { FailDispose = true };
        field.SetValue(first, retained);
        first.Close();
        await CameraRoundHarness.Until(async () => { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); return retained.DisposeCalls == 1 && !(bool)busy.GetValue(first)!; });
        Check(first.IsVisible && ReferenceEquals(field.GetValue(first), retained), "failed startup cleanup retains the same service and the open window for an explicit retry");
        retained.FailDispose = false; first.Close();
        await CameraRoundHarness.Until(async () => { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); return !first.IsVisible; });
        Check(retained.DisposeCalls == 2, "closing startup again retries the original owner before the window can exit");
    }

    static async Task VerifyStationSettings(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS STATION01 " + text); }
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
        async Task Until(Func<bool> ready) { for (var i = 0; i < 200; i++) { await Flush(); if (ready()) return; await Task.Delay(25); } throw new TimeoutException("Station UI did not settle"); }
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "station-settings"), nativeResults: true, stationCode: "SYNTHETIC-STATION");
        await h.StartAsync(); var runtime = h.App;
        var window = new OperationsViewFixture(runtime) { ConfirmDiscardDrafts = _ => true };
        await using var owner = new NativeWindowOwner(window); window.Show();
        var vm = window.ViewModel;
        await Until(() => vm.IsManagementMode && vm.Fresh);
        window.Navigate("maintenance");
        await Until(() => vm.HasLoadedConfiguration && !vm.Busy);
        var editor = Descendants(window).OfType<ParameterSettingsControl>().Single();
        var baseline = (await runtime.SnapshotAsync()).Configuration;
        var original = baseline.Pending ?? baseline.Effective;
        Check(editor.Audience == SmParameterAudience.Technical && editor.NavigateTo("ReadTimeoutMs") && !editor.NavigateTo("StationCode"),
            "actual professional form exposes automatic read deadline and removes field-owned station identity");
        var rcs = vm.CapabilityDrafts.Single(ParameterOwnership.IsRcs);
        Check(editor.NavigateTo(rcs.InstanceId + "/heartbeat-interval-ms") && !editor.NavigateTo(rcs.InstanceId + "/zone-id") &&
            !editor.NavigateTo(rcs.InstanceId + "/project-id") && !editor.NavigateTo(rcs.InstanceId + "/Target"),
            "professional heartbeat remains editable while warehouse, production line and server address remain field-owned");
        var reader = vm.ReaderDrafts.First().Capability;
        Check(editor.NavigateTo(reader.InstanceId + "/reader.engine") && !editor.NavigateTo(reader.InstanceId + "/device.ip"),
            "camera engine belongs to professional form while its IP belongs to field settings");
        vm.StructuredPolicy!.StationCode = "HIDDEN-FIELD-MUST-NOT-SAVE";
        vm.StructuredPolicy.ReadTimeoutMs = "1700";
        var technical = vm.BuildStructuredConfiguration();
        Check(technical.StationCode == original.StationCode && technical.ReadTimeoutMs == 1700 && Wire.Encode(technical.Wcs) == Wire.Encode(original.Wcs),
            "professional candidate preserves hidden field values even when a template/editor draft carries other values");
        CaptureInternal(evidence, "station-professional-fields", window, lines);
        // The same production form/definition is mounted in its field audience;
        // real workstation authorization/PDA interactions are in independent-settings.
        window.UseFieldParameters();
        Check(editor.NavigateTo("StationCode") && editor.NavigateTo(reader.InstanceId + "/device.ip") &&
            editor.NavigateTo(rcs.InstanceId + "/zone-id") && editor.NavigateTo(rcs.InstanceId + "/project-id") &&
            !editor.NavigateTo("ReadTimeoutMs") && !editor.NavigateTo(rcs.InstanceId + "/heartbeat-interval-ms"),
            "field audience uses existing station, IP, warehouse and production-line fields and removes professional controls");
        var field = vm.BuildStructuredConfiguration();
        Check(field.StationCode == "HIDDEN-FIELD-MUST-NOT-SAVE" && field.ReadTimeoutMs == original.ReadTimeoutMs &&
            Wire.Encode(field.BarcodeFilter) == Wire.Encode(original.BarcodeFilter) && field.SchemaVersion == original.SchemaVersion,
            "field complete candidate preserves hidden professional values and omitted fields without a duplicate configuration model");
        editor.NavigateTo("WindowOpeningMode"); window.UpdateLayout();
        Check(Descendants(editor).OfType<ComboBox>().Any(t => AutomationProperties.GetAutomationId(t) == "WindowOpeningMode" && t.IsVisible),
            "field personal window preference remains editable");
        Check((await runtime.SnapshotAsync()).Positions!.All(p => p.Admit) && (await runtime.SnapshotAsync()).Configuration.Revision == baseline.Revision,
            "browsing, changing audiences and editing views never pause production or save implicitly");
        vm.StructuredPolicy.StationCode = original.StationCode ?? "";
        vm.StructuredPolicy.ReadTimeoutMs = original.EffectiveReadTimeoutMs.ToString();
        CaptureInternal(evidence, "station-field-fields", window, lines);
        var center = window;
        await runtime.ExitManagementModeAsync(); await vm.RefreshAsync();
        await Until(() => !window.Parameters.IsEnabled);
        Check(!window.Parameters.IsEnabled, "revocation locks the actual target editor without discarding its complete draft");
        var management = new ManagementModeWindow((IWorkstationAdministration)runtime) { Owner = center }; management.Show();
        await Until(() => Descendants(management).OfType<Button>().Any(b => Equals(b.Content, "忘记管理密码？") && b.IsVisible));
        var recoveryDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await Until(() => System.Windows.Application.Current.Windows.OfType<ManagementPasswordRecoveryWindow>().Any());
                var recovery = System.Windows.Application.Current.Windows.OfType<ManagementPasswordRecoveryWindow>().Single();
                PasswordBox Field(string name) => Descendants(recovery).OfType<PasswordBox>().Single(p => p.Name == name);
                Check(!Descendants(recovery).OfType<PasswordBox>().Any(p => p.Name == "RecoveryRootPassword"), "native recovery exposes no shared password input");
                Field("RecoveryNewPassword").Password = "Synthetic-Replacement";
                Field("RecoveryConfirmation").Password = "different";
                Click(recovery, "重置管理密码");
                await Until(() => Descendants(recovery).OfType<TextBlock>().Any(t => t.Text.Contains("两次新管理密码不一致")));
                Check(recovery.IsVisible && Descendants(recovery).OfType<PasswordBox>().All(p => p.Password.Length == 0), "mismatched confirmation keeps the dialog and clears secrets");
                CaptureInternal(evidence, "station-windows-recovery", recovery, lines);
                Click(recovery, "取消");
                recoveryDone.SetResult();
            }
            catch (Exception error) { recoveryDone.SetException(error); foreach (var child in System.Windows.Application.Current.Windows.OfType<ManagementPasswordRecoveryWindow>().ToArray()) child.Close(); }
        }));
        Click(management, "忘记管理密码？"); await recoveryDone.Task;
        management.Close(); await vm.RefreshAsync(); window.UpdateLayout();
        Check(!vm.ManagementStatus.Authorized, "cancelling password recovery cannot reauthorize the exited management session");
        await h.AuthorizeAsync();
        await Until(() => vm.IsManagementMode && window.Parameters.IsEnabled);
        Check(window.Parameters.IsEnabled, "a newly verified fixture session restores the retained editor");
        window.Close(); await Until(() => !window.IsVisible);
        lines.Add("Actual shared WPF field/professional forms over controlled camera/RCS ports. Native production authorization/routing is verified separately; real hardware NOT_RUN.");
    }
}

public class CameraSettingsFeedbackProbe : ManagementStatusUiProbe
{
    public int SaveCalls;
    public string? ReconnectedDevice;
    public System.Collections.Concurrent.ConcurrentQueue<string>? NavigationCalls;
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        NavigationCalls?.Enqueue(method!.Name);
        if (method!.Name == nameof(IParcelApplication.StageConfigurationAsync))
            return LoseSaveResponse((Task<BusinessReceipt>)base.Invoke(method, arguments)!);
        if (method.Name == nameof(IParcelApplication.ReconnectDeviceAsync))
        {
            ReconnectedDevice = (string)arguments![0]!;
            return Task.FromResult(new BusinessReceipt("MaintenanceIncomplete", "Original resources retained; inspect and explicitly retry maintenance", 0));
        }
        return base.Invoke(method, arguments);
    }
    async Task<BusinessReceipt> LoseSaveResponse(Task<BusinessReceipt> saving)
    {
        SaveCalls++;
        var result = await saving;
        if (result.Status != "Effective") return result;
        throw new TaskCanceledException("Synthetic response cancelled after commit");
    }
}
