using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.DirectPlc;
using Ipc.Assembly;
using Ipc.Platform;
var root=Path.Combine(AppContext.BaseDirectory,"synthetic");Directory.CreateDirectory(root);
var c=Wire.Decode<ParcelConfiguration>(File.ReadAllText("/Users/yuan/.codex/worktrees/architecture-quality-02/importer-pc/config/direct-plc/business.json"));
c=c with {Plc=c.Plc! with {Parameters=new(c.Plc.Parameters){["complete-on-query"]="false"}}};
ExtensionStore.AtomicWrite(Path.Combine(root,"business.json"),c);var launch=new BusinessLaunch(root,Path.Combine(root,"data"),null,null);
var dir=Path.Combine(launch.DataRoot,c.Mode.ToString(),c.StationId,c.ApplicationId);
ParcelOperation original;
await using(var a=new ParcelRuntime(launch,new DirectPlcFlow(),new ParcelCapabilityFactory(launch))){
 await a.StartAsync(default);a.EmitReference("review-evict-original","001","review-evict-source");
 for(int i=0;i<200&&(await a.SnapshotAsync()).Operations.FirstOrDefault()?.State!=ParcelState.PlcAccepted;i++)await Task.Delay(20);
 original=(await a.SnapshotAsync()).Operations.Single();if(original.State!=ParcelState.PlcAccepted)throw new Exception("Main not accepted");
}
var reply=new StageReply(Wire.Id(),original.Plc!.Request.Id,original.Plc.Request.PayloadHash,true,true,null,null,null,"Synthetic original query before index commit",DateTimeOffset.UtcNow);
using(var repo=new ParcelRepository(dir,c.ApplicationId)){var id=repo.BeginReceipt(original,original.Plc,"Query");ExtensionStore.AtomicWrite(Path.Combine(dir,"receipts",id+".json"),reply);}
var factory=new ChangedReplyFactory(new ParcelCapabilityFactory(launch),reply);
await using(var a=new ParcelRuntime(launch,new DirectPlcFlow(),factory)){
 await a.StartAsync(default);
 using(var repo=new ParcelRepository(dir,c.ApplicationId))Console.WriteLine("RecoveredIdentity="+(repo.OriginalReplyHash(reply.RequestId,reply.Id)??"NULL"));
 for(int i=0;i<65;i++)await a.ReconcileAsync(original.Id);
 var before=(await a.SnapshotAsync()).Operations.Single();Console.WriteLine("OriginalEvicted="+!before.Plc!.Replies.Any(r=>r.Id==reply.Id));
 await a.ReconcileAsync(original.Id);var after=(await a.SnapshotAsync()).Operations.Single();
 Console.WriteLine(Wire.Encode(new{Probe="ChangedRecoveredReplyAfterEviction",Before=before.State,After=after.State,Stage=after.Plc!.State,Attempts=after.Plc.Attempts,ReturnedSameIdWithChangedContent=factory.ReturnedChanged,DefectReproduced=after.State==ParcelState.PhysicalCompleted&&factory.ReturnedChanged}));
}
sealed class ChangedReplyFactory(ParcelCapabilityFactory inner,StageReply original):IParcelCapabilityFactory{
 public bool ReturnedChanged;public ParcelConfiguration Normalize(ParcelConfiguration c)=>inner.Normalize(c);public void Validate(ParcelConfiguration c)=>inner.Validate(c);public CapabilityRequirements Requirements(CapabilityBinding b)=>inner.Requirements(b);public ParcelPorts Create(ParcelConfiguration c){var p=inner.Create(c);return p with {Plc=new ReplyPort(p.Plc!,this,original)};}public ValueTask DisposeAsync()=>inner.DisposeAsync();
}
sealed class ReplyPort(IPlcPort inner,ChangedReplyFactory owner,StageReply original):IPlcPort{
 int n;public string InstanceId=>inner.InstanceId;public Task StartAsync(CancellationToken t)=>inner.StartAsync(t);public Task<CapabilityHealth> HealthAsync(CancellationToken t)=>inner.HealthAsync(t);public Task ReconnectAsync(CancellationToken t)=>inner.ReconnectAsync(t);public Task<StageReply> SubmitAsync(FrozenRequest r,CancellationToken t)=>inner.SubmitAsync(r,t);
 public Task<StageReply?> QueryAsync(FrozenRequest r,CancellationToken t){n++;if(n>65){owner.ReturnedChanged=true;return Task.FromResult<StageReply?>(original with {BusinessAccepted=true,PhysicalCompleted=true,Evidence="Synthetic changed content under old reply identity"});}return Task.FromResult<StageReply?>(original with {Id=Wire.Id(),At=DateTimeOffset.UtcNow,Evidence="Synthetic intermediate original query"});}
 public Task StopAsync(CancellationToken t)=>inner.StopAsync(t);public ValueTask DisposeAsync()=>inner.DisposeAsync();
}
