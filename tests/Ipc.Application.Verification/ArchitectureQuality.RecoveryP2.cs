using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
using System.Diagnostics;

static partial class ArchitectureQuality
{
    public static async Task RecoveryP2Checks(string source, string evidence)
    {
        workspace=source;root=evidence;failures=0;
        var fixture=Path.Combine(root,"p2-original");
        var psi=new ProcessStartInfo(Environment.ProcessPath!){RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{typeof(ArchitectureQuality).Assembly.Location,workspace,"--raw-custody-child",fixture})psi.ArgumentList.Add(arg);
        using(var child=Process.Start(psi)!)
        {
            var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));Console.Write(await output);Console.Write(await error);
            Check(child.ExitCode==75,"P2 input: real child terminated after original upload file flush, before T3");
        }
        var c=InterruptedConfig();var sourceDirectory=DirectoryFor("p2-original",c);
        // Preserve the genuine raw outside the Application; the responsibility stays Awaiting.
        string originalOccurrence;
        using(var repo=new ParcelRepository(sourceDirectory,c.ApplicationId))originalOccurrence=repo.PendingReceipts().Single(x=>x.Stage=="image-upload").Id;
        File.Move(Path.Combine(sourceDirectory,"receipts",originalOccurrence+".json"),Path.Combine(root,"p2-original-upload-preserved.json"));
        foreach(var version in new[]{9})foreach(var local in new[]{false,true})
        {
            try { await OfflineIdentityP2(sourceDirectory,c,version,local); }
            catch(Exception ex){Check(false,$"P2 DB{version} local={local}: {ex.GetType().Name}: {ex.Message}");}
        }
        foreach(var local in new[]{false,true}) await OfflineIdentityP2(sourceDirectory,c,9,local,sameBytes:true);

        try { await DurableBasisP2(c,originalOccurrence); }
        catch(Exception ex){Check(false,"P2 durable basis: "+ex);}
        if(failures!=0)throw new Exception("Recovery P2 failures: "+failures);
        Console.WriteLine("PASS RECOVERY P2 COMPLETE");
    }
    static async Task OfflineIdentityP2(string source,ParcelConfiguration c,int version,bool local,bool sameBytes=false,int targetStorageVersion=9)
    {
        var name=$"p2-identity-v{version}-local{local}-same{sameBytes}-target{targetStorageVersion}";var directory=DirectoryFor(name,c);Directory.CreateDirectory(directory);
        ParcelOperation op;using(var repo=new ParcelRepository(source,c.ApplicationId))op=repo.Load().Single();
        ExtensionStore.AtomicWrite(Path.Combine(directory,"configuration.json"),CurrentConfigurationFixture.Create<ParcelConfiguration>(1,c,null,"Effective",null));
        var image=op.Image!;var imageFile=Path.Combine(directory,image.RelativeFile);Directory.CreateDirectory(Path.GetDirectoryName(imageFile)!);File.Copy(Path.Combine(source,image.RelativeFile),imageFile);
        string original;var early=new StageReply(Wire.Id(),op.Upload!.Request.Id,op.Upload.Request.PayloadHash,true,true,null,null,null,"Synthetic first nonterminal reply",DateTimeOffset.UtcNow);
        if(sameBytes) early=early with{BusinessAccepted=true,Image=image with{UploadedUri="reference://upload/p2"}};
        var changed=early with{BusinessAccepted=true,Image=image with{UploadedUri="reference://upload/p2"},Evidence="Changed bytes under evicted original ID"};
        if(sameBytes)changed=early;
        using(var repo=new ParcelRepository(directory,c.ApplicationId,version))
        {
            repo.Commit(op,"P2SyntheticFixture","Current schema with frozen original receipts");
            foreach(var stage in new[]{op.Capture,op.Wcs}.OfType<StageFact>())foreach(var reply in stage.Replies)
            {var id=repo.BeginReceipt(op,stage,"Submit");repo.StoreRaw(id,reply);repo.Commit(op,"P2MainCustody","Preserved reference originals",occurrence:id);}
            original=repo.BeginReceipt(op,op.Upload,"Submit");var replies=new List<StageReply>();
            for(var i=0;i<65;i++)
            {
                var reply=i==(sameBytes?1:0)?early:early with{Id=Wire.Id(),Evidence="Later observation "+i};replies.Add(reply);
                op=op with{Upload=op.Upload with{State=sameBytes?StageState.Accepted:StageState.Unknown,Replies=sameBytes&&replies.Count>64?replies.Take(1).Concat(replies.TakeLast(62)).ToArray():replies.TakeLast(64).ToArray()},Image=sameBytes?early.Image:op.Image,RecoveryRequired=false};
                var id=repo.BeginReceipt(op,op.Upload,"Query");repo.StoreRaw(id,reply);repo.Commit(op,"P2QueryOriginal","Distinct durable nonterminal observation",occurrence:id);
            }
            Check(!op.Upload.Replies.Any(r=>r.Id==early.Id),$"P2 DB{version} original ID evicted from 64-reply projection");
            if(local){var query=repo.BeginReceipt(op,op.Upload,"Query");repo.StoreRaw(query,changed);}
            repo.ValidateRawInventory();
        }
        var calls=0;var database=Path.Combine(directory,c.ApplicationId+".db");var backup=Path.Combine(root,name+".backup.db");
        var result=await ParcelOfflineReceiptRecovery.RecoverAsync(database,Wire.FileHash(database),backup,original,"/synthetic/original-release", "Controlled duplicate identity investigation",new SportmasterFlow(),
            (_,_,_,_)=>{calls++;return Task.FromResult<StageReply?>(changed);},expectedWalHash:WalHash(database));
        using(var repo=new ParcelRepository(directory,c.ApplicationId,sameBytes?targetStorageVersion:version))
        {
            var after=repo.Load().Single();repo.ValidateRawInventory();
            if(sameBytes)
            {
                Check(repo.StorageVersion==targetStorageVersion&&Wire.Encode(result).Contains("OutcomeCheckedCustodyRetained")&&after.Upload!.State==StageState.Accepted&&repo.PendingReceipts().Length==1&&repo.HasReceiptOutcomeBasis(original)&&calls==(local?0:1),$"P2 same-ID/same-bytes local={local} remains idempotent in the current format with original missing raw retained");
                return;
            }
            Check(Wire.Encode(result).Contains("Conflict")&&after.Upload!.State==StageState.Conflict&&repo.StorageVersion==version,$"P2 DB{version} local={local} rejects changed permanent identity before acceptance/upgrade");
            Check(calls==(local?0:1)&&repo.ReceiptById(original) is{State:"Awaiting",RawHash:null,QueryOccurrence:null}&&!after.ImageReleased&&File.Exists(imageFile)&&File.Exists(backup+".recovery/COMPLETED.json"),"P2 identity conflict retains original missing raw, image and full backup without another local Query");
            Check(Wire.Encode(after.Upload!.Request)==Wire.Encode(op.Upload.Request)&&after.Upload.Attempts==1&&after.Wcs!.Attempts==1&&repo.ReceiptResponsibilities(op.Id).Any(r=>r.Source=="Query"&&r.State=="Committed"&&r.RawHash==Wire.Hash(Wire.Encode(changed))),"P2 conflict retains contradictory raw and frozen submit identity/counts");
        }
    }
    static async Task DurableBasisP2(ParcelConfiguration c,string original)
    {
        var name="p2-original";var directory=DirectoryFor(name,c);string operation;var pending=new List<string>{original};
        using(var repo=new ParcelRepository(directory,c.ApplicationId))
        {
            var op=repo.Load().Single();operation=op.Id;
            // Transaction-level cuts before replies, followed by real maintenance Query calls.
            for(var i=0;i<66;i++)pending.Add(repo.BeginReceipt(op,op.Upload!,"Query"));
        }
        ParcelOperation frozen;long candidateRevision;
        await using(var app=await Open(c,name,f=>new UniqueQueryFactory(f),a=>a.BeforeMonitorProbe=(_,t)=>Task.Delay(Timeout.Infinite,t)))
        {
            // Park periodic probes only; health for actual commands still uses real ports.
            await Maintenance(app);await Wait(app,s=>s.Availability!.Configure);
            frozen=(await app.OperationAsync(operation))!;
            foreach(var id in pending)
                Check((await Command(app,"ReconcileReceipt",id,"Independent original-query observation after simulated reply cut")).Status=="OutcomeKnownRawMissing","P2 distinct missing occurrence obtains its own committed Query basis");
            using(var repo=new ParcelRepository(directory,c.ApplicationId))
            {
                repo.ValidateRawInventory();var after=repo.ReadOperation(operation)!;
                Check(after.Upload!.Replies.Length<=64&&pending.All(repo.HasReceiptOutcomeBasis),"P2 all 67 durable Query bases remain valid beyond bounded reply projection");
                var row=repo.ReceiptById(pending[1])!;var query=row.QueryOccurrence!;
                var reply=Wire.Decode<StageReply>(File.ReadAllText(Path.Combine(directory,"receipts",query+".json")));
                Check(!after.Upload.Replies.Any(r=>r.Id==reply.Id)&&repo.HasReceiptOutcomeBasis(pending[1]),"P2 evicted intermediate terminal Query remains authoritative");
                Check(repo.OriginalReplyHash(reply.RequestId,reply.Id)==Wire.Hash(Wire.Encode(reply)),"P2 unchanged permanent identity remains idempotent");
            }
            var s=await app.SnapshotAsync();var staged=await app.StageConfigurationAsync(s.Configuration.Revision,s.Configuration.Effective with{MaximumOperations=s.Configuration.Effective.MaximumOperations+1},"Controlled activation with independent raw custody retained");
            Check(staged.Status=="PendingRestart","P2 candidate may stage after proven external completion");candidateRevision=(await app.SnapshotAsync()).Configuration.Revision;
        }
        using(var repo=new ParcelRepository(directory,c.ApplicationId))
        {
            var after=repo.ReadOperation(operation)!;
            repo.Commit(after with{Upload=after.Upload! with{State=StageState.Rejected}},"P2NegativeBasis","Contradictory current terminal state");
            Check(!repo.HasReceiptOutcomeBasis(pending[1]),"P2 opposite terminal state rejects durable Query basis");
            repo.Commit(after with{FrozenConfiguration=after.FrozenConfiguration with{MaximumOperations=1}},"P2NegativeBasis","Changed frozen configuration");
            Check(!repo.HasReceiptOutcomeBasis(pending[1]),"P2 changed frozen configuration cannot authorize custody basis");
            repo.Commit(after,"P2RestoreBasis","Restore exact prior operation after negative checks");
            var raw=Path.Combine(directory,"receipts",repo.ReceiptById(pending[1])!.QueryOccurrence+".json");var bytes=File.ReadAllBytes(raw);
            try
            {
                File.AppendAllText(raw," ");
                try{repo.HasReceiptOutcomeBasis(pending[1]);Check(false,"P2 altered raw must fail checksum");}
                catch(InvalidDataException){Check(true,"P2 altered linked Query raw refuses custody proof");}
            }
            finally{File.WriteAllBytes(raw,bytes);}
        }
        await using(var app=await Open(c,name,f=>new UniqueQueryFactory(f),a=>a.BeforeMonitorProbe=(_,t)=>Task.Delay(Timeout.Infinite,t)))
        {
            var s=await app.SnapshotAsync();var after=(await app.OperationAsync(operation))!;
            using var repo=new ParcelRepository(directory,c.ApplicationId);repo.ValidateRawInventory();
            Check(s.Configuration.Status=="Effective"&&s.Configuration.Pending is null&&s.Configuration.Revision>=candidateRevision&&pending.All(repo.HasReceiptOutcomeBasis),"P2 actual restart activates candidate using persistent Query evidence beyond projection");
            Check(repo.PendingReceipts().Length==67&&!after.ImageReleased&&after.Upload!.Attempts==frozen.Upload!.Attempts&&Wire.Encode(after.Upload.Request)==Wire.Encode(frozen.Upload.Request)&&after.Wcs!.Attempts==1,"P2 activation preserves all missing raw/image responsibility and never resubmits original work");
        }
    }
    sealed class UniqueQueryFactory(ParcelCapabilityFactory inner):IParcelCapabilityFactory
    {
        public ParcelConfiguration Normalize(ParcelConfiguration c)=>inner.Normalize(c);public void Validate(ParcelConfiguration c)=>inner.Validate(c);public CapabilityRequirements Requirements(CapabilityBinding b)=>inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c){var p=inner.Create(c);return p with{Uploader=new UniqueQueryUpload(p.Uploader!)};}public ValueTask DisposeAsync()=>inner.DisposeAsync();
    }
    sealed class UniqueQueryUpload(IImageUploader inner):IImageUploader
    {
        public string InstanceId=>inner.InstanceId;public Task StartAsync(CancellationToken t)=>inner.StartAsync(t);public Task<CapabilityHealth> HealthAsync(CancellationToken t)=>inner.HealthAsync(t);public Task ReconnectAsync(CancellationToken t)=>inner.ReconnectAsync(t);
        public Task<StageReply> UploadAsync(FrozenRequest r,ReadOnlyMemory<byte>b,CancellationToken t)=>inner.UploadAsync(r,b,t);
        public async Task<StageReply?> QueryAsync(FrozenRequest r,CancellationToken t){var reply=await inner.QueryAsync(r,t);return reply is null?null:reply with{Id=Wire.Id(),At=DateTimeOffset.UtcNow,Evidence="SIMULATION independent original-query observation"};}
        public Task StopAsync(CancellationToken t)=>inner.StopAsync(t);public ValueTask DisposeAsync()=>inner.DisposeAsync();
    }
}
