using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

// Public API only: this exact harness also runs against the unmodified parent DLLs.
// Do not reference the candidate's internal cancellation observer from this type.
static class HistoryOpenBenchmark
{
    internal sealed record Facts(long Resident, long Archived, long Owners, long MaximumArchived,
        long Floor, long Restores, string OwnersSha256, string RestoresSha256, string DatabaseSha256);
    static void Require(bool value,string detail) {if(!value)throw new InvalidDataException(detail);}
    internal static ParcelOperation Original(ParcelConfiguration configuration,int i)
    {
        var at=new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero).AddMilliseconds(i);
        var id="op-"+i;var admission="entity-"+i;var observation="observation-"+i;
        var payload=Wire.Encode(new ParcelPayload("SYNTH-"+i,admission,null,null,null));
        var request=new FrozenRequest("request-"+i,"sportmaster",id,admission,"Wcs",configuration.Wcs!,
            new string('A',64),Wire.Hash(Wire.Encode(configuration)),payload,Wire.Hash(payload),at,
            new(i,"application/json",payload,Wire.Hash(payload)));
        return new(id,admission,"SYNTH-"+i,observation,at,request.ConfigurationHash,configuration,
            new(),configuration.Weight,null,ParcelState.Abandoned,null,new(request,StageState.Cancelled,0,[]),null,
            "unsubmitted synthetic disposition",PositionId:"left");
    }
    internal static void Populate(string directory,ParcelConfiguration configuration)
    {
        using var repository=new ParcelRepository(directory,"sportmaster");
        for(var i=1;i<=2000;i++)repository.Commit(Original(configuration,i),"SyntheticUnsubmittedDisposition","{}");
        repository.Archive(Enumerable.Range(1001,1000).Select(i=>"op-"+i).ToArray(),"synthetic archive; no remote submission");
    }
    public static void CreateFixture(string workspace,string directory)
    {
        directory=Path.GetFullPath(directory);Require(!Directory.Exists(directory)&&!File.Exists(directory),"New synthetic fixture directory required");
        var configuration=Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace,"tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        Populate(directory,configuration);var facts=ReadFacts(directory);RequireCorpus(facts);
        Console.WriteLine(JsonSerializer.Serialize(new{Kind="fixture",Facts=facts,Scope="Synthetic unsubmitted T1 originals; no physical, device or elapsed-time qualification"}));
    }
    internal static Facts ReadFacts(string directory)
    {
        var path=Path.Combine(directory,"sportmaster.db");
        long resident,archived,owners,maximum,floor,restores;string ownerHash,restoreHash;
        using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
        {
            db.Open();using var command=db.CreateCommand();
            long Number(string sql){command.CommandText=sql;return Convert.ToInt64(command.ExecuteScalar());}
            resident=Number("SELECT count(*) FROM parcel_operations");archived=Number("SELECT count(*) FROM parcel_archives");
            owners=Number("SELECT count(*) FROM parcel_integration_id_owners");
            maximum=Number("SELECT coalesce(max(json_extract(body,'$.Wcs.Request.IntegrationRequest.RequestId')),0) FROM parcel_archives");
            floor=Number("SELECT max(value) FROM (SELECT coalesce(max(id),0) value FROM parcel_integration_id_owners UNION ALL SELECT coalesce(max(ceiling),0) value FROM parcel_integration_id_restores)");
            restores=Number("SELECT count(*) FROM parcel_integration_id_restores");
            string Digest(string sql)
            {
                command.CommandText=sql;using var reader=command.ExecuteReader();using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                while(reader.Read())
                {
                    var values=Enumerable.Range(0,reader.FieldCount).Select(i=>reader.IsDBNull(i)?null:reader.GetValue(i)).ToArray();
                    hash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values)));hash.AppendData("\n"u8);
                }
                return Convert.ToHexString(hash.GetHashAndReset());
            }
            ownerHash=Digest("SELECT id,operation,request,request_hash FROM parcel_integration_id_owners ORDER BY id");
            restoreHash=Digest("SELECT identity,ceiling FROM parcel_integration_id_restores ORDER BY identity");
        }
        return new(resident,archived,owners,maximum,floor,restores,ownerHash,restoreHash,Wire.FileHash(path));
    }
    static void RequireCorpus(Facts facts) => Require(facts.Resident==1000&&facts.Archived==1000&&facts.Owners==2000&&facts.MaximumArchived==2000&&facts.Floor==2000&&facts.Restores==0,
        "Expected one frozen mixed corpus: 1000 resident,1000 archived,2000 owners,archive maximum2000,empty restore set");
    internal static double Measure(string directory,Facts expected)
    {
        Require(ReadFacts(directory)==expected,"Original corpus changed before open");
        double elapsed;
        var watch=Stopwatch.StartNew();
        using(var repository=new ParcelRepository(directory,"sportmaster"))
        {
            watch.Stop();elapsed=watch.Elapsed.TotalMilliseconds;
            // Public budget and all evidence work are deliberately outside the timer.
            Require(repository.IntegrationBudget(new(1,2100)).PermanentHighWater==expected.Floor,"Public allocation floor changed");
            Require(repository.StorageVersion==ParcelRepository.CurrentStorageVersion,"Storage version changed");
        }
        Require(ReadFacts(directory)==expected,"Open/disposal changed original bytes, owners or restore ceiling");
        return elapsed;
    }
    public static void Worker(string directory)
    {
        directory=Path.GetFullPath(directory);var expected=ReadFacts(directory);RequireCorpus(expected);
        var runtime=typeof(ParcelRepository).Assembly;var harness=Assembly.GetExecutingAssembly();
        Console.WriteLine(JsonSerializer.Serialize(new{Kind="ready",Process=Environment.ProcessId,
            RuntimeSha256=Wire.FileHash(runtime.Location),HarnessSha256=Wire.FileHash(harness.Location),
            RuntimeVersion=runtime.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            Framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,OS=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),Processors=Environment.ProcessorCount,Facts=expected}));
        while(Console.ReadLine() is { } command)
        {
            if(command=="quit")return;
            if(command is not ("warmup" or "sample"))throw new InvalidDataException("Unsupported benchmark command");
            var elapsed=Measure(directory,expected);
            Console.WriteLine(JsonSerializer.Serialize(new{Kind=command,Milliseconds=elapsed,OriginalsUnchanged=true,Floor=expected.Floor}));
        }
    }
}
