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
        var catalog = new Ipc.Assembly.ParcelCapabilityFactory(new(Path.Combine(root,"config/sportmaster"),evidence,null,null)).Catalog();
        var draft = new StartupConfigurationDraft(config,catalog);
        var rcs = draft.Capabilities.Single(ParameterOwnership.IsRcs);
        rcs.Parameters.Single(p=>p.Field.Key=="left-bcr-code").Value="SYNTH-LEFT";
        rcs.Parameters.Single(p=>p.Field.Key=="right-bcr-code").Value="SYNTH-RIGHT";
        var left=draft.Readers[0]; var right=draft.Readers[1];
        for(var i=0;i<draft.Readers.Length;i++) {
            draft.Readers[i].Parameters.Single(p=>p.Field.Key=="device.ip").Value="192.0.2."+(10+i);
            draft.Readers[i].Parameters.Single(p=>p.Field.Key=="device.serial").Value="SYNTH-"+i;
        }
        Check(draft.Readers.All(r=>r.Provider?.Implementation=="hikrobot-mvs-reader"&&r.Provider.Package is null),"fresh first setup uses built-in MVS software reader without inventory");
        Check(rcs.Parameters.All(p=>p.Field.Key is not ("source" or "locale")),"fixed source/locale have no editor");
        var model=draft.CreateParameterEditor((p,c)=>Task.FromResult(BarcodeFiltering.Select(p,c)));
        var editor=new ParameterSettingsControl { Model=model };
        var window=new Window { Title="内置SDK参数验证", Content=editor, Width=1120, Height=760 }; window.Show();
        void Fields(ParameterSettingsControl control,CapabilityDraft reader) {
            var names=Descendants(control).OfType<FrameworkElement>().Select(AutomationProperties.GetName).ToArray();
            Check(names.Contains(reader.InstanceId+"/device.ip")&&names.Contains(reader.InstanceId+"/device.serial"),"actual SDK IP and serial fields available");
            Check(!names.Any(n=>n==reader.InstanceId+"/acquisition.trigger"||n==reader.InstanceId+"/entity.mode"||n==reader.InstanceId+"/ConfigureLater") && !Descendants(control).OfType<FrameworkElement>().Any(e=>AutomationProperties.GetAutomationId(e)==reader.InstanceId+"/RuntimeSummary"),"camera page has no trigger/result policy or configuration-state block");
            Check(!names.Contains(reader.InstanceId+"/device.port")&&!Descendants(control).OfType<TextBlock>().Any(t=>t.Text.Contains("独立驱动包")||t.Text.Contains("TCP")) &&
                !Descendants(control).OfType<TextBox>().Any(t=>t.Text.Contains("TCP")),"camera page has no TCP copy, fields or separate driver installation");
            Check(Descendants(control).OfType<TextBox>().Any(t=>AutomationProperties.GetAutomationId(t)==reader.InstanceId+"/Provider" && t.IsReadOnly && t.Text.Contains("MVS SDK")),"camera driver identifies the fixed MVS SDK");
            Check(!Descendants(control).OfType<Button>().Any(b=>AutomationProperties.GetAutomationId(b).EndsWith("/OpenPackageInstallation")),"no separate driver-install action");
        }
        try {
            editor.ShowPage(2,"左相机"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); Fields(editor,left);
            Descendants(editor).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)==rcs.InstanceId+"/left-bcr-code").Text="SYNTH-LEFT-EDITED";
            Check(rcs.Parameters.Single(p=>p.Field.Key=="left-bcr-code").Value=="SYNTH-LEFT-EDITED"&&rcs.Parameters.Single(p=>p.Field.Key=="right-bcr-code").Value=="SYNTH-RIGHT","one shared camera code editor isolates left/right");
            CaptureInternal(evidence,"bundled-sdk-daily-left",editor,lines);
            editor.ShowPage(2,"右相机"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Fields(editor,right);
            var saved=draft.Build(); Check(saved.Topology!.Readers[0].Parameters!["device.ip"]=="192.0.2.10"&&saved.Topology.Readers[1].Parameters!["device.ip"]=="192.0.2.11","full candidate saves both SDK dictionaries");
            var startup=new ParameterSettingsControl { FirstConfiguration=true,Model=model }; window.Content=startup;
            startup.ShowPage(2,"左相机"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); Fields(startup,left);
            CaptureInternal(evidence,"bundled-sdk-first-left",startup,lines);
            window.Width=800;window.Height=420;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();CaptureInternal(evidence,"bundled-sdk-first-compact",startup,lines);
            Check(ReferenceEquals(startup.Model,model),"compact first setup retains same full candidate");
            var oldReader = config.Reader with { Driver = "tcp-line", Parameters = new() { ["device.ip"] = "192.0.2.80", ["device.port"] = "2001" } };
            var old = config with { Reader = oldReader, Topology = config.Topology! with { Readers = config.Topology.Readers.Select(r => r.DeviceId == oldReader.DeviceId ? oldReader : r).ToArray() } };
            var oldDraft = new StartupConfigurationDraft(old,catalog);
            var oldCode = oldDraft.Capabilities.Single(ParameterOwnership.IsRcs).Parameters.Single(p=>p.Field.Key=="left-bcr-code"); oldCode.Value="SYNTH-MIGRATION";
            var oldControl = new ParameterSettingsControl { Model=oldDraft.CreateParameterEditor((p,c)=>Task.FromResult(BarcodeFiltering.Select(p,c))) };
            window.Width=1120;window.Height=760;window.Content=oldControl;oldControl.ShowPage(2,"左相机");await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(!Descendants(oldControl).OfType<TextBox>().Any(t=>AutomationProperties.GetName(t).EndsWith("/device.port")) &&
                !Descendants(oldControl).OfType<ComboBox>().Any(t=>AutomationProperties.GetAutomationId(t)==oldDraft.Readers[0].InstanceId+"/Provider"),
                "legacy TCP candidate shows one explicit SDK transition, never a blank selector or TCP port mixed with the SDK");
            Check(oldDraft.Readers[0].Provider!.Implementation=="tcp-line","merely opening the page never rewrites legacy configuration");
            Check(!Descendants(oldControl).OfType<TextBlock>().Any(t=>t.Text.Contains("TCP")) && !Descendants(oldControl).OfType<TextBox>().Any(t=>t.Text.Contains("TCP")),
                "unsupported old settings request MVS SDK reconfiguration without presenting TCP as a connection option");
            Check(Descendants(oldControl).OfType<TextBox>().Count(t=>AutomationProperties.GetAutomationId(t)=="sportmaster.integration/left-bcr-code")==1,"legacy transition still exposes exactly one shared camera-code editor");
            Descendants(oldControl).OfType<Button>().Single(b=>AutomationProperties.GetAutomationId(b)==oldDraft.Readers[0].InstanceId+"/ApplyProviderChange").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();Fields(oldControl,oldDraft.Readers[0]);
            Check(oldDraft.Readers[0].Provider!.Implementation=="hikrobot-mvs-reader" && oldCode.Value=="SYNTH-MIGRATION" && oldDraft.Readers[1].Provider!.Implementation=="hikrobot-mvs-reader",
                "explicit transition activates SDK parameter editors while preserving camera code and other camera");
            CaptureInternal(evidence,"bundled-sdk-legacy-transition",oldControl,lines);
        } finally { window.Close(); }
        lines.Add("BOUNDARY real WPF and built-in descriptors; synthetic values, no device/barcode/site acceptance");
    }
}
