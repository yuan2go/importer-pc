using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
using Microsoft.Data.Sqlite;
using System.Diagnostics;

static partial class ArchitectureQuality
{
    static ParcelConfiguration InterruptedConfig()
    {
        var c = Config("sportmaster");
        var left = B("camera") with { Target = c.Topology!.Readers[0].DeviceId };
        var right = left with { InstanceId = "quality.camera.right", Target = c.Topology.Readers[1].DeviceId };
        return c with { Images = new(left, B("upload"), Strategy: UploadStrategy.Attached), CapabilityInstances = [c.Wcs!, left, right, B("upload")],
            Topology = c.Topology with { Positions = [c.Topology.Positions[0], c.Topology.Positions[1] with { CameraInstanceId = right.InstanceId }] } };
    }
    public static async Task UploadCrashChild(string source, string path)
    {
        workspace = source;
        var c = InterruptedConfig(); Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path,"business.json"),c);
        var launch = new BusinessLaunch(path,Path.Combine(path,"data"),null,null);
        var crash = new UploadCrashFactory(new ParcelCapabilityFactory(launch));
        var app = new ParcelRuntime(launch,new SportmasterFlow(),crash); await app.StartAsync(default);
        await ManagementModeFixture.Authorize(app);
        app.EmitReferenceAt("left","interrupted-attached","00017","interrupted-source");
        await crash.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var s = await Wait(app, s => s.Operations.FirstOrDefault()?.State == ParcelState.SlotResolved);
        ExtensionStore.AtomicWrite(Path.Combine(path,"before.json"),s); Environment.Exit(74);
    }
    sealed class UploadCrashFactory(ParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public CapabilityRequirements Requirements(CapabilityBinding b) => inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c) { var p=inner.Create(c); return p with { Uploader=new WaitingUpload(p.Uploader!,Entered) }; }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class WaitingUpload(IImageUploader inner, TaskCompletionSource entered) : IImageUploader
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public async Task<StageReply> UploadAsync(FrozenRequest r,ReadOnlyMemory<byte> bytes,CancellationToken t)
        { var reply=await inner.UploadAsync(r,bytes,t); entered.TrySetResult(); await Task.Delay(Timeout.Infinite,t); return reply; }
        public Task<StageReply?> QueryAsync(FrozenRequest r,CancellationToken t) => inner.QueryAsync(r,t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    // Keep the real capability factory, receiver and runtime. Only the health
    // completion and the empty authoritative Query are controlled by this fixture.
    sealed class RecoveryQueryFactory(IParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        int holdHealth, queryCalls;
        public int QueryCalls => Volatile.Read(ref queryCalls);
        public TaskCompletionSource HealthEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource healthRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void HoldNextHealth() => Interlocked.Exchange(ref holdHealth,1);
        public void ReleaseHealth() { Interlocked.Exchange(ref holdHealth,0); healthRelease.TrySetResult(); }
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public CapabilityRequirements Requirements(CapabilityBinding b) => inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c)
        {
            var p=inner.Create(c);
            return p with { Wcs=new Health(p.Wcs!,this), Uploader=new UploadQuery(p.Uploader!,this) };
        }
        public ValueTask DisposeAsync() { ReleaseHealth(); return inner.DisposeAsync(); }
        sealed class Health(IWcsPort inner,RecoveryQueryFactory owner) : IWcsPort
        {
            public string InstanceId => inner.InstanceId;
            public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
            public async Task<CapabilityHealth> HealthAsync(CancellationToken t)
            {
                if(Volatile.Read(ref owner.holdHealth)!=0)
                { owner.HealthEntered.TrySetResult(); await owner.healthRelease.Task.WaitAsync(t); }
                return await inner.HealthAsync(t);
            }
            public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
            public Task<StageReply> ResolveSlotAsync(FrozenRequest r,CancellationToken t) => inner.ResolveSlotAsync(r,t);
            public Task<StageReply?> QueryAsync(FrozenRequest r,CancellationToken t) => inner.QueryAsync(r,t);
            public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
        sealed class UploadQuery(IImageUploader inner,RecoveryQueryFactory owner) : IImageUploader
        {
            public string InstanceId => inner.InstanceId;
            public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
            public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
            public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
            public Task<StageReply> UploadAsync(FrozenRequest r,ReadOnlyMemory<byte> bytes,CancellationToken t) => inner.UploadAsync(r,bytes,t);
            public Task<StageReply?> QueryAsync(FrozenRequest r,CancellationToken t)
            { Interlocked.Increment(ref owner.queryCalls); return inner.QueryAsync(r,t); }
            public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
    static async Task Recovery02Checks()
    {
        var name="real-upload-crash"; var path=Path.Combine(root,name); var c=InterruptedConfig();
        var psi=new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput=true,RedirectStandardError=true };
        ManagementModeFixture.ShareWith(psi);
        foreach(var arg in new[]{typeof(ArchitectureQuality).Assembly.Location,workspace,"--upload-crash-child",path}) psi.ArgumentList.Add(arg);
        using(var child=Process.Start(psi)!)
        { var output=child.StandardOutput.ReadToEndAsync(); var error=child.StandardError.ReadToEndAsync(); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); Console.Write(await output); Console.Write(await error); Check(child.ExitCode==74,"real process stops inside upload after independent receiver commit and before response custody"); }
        var before=Wire.Decode<ParcelSnapshot>(File.ReadAllText(Path.Combine(path,"before.json"))).Operations.Single();
        string occurrence;
        RecoveryQueryFactory? recoveryFactory = null;
        await using(var app=await Open(c,name,f=>recoveryFactory=new RecoveryQueryFactory(new FaultFactory(f){EmptyUploadQuery=true})))
        {
            var s=await app.SnapshotAsync(); var op=s.Operations.Single();
            Check(op.State==ParcelState.SlotResolved && op.Reason==before.Reason && op.Wcs!.Attempts==1 && op.Upload!.State==StageState.Unknown && !op.ImageReleased,
                "Dispatching interruption preserves accepted main state/reason and the original upload/image responsibility");
            recoveryFactory!.HoldNextHealth();
            await recoveryFactory.HealthEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Maintenance(app);
            var inventory=await Command(app,"ReceiptResponsibilities",op.Id);
            var rows=Wire.Decode<ParcelReceiptResponsibility[]>(inventory.Reason); occurrence=rows.Single(r=>r.State=="Awaiting").Id;
            var fenced=await Command(app,"ReconcileReceipt",occurrence,"Capability health is still in flight; retain original responsibility");
            var fencedSnapshot=await app.SnapshotAsync();
            var fencedInventory=await Command(app,"ReceiptResponsibilities",op.Id);
            Check(fenced.Status=="Rejected" && fencedSnapshot.Availability?.Configure==false && recoveryFactory.QueryCalls==0 &&
                Wire.Encode(fencedSnapshot.Operations.Single().Upload)==Wire.Encode(op.Upload) && !fencedSnapshot.Operations.Single().ImageReleased &&
                fencedInventory.Reason==inventory.Reason,
                "in-flight health fences original recovery before Query without changing receipt custody, upload facts or image ownership");
            recoveryFactory.ReleaseHealth();
            await Wait(app,s=>s.Availability?.Configure==true);
            var inconclusive=await Command(app,"ReconcileReceipt",occurrence,"Synthetic authority returned no result; retain original");
            Console.WriteLine("AQ original receipt recovery after health drain: "+Wire.Encode(inconclusive));
            Check(recoveryFactory.QueryCalls==1,"drained explicit recovery executes exactly one original upload Query");
            Check(inconclusive.Status=="Unresolved" && !(await app.SnapshotAsync()).Operations.Single().ImageReleased,"inconclusive original Query never dispositions missing raw or releases its image");
            await Command(app,"Resume");
            app.EmitReferenceAt("right","unrelated-right","00018","unrelated-right-source");
            await Wait(app,s=>s.Operations.Any(o=>o.AdmissionId=="unrelated-right" && o.State==ParcelState.SlotResolved));
            Check((await app.OperationAsync(op.Id))!.Wcs!.Attempts==1,"unrelated position continues while old attachment retains one original WCS attempt");
            await Maintenance(app); await Wait(app,s=>s.Availability?.Configure==true);
            await app.BackupAsync(Path.Combine(root,"legacy-before.backup.db"));
        }
        // Current-format restart keeps the proven main result and missing upload custody.
        // Old projection conversion is separately refused by LegacyProjectionGuard.
        var database=Path.Combine(DirectoryFor(name,c),"sportmaster.db");
        await using(var app=await Open(c,name))
        {
            var restored=await app.OperationAsync(before.Id);
            Check(restored!.State==ParcelState.SlotResolved && restored.Reason==before.Reason && Wire.Encode(restored.Wcs)==Wire.Encode(before.Wcs) && Wire.Encode(restored.Upload!.Request)==Wire.Encode(before.Upload!.Request),
                "current restart retains the committed main projection without rewriting original requests or missing upload custody");
            await app.ExitManagementModeAsync();
            var denied=await Command(app,"ReconcileReceipt",occurrence,"Must authenticate first"); Check(denied.Status=="Rejected","missing receipt recovery requires paused authenticated maintenance");
            await Maintenance(app); await Wait(app,s=>s.Availability?.Configure==true);
            var result=await Command(app,"ReconcileReceipt",occurrence,"Original receiver queried; authoritative upload result and image verified");
            Check(result.Status=="OutcomeKnownRawMissing","explicit recovery links business outcome to its own Query/T3 while original raw responsibility remains");
            Check(!(await app.OperationAsync(before.Id))!.ImageReleased,"known business outcome does not release missing original raw/image custody");
            var rows=Wire.Decode<ParcelReceiptResponsibility[]>((await Command(app,"ReceiptResponsibilities",before.Id)).Reason);
            var original=rows.Single(r=>r.Id==occurrence);
            Check(original.State=="Awaiting" && original.Source=="Submit" && original.RawHash is null && rows.Any(r=>r.Source=="Query" && r.State=="Committed" && r.RawHash is not null),
                "original Awaiting remains pending with absent raw; distinct query has its own durable raw");
            Check(File.Exists(Path.Combine(DirectoryFor(name,c),before.Image!.RelativeFile)) && (await app.OperationAsync(before.Id))!.Upload!.Attempts==1,
                "original missing raw keeps image bytes and upload is never resubmitted");
            await Command(app,"ArchiveHistory",value:"Synthetic settled archive after explicit receipt resolution");
            Check((await app.OperationAsync(before.Id))!.Wcs!.Request.Id==before.Wcs!.Request.Id,"archive keeps frozen original request after outcome recovery");
        }
        await using(var app=await Open(c,name)) Check(!(await app.OperationAsync(before.Id))!.ImageReleased,"independent missing raw/image custody survives restart after outcome query");
        await CurrentCustody07Checks(c);
        await CurrentOfflineRecoveryCuts(c);
        await LegacyProjection02Checks(c,before.Id);
        await Relocation02Checks(c);
        await LegacyConflict02Checks();
    }
    static async Task CurrentCustody07Checks(ParcelConfiguration c)
    {
        var directory = DirectoryFor("current-db8-offline-custody", c);
        ParcelRecovery.Restore(Path.Combine(root, "legacy-before.backup.db"), directory);
        await using (var app = await Open(c, "current-db8-offline-custody", factory => new FaultFactory(factory) { EmptyUploadQuery = true }))
            await Wait(app, snapshot => snapshot.Operations.Single(x => x.AdmissionId == "unrelated-right").ImageReleased);
        var database = Path.Combine(directory, "sportmaster.db"); string occurrence; ParcelOperation original;
        using (var repository = new ParcelRepository(directory, "sportmaster", 9))
        {
            original = repository.Load().Single(x => x.AdmissionId == "interrupted-attached");
            occurrence = repository.PendingReceipts().Single(x => x.Operation == original.Id && x.Stage == "image-upload" && x.Reply is null).Id;
        }
        var calls = 0;
        async Task<StageReply?> Query(string json, string _, string __, CancellationToken token)
        {
            calls++; var operation = Wire.Decode<ParcelOperation>(json); var request = operation.Upload!.Request;
            await using var uploader = new Ipc.Components.ReferenceImageUploader(request.Binding, request.Binding.Parameters["peer-root"]);
            await uploader.StartAsync(token); return await uploader.QueryAsync(request, token);
        }
        var release = Path.GetDirectoryName(typeof(ArchitectureQuality).Assembly.Location)!;
        var first = await ParcelOfflineReceiptRecovery.RecoverAsync(database, Wire.FileHash(database), Path.Combine(root, "current8-first.backup.db"), occurrence, release,
            "Synthetic current-schema offline query; retain original missing raw", new SportmasterFlow(), Query, expectedWalHash: WalHash(database));
        Check(calls == 1 && ParcelRepository.ReadStorageVersion(database, "sportmaster") == 9 && Wire.Encode(first).Contains("\"DatabaseVersion\": 9", StringComparison.Ordinal),
            "current SQL9 offline receipt recovery performs one original Query and returns SQL9 without a post-query downgrade failure");
        await ParcelOfflineReceiptRecovery.RecoverAsync(database, Wire.FileHash(database), Path.Combine(root, "current8-second.backup.db"), occurrence, release,
            "Synthetic repeat must reuse its committed original Query basis", new SportmasterFlow(), Query, expectedWalHash: WalHash(database));
        using var readback = new ParcelRepository(directory, "sportmaster", 9); var current = readback.Load().Single(x => x.Id == original.Id);
        Check(calls == 1 && current.Wcs!.Attempts == 1 && current.Upload!.Attempts == 1 && !current.ImageReleased && readback.ReceiptById(occurrence) is { State: "Awaiting", RawHash: null, QueryOccurrence: not null },
            "repeated SQL9 offline recovery reuses original Query, preserves missing raw/image custody and never resubmits WCS or upload");
    }
    static async Task LegacyProjection02Checks(ParcelConfiguration c,string id)
    {
        var directory=DirectoryFor("legacy-db3-repair",c); ParcelRecovery.Restore(Path.Combine(root,"legacy-before.backup.db"),directory);
        var database=Path.Combine(directory,"sportmaster.db");
        using(var db=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()))
        {
            db.Open(); using var q=db.CreateCommand(); q.CommandText="UPDATE parcel_operations SET body=json_set(body,'$.State','Blocked','$.Reason','RawUnavailable:old-version') WHERE id=$id"; q.Parameters.AddWithValue("$id",id); q.ExecuteNonQuery();
            q.CommandText=SyntheticPriorStorage.RemoveNine + "DROP TABLE IF EXISTS parcel_commercial_semantics; DROP TABLE IF EXISTS parcel_integration_id_owners; DROP TABLE IF EXISTS parcel_integration_id_ranges; DROP TABLE IF EXISTS parcel_integration_id_restores; DROP TABLE parcel_integration_semantics; DELETE FROM platform_migrations WHERE version>=6; DROP TABLE parcel_receipt_custody; DROP TABLE parcel_archive_versions; DELETE FROM platform_migrations WHERE version=5; DROP TABLE parcel_history_index; DROP TABLE parcel_archives; DROP TABLE parcel_reply_identities; DROP TABLE parcel_archived_receipts; DROP TABLE parcel_overload_ack; DROP TABLE parcel_identity_conflicts; DROP TABLE parcel_configuration_audit; DELETE FROM platform_migrations WHERE version=4"; q.ExecuteNonQuery();
        }
        var hash=Wire.FileHash(database); var backup=Path.Combine(root,"legacy-repair-preserved.db");
        try { ParcelRepository.RepairLegacyMain(database,hash,backup,new SportmasterFlow(),"Inspect synthetic old projection");Check(false,"old projection repair must refuse"); }
        catch(InvalidDataException){Check(Wire.FileHash(database)==hash && !File.Exists(backup) && !Directory.Exists(backup+".recovery") && ParcelRepository.ReadStorageVersion(database,"sportmaster")==3,"old projection repair refuses before backup/writes and preserves missing custody");}
        await Task.CompletedTask;
    }
    static async Task LegacyConflict02Checks()
    {
        var c = Config("sportmaster"); var name = "legacy-conflict";
        await using (var app = await Open(c, name)) await Maintenance(app);
        using(var repo=new ParcelRepository(DirectoryFor(name,c),c.ApplicationId)) repo.Commit(null,"IdentityConflict","Legacy synthetic audit without scope");
        var originalFiles=CustodyFiles(name,c);
        try {await using var app=await Open(c,name);Check(false,"old unscoped conflict must refuse");}
        catch(InvalidDataException){Check(originalFiles.All(p=>Wire.FileHash(p.Key)==p.Value),"old conflict audit is retained without synthesized identities or a force-clear path");}
        await using (var app = await Open(c,"same-position-conflict"))
        {
            app.EmitReferenceAt("left","local-conflict","001","local-conflict-1"); await Wait(app,s=>s.Operations.Any(o=>o.State==ParcelState.SlotResolved));
            app.EmitReferenceAt("left","local-conflict","002","local-conflict-2"); var s=await Wait(app,s=>s.IdentityConflicts?.Length==1);
            Check(!s.Positions!.Single(p=>p.Id=="left").Admit && s.Positions!.Single(p=>p.Id=="right").Admit,"same-position durable conflict isolates left while unrelated right retains admission");
        }
        await using (var app = await Open(Config("sportmaster") with { Retention = new(MinimumFreeBytes:1099511627776) },"backup-capacity"))
        {
            await Maintenance(app); var destination=Path.Combine(root,"no-space.backup.db");
            try { await app.BackupAsync(destination); Check(false,"backup must preserve configured free reserve"); }
            catch(IOException) { Check(!File.Exists(destination) && !Directory.Exists(destination+".recovery"),"backup capacity preflight rejects before creating any incomplete recovery set"); }
        }
    }
    static async Task Relocation02Checks(ParcelConfiguration c)
    {
        var backup=Path.Combine(root,"legacy-before.backup.db"); var target=DirectoryFor("relocated-history",c);
        var plan=ParcelRecovery.PlanRestore(backup,target);
        Check(plan.CopyBytes>=new FileInfo(backup).Length && plan.RemainingGrowthBytes==plan.AvailableBytes-plan.CopyBytes-plan.MinimumFreeBytes && !Directory.Exists(target),"new-volume restore plan uses measured complete recovery bytes plus explicit reserve before creating target");
        ParcelRecovery.Restore(backup,target);
        await using var app=await Open(c,"relocated-history"); var s=await app.SnapshotAsync();
        Check(s.Paused && s.Operations.Any(o=>o.AdmissionId=="interrupted-attached") && s.Operations.Any(o=>o.AdmissionId=="unrelated-right"),"whole-history relocation preserves both positions, unresolved image/raw and starts isolated");
    }
}
