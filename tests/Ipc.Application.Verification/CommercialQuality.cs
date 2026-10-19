using Ipc.Platform;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Application.DirectPlc;
using Ipc.Application.WcsToPlc;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Assembly;
using Microsoft.Data.Sqlite;

static class CommercialQuality
{
    static void Check(bool value, string reason)
    { if (!value) throw new Exception("FAIL CQ03: " + reason); Console.WriteLine("PASS CQ03: " + reason); }
    static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> predicate)
    {
        for (var i=0; i<300; i++) { var s=await app.SnapshotAsync(); if(predicate(s)) return s; await Task.Delay(10); }
        throw new Exception("CQ03 timeout: " + Wire.Encode(await app.SnapshotAsync()));
    }
    static FlowDefinition Flow(string id) => id switch { "sportmaster"=>new SportmasterFlow(), "direct-plc"=>new DirectPlcFlow(), _=>new WcsToPlcFlow() };
    static async Task<ParcelRuntime> Open(ParcelConfiguration config, string directory, bool timestamp=false)
    {
        Directory.CreateDirectory(directory); ExtensionStore.AtomicWrite(Path.Combine(directory,"business.json"),config);
        var launch=new BusinessLaunch(directory,Path.Combine(directory,"data"),null,null);
        var inner=new ParcelCapabilityFactory(launch);
        var app=new ParcelRuntime(launch,Flow(config.ApplicationId),timestamp?new TimestampFactory(inner):inner);
        try { await app.StartAsync(CancellationToken.None); return app; } catch { await app.DisposeAsync(); throw; }
    }
    public static async Task Run(string workspace,string root,string[] args)
    {
        ParcelConfiguration Config(string id)=>Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace,"config",id,"business.json")));
        if(args.Contains("--commercial-quality") || args.Contains("--commercial-quality-current")) await Current(Config("sportmaster"),root);
        if(args.Contains("--commercial-quality") || args.Contains("--commercial-quality-freshness")) await Freshness(Config("sportmaster"),root);
        if(args.Contains("--commercial-quality") || args.Contains("--commercial-quality-recovery")) await Recovery(Config,root);
    }
    static async Task Current(ParcelConfiguration config,string root)
    {
        await using var app=await Open(config,Path.Combine(root,"current"),true);
        Check(app.EmitReferenceAt("left","first-entity","FIRST","first"),"first observation admitted");
        var first=(await Wait(app,s=>s.Operations.Any(o=>o.AdmissionId=="first-entity"&&o.State==ParcelState.SlotResolved))).Operations.Single();
        Check(app.EmitReferenceAt("left","second-entity","SECOND","second"),"second observation admitted");
        var s=await Wait(app,s=>s.Operations.Any(o=>o.AdmissionId=="second-entity"&&o.State==ParcelState.WcsUnknown));
        var current=s.Operations.Single(o=>o.AdmissionId=="second-entity");
        Check(current.ObservedAt<first.ObservedAt && s.Operations.First().Id==first.Id,"real runtime counterexample retains historical timestamp order");
        var probe=new SportmasterUi.SnapshotProbe(app); var vm=new ParcelViewModel(probe,"classic","sportmaster"); var second=new ParcelViewModel(probe,"auxiliary","sportmaster");
        var alternate=new SupplyStationViewModel(probe);
        await vm.RefreshAsync(); await second.RefreshAsync(); await alternate.RefreshDisplayAsync(); vm.SelectedPositionId=second.SelectedPositionId="left";
        Check(vm.Current?.Id==current.Id && vm.CurrentSlot=="未取得" && vm.PositionRows.Single(p=>p.Id=="left").Code=="SECOND" && vm.CurrentCommandTarget.Contains(current.Id),"classic current identity and empty slot match authoritative position despite reversed timestamps");
        Check(second.Current?.Id==current.Id && alternate.Left.Operation?.Id==current.Id,"auxiliary and alternate agree on current identity");
        vm.Selected=first; await vm.ReconcileAsync();
        Check(probe.Queries.Last()==current.Id,"current query targets original current request despite selected historical row");
        await vm.ContinueAsync(); Check(probe.Commands.Last().Owner==current.Id,"current continuation cannot target selected historical parcel");
        var display=await app.SnapshotAsync();
        foreach(var variant in new[]{display with{Operations=display.Operations.Select(o=>o with{ObservedAt=first.ObservedAt}).ToArray()},
            display with{Operations=display.Operations.Where(o=>o.Id!=current.Id).ToArray()},
            display with{Operations=display.Operations.Select(o=>o.Id==current.Id?o with{PositionId="right"}:o).ToArray()},
            display with{Positions=display.Positions!.Select(p=>p.Id=="left"?p with{CurrentOperation=null}:p).ToArray()}})
        {
            probe.Read=()=>Task.FromResult(variant); await vm.RefreshAsync(); await alternate.RefreshDisplayAsync();
            var expected=variant.Positions!.Single(p=>p.Id=="left").CurrentOperation is not null && variant.Operations.Any(o=>o.Id==current.Id&&o.PositionId=="left")?current.Id:null;
            Check(vm.Current?.Id==expected && alternate.Left.Operation?.Id==expected,"equal timestamp / missing ID / wrong owner / empty current never falls back to history");
        }
    }
    static async Task Freshness(ParcelConfiguration config,string root)
    {
        await using var app=await Open(config,Path.Combine(root,"freshness"));
        var probe=new SportmasterUi.SnapshotProbe(app); var vm=new ParcelViewModel(probe,"classic","sportmaster");
        await vm.RefreshAsync(); Check(vm.CanAdmit && vm.Status=="Ready","real initial snapshot ready");
        probe.Read=()=>Task.FromException<ParcelSnapshot>(new IOException("synthetic read failure"));
        await vm.RunAsync(vm.RefreshAsync);
        Check(!vm.CanAdmit && !vm.CanResume && !vm.CanConfigure && vm.Status.Contains("失效") && !vm.Health.Contains("就绪"),"classic failed read invalidates Ready, health and new actions");
        var writes=probe.Writes;
        foreach(var action in new Func<Task>[] {()=>vm.CommandAsync("Resume"),vm.ReconnectAsync,vm.SaveConfigurationAsync,vm.ReconcileAsync,vm.InstallAsync,vm.ArchiveHistoryAsync})
            await vm.RunAsync(action);
        vm.Emit(); Check(probe.Writes==writes,"stale actions fail at VM boundary before any application write");
        probe.Read=null; await vm.PauseAsync();
        Check(probe.Commands.Last().Kind=="Pause" && vm.Snapshot!.Paused,"independent pause still reads current revision and reaches application");
        await vm.RefreshAsync();
        var pending=new TaskCompletionSource<ParcelSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.Read=()=>pending.Task; var read=vm.RefreshAsync();
        await Task.Delay(3100);
        Check(!vm.CanAdmit && !vm.CanResume && vm.Status.Contains("失效"),"pending read does not renew last success deadline");
        pending.SetResult(await app.SnapshotAsync()); await read;
        Check(vm.Fresh && vm.Snapshot!.Paused && vm.Status==vm.Snapshot.State && vm.CanResume,"new successful snapshot restores permitted actions");
        var old=new TaskCompletionSource<ParcelSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.Read=()=>old.Task; var oldRead=vm.RefreshAsync(); probe.Read=null; await vm.RefreshAsync();
        old.SetException(new IOException("late old failure")); try {await oldRead;} catch(IOException){}
        Check(vm.CanResume,"older read failure cannot invalidate newer applied success");
        var oldSuccess=new TaskCompletionSource<ParcelSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.Read=()=>oldSuccess.Task; var oldSuccessRead=vm.RefreshAsync();
        probe.Read=()=>Task.FromException<ParcelSnapshot>(new IOException("new failure")); await vm.RunAsync(vm.RefreshAsync);
        oldSuccess.SetResult(await app.SnapshotAsync()); await oldSuccessRead;
        Check(!vm.CanResume,"older success cannot revive snapshot after newer failure");
        probe.Read=null; await vm.RefreshAsync(); Check(vm.CanResume,"fresh poll recovers after ordered failure");
    }
    static async Task Recovery(Func<string,ParcelConfiguration> config,string root)
    {
        var c=config("direct-plc") with {Weight=new(WeightSource.Measured,WeightUnit.Kg,3,WaitTimeoutMs:30000),
            Scale=new("weight","reference-weight","1.0.0","weight","reference://scale/cq03","PARCEL-REFERENCE-v1",[])};
        var directory=Path.Combine(root,"abandon");
        await using(var app=await Open(c,directory))
        { app.EmitReference("unsent","CANCEL","unsent"); await Wait(app,s=>s.Operations.Any(o=>o.State==ParcelState.WaitingWeight)); await app.StopAsync(CancellationToken.None); }
        ParcelOperation abandoned;
        await using(var app=await Open(c,directory))
        {
            var s=await app.SnapshotAsync(); var op=s.Operations.Single(); Check(op.RecoveryRequired,"restart waiting weight requires inspection");
            var result=await app.CommandAsync(new(Wire.Id(),s.Revision,"AbandonUnsubmitted",op.Id,"Synthetic inspected original unsent parcel removed"));
            abandoned=(await app.SnapshotAsync()).Operations.Single();
            Check(result.Status=="Persisted" && abandoned.State==ParcelState.Abandoned && !abandoned.RecoveryRequired,"safe abandonment converges recovery flag without external send");
        }
        foreach(var archived in new[]{false,true})
        {
            var fixture=Path.Combine(root,"db5-abandoned-"+archived); var legacy=abandoned with{RecoveryRequired=true};
            using(var db5=new ParcelRepository(fixture,"direct-plc",5))
            {db5.Commit(legacy,"Abandoned","Original inspected cancellation with historical stale flag"); if(archived)db5.Archive([legacy.Id],"Synthetic historical archive");}
            using(var db6=new ParcelRepository(fixture,"direct-plc",6))
                Check(db6.StorageVersion==6 && db6.ReadOperation(legacy.Id) is{RecoveryRequired:false,State:ParcelState.Abandoned} && db6.ArchivedCount==(archived?1:0),"DB5 resident/archive terminal recovery flag repairs without reactivating history");
        }
        var sportDirectory=Path.Combine(root,"sport-restarted-unknown");
        await using(var app=await Open(config("sportmaster"),sportDirectory,true))
        {
            app.EmitReferenceAt("left","sport-first","FIRST","sport-first"); await Wait(app,s=>s.Operations.Any(o=>o.State==ParcelState.SlotResolved));
            app.EmitReferenceAt("left","sport-unknown","SECOND","sport-second"); await Wait(app,s=>s.Operations.Any(o=>o.State==ParcelState.WcsUnknown));
            await app.StopAsync(CancellationToken.None);
        }
        await using(var app=await Open(config("sportmaster"),sportDirectory))
        {
            var op=(await app.SnapshotAsync()).Operations.Single(o=>o.AdmissionId=="sport-unknown");
            Check(op.RecoveryRequired && op.State==ParcelState.WcsUnknown,"Sportmaster unknown remains blocked after restart");
            await app.ReconcileAsync(op.Id); var terminal=(await app.SnapshotAsync()).Operations.Single(o=>o.Id==op.Id);
            Check(terminal.State==ParcelState.SlotResolved && !terminal.RecoveryRequired && terminal.Wcs!.Attempts==1 && Wire.Encode(terminal.Wcs.Request)==Wire.Encode(op.Wcs!.Request),"Sportmaster original WCS query clears entity check only after authoritative slot, without PLC or resend");
        }
        foreach(var id in new[]{"direct-plc","wcs-to-plc"})
        {
            var run=Path.Combine(root,"query-"+id); ParcelOperation before;
            await using(var app=await Open(config(id),run))
            {app.EmitReference("accepted","QUERY","accepted"); before=(await Wait(app,s=>s.Operations.Any(o=>o.State==ParcelState.PlcAccepted))).Operations.Single(); await app.StopAsync(CancellationToken.None);}
            await using(var app=await Open(config(id),run))
            {
                var recovered=(await app.SnapshotAsync()).Operations.Single(); Check(recovered.RecoveryRequired && recovered.State==ParcelState.PlcAccepted,id+" acceptance remains unresolved across restart");
                await app.ReconcileAsync(recovered.Id); var terminal=(await app.SnapshotAsync()).Operations.Single();
                LegacyTerminalChecks(terminal,before,root);
                Check(terminal.State==ParcelState.PhysicalCompleted && !terminal.RecoveryRequired && terminal.Plc!.Attempts==1 && Wire.Encode(terminal.Plc.Request)==Wire.Encode(before.Plc!.Request),id+" original authoritative query terminates recovery without resending or changing payload");
            }
        }
    }
    static void LegacyTerminalChecks(ParcelOperation terminal,ParcelOperation accepted,string root)
    {
        foreach(var archived in new[]{false,true})
        {
            var directory=Path.Combine(root,"legacy-terminal-"+terminal.FrozenConfiguration.ApplicationId+"-"+archived);
            var legacy=terminal with{RecoveryRequired=true}; string original=Wire.Encode(legacy);
            using(var db=new ParcelRepository(directory,terminal.FrozenConfiguration.ApplicationId,5))
            {
                Seed(db,legacy,true);
                if(archived)db.Archive([legacy.Id],"Synthetic original completed archive");
            }
            using(var upgraded=new ParcelRepository(directory,terminal.FrozenConfiguration.ApplicationId,6))
                Check(upgraded.ReadOperation(legacy.Id) is{RecoveryRequired:false} repaired &&
                    Wire.Encode(repaired with{RecoveryRequired=true})==original && upgraded.ArchivedCount==(archived?1:0),
                    "proven original terminal receipt clears only stale flag in DB5 resident/archive");
            using var sql=new SqliteConnection("Data Source="+Path.Combine(directory,terminal.FrozenConfiguration.ApplicationId+".db"));sql.Open();
            using var audit=sql.CreateCommand();audit.CommandText="SELECT detail FROM parcel_audit WHERE event='TerminalRecoveryConverged'";
            using var json=System.Text.Json.JsonDocument.Parse((string)audit.ExecuteScalar()!);
            Check(json.RootElement.GetProperty("OriginalBody").GetString()==original && json.RootElement.GetProperty("OriginalSha256").GetString()==Wire.Hash(original),"terminal repair retains exact original JSON and SHA in transactional audit");
        }
        foreach(var variant in new[]{"accepted","unknown","missing-raw","conflict","unproved-abandon"})
        {
            var directory=Path.Combine(root,"legacy-refuse-"+terminal.FrozenConfiguration.ApplicationId+"-"+variant);
            var op=variant switch
            {
                "accepted"=>accepted with{RecoveryRequired=true},
                "unknown"=>accepted with{RecoveryRequired=true,State=ParcelState.PlcUnknown,Plc=accepted.Plc! with{State=StageState.Unknown}},
                "conflict"=>terminal with{RecoveryRequired=true,Plc=terminal.Plc! with{State=StageState.Conflict}},
                "unproved-abandon"=>terminal with{RecoveryRequired=true,State=ParcelState.Abandoned,Plc=null,Wcs=null,Capture=null,Reason="Unsubmitted parcel inspected and removed: synthetic unproved label"},
                _=>terminal with{RecoveryRequired=true}
            };
            using(var db=new ParcelRepository(directory,op.FrozenConfiguration.ApplicationId,5))Seed(db,op,variant!="missing-raw");
            var database=Path.Combine(directory,op.FrozenConfiguration.ApplicationId+".db");var hash=Wire.FileHash(database);
            try {using var upgrade=new ParcelRepository(directory,op.FrozenConfiguration.ApplicationId,6);throw new Exception("Unsafe upgrade accepted: "+variant);}
            catch(InvalidDataException error){Check(error.Message.Contains("LegacyUnsettled") && Wire.FileHash(database)==hash,variant+" retains original DB5 bytes and refuses future marker");}
        }
        static void Seed(ParcelRepository db,ParcelOperation op,bool raw)
        {
            db.Commit(op,"HistoricalFixture","Original operation with legacy recovery flag");
            if(!raw)return;
            foreach(var stage in new[]{op.Wcs,op.Plc}.OfType<StageFact>())foreach(var reply in stage.Replies.Where(r=>r.BusinessAccepted is not null))
            {
                var occurrence=db.BeginReceipt(op,stage,"Query");db.StoreRaw(occurrence,reply);db.Commit(op,"OriginalReplyFixture","Original authoritative raw preserved",occurrence:occurrence);
            }
        }
    }
    sealed class TimestampFactory(ParcelCapabilityFactory inner):IParcelCapabilityFactory{
 int observations;
 public ParcelConfiguration Normalize(ParcelConfiguration c)=>inner.Normalize(c);
 public void Validate(ParcelConfiguration c)=>inner.Validate(c);
 public CapabilityRequirements Requirements(CapabilityBinding b)=>inner.Requirements(b);
 public ParcelPorts Create(ParcelConfiguration c){var p=inner.Create(c);var readers=p.Readers!.ToDictionary(x=>x.Key,x=>(IControlledObservationSource)new TimestampReader(x.Value,()=>TimeSpan.FromMilliseconds(Interlocked.Increment(ref observations)>1?-750:0)));return p with{Reader=readers[c.Reader.DeviceId],Readers=readers,Wcs=new LostSecondReply(p.Wcs!)};}
 public ValueTask DisposeAsync()=>inner.DisposeAsync();
}
sealed class TimestampReader(IControlledObservationSource inner,Func<TimeSpan> offset):IControlledObservationSource,ISyntheticObservationInput{
 public string DeviceId=>inner.DeviceId;public string Epoch=>inner.Epoch;public string ConfirmationSource=>inner.ConfirmationSource;
 public Task StartControlledAsync(string e,Func<Observation,bool> f,Action<DeviceHealthConfirmation> h,CancellationToken t)=>inner.StartControlledAsync(e,o=>f(o with{ObservedAt=o.ObservedAt+offset()}),h,t);
 public Task StartAsync(Func<Observation,bool> f,Action<string,string,DeviceHealth> h,CancellationToken t)=>throw new NotSupportedException();
 public bool Emit(string k,string a,string[] c,int? q,string s)=>((ISyntheticObservationInput)inner).Emit(k,a,c,q,s);
 public Task StopAsync(CancellationToken t)=>inner.StopAsync(t);public ValueTask DisposeAsync()=>inner.DisposeAsync();
}
sealed class LostSecondReply(IWcsPort inner):IWcsPort{
 int calls; public string InstanceId=>inner.InstanceId;
 public async Task<StageReply> ResolveSlotAsync(FrozenRequest r,CancellationToken t){var result=await inner.ResolveSlotAsync(r,t);if(Interlocked.Increment(ref calls)==2)throw new IOException("synthetic response loss after peer commit");return result;}
 public Task<StageReply?> QueryAsync(FrozenRequest r,CancellationToken t)=>inner.QueryAsync(r,t);
 public Task StartAsync(CancellationToken t)=>inner.StartAsync(t);public Task<CapabilityHealth> HealthAsync(CancellationToken t)=>inner.HealthAsync(t);public Task ReconnectAsync(CancellationToken t)=>inner.ReconnectAsync(t);public Task StopAsync(CancellationToken t)=>inner.StopAsync(t);public ValueTask DisposeAsync()=>inner.DisposeAsync();
}

}
