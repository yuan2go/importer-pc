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
        Check(draft.Readers.All(r=>r.Provider?.Implementation=="hikrobot-idmvs"&&r.Provider.Package is null),"fresh first setup uses built-in MvCodeReader without inventory");
        Check(rcs.Parameters.All(p=>p.Field.Key is not ("source" or "locale")),"fixed source/locale have no editor");
        var model=draft.CreateParameterEditor((p,c)=>Task.FromResult(BarcodeFiltering.Select(p,c)));
        var editor=new ParameterSettingsControl { Model=model };
        var window=new Window { Title="内置SDK参数验证", Content=editor, Width=1120, Height=760 }; window.Show();
        void Fields(ParameterSettingsControl control,CapabilityDraft reader) {
            var names=Descendants(control).OfType<FrameworkElement>().Select(AutomationProperties.GetName).ToArray();
            Check(names.Contains(reader.InstanceId+"/device.ip")&&names.Contains(reader.InstanceId+"/device.serial")&&names.Contains(reader.InstanceId+"/acquisition.mode"),"actual SDK IP/serial/trigger fields available");
            Check(!names.Contains(reader.InstanceId+"/device.port")&&!Descendants(control).OfType<TextBlock>().Any(t=>t.Text.Contains("独立驱动包")||t.Text.Contains("TCP空闲")),"no TCP fields or separate driver installation");
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
        } finally { window.Close(); }
        lines.Add("BOUNDARY real WPF and built-in descriptors; synthetic values, no device/barcode/site acceptance");
    }
}
