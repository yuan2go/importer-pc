using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
using System.Diagnostics;
static partial class ArchitectureQuality
{
    public static async Task RawCustodyChild(string source,string path)
    {
        workspace=source;var c=InterruptedConfig();Directory.CreateDirectory(path);ExtensionStore.AtomicWrite(Path.Combine(path,"business.json"),c);
        var launch=new BusinessLaunch(path,Path.Combine(path,"data"),null,null);var f=new RawCustodyFactory(new(launch));
        var a=new ParcelRuntime(launch,new SportmasterFlow(),f);a.Boundary=p=>{if(p=="AfterRawFileDurable:image-upload")Environment.Exit(75);};await a.StartAsync(default);
        a.EmitReferenceAt("left","raw-original","001","raw-original-source");await f.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
        await Wait(a,s=>s.Operations.Single().State==ParcelState.SlotResolved);f.Release.TrySetResult();await Task.Delay(Timeout.Infinite);
    }
    sealed class RawCustodyFactory(ParcelCapabilityFactory inner):IParcelCapabilityFactory
    {
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ParcelConfiguration Normalize(ParcelConfiguration c)=>inner.Normalize(c);public void Validate(ParcelConfiguration c)=>inner.Validate(c);public CapabilityRequirements Requirements(CapabilityBinding b)=>inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c){var p=inner.Create(c);return p with{Uploader=new RawCustodyUpload(p.Uploader!,this)};}public ValueTask DisposeAsync()=>inner.DisposeAsync();
    }
    sealed class RawCustodyUpload(IImageUploader inner,RawCustodyFactory f):IImageUploader
    {
        public string InstanceId=>inner.InstanceId;public Task StartAsync(CancellationToken t)=>inner.StartAsync(t);public Task<CapabilityHealth> HealthAsync(CancellationToken t)=>inner.HealthAsync(t);public Task ReconnectAsync(CancellationToken t)=>inner.ReconnectAsync(t);
        public async Task<StageReply> UploadAsync(FrozenRequest r,ReadOnlyMemory<byte>b,CancellationToken t){var reply=await inner.UploadAsync(r,b,t);f.Entered.TrySetResult();await f.Release.Task.WaitAsync(t);return reply;}
        public Task<StageReply?> QueryAsync(FrozenRequest r,CancellationToken t)=>inner.QueryAsync(r,t);public Task StopAsync(CancellationToken t)=>inner.StopAsync(t);public ValueTask DisposeAsync()=>inner.DisposeAsync();
    }
    public static async Task CustodyChecks(string source,string evidence)
    {
        workspace=source;root=evidence;failures=0;var name="actual-original-file";var path=Path.Combine(root,name);var c=InterruptedConfig();
        var psi=new ProcessStartInfo(Environment.ProcessPath!){RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{typeof(ArchitectureQuality).Assembly.Location,workspace,"--raw-custody-child",path})psi.ArgumentList.Add(arg);
        using(var child=Process.Start(psi)!){var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12));Console.Write(await output);Console.Write(await error);Check(child.ExitCode==75,"real process dies after original upload raw file flush and before raw-durable/T3 transaction");}
        var directory=DirectoryFor(name,c);string occurrence;string originalHash;ParcelOperation original;var backup=Path.Combine(root,"original-present.backup.db");
        using(var repo=new ParcelRepository(directory,c.ApplicationId))
        {
            original=repo.Load().Single();var pending=repo.PendingReceipts().Single(x=>x.Stage=="image-upload");occurrence=pending.Id;
            Check(pending.Reply is not null&&repo.ReceiptById(occurrence)!.State=="Awaiting","pre-T3 original bytes belong to the original Awaiting occurrence");
            originalHash=Wire.FileHash(Path.Combine(directory,"receipts",occurrence+".json"));repo.Backup(backup);
        }
        var recovery=backup+".recovery";Directory.CreateDirectory(recovery);var names=new List<string>{"configuration.json"};names.AddRange(RecoveryFiles.Enumerate(directory,"receipts"));names.AddRange(RecoveryFiles.Enumerate(directory,"images",true));var hashes=names.ToDictionary(n=>n,n=>Wire.FileHash(RecoveryFiles.Resolve(directory,n)));
        foreach(var n in names){var target=RecoveryFiles.Resolve(recovery,n);Directory.CreateDirectory(Path.GetDirectoryName(target)!);RecoveryFiles.CopyVerified(RecoveryFiles.Resolve(directory,n),target,hashes[n]);}
        ExtensionStore.AtomicWrite(Path.Combine(recovery,"COMPLETED.json"),new ParcelRecoveryManifest(c.ApplicationId,Path.GetFileName(backup),Wire.FileHash(backup),hashes,DateTimeOffset.UtcNow,"Synthetic recovery set containing actual original pre-T3 upload bytes"));
        File.Delete(Path.Combine(directory,"receipts",occurrence+".json"));
        await using(var a=await Open(c,name))
        {
            await Maintenance(a);
            // Periodic health also holds the external-call fence. Obtain the public
            // maintenance availability before this test expects a query to execute.
            await Wait(a,s=>s.Availability?.Configure==true);
            var outcome=await Command(a,"ReconcileReceipt",occurrence,"Original receiver outcome checked; absent local original remains independent");
            Console.WriteLine("Receipt custody query result: "+Wire.Encode(outcome));
            Check(outcome.Status=="OutcomeKnownRawMissing"&&!(await a.OperationAsync(original.Id))!.ImageReleased,"authoritative Query confirms outcome but cannot release original missing raw/image custody");
            await Command(a,"ArchiveHistory",value:"Do not archive missing original custody");using var repo=new ParcelRepository(directory,c.ApplicationId);
            Check(repo.PendingReceipts().Any(x=>x.Id==occurrence)&&repo.Load().Any(o=>o.Id==original.Id)&&repo.ReceiptById(occurrence)!.QueryOccurrence is not null,"pending original, active owner and separate Query basis remain after attempted archive");
        }
        var restored=DirectoryFor("actual-original-restored",c);ParcelRecovery.Restore(backup,restored);
        await using(var a=await Open(c,"actual-original-restored"))
        {
            var op=await a.OperationAsync(original.Id);using var repo=new ParcelRepository(restored,c.ApplicationId);
            Check(op!.ImageReleased&&repo.PendingReceipts().Length==0&&repo.ReceiptById(occurrence)!.RawHash==originalHash,"complete recovery set restores actual original bytes; local T3 clears only that occurrence and releases image by original policy");
            Check(op.Wcs!.Attempts==1&&op.Upload!.Attempts==1,"restoring original raw never resubmits WCS or upload");
        }
        using(var repo=new ParcelRepository(restored,c.ApplicationId))
        {
            var op=repo.ReadOperation(original.Id)!;repo.Archive([op.Id],"Synthetic archive after actual original custody restoration");
            repo.Commit(op,"SyntheticCustodyReactivation","Exercise an archived owner returning to the active custody set");
            repo.Archive([op.Id],"Synthetic rearchive must reuse verified raw BLOBs after spool cleanup");
            repo.ValidateRawInventory();
            Check(repo.ReadOperation(op.Id)!.ImageReleased&&repo.ReceiptById(occurrence)!.RawHash==originalHash,"reactivated archived owner can rearchive without losing or duplicating original raw BLOBs");
        }
        await using(var a=await Open(c,"actual-original-restored")) Check((await a.OperationAsync(original.Id))!.Upload!.Attempts==1,"rearchive survives restart without another upload");
        if(failures!=0)throw new Exception("Receipt custody failures: "+failures);Console.WriteLine("PASS RECEIPT CUSTODY COMPLETE");
    }
}
