using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
static partial class ArchitectureQuality
{
    public static async Task PriorCustodyChecks(string source,string evidence,string priorDirectory)
    {
        workspace=source;root=evidence;failures=0;var target=Path.Combine(root,"prior-db4");Directory.CreateDirectory(target);
        foreach(var file in Directory.EnumerateFiles(priorDirectory,"*",SearchOption.AllDirectories)){var dest=Path.Combine(target,Path.GetRelativePath(priorDirectory,file));Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.Copy(file,dest);}
        Check(ParcelRepository.ReadStorageVersion(Path.Combine(target,"sportmaster.db"),"sportmaster")==4,"prior input is actual 2ad DB4 produced by the original flawed receipt-outcome test");
        string operation,occurrence;using(var db=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+Path.Combine(target,"sportmaster.db")))
        {db.Open();using var q=db.CreateCommand();q.CommandText="SELECT operation,id FROM parcel_receipts WHERE state='OutcomeReconciled'";using var r=q.ExecuteReader();if(!r.Read())throw new Exception("Expected actual prior OutcomeReconciled");operation=r.GetString(0);occurrence=r.GetString(1);}
        var files=(Directory.Exists(Path.Combine(target,"receipts"))?Directory.EnumerateFiles(Path.Combine(target,"receipts"),"*.json"):Array.Empty<string>()).ToDictionary(p=>p,Wire.FileHash);
        ParcelConfiguration config;using(var repo=new ParcelRepository(target,"sportmaster"))
        {
            repo.ValidateRawInventory();var op=repo.ReadOperation(operation)!;config=op.FrozenConfiguration;
            Check(repo.ReceiptById(occurrence) is{State:"Awaiting",RawHash:null,QueryOccurrence:not null}&&repo.HasReceiptOutcomeBasis(occurrence),"SQL5 restores prior cleared raw responsibility and preserves its separate verified Query basis");
            Check(repo.Load().Any(o=>o.Id==operation)&&op.ImageReleased,"previously archived owner is visible again; historical already-released image fact is preserved without inventing bytes");
            Check(files.All(p=>File.Exists(p.Key)&&Wire.FileHash(p.Key)==p.Value),"custody migration preserves every remaining original raw byte");
        }
        var path=Path.Combine(root,"prior-runtime");var dir=DirectoryFor("prior-runtime",config);Directory.CreateDirectory(dir);
        foreach(var file in Directory.EnumerateFiles(target,"*",SearchOption.AllDirectories)){var dest=Path.Combine(dir,Path.GetRelativePath(target,file));Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.Copy(file,dest);}
        await using(var app=await Open(config,"prior-runtime"))
        {
            var s=await app.SnapshotAsync();Check(s.AuxiliaryReasons!.Any(x=>x.Contains(occurrence))&&!s.Positions!.Single(p=>p.Id=="left").BackgroundComplete,"prior missing raw is a current auxiliary responsibility, not falsely complete background history");
            await Maintenance(app);await Command(app,"ArchiveHistory",value:"Prior custody gap must remain active");
            Check((await app.OperationAsync(operation))!.Id==operation,"history recovery retains original identity and permanent missing occurrence");
        }
        if(failures!=0)throw new Exception("Prior custody migration failures: "+failures);Console.WriteLine("PASS PRIOR CUSTODY MIGRATION COMPLETE");
    }
}
