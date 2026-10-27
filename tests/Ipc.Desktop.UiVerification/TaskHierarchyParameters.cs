using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyHierarchyParameters(string root, string evidence, List<string> lines, bool baseline, bool interactive = false)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); lines.Add("PASS HIERARCHY-PARAMETERS " + reason); }
        var initial = new SportmasterFlow().InitialConfiguration() with { DeferredConnections = null };
        var catalog = new Ipc.Assembly.ParcelCapabilityFactory(new BusinessLaunch(Path.Combine(root, "config/sportmaster"), Path.Combine(evidence, "data"), null, null)).Catalog();
        var draft = new StartupConfigurationDraft(initial, catalog);
        var model = draft.CreateParameterEditor((policy, codes) => Task.FromResult(Ipc.Application.ParcelFlow.BarcodeFiltering.Select(policy, codes)));
        foreach (var (reader, i) in model.Readers.Select((r, i) => (r, i)))
            foreach (var p in reader.Parameters) p.Value = p.Field.Key switch { "device.ip" => "192.0.2." + (11 + i), "device.port" => "9000", _ => p.Value };
        foreach (var component in model.Components.Where(ParameterOwnership.IsRcs))
        {
            component.Target = "http://192.0.2.10/rcs/sorter/api";
            foreach (var p in component.Parameters) p.Value = p.Field.Key switch
            { "source" => "SYNTHETIC", "locale" => "en-US", "zone-id" or "project-id" or "quantity" => "1", "request-id-base" => "1000000", "request-id-count" => "100", "left-station-code" or "right-station-code" => "TASK-STATION", "left-bcr-code" => "TASK-LEFT", "right-bcr-code" => "TASK-RIGHT", "specified-chute" => "false", _ => p.Value };
        }
        model.Policy.StationCode = "TASK-STATION";
        var editor = new ParameterSettingsControl { Model = model };
        var client = new Grid(); client.Children.Add(editor);
        var window = new Window { Content = client, Title = "合成参数候选 · 共用日常表单", Width = 1100, Height = 760 };
        NameScope.SetNameScope(window, new NameScope()); window.RegisterName("ClientArea", client);
        window.Show();
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        foreach (var size in new[] { (1366d, 768d), (800d, 420d) })
        {
            await SetClient(window, size.Item1, size.Item2);
            foreach (var page in new[] { "供包台标识", "左相机", "右相机" })
            {
                editor.ShowPage(page == "供包台标识" ? 0 : 2, page); await Flush();
                CaptureInternal(evidence, "parameters-" + page + "-" + (int)size.Item1, client, lines);
                if (!baseline) Check(Descendants(editor).OfType<TextBox>().Any(t => t.IsVisible && !t.IsReadOnly), page + " has real editable fields at " + size.Item1);
            }
        }
        var right = model.Readers.Single(r => r.Role == "right");
        var ip = right.Parameters.Single(p => p.Field.Key == "device.ip");
        ip.Value = "111"; model.ValidateFields(); editor.NavigateTo(right.InstanceId + "/device.ip"); await Flush();
        CaptureInternal(evidence, "parameters-right-error-800", client, lines);
        if (!baseline)
        {
            var input = Descendants(editor).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == right.InstanceId + "/device.ip");
            input.Focus(); input.SelectAll();
            var expander = Descendants(editor).OfType<Expander>().First(e => Equals(e.Header, "参数说明与诊断详情")); expander.IsExpanded = true;
            await Flush();
            Check(input.Text == "111" && model.Issues.Any(i => i.Path == right.InstanceId + "/device.ip"), "invalid input stays intact with its exact field issue");
            Check(model.Readers.All(r => r.Parameters.All(p => p.Field.Key != "bcr-code")) && model.Components.Single(ParameterOwnership.IsRcs).Parameters.Single(p => p.Field.Key == "left-bcr-code").Value == "TASK-LEFT", "one camera code is shared by task and heartbeat without a driver copy");
            Check(model.Policy.StationCode == "TASK-STATION" && model.Components.Single(ParameterOwnership.IsRcs).Parameters.Single(p => p.Field.Key == "left-station-code").Value == "TASK-STATION", "task and heartbeat station identifiers use the same confirmed value");
        }
        if (interactive)
        {
            await SetClient(window, 1092, 614);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult(); await closed.Task;
        }
        else window.Close();
        lines.Add("Shared native daily parameter form; synthetic candidate only, no save, connection or production configuration change.");
    }
}
