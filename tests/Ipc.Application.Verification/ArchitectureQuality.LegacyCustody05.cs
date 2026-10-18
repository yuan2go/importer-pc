using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
using System.Diagnostics;
static partial class ArchitectureQuality
{
    static string LegacyDatabase(string path)=>Path.Combine(path,"data","Simulation","reference-17","sportmaster","sportmaster.db");
    static string? WalHash(string database)=>File.Exists(database+"-wal")&&new FileInfo(database+"-wal").Length>0?Wire.FileHash(database+"-wal"):null;
    public static async Task LegacyCutChild(string path,string release,string occurrence)
    {
        ParcelLegacyReceiptRecovery.Boundary=p=>{if(p=="AfterLegacyRawDurable")Environment.Exit(76);};var db=LegacyDatabase(path);
        await ParcelLegacyReceiptRecovery.RecoverAsync(db,Wire.FileHash(db),Path.Combine(path,"first.backup.db"),occurrence,release,"Synthetic process interruption after original Query raw flush",new SportmasterFlow(),
            (json,config,data,t)=>OriginalParcelQuery.QueryAsync(release,json,"image-upload",Path.Combine(path,"config"),data,t),expectedWalHash:WalHash(db));
    }
    public static async Task LegacyCustodyChecks(string source,string evidence,string release,string fixture)
    {
        workspace=source;root=evidence;failures=0;
        string Copy(string name){var target=Path.Combine(root,name);Directory.CreateDirectory(target);foreach(var file in Directory.EnumerateFiles(fixture,"*",SearchOption.AllDirectories)){var to=Path.Combine(target,Path.GetRelativePath(fixture,file));Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(file,to);}return target;}
        string Original(string path){var db=LegacyDatabase(path);using var repo=new ParcelRepository(Path.GetDirectoryName(db)!,"sportmaster",3);return repo.PendingReceipts().Single(r=>r.Stage=="image-upload"&&r.Reply is null).Id;}
        var cut=Copy("legacy-query-cut");var occurrence=Original(cut);
        Check(ParcelRepository.ReadStorageVersion(LegacyDatabase(cut),"sportmaster")==3,"input is actual old-release DB3, not a downgraded current database");
        var psi=new ProcessStartInfo(Environment.ProcessPath!){RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{typeof(ArchitectureQuality).Assembly.Location,workspace,"--legacy-cut-child",cut,release,occurrence})psi.ArgumentList.Add(arg);
        using(var child=Process.Start(psi)!){var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));Console.Write(await output);Console.Write(await error);Check(child.ExitCode==76,"real maintenance child terminates after original-release Query raw-durable and before T3");}
        var calls=0;var dbFile=LegacyDatabase(cut);
        var result=await ParcelLegacyReceiptRecovery.RecoverAsync(dbFile,Wire.FileHash(dbFile),Path.Combine(cut,"retry.backup.db"),occurrence,release,"Recover exact locally durable Query before custody upgrade",new SportmasterFlow(),
            (_,_,_,_)=>{calls++;throw new Exception("A new external query must not replace the locally durable original query");},expectedWalHash:WalHash(dbFile));
        using(var repo=new ParcelRepository(Path.GetDirectoryName(dbFile)!,"sportmaster"))
        {
            var op=repo.Load().Single();
            Check(calls==0&&ParcelRepository.ReadStorageVersion(dbFile,"sportmaster")==5,"retry finishes original Query locally and upgrades without another external call");
            Check(op.State==ParcelState.SlotResolved&&op.Upload!.State==StageState.Accepted&&!op.ImageReleased&&repo.PendingReceipts().Length==1&&repo.ReceiptById(occurrence) is{State:"Awaiting",RawHash:null,QueryOccurrence:not null},"DB5 preserves original missing Submit raw, image, query basis and completed main");
            Check(op.Wcs!.Attempts==1&&op.Upload!.Attempts==1,"maintenance interruption never resubmits WCS or upload");
        }
        var missing=Copy("legacy-other-raw-missing");var missingOccurrence=Original(missing);var missingDb=LegacyDatabase(missing);string captureRaw;
        using(var repo=new ParcelRepository(Path.GetDirectoryName(missingDb)!,"sportmaster",3))captureRaw=repo.ReceiptResponsibilities(repo.Load().Single().Id).Single(x=>x.Stage=="image-capture").Id;
        File.Delete(Path.Combine(Path.GetDirectoryName(missingDb)!,"receipts",captureRaw+".json"));
        try{await ParcelLegacyReceiptRecovery.RecoverAsync(missingDb,Wire.FileHash(missingDb),Path.Combine(missing,"refused.backup.db"),missingOccurrence,release,"Missing original capture raw must retain old reader",new SportmasterFlow(),(_,_,_,_)=>throw new Exception("No query allowed"),expectedWalHash:WalHash(missingDb));Check(false,"missing committed raw must refuse migration");}
        catch(InvalidDataException){Check(ParcelRepository.ReadStorageVersion(missingDb,"sportmaster")==3,"complete raw inventory failure retains original schema before Query and migration");}
        var conflict=Copy("legacy-query-conflict");var conflictOccurrence=Original(conflict);var conflictDb=LegacyDatabase(conflict);
        var bad=await ParcelLegacyReceiptRecovery.RecoverAsync(conflictDb,Wire.FileHash(conflictDb),Path.Combine(conflict,"conflict.backup.db"),conflictOccurrence,release,"Invalid physical completion must remain a conflict",new SportmasterFlow(),
            async(json,config,data,t)=>(await OriginalParcelQuery.QueryAsync(release,json,"image-upload",Path.Combine(conflict,"config"),data,t))! with{PhysicalCompleted=true},expectedWalHash:WalHash(conflictDb));
        using(var repo=new ParcelRepository(Path.GetDirectoryName(conflictDb)!,"sportmaster",3))Check(repo.Load().Single().Upload!.State==StageState.Conflict&&ParcelRepository.ReadStorageVersion(conflictDb,"sportmaster")==3,"invalid original response commits raw and Conflict without a future reader marker");
        try{await ParcelLegacyReceiptRecovery.RecoverAsync(conflictDb,Wire.FileHash(conflictDb),Path.Combine(conflict,"no-force-clear.backup.db"),conflictOccurrence,release,"A good query cannot clear existing Conflict",new SportmasterFlow(),(_,_,_,_)=>throw new Exception("Conflict must reject before query"),expectedWalHash:WalHash(conflictDb));Check(false,"existing Conflict cannot be force-cleared");}
        catch(InvalidDataException){Check(ParcelRepository.ReadStorageVersion(conflictDb,"sportmaster")==3,"existing Conflict refuses retry before provider call and preserves original schema");}
        if(failures!=0)throw new Exception("Legacy custody failures: "+failures);Console.WriteLine("PASS LEGACY CUSTODY COMPLETE "+Wire.Encode(result));
    }
}
