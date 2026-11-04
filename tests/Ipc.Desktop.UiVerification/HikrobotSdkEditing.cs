using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
static partial class Program
{
    static async Task VerifyHikrobotSdkEditing(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); lines.Add("PASS BUNDLED SDK NATIVE " + reason); }
        var config = new Ipc.Application.Sportmaster.SportmasterFlow().InitialConfiguration();
        var catalog = new Ipc.Assembly.ParcelCapabilityFactory(new(Path.Combine(root, "config/sportmaster"), evidence, null, null)).Catalog();
        var existingReaders = config.Topology!.Readers.Select(reader => reader with { Parameters = reader.Parameters!.Where(pair => pair.Key != "code.formats").ToDictionary(pair => pair.Key, pair => pair.Value) }).ToArray();
        var existing = config with { Reader = existingReaders[0], Topology = config.Topology with { Readers = existingReaders } };
        var existingBefore = Wire.Encode(existing);
        var existingDraft = new StartupConfigurationDraft(existing, catalog);
        Check(existingDraft.Readers.All(reader => Wire.Decode<string[]>(reader.Parameters.Single(p => p.Field.Key == "code.formats").Value).SequenceEqual(new[] { "All" })) && Wire.Encode(existing) == existingBefore,
            "opening an existing station without symbology settings retains its All fallback and does not mutate the original configuration");
        var draft = new StartupConfigurationDraft(config, catalog);
        var rcs = draft.Capabilities.Single(ParameterOwnership.IsRcs);
        rcs.Parameters.Single(p => p.Field.Key == "left-bcr-code").Value = "SYNTH-LEFT";
        rcs.Parameters.Single(p => p.Field.Key == "right-bcr-code").Value = "SYNTH-RIGHT";
        var left = draft.Readers[0]; var right = draft.Readers[1];
        for (var i = 0; i < draft.Readers.Length; i++)
        {
            draft.Readers[i].Parameters.Single(p => p.Field.Key == "device.ip").Value = "192.0.2." + (10 + i);
            draft.Readers[i].Parameters.Single(p => p.Field.Key == "device.serial").Value = "SYNTH-" + i;
        }
        Check(draft.Readers.All(r => r.Provider?.Implementation == "hikrobot-mvs-reader" && r.Provider.Package is null), "fresh first setup uses built-in MVS software reader without inventory");
        Check(rcs.Parameters.All(p => p.Field.Key is not ("source" or "locale")), "fixed source/locale have no editor");
        var model = draft.CreateParameterEditor((p, c) => Task.FromResult(BarcodeFiltering.Select(p, c)));
        var editor = new ParameterSettingsControl { Model = model };
        var window = new Window { Title = "内置SDK参数验证", Content = editor, Width = 1120, Height = 760 }; window.Show();
        void Fields(ParameterSettingsControl control, CapabilityDraft reader)
        {
            var names = Descendants(control).OfType<FrameworkElement>().Select(AutomationProperties.GetAutomationId).ToArray();
            Check(names.Contains(reader.InstanceId + "/device.ip") && names.Contains(reader.InstanceId + "/device.serial"), "actual SDK IP and serial fields available");
            Check(!names.Any(n => n == reader.InstanceId + "/acquisition.trigger" || n == reader.InstanceId + "/entity.mode" || n == reader.InstanceId + "/ConfigureLater") && !Descendants(control).OfType<FrameworkElement>().Any(e => AutomationProperties.GetAutomationId(e) == reader.InstanceId + "/RuntimeSummary"), "camera page has no trigger/result policy or configuration-state block");
            Check(!names.Contains(reader.InstanceId + "/device.port") && !Descendants(control).OfType<TextBlock>().Any(t => t.Text.Contains("独立驱动包") || t.Text.Contains("TCP")) &&
                !Descendants(control).OfType<TextBox>().Any(t => t.Text.Contains("TCP")), "camera page has no TCP copy, fields or separate driver installation");
            Check(Descendants(control).OfType<TextBox>().Any(t => AutomationProperties.GetAutomationId(t) == reader.InstanceId + "/Provider" && t.IsReadOnly && t.Text.Contains("海康相机")), "camera driver identifies the bundled Hikrobot camera");
            Check(Descendants(control).OfType<ComboBox>().Any(t => AutomationProperties.GetAutomationId(t) == reader.InstanceId + "/reader.engine" && t.Items.Count == 2), "each camera offers software and native recognition in the shared settings draft");
            Check(!Descendants(control).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b).EndsWith("/OpenPackageInstallation")), "no separate driver-install action");
        }
        try
        {
            editor.ShowPage(2, "左相机"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); Fields(editor, left);
            var formats = left.Parameters.Single(p => p.Field.Key == "code.formats");
            var formatSection = Descendants(editor).OfType<Expander>().Single(e => AutomationProperties.GetAutomationId(e) == left.InstanceId + "/code.formats");
            formatSection.IsExpanded = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var formatChecks = Descendants(formatSection).OfType<CheckBox>().ToArray();
            var initialFormats = new HashSet<string>(new[] { "Code128", "Code39", "EAN13" }, StringComparer.Ordinal);
            Check(formatChecks.Length == formats.Field.Choices!.Length &&
                initialFormats.SetEquals(formatChecks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag)) &&
                formatSection.Header is string header && header.StartsWith("当前：", StringComparison.Ordinal) && !header.Contains("全部码制") && Descendants(formatSection).OfType<TextBlock>().Any(t => t.Text == header),
                "new-site native selector exposes every declared format and selects only Code128, Code39 and EAN13");
            var rightFormats = right.Parameters.Single(p => p.Field.Key == "code.formats").Value;
            foreach (var extra in new[] { "Code39", "EAN13" }) formatChecks.Single(c => Equals(c.Tag, extra)).IsChecked = false;
            formatChecks.Single(c => Equals(c.Tag, "Code128")).IsChecked = true;
            formatChecks.Single(c => Equals(c.Tag, "QRCode")).IsChecked = true;
            Check(Wire.Decode<string[]>(formats.Value).SequenceEqual(new[] { "Code128", "QRCode" }) && formatChecks.Single(c => Equals(c.Tag, "All")).IsChecked == false,
                "native multi-selection clears All and writes both selected formats to the shared candidate");
            Check(draft.Build().Topology!.Readers[0].Parameters!["code.formats"] == formats.Value && right.Parameters.Single(p => p.Field.Key == "code.formats").Value == rightFormats,
                "saved full candidate isolates left format selection from right defaults");
            CaptureInternal(evidence, "bundled-sdk-format-selection", editor, lines);
            formatChecks.Single(c => Equals(c.Tag, "All")).IsChecked = true;
            Check(formatChecks.Count(c => c.IsChecked == true) == 1 && Wire.Decode<string[]>(formats.Value).SequenceEqual(new[] { "All" }), "choosing All clears explicit native choices");
            formatSection.IsExpanded = false;
            var engine = Descendants(editor).OfType<ComboBox>().Single(t => AutomationProperties.GetAutomationId(t) == left.InstanceId + "/reader.engine");
            engine.SelectedItem = "mvid-native";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            Check(left.Parameters.Single(p => p.Field.Key == "reader.engine").Value == "mvid-native" &&
                right.Parameters.Single(p => p.Field.Key == "reader.engine").Value == "zxing-cpp" &&
                !Descendants(editor).OfType<FrameworkElement>().Any(e => AutomationProperties.GetAutomationId(e) == left.InstanceId + "/code.try-invert"),
                "native selection updates only the left camera and hides the software-only inverted-code option");
            Check(draft.Build().Topology!.Readers[0].Parameters!["reader.engine"] == "mvid-native", "native selection is saved with the complete configuration candidate");
            CaptureInternal(evidence, "bundled-sdk-native-reader", editor, lines);
            Check(editor.NavigateTo(rcs.InstanceId + "/left-bcr-code"), "shared camera code is reached in the station task page");
            Descendants(editor).OfType<TextBox>().Single(t => AutomationProperties.GetAutomationId(t) == rcs.InstanceId + "/left-bcr-code").Text = "SYNTH-LEFT-EDITED";
            Check(rcs.Parameters.Single(p => p.Field.Key == "left-bcr-code").Value == "SYNTH-LEFT-EDITED" && rcs.Parameters.Single(p => p.Field.Key == "right-bcr-code").Value == "SYNTH-RIGHT", "one shared camera code editor isolates left/right");
            CaptureInternal(evidence, "bundled-sdk-daily-left", editor, lines);
            editor.ShowPage(2, "右相机"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Fields(editor, right);
            var saved = draft.Build(); Check(saved.Topology!.Readers[0].Parameters!["device.ip"] == "192.0.2.10" && saved.Topology.Readers[1].Parameters!["device.ip"] == "192.0.2.11", "full candidate saves both SDK dictionaries");
            editor.ShowPage(3, "RCS 服务"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            Check(!Descendants(editor).OfType<CheckBox>().Any(b => b.Content?.ToString()?.Contains("稍后配置") == true), "RCS page removes the configure-later checkbox");
            rcs.ConfigureLater = true; rcs.Target = "http://127.0.0.1:18848/rcs/sorter/api";
            var invalidRejected = false; try { draft.Build(); } catch (InvalidOperationException) { invalidRejected = true; }
            Check(invalidRejected, "filled RCS address still validates every required parameter");
            foreach (var pair in new Dictionary<string, string> { ["zone-id"] = "71", ["project-id"] = "83" })
                rcs.Parameters.Single(p => p.Field.Key == pair.Key).Value = pair.Value;
            Check(!rcs.ConfigureLater && !draft.Build().DeferredConnections!.Contains(rcs.InstanceId), "filled RCS address clears the obsolete deferred marker in the saved candidate");
            CaptureInternal(evidence, "rcs-without-deferred-checkbox", editor, lines);
            rcs.Target = ""; Check(rcs.ConfigureLater, "blank RCS remains explicitly unconfigured");
            var startup = new ParameterSettingsControl { FirstConfiguration = true, Model = model }; window.Content = startup;
            startup.ShowPage(2, "左相机"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); Fields(startup, left);
            CaptureInternal(evidence, "bundled-sdk-first-left", startup, lines);
            window.Width = 800; window.Height = 420; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); CaptureInternal(evidence, "bundled-sdk-first-compact", startup, lines);
            Check(ReferenceEquals(startup.Model, model), "compact first setup retains same full candidate");
            var oldReader = config.Reader with { Driver = "tcp-line", Parameters = new() { ["device.ip"] = "192.0.2.80", ["device.port"] = "2001" } };
            var old = config with { Reader = oldReader, Topology = config.Topology! with { Readers = config.Topology.Readers.Select(r => r.DeviceId == oldReader.DeviceId ? oldReader : r).ToArray() } };
            var oldDraft = new StartupConfigurationDraft(old, catalog);
            var oldCode = oldDraft.Capabilities.Single(ParameterOwnership.IsRcs).Parameters.Single(p => p.Field.Key == "left-bcr-code"); oldCode.Value = "SYNTH-MIGRATION";
            var oldControl = new ParameterSettingsControl { Model = oldDraft.CreateParameterEditor((p, c) => Task.FromResult(BarcodeFiltering.Select(p, c))) };
            window.Width = 1120; window.Height = 760; window.Content = oldControl; oldControl.ShowPage(2, "左相机"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(!Descendants(oldControl).OfType<TextBox>().Any(t => AutomationProperties.GetAutomationId(t).EndsWith("/device.port")) &&
                !Descendants(oldControl).OfType<ComboBox>().Any(t => AutomationProperties.GetAutomationId(t) == oldDraft.Readers[0].InstanceId + "/Provider"),
                "legacy TCP candidate shows one explicit SDK transition, never a blank selector or TCP port mixed with the SDK");
            Check(oldDraft.Readers[0].CaptureRaw().Original?.Implementation == "tcp-line" && oldDraft.Readers[0].Provider?.Implementation != "hikrobot-mvs-reader",
                "merely opening the page preserves the unsupported legacy binding without selecting the SDK provider");
            Check(!Descendants(oldControl).OfType<TextBlock>().Any(t => t.Text.Contains("TCP")) && !Descendants(oldControl).OfType<TextBox>().Any(t => t.Text.Contains("TCP")),
                "unsupported old settings request MVS SDK reconfiguration without presenting TCP as a connection option");
            Check(oldControl.NavigateTo("sportmaster.integration/left-bcr-code"), "legacy camera code remains addressable in the station task");
            Check(Descendants(oldControl).OfType<TextBox>().Count(t => AutomationProperties.GetAutomationId(t) == "sportmaster.integration/left-bcr-code") == 1, "legacy transition still exposes exactly one shared camera-code editor");
            oldControl.ShowPage(2, "左相机"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Descendants(oldControl).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == oldDraft.Readers[0].InstanceId + "/ApplyProviderChange").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); Fields(oldControl, oldDraft.Readers[0]);
            Check(oldDraft.Readers[0].Provider!.Implementation == "hikrobot-mvs-reader" && oldCode.Value == "SYNTH-MIGRATION" && oldDraft.Readers[1].Provider!.Implementation == "hikrobot-mvs-reader",
                "explicit transition activates SDK parameter editors while preserving camera code and other camera");
            CaptureInternal(evidence, "bundled-sdk-legacy-transition", oldControl, lines);
        }
        finally { window.Close(); }
        lines.Add("BOUNDARY real WPF and built-in descriptors; synthetic values, no device/barcode/site acceptance");
    }
}
