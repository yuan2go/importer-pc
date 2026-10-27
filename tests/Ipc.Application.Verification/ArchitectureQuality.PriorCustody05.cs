using Ipc.Application.ParcelFlow;
using Ipc.Platform;
static partial class ArchitectureQuality
{
    public static async Task PriorCustodyChecks(string source,string evidence,string priorDirectory)
    {
        workspace=source;root=evidence;failures=0;
        var target=Path.Combine(root,"unsupported-actual-db4");Directory.CreateDirectory(target);
        var originals=Directory.EnumerateFiles(priorDirectory,"*",SearchOption.AllDirectories).ToDictionary(p=>p,Wire.FileHash);
        foreach(var original in originals.Keys)
        {var copy=Path.Combine(target,Path.GetRelativePath(priorDirectory,original));Directory.CreateDirectory(Path.GetDirectoryName(copy)!);File.Copy(original,copy);}
        var database=Path.Combine(target,"sportmaster.db");
        Check(ParcelRepository.ReadStorageVersion(database,"sportmaster")==4,"actual prior fixture retains DB4 checksum identity");
        var hash=Wire.FileHash(database);
        try{using var denied=new ParcelRepository(target,"sportmaster");Check(false,"old cleared-custody storage cannot open");}
        catch(InvalidDataException){Check(Wire.FileHash(database)==hash,"old falsely-cleared custody is preserved without a new reader marker");}
        await RefuseHistoricalReceipt(database,Path.Combine(root,"must-not-create.db"),root,"unsupported-original");
        Check(originals.All(p=>Wire.FileHash(p.Key)==p.Value),"prior custody source evidence remains unchanged");
        if(failures!=0)throw new Exception("Prior custody refusal failures: "+failures);
        Console.WriteLine("PASS PRIOR CUSTODY REFUSAL COMPLETE");
    }
}
