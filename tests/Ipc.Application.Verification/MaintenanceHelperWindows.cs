#if FROZEN_MAINTENANCE_BOUNDARY
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Assembly;
using Ipc.Platform;

[SupportedOSPlatform("windows")]
static class MaintenanceHelperWindows
{
    sealed class Observer(Action<MaintenanceProgress> report):IProgress<MaintenanceProgress>{public void Report(MaintenanceProgress p)=>report(p);}
    static void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);Console.WriteLine("PASS WT048-HELPER "+reason);}
    static async Task CancelBeforeBegin(Func<IProgress<MaintenanceProgress>,CancellationToken,Task> operation)
    {
        using var cancellation=new CancellationTokenSource();var ready=false;
        try{await operation(new Observer(p=>{if(p.Stage==MaintenanceStage.Checking){ready=true;cancellation.Cancel();}}),cancellation.Token);throw new Exception("Expected pre-Begin cancellation");}
        catch(MaintenanceCancelledException){Check(ready,"actual protected helper acknowledges cancellation before Begin and exits with a cancelled terminal");}
    }
    public static async Task Run(string mediaRoot,string root,string syntheticConfiguration)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true"||Environment.GetEnvironmentVariable("RUNNER_OS")!="Windows"||Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT")!="github-hosted")
            throw new InvalidOperationException("Disposable hosted Windows CI only; never install on a developer workstation");
        var run=Environment.GetEnvironmentVariable("GITHUB_RUN_ID");
        if(string.IsNullOrEmpty(run)||run.Length>32||!run.All(char.IsAsciiDigit)||WindowsStoragePaths.ProtectedAnchor!=@"D:\ImporterPc.CI-"+run)
            throw new InvalidOperationException("The existing protected anchor must belong to this disposable CI run; it is never replaced");
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        if(Path.GetPathRoot(root)!=@"D:\"||!WindowsDeploymentSetup.MachineRoot.StartsWith(@"D:\",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("D storage required");
        if(WindowsDeploymentSetup.Current("sportmaster") is not null||Directory.Exists(root))throw new InvalidOperationException("Isolated new product/data required; existing deployment is never adopted");
        Directory.CreateDirectory(root);var media=DeploymentMedia.Inspect(mediaRoot,"sportmaster");
        var config=Path.Combine(root,"seed");Directory.CreateDirectory(config);File.Copy(syntheticConfiguration,Path.Combine(config,"business.json"));
        var policy=Path.Combine(WindowsDeploymentSetup.MachineRoot,"runtime-policy.json");
        var sid=File.Exists(policy)?Wire.Decode<MachineRuntimePolicy>(File.ReadAllText(policy)).RuntimeAccountSid:MachineRuntimePolicy.SharedUsersSid;
        var data=Path.Combine(root,"data");var program=WindowsStoragePaths.ProgramRoot("ImporterPc.MaintenanceBoundary");
        var request=ParcelStartupServices.Review(media,sid,data,config,null,[],false,program) with{Shortcuts=new(false,false),Recovery=new(){Enabled=false}};
        await CancelBeforeBegin(async(p,t)=>{_ = await ParcelStartupServices.InstallWithPreflightAsync(request,null,p,t);});
        Check(WindowsDeploymentSetup.Current("sportmaster") is null&&!Directory.Exists(data),"cancelled fresh installation has no deployment registration or data-root preparation");
        using(var late=new CancellationTokenSource())
        {
            var sealedPhase=false;
            var installed=await ParcelStartupServices.InstallWithPreflightAsync(request,null,new Observer(p=>{if(p.Stage==MaintenanceStage.InstallingMachine){sealedPhase=true;Check(!p.CanCancel,"machine-side effects seal cancellation before installation changes");late.Cancel();}}),late.Token);
            Check(sealedPhase&&installed.SourceCommit==media.SourceCommit&&ParcelStartupServices.InspectInstallation(request).Status=="Completed","late cancellation preserves the actual completed new-install receipt");
        }
        var location=WindowsDeploymentSetup.Current("sportmaster")!;var launch=new BusinessLaunch(location.ConfigurationDirectory,data,null,null);
        var mode=LocalManagementMode.ForRoot(data);
        // Keep the password only in memory, never in source/config/output.
        var password="Synthetic-"+Guid.NewGuid().ToString("N");
        mode.ConfigurePassword(password);mode.Enter(password);Check(mode.Status().Authorized,"normal managed password setup creates an explicit software management grant");
        var service=ParcelStartupServices.Maintenance(launch);var current=await service.ReadAsync();
        var staged=await service.StageAsync(current.ConfigurationHash,current.Candidate! with{TimeoutMs=current.Candidate!.TimeoutMs+1},"synthetic protected maintenance verification");
        var directory=staged.RuntimeDirectory!;using(var repository=new ParcelRepository(directory,"sportmaster")){}
        var original=Path.Combine(directory,"receipts","synthetic-large.bin");Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        using(var bytes=File.Create(original)){var buffer=new byte[65536];for(var i=0;i<128;i++)bytes.Write(buffer);}
        var database=Path.Combine(directory,"sportmaster.db");var databaseHash=Wire.FileHash(database);var originalHash=Wire.FileHash(original);var configurationHash=Wire.FileHash(Path.Combine(directory,"configuration.json"));
        void Originals(){Check(Wire.FileHash(database)==databaseHash&&Wire.FileHash(original)==originalHash&&Wire.FileHash(Path.Combine(directory,"configuration.json"))==configurationHash,"actual helper retains original database, custody and configuration bytes");using var owner=new ResourceLease(directory,["application-owner"]);Check(true,"actual helper exit releases original application ownership");}
        var cancelledBackup=Path.Combine(root,"cancelled-backup.db");
        await CancelBeforeBegin((p,t)=>service.BackupAsync(cancelledBackup,p,t));
        Check(!File.Exists(cancelledBackup)&&!Directory.Exists(cancelledBackup+".recovery"),"cancel-before-Begin never creates the requested backup set");Originals();
        var backup=Path.Combine(root,"completed.db");var copied=false;
        await service.BackupAsync(backup,new Observer(p=>{if(p.ProcessedBytes>0)copied=true;}),default);
        Check(copied&&File.Exists(backup+".recovery/COMPLETED.json"),"actual elevated copy observations end in a completed backup marker");
        _=await service.PlanRestoreAsync(backup,Path.Combine(root,"verify-only"));Originals();
        var obstructed=Path.Combine(root,"obstructed.db");Directory.CreateDirectory(obstructed);
        try{await service.BackupAsync(obstructed,null,default);throw new Exception("Expected actual filesystem refusal");}
        catch(IOException e)when(e is not InstallationOutcomeUnknownException){Check(!File.Exists(obstructed+".recovery/COMPLETED.json"),"actual helper filesystem refusal cannot publish backup success");}Originals();
        var restore=await service.PlanRestoreAsync(backup,Path.Combine(root,"restored"),null,default);
        await CancelBeforeBegin((p,t)=>service.RestoreAsync(backup,restore.Destination,restore.BackupSha256,p,t));
        Check(!Directory.Exists(restore.Destination),"cancelled protected restore creates no isolated application directory");
        var registration=WindowsDeploymentSetup.Registration("sportmaster");var beforeRestore=Wire.FileHash(registration);
        await service.RestoreAsync(backup,restore.Destination,restore.BackupSha256,null,default);
        Check(File.Exists(Path.Combine(restore.Destination,"RESTORED-ISOLATED.json"))&&Wire.FileHash(registration)==beforeRestore,"actual protected restore creates isolated files without production handoff or changing the active deployment");Originals();
        await PeerFailure(location,launch,mode,root,"wrong-control");Originals();
        await PeerFailure(location,launch,mode,root,"disconnect");Originals();
        var repair=ParcelStartupServices.Review(media,sid,data,location.ConfigurationDirectory,null,[],false,program) with{Shortcuts=new(false,false),Recovery=location.Recovery,BackupDestination=Path.Combine(root,"pre-repair.db")};
        var beforeRepair=Wire.FileHash(registration);
        await CancelBeforeBegin(async(p,t)=>{_=await ParcelStartupServices.InstallWithPreflightAsync(repair,null,p,t);});
        Check(Wire.FileHash(registration)==beforeRepair&&!File.Exists(repair.BackupDestination),"cancelled same-source repair retains the original deployment and does not create its backup");
        location=await ParcelStartupServices.InstallWithPreflightAsync(repair,null,null,default);
        Check(ParcelStartupServices.InspectInstallation(repair).Status=="Completed"&&File.Exists(repair.BackupDestination+".recovery/COMPLETED.json"),"actual same-source repair verifies original responsibilities, completes a backup and retains its installation identity");Originals();
        beforeRepair=Wire.FileHash(registration);
        await CancelBeforeBegin((p,t)=>ParcelStartupServices.RepairRegistrationAsync(beforeRepair,null,p,t));
        Check(Wire.FileHash(registration)==beforeRepair,"system-entry repair cancellation leaves the authoritative registration unchanged");
        await ParcelStartupServices.RepairRegistrationAsync(beforeRepair,null,null,default);Originals();
        // Uninstall retains original runtime authority; no new management-password policy is introduced.
        mode.Enter(password);var removalBackup=Path.Combine(root,"pre-remove.db");var plan=await service.PlanChangeAsync("Uninstall",removalBackup,null,default);
        await CancelBeforeBegin(async(p,t)=>{_=await ParcelStartupServices.RemoveAsync(Wire.FileHash(registration),plan.Identity,removalBackup,p,t);});
        Check(WindowsDeploymentSetup.Current("sportmaster")!.LifecycleState=="Installed"&&!File.Exists(removalBackup),"cancelled uninstall neither retires the product nor starts its backup");
        var receipt=await ParcelStartupServices.RemoveAsync(Wire.FileHash(registration),plan.Identity,removalBackup,null,default);
        Check(WindowsDeploymentSetup.Current("sportmaster")!.LifecycleState!="Installed"&&File.Exists(removalBackup+".recovery/COMPLETED.json")&&File.Exists(database),"actual uninstall retires only after complete backup and retains original data");
        Check(receipt.Status is "RemovedDataRetained" or "RestartRequired" or "RemovedWithRetainedFiles" or "RetiredCleanupPending","actual removal reports retained loaded files/restart truthfully: "+receipt.Status);
        ExtensionStore.AtomicWrite(Path.Combine(root,"REMOVAL-RECEIPT.json"),receipt);Originals();
        Console.WriteLine("BOUNDARY: actual Windows protected helper and SCM; synthetic offline data; interactive UAC, devices and a second-version upgrade medium NOT_RUN");
    }
    static async Task PeerFailure(DeploymentLocation installed,BusinessLaunch launch,LocalManagementMode mode,string root,string scenario)
    {
        var pipeName=Wire.Id()+Wire.Id();using var parent=Process.GetCurrentProcess();var destination=Path.Combine(root,scenario+".db");
        var request=new StartupMaintenanceRequest("Backup",launch,Destination:destination,Pipe:pipeName)
            {InitiatorProcessId=parent.Id,InitiatorStartedTicks=parent.StartTime.ToUniversalTime().Ticks};
        var identity=(string)typeof(ParcelStartupServices).GetMethod("ManagementRequestHash",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[request])!;
        request=request with{ManagementGrant=mode.DelegateOperation(identity,TimeSpan.FromMinutes(10))};
        var file=Path.Combine(root,scenario+".bin");ProtectedDraftPayload.Write(file,Wire.Encode(request),false);var hash=Wire.FileHash(file);
        using var pinned=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var server=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        var start=new ProcessStartInfo(Path.Combine(installed.ReleaseRoot,"app","Ipc.Assembly.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var argument in new[]{"--startup-command",file,hash})start.ArgumentList.Add(argument);
        var connected=server.WaitForConnectionAsync(timeout.Token);using var child=Process.Start(start)??throw new IOException("Helper did not start");
        var stdout=child.StandardOutput.ReadToEndAsync(timeout.Token);var stderr=child.StandardError.ReadToEndAsync(timeout.Token);
        StreamWriter? controlWriter=null;var disconnected=false;var terminalSeen=false;
        try
        {
            await connected;using var reader=new StreamReader(server,new UTF8Encoding(false,true),true,1024,true);var writer=controlWriter=new StreamWriter(server,new UTF8Encoding(false,true),1024,true){AutoFlush=true};
            var ready=JsonDocument.Parse(await reader.ReadLineAsync(timeout.Token)??throw new IOException("Missing Ready"));
            Check(ready.RootElement.GetProperty("Status").GetString()=="Ready"&&ready.RootElement.GetProperty("RequestSha256").GetString()==hash,"controlled peer connects to the real protected helper and bound request");ready.Dispose();
            if(scenario=="wrong-control")
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new{Command="Begin",RequestSha256=new string('0',64)}));
                string? terminal=null;
                while(await reader.ReadLineAsync(timeout.Token) is { } line){using var frame=JsonDocument.Parse(line);if(frame.RootElement.GetProperty("Status").GetString()=="Unknown"){terminal=line;break;}}
                Check(terminal is not null&&!File.Exists(destination)&&!Directory.Exists(destination+".recovery"),"wrong control hash yields unknown without creating either backup destination");terminalSeen=terminal is not null;
            }
            else
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new{Command="Begin",RequestSha256=hash}));
                while(await reader.ReadLineAsync(timeout.Token) is { } line)
                {using var frame=JsonDocument.Parse(line);if(frame.RootElement.GetProperty("Status").GetString()=="MaintenanceProgress"&&frame.RootElement.GetProperty("Stage").GetString()=="CopyingSnapshot"){server.Dispose();disconnected=true;break;}}
            }
            await child.WaitForExitAsync(timeout.Token);
            if(scenario=="wrong-control")Check(child.ExitCode!=0,"wrong control identity cannot return a successful helper exit");
            else
            {
                Check(disconnected,"the controlled peer actually disconnected after a real copy observation, before reading any terminal");
                try{ParcelStartupServices.CompleteBoundMaintenanceResponse(new(null,false),child.ExitCode,false);throw new Exception("Lost terminal was accepted");}
                catch(InstallationOutcomeUnknownException){Check(true,"actual missing terminal remains unknown even if the helper completed before disconnection; exit="+child.ExitCode);}
            }
            if(File.Exists(destination+".recovery/COMPLETED.json"))_ = await ParcelStartupServices.Maintenance(launch).PlanRestoreAsync(destination,Path.Combine(root,scenario+"-verify"));
            else Check(true,"interrupted helper leaves no false completed recovery marker");
        }
        finally
        {
            if(!child.HasExited){child.Kill(entireProcessTree:true);await child.WaitForExitAsync();}
            File.WriteAllText(Path.Combine(root,scenario+"-process.log"),(await stdout)+(await stderr));
            if(controlWriter is not null)ParcelStartupServices.DisposeMaintenanceWriter(controlWriter,terminalSeen,disconnected);
        }
    }
}
#endif
