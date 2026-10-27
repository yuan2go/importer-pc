using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Assembly;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyParameterGuidanceStartup(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); lines.Add("PASS PGS01-WPF " + reason); }
        async Task Flush() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var initial = new SportmasterFlow().InitialConfiguration();
        var launch = new BusinessLaunch(Path.Combine(evidence, "config"), Path.Combine(evidence, "data"), null, null);
        await using var factory = new ParcelCapabilityFactory(launch);
        var draft = new StartupConfigurationDraft(initial, factory.Catalog());
        var model = draft.CreateParameterEditor((p, codes) => Task.FromResult(BarcodeFiltering.Select(p, codes)));
        var editor = new ParameterSettingsControl { FirstConfiguration = true, Model = model };
        var client = new Grid(); client.Children.Add(editor);
        var window = new Window { Content = client, Title = "合成首配参数 · PGS01", Width = 1100, Height = 740 };
        NameScope.SetNameScope(window, new NameScope()); window.RegisterName("ClientArea", client);
        var overrideRoot = typeof(PresentationPreferences).GetField("UserRootForVerification", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldOverride = overrideRoot.GetValue(null); overrideRoot.SetValue(null, (Func<string>)(() => Path.Combine(evidence, "user-preferences")));
        var preference = Path.Combine(evidence, "user-preferences", "presentation", "sportmaster.window.json");
        Check(Path.GetPathRoot(preference)!.Equals(Path.GetPathRoot(evidence), StringComparison.OrdinalIgnoreCase), "local preference is isolated under the evidence root");
        var previous = File.Exists(preference) ? File.ReadAllBytes(preference) : null;
        SupplyStationWindow? workstation = null;
        try
        {
            window.Show(); await Flush();
            for (var count = 0; !editor.IsReviewPage && count < 25; count++)
                Check(await editor.MoveNextAsync(), "first-run page can advance with explicitly deferred camera/RCS fields: " + editor.CurrentPageTitle);
            Check(editor.IsReviewPage, "incomplete connections do not block the first-run completion page");
            foreach (var size in new[] { (1366d, 768d), (1092d, 614d), (800d, 420d) })
            {
                await SetClient(window, size.Item1, size.Item2);
                foreach (var (category, title) in new[] { (1, "格口请求"), (1, "接收与等待限制"), (0, "供包台标识"), (0, "数据与记录"), (0, "显示偏好"), (2, "左相机"), (3, "RCS 服务") })
                {
                    Check(editor.ShowPage(category, title), "existing shared directory contains " + title);
                    await Flush(); window.UpdateLayout();
                    if (title == "格口请求")
                    {
                        Check(!Descendants(editor).OfType<TextBox>().Any(t => AutomationProperties.GetAutomationId(t).EndsWith("/quantity")), "no configurable quantity editor remains");
                        var help = Descendants(editor).OfType<Expander>().First(e => Equals(e.Header, "参数说明与诊断详情")); help.IsExpanded = true; await Flush();
                        Check(!Descendants(help).OfType<TextBox>().Any(t => t.Text.Contains("sportmaster.integration/quantity")), "parameter help describes purpose without an internal configuration path");
                    }
                    CaptureInternal(evidence, "pgs-" + title + "-" + (int)size.Item1, client, lines);
                }
            }
            editor.ShowPage(0, "供包台标识"); await Flush();
            var input = Descendants(editor).OfType<TextBox>().Single(t => AutomationProperties.GetAutomationId(t) == "sportmaster.integration/left-station-code");
            input.Text = "SYNTHETIC-SHARED"; await Flush();
            Check(draft.Policy.StationCode == input.Text && draft.Capabilities.Single(ParameterOwnership.IsRcs).Parameters.Where(p => p.Field.Key.EndsWith("-station-code")).All(p => p.Value == input.Text), "one real WPF input updates request and heartbeat station identities");
            Check(!Descendants(editor).OfType<TextBox>().Any(t => AutomationProperties.GetAutomationId(t) == "StationCode"), "no second heartbeat station editor remains");
            Check(draft.Policy.DiagnosticLogDays == "7" && draft.Policy.HistoryDays == "7", "first-run retention editors contain seven days");
            editor.ShowPage(2, "左相机"); await Flush();
            Check(!Descendants(editor).OfType<ComboBox>().Any(t => AutomationProperties.GetAutomationId(t).EndsWith("/entity.mode") && t.IsEnabled), "current MVS acquisition has no editable business-association choice");
            var deferred = Descendants(editor).OfType<CheckBox>().Single(t => AutomationProperties.GetAutomationId(t) == draft.Readers[0].InstanceId + "/ConfigureLater");
            deferred.IsChecked = false; await Flush();
            Check(!model.ValidateFields() && model.Issues.Any(i => i.Path.EndsWith("/device.ip")), "turning off deferred mode exposes required camera errors");
            deferred.IsChecked = true; await Flush();
            Check(model.ValidateFields(), "reselecting deferred mode preserves inputs and removes inapplicable connection errors");
            await VerifyIndependentDeferredPreflight(evidence, window, editor, draft, model, lines);
            editor.ShowPage(0, "显示偏好"); await Flush();
            var modes = Descendants(editor).OfType<ComboBox>().Single(t => AutomationProperties.GetAutomationId(t) == "WindowOpeningMode");
            modes.SelectedValue = PresentationPreferences.WindowOpeningMode.FullScreen;
            Descendants(editor).OfType<Button>().Single(b => Equals(b.Content, "保存打开方式")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(PresentationPreferences.ReadWindowMode("sportmaster") == PresentationPreferences.WindowOpeningMode.FullScreen, "the visible first-run mode control persists the full-screen preference");
            var candidate = draft.Build() with { Mode = RunMode.Simulation, StationId = "pgs-wpf-" + Wire.Id() };
            Directory.CreateDirectory(launch.ConfigurationDirectory); ExtensionStore.AtomicWrite(Path.Combine(launch.ConfigurationDirectory, "business.json"), candidate);
            await using var app = new ParcelRuntime(launch, new SportmasterFlow(), new ParcelCapabilityFactory(launch)) { RuntimeIdentityPreflight = () => { } };
            await app.StartAsync(default);
            workstation = new SupplyStationWindow(app, null, "standard") { ConfirmDiscardDrafts = _ => true };
            workstation.Show(); await Flush(); await workstation.ViewModel.RefreshAsync();
            Check(workstation.WindowStyle == WindowStyle.None && workstation.WindowState == WindowState.Maximized && workstation.ResizeMode == ResizeMode.NoResize,
                "the actual workstation opens in game-style borderless full screen");
            Check(workstation.ViewModel.Snapshot!.Availability is { Admit: false, Resume: false }, "the open native workstation retains Application admission guards");
            CaptureInternal(evidence, "pgs-workstation-fullscreen", (FrameworkElement)workstation.FindName("ClientArea"), lines);
            workstation.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(workstation), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await Flush(); Check(workstation.WindowStyle != WindowStyle.None && workstation.ResizeMode != ResizeMode.NoResize, "Escape returns the real HWND to an ordinary resizable window");
            var toggle = (RoutedCommand)workstation.InputBindings.OfType<KeyBinding>().Single(b => b.Key == Key.F11).Command;
            toggle.Execute(null, workstation); await Flush();
            Check(workstation.WindowStyle == WindowStyle.None && workstation.WindowState == WindowState.Maximized, "F11 enters full screen through the native window command");
            toggle.Execute(null, workstation); await Flush();
            Check(workstation.WindowStyle != WindowStyle.None, "F11 also exits full screen without changing business state");
            workstation.Close(); await Flush(); await app.StopAsync(default);
        }
        finally
        {
            window.Close(); workstation?.Close();
            if (previous is null) { if (File.Exists(preference)) File.Delete(preference); } else File.WriteAllBytes(preference, previous);
            overrideRoot.SetValue(null, oldOverride);
        }
    }

    static async Task VerifyIndependentDeferredPreflight(string evidence, Window window, ParameterSettingsControl editor,
        StartupConfigurationDraft draft, ParameterEditorModel model, List<string> lines)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); lines.Add("PASS DEFERRED-WPF " + reason); }
        var original = draft.Capture();
        var launch = new BusinessLaunch(Path.Combine(evidence, "deferred-preflight-config"), Path.Combine(evidence, "deferred-preflight-data"), null, null);
        var offline = new ParcelStartupMaintenance(launch, new SportmasterFlow(), () => new ParcelCapabilityFactory(launch), () => { });
        try
        {
            for (var mask = 1; mask <= 7; mask++)
            {
                for (var i = 0; i < 2; i++)
                {
                    foreach (var p in draft.Readers[i].Parameters)
                        p.Value = p.Field.Key switch
                        {
                            "device.ip" => "192.0.2." + (10 + i), "device.serial" => "SYNTH-SERIAL-" + i,
                            _ => p.Value
                        };
                    Check(editor.ShowPage(2, i == 0 ? "左相机" : "右相机"), "camera page is reachable");
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Descendants(editor).OfType<CheckBox>().Single(t => AutomationProperties.GetAutomationId(t) == draft.Readers[i].InstanceId + "/ConfigureLater").IsChecked = (mask & (1 << i)) != 0;
                }
                var rcs = draft.Capabilities.Single(ParameterOwnership.IsRcs);
                rcs.Target = "http://127.0.0.1:24999/rcs/sorter/api";
                foreach (var p in rcs.Parameters)
                    p.Value = p.Field.Key switch
                    {
                        "zone-id" => (mask & 4) != 0 ? "" : "71", "project-id" => "83",
                        "specified-chute" => "false", "heartbeat-interval-seconds" => "5",
                        "left-bcr-code" => (mask & 1) != 0 ? "" : "SYNTH-LEFT", "right-bcr-code" => (mask & 2) != 0 ? "" : "SYNTH-RIGHT",
                        "request-id-base" => "810000000", "request-id-count" => "100000", _ => p.Value
                    };
                Check(editor.ShowPage(3, "RCS 服务"), "RCS page is reachable");
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Descendants(editor).OfType<CheckBox>().Single(t => AutomationProperties.GetAutomationId(t) == rcs.InstanceId + "/ConfigureLater").IsChecked = (mask & 4) != 0;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(model.ValidateFields(), "mask " + mask + " native checkbox changes pass shared field validation: " + string.Join("; ", model.Issues.Select(i => i.Path + " " + i.Message)));
                var report = await offline.CheckAsync(draft.Build());
                Check(report.Checks.All(c => c.Status != "FAIL"), "mask " + mask + " native candidate passes actual offline preflight with independent deferrals");
                if (mask == 1) CaptureInternal(evidence, "deferred-left-active-rcs", editor, lines);
            }
            var activeRcs = draft.Capabilities.Single(ParameterOwnership.IsRcs);
            activeRcs.ConfigureLater = false;
            activeRcs.Parameters.Single(p => p.Field.Key == "zone-id").Value = "71";
            draft.Readers[0].ConfigureLater = false;
            var incomplete = await offline.CheckAsync(draft.Build());
            Check(incomplete.Checks.Any(c => c.Status == "FAIL" && c.Reason.Contains("bcr-code", StringComparison.Ordinal)),
                "restoring a camera requires its real identity at preflight; stale deferred blank is refused");
        }
        finally { draft.Restore(original); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    }
}
