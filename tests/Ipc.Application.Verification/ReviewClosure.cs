using Ipc.Application.Contracts;
using Ipc.Application.DirectPlc;
using Ipc.Application.Sportmaster;
using Ipc.Application.ParcelFlow;
using Ipc.Assembly;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static partial class ArchitectureQuality
{
    public static async Task ReviewClosureChecks(string source, string evidence)
    {
        workspace=source;root=evidence;failures=0;
        await ReviewReplyIdentity(); await ReviewArchivedIdentities(); await ReviewMaintenance(); await ReviewIndependentMaintenance(); await ReviewOfflineConsumers();
        if(failures!=0)throw new Exception("PR24 independent review closure failures: "+failures);
        Console.WriteLine("PASS PR24 REVIEW CLOSURE COMPLETE");
    }
    static async Task ReviewReplyIdentity()
    {
        var c=Config("direct-plc");c=c with {Plc=c.Plc! with {Parameters=new(c.Plc.Parameters){["complete-on-query"]="false"}}};
        ParcelOperation original;
        await using(var app=await Open(c,"review-reply"))
        {app.EmitReference("review-raw-original","001","review-raw-source");original=(await Wait(app,s=>s.Operations.FirstOrDefault()?.State==ParcelState.PlcAccepted)).Operations.Single();}
        var directory=DirectoryFor("review-reply",c);var reply=new StageReply(Wire.Id(),original.Plc!.Request.Id,original.Plc.Request.PayloadHash,true,true,null,null,null,"Original received query before identity commit",DateTimeOffset.UtcNow);
        using(var custody=new ParcelRepository(directory,c.ApplicationId))
        {
            var occurrence=custody.BeginReceipt(original,original.Plc,"Query");var file=Path.Combine(directory,"receipts",occurrence+".json");ExtensionStore.AtomicWrite(file,reply);var bytes=File.ReadAllBytes(file);
            custody.Boundary=point=>{if(point=="BeforeRecoveredReplyIdentityCommit")throw new IOException("Synthetic recovery identity commit interruption");};
            try{custody.RecoverRaw(occurrence,reply);Check(false,"R01 recovery transaction interruption is visible");}
            catch(IOException){Check(custody.OriginalReplyHash(reply.RequestId,reply.Id) is null && custody.PendingReceipts().Single().Reply==reply && File.ReadAllBytes(file).SequenceEqual(bytes),"R01 failed recovery commit retains Awaiting occurrence/raw bytes and rolls back reply identity");}
        }
        ReviewFactory? factory=null;
        await using(var app=await Open(c,"review-reply",f=>factory=new(f){OriginalReply=reply}))
        {
            using(var db=new SqliteConnection("Data Source="+Path.Combine(directory,c.ApplicationId+".db")))
            {db.Open();using var q=db.CreateCommand();q.CommandText="SELECT hash FROM parcel_reply_identities WHERE request=$r AND id=$id";q.Parameters.AddWithValue("$r",reply.RequestId);q.Parameters.AddWithValue("$id",reply.Id);Check(q.ExecuteScalar() as string==Wire.Hash(Wire.Encode(reply)),"R01 startup restores permanent original reply identity from Awaiting raw bytes");}
            for(int i=0;i<65;i++)await app.ReconcileAsync(original.Id);
            var before=(await app.SnapshotAsync()).Operations.Single();Check(!before.Plc!.Replies.Any(r=>r.Id==reply.Id),"R01 raw identity test actually passes bounded reply eviction");
            await app.ReconcileAsync(original.Id);var after=(await app.SnapshotAsync()).Operations.Single();
            Check(after.Plc!.State==StageState.Conflict && after.State!=ParcelState.PhysicalCompleted && after.Plc.Attempts==1,"R01 changed old reply ID becomes Conflict after 65 queries without resending Submit");
        }
        var missingFile=Path.Combine(directory,c.ApplicationId+".db");
        using(var db=new SqliteConnection("Data Source="+missingFile+";Pooling=False"))
        { db.Open();using var q=db.CreateCommand();q.CommandText="DELETE FROM parcel_reply_identities WHERE request=$r";q.Parameters.AddWithValue("$r",reply.RequestId);q.ExecuteNonQuery(); }
        var missingHash=Wire.FileHash(missingFile);var originalRaw=Directory.EnumerateFiles(Path.Combine(directory,"receipts")).ToDictionary(p=>p,Wire.FileHash);
        try { using var denied=new ParcelRepository(directory,c.ApplicationId);Check(false,"missing current identity must refuse"); }
        catch(InvalidDataException ex) when(ex.Message.Contains("OriginalReplyIdentityMissing"))
        { Check(Wire.FileHash(missingFile)==missingHash && originalRaw.All(p=>Wire.FileHash(p.Key)==p.Value),"missing current identity refuses before any database/raw write"); }
        using(var custody=new ParcelRepository(Path.Combine(root,"review-foreign-request"),c.ApplicationId))
        {
            custody.Commit(original,"SyntheticOriginal","synthetic");var occurrence=custody.BeginReceipt(original,original.Plc!,"Query");var foreign=reply with {RequestId=Wire.Id()};custody.StoreRaw(occurrence,foreign);
            Check(custody.OriginalReplyHash(foreign.RequestId,foreign.Id) is null&&custody.OriginalReplyHash(original.Plc!.Request.Id,foreign.Id)==Wire.Hash(Wire.Encode(foreign)),"R01 malformed returned RequestId cannot poison another original request identity namespace");
        }
        var b=Config("direct-plc");var camera=B("camera") with {Target=b.Reader.DeviceId};var upload=B("upload") with {Parameters=new(){["reply-mode"]="lose-after-commit"}};
        var attached=b with {Images=new(camera,upload,Strategy:UploadStrategy.Attached)};string id;
        await using(var app=await Open(attached,"review-attached"))
        {app.EmitReference("review-attached-original","002","review-attached-source");var op=(await Wait(app,s=>s.Operations.FirstOrDefault() is {State:ParcelState.PlcAccepted,Upload.State:StageState.Unknown})).Operations.Single();await app.ReconcileAsync(op.Id,"plc-submit");id=op.Id;}
        // Model abrupt exit while upload is in flight after the independent PLC completion.
        using(var db=new SqliteConnection("Data Source="+Path.Combine(DirectoryFor("review-attached",attached),"direct-plc.db")))
        {db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE parcel_operations SET body=json_set(body,'$.Upload.State','Dispatching','$.Reason',NULL) WHERE id=$id";q.Parameters.AddWithValue("$id",id);q.ExecuteNonQuery();}
        await using(var app=await Open(attached,"review-attached"))
        {var op=(await app.SnapshotAsync()).Operations.Single();Check(op.State==ParcelState.PhysicalCompleted && op.Reason is null && op.Upload!.State==StageState.Unknown && op.Upload.Failure is not null,"R02 interrupted Attached upload preserves completed main result and null main Reason");}
    }
    static async Task ReviewArchivedIdentities()
    {
        var c=Config("sportmaster") with {Retention=new(1,1048576)};ParcelOperation original;
        await using(var app=await Open(c,"review-archived-identities"))
        {
            app.EmitReferenceAt("left","review-archive-first","001","review-archive-first-source");original=(await Wait(app,s=>s.Operations.FirstOrDefault()?.State==ParcelState.SlotResolved)).Operations.Single();
            for(int i=0;i<3;i++){app.EmitReferenceAt("left","review-archive-"+i,"002","review-archive-source-"+i);await Wait(app,s=>s.Operations.Any(o=>o.AdmissionId=="review-archive-"+i&&o.State==ParcelState.SlotResolved));}
            await Wait(app,s=>s.Capacity!.Archived>0);
        }
        var directory=DirectoryFor("review-archived-identities",c);var file=Path.Combine(directory,c.ApplicationId+".db");
        string archivedHash;
        using(var db=new SqliteConnection("Data Source="+file))
        {
            db.Open();using var q=db.CreateCommand();q.CommandText="SELECT raw_hash FROM parcel_archived_receipts WHERE id IN (SELECT id FROM parcel_receipts WHERE operation=$op)";q.Parameters.AddWithValue("$op",original.Id);archivedHash=(string)q.ExecuteScalar()!;
            q.CommandText="DELETE FROM parcel_reply_identities WHERE request=$request";q.Parameters.AddWithValue("$request",original.Wcs!.Request.Id);q.ExecuteNonQuery();
        }
        var missingArchiveHash=Wire.FileHash(file);
        try { using var denied=new ParcelRepository(directory,c.ApplicationId);Check(false,"missing archived identity must refuse"); }
        catch(InvalidDataException ex) when(ex.Message.Contains("OriginalReplyIdentityMissing"))
        { Check(Wire.FileHash(file)==missingArchiveHash && archivedHash.Length==64,"archived BLOB and main facts unchanged when current identity is missing"); }
        // Current-format corrupt completion must refuse before reconstructing identities.
        var live=Config("direct-plc");var source=DirectoryFor("review-reply",live);var target=DirectoryFor("review-false-completion",live);Directory.CreateDirectory(target);
        foreach(var sourceFile in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories))
        {var destination=Path.Combine(target,Path.GetRelativePath(source,sourceFile));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(sourceFile,destination);}
        var badDb=Path.Combine(target,"direct-plc.db");
        using(var db=new SqliteConnection("Data Source="+badDb))
        {db.Open();using var q=db.CreateCommand();q.CommandText="DELETE FROM parcel_reply_identities; UPDATE parcel_operations SET body=json_set(body,'$.State','PhysicalCompleted','$.Plc.State','Accepted','$.Reason',NULL)";q.ExecuteNonQuery();}
        var raw=Directory.EnumerateFiles(Path.Combine(target,"receipts"),"*.json").ToDictionary(p=>p,Wire.FileHash);var before=Wire.FileHash(badDb);
        try{using var repository=new ParcelRepository(target,live.ApplicationId);Check(false,"R01 inconsistent completion must refuse repository startup");}
        catch(InvalidDataException ex) when(ex.Message.Contains("OriginalReplyIdentityMissing",StringComparison.Ordinal))
        {Check(raw.All(p=>Wire.FileHash(p.Key)==p.Value)&&Wire.FileHash(badDb)==before,"R01 inconsistent completion refuses without rewriting raw, database or inventing settlement");}
    }
    static async Task ReviewMaintenance()
    {
        ReviewFactory? f=null;var c=Config("sportmaster");c=c with {Wcs=c.Wcs! with {Parameters=new(c.Wcs.Parameters){["reply-mode"]="lose-after-commit"}}};
        await using var app=await Open(c,"review-maintenance",inner=>f=new(inner){HoldReconnect=true});
        app.EmitReferenceAt("left","review-original-query","003","review-query-source");var original=(await Wait(app,s=>s.Operations.FirstOrDefault()?.State==ParcelState.WcsUnknown)).Operations.Single();
        await Maintenance(app);var reconnect=app.ReconnectDeviceAsync(c.Wcs!.InstanceId);await f!.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            var s=await app.SnapshotAsync();Check(!s.Availability!.Resume && s.Positions!.All(p=>!p.Admit),"R05 shared service maintenance closes Resume and every dependent position");
            var resume=await Command(app,"Resume");Check(resume.Status=="Rejected","R05 explicit Resume cannot bypass maintained shared WCS");
            app.EmitReferenceAt("right","review-during-maintenance","003","review-maint-source");await Task.Delay(100);
            var query=await app.ReconcileAsync(original.Id,"wcs-slot");
            Check(query.Status=="MaintenanceInProgress"&&f.CallsDuringReconnect==0 && (await app.SnapshotAsync()).Operations.Length==1,"R05 no new Submit or original Query enters a WCS with reconnect in flight");
            Check((await app.OperationAsync(original.Id))!.Wcs!.Attempts==1,"R05 maintained original retains its single sending attempt");
        }
        finally {f.Release.TrySetResult();}
        var result=await reconnect;Check(result.Status=="Reconnected" && (await app.SnapshotAsync()).Paused,"R05 successful maintenance retains actual software pause");
    }
    static async Task ReviewIndependentMaintenance()
    {
        // Keep a real WCS request in flight while reconnecting only the left camera.
        // The right position must still finish and the lifecycle timeout cannot clear
        // an occupied left resource just because its provider ignores cancellation.
        var seed=Config("sportmaster");var left=B("camera") with {InstanceId="review.left.camera",Target="reference://camera/review-left"};var right=left with {InstanceId="review.right.camera",Target="reference://camera/review-right"};
        var c=seed with {Images=new(left,null),CapabilityInstances=[seed.Wcs!,left,right],TimeoutMs=500,
            Topology=seed.Topology! with {Positions=seed.Topology.Positions.Select(p=>p with {CameraInstanceId=p.Id=="left"?left.InstanceId:right.InstanceId}).ToArray()}};
        ReviewFactory? f=null;
        await using var app=await Open(c,"review-independent-maintenance",inner=>f=new(inner){HoldCamera=left.InstanceId,HoldWcs=true});
        app.EmitReferenceAt("right","review-existing-right","005","review-existing-right-source");await f!.CallEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Maintenance(app);
        Check((await app.ReconnectDeviceAsync(c.Wcs!.InstanceId)).Status=="Rejected","R05 selected service rejects maintenance until its original active call drains");
        var reconnect=app.ReconnectDeviceAsync(left.InstanceId);await f.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            var s=await app.SnapshotAsync();Check(s.Availability!.Resume&&!s.Positions!.Single(p=>p.Id=="left").Admit,"R05 unrelated active WCS does not prevent left-camera maintenance or independent Resume");
            Check((await Command(app,"Resume")).Status=="Persisted","R05 independent right position resumes while left resource remains fenced");
            f.CallRelease.TrySetResult();await Wait(app,x=>x.Operations.Single(o=>o.AdmissionId=="review-existing-right").State==ParcelState.SlotResolved);
            await Task.Delay(550);s=await app.SnapshotAsync();
            Check(!reconnect.IsCompleted&&!s.Positions!.Single(p=>p.Id=="left").Admit&&s.Positions!.Single(p=>p.Id=="right").Admit,"R05 expired maintenance budget retains noncooperative resource fence and keeps right position available");
            app.EmitReferenceAt("right","review-next-right","006","review-next-right-source");await Wait(app,x=>x.Operations.Any(o=>o.AdmissionId=="review-next-right"&&o.State==ParcelState.SlotResolved));
            app.EmitReferenceAt("left","review-blocked-left","007","review-blocked-left-source");await Task.Delay(80);
            Check((await app.SnapshotAsync()).Operations.All(o=>o.AdmissionId!="review-blocked-left")&&f.CallsDuringReconnect==0,"R05 right progresses while maintained left cannot admit or call its camera");
        }
        finally{f.CallRelease.TrySetResult();f.Release.TrySetResult();}
        var result=await reconnect;var final=await app.SnapshotAsync();
        Check(result.Status=="MaintenanceIncomplete"&&!final.Paused&&final.Health.Single(h=>h.InstanceId==left.InstanceId).Ready==false,"R05 late provider return reports timeout and actual unpaused state without inventing maintenance success");
    }
    static async Task ReviewOfflineConsumers()
    {
        var c=Config("direct-plc");
        await using(var app=await Open(c,"review-consumer")) { }
        var database=Path.Combine(DirectoryFor("review-consumer",c),c.ApplicationId+".db");
        var configuration=Path.Combine(Path.GetDirectoryName(database)!,"configuration.json");
        var original=File.ReadAllBytes(configuration);var revision=Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(configuration));
        var package=new PackageReference("quality.offline","1.0.0",new string('A',64));
        void Refuse(ParcelConfiguration candidate,string reason)
        {
            ExtensionStore.AtomicWrite(configuration,revision with {Pending=candidate});var frozen=File.ReadAllBytes(configuration);var hash=Wire.FileHash(database);
            try { ExtensionConsumerReaders.Inspect(database);Check(false,reason); }
            catch(Exception ex) when(ex is InvalidDataException or System.Text.Json.JsonException or InvalidOperationException)
            {Check(File.ReadAllBytes(configuration).SequenceEqual(frozen)&&Wire.FileHash(database)==hash,reason+"; read-only bytes retained");}
            finally {File.WriteAllBytes(configuration,original);}
        }
        Refuse(c with {SchemaVersion=999},"R03 unknown Pending schema conservatively blocks retirement");
        Refuse(c with {Plc=c.Plc! with {Package=package,Module="parcel-plc",Implementation="extension:"+package.Id+"/parcel-plc"}},"R03 binding package without deployment cannot become empty references");
        Refuse(c with {Reader=c.Reader with {Driver="extension:missing"},Topology=null,SchemaVersion=2},"R03 dangling observation extension blocks retirement");
        Refuse(c with {StationId="another-station"},"R03 Pending cannot change consumer station ownership");
        var valid=c with {Plc=c.Plc! with {Package=package,Module="parcel-plc",Implementation="extension:"+package.Id+"/parcel-plc"},Extensions=new([package],[])};
        ExtensionStore.AtomicWrite(configuration,revision with {Pending=valid});Check(ExtensionConsumerReaders.Inspect(database).Contains(package),"R03 valid exact offline Pending package is retained without loading modules");File.WriteAllBytes(configuration,original);
        var supplyDb=Path.Combine(root,"review-supply-consumer","station.db");
        var supplyConfig=Wire.Decode<StationConfig>(File.ReadAllText(Path.Combine(workspace,"config/station.simulation.json")));
        using(var supply=new Ipc.Supply.SupplyStore(supplyDb))
            supply.Commit(supply.Load(),"SyntheticOfflineConsumer","deployment","synthetic",config:new(Wire.Id(),"seed",supplyConfig,"synthetic","Effective","",DateTimeOffset.UtcNow));
        var supplyHash=Wire.FileHash(supplyDb);var parcelHash=Wire.FileHash(database);
        Check(ExtensionConsumerReaders.Inspect(supplyDb).Length==0&&ExtensionConsumerReaders.Inspect(database).Length==0&&Wire.FileHash(supplyDb)==supplyHash&&Wire.FileHash(database)==parcelHash,"R04 current Supply6 and Parcel10 dispatch to owning readers without mutation");
        using(var supply=new Ipc.Supply.SupplyStore(supplyDb))
            supply.Commit(supply.Load(),"SyntheticPendingConsumer","deployment","synthetic",config:new(Wire.Id(),"seed",supplyConfig with {Extensions=new([package],[])},"synthetic","PendingActivation","",DateTimeOffset.UtcNow));
        Check(ExtensionConsumerReaders.Inspect(supplyDb).Contains(package),"R04 mixed-reader dispatch preserves legacy Supply Pending package responsibility");
        if (!OperatingSystem.IsWindows())
        {
            var link=Path.Combine(root,"review-linked-consumer");Directory.CreateSymbolicLink(link,Path.GetDirectoryName(database)!);
            try{ExtensionConsumerReaders.Inspect(Path.Combine(link,Path.GetFileName(database)));Check(false,"R04 linked consumer ancestor must block");}catch(InvalidDataException){Check(Wire.FileHash(database)==parcelHash,"R04 linked consumer ancestor is rejected before opening database");}
        }
        var ambiguous=Path.Combine(root,"review-ambiguous.db");File.Copy(database,ambiguous);
        using(var db=new SqliteConnection("Data Source="+ambiguous)){db.Open();using var q=db.CreateCommand();q.CommandText="CREATE TABLE schema_history(id INTEGER PRIMARY KEY,checksum TEXT,applied TEXT)";q.ExecuteNonQuery();}
        var before=Wire.FileHash(ambiguous);
        try{ExtensionConsumerReaders.Inspect(ambiguous);Check(false,"R04 ambiguous consumer must block");}catch(InvalidDataException){Check(Wire.FileHash(ambiguous)==before,"R04 ambiguous consumer remains blocked without mutation");}
        // Unknown future frozen operation semantics may not erase package responsibility.
        await using(var app=await Open(c,"review-consumer-frozen")){app.EmitReference("frozen-consumer","004","frozen-consumer-source");await Wait(app,s=>s.Operations.FirstOrDefault()?.State==ParcelState.PlcAccepted);}
        var frozenDb=Path.Combine(DirectoryFor("review-consumer-frozen",c),c.ApplicationId+".db");
        using(var db=new SqliteConnection("Data Source="+frozenDb)){db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE parcel_operations SET body=json_set(body,'$.FrozenConfiguration.SchemaVersion',999)";q.ExecuteNonQuery();}
        before=Wire.FileHash(frozenDb);
        try{ExtensionConsumerReaders.Inspect(frozenDb);Check(false,"R03 future frozen configuration must block");}catch(InvalidDataException){Check(Wire.FileHash(frozenDb)==before,"R03 future frozen configuration blocks retirement without rewriting originals");}
    }
    sealed class ReviewFactory(ParcelCapabilityFactory inner):IParcelCapabilityFactory
    {
        public StageReply? OriginalReply;public bool HoldReconnect,Reconnecting,HoldWcs;public string? HoldCamera;public int Queries,CallsDuringReconnect;
        public readonly TaskCompletionSource CallEntered=new(TaskCreationOptions.RunContinuationsAsynchronously),CallRelease=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ParcelConfiguration Normalize(ParcelConfiguration c)=>inner.Normalize(c);public void Validate(ParcelConfiguration c)=>inner.Validate(c);public CapabilityRequirements Requirements(CapabilityBinding b)=>inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c){var p=inner.Create(c);if(p.Wcs is not null)p=p with {Wcs=new ReviewWcs(p.Wcs,this)};if(p.Plc is not null&&OriginalReply is not null)p=p with {Plc=new ReviewPlc(p.Plc,this)};if(HoldCamera is not null){var ports=p.Devices!.ToDictionary(x=>x.Key,x=>x.Value);ports[HoldCamera]=new ReviewCamera((IImageCamera)ports[HoldCamera],this);p=p with {Devices=ports,Camera=p.Camera?.InstanceId==HoldCamera?(IImageCamera)ports[HoldCamera]:p.Camera};}return p;}public ValueTask DisposeAsync()=>inner.DisposeAsync();
    }
    sealed class ReviewWcs(IWcsPort inner,ReviewFactory f):IWcsPort
    {
        public string InstanceId=>inner.InstanceId;public Task StartAsync(CancellationToken t)=>inner.StartAsync(t);public Task<CapabilityHealth> HealthAsync(CancellationToken t)=>inner.HealthAsync(t);
        public async Task ReconnectAsync(CancellationToken t){if(!f.HoldReconnect){await inner.ReconnectAsync(t);return;}f.Reconnecting=true;f.Entered.TrySetResult();try{await f.Release.Task;await inner.ReconnectAsync(t);}finally{f.Reconnecting=false;}}
        public async Task<StageReply> ResolveSlotAsync(FrozenRequest r,CancellationToken t){if(f.HoldReconnect&&f.Reconnecting)Interlocked.Increment(ref f.CallsDuringReconnect);if(f.HoldWcs){f.CallEntered.TrySetResult();await f.CallRelease.Task;}return await inner.ResolveSlotAsync(r,t);}public Task<StageReply?> QueryAsync(FrozenRequest r,CancellationToken t){if(f.Reconnecting)Interlocked.Increment(ref f.CallsDuringReconnect);return inner.QueryAsync(r,t);}public Task StopAsync(CancellationToken t)=>inner.StopAsync(t);public ValueTask DisposeAsync()=>inner.DisposeAsync();
    }
    sealed class ReviewCamera(IImageCamera inner,ReviewFactory f):IImageCamera
    {
        public string InstanceId=>inner.InstanceId;public Task StartAsync(CancellationToken t)=>inner.StartAsync(t);public Task<CapabilityHealth> HealthAsync(CancellationToken t)=>inner.HealthAsync(t);
        public async Task ReconnectAsync(CancellationToken t){f.Reconnecting=true;f.Entered.TrySetResult();try{await f.Release.Task;await inner.ReconnectAsync(CancellationToken.None);}finally{f.Reconnecting=false;}}
        public Task<StageReply> CaptureAsync(FrozenRequest r,CancellationToken t){if(f.Reconnecting)Interlocked.Increment(ref f.CallsDuringReconnect);return inner.CaptureAsync(r,t);}public Task<StageReply?> QueryAsync(FrozenRequest r,CancellationToken t)=>inner.QueryAsync(r,t);public Task StopAsync(CancellationToken t)=>inner.StopAsync(t);public ValueTask DisposeAsync()=>inner.DisposeAsync();
    }
    sealed class ReviewPlc(IPlcPort inner,ReviewFactory f):IPlcPort
    {
        public string InstanceId=>inner.InstanceId;public Task StartAsync(CancellationToken t)=>inner.StartAsync(t);public Task<CapabilityHealth> HealthAsync(CancellationToken t)=>inner.HealthAsync(t);public Task ReconnectAsync(CancellationToken t)=>inner.ReconnectAsync(t);public Task<StageReply> SubmitAsync(FrozenRequest r,CancellationToken t)=>inner.SubmitAsync(r,t);
        public async Task<StageReply?> QueryAsync(FrozenRequest r,CancellationToken t){var reply=await inner.QueryAsync(r,t);var n=++f.Queries;return reply is null?null:reply with {Id=n<=65?Wire.Id():f.OriginalReply!.Id,PhysicalCompleted=n>65,At=DateTimeOffset.UtcNow,Evidence=n<=65?"Later original query "+n:"Changed original reply ID"};}public Task StopAsync(CancellationToken t)=>inner.StopAsync(t);public ValueTask DisposeAsync()=>inner.DisposeAsync();
    }
}
