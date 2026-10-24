using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;

// Explicit synthetic offline fixture. No bulk row claims to have traversed a real device/runtime.
static class RetentionQualification
{
    static void Check(bool value, string reason) { if (!value) throw new InvalidDataException(reason); Console.WriteLine("PASS RETENTION-QUAL: " + reason); }
    static string Id(int kind, long n) => kind.ToString("x2") + n.ToString("x30");
    static void Save(string path, object value) => ExtensionStore.AtomicWrite(path, value);
    static long Scalar(SqliteConnection db, string sql) { using var c = db.CreateCommand(); c.CommandText = sql; return Convert.ToInt64(c.ExecuteScalar()); }
    static SqliteConnection Open(string path, bool readOnly = false)
    { var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path, Pooling=false, Mode=readOnly?SqliteOpenMode.ReadOnly:SqliteOpenMode.ReadWrite }.ToString()); db.Open(); return db; }
    static string Fingerprint(SqliteConnection db,string sql)
    {
        using var h=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);using var c=db.CreateCommand();c.CommandText=sql;
        using var r=c.ExecuteReader();while(r.Read())for(var i=0;i<r.FieldCount;i++){var bytes=Encoding.UTF8.GetBytes(r.GetString(i));h.AppendData(BitConverter.GetBytes(bytes.Length));h.AppendData(bytes);}
        return Convert.ToHexString(h.GetHashAndReset());
    }
    const string IdentitySql="SELECT id,admission,observation,position FROM parcel_history_index ORDER BY seq";
    const string ReplySql="SELECT request,id,hash FROM parcel_reply_identities ORDER BY request,id";
    public static async Task Run(string workspace, string[] args)
    {
        var mode=args[0]; var output=Path.GetFullPath(args[1]);
        if(mode=="seed") { await Seed(workspace, output); return; }
        if(mode=="create") { Create(args[2], output, int.Parse(args[3])); return; }
        if(mode=="verify") { Verify(output); return; }
        throw new ArgumentException("Use seed OUTPUT, create CASE_ROOT SEED_ROOT COUNT, or verify CASE_ROOT");
    }
    static async Task Seed(string workspace,string output)
    {
        if(Directory.Exists(output))throw new IOException("New seed directory required"); Directory.CreateDirectory(output);
        var c=Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace,"config/sportmaster/business.json"))) with
        { StationId="retention-qualification", TimeoutMs=30000, Retention=new(200,67108864,HistoryDays:7) };
        var config=Path.Combine(output,"config");Directory.CreateDirectory(config); Save(Path.Combine(config,"business.json"),c);
        var launch=new BusinessLaunch(config,Path.Combine(output,"data"),null,null);
        ParcelOperation operation;
        await using(var app=new ParcelRuntime(launch,new SportmasterFlow(),new ParcelCapabilityFactory(launch)))
        {
            await app.StartAsync(default);
            Check(app.EmitReferenceAt("left","synthetic-seed-admission","SYNTHETIC-RETENTION-SEED","synthetic-seed-event"),"actual reference ingress accepted for seed");
            var deadline=DateTimeOffset.UtcNow.AddSeconds(10);
            while(true)
            {
                var s=await app.SnapshotAsync();
                if(s.Operations.SingleOrDefault() is { State:ParcelState.SlotResolved } ready) { operation=ready; await app.CommandAsync(new(Wire.Id(),s.Revision,"Pause")); break; }
                if(DateTimeOffset.UtcNow>deadline)throw new TimeoutException("Reference seed did not settle");await Task.Delay(10);
            }
        }
        var directory=Path.Combine(launch.DataRoot,"Simulation",c.StationId,c.ApplicationId);
        // Public snapshots redact binding parameters; the frozen hash belongs to the original repository fact.
        using(var repository=new ParcelRepository(directory,c.ApplicationId))operation=repository.Load().Single();
        Save(Path.Combine(output,"operation.json"),operation);
        File.Copy(Path.Combine(directory,"configuration.json"),Path.Combine(output,"configuration.json"));
        Save(Path.Combine(output,"SEED.json"),new{Scope="Actual short reference-runtime seed only; bulk fixture is explicitly synthetic",OperationBytes=Encoding.UTF8.GetByteCount(Wire.Encode(operation)),RawBytes=Encoding.UTF8.GetByteCount(Wire.Encode(operation.Wcs!.Replies.Single())),ProductSha256=Wire.FileHash(typeof(ParcelRepository).Assembly.Location),FixtureSha256=Wire.FileHash(typeof(RetentionQualification).Assembly.Location)});
    }
    static void Create(string seed,string output,int count)
    {
        if(count<1||count>2592000||Directory.Exists(output))throw new InvalidDataException("New case root and bounded 1..2592000 count required");
        var watch=Stopwatch.StartNew();Directory.CreateDirectory(output);
        var template=Wire.Decode<ParcelOperation>(File.ReadAllText(Path.Combine(seed,"operation.json")));
        var seedWcs=template.Wcs??throw new InvalidDataException("A settled WCS reference seed is required");
        var directory=Path.Combine(output,"data","Simulation",template.FrozenConfiguration.StationId,"sportmaster");Directory.CreateDirectory(directory);
        File.Copy(Path.Combine(seed,"configuration.json"),Path.Combine(directory,"configuration.json"));
        using(var repository=new ParcelRepository(directory,"sportmaster"))repository.Commit(null,"SyntheticFixturePaused","Offline synthetic full-custody fixture; no equipment",paused:true);
        var database=Path.Combine(directory,"sportmaster.db");
        var now=DateTimeOffset.UtcNow;var old=now.AddDays(-8);var rawDirectory=Path.Combine(directory,"receipts");
        using(var db=Database.Open(database,create:false))
        {
            SqliteCommand Command(string sql){var c=db.CreateCommand();c.CommandText=sql;return c;}
            using var history=Command("INSERT INTO parcel_history_index(id,admission,observation,position) VALUES($id,$admission,$observation,$position)");
            using var archived=Command("INSERT INTO parcel_archives VALUES($id,$admission,$observation,$body,$hash,$at)");
            using var resident=Command("INSERT INTO parcel_operations VALUES($id,$admission,$observation,$body)");
            using var receipt=Command("INSERT INTO parcel_receipts VALUES($receipt,$id,'wcs-slot',$request,'Send',$state,$hash)");
            using var raw=Command("INSERT INTO parcel_archived_receipts VALUES($receipt,$bytes,$hash)");
            using var replyIdentity=Command("INSERT INTO parcel_reply_identities VALUES($request,$reply,$hash)");
            using var ingress=Command("INSERT INTO parcel_ingress(id,position,received_at,state,body,body_hash,detail,session,started_at,completed_at,queue_ms,processing_ms) VALUES($input,$position,$at,$state,$body,$hash,'Synthetic offline imported custody','synthetic-full-scale',$at,$at,1,1)");
            using var audit=Command("INSERT INTO parcel_audit(at,event,owner,detail) VALUES($at,$event,$id,$detail)");
            using var association=Command("INSERT INTO parcel_ingress_audits VALUES($input,last_insert_rowid())");
            var commands=new[]{history,archived,resident,receipt,raw,replyIdentity,ingress,audit,association};
            void Exec(SqliteCommand c, params (string Name,object? Value)[] values){c.Parameters.Clear();foreach(var (name,value) in values)c.Parameters.AddWithValue(name,value??DBNull.Value);c.ExecuteNonQuery();}
            for(var first=1;first<=count+6;first+=1000)
            {
                if(new DriveInfo(Path.GetPathRoot(output)!).AvailableFreeSpace<8L*1024*1024*1024)throw new IOException("Synthetic fixture stopped at 8GiB local free-space reserve; original partial dataset retained");
                using var tx=db.BeginTransaction();foreach(var c in commands)c.Transaction=tx;
                for(var n=first;n<=Math.Min(first+999,count+6);n++)
                {
                    var guard=n-count;var at=guard==6?now.AddDays(-1):old;var position=n%2==0?"left":"right";
                    var opId=Id(1,n);var admission="qualification-admission-"+n;var observation="qualification-observation-"+n;
                    var reader=template.FrozenConfiguration.Topology!.Readers[n%2==0?0:1];
                    var payload=new ParcelPayload("SYNTHETIC-RETENTION-"+n,admission,template.Weight,null,null,PositionId:position,ReaderBinding:reader);
                    var payloadJson=Wire.Encode(payload);var request=seedWcs.Request with {Id=Id(2,n),OperationId=opId,AdmissionId=admission,Payload=payloadJson,PayloadHash=Wire.Hash(payloadJson),PreparedAt=at};
                    var reply=seedWcs.Replies.Single() with {Id=Id(3,n),RequestId=request.Id,PayloadHash=request.PayloadHash,At=at.AddMilliseconds(20),Evidence="Synthetic accepted reference reply; not customer wire. "+new string('R',1024)};
                    var unknown=guard==4;
                    var fact=new StageFact(request,unknown?StageState.Unknown:StageState.Accepted,1,unknown?[]:[reply]);
                    var op=template with{Id=opId,AdmissionId=admission,ObservationId=observation,ParcelCode=payload.ParcelCode,ObservedAt=at,Wcs=fact,State=unknown?ParcelState.WcsUnknown:ParcelState.SlotResolved,PositionId=position,ReaderBinding=reader,Acquisition=null,BarcodeSelection=null,RecoveryRequired=unknown};
                    var body=Wire.Encode(op);var bodyHash=Wire.Hash(body);var receiptId=Id(4,n);var inputId=Id(5,n);var replyJson=Wire.Encode(reply);var rawHash=Wire.Hash(replyJson);
                    Exec(history,("$id",opId),("$admission",admission),("$observation",observation),("$position",position));
                    var isResident=n<=count && n>count-8;
                    if(isResident)Exec(resident,("$id",opId),("$admission",admission),("$observation",observation),("$body",body));
                    else Exec(archived,("$id",opId),("$admission",admission),("$observation",observation),("$body",body),("$hash",bodyHash),("$at",at.ToString("O")));
                    Exec(receipt,("$receipt",receiptId),("$id",opId),("$request",request.Id),("$state",unknown?"Awaiting":"Committed"),("$hash",unknown?null:rawHash));
                    if(!unknown)
                    {
                        if(isResident)File.WriteAllText(Path.Combine(rawDirectory,receiptId+".json"),replyJson,new UTF8Encoding(false));
                        else Exec(raw,("$receipt",receiptId),("$bytes",Encoding.UTF8.GetBytes(replyJson)),("$hash",rawHash));
                        Exec(replyIdentity,("$request",request.Id),("$reply",reply.Id),("$hash",rawHash));
                    }
                    var inputBody=Wire.Encode(new{Position=position,Observation=new{AdmissionId=admission,Id=observation,Codes=new[]{payload.ParcelCode},At=at}});
                    Exec(ingress,("$input",inputId),("$position",position),("$at",at.ToString("O")),("$state",guard==3?"Interrupted":"Processed"),("$body",inputBody),("$hash",Wire.Hash(inputBody)));
                    Exec(audit,("$at",at.ToString("O")),("$event","InputObserved"),("$id",opId),("$detail",Wire.Encode(new{OperationId=opId,AdmissionId=admission,Synthetic=true})));
                    Exec(association,("$input",inputId));
                    foreach(var phase in new[]{"SyntheticT1","SyntheticT2","SyntheticT3"})Exec(audit,("$at",(guard==5?now:at).ToString("O")),("$event",phase),("$id",opId),("$detail",Wire.Encode(new{Request=request.Id,OriginalReply=unknown?null:reply.Id,Synthetic=true})));
                }
                tx.Commit();foreach(var c in commands)c.Transaction=null;
                Console.WriteLine($"PREFILL {Math.Min(first+999,count+6)}/{count+6} elapsed={watch.Elapsed.TotalSeconds:F1}s");
            }
            using var checkpoint=db.CreateCommand();checkpoint.CommandText="PRAGMA wal_checkpoint(TRUNCATE)";checkpoint.ExecuteNonQuery();
        }
        using(var repo=new ParcelRepository(directory,"sportmaster"))
        {
            repo.AdmitCommand(new(Id(6,count+1),repo.Revision,"Reconcile",Id(1,count+1),"Synthetic pending command must retain owner"));
            repo.Commit(null,"IdentityConflict","Synthetic unresolved conflict",identityConflict:new(Id(7,count+2),Id(1,count+2),["left"],"Synthetic qualification conflict; inspect",old));
            repo.ValidateRawInventory();
        }
        using(var db=Open(database,true))
        {
            using var integrity=db.CreateCommand();integrity.CommandText="PRAGMA integrity_check";Check(integrity.ExecuteScalar() as string=="ok","complete synthetic DB integrity");
            Save(Path.Combine(output,"FIXTURE.json"),new{Status="CREATED_FULL_SYNTHETIC_CUSTODY",Eligible=count,Guards=6,Database=database,DatabaseSha256=Wire.FileHash(database),DatabaseBytes=new FileInfo(database).Length,PrefillSeconds=watch.Elapsed.TotalSeconds,Operations=Scalar(db,"SELECT count(*) FROM parcel_operations"),Archives=Scalar(db,"SELECT count(*) FROM parcel_archives"),RawRows=Scalar(db,"SELECT count(*) FROM parcel_archived_receipts"),Ingress=Scalar(db,"SELECT count(*) FROM parcel_ingress"),Audit=Scalar(db,"SELECT count(*) FROM parcel_audit"),ReplyIdentities=Scalar(db,"SELECT count(*) FROM parcel_reply_identities"),IdentityFingerprint=Fingerprint(db,IdentitySql),ReplyFingerprint=Fingerprint(db,ReplySql),GuardKinds=new[]{"PendingCommand","UnacknowledgedConflict","InterruptedInput","UnknownAwaitingRaw","RecentAudit","NotExpired"},Scope="Reference-flow synthetic import with complete operation/frozen intent/reply raw/input/audit identities; no real device/RCS traffic, no real-time duration, no images or RCS numeric wire profile",ProductSha256=Wire.FileHash(typeof(ParcelRepository).Assembly.Location),FixtureSha256=Wire.FileHash(typeof(RetentionQualification).Assembly.Location)});
        }
    }
    static void Verify(string output)
    {
        var fixture=JsonNode.Parse(File.ReadAllText(Path.Combine(output,"FIXTURE.json")))!;var count=fixture["Eligible"]!.GetValue<int>();var database=fixture["Database"]!.GetValue<string>();
        using var db=Open(database,true);
        Check(Scalar(db,"SELECT count(*) FROM parcel_retired_operations")==count,"exact eligible operation retirement count");
        Check(Scalar(db,"SELECT count(*) FROM parcel_retired_inputs")==count,"exact processed input retirement count");
        Check(Scalar(db,"SELECT count(*) FROM parcel_history_index")==count+6,"all permanent admission identities retained");
        Check(Scalar(db,"SELECT count(*) FROM parcel_operations")+Scalar(db,"SELECT count(*) FROM parcel_archives")==6,"all six independent guard cases retained");
        Check(Scalar(db,"SELECT count(*) FROM parcel_reply_identities")==count+5,"all original reply fingerprints retained");
        Check(Scalar(db,"SELECT count(*) FROM parcel_ingress")==6,"retained-operation inputs remain");
        Check(Scalar(db,"SELECT count(*) FROM parcel_receipts")==6,"only retained-operation receipts remain");
        Check(Fingerprint(db,IdentitySql)==fixture["IdentityFingerprint"]!.GetValue<string>(),"all permanent admission identity bytes unchanged");
        Check(Fingerprint(db,ReplySql)==fixture["ReplyFingerprint"]!.GetValue<string>(),"all original reply identity/hash bytes unchanged");
        using var integrity=db.CreateCommand();integrity.CommandText="PRAGMA integrity_check";Check(integrity.ExecuteScalar() as string=="ok","post-retention integrity");
        using var process=Process.GetCurrentProcess();
        Save(Path.Combine(output,"VERIFIED.json"),new{Status="PASS_FULL_SYNTHETIC_RETENTION_COUNTS",Eligible=count,OriginalDatabaseSha256=fixture["DatabaseSha256"]!.GetValue<string>(),PostDatabaseSha256=Wire.FileHash(database),PostBytes=new FileInfo(database).Length,VerifierPeakWorkingSet=process.PeakWorkingSet64,Scope="Synthetic count/custody/permanent identity verification; not protected authorization, real devices or 24h/72h qualification"});
    }
}
