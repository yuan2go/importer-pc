using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

SQLitePCL.Batteries_V2.Init();
var root = Path.GetFullPath(args[0]);
var oldSql = File.ReadAllText(Path.Combine(root, "old.sql"));
var newSql = File.ReadAllText(Path.Combine(root, "new.sql"));
var prepareSql = File.ReadAllText(Path.Combine(root, "prepare.sql"));
var schema = File.ReadAllText(Path.Combine(root, "schema.sql"));
var scales = new List<object>();
var checks = new List<string>();
void Check(bool yes, string why) { if (!yes) throw new Exception(why); checks.Add(why); }
void Sql(SqliteConnection db, string sql) { using var c = db.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
object? Scalar(SqliteConnection db, string sql) { using var c = db.CreateCommand(); c.CommandText = sql; return c.ExecuteScalar(); }
List<string> Plan(SqliteConnection db, string sql, bool parameters = true)
{
    using var c = db.CreateCommand(); c.CommandText = "EXPLAIN QUERY PLAN " + sql;
    if (parameters) { c.Parameters.AddWithValue("$id", "candidate"); c.Parameters.AddWithValue("$cutoff", "2030-01-01"); }
    using var r = c.ExecuteReader(); var result = new List<string>(); while (r.Read()) result.Add(r.GetString(3)); return result;
}
string MainSchema(SqliteConnection db)
{
    using var c = db.CreateCommand(); c.CommandText = "SELECT type,name,coalesce(sql,'') FROM main.sqlite_master ORDER BY type,name";
    using var r = c.ExecuteReader(); var text = new StringBuilder();
    while (r.Read()) text.AppendLine(r.GetString(0)+"|"+r.GetString(1)+"|"+r.GetString(2));
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
}
void Command(SqliteConnection db, string id, string? owner, string status)
{
    using var c = db.CreateCommand(); c.CommandText = "INSERT INTO parcel_commands VALUES($id,'synthetic-hash',$receipt); INSERT INTO parcel_audit(at,event,owner,detail) VALUES('2020-01-01','CommandAdmitted','runtime',$detail)";
    c.Parameters.AddWithValue("$id", id); c.Parameters.AddWithValue("$receipt", JsonSerializer.Serialize(new { Status = status }));
    c.Parameters.AddWithValue("$detail", JsonSerializer.Serialize(new { Id = id, Owner = owner })); c.ExecuteNonQuery();
}
long Clear(SqliteConnection db, string sql, string id)
{
    using var c = db.CreateCommand(); c.CommandText = sql; c.Parameters.AddWithValue("$id", id); c.Parameters.AddWithValue("$cutoff", "2030-01-01"); return (long)c.ExecuteScalar()!;
}
foreach (var rows in new[] { 10000, 100000, 1000000 })
foreach (var pending in new[] { 0, 128 })
{
    using var db = new SqliteConnection("Data Source=:memory:;Pooling=False"); db.Open(); Sql(db, schema);
    Sql(db, $"WITH RECURSIVE n(x) AS(SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<{rows}) INSERT INTO parcel_audit(at,event,owner,detail) SELECT '2020-01-01','Noise','runtime','not-json' FROM n");
    for (var n=0; n<128; n++) Command(db, "command-"+n, "owner-"+n, n<pending ? "Pending" : "Inspected");
    var schemaHash = MainSchema(db);
    foreach (var mode in new[] { "old", "new" })
    {
        var timer = Stopwatch.StartNew(); if (mode == "new") Sql(db, prepareSql);
        var prepareMs = timer.Elapsed.TotalMilliseconds;
        var sql = mode == "old" ? oldSql : newSql;
        var plan = Plan(db, sql);
        Check(mode == "old" ? plan.Contains("SCAN a") : plan.Any(x=>x.Contains("parcel_retention_pending_owners USING PRIMARY KEY")) && !plan.Contains("SCAN a"), $"{rows}/{pending}/{mode}: expected audit scan or indexed owner lookup");
        using var c = db.CreateCommand(); c.CommandText = sql; c.Parameters.AddWithValue("$id", "candidate"); c.Parameters.AddWithValue("$cutoff", "2030-01-01");
        c.Prepare(); c.ExecuteScalar(); // warm prepared/native execution before measuring
        timer.Restart(); const int count = 128;
        for (var n=0; n<count; n++)
        {
            c.Parameters["$id"].Value = "candidate-"+n;
            if ((long)c.ExecuteScalar()! != 1) throw new Exception("unowned candidate unexpectedly blocked");
        }
        timer.Stop(); // wall-clock measurement excludes VM instrumentation callbacks
        long ticks = 0; SQLitePCL.raw.sqlite3_progress_handler(db.Handle, 1000, _ => { ticks++; return 0; }, null);
        for(var n=0; n<count; n++) { c.Parameters["$id"].Value="candidate-"+n; c.ExecuteScalar(); }
        SQLitePCL.raw.sqlite3_progress_handler(db.Handle, 0, null, null);
        Check(Clear(db, sql, "owner-0") == (pending==0 ? 1 : 0), $"{rows}/{pending}/{mode}: owning Pending command preserved");
        Check(MainSchema(db) == schemaHash, $"{rows}/{pending}/{mode}: main schema unchanged");
        scales.Add(new { AuditNoiseRows=rows, CommandRows=128, PendingCommands=pending, Mode=mode, SQLite=db.ServerVersion,
            PrepareMs=prepareMs, CandidateChecks=count, ElapsedMs=timer.Elapsed.TotalMilliseconds, MeanMs=timer.Elapsed.TotalMilliseconds/count,
            ApproxVmSteps=ticks*1000, VmStepSamplingInterval=1000, TimingExcludesInstrumentation=true, Plan=plan });
    }
    var prepSelect = prepareSql[prepareSql.IndexOf("SELECT json_extract", StringComparison.Ordinal)..].Trim().TrimEnd(';');
    if (rows==10000 && pending==128)
        File.WriteAllText(Path.Combine(root,"prepare-plan.json"), JsonSerializer.Serialize(Plan(db,prepSelect,false),new JsonSerializerOptions{WriteIndented=true}));
}
// Pair old/new results over duplicate owners, null owners, completed commands and misleading audit.owner.
using (var db = new SqliteConnection("Data Source=:memory:;Pooling=False"))
{
    db.Open(); Sql(db,schema); var before=MainSchema(db);
    Command(db,"p1","shared","Pending"); Command(db,"p2","shared","Pending");
    Command(db,"s1","shared","Inspected"); Command(db,"done","completed","Completed"); Command(db,"global",null,"Pending");
    Sql(db,prepareSql);
    foreach(var id in new[]{"shared","completed","runtime","unowned"})
        Check(Clear(db,oldSql,id)==Clear(db,newSql,id),"old/new semantic agreement: "+id);
    Check((long)Scalar(db,"SELECT count(*) FROM temp.parcel_retention_pending_owners")! == 1,"duplicate owners collapse; null owner ignored");
    Sql(db,"UPDATE parcel_commands SET receipt='{\"Status\":\"Inspected\"}' WHERE id='p1'"); Sql(db,prepareSql);
    Check(Clear(db,newSql,"shared")==0,"one outstanding command still protects shared owner");
    Sql(db,"UPDATE parcel_commands SET receipt='{\"Status\":\"Inspected\"}' WHERE id='p2'"); Sql(db,prepareSql);
    Check(Clear(db,newSql,"shared")==1,"next maintenance rebuild removes settled owners");
    using var backup = new SqliteConnection("Data Source=:memory:;Pooling=False"); backup.Open(); db.BackupDatabase(backup);
    Check(MainSchema(db)==before && MainSchema(backup)==before,"TEMP preparation leaves DB9/backup main schema unchanged");
    Check((long)Scalar(backup,"SELECT count(*) FROM sqlite_temp_master WHERE name='parcel_retention_pending_owners'")! == 0,"derived TEMP owner collection excluded from backup");
    var indexes = new List<object>(); using var cmd=db.CreateCommand(); cmd.CommandText="SELECT name,sql FROM sqlite_master WHERE type='index' AND tbl_name IN ('parcel_audit','parcel_commands') ORDER BY name";
    using(var r=cmd.ExecuteReader()) while(r.Read()) indexes.Add(new{Name=r.GetString(0),Sql=r.IsDBNull(1)?null:r.GetString(1)});
    File.WriteAllText(Path.Combine(root,"indexes.json"),JsonSerializer.Serialize(indexes,new JsonSerializerOptions{WriteIndented=true}));
}
File.WriteAllText(Path.Combine(root,"RESULT.json"),JsonSerializer.Serialize(new { Scales=scales, Checks=checks, Limitations="Synthetic warm in-memory SQL only; not full expiry RTO, protected CLI, target IPC or 24h/72h/field qualification" },new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"PASS {scales.Count} paired scale cases; {checks.Count} plan/ownership/schema/backup assertions");
