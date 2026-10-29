using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static class HistoryOpenSafety
{
    static void Check(bool ok,string detail) { if(!ok)throw new Exception(detail);Console.WriteLine("PASS WT054 "+detail); }
    internal static string Snapshot(string directory) => Wire.Hash(Wire.Encode(
        Directory.EnumerateFileSystemEntries(directory,"*",SearchOption.AllDirectories).OrderBy(x=>x,StringComparer.Ordinal)
            .Select(x=>new{Path=Path.GetRelativePath(directory,x),Hash=Directory.Exists(x)?"directory":Wire.FileHash(x)}).ToArray()));
    internal static string Owners(string file)
    {
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());db.Open();
        using var q=db.CreateCommand();q.CommandText="SELECT id,operation,request,request_hash FROM parcel_integration_id_owners ORDER BY id";
        using var r=q.ExecuteReader();List<object> rows=[];while(r.Read())rows.Add(new{Id=r.GetInt64(0),Operation=r.GetString(1),Request=r.GetString(2),Hash=r.GetString(3)});
        return Wire.Hash(Wire.Encode(rows));
    }
    internal static void ChangedArchiveRequest(string baseline,string root)
    {
        var directory=Path.Combine(root,"archive-request-changed");Directory.CreateDirectory(directory);
        var file=Path.Combine(directory,"sportmaster.db");File.Copy(baseline,file);
        using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Pooling=false}.ToString()))
        {
            db.Open();using var q=db.CreateCommand();q.CommandText="PRAGMA journal_mode=DELETE";q.ExecuteScalar();
            q.CommandText="SELECT body FROM parcel_archives WHERE id='op-1'";var original=Wire.Decode<ParcelOperation>((string)q.ExecuteScalar()!);
            var request=original.Wcs!.Request with{PreparedAt=original.Wcs.Request.PreparedAt.AddSeconds(1)};
            var body=Wire.Encode(original with{Wcs=original.Wcs with{Request=request}});
            q.CommandText="UPDATE parcel_archives SET body=$body,body_hash=$hash WHERE id='op-1'";
            q.Parameters.AddWithValue("$body",body);q.Parameters.AddWithValue("$hash",Wire.Hash(body));q.ExecuteNonQuery();
        }
        var before=Snapshot(directory);var owners=Owners(file);var refused=false;
        try{using var invalid=new ParcelRepository(directory,"sportmaster");}catch(InvalidDataException){refused=true;}
        Check(refused,"changed frozen archive request refuses despite recomputed archive checksum");
        Check(before==Snapshot(directory)&&owners==Owners(file),"changed archive request refusal retains bytes, sidecars, directory and original owners");
    }
    internal static void Cancellation(string corpus,string root)
    {
        var directory=Path.Combine(root,"cancel-originals");Directory.CreateDirectory(directory);
        var file=Path.Combine(directory,"sportmaster.db");File.Copy(Path.Combine(corpus,"sportmaster.db"),file);
        using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Pooling=false}.ToString()))
        {db.Open();using var q=db.CreateCommand();q.CommandText="PRAGMA journal_mode=DELETE";Check((string)q.ExecuteScalar()! == "delete","cancellation fixture is closed DELETE-journal copy");}
        var before=Snapshot(directory);var owners=Owners(file);
        using var cancellation=new CancellationTokenSource();int rows=0,writable=0;bool archive=false,readOnly=false,cancelled=false;
        void Observe(RepositoryOpenPhase phase,SqliteConnection? db,bool archived)
        {
            if(phase==RepositoryOpenPhase.BeforeWritableOpen){writable++;return;}
            rows++;
            if(rows!=1250)return;
            archive=archived;readOnly=SQLitePCL.raw.sqlite3_db_readonly(db!.Handle,"main")==1;cancellation.Cancel();
        }
        try{using var invalid=new ParcelRepository(directory,"sportmaster",cancellation.Token,null,Observe);}catch(OperationCanceledException){cancelled=true;}
        Check(cancelled&&rows==1250&&archive&&readOnly,"cancel at real archived row 1250 on actual SQLite read-only handle");
        Check(writable==0,"mid-history cancellation never reaches writable open");
        Check(before==Snapshot(directory)&&owners==Owners(file),"cancelled open leaves database, WAL/SHM, directory and owners unchanged");
        using(var exclusive=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(exclusive.Length>0,"cancelled reader and database handles are released");
        var preCancelled=false;
        try{using var invalid=new ParcelRepository(directory,"sportmaster",cancellation.Token);}catch(OperationCanceledException){preCancelled=true;}
        Check(preCancelled&&before==Snapshot(directory),"already-cancelled token refuses before filesystem changes");
        var absent=Path.Combine(root,"cancel-must-not-create");
        try{using var invalid=new ParcelRepository(absent,"sportmaster",cancellation.Token);}catch(OperationCanceledException){ }
        Check(!Directory.Exists(absent),"already-cancelled first open creates no directory");
        using var atWrite=new CancellationTokenSource();var atWriteRefused=false;var boundaryCalls=0;
        var atWriteDirectory=Path.Combine(root,"cancel-before-write");
        try{using var invalid=new ParcelRepository(atWriteDirectory,"sportmaster",atWrite.Token,null,(phase,_,_)=>
        {if(phase==RepositoryOpenPhase.BeforeWritableOpen){boundaryCalls++;atWrite.Cancel();}});}catch(OperationCanceledException){atWriteRefused=true;}
        Check(atWriteRefused&&boundaryCalls==1&&!Directory.Exists(atWriteDirectory),"cancellation at writable boundary precedes directory creation");
        int readRows=0,writeRows=0,readArchives=0,writeArchives=0,opens=0;
        using(var normal=new ParcelRepository(directory,"sportmaster",CancellationToken.None,null,(phase,db,archived)=>
        {
            if(phase==RepositoryOpenPhase.BeforeWritableOpen){opens++;return;}
            if(SQLitePCL.raw.sqlite3_db_readonly(db!.Handle,"main")==1){readRows++;if(archived)readArchives++;}
            else{writeRows++;if(archived)writeArchives++;}
        }))Check(normal.IntegrationBudget(new(1,2100)).PermanentHighWater==2000,"normal reopen after cancellation retains archived allocation floor");
        Check(opens==1&&readRows==2000&&writeRows==2000&&readArchives==1000&&writeArchives==1000,
            "normal open still validates every resident and archived original on both read-only and writable connections");
    }
}
