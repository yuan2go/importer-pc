using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyFirstPreferenceSave(string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS FIRST-PREFERENCES " + text); }
        var desktop = Assembly.LoadFrom(Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP") ?? throw new InvalidOperationException("Set same-build desktop assembly"));
        var type = desktop.GetType("Ipc.Desktop.StartupRepairWindow", true)!;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var busy = type.BaseType!.GetField("Busy", flags)!;
        var launch = new BusinessLaunch(Path.Combine(evidence,"first-preference-config"),Path.Combine(evidence,"first-preference-data"),null,null);
        ManagementModeFixture.AuthorizeRoot(launch.DataRoot);
        var first = (Window)Activator.CreateInstance(type,[launch,null,true])!;
        type.GetProperty("ConfirmDiscardInputs",flags)!.SetValue(first,(Func<bool>)(()=>true));
        var modal = first.Dispatcher.BeginInvoke(new Action(()=>first.ShowDialog()));
        async Task Flush() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        async Task Until(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while(!condition()) { if(DateTime.UtcNow>=deadline) throw new TimeoutException("First preference save did not settle"); await Task.Delay(20); await Flush(); }
        }
        try
        {
            var editor = await FirstConfigurationReady(first,busy,Check);
            var maintenanceField=type.GetField("maintenance",flags)!;
            var proxy=DispatchProxy.Create<IStartupMaintenance,FirstPreferenceMaintenanceProbe>();
            var probe=(FirstPreferenceMaintenanceProbe)(object)proxy;
            // Control only the Windows elevation/installed-helper boundary. The
            // actual Application, filesystem, complete candidate and CAS run below.
            await ((IStartupMaintenance)maintenanceField.GetValue(first)!).DisposeAsync();
            probe.Inner=new Ipc.Application.ParcelFlow.ParcelStartupMaintenance(launch,new Ipc.Application.Sportmaster.SportmasterFlow(),
                ()=>new Ipc.Assembly.ParcelCapabilityFactory(launch));
            lines.Add("BOUNDARY Windows elevation/installed helper not run; real isolated Application/CAS persistence below");
            maintenanceField.SetValue(first,proxy);
            var draft=(StartupConfigurationDraft)type.GetField("draft",flags)!.GetValue(first)!;
            var preferences=draft.DisplayPreferences!;
            preferences.OpeningMode=PresentationPreferences.WindowOpeningMode.FullScreen; preferences.ShowPace=false; preferences.CueVolume=25;
            var rootField=typeof(PresentationPreferences).GetField("UserRootForVerification",BindingFlags.Static|BindingFlags.NonPublic)!;
            var root=((Func<string>)rootField.GetValue(null)!)();
            var file=Path.Combine(root,"presentation","sportmaster.pace.json"); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file,"{broken");
            editor.ShowReview(); await Flush(); first.UpdateLayout();
            Button Save() => Descendants(first).OfType<Button>().Single(b=>b.IsVisible && Equals(b.Content,"保存配置"));
            Save().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(()=>!(bool)busy.GetValue(first)! && probe.Saves>0);
            Check(first.IsVisible && (bool)type.GetField("lastSaveConfirmed",flags)!.GetValue(first)! && !(bool)type.GetField("startupSaveUncertain",flags)!.GetValue(first)! && probe.Saves==1,
                "actual first-configuration workflow acknowledges business persistence before a local preference failure and remains open; facts=" + Wire.Encode(new { first.IsVisible, Confirmed=type.GetField("lastSaveConfirmed",flags)!.GetValue(first), Uncertain=type.GetField("startupSaveUncertain",flags)!.GetValue(first), probe.Saves, Status=((TextBlock)type.BaseType!.GetField("Status",flags)!.GetValue(first)!).Text }));
            var configFile=Path.Combine(launch.ConfigurationDirectory,"business.json"); var hash=Wire.FileHash(configFile);
            Check(preferences.HasChanges && File.ReadAllText(file)=="{broken" && preferences.LastSave.Single(x=>x.Path=="ShowPace").State=="Unknown" && PresentationPreferences.ReadCueVolume("sportmaster")==25,
                "first configuration keeps the failed original file/draft and reports independent successful preference writes");
            var status=(TextBlock)type.BaseType!.GetField("Status",flags)!.GetValue(first)!;
            await Flush(); first.UpdateLayout();
            Check(status.IsVisible && status.Text.Contains("业务配置已保存",StringComparison.Ordinal) && status.Text.Contains("效率显示",StringComparison.Ordinal)
                && ((TextBlock)type.GetField("saveState",flags)!.GetValue(first)!).Text=="业务配置已保存；本机偏好待保存",
                "partial save shows the actual successful and uncertain items above the scrolling form, with an accurate footer");
            CaptureInternal(evidence,"first-preference-partial",first,lines);
            File.WriteAllText(file,Wire.Encode(new { Schema=1,Compact=true }));
            Save().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(()=>!first.IsVisible);
            Check(probe.Saves==1 && Wire.FileHash(configFile)==hash && !preferences.HasChanges && PresentationPreferences.Read("sportmaster","pace")==false && (bool)type.GetProperty("ConfigurationSaved")!.GetValue(first)!,
                "retry completes the real modal first configuration without another business save or changing its persisted bytes");
            Check(!Directory.EnumerateFiles(launch.DataRoot,"*.db",SearchOption.AllDirectories).Any(),
                "saving first configuration/preferences does not create a business database or operate devices");
            await modal.Task;
        }
        finally { if(first.IsVisible) first.Close(); }
    }
    public class FirstPreferenceMaintenanceProbe : DispatchProxy
    {
        public IStartupMaintenance Inner=null!;
        public int Saves;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if(method!.Name is nameof(IStartupMaintenance.RepairInitialAsync) or nameof(IStartupMaintenance.StageAsync)) Interlocked.Increment(ref Saves);
            try { return method.Invoke(Inner,args); }
            catch(TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException!).Throw(); throw; }
        }
    }
}
