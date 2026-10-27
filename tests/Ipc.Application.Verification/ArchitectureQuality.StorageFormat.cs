using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static partial class ArchitectureQuality
{
    // Only for a fixture's deliberately open, quiescent WAL writer. Production custody
    // keeps strict sharing; do not weaken its hash checks to accommodate this fixture.
    static string StableOpenFixtureHash(string path)
    { using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s)); }
    static string StorageDatabase(string name, ParcelConfiguration c) => Path.Combine(DirectoryFor(name,c), c.ApplicationId+".db");
    static void Sql(string file, string sql)
    { using var db=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=file, Pooling=false }.ToString()); db.Open(); using var q=db.CreateCommand(); q.CommandText=sql; q.ExecuteNonQuery(); }
    // Explicit synthetic format fixture. Source facts are produced by the real current
    // Application; historical-release fixtures are verified separately, never relabelled.
    static void MarkNonCurrent(string file, int version = 9) => Sql(file,"UPDATE platform_migrations SET version=" + version);
    static Dictionary<string,string> CustodyFiles(string name,ParcelConfiguration c) => Directory.EnumerateFiles(DirectoryFor(name,c),"*",SearchOption.AllDirectories)
        .Where(p=>!p.EndsWith("-wal")&&!p.EndsWith("-shm")&&!p.Contains("diagnostics")&&!p.EndsWith(".lock") &&
            // ResourceLease rewrites only this root control file with the current PID;
            // it is not original business custody. Nested image/raw files remain checked.
            !(Path.GetDirectoryName(p)==DirectoryFor(name,c)&&p.EndsWith(".lease",StringComparison.Ordinal)))
        .ToDictionary(p=>p,Wire.FileHash);
    static async Task RefuseNonCurrentStorage(string name,ParcelConfiguration c,bool historical=false,int storageVersion=4)
    {
        var file=StorageDatabase(name,c); if(!historical) { MarkNonCurrent(file,storageVersion); } var files=CustodyFiles(name,c);
        string? refusal=null;
        try { await using var unexpected=await Open(c,name); }
        catch(InvalidDataException ex) { refusal=ex.Message; }
        Console.WriteLine($"DB{storageVersion} preflight: "+Wire.Encode(new {name,refusal,Version=NonCurrentStorageFixture.Version(file),Changed=files.Where(p=>!File.Exists(p.Key)||Wire.FileHash(p.Key)!=p.Value).Select(p=>Path.GetRelativePath(DirectoryFor(name,c),p.Key)).ToArray()}));
        Check(refusal?.Contains("全新数据目录")==true && NonCurrentStorageFixture.Version(file)==storageVersion,
            $"{name}: unresolved DB{storageVersion} refuses BEFORE future reader marker, not after provider construction");
        Check(files.All(p=>File.Exists(p.Key)&&Wire.FileHash(p.Key)==p.Value),name+": refusal preserves original DB/config/raw/image bytes");
        try { using var old=new ParcelRepository(DirectoryFor(name,c),c.ApplicationId); Check(false,name+": historical writer must refuse"); }
        catch(InvalidDataException) { Check(files.All(p=>File.Exists(p.Key)&&Wire.FileHash(p.Key)==p.Value),name+": historical writer rejects without changing custody"); }
    }
    static async Task StoragePreflightChecks()
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
            await RefuseNonCurrentStorage(name,c);
        }
        var db5 = Config("sportmaster");
        db5 = db5 with { Wcs = db5.Wcs! with { Parameters = new(db5.Wcs.Parameters) { ["reply-mode"] = "lose-after-commit" } } };
        await using (var app = await Open(db5,"db5-unknown"))
        { app.EmitReference("db5-original","00085","db5-source"); await Wait(app,s=>s.Operations.FirstOrDefault()?.State==ParcelState.WcsUnknown); }
        await RefuseNonCurrentStorage("db5-unknown",db5,storageVersion:5);
        // A different frozen implementation is rejected at the same pre-migration
        // boundary. This fixture retains the real original requests and replies.
        var changed=Config("sportmaster") with { Wcs=Config("sportmaster").Wcs! with { Parameters=new(Config("sportmaster").Wcs!.Parameters){["reply-mode"]="lose-after-commit"} } };
        var changedName="db4-old-implementation";
        await using(var app=await Open(changed,changedName)){app.EmitReference("old-implementation","00083","old-implementation-source");await Wait(app,s=>s.Operations.FirstOrDefault()?.State==ParcelState.WcsUnknown);}
        using(var repo=new ParcelRepository(DirectoryFor(changedName,changed),changed.ApplicationId))
        {var op=repo.Load().Single();repo.Commit(op with { ImplementationHashes=op.ImplementationHashes.ToDictionary(x=>x.Key,_=>new string('A',64)) },"SyntheticPriorImplementation","Only frozen implementation hash differs in this synthetic fixture");}
        await RefuseNonCurrentStorage(changedName,changed);
        // Hold a nonempty WAL without a live Application. Refusal must not checkpoint
        // or alter the source DB/WAL, even when the unresolved source is only in WAL.
        var walName="db4-nonempty-wal";
        await using(var app=await Open(changed,walName)){app.EmitReference("wal-original","00084","wal-source");await Wait(app,s=>s.Operations.FirstOrDefault()?.State==ParcelState.WcsUnknown);}
        var walDb=StorageDatabase(walName,changed);MarkNonCurrent(walDb,4);
        using(var writer=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=walDb,Pooling=false}.ToString()))
        {
            writer.Open();using var q=writer.CreateCommand();q.CommandText="PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; UPDATE parcel_meta SET revision=revision+1";q.ExecuteNonQuery();
            var dbHash=StableOpenFixtureHash(walDb);var walHash=StableOpenFixtureHash(walDb+"-wal");var configHash=Wire.FileHash(Path.Combine(DirectoryFor(walName,changed),"configuration.json"));
            try {await using var unexpected=await Open(changed,walName);Check(false,"nonempty WAL unresolved upgrade must refuse");}
            catch(InvalidDataException ex){Check(ex.Message.Contains("全新数据目录")&&StableOpenFixtureHash(walDb)==dbHash&&StableOpenFixtureHash(walDb+"-wal")==walHash&&Wire.FileHash(Path.Combine(DirectoryFor(walName,changed),"configuration.json"))==configHash,"read-only preflight preserves exact nonempty DB/WAL/config originals");}
        }
        var settled=Config("sportmaster");var terminal="db4-terminal";
        await using(var app=await Open(settled,terminal)) {app.EmitReference("settled-upgrade","00082","settled-upgrade-source");await Wait(app,s=>s.Operations.FirstOrDefault()?.State==ParcelState.SlotResolved);}
        var database=StorageDatabase(terminal,settled);MarkNonCurrent(database,4);
        var settledHash = Wire.FileHash(database);
        try { using var repo=new ParcelRepository(DirectoryFor(terminal,settled),settled.ApplicationId); Check(false,"settled historical storage must refuse too"); }
        catch (InvalidDataException) { Check(Wire.FileHash(database)==settledHash && NonCurrentStorageFixture.Version(database)==4,"settled DB4 remains unchanged; settlement does not authorize format conversion"); }
    }
    static async Task<BusinessReceipt> CurrentMaintenanceAction(ParcelRuntime app, Func<ParcelSnapshot, Task<BusinessReceipt>> action)
    {
        // Configure in a snapshot is not a reservation: a health probe can acquire
        // the external-I/O guard before the next call. Retry only explicit rejection
        // before admission (or command CAS conflict), never accepted/unknown work.
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var snapshot = await app.SnapshotAsync().WaitAsync(TimeSpan.FromSeconds(2));
            if (snapshot.Availability?.Configure == true)
            {
                var result = await action(snapshot);
                if (result.Status != "Conflict" && !(result.Status == "Rejected" && result.Reason is
                    "Unsupported command or an application guard is closed" or "Pause and authenticate maintenance before configuration"))
                    return result;
            }
            await Task.Delay(20);
        }
        throw new TimeoutException("Legacy maintenance guard did not settle; no successful receipt inferred");
    }
    static async Task NonCurrentPositionChecks(int schema, bool previouslyRecovered)
    {
        var c = Config("sportmaster"); var name = $"unsupported-conflict-{schema}-{previouslyRecovered}";
        await using (var initial = await Open(c, name)) { }
        using (var repo = new ParcelRepository(DirectoryFor(name,c), c.ApplicationId))
        {
            if (previouslyRecovered)
            {
                var old = new IdentityConflict(Wire.Id(), "original", ["main"], "Historical invented scope", DateTimeOffset.UtcNow);
                repo.Commit(null, "IdentityConflict", Wire.Encode(old), identityConflict: old);
            }
            else repo.Commit(null, "IdentityConflict", "Original unscoped audit; no identity may be invented");
        }
        var originals = CustodyFiles(name,c); var constructed = 0;
        try { await using var unexpected = await Open(c,name,f => { f.Boundary = _ => constructed++; return f; }); Check(false,"unsupported conflict must refuse"); }
        catch (InvalidDataException)
        {
            Check(constructed == 0 && originals.All(p=>File.Exists(p.Key)&&Wire.FileHash(p.Key)==p.Value),
                name + ": read-only refusal preserves original conflict, scope, DB and configuration before resources start");
        }
    }
    static async Task TopologyFormatChecks()
    {
        foreach (var schema in new[]{1,2})
        {
            var c = Config("sportmaster") with { SchemaVersion=schema, Topology=null };
            var name = "unsupported-topology-" + schema; var path = Path.Combine(root,name);
            Directory.CreateDirectory(path); var seed = Path.Combine(path,"business.json"); ExtensionStore.AtomicWrite(seed,c);
            var original=Wire.FileHash(seed); var constructed=0;
            try { await using var unexpected=await Open(c,name,f=>{f.Boundary=_=>constructed++;return f;});Check(false,"old topology must refuse"); }
            catch(InvalidDataException) { Check(constructed==0 && Wire.FileHash(seed)==original && !File.Exists(StorageDatabase(name,c)),"old schema refuses before DB/resources; no main/left mapping or candidate upgrade"); }
        }
        var current = Config("sportmaster"); const string currentName = "current-conflict-restart";
        await using(var app=await Open(current,currentName)) { }
        var fact=new IdentityConflict(Wire.Id(),"retired-original",["left"],"Current scoped original",DateTimeOffset.UtcNow);
        using(var repo=new ParcelRepository(DirectoryFor(currentName,current),current.ApplicationId)) repo.Commit(null,"IdentityConflict",Wire.Encode(fact),identityConflict:fact);
        await using(var reopened=await Open(current,currentName))
        {
            Check(Wire.Encode((await reopened.SnapshotAsync()).IdentityConflicts!.Single())==Wire.Encode(fact),"current scoped conflict restarts without rewriting original identity");
            await Maintenance(reopened);
            var receipt=await CurrentMaintenanceAction(reopened,s=>reopened.CommandAsync(new(Wire.Id(),s.Revision,"AcknowledgeIdentityConflict",fact.Id,"Inspected synthetic original")));
            Check(receipt.Status=="Persisted" && !(await reopened.SnapshotAsync()).IdentityConflicts!.Any(),"current explicit authorized acknowledgement retains its business guard");
        }
    }
    public static async Task StorageFormatChecks(string source,string evidence)
    {
        workspace=source;root=evidence;failures=0;
        await StoragePreflightChecks();
        foreach(var schema in new[]{1,2})foreach(var recovered in new[]{false,true})await NonCurrentPositionChecks(schema,recovered);
        await TopologyFormatChecks();
        if(failures!=0)throw new Exception("Upgrade/conflict closure failures: "+failures);
        Console.WriteLine("PASS STORAGE FORMAT GUARDS COMPLETE");
    }
}
