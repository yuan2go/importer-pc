using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Assembly;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyMaintenanceProgressNative(string evidence,List<string> lines)
    {
        void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);lines.Add("PASS WT048-NATIVE "+reason);}
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        var assembly=Assembly.LoadFrom(Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP")!);
        var type=assembly.GetType("Ipc.Desktop.ProductMaintenanceWindow",true)!;var parent=type.BaseType!;
        var window=(Window)Activator.CreateInstance(type)!;
        type.GetProperty("CurrentDeployment",flags)!.SetValue(window,(Func<DeploymentLocation?>)(()=>null));
        window.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var run=parent.GetMethod("Run",flags)!;
        var maintenance=parent.GetMethods(flags).Single(m=>m.Name=="RunMaintenance"&&!m.IsGenericMethod);
        var busy=parent.GetField("Busy",flags)!;
        var body=(FrameworkElement)parent.GetField("Body",flags)!.GetValue(window)!;
        var actions=(FrameworkElement)parent.GetField("Actions",flags)!.GetValue(window)!;
        var readout=Descendants(window).OfType<TextBox>().Single(x=>AutomationProperties.GetAutomationId(x)=="Maintenance.Progress");
        var cancel=Descendants(window).OfType<Button>().Single(x=>AutomationProperties.GetAutomationId(x)=="Maintenance.Cancel");
        Task Run(Func<IProgress<MaintenanceProgress>,CancellationToken,Task> operation)
            => (Task)run.Invoke(window,[(Func<Task>)(()=>(Task)maintenance.Invoke(window,["合成维护验证\n源：D:\\Synthetic\\original\n目标：D:\\Synthetic\\isolated\n原数据保留；不启动生产。",operation])!)])!;
        async Task Until(Func<bool> condition){for(var n=0;n<500;n++){if(condition())return;await Task.Delay(10);}throw new TimeoutException(readout.Text);}
        IProgress<MaintenanceProgress>? late=null;var calls=0;
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var running=Run(async(p,t)=>{calls++;late=p;p.Report(new(MaintenanceStage.CopyingCustody,4096,16384,true));using var subscription=t.Register(()=>requested.TrySetResult());entered.SetResult();await release.Task;throw new MaintenanceCancelledException("合成资源已释放，取消完成。");});
            await entered.Task;await Until(()=>readout.Text.Contains("4,096"));
            Check((bool)busy.GetValue(window)!&&!body.IsEnabled&&!actions.IsEnabled&&cancel.IsEnabled&&readout.IsEnabled&&readout.IsReadOnly,"real native window keeps source, target, byte observations and cancellation readable while ordinary actions are disabled");
            await Until(()=>Descendants(window).OfType<TextBlock>().Any(t=>t.Text.StartsWith("已等待 ")&&!t.Text.StartsWith("已等待 0 ")));
            Check((bool)busy.GetValue(window)!,"actual elapsed-time observations continue while the backend is deliberately waiting");
            window.Width=960;window.Height=760;window.UpdateLayout();CaptureInternal(evidence,"maintenance-progress-standard",window,lines);
            window.Width=800;window.Height=500;window.UpdateLayout();
            var bounds=cancel.TransformToAncestor(window).TransformBounds(new Rect(cancel.RenderSize));
            Check(cancel.ActualHeight>=44&&bounds.Bottom<window.ActualHeight&&readout.ActualHeight>=54,"supported minimum maintenance window retains the actual cancellation target and selectable status");
            CaptureInternal(evidence,"maintenance-progress-minimum",window,lines);
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await requested.Task;
            Check((bool)busy.GetValue(window)!&&!cancel.IsEnabled&&readout.Text.Contains("等待"),"requesting cancellation does not claim completion or release the busy owner");
            window.Close();Check(window.IsVisible,"real close guard rejects closing before operation cleanup completes");
            release.SetResult();await running;
            Check(!(bool)busy.GetValue(window)!&&body.IsEnabled&&readout.Text.Contains("提交前取消")&&!readout.Text.Contains("回滚成功"),"only the typed terminal cancellation releases the native operation and retains original context");
            var terminal=readout.Text;late!.Report(new(MaintenanceStage.CopyingCustody,16384,16384,true));await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(readout.Text==terminal&&!cancel.IsVisible,"late queued progress cannot overwrite the verified terminal result or reenable cancellation");
            var committed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var finish=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            running=Run(async(p,t)=>{p.Report(new(MaintenanceStage.PublishingRestore,null,null,false));committed.SetResult();await finish.Task;});await committed.Task;await Until(()=>readout.Text.Contains("不可取消"));
            Check(!cancel.IsEnabled&&(bool)busy.GetValue(window)!,"commit-stage observations disable cancellation while the real completion remains pending");finish.SetResult();await running;
            await Run((p,t)=>{calls++;throw new InstallationOutcomeUnknownException("合成回执中断");});var before=calls;
            await Run((p,t)=>{calls++;return Task.CompletedTask;});
            Check((bool)parent.GetField("UnknownOutcome",flags)!.GetValue(window)!&&calls==before&&readout.Text.Contains("结果待核对"),"the shared maintenance entry rejects a second submission after unknown outcome, including paths that bypass Action");
            CaptureInternal(evidence,"maintenance-unknown",window,lines);
        }
        finally{release.TrySetResult();window.Close();}
        await VerifyMaintenanceProtocol(lines);
    }
    static async Task VerifyMaintenanceProtocol(List<string> lines)
    {
        void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);lines.Add("PASS WT048-PROTOCOL "+reason);}
        var desktop=Assembly.LoadFrom(Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP")!);var type=Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(desktop.Location)!,"Ipc.Assembly.dll")).GetType("Ipc.Assembly.ParcelStartupServices",true)!;const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
        var read=type.GetMethod("ReadBoundMaintenanceResponse",flags)!;var complete=type.GetMethod("CompleteBoundMaintenanceResponse",flags)!;
        var hash=new string('A',64);var ready=0;var observations=0;
        string Frame(object value)=>JsonSerializer.Serialize(value)+"\n";
        var start=Frame(new{Status="Ready",RequestSha256=hash});
        var done=Frame(new{Status="Completed",RequestSha256=hash,Result="complete"});
        var ack=Frame(new{Status="CancelAck",RequestSha256=hash,Decision="Accepted"});
        var cancelled=Frame(new{Status="Cancelled",RequestSha256=hash});
        async Task<object> Read(string text)
        {using var input=new StringReader(text);var task=(Task)read.Invoke(null,[input,hash,true,(Action<WindowsInstallationStage>)(_=>{}),(Action<MaintenanceProgress>)(_=>observations++),(Func<Task>)(()=>{ready++;return Task.CompletedTask;}),CancellationToken.None])!;await task;return task.GetType().GetProperty("Result")!.GetValue(task)!;}
        object? Complete(object reply,int exit,bool requested)
        {try{return complete.Invoke(null,[reply,exit,requested]);}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw();throw;}}
        var reply=await Read(start+Frame(new{Status="MaintenanceProgress",RequestSha256=hash,Stage="CopyingDatabase",ProcessedBytes=4L,TotalBytes=8L,CanCancel=true})+done);
        Check((string?)Complete(reply,0,false)=="complete"&&ready==1&&observations==1,"request-bound ready, real observations and successful terminal decode without synthetic completion");
        reply=await Read(start+ack+cancelled);
        try{Complete(reply,3,true);throw new InvalidOperationException("Cancellation missing");}catch(MaintenanceCancelledException){Check(true,"cancellation requires an accepted request and exit code three");}
        foreach(var scenario in new[]{(start+cancelled,3,true),(start+ack+cancelled,0,true),(start+ack+cancelled,3,false),(start+ack+done,0,true),(start,0,false)})
        {try{Complete(await Read(scenario.Item1),scenario.Item2,scenario.Item3);throw new InvalidOperationException("Uncertain terminal accepted");}catch(InstallationOutcomeUnknownException){Check(true,"incomplete or contradictory cancellation evidence stays unknown");}}
        foreach(var invalid in new[]{done,start+start+done,Frame(new{Status="Ready",RequestSha256=new string('B',64)}),"{\"Status\":\"Ready\",\"Status\":\"Ready\",\"RequestSha256\":\""+hash+"\"}\n",start+"{",new string('x',65537)+"\n",start+Frame(new{Status="MaintenanceProgress",RequestSha256=hash,Stage="CopyingDatabase",ProcessedBytes=9L,TotalBytes=8L,CanCancel=true}),start+string.Concat(Enumerable.Repeat(Frame(new{Status="CancelAck",RequestSha256=hash,Decision="TooLate"}),2048))})
        {var refused=false;try{await Read(invalid);}catch(Exception e)when(e is IOException or JsonException or InvalidDataException){refused=true;}Check(refused,"wrong identity, order, duplicate field, truncation or bounded frame budget is rejected");}
        var exchange=type.GetMethod("ExchangeMaintenanceResponse",flags)!;
        foreach(var malformed in new[]{Frame(new{Status="Completed",RequestSha256=new string('B',64)}),Frame(new{Status="Unrecognized",RequestSha256=hash}),Frame(new{Status="MaintenanceProgress",RequestSha256=hash,Stage="Invalid"})})
        {
            using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(10));var name=Guid.NewGuid().ToString("N");
            using var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
            var connected=pipe.WaitForConnectionAsync(budget.Token);
            var peer=Task.Run(async()=>
            {
                using var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);await client.ConnectAsync(budget.Token);
                using var input=new StreamReader(client,new UTF8Encoding(false,true),true,1024,true);var output=new StreamWriter(client,new UTF8Encoding(false,true),1024,true){AutoFlush=true};
                try
                {
                    await output.WriteAsync(start);var begin=await input.ReadLineAsync(budget.Token);if(begin is null||!begin.Contains("Begin"))throw new IOException("No Begin");
                    await output.WriteAsync(malformed);
                    try{return await input.ReadLineAsync(budget.Token) is null;}catch(IOException){return true;}
                }
                finally{try{output.Dispose();}catch(IOException){}} // This peer deliberately receives an abort.
            });
            await connected;
            var task=(Task)exchange.Invoke(null,[pipe,hash,true,(Action<WindowsInstallationStage>)(_=>{}),(Action<MaintenanceProgress>)(_=>{}),CancellationToken.None,budget.Token])!;
            var rejected=false;try{await task;}catch(Exception error)when(error is IOException or InvalidDataException){rejected=true;}
            Check(rejected&&await peer,"production exchange immediately closes a malformed real duplex connection and the peer observes EOF within the test budget");
        }
        var dispose=type.GetMethod("DisposeMaintenanceWriter",flags)!;
        for(var outcome=0;outcome<3;outcome++)
        {
            using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(10));var name=Guid.NewGuid().ToString("N");
            using var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
            var connected=pipe.WaitForConnectionAsync(budget.Token);using var peer=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);await peer.ConnectAsync(budget.Token);await connected;
            var writer=new StreamWriter(pipe,new UTF8Encoding(false,true),1024,true){AutoFlush=true};peer.Dispose();
            var failed=false;try{await writer.WriteLineAsync(JsonSerializer.Serialize(new{Command="Cancel",RequestSha256=hash}));}catch(IOException){failed=true;}
            Check(failed,"actual disconnected duplex pipe rejects a late cancellation write");
            dispose.Invoke(null,[writer,true,false]);
            var terminal=outcome==1?Frame(new{Status="Failed",RequestSha256=hash,Error="actual failed terminal"}):done;
            reply=await Read(start+terminal);
            if(outcome==0)Check((string?)Complete(reply,0,true)=="complete","late writer cleanup cannot replace completed terminal plus actual success exit");
            else if(outcome==1){try{Complete(reply,2,true);throw new InvalidOperationException("Failure lost");}catch(IOException e)when(e is not InstallationOutcomeUnknownException){Check(true,"late writer cleanup preserves an explicit failed terminal");}}
            else{try{Complete(reply,2,true);throw new InvalidOperationException("Exit mismatch accepted");}catch(InstallationOutcomeUnknownException){Check(true,"cleanup tolerance never relaxes terminal and actual exit-code agreement");}}
        }
        reply=await Read(Frame(new{Status="Failed",RequestSha256=hash,Error="authority rejected"}));
        try{Complete(reply,2,false);throw new InvalidOperationException("Pre-ready failure accepted");}catch(IOException e)when(e is not InstallationOutcomeUnknownException){Check(true,"authenticated pre-ready authority refusal remains a known failure");}
    }
}
