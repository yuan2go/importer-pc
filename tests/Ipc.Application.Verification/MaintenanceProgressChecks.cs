using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;

static class MaintenanceProgressChecks
{
    sealed class Observer(Action<MaintenanceProgress> report):IProgress<MaintenanceProgress>{public void Report(MaintenanceProgress p)=>report(p);}
    static void Check(bool yes,string reason){if(!yes)throw new Exception("FAIL WT048 "+reason);Console.WriteLine("PASS WT048 "+reason);}
    static async Task Cancelled(Func<Task> run,string reason)
    {try{await run();throw new Exception("Expected cancellation: "+reason);}catch(MaintenanceCancelledException){Check(true,reason);}}
    static void Released(string directory){using var lease=new ResourceLease(directory,["application-owner"]);Check(true,"completed cancellation/failure releases actual original application ownership");}
    public static async Task Run(string workspace,string root)
    {
        Directory.CreateDirectory(root);
        using(var gate=new MaintenanceExecution())
        using(var flagged=new ManualResetEventSlim())
        using(var signal=new ManualResetEventSlim())
        {
            gate.BeforeCancellationSignalForVerification=()=>{flagged.Set();if(!signal.Wait(TimeSpan.FromSeconds(10)))throw new TimeoutException();};
            var cancel=Task.Run(gate.TryCancel);
            try
            {
                Check(flagged.Wait(TimeSpan.FromSeconds(10)),"cancel wins the decision lock before CTS signalling");
                try{gate.BeginCommit(MaintenanceStage.PublishingBackup);throw new Exception("Commit overtook accepted cancellation");}
                catch(OperationCanceledException){Check(!gate.Committing,"commit cannot overtake accepted cancellation even before its token is signalled");}
            }
            finally{signal.Set();}
            Check(await cancel,"accepted cancellation completes its signal before disposal");
        }
        using(var gate=new MaintenanceExecution())
        {gate.BeginCommit(MaintenanceStage.RetiringDeployment);Check(!gate.TryCancel()&&!gate.Token.IsCancellationRequested,"commit winner rejects cancellation without interrupting durable work");gate.Finish();Check(!gate.TryCancel(),"terminal operation rejects late cancellation");}
        var observations=new List<MaintenanceProgress>();
        using(var gate=new MaintenanceExecution(new Observer(observations.Add)))
        {
            for(var i=0;i<=100000;i++)gate.Report(MaintenanceStage.CopyingCustody,i,100000);
            Check(observations.Count<=128&&observations[^1].ProcessedBytes==100000,"one hundred thousand copy observations remain bounded before any UI/pipe queue");
        }
        observations.Clear();
        using(var gate=new MaintenanceExecution(new Observer(observations.Add)))
        {
            gate.Report(MaintenanceStage.CopyingSnapshot,100,100);gate.Report(MaintenanceStage.Verifying);gate.Report(MaintenanceStage.CopyingSnapshot,1,100);
            Check(observations[^1].Stage==MaintenanceStage.CopyingSnapshot&&observations[^1].ProcessedBytes==1,"a repeated preparation phase publishes its own real progress instead of retaining the earlier verification stage");
            for(var i=0;i<100000;i++)gate.Report(i%2==0?MaintenanceStage.Checking:MaintenanceStage.Verifying,i,100000);
            gate.BeginCommit(MaintenanceStage.PublishingBackup);
            Check(observations.Count<1800&&!observations[^1].CanCancel,"adversarial stage oscillation remains below the pipe budget and preserves the commit observation");
        }
        var inventoryRoot=Path.Combine(root,"enumeration");Directory.CreateDirectory(Path.Combine(inventoryRoot,"receipts"));
        File.WriteAllText(Path.Combine(inventoryRoot,"receipts","one.json"),"{}");File.WriteAllText(Path.Combine(inventoryRoot,"receipts","two.json"),"{}");
        using(var cancellation=new CancellationTokenSource())
        using(var files=RecoveryFiles.Enumerate(inventoryRoot,"receipts",false,cancellation.Token).GetEnumerator())
        {
            Check(files.MoveNext(),"real recovery inventory exposes its first ordinary file");cancellation.Cancel();
            try{files.MoveNext();throw new Exception("Enumeration ignored cancellation");}catch(OperationCanceledException){Check(true,"cancellation interrupts the real inventory between entries without dropping validation");}
        }
        var config=Path.Combine(root,"config");Directory.CreateDirectory(config);
        var seed=Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace,"tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        ExtensionStore.AtomicWrite(Path.Combine(config,"business.json"),seed);
        var launch=new BusinessLaunch(config,Path.Combine(root,"data"),null,null);var deviceConstructions=0;
        IParcelCapabilityFactory Factory()=>new ParcelCapabilityFactory(launch){Boundary=_=>{deviceConstructions++;throw new Exception("Offline maintenance constructed a device");}};
        var service=new ParcelStartupMaintenance(launch,new SportmasterFlow(),Factory,()=>{});
        var read=await service.ReadAsync();var staged=await service.StageAsync(read.ConfigurationHash,read.Candidate! with{TimeoutMs=read.Candidate!.TimeoutMs+1},"synthetic maintenance fixture");
        var directory=staged.RuntimeDirectory!;using(var repository=new ParcelRepository(directory,seed.ApplicationId)){}
        var original=Path.Combine(directory,"receipts","large.synthetic");Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        using(var bytes=File.Create(original)){var chunk=new byte[65536];for(var i=0;i<128;i++)bytes.Write(chunk);}
        var database=Path.Combine(directory,seed.ApplicationId+".db");var databaseHash=Wire.FileHash(database);var originalHash=Wire.FileHash(original);var configHash=Wire.FileHash(Path.Combine(directory,"configuration.json"));
        void Originals()=>Check(Wire.FileHash(database)==databaseHash&&Wire.FileHash(original)==originalHash&&Wire.FileHash(Path.Combine(directory,"configuration.json"))==configHash,"original database, custody and configuration bytes remain unchanged");
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();var target=Path.Combine(root,"pre-cancel.db");
            await Cancelled(()=>service.BackupAsync(target,null,cancellation.Token),"pre-cancelled backup creates no destination");Check(!File.Exists(target)&&!Directory.Exists(target+".recovery"),"pre-cancellation never publishes a recovery set");
        }
        using(var cancellation=new CancellationTokenSource())
        {
            var target=Path.Combine(root,"snapshot-cancel.db");
            await Cancelled(()=>service.BackupAsync(target,new Observer(p=>{if(p.Stage==MaintenanceStage.CopyingSnapshot&&p.ProcessedBytes>0)cancellation.Cancel();}),cancellation.Token),"actual bounded snapshot copy observes cancellation");
            Check(!File.Exists(target+".recovery/COMPLETED.json"),"snapshot cancellation cannot advertise a completed backup");Released(directory);Originals();
        }
        using(var cancellation=new CancellationTokenSource())
        {
            var target=Path.Combine(root,"database-cancel.db");
            await Cancelled(()=>service.BackupAsync(target,new Observer(p=>{if(p.Stage==MaintenanceStage.CopyingDatabase&&p.ProcessedBytes>0)cancellation.Cancel();}),cancellation.Token),"real SQLite copy step observes the same operation cancellation");
            Check(File.Exists(target)&&!File.Exists(target+".recovery/COMPLETED.json"),"cancelled database copy retains an incomplete target without publishing success");Released(directory);Originals();
        }
        using(var cancellation=new CancellationTokenSource())
        using(var copying=new ManualResetEventSlim())
        using(var resume=new ManualResetEventSlim())
        {
            var first=true;
            var task=Task.Run(()=>service.BackupAsync(Path.Combine(root,"slow-cancel.db"),new Observer(p=>
            {if(first&&p.Stage==MaintenanceStage.CopyingSnapshot&&p.ProcessedBytes>0){first=false;copying.Set();if(!resume.Wait(TimeSpan.FromSeconds(10)))throw new TimeoutException();}}),cancellation.Token));
            try
            {
                Check(await Task.Run(()=>copying.Wait(TimeSpan.FromSeconds(10))),"slow real snapshot copy exposes a live preparation phase");
                try{await service.BackupAsync(Path.Combine(root,"parallel.db"));throw new Exception("Concurrent owner admitted");}
                catch(IOException){Check(true,"second maintenance cannot enter while original operation owns the source");}
                cancellation.Cancel();
            }
            finally{resume.Set();}
            await Cancelled(()=>task,"slow cancellation waits until actual copy work and ownership finish");Released(directory);
        }
        var authorized=true;
        var revocable=new ParcelStartupMaintenance(launch,new SportmasterFlow(),Factory,()=>{if(!authorized)throw new UnauthorizedAccessException("synthetic revocation");});
        var revokedTarget=Path.Combine(root,"revoked.db");
        try{await revocable.BackupAsync(revokedTarget,new Observer(p=>{if(p.Stage==MaintenanceStage.CopyingSnapshot&&p.ProcessedBytes>0)authorized=false;}),default);throw new Exception("Revoked maintenance continued");}
        catch(UnauthorizedAccessException){Check(!File.Exists(revokedTarget+".recovery/COMPLETED.json"),"authority revocation before publication remains refusal rather than completion or user cancellation");}
        Released(directory);Originals();
        using(var cancellation=new CancellationTokenSource())
        {
            await Cancelled(()=>service.PlanChangeAsync("Uninstall",Path.Combine(root,"planned.db"),new Observer(p=>{if(p.Stage==MaintenanceStage.Verifying)cancellation.Cancel();}),cancellation.Token),"long original-responsibility preflight shares cancellation before entering the original scan");Released(directory);Originals();
        }
        var prepareTarget=Path.Combine(root,"prepare-complete.db");var preparePlan=await service.PlanChangeAsync("Repair",prepareTarget);
        using(var cancellation=new CancellationTokenSource())
        {
            var prepared=await service.PrepareChangeAsync(preparePlan.Identity,"Repair",prepareTarget,new Observer(p=>{if(p.Stage==MaintenanceStage.PublishingBackup)cancellation.Cancel();}),cancellation.Token);
            Check(prepared.Identity==preparePlan.Identity&&File.Exists(prepareTarget+".recovery/COMPLETED.json"),"standalone preparation completes once its backup publication commits without pretending to cancel the completed result");
        }
        var complete=Path.Combine(root,"complete.db");var tooLate=false;
        using(var cancellation=new CancellationTokenSource())
        {
            await service.BackupAsync(complete,new Observer(p=>{if(p.Stage==MaintenanceStage.PublishingBackup)cancellation.Cancel();if(p.Stage==MaintenanceStage.CancelTooLate)tooLate=true;}),cancellation.Token);
            Check(tooLate&&File.Exists(complete+".recovery/COMPLETED.json"),"cancellation after backup commit begins returns real completion, never false cancellation");
            _=ParcelRecovery.VerifyBackup(complete);Check(true,"completed backup is independently verified through the real recovery verifier");
        }
        var failed=Path.Combine(root,"failed-marker.db");
        try{await service.BackupAsync(failed,new Observer(p=>{if(p.Stage==MaintenanceStage.PublishingBackup)Directory.CreateDirectory(failed+".recovery/COMPLETED.json");}),default);throw new Exception("Marker failure swallowed");}
        catch(IOException){Check(!File.Exists(failed+".recovery/COMPLETED.json"),"real completion-file obstruction remains failure with no success marker");}
        catch(UnauthorizedAccessException){Check(!File.Exists(failed+".recovery/COMPLETED.json"),"real completion-file obstruction remains failure with no success marker");}
        Released(directory);Originals();
        var restored=Path.Combine(root,"restore-cancel",seed.Mode.ToString(),seed.StationId,seed.ApplicationId);
        using(var cancellation=new CancellationTokenSource())
        using(var execution=new MaintenanceExecution(new Observer(p=>{if(p.Stage==MaintenanceStage.CopyingCustody&&p.ProcessedBytes>0)cancellation.Cancel();}),cancellation.Token))
        {
            try{ParcelRecovery.RestoreAuthorized(complete,restored,()=>{},execution);throw new Exception("Restore cancellation ignored");}
            catch(OperationCanceledException){Check(File.Exists(Path.Combine(restored,"RESTORE-INCOMPLETE.json"))&&!File.Exists(Path.Combine(restored,"RESTORED-ISOLATED.json")),"cancelled restore retains incomplete isolation and cannot become production-ready");}
        }
        var isolated=Path.Combine(root,"restore-complete",seed.Mode.ToString(),seed.StationId,seed.ApplicationId);tooLate=false;
        using(var cancellation=new CancellationTokenSource())
        using(var execution=new MaintenanceExecution(new Observer(p=>{if(p.Stage==MaintenanceStage.PublishingRestore)cancellation.Cancel();if(p.Stage==MaintenanceStage.CancelTooLate)tooLate=true;}),cancellation.Token))
        {ParcelRecovery.RestoreAuthorized(complete,isolated,()=>{},execution);Check(tooLate&&File.Exists(Path.Combine(isolated,"RESTORED-ISOLATED.json"))&&!File.Exists(Path.Combine(isolated,"restore-handoff.json")),"restore commit winner completes isolated files without authorizing production handoff");}
        var uninstallBackup=Path.Combine(root,"uninstall-cancel.db");var plan=await service.PlanChangeAsync("Uninstall",uninstallBackup);var changes=0;
        using(var cancellation=new CancellationTokenSource())
        using(var execution=new MaintenanceExecution(new Observer(p=>{if(p.Stage==MaintenanceStage.PublishingBackup)cancellation.Cancel();}),cancellation.Token))
        {
            try{await service.ExecuteChangeAsync(plan.Identity,"Uninstall",uninstallBackup,()=>{changes++;return Task.CompletedTask;},execution);throw new Exception("Uninstall cancellation ignored");}
            catch(OperationCanceledException){Check(changes==0&&File.Exists(uninstallBackup+".recovery/COMPLETED.json"),"uninstall cancellation may retain a complete backup but cannot retire the deployment");}
        }
        _=ParcelRecovery.VerifyBackup(uninstallBackup);Released(directory);Originals();
        Check(deviceConstructions==0,"offline progress and cancellation never construct device sessions");
    }
}
