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
        try { using var refused=new ParcelRepository(target,"sportmaster"); Check(false,"ordinary DB4 startup cannot skip prior missing raw custody proof"); }
        catch(InvalidDataException ex){Check(ex.Message.Contains("LegacyUnsettled")&&ParcelRepository.ReadStorageVersion(Path.Combine(target,"sportmaster.db"),"sportmaster")==4,"prior cleared custody refuses ordinary upgrade before SQL5");}
        // Use the actual offline maintenance entry with a complete immutable backup.
        // Arrange the original deployment path; the archived owner remains original.
        var oldConfig=Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(Path.Combine(target,"configuration.json"))).Effective;
        var maintenanceDirectory=DirectoryFor("prior-offline",oldConfig);Directory.CreateDirectory(maintenanceDirectory);
        foreach(var file in Directory.EnumerateFiles(target,"*",SearchOption.AllDirectories)){var dest=Path.Combine(maintenanceDirectory,Path.GetRelativePath(target,file));Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.Copy(file,dest);}
        target=maintenanceDirectory;var originalDb=Path.Combine(target,"sportmaster.db");var queryCalls=0;
        var transition=await ParcelLegacyReceiptRecovery.RecoverAsync(originalDb,Wire.FileHash(originalDb),Path.Combine(root,"prior-custody.backup.db"),occurrence,root,
            "Verify historical Query and preserve prior false raw disposition",new Ipc.Application.Sportmaster.SportmasterFlow(),
            (_,_,_,_)=>{queryCalls++;throw new Exception("Prior proved outcome must not call another Query");},expectedWalHash:WalHash(originalDb),targetStorageVersion:6);
        Check(queryCalls==0&&File.Exists(Path.Combine(root,"prior-custody.backup.db.recovery/COMPLETED.json")),"prior OutcomeReconciled uses complete backup and existing original Query without any external call");
        using(var auditDb=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+originalDb))
        {
            auditDb.Open();using var command=auditDb.CreateCommand();command.CommandText="SELECT detail FROM parcel_audit WHERE event='LegacyPriorCustodyUpgradePrepared'";
            using var detail=System.Text.Json.JsonDocument.Parse((string)command.ExecuteScalar()!);var entry=detail.RootElement;
            Check(entry.GetProperty("OriginalOccurrence").GetString()==occurrence && entry.GetProperty("Reason").GetString()=="Verify historical Query and preserve prior false raw disposition" &&
                entry.GetProperty("Actor").GetString()==Environment.UserName && entry.GetProperty("Backup").GetString()==Path.Combine(root,"prior-custody.backup.db"),
                "prior custody migration retains maintenance actor/reason/original occurrence and immutable backup identity");
        }
        files=Directory.EnumerateFiles(Path.Combine(target,"receipts"),"*.json").ToDictionary(p=>p,Wire.FileHash);
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
