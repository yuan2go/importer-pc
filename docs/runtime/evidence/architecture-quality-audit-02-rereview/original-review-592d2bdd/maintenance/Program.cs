using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
var root=Path.Combine(AppContext.BaseDirectory,"synthetic");Directory.CreateDirectory(root);
var c=Wire.Decode<ParcelConfiguration>(File.ReadAllText("/Users/yuan/.codex/worktrees/architecture-quality-02/importer-pc/config/sportmaster/business.json"));
c=c with {Wcs=c.Wcs! with {Parameters=new(c.Wcs.Parameters){["reply-mode"]="lose-after-commit"}}};
ExtensionStore.AtomicWrite(Path.Combine(root,"business.json"),c);var launch=new BusinessLaunch(root,Path.Combine(root,"data"),null,null);
var f=new MaintenanceFactory(new ParcelCapabilityFactory(launch));await using var a=new ParcelRuntime(launch,new SportmasterFlow(),f);await a.StartAsync(default);
a.EmitReferenceAt("left","review-maint-original","001","review-maint-source");
for(int i=0;i<200&&(await a.SnapshotAsync()).Operations.FirstOrDefault()?.State!=ParcelState.WcsUnknown;i++)await Task.Delay(20);
var op=(await a.SnapshotAsync()).Operations.Single();if(op.State!=ParcelState.WcsUnknown)throw new Exception("Unknown required");
async Task<BusinessReceipt> Cmd(string kind){for(int i=0;i<10;i++){var s=await a.SnapshotAsync();var r=await a.CommandAsync(new(Wire.Id(),s.Revision,kind));if(r.Status!="Conflict")return r;}throw new Exception("Revision unstable");}
await Cmd("Pause");var authorization=await a.AuthorizeMaintenanceAsync("maintenance","SIMULATION_ONLY");if(authorization.Status!="Authorized")throw new Exception("Auth required");
var maintenance=a.ReconnectDeviceAsync(c.Wcs!.InstanceId);await f.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
try{
 var q=await a.ReconcileAsync(op.Id,"wcs-slot");var canResume=(await a.SnapshotAsync()).Availability!.Resume;var r=await Cmd("Resume");
 a.EmitReferenceAt("right","review-during-maintenance","002","review-maint-new-source");
 for(int i=0;i<50&&(await a.SnapshotAsync()).Operations.Length<2;i++)await Task.Delay(10);
 var s=await a.SnapshotAsync();Console.WriteLine(Wire.Encode(new{Probe="SharedWcsReconnectFence",ReconnectStillRunning=!maintenance.IsCompleted,CallsDuringReconnect=f.CallsDuringReconnect,Query=q.Status,MainAfterQuery=(await a.OperationAsync(op.Id))!.State,CanResume=canResume,Resume=r.Status,NewOperationAccepted=s.Operations.Length==2,DefectReproduced=f.CallsDuringReconnect>0}));
}finally{f.Release.TrySetResult();await maintenance;}
sealed class MaintenanceFactory(ParcelCapabilityFactory inner):IParcelCapabilityFactory{
 public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);public bool Reconnecting;public int CallsDuringReconnect;
 public ParcelConfiguration Normalize(ParcelConfiguration c)=>inner.Normalize(c);public void Validate(ParcelConfiguration c)=>inner.Validate(c);public CapabilityRequirements Requirements(CapabilityBinding b)=>inner.Requirements(b);public ParcelPorts Create(ParcelConfiguration c){var p=inner.Create(c);return p with {Wcs=new HeldWcs(p.Wcs!,this)};}public ValueTask DisposeAsync()=>inner.DisposeAsync();
}
sealed class HeldWcs(IWcsPort inner,MaintenanceFactory owner):IWcsPort{
 public string InstanceId=>inner.InstanceId;public Task StartAsync(CancellationToken t)=>inner.StartAsync(t);public Task<CapabilityHealth> HealthAsync(CancellationToken t)=>inner.HealthAsync(t);
 public async Task ReconnectAsync(CancellationToken t){owner.Reconnecting=true;owner.Entered.TrySetResult();try{await owner.Release.Task.WaitAsync(t);}finally{owner.Reconnecting=false;}}
 public Task<StageReply> ResolveSlotAsync(FrozenRequest r,CancellationToken t){if(owner.Reconnecting)Interlocked.Increment(ref owner.CallsDuringReconnect);return inner.ResolveSlotAsync(r,t);}
 public Task<StageReply?> QueryAsync(FrozenRequest r,CancellationToken t){if(owner.Reconnecting)Interlocked.Increment(ref owner.CallsDuringReconnect);return inner.QueryAsync(r,t);}
 public Task StopAsync(CancellationToken t)=>inner.StopAsync(t);public ValueTask DisposeAsync()=>inner.DisposeAsync();
}
