using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static partial class ArchitectureQuality
{
    static string UpgradeDatabase(string name, ParcelConfiguration c) => Path.Combine(DirectoryFor(name,c), c.ApplicationId+".db");
    static void Sql(string file, string sql)
    { using var db=new SqliteConnection("Data Source="+file); db.Open(); using var q=db.CreateCommand(); q.CommandText=sql; q.ExecuteNonQuery(); }
    // Explicit synthetic format fixture. Source facts are produced by the real current
    // Application; historical-release fixtures are verified separately, never relabelled.
    static void SyntheticDb4(string file) => Sql(file,"DROP TABLE parcel_receipt_custody; DROP TABLE parcel_archive_versions; DELETE FROM platform_migrations WHERE version=5");
    static Dictionary<string,string> CustodyFiles(string name,ParcelConfiguration c) => Directory.EnumerateFiles(DirectoryFor(name,c),"*",SearchOption.AllDirectories)
        .Where(p=>!p.EndsWith("-wal")&&!p.EndsWith("-shm")&&!p.Contains("diagnostics")&&!p.EndsWith(".lock"))
        .ToDictionary(p=>p,Wire.FileHash);
    static async Task RefuseDb4(string name,ParcelConfiguration c)
    {
        var file=UpgradeDatabase(name,c); SyntheticDb4(file); var files=CustodyFiles(name,c);
        string? refusal=null;
        try { await using var unexpected=await Open(c,name); }
        catch(InvalidDataException ex) { refusal=ex.Message; }
        Console.WriteLine("DB4 preflight: "+Wire.Encode(new {name,refusal,Version=ParcelRepository.ReadStorageVersion(file,c.ApplicationId),Changed=files.Where(p=>!File.Exists(p.Key)||Wire.FileHash(p.Key)!=p.Value).Select(p=>Path.GetRelativePath(DirectoryFor(name,c),p.Key)).ToArray()}));
        Check(refusal?.Contains("LegacyUnsettled")==true && ParcelRepository.ReadStorageVersion(file,c.ApplicationId)==4,
            name+": unresolved DB4 refuses BEFORE future reader marker, not after provider construction");
        Check(files.All(p=>File.Exists(p.Key)&&Wire.FileHash(p.Key)==p.Value),name+": refusal preserves original DB/config/raw/image bytes");
        try { using var old=new ParcelRepository(DirectoryFor(name,c),c.ApplicationId,4); Check(old.StorageVersion==4,name+": matching DB4 reader remains usable"); }
        catch(InvalidDataException ex) { Check(false,name+": old reader stranded: "+ex.Message); }
    }
    static async Task UpgradePreflightChecks()
    {
        foreach(var kind in new[]{"wcs-unknown","plc-accepted","plc-unknown","attached-unknown"})
        {
            var c=kind.StartsWith("plc")?Config("direct-plc"):kind=="attached-unknown"?InterruptedConfig():Config("sportmaster");
            if(kind=="wcs-unknown") c=c with{Wcs=c.Wcs! with{Parameters=new(c.Wcs.Parameters){["reply-mode"]="lose-after-commit"}}};
            if(kind=="plc-unknown") c=c with{Plc=c.Plc! with{Parameters=new(c.Plc.Parameters){["reply-mode"]="lose-after-commit"}}};
            if(kind=="attached-unknown") { var upload=c.Images!.Uploader! with{Parameters=new(c.Images!.Uploader!.Parameters){["reply-mode"]="lose-after-commit"}};c=c with{Images=c.Images with{Uploader=upload},CapabilityInstances=c.CapabilityInstances!.Select(b=>b.InstanceId==upload.InstanceId?upload:b).ToArray()}; }
            var name="db4-"+kind;
            await using(var app=await Open(c,name))
            {
                app.EmitReference("upgrade-"+kind,"00081","upgrade-source-"+kind);
                await Wait(app,s=>s.Operations.FirstOrDefault() is {} op && (kind switch{"wcs-unknown"=>op.State==ParcelState.WcsUnknown,"plc-unknown"=>op.State==ParcelState.PlcUnknown,"plc-accepted"=>op.State==ParcelState.PlcAccepted,_=>op.Upload?.State==StageState.Unknown}));
            }
            await RefuseDb4(name,c);
        }
        var settled=Config("sportmaster");var terminal="db4-terminal";
        await using(var app=await Open(settled,terminal)) {app.EmitReference("settled-upgrade","00082","settled-upgrade-source");await Wait(app,s=>s.Operations.FirstOrDefault()?.State==ParcelState.SlotResolved);}
        var database=UpgradeDatabase(terminal,settled);SyntheticDb4(database);
        using(var repo=new ParcelRepository(DirectoryFor(terminal,settled),settled.ApplicationId))
            Check(repo.StorageVersion==5&&repo.Load().Single().Wcs!.Attempts==1,"fully settled DB4 upgrades without changing original send attempts");
    }
    static async Task LegacyActualPositionChecks(int schema,bool previouslyRecovered)
    {
        var c=Config("sportmaster") with{SchemaVersion=schema,Topology=null}; if(schema==1)c=c with{WeightValidity=null};
        var name=$"schema{schema}-legacy-"+previouslyRecovered;
        await using(var app=await Open(c,name)) { }
        string? originalBody=null;string? conflictId=null;
        using(var repo=new ParcelRepository(DirectoryFor(name,c),c.ApplicationId))
        {
            repo.Commit(null,"IdentityConflict","Synthetic legacy identity conflict; original scope absent");
            if(previouslyRecovered){repo.RecoverLegacyConflicts(["left","right"]);var fact=repo.LoadConflicts().Single();originalBody=Wire.Encode(fact);conflictId=fact.Id;}
        }
        await using(var app=await Open(c,name))
        {
            var s=await app.SnapshotAsync();var fact=s.IdentityConflicts!.Single();
            var resume=await Command(app,"Resume");s=await app.SnapshotAsync();
            Check(fact.Positions.Contains("main")&&s.Positions!.Single().Id=="main"&&!s.Positions!.Single().Admit&&s.Paused&&resume.Status=="Rejected",
                name+": actual main remains blocked until explicit conflict acknowledgement");
            if(previouslyRecovered)Check(fact.Id==conflictId,"scope repair preserves original responsibility identity");
            await Maintenance(app);await Wait(app,x=>x.Availability?.Configure==true);
            var ack=await Command(app,"AcknowledgeIdentityConflict",fact.Id,"Original legacy conflict and current main position inspected");
            Check(ack.Status=="Persisted",name+": authorized original conflict acknowledgement commits");
            await Command(app,"Resume");Check((await app.SnapshotAsync()).Positions!.Single().Admit,name+": inspected main can resume");
        }
        await using(var app=await Open(c,name))Check((await app.SnapshotAsync()).IdentityConflicts!.Length==0,name+": acknowledged identity stays cleared across restart");
        if(originalBody is not null)
        {
            using var db=new SqliteConnection("Data Source="+UpgradeDatabase(name,c));db.Open();using var q=db.CreateCommand();q.CommandText="SELECT detail FROM parcel_audit WHERE event='LegacyIdentityConflictScopeExpanded'";
            var detail=q.ExecuteScalar() as string;
            using var audit=System.Text.Json.JsonDocument.Parse(detail??"{}");
            Check(audit.RootElement.TryGetProperty("OriginalBody",out var original)&&original.GetString()==originalBody,name+": exact old incorrect scope body retained in append-only audit");
        }
    }
    static async Task LegacyPendingTopologyChecks()
    {
        var c=Config("sportmaster") with{SchemaVersion=2,Topology=null};var name="legacy-pending-topology";var candidate=Config("sportmaster");
        await using(var app=await Open(c,name))
        { await Maintenance(app);await Wait(app,s=>s.Availability?.Configure==true);var s=await app.SnapshotAsync();Check((await app.StageConfigurationAsync(s.Configuration.Revision,candidate,"Controlled main to independent positions transition")).Status=="PendingRestart","schema2 to schema3 candidate saved before legacy conflict appears"); }
        using(var repo=new ParcelRepository(DirectoryFor(name,c),c.ApplicationId))repo.Commit(null,"IdentityConflict","Unscoped old conflict must not disappear on topology activation");
        await using(var app=await Open(c,name))
        {
            var s=await app.SnapshotAsync();Check(s.Configuration.Effective.SchemaVersion==2&&s.Configuration.Pending?.SchemaVersion==3&&s.Configuration.Status=="ActivationFailed"&&s.Positions!.Single().Id=="main"&&!s.Positions!.Single().Admit,"unacknowledged legacy conflict blocks Pending topology activation and retains actual main");
            await Maintenance(app);await Wait(app,x=>x.Availability?.Configure==true);await Command(app,"AcknowledgeIdentityConflict",s.IdentityConflicts!.Single().Id,"Main position and legacy original inspected before topology transition");
            s=await app.SnapshotAsync();Check((await app.StageConfigurationAsync(s.Configuration.Revision,candidate,"Restage only after original conflict acknowledgement")).Status=="PendingRestart","topology transition requires explicit restage after acknowledgement");
        }
        await using(var app=await Open(c,name)){var s=await app.SnapshotAsync();Check(s.Configuration.Effective.SchemaVersion==3&&s.Positions!.Select(p=>p.Id).Order().SequenceEqual(new[]{"left","right"}),"acknowledged conflict permits controlled schema3 activation on later startup");}
    }
    public static async Task UpgradeConflictClosureChecks(string source,string evidence)
    {
        workspace=source;root=evidence;failures=0;
        await UpgradePreflightChecks();
        foreach(var schema in new[]{1,2})foreach(var recovered in new[]{false,true})await LegacyActualPositionChecks(schema,recovered);
        await LegacyPendingTopologyChecks();
        if(failures!=0)throw new Exception("Upgrade/conflict closure failures: "+failures);
        Console.WriteLine("PASS UPGRADE CONFLICT CLOSURE COMPLETE");
    }
}
