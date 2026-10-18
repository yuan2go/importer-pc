using Ipc.Platform;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
var root=Path.Combine(AppContext.BaseDirectory,"data");Directory.CreateDirectory(root);
var seed=Wire.Decode<ParcelConfiguration>(File.ReadAllText(args[0]));
using(var db=new ParcelRepository(root,seed.ApplicationId,4)){}
var database=Path.Combine(root,seed.ApplicationId+".db");
var file=Path.Combine(root,"configuration.json");
var package=new PackageReference("quality.offline","1.0.0",new string('A',64));
foreach(var item in new[]{("unknownPending",seed with {SchemaVersion=999}), ("danglingBinding",seed with {Wcs=seed.Wcs! with {Package=package,Module="parcel-wcs",Implementation="extension:"+package.Id+"/parcel-wcs"}})}) {
 ExtensionStore.AtomicWrite(file,new ConfigurationRevision<ParcelConfiguration>(1,seed,item.Item2,"PendingRestart",null));
 var beforeDb=Wire.FileHash(database);var beforeConfig=Wire.FileHash(file);
 try {var refs=ParcelRepository.InspectRequiredPackages(database);Console.WriteLine(Wire.Encode(new {Case=item.Item1,Status="ACCEPTED",ReturnedReferences=refs,BytesUnchanged=beforeDb==Wire.FileHash(database)&&beforeConfig==Wire.FileHash(file)}));}
 catch(Exception ex){Console.WriteLine(Wire.Encode(new {Case=item.Item1,Status="REJECTED",Error=ex.Message}));}
}
