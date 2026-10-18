using Ipc.Platform;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Assembly;
var root=Path.Combine(AppContext.BaseDirectory,"cases"); Directory.CreateDirectory(root);
var c=Wire.Decode<ParcelConfiguration>(File.ReadAllText(args[0]));
var pkg=new PackageReference("dangling.example","1.0.0",new string('A',64));
void Test(string name, ParcelConfiguration candidate, bool effective=false) {
var dir=Path.Combine(root,name);Directory.CreateDirectory(dir);using(var repo=new ParcelRepository(dir,"direct-plc")){}
var file=Path.Combine(dir,"configuration.json");ExtensionStore.AtomicWrite(file,new ConfigurationRevision<ParcelConfiguration>(0,effective?candidate:c,effective?null:candidate,effective?"Effective":"PendingRestart",null));
var db=Path.Combine(dir,"direct-plc.db");var before=Wire.FileHash(db);var configBefore=Wire.FileHash(file);
try {var r=ExtensionConsumerReaders.Inspect(db);Console.WriteLine(name+" ACCEPTED references="+r.Length+" readonly="+(Wire.FileHash(db)==before&&Wire.FileHash(file)==configBefore)+" db_before="+before+" db_after="+Wire.FileHash(db)+" config_before="+configBefore+" config_after="+Wire.FileHash(file));}
catch(Exception e){Console.WriteLine(name+" REJECTED "+e.GetType().Name+": "+e.Message);}}
Test("future-schema",c with {SchemaVersion=999});
Test("dangling-direct-binding",c with {Plc=c.Plc! with {Package=pkg,Module="main",Implementation="extension:"+pkg.Id+"/main"}});
var legacy=c with {SchemaVersion=2,Topology=null};
Test("duplicate-schema2-inventory",legacy with {CapabilityInstances=[c.Plc!,c.MaintenanceLogin!,c.Plc! with {Package=pkg,Module="main",Implementation="extension:"+pkg.Id+"/main"}]});
Test("duplicate-schema2-effective",legacy with {CapabilityInstances=[c.Plc!,c.MaintenanceLogin!,c.Plc! with {Package=pkg,Module="main",Implementation="extension:"+pkg.Id+"/main"}]},true);
Test("duplicate-schema3-inventory",c with {CapabilityInstances=[c.Plc!,c.MaintenanceLogin!,c.Plc! with {Package=pkg,Module="main",Implementation="extension:"+pkg.Id+"/main"}]});
Test("unknown-inventory-kind",c with {CapabilityInstances=[c.Plc!,c.MaintenanceLogin!,c.Plc! with {InstanceId="future",Kind="future-kind"}]});
Test("valid-empty",c);

var sd=Path.Combine(root,"supply");Directory.CreateDirectory(sd);var sdb=Path.Combine(sd,"station.db");
var sc=Wire.Decode<StationConfig>(File.ReadAllText(args[1]));
using(var ss=new Ipc.Supply.SupplyStore(sdb)){ss.Commit(ss.Load(),"ProbeSynthetic","deployment","synthetic",config:new(Wire.Id(),"seed",sc,"synthetic","Effective","",DateTimeOffset.UtcNow));}
var sh=Wire.FileHash(sdb);Console.WriteLine("supply-dispatch references="+ExtensionConsumerReaders.Inspect(sdb).Length+" readonly="+(Wire.FileHash(sdb)==sh));

Test("duplicate-schema1-inventory",legacy with {SchemaVersion=1,WeightValidity=null,CapabilityInstances=[c.Plc!,c.MaintenanceLogin!,c.Plc! with {Package=pkg,Module="main",Implementation="extension:"+pkg.Id+"/main"}]});
var fd=Path.Combine(root,"duplicate-frozen-schema2");Directory.CreateDirectory(fd);
var fc=legacy with {CapabilityInstances=[c.Plc!,c.MaintenanceLogin!,c.Plc! with {Package=pkg,Module="main",Implementation="extension:"+pkg.Id+"/main"}]};
var fop=new ParcelOperation("frozen-op","frozen-admission","001","frozen-observation",DateTimeOffset.UtcNow,Wire.Hash(Wire.Encode(fc)),fc,new(),fc.Weight,null,ParcelState.Prepared,null,null,null,null);
using(var fr=new ParcelRepository(fd,"direct-plc")){fr.Commit(fop,"ProbeFrozen","synthetic");}
var fcfg=Path.Combine(fd,"configuration.json");ExtensionStore.AtomicWrite(fcfg,new ConfigurationRevision<ParcelConfiguration>(0,c,null,"Effective",null));
var fdb=Path.Combine(fd,"direct-plc.db");var fh=Wire.FileHash(fdb);var fch=Wire.FileHash(fcfg);
try{var refs=ExtensionConsumerReaders.Inspect(fdb);Console.WriteLine("duplicate-frozen-schema2 ACCEPTED references="+refs.Length+" db_before="+fh+" db_after="+Wire.FileHash(fdb)+" config_before="+fch+" config_after="+Wire.FileHash(fcfg));}catch(Exception e){Console.WriteLine("duplicate-frozen-schema2 REJECTED "+e.Message);}
