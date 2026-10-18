using System.Reflection;
using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Assembly;
using Ipc.Platform;
var source = "/Users/yuan/.codex/artifacts/ipc-pr26-independent-review-78298b5-20261019/source";
var build = "/Users/yuan/.codex/artifacts/ipc-pr26-independent-review-78298b5-20261019/build/bin/Ipc.Application.Verification/debug";
var root = "/Users/yuan/.codex/artifacts/ipc-pr26-aq-diagnosis-20261019/runtime";
Directory.CreateDirectory(root);
var aq=typeof(ParcelCapabilityFactory).Assembly.GetType("ArchitectureQuality")!;
const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
aq.GetField("workspace",flags)!.SetValue(null,source); aq.GetField("root",flags)!.SetValue(null,root);
var c=(ParcelConfiguration)aq.GetMethod("InterruptedConfig",flags)!.Invoke(null,null)!;
var path=Path.Combine(root,"held-health");
var psi=new ProcessStartInfo("/Users/yuan/.local/share/ipc-dotnet/dotnet") {RedirectStandardOutput=true,RedirectStandardError=true};
foreach(var arg in new[]{Path.Combine(build,"Ipc.Application.Verification.dll"),source,"--upload-crash-child",path}) psi.ArgumentList.Add(arg);
using(var child=Process.Start(psi)!) {var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));Console.Write(await stdout);Console.Write(await stderr);if(child.ExitCode!=74)throw new Exception("Wrong crash status:"+child.ExitCode);}
HeldFactory? factory=null;
Func<ParcelCapabilityFactory,IParcelCapabilityFactory> wrap=f=>factory=new HeldFactory(f);
var open=(Task<ParcelRuntime>)aq.GetMethod("Open",flags)!.Invoke(null,new object?[]{c,"held-health",wrap})!;
await using var app=await open;
factory!.Hold=true;
await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
async Task<BusinessReceipt> Command(string kind,string? owner=null,string? value=null){for(int i=0;i<8;i++){var s=await app.SnapshotAsync();var r=await app.CommandAsync(new(Wire.Id(),s.Revision,kind,owner,value));if(r.Status!="Conflict")return r;}throw new Exception("CAS repeated");}
Console.WriteLine("Pause="+Wire.Encode(await Command("Pause")));
Console.WriteLine("Authorize="+Wire.Encode(await app.AuthorizeMaintenanceAsync("maintenance","SIMULATION_ONLY")));
var op=(await app.SnapshotAsync()).Operations.Single();
var rows=Wire.Decode<ParcelReceiptResponsibility[]>((await Command("ReceiptResponsibilities",op.Id)).Reason);
var occurrence=rows.Single(r=>r.State=="Awaiting").Id;
Console.WriteLine("DuringHeldHealth="+Wire.Encode(new{Snapshot=(await app.SnapshotAsync()).Availability,ExternalWaits=typeof(ParcelRuntime).GetField("externalWaits",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(app)}));
var blocked=await Command("ReconcileReceipt",occurrence,"Held health must preserve production maintenance fence");
Console.WriteLine("HeldHealthReceipt="+Wire.Encode(blocked)); Console.WriteLine("HeldHealthQueryCalls="+factory.QueryCalls);
if(blocked.Status!="Rejected"||factory.QueryCalls!=0)throw new Exception("Maintenance fence failed");
factory.Release.TrySetResult();
for(int i=0;i<200;i++){if((await app.SnapshotAsync()).Availability?.Configure==true)break;await Task.Delay(20);}
var result=await Command("ReconcileReceipt",occurrence,"No authoritative upload outcome; keep original");
var after=await app.SnapshotAsync();
Console.WriteLine("DrainedReceipt="+Wire.Encode(result));Console.WriteLine("DrainedFacts="+Wire.Encode(new{factory.QueryCalls,ImageReleased=after.Operations.Single().ImageReleased,Receipts=Wire.Decode<ParcelReceiptResponsibility[]>((await Command("ReceiptResponsibilities",op.Id)).Reason)}));
if(result.Status!="Unresolved"||factory.QueryCalls!=1||after.Operations.Single().ImageReleased)throw new Exception("Original assertion failed after drain");
Console.WriteLine("PASS AQ DIAGNOSIS: health in-flight rejects without query; explicit post-drain query is Unresolved with original raw/image retained");
sealed class HeldFactory(ParcelCapabilityFactory inner):IParcelCapabilityFactory {
 public bool Hold; public int QueryCalls; public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public ParcelConfiguration Normalize(ParcelConfiguration c)=>inner.Normalize(c);public void Validate(ParcelConfiguration c)=>inner.Validate(c);public CapabilityRequirements Requirements(CapabilityBinding b)=>inner.Requirements(b);
 public ParcelPorts Create(ParcelConfiguration c){var p=inner.Create(c);return p with{Wcs=new HeldWcs(p.Wcs!,this),Uploader=new EmptyUpload(p.Uploader!,this)};} public ValueTask DisposeAsync()=>inner.DisposeAsync();
 sealed class HeldWcs(IWcsPort inner,HeldFactory f):IWcsPort { public string InstanceId=>inner.InstanceId;public Task StartAsync(CancellationToken t)=>inner.StartAsync(t);public async Task<CapabilityHealth> HealthAsync(CancellationToken t){if(f.Hold){f.Entered.TrySetResult();await f.Release.Task.WaitAsync(t);}return await inner.HealthAsync(t);}public Task ReconnectAsync(CancellationToken t)=>inner.ReconnectAsync(t);public Task<StageReply> ResolveSlotAsync(FrozenRequest r,CancellationToken t)=>inner.ResolveSlotAsync(r,t);public Task<StageReply?> QueryAsync(FrozenRequest r,CancellationToken t)=>inner.QueryAsync(r,t);public Task StopAsync(CancellationToken t)=>inner.StopAsync(t);public ValueTask DisposeAsync()=>inner.DisposeAsync();}
 sealed class EmptyUpload(IImageUploader inner,HeldFactory f):IImageUploader {public string InstanceId=>inner.InstanceId;public Task StartAsync(CancellationToken t)=>inner.StartAsync(t);public Task<CapabilityHealth> HealthAsync(CancellationToken t)=>inner.HealthAsync(t);public Task ReconnectAsync(CancellationToken t)=>inner.ReconnectAsync(t);public Task<StageReply> UploadAsync(FrozenRequest r,ReadOnlyMemory<byte>b,CancellationToken t)=>inner.UploadAsync(r,b,t);public Task<StageReply?> QueryAsync(FrozenRequest r,CancellationToken t){Interlocked.Increment(ref f.QueryCalls);return Task.FromResult<StageReply?>(null);}public Task StopAsync(CancellationToken t)=>inner.StopAsync(t);public ValueTask DisposeAsync()=>inner.DisposeAsync();}
}
