using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Text.Json;
SQLitePCL.Batteries_V2.Init();
var root=args[0]; var results=new List<object>();
foreach(var rows in new[]{10000,100000}) {
 using var db=new SqliteConnection("Data Source=:memory:"); db.Open();
 void Sql(string s){using var c=db.CreateCommand();c.CommandText=s;c.ExecuteNonQuery();}
 Sql(File.ReadAllText(Path.Combine(root,"schema.sql")));
 Sql("WITH RECURSIVE n(x) AS(SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<"+rows+") INSERT INTO parcel_audit(at,event,owner,detail) SELECT '2020-01-01','Noise','runtime','{}' FROM n");
 // A different operation owns a real pending command; no global skip is allowed.
 Sql("""INSERT INTO parcel_commands VALUES('pending','hash','{"Status":"Pending"}'); INSERT INTO parcel_audit(at,event,owner,detail) VALUES('2020-01-01','CommandAdmitted','runtime','{"Id":"pending","Owner":"other"}')""");
 foreach(var mode in new[]{"old","new"}) {
  var timer=Stopwatch.StartNew(); if(mode=="new") Sql(File.ReadAllText(Path.Combine(root,"prepare.sql")));
  var prepareMs=timer.Elapsed.TotalMilliseconds;
  using var c=db.CreateCommand();var sql=File.ReadAllText(Path.Combine(root,mode+".sql"));c.CommandText="EXPLAIN QUERY PLAN "+sql;
  c.Parameters.AddWithValue("$id","candidate");c.Parameters.AddWithValue("$cutoff","2030-01-01");
  var plan=new List<string>();using(var r=c.ExecuteReader())while(r.Read())plan.Add(r.GetString(3));
  c.CommandText=sql;timer.Restart();for(var i=0;i<100;i++)if((long)c.ExecuteScalar()!=1)throw new Exception("Unexpected blocked candidate");
  c.Parameters["$id"].Value="other";if((long)c.ExecuteScalar()!=0)throw new Exception("Pending owner lost its guard");
  results.Add(new{Rows=rows,Mode=mode,SQLite=db.ServerVersion,PreparationMs=prepareMs,Checks=100,CheckMs=timer.Elapsed.TotalMilliseconds,Plan=plan,PendingOwnerPreserved=true});
 }
}
File.WriteAllText(Path.Combine(root,"RESULT.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(results));
