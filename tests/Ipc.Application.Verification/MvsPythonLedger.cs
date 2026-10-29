using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static partial class SportmasterRcs
{
    static void VerifyPythonLedgerRefusals(string original, ParcelOperation[] operations, string root)
    {
        var cases=new (string Name,string Sql)[]
        {
            ("missing-task","DELETE FROM requests WHERE id=(SELECT min(id) FROM requests WHERE json_extract(body,'$.data[0].command')='sorter.task.dest_request')"),
            ("changed-hash","UPDATE requests SET sha256='changed' WHERE json_extract(body,'$.data[0].command')='sorter.task.dest_request'"),
            ("wrong-response-id","UPDATE requests SET response=json_set(response,'$.requestId',request_id+1) WHERE json_extract(body,'$.data[0].command')='sorter.task.dest_request'"),
            ("duplicate-task","INSERT INTO requests(request_id,received,sha256,body,response,mode) SELECT request_id,received,sha256,body,response,mode FROM requests WHERE json_extract(body,'$.data[0].command')='sorter.task.dest_request' LIMIT 1")
        };
        foreach(var test in cases)
        {
            var copy=Path.Combine(root,"python-oracle-"+test.Name+".sqlite");
            using(var source=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=original,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
            using(var target=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=copy,Pooling=false}.ToString()))
            {
                source.Open();target.Open();source.BackupDatabase(target);
                using var mutation=target.CreateCommand();mutation.CommandText=test.Sql;mutation.ExecuteNonQuery();
            }
            var refused=false;
            try{VerifyPythonLedger(copy,operations);}catch(InvalidDataException){refused=true;}
            Check(refused,"independent Python oracle refuses "+test.Name+" in a private copy; live simulator ledger is read-only");
        }
        Check(VerifyPythonLedger(original,operations),"live Python ledger still matches every original after negative oracle checks");
    }
    // Verification-only oracle. Never opens the Python simulator's private ledger
    // writable, and never substitutes the oracle for actual HTTP/Application facts.
    static bool VerifyPythonLedger(string path, ParcelOperation[] operations)
    {
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false }.ToString());
        db.Open();using var query=db.CreateCommand();query.CommandText="SELECT request_id,sha256,body,response,mode FROM requests ORDER BY id";
        List<(long Id,string Hash,string Body,string Response,string Mode)> tasks=[];
        using(var rows=query.ExecuteReader())
        {
            while(rows.Read())
            {
                using var body=JsonDocument.Parse(rows.GetString(2));
                if(body.RootElement.GetProperty("data").EnumerateArray().Any(x=>x.GetProperty("command").GetString()=="sorter.task.dest_request"))
                    tasks.Add((rows.GetInt64(0),rows.GetString(1),rows.GetString(2),rows.GetString(3),rows.GetString(4)));
            }
        }
        if(tasks.Count!=operations.Length || tasks.Select(x=>x.Id).Distinct().Count()!=operations.Length)
            throw new InvalidDataException("Python task count/IDs differ from independently durable client originals");
        foreach(var operation in operations)
        {
            var stage=operation.Wcs ?? throw new InvalidDataException("Missing durable RCS stage");
            var wire=stage.Request.IntegrationRequest ?? throw new InvalidDataException("Missing T1 HTTP original");
            var stored=tasks.Single(x=>x.Id==wire.RequestId);
            if(stored.Mode!="assigned" || stored.Body!=wire.Body || !StringComparer.OrdinalIgnoreCase.Equals(stored.Hash,wire.BodyHash) ||
                !StringComparer.OrdinalIgnoreCase.Equals(stored.Hash,Wire.Hash(stored.Body)) || stage.Attempts!=1 || operation.State!=ParcelState.SlotResolved)
                throw new InvalidDataException("Python received bytes/hash/attempt differ from client frozen original");
            using var body=JsonDocument.Parse(stored.Body);var command=body.RootElement.GetProperty("data").EnumerateArray().Single();
            var parameters=command.GetProperty("params");
            if(body.RootElement.GetProperty("requestId").GetInt64()!=wire.RequestId || command.GetProperty("command").GetString()!="sorter.task.dest_request" ||
                parameters.GetProperty("quantity").GetInt32()!=1 || parameters.GetProperty("loadCode").GetString()!=operation.ParcelCode)
                throw new InvalidDataException("Python envelope does not preserve the original SM code/quantity/identity");
            using var response=JsonDocument.Parse(stored.Response);var result=response.RootElement.GetProperty("result").EnumerateArray().Single();
            var accepted=stage.Replies.Single(x=>x.BusinessAccepted==true);
            var http=accepted.HttpResponse ?? throw new InvalidDataException("Accepted reply lacks original HTTP custody");
            var raw=Convert.FromBase64String(http.BodyBase64);
            if(response.RootElement.GetProperty("requestId").GetInt64()!=wire.RequestId || !result.GetProperty("success").GetBoolean() ||
                result.GetProperty("data").GetProperty("chuteCode").GetString()!=operation.Slot ||
                result.GetProperty("data").GetProperty("taskId").GetString()!=accepted.Destination?.TaskId ||
                http.StatusCode!=200 || !http.Complete || !StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(SHA256.HashData(raw)),http.BodyHash) ||
                !JsonNode.DeepEquals(JsonNode.Parse(stored.Response),JsonNode.Parse(raw)))
                throw new InvalidDataException("Python response does not match original client HTTP custody and assigned task/chute");
        }
        query.CommandText="SELECT count(*) FROM assignments";
        if(Convert.ToInt64(query.ExecuteScalar())!=operations.Select(x=>x.ParcelCode).Distinct().Count())
            throw new InvalidDataException("Python same-code assignment dedup differs from distinct client request ownership");
        return true;
    }
}
