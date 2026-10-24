using Ipc.Application.ParcelFlow;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.Json;

var root=Path.Combine(args[0],"migration-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root); var database=Path.Combine(root,"sportmaster.db");
var checks=new List<string>();
void Check(bool yes,string message){if(!yes)throw new Exception(message);checks.Add(message);}
string[] Ledger()
{
    using var db=new SqliteConnection($"Data Source={database};Pooling=False;Mode=ReadOnly");db.Open();
    using var c=db.CreateCommand();c.CommandText="SELECT application||'|'||version||'|'||sha FROM platform_migrations ORDER BY version";
    using var r=c.ExecuteReader();var result=new List<string>();while(r.Read())result.Add(r.GetString(0));return result.ToArray();
}
using(var repo=new ParcelRepository(root,"sportmaster",8))Check(repo.StorageVersion==8,"actual constructor creates synthetic DB8");
var before=Ledger();
using(var repo=new ParcelRepository(root,"sportmaster"))Check(repo.StorageVersion==9,"actual constructor upgrades settled DB8 to DB9");
var after=Ledger();Check(before.Length==8 && after.Length==9 && before.SequenceEqual(after.Take(8)),"SQL1-8 ledger checksums unchanged after DB9 migration");
using(var repo=new ParcelRepository(root,"sportmaster"))Check(repo.StorageVersion==9,"existing DB9 reopens without migration/checksum changes");
Check(Ledger().SequenceEqual(after),"existing DB9 migration ledger unchanged on reopen");
var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(database)));
try{using var older=new ParcelRepository(root,"sportmaster",8);throw new Exception("DB8 reader accepted DB9");}
catch(InvalidDataException){Check(hash==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(database))),"old DB8 reader refuses DB9 before changing original bytes");}
File.WriteAllText(Path.Combine(args[0],"migration-result.json"),JsonSerializer.Serialize(new{Root=root,Checks=checks,Db8Ledger=before,Db9Ledger=after},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"PASS {checks.Count} actual repository migration compatibility checks");
