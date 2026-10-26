using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
static partial class ArchitectureQuality
{
    static string LegacyDatabase(string path)=>Path.Combine(path,"data","Simulation","reference-17","sportmaster","sportmaster.db");
    static string? WalHash(string database)=>File.Exists(database+"-wal")&&new FileInfo(database+"-wal").Length>0?Wire.FileHash(database+"-wal"):null;
    public static Task LegacyCutChild(string path,string release,string occurrence)=>
        throw new InvalidDataException("旧回执转换不再支持；当前回执崩溃边界由当前数据库专项验证。");
    static async Task RefuseHistoricalReceipt(string database,string backup,string release,string occurrence)
    {
        var directory=Path.GetDirectoryName(database)!;
        var originals=Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith("-shm")).ToDictionary(p=>p,Wire.FileHash);
        var calls=0;var version=ParcelRepository.ReadStorageVersion(database,"sportmaster");
        for(var attempt=0;attempt<2;attempt++)
        {
            try
            {
                await ParcelLegacyReceiptRecovery.RecoverAsync(database,Wire.FileHash(database),backup,occurrence,release,"Read-only unsupported original inspection",new SportmasterFlow(),
                    (_,_,_,_)=>{calls++;return Task.FromResult<StageReply?>(null);},expectedWalHash:WalHash(database));
                Check(false,"old recovery must refuse");
            }
            catch(InvalidDataException)
            {
                Check(calls==0 && originals.All(p=>File.Exists(p.Key)&&Wire.FileHash(p.Key)==p.Value) &&
                    ParcelRepository.ReadStorageVersion(database,"sportmaster")==version && !File.Exists(backup) && !Directory.Exists(backup+".recovery"),
                    "old receipt entry repeatedly refuses without Query, Submit, conversion, backups, invented success or original byte changes");
            }
        }
    }
    public static async Task LegacyCustodyChecks(string source,string evidence,string release,string fixture)
    {
        workspace=source;root=evidence;failures=0;
        var originals=Directory.EnumerateFiles(fixture,"*",SearchOption.AllDirectories).ToDictionary(p=>p,Wire.FileHash);
        var target=Path.Combine(root,"unsupported-actual-db3");Directory.CreateDirectory(target);
        foreach(var original in originals.Keys)
        {var copy=Path.Combine(target,Path.GetRelativePath(fixture,original));Directory.CreateDirectory(Path.GetDirectoryName(copy)!);File.Copy(original,copy);}
        var database=LegacyDatabase(target);
        Check(ParcelRepository.ReadStorageVersion(database,"sportmaster")==3,"historical fixture retains its actual DB3 identity");
        await RefuseHistoricalReceipt(database,Path.Combine(root,"must-not-create.db"),release,"unsupported-original");
        Check(originals.All(p=>Wire.FileHash(p.Key)==p.Value),"historical source fixture itself remains byte-identical");
        if(failures!=0)throw new Exception("Historical custody refusal failures: "+failures);
        Console.WriteLine("PASS HISTORICAL CUSTODY REFUSAL COMPLETE");
    }
}
