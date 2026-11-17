using System.Reflection;
using System.Windows;
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
    static async Task VerifyLifecycleNative(string root, string evidence, List<string> lines)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); lines.Add("PASS LIA-NATIVE " + reason); }
        var configRoot = Path.Combine(evidence, "config"); Directory.CreateDirectory(configRoot);
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        ExtensionStore.AtomicWrite(Path.Combine(configRoot, "business.json"), c);
        var launch = new BusinessLaunch(configRoot, Path.Combine(evidence, "data"), null, null);
        ParcelRuntime Make() => new(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await using var app = Make(); await app.StartAsync(default); await ManagementModeFixture.Authorize(app);
        var failStop = true; var stopCalls = 0; var reopen = 0; ParcelRuntime? replacement = null;
        var window = new SupplyStationWindow(app, null, "standard", async () =>
        {
            stopCalls++;
            if (failStop) { failStop = false; throw new IOException("合成停止失败；原资源仍归原实例"); }
            await app.DisposeAsync();
        })
        { ConfirmOwnerExit = _ => true, ConfirmDiscardDrafts = _ => true };
        window.ReopenOwnerAsync = async () => { reopen++; replacement = Make(); await replacement.StartAsync(default); };
        window.Show(); var vm = window.ViewModel; await SetClient(window, 1366, 768); await vm.RefreshAsync();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var lane = i % 2 == 0 ? "left" : "right";
                if ((await app.SnapshotAsync()).Positions!.Single(position => position.Id == lane).CurrentRoundId is not null) await SmRoundFixture.NextVehicleAsync(app, lane);
                app.EmitReferenceAt(lane, "lia-native-" + i, "00001234", "lia-native-event-" + i);
                await Until(() => vm.Snapshot!.Operations.Any(o => o.AdmissionId == "lia-native-" + i && o.State == ParcelState.SlotResolved));
            }
            Click(window, "作业记录"); await Until(() => vm.HistoryLoaded && !vm.Busy);
            Check(vm.HistoryItems.Length == 3 && Descendants(window).OfType<Button>().Any(b => Equals(b.Content, "上一页")), "native history uses the full real Application query contract and exposes previous-page navigation");
            var combo = Descendants(window).OfType<ComboBox>().Single(b => b.ItemsSource is ParcelViewModel.HistoryPositionChoice[]);
            combo.SelectedValue = "right"; await vm.RefreshAsync(); window.UpdateLayout();
            Check(vm.HistoryPosition == "right" && vm.HistoryFiltersChanged && vm.HistoryStatus.Contains("尚未应用"), "native business label maps to stable ID and refresh cannot erase edited conditions");
            CaptureInternal(evidence, "lia-history-edited-standard", (FrameworkElement)window.Content, lines);
            await SetClient(window, 800, 420); window.UpdateLayout();
            var rows = Descendants(window).OfType<DataGrid>().Single(g => g.Name == "HistoryRows");
            Check(rows.ActualHeight >= rows.ColumnHeaderHeight + rows.RowHeight, "minimum native history viewport retains a full result row with applied-range feedback");
            CaptureInternal(evidence, "lia-history-edited-minimum", (FrameworkElement)window.Content, lines);
            var professional = new OperationsViewFixture(app); await using var professionalOwner = new NativeWindowOwner(professional); professional.Show();
            await Until(() => professional.ViewModel.IsManagementMode && professional.ViewModel.Fresh);
            professional.Navigate("maintenance"); var technicalModel = professional.ViewModel;
            await technicalModel.EnsureMaintenanceLoadedAsync();
            await Until(() => technicalModel.HasLoadedConfiguration && !technicalModel.Busy);
            var policy = technicalModel.StructuredPolicy!; policy.ReadTimeoutMs = "77";
            var editor = Descendants(professional).OfType<ParameterSettingsControl>().Single(); var model = editor.Model;
            var authority = LocalManagementMode.ForRoot(launch.DataRoot);
            var clock = typeof(LocalManagementMode).GetProperty("Clock", BindingFlags.Instance | BindingFlags.NonPublic)!;
            clock.SetValue(authority, (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow.AddHours(1)));
            await Until(() => !technicalModel.IsManagementMode && !professional.Parameters.IsEnabled);
            Check(!professional.Parameters.IsEnabled && policy.ReadTimeoutMs == "77", "expired real grant locks the professional editor and preserves the same draft");
            CaptureInternal(evidence, "lia-mode-expired", professional, lines);
            clock.SetValue(authority, (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow)); await ManagementModeFixture.Authorize(app);
            await Until(() => technicalModel.IsManagementMode && professional.Parameters.IsEnabled);
            Check(ReferenceEquals(model, editor.Model) && policy.ReadTimeoutMs == "77", "reauthorization restores the same professional editor and context");
            policy.ReadTimeoutMs = (c.ReadTimeoutMs ?? 1000).ToString();
            professional.Close(); await Until(() => !professional.IsVisible);
            await ManagementModeFixture.Authorize(app);
            var snapshot = await app.SnapshotAsync(); await app.CommandAsync(new(Wire.Id(), snapshot.Revision, "Pause"));
            await Until(() => vm.Fresh);
            Check((await app.SnapshotAsync()).Availability?.Configure == true, "original Application permits the explicitly authorized candidate staging");
            var b = c with { TimeoutMs = c.TimeoutMs + 1 };
            var staged = await app.StageConfigurationAsync((await app.SnapshotAsync()).Configuration.Revision, b, "LIA native controlled activation");
            Check(staged.Status == "PendingRestart", "candidate B is saved into the existing pending ledger");
            await OpenFieldSettingsForVerification(window);
            await Until(() => vm.CanRequestActivation && vm.Snapshot?.HasPendingConfiguration == true);
            Check(vm.CanRequestActivation && vm.Snapshot!.Configuration.Pending!.TimeoutMs == b.TimeoutMs, "native activation is offered only after the saved candidate B is observed");
            await vm.RequestActivationAsync!();
            await CloseCompleted(() => stopCalls == 1 && window.IsEnabled);
            Check(window.IsVisible && stopCalls == 1 && reopen == 0 && vm.Message.Contains("仍持有资源"), "failed owner stop retains the original native window and prevents reopen; visible=" + window.IsVisible + "; reopen=" + reopen + "; message=" + vm.Message);
            var originalRequests = (await app.SnapshotAsync()).Operations.ToDictionary(o => o.Id, o => Wire.Encode(o.Wcs));
            Check(originalRequests.Count == 3, "three specific original operations exist before activation");
            await OpenFieldSettingsForVerification(window);
            await vm.RefreshAsync();
            await vm.RequestActivationAsync!();
            await CloseCompleted(() => !window.IsVisible && reopen == 1);
            Check(!window.IsVisible && stopCalls == 2 && reopen == 1 && replacement is not null, "successful resource release permits exactly one controlled replacement");
            var activated = await replacement!.SnapshotAsync();
            Check(activated.Configuration.Effective.TimeoutMs == b.TimeoutMs && activated.Configuration.Pending is null && activated.Operations.Select(o => o.Id).Order().SequenceEqual(originalRequests.Keys.Order()) && activated.Operations.All(o => o.FrozenConfiguration.TimeoutMs == c.TimeoutMs && Wire.Encode(o.Wcs) == originalRequests[o.Id]), "real restart reads back B and retains the complete original operation set and frozen requests on A");
            await ManagementModeFixture.Authorize(replacement);
            // The current product uses the SM workstation; paused classic-shell businesses are outside this activation fixture.
            await replacement.DisposeAsync(); replacement = null;
        }
        finally
        {
            if (replacement is not null) await replacement.DisposeAsync();
            if (window.IsVisible) { window.ReopenOwnerAsync = null; window.ConfirmOwnerExit = _ => true; window.ConfirmDiscardDrafts = _ => true; failStop = false; window.Close(); await Task.Delay(100); }
        }
        await VerifyCurrentDraftClose(evidence, c, Check, syntheticIdentity: true);
        lines.Add("BOUNDARY actual WPF/HWND/Dispatcher + real Application/SQLite; explicit synthetic ports and identity. UAC/system install/reboot/real SDK/RCS/target-touch/field NOT_RUN.");
        await VerifyRemovalPresentation();
        async Task CloseCompleted(Func<bool> done)
        {
            // Closing drains service calls asynchronously, with its own five-second
            // bound. Observe completion instead of assuming a 250/300ms CI schedule.
            for (var i = 0; i < 320; i++) { if (done()) return; await Task.Delay(25); }
            throw new TimeoutException("LIA native close did not complete; stopCalls=" + stopCalls + "; reopen=" + reopen + "; message=" + vm.Message);
        }
        async Task Until(Func<bool> done)
        {
            for (var i = 0; i < 150; i++) { await vm.RefreshAsync(); window.UpdateLayout(); if (done()) return; await Task.Delay(25); }
            throw new TimeoutException("LIA native state: " + vm.Message);
        }
        async Task VerifyRemovalPresentation()
        {
            var assembly = Assembly.LoadFrom(Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP")!);
            var type = assembly.GetType("Ipc.Desktop.ProductMaintenanceWindow", true)!; const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Window NewWindow()
            {
                var instance = (Window)Activator.CreateInstance(type)!;
                type.GetProperty("RegistrationIdentity", flags)!.SetValue(instance, (Func<string>)(() => "synthetic-registration"));
                type.GetProperty("RegistrationScope", flags)!.SetValue(instance, (Func<string>)(() => "synthetic-installation"));
                return instance;
            }
            var empty = NewWindow();
            await using var emptyOwner = new NativeWindowOwner(empty); empty.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); empty.UpdateLayout();
            Check(!Descendants(empty).OfType<TextBox>().Any(t => !t.IsReadOnly) && Descendants(empty).OfType<RadioButton>().Count() == 2,
                "uninstall offers one data choice without a backup path or typed confirmation");
            empty.Close();
            foreach (var status in new[] { "RestartRequired", "RemovedWithRetainedFiles", "RetiredCleanupPending" })
            {
                var receipt = new WindowsRemovalReceipt(status, "D:\\Synthetic\\original-remove.json", ["D:\\Synthetic\\retained-data"], status == "RestartRequired", ["app/locked.dll", "D:\\Synthetic\\extra-directory"], "D:\\Synthetic\\original-release");
                var result = NewWindow(); await using var resultOwner = new NativeWindowOwner(result);
                result.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                type.GetMethod("ShowResult", flags)!.Invoke(result, [new ProgramUninstallResult(receipt, null, false)]);
                result.UpdateLayout();
                Descendants(result).OfType<Expander>().Single(expander => Equals(expander.Header, "卸载详情")).IsExpanded = true;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); result.UpdateLayout();
                var text = string.Join("\n", Descendants(result).OfType<TextBlock>().Select(t => t.Text).Concat(Descendants(result).OfType<TextBox>().Select(t => t.Text)));
                Check(text.Contains("程序已卸载") && text.Contains("locked.dll") && text.Contains("retained-data") && !text.Contains("卸载完成"), "native removal receipt distinguishes " + status + " from clean removal while retaining original inventory evidence");
                Check(Descendants(result).OfType<Button>().Where(b => b.IsVisible).All(b => b.Content is "重试卸载" or "关闭"), "retired result offers fixed-inventory retry and close without removed-service authentication");
                CaptureInternal(evidence, "lia-removal-" + status, result, lines); result.Close();
            }
        }
    }
}
