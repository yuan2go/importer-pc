using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using Microsoft.Data.Sqlite;
using System.Text.Json;
var root=Path.GetFullPath(args[0]);var source=Path.GetFullPath(args[1]);var hashes=Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories).ToDictionary(p=>p,Wire.FileHash);
List<object> results=[];
foreach(var variant in new[]{"missing-query-raw","changed-query-hash","orphan-receipt-owner"})
{
 var target=Path.Combine(root,variant,"data","Simulation","reference-17","sportmaster");Directory.CreateDirectory(target);
 foreach(var file in hashes.Keys){var to=Path.Combine(target,Path.GetRelativePath(source,file));Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(file,to);}
 var database=Path.Combine(target,"sportmaster.db");string occurrence,query;
 using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=database,Pooling=false}.ToString()))
 {
  db.Open();using var q=db.CreateCommand();q.CommandText="SELECT r.id,json_extract(a.detail,'$.QueryOccurrence') FROM parcel_receipts r JOIN parcel_audit a ON a.event='ReceiptOutcomeReconciled' AND json_valid(a.detail) AND json_extract(a.detail,'$.OriginalOccurrence')=r.id WHERE r.state='OutcomeReconciled' LIMIT 1";
  using(var reader=q.ExecuteReader()){if(!reader.Read())throw new Exception("Actual historical outcome/query fixture absent");occurrence=reader.GetString(0);query=reader.GetString(1);}
  q.Parameters.AddWithValue("$id",query);
  if(variant=="missing-query-raw") {var file=Path.Combine(target,"receipts",query+".json");if(File.Exists(file))File.Delete(file);q.CommandText="DELETE FROM parcel_archived_receipts WHERE id=$id";q.ExecuteNonQuery();}
  if(variant=="changed-query-hash") {q.CommandText="UPDATE parcel_receipts SET raw_hash=$hash WHERE id=$id";q.Parameters.AddWithValue("$hash",new string('A',64));q.ExecuteNonQuery();}
  if(variant=="orphan-receipt-owner") {q.CommandText="INSERT INTO parcel_receipts SELECT $new,'synthetic-missing-owner',stage,request,'Submit','Awaiting',NULL FROM parcel_receipts WHERE id=$id";q.Parameters.AddWithValue("$new",Wire.Id());q.ExecuteNonQuery();}
 }
 var config=File.ReadAllBytes(Path.Combine(target,"configuration.json"));var calls=0;string? failure=null;
 try{await ParcelLegacyReceiptRecovery.RecoverAsync(database,Wire.FileHash(database),Path.Combine(root,variant+".backup.db"),occurrence,root,"Independent negative prior custody evidence check",new SportmasterFlow(),(_,_,_,_)=>{calls++;throw new Exception("Unexpected external query");});}
 catch(InvalidDataException ex){failure=ex.Message;}
 var version=ParcelRepository.ReadStorageVersion(database,"sportmaster");using var repository=new ParcelRepository(target,"sportmaster",4);var original=repository.ReceiptById(occurrence);
 var passed=failure is not null&&version==4&&calls==0&&config.SequenceEqual(File.ReadAllBytes(Path.Combine(target,"configuration.json")))&&original is{State:"OutcomeReconciled",RawHash:null};
 results.Add(new{variant,passed,version,externalQueryCalls=calls,failure,original});
 if(!passed)throw new Exception("Negative proof failed: "+variant+" "+failure);
}
if(!hashes.All(p=>File.Exists(p.Key)&&Wire.FileHash(p.Key)==p.Value))throw new Exception("Original fixture changed");
Console.WriteLine(JsonSerializer.Serialize(new{Head="f068156ed9d4a0ffe4feee94b5c11e966aaa366b",SourceFixtureUnchanged=true,results},new JsonSerializerOptions{WriteIndented=true}));
