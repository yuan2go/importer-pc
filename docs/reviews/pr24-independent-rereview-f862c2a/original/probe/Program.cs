using Ipc.Platform;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Assembly;
var root=Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
var original=Wire.Decode<ParcelConfiguration>(File.ReadAllText(args[0]));
var package=new PackageReference("review.example","1.0.0",new string('A',64));
var failed=0;
void Inspect(string name,ParcelConfiguration seed,ParcelConfiguration candidate,string scope,bool expectReject)
{
 var path=Path.Combine(root,name);Directory.CreateDirectory(path);
 using(var repo=new ParcelRepository(path,seed.ApplicationId))
  if(scope=="Frozen")repo.Commit(new ParcelOperation(Wire.Id(),Wire.Id(),"001",Wire.Id(),DateTimeOffset.UtcNow,Wire.Hash(Wire.Encode(candidate)),candidate,[],candidate.Weight,WeightRules.Configured(candidate.Weight),ParcelState.Prepared,null,null,null,null),"IndependentReviewFixture","Synthetic never-sent unresolved original");
 var cfg=Path.Combine(path,"configuration.json"); ExtensionStore.AtomicWrite(cfg,new ConfigurationRevision<ParcelConfiguration>(1,scope=="Effective"?candidate:seed,scope=="Pending"?candidate:null,scope=="Pending"?"PendingRestart":"Effective",null));
 var db=Path.Combine(path,seed.ApplicationId+".db");var beforeDb=Wire.FileHash(db);var beforeConfig=Wire.FileHash(cfg);
 bool rejected=false;PackageReference[] refs=[];string detail="";
 try{refs=ExtensionConsumerReaders.Inspect(db);}catch(InvalidDataException ex){rejected=true;detail=ex.Message;}
 bool unchanged=beforeDb==Wire.FileHash(db)&&beforeConfig==Wire.FileHash(cfg); bool pass=unchanged&&rejected==expectReject;
 if(!pass)failed++;
 Console.WriteLine(Wire.Encode(new{name,pass,rejected,unchanged,references=refs.Length,dbBefore=beforeDb,dbAfter=Wire.FileHash(db),configBefore=beforeConfig,configAfter=Wire.FileHash(cfg),detail}));
}
foreach(var schema in new[]{1,2,3})
{
 var seed=original with{SchemaVersion=schema,Topology=schema==3?original.Topology:null,WeightValidity=schema==1?null:WeightValidity.UntilDispatch};var plc=seed.Plc!;
 (string,CapabilityBinding)[] differences=[("kind",plc with{Kind="upload"}),("implementation",plc with{Implementation="other"}),("version",plc with{Version="2.0.0"}),("target",plc with{Target="reference://other"}),("contract",plc with{Contract="other"}),("parameters",plc with{Parameters=new(){{"changed","true"}}}),("enabled",plc with{Enabled=false}),("package",plc with{Package=package,Module="main",Implementation="extension:review.example/main"}),("module",plc with{Module="other"})];
 foreach(var scope in new[]{"Effective","Pending","Frozen"})
 {
  foreach(var(name,binding)in differences)Inspect($"schema{schema}-{scope}-{name}",seed,seed with{CapabilityInstances=[binding,seed.MaintenanceLogin!]},scope,true);
  Inspect($"schema{schema}-{scope}-cloned-mirror",seed,seed with{CapabilityInstances=[plc with{Parameters=new(plc.Parameters)},seed.MaintenanceLogin!]},scope,false);
  Inspect($"schema{schema}-{scope}-duplicate-packages",seed,seed with{Extensions=new([package,package],[])},scope,true);
  Inspect($"schema{schema}-{scope}-null-item",seed,seed with{CapabilityInstances=[plc,seed.MaintenanceLogin!,null!]},scope,true);
  Inspect($"schema{schema}-{scope}-oversize",seed,seed with{CapabilityInstances=Enumerable.Range(0,65).Select(i=>plc with{InstanceId="extra"+i}).ToArray()},scope,true);
 }
}
Console.WriteLine("Independent extra raw field matrix failures="+failed); return failed==0?0:1;
