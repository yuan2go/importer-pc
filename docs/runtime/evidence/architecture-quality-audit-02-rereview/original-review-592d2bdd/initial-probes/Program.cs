using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
var root=Path.Combine(AppContext.BaseDirectory,"synthetic");Directory.CreateDirectory(root);
var c=Wire.Decode<ParcelConfiguration>(File.ReadAllText("/Users/yuan/.codex/worktrees/architecture-quality-02/importer-pc/config/sportmaster/business.json"));
async Task<(ParcelRuntime, string)> Open(string name, ParcelConfiguration config, bool legacy=false){
 var path=Path.Combine(root,name);Directory.CreateDirectory(path);ExtensionStore.AtomicWrite(Path.Combine(path,"business.json"),config);
 var launch=new BusinessLaunch(path,Path.Combine(path,"data"),null,null);
 var dir=Path.Combine(launch.DataRoot,config.Mode.ToString(),config.StationId,config.ApplicationId);
 if(legacy){Directory.CreateDirectory(dir);ExtensionStore.AtomicWrite(Path.Combine(dir,"configuration.json"),new ConfigurationRevision<ParcelConfiguration>(7,config with {TimeoutMs=1700},null,"Effective",null));}
 var a=new ParcelRuntime(launch,new SportmasterFlow(),new ParcelCapabilityFactory(launch));await a.StartAsync(default);return(a,dir);
}
var (legacy,legacyDir)=await Open("legacy-effective",c,true);
var snapshot=await legacy.SnapshotAsync();
Console.WriteLine(Wire.Encode(new{Probe="LegacyEffectiveNoConfirmation",snapshot.Configuration.Status,TimeoutMs=snapshot.Configuration.Effective.TimeoutMs,snapshot.Configuration.Activation,snapshot.Configuration.Failure,Admit=snapshot.Availability!.Admit,DefectReproduced=snapshot.Configuration.Status=="Effective"&&snapshot.Configuration.Activation==null&&snapshot.Availability.Admit}));
await legacy.DisposeAsync();
var (app,dir)=await Open("raw-identity",c);
app.EmitReferenceAt("left","review-original","001","review-source");ParcelOperation? op=null;
for(var i=0;i<200;i++){op=(await app.SnapshotAsync()).Operations.FirstOrDefault();if(op?.State==ParcelState.SlotResolved)break;await Task.Delay(20);}
if(op?.Wcs?.State!=StageState.Accepted)throw new Exception("Reference main did not finish");
await app.DisposeAsync();
using(var repo=new ParcelRepository(dir,c.ApplicationId)){
 var reply=new StageReply(Wire.Id(),op.Wcs.Request.Id,op.Wcs.Request.PayloadHash,true,true,null,null,null,"Synthetic received query before raw-index transaction",DateTimeOffset.UtcNow);
 var occurrence=repo.BeginReceipt(op,op.Wcs,"Query");ExtensionStore.AtomicWrite(Path.Combine(dir,"receipts",occurrence+".json"),reply);
 repo.RecoverRaw(occurrence,reply);repo.Commit(op,"ReviewRecovered","Synthetic original raw recovery",occurrence:occurrence);
 var indexed=repo.OriginalReplyHash(reply.RequestId,reply.Id);
 Console.WriteLine(Wire.Encode(new{Probe="RecoveredRawPermanentReplyIdentity",Occurrence=repo.ReceiptById(occurrence),Expected=Wire.Hash(Wire.Encode(reply)),Actual=indexed,DefectReproduced=indexed==null}));
}
Console.WriteLine("REVIEW_ROOT="+root);
