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
    static void SyntheticDb4(string file) => Sql(file,"DROP TABLE IF EXISTS parcel_integration_id_owners; DROP TABLE IF EXISTS parcel_integration_id_ranges; DROP TABLE IF EXISTS parcel_integration_id_restores; DROP TABLE IF EXISTS parcel_integration_semantics; DROP TABLE parcel_receipt_custody; DROP TABLE parcel_archive_versions; DELETE FROM platform_migrations WHERE version>=5");
    static void SyntheticDb5(string file) => Sql(file,"DROP TABLE IF EXISTS parcel_integration_id_owners; DROP TABLE IF EXISTS parcel_integration_id_ranges; DROP TABLE IF EXISTS parcel_integration_id_restores; DROP TABLE IF EXISTS parcel_integration_semantics; DELETE FROM platform_migrations WHERE version>=6");
    static Dictionary<string,string> CustodyFiles(string name,ParcelConfiguration c) => Directory.EnumerateFiles(DirectoryFor(name,c),"*",SearchOption.AllDirectories)
        .Where(p=>!p.EndsWith("-wal")&&!p.EndsWith("-shm")&&!p.Contains("diagnostics")&&!p.EndsWith(".lock") &&
            // ResourceLease rewrites only this root control file with the current PID;
            // it is not original business custody. Nested image/raw files remain checked.
            !(Path.GetDirectoryName(p)==DirectoryFor(name,c)&&p.EndsWith(".lease",StringComparison.Ordinal)))
        .ToDictionary(p=>p,Wire.FileHash);
    static async Task RefuseDb4(string name,ParcelConfiguration c,bool historical=false,int storageVersion=4)
    {
        var file=UpgradeDatabase(name,c); if(!historical) { if(storageVersion==4)SyntheticDb4(file); else SyntheticDb5(file); } var files=CustodyFiles(name,c);
        string? refusal=null;
        try { await using var unexpected=await Open(c,name); }
        catch(InvalidDataException ex) { refusal=ex.Message; }
        Console.WriteLine($"DB{storageVersion} preflight: "+Wire.Encode(new {name,refusal,Version=ParcelRepository.ReadStorageVersion(file,c.ApplicationId),Changed=files.Where(p=>!File.Exists(p.Key)||Wire.FileHash(p.Key)!=p.Value).Select(p=>Path.GetRelativePath(DirectoryFor(name,c),p.Key)).ToArray()}));
        Check(refusal?.Contains("LegacyUnsettled")==true && ParcelRepository.ReadStorageVersion(file,c.ApplicationId)==storageVersion,
            $"{name}: unresolved DB{storageVersion} refuses BEFORE future reader marker, not after provider construction");
        Check(files.All(p=>File.Exists(p.Key)&&Wire.FileHash(p.Key)==p.Value),name+": refusal preserves original DB/config/raw/image bytes");
        try { using var old=new ParcelRepository(DirectoryFor(name,c),c.ApplicationId,storageVersion); Check(old.StorageVersion==storageVersion,name+": matching DB4 reader remains usable"); }
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
        var db5 = Config("sportmaster");
        db5 = db5 with { Wcs = db5.Wcs! with { Parameters = new(db5.Wcs.Parameters) { ["reply-mode"] = "lose-after-commit" } } };
        await using (var app = await Open(db5,"db5-unknown"))
        { app.EmitReference("db5-original","00085","db5-source"); await Wait(app,s=>s.Operations.FirstOrDefault()?.State==ParcelState.WcsUnknown); }
        await RefuseDb4("db5-unknown",db5,storageVersion:5);
        // A different frozen implementation is rejected at the same pre-migration
        // boundary. This fixture retains the real original requests and replies.
        var changed=Config("sportmaster") with { Wcs=Config("sportmaster").Wcs! with { Parameters=new(Config("sportmaster").Wcs!.Parameters){["reply-mode"]="lose-after-commit"} } };
        var changedName="db4-old-implementation";
        await using(var app=await Open(changed,changedName)){app.EmitReference("old-implementation","00083","old-implementation-source");await Wait(app,s=>s.Operations.FirstOrDefault()?.State==ParcelState.WcsUnknown);}
        using(var repo=new ParcelRepository(DirectoryFor(changedName,changed),changed.ApplicationId))
        {var op=repo.Load().Single();repo.Commit(op with { ImplementationHashes=op.ImplementationHashes.ToDictionary(x=>x.Key,_=>new string('A',64)) },"SyntheticPriorImplementation","Only frozen implementation hash differs in this synthetic fixture");}
        await RefuseDb4(changedName,changed);
        // Hold a nonempty WAL without a live Application. Refusal must not checkpoint
        // or alter the source DB/WAL, even when the unresolved source is only in WAL.
        var walName="db4-nonempty-wal";
        await using(var app=await Open(changed,walName)){app.EmitReference("wal-original","00084","wal-source");await Wait(app,s=>s.Operations.FirstOrDefault()?.State==ParcelState.WcsUnknown);}
        var walDb=UpgradeDatabase(walName,changed);SyntheticDb4(walDb);
        using(var writer=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=walDb,Pooling=false}.ToString()))
        {
            writer.Open();using var q=writer.CreateCommand();q.CommandText="PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; UPDATE parcel_meta SET revision=revision+1";q.ExecuteNonQuery();
            var dbHash=Wire.FileHash(walDb);var walHash=Wire.FileHash(walDb+"-wal");var configHash=Wire.FileHash(Path.Combine(DirectoryFor(walName,changed),"configuration.json"));
            try {await using var unexpected=await Open(changed,walName);Check(false,"nonempty WAL unresolved upgrade must refuse");}
            catch(InvalidDataException ex){Check(ex.Message.Contains("LegacyUnsettled")&&Wire.FileHash(walDb)==dbHash&&Wire.FileHash(walDb+"-wal")==walHash&&Wire.FileHash(Path.Combine(DirectoryFor(walName,changed),"configuration.json"))==configHash,"read-only preflight preserves exact nonempty DB/WAL/config originals");}
        }
        var settled=Config("sportmaster");var terminal="db4-terminal";
        await using(var app=await Open(settled,terminal)) {app.EmitReference("settled-upgrade","00082","settled-upgrade-source");await Wait(app,s=>s.Operations.FirstOrDefault()?.State==ParcelState.SlotResolved);}
        var database=UpgradeDatabase(terminal,settled);SyntheticDb4(database);
        using(var repo=new ParcelRepository(DirectoryFor(terminal,settled),settled.ApplicationId))
            Check(repo.StorageVersion==7&&repo.Load().Single().Wcs!.Attempts==1,"fully settled DB4 upgrades without changing original send attempts");
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
    public static async Task HistoricalDb4UpgradeChecks(string source,string evidence,string fixture)
    {
        workspace=source;root=evidence;failures=0;var name="historical-pr24-db4";var target=Path.Combine(root,name);Directory.CreateDirectory(target);
        var originals=Directory.EnumerateFiles(fixture,"*",SearchOption.AllDirectories).ToDictionary(p=>p,Wire.FileHash);
        foreach(var file in originals.Keys){var to=Path.Combine(target,Path.GetRelativePath(fixture,file));Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(file,to);}
        var c=Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(target,"business.json")));
        Check(ParcelRepository.ReadStorageVersion(UpgradeDatabase(name,c),c.ApplicationId)==4,"historical input retains actual PR24 DB4 migration identity");
        await RefuseDb4(name,c,historical:true);
        Check(originals.All(p=>File.Exists(p.Key)&&Wire.FileHash(p.Key)==p.Value),"historical source directory remains byte-identical; probe changed only an independent copy");
        if(failures!=0)throw new Exception("Historical DB4 upgrade failures: "+failures);Console.WriteLine("PASS HISTORICAL DB4 UPGRADE COMPLETE");
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
