using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;

static class SessionShift25
{
    static void Check(bool value, string why) { if (!value) throw new InvalidDataException(why); Console.WriteLine("PASS SS25 " + why); }
    static StationReportQuery Detail(StationReportRange range = StationReportRange.CurrentShift) => new(range);
    public static async Task Run(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root,"live"), true); await h.StartAsync();
        var api = (IStationSessionStatistics)h.App;
        var first = await api.ReadSessionStatisticsAsync(Detail(),default);
        Check(first.Shift is { Status:"Open",ElapsedReliable:true } && first.Totals.Sum(x=>x.Requests)==0,"successful business startup owns one durable empty shift");
        Check((await api.ReadSessionStatisticsAsync(Detail(StationReportRange.PreviousShift),default)).Shift is null,"first run has an explicit no-previous-shift result");
        await ManagementModeFixture.Authorize(h.App); var id = first.Shift!.Id;
        await h.App.ExitManagementModeAsync(); await ManagementModeFixture.Authorize(h.App);
        Check((await api.ReadSessionStatisticsAsync(Detail(),default)).Shift!.Id==id,"management enter/exit and repeated reads never create another shift");
        var admitted = await h.App.SubmitParcelBarcodeAsync(await h.Command("left","SYNTH-SESSION-SAME"),default);
        await CameraRoundHarness.Until(async()=> (await h.App.OperationAsync(admitted.OperationId!))?.Wcs?.State==StageState.Accepted);
        var frozen=Wire.Encode((await h.App.OperationAsync(admitted.OperationId!))!.Wcs!.Request);
        h.Peer.Mode="reject";
        var rejected = await h.App.SubmitParcelBarcodeAsync(await h.Command("right","SYNTH-SESSION-SAME"),default);
        await CameraRoundHarness.Until(async()=> (await h.App.OperationAsync(rejected.OperationId!))?.Wcs?.State==StageState.Rejected);
        var two = await api.ReadSessionStatisticsAsync(Detail(),default);
        Check(two.Totals.Sum(x=>x.Requests)==2 && two.Totals.Sum(x=>x.Assigned)==1 && two.Totals.Sum(x=>x.Rejected)==1 && two.Totals.Sum(x=>x.Recognitions)==0,"two independent same-code manual requests count separately without fabricating camera or physical parcel counts");
        Check(two.Reasons.Any(x=>x.Scope=="rcs"&&x.Code=="RcsRejected"&&x.Count==1),"typed RCS rejection has its own reason group");
        var exceptions = (IStationExceptions)h.App;
        var rejectedPage = await exceptions.ReadExceptionsAsync(new(StationExceptionKind.Rejected), default);
        Check(rejectedPage.Items is [{ OperationId: not null, RequestId: not null, QuerySupported:false }] && rejectedPage.Items[0].OperationId==rejected.OperationId && rejectedPage.Items[0].ReasonCode=="RcsRejected", "exception read model retains the rejected original identity without inventing RCS query support");
        var snap=await h.App.SnapshotAsync();
        await h.App.StageConfigurationAsync(snap.Configuration.Revision,snap.Configuration.Effective with { BarcodeFilter=new("^SYNTH-.*$") },"合成班次筛选验证");
        var posts=h.Peer.Requests.Count;
        await h.App.SubmitParcelBarcodeAsync(await h.Command("left","OTHER-NO-MATCH"),default);
        var filtered=await api.ReadSessionStatisticsAsync(Detail(),default);
        Check(filtered.Reasons.Any(x=>x.Scope=="recognition"&&x.Code=="BarcodeRule"&&x.Count==1)&&h.Peer.Requests.Count==posts,"rule rejection is typed input evidence, not a fabricated RCS rejection or POST");
        var inputPage = await exceptions.ReadExceptionsAsync(new(StationExceptionKind.Observation), default);
        Check(inputPage.Items is [{OperationId:null,RequestId:null,ReasonCode:"BarcodeRule"}], "input-only rejection stays an observation without a fabricated operation or request");
        using(var cancelled=new CancellationTokenSource())
        { cancelled.Cancel(); try { await exceptions.ReadExceptionsAsync(new(StationExceptionKind.Unconfirmed),cancelled.Token); throw new Exception("cancel ignored"); } catch(OperationCanceledException){} }
        Check(true,"exception read cancellation propagates without changing business facts");
        var operationsPolicy=await h.App.ReadOperationsPolicyAsync();
        var scheduleChange=await h.App.SaveOperationsPolicyAsync((await h.App.SnapshotAsync()).Configuration.Revision,operationsPolicy with { Shifts=[new("不再支持的固定班次",0,1440)] });
        Check(scheduleChange.Status=="Rejected","fixed clock schedules cannot silently replace open-close shift semantics");
        var selection=two.SelectionId!; two.Totals[0]=two.Totals[0] with { Assigned=999 };
        var output=Path.Combine(root,"snapshot.csv"); await api.ExportSessionStatisticsAsync(selection,output,default);
        using(var receipt=JsonDocument.Parse(File.ReadAllText(output+".result.json")))
            Check(receipt.RootElement.GetProperty("Snapshot").GetProperty("Totals").EnumerateArray().Sum(x=>x.GetProperty("Requests").GetInt64())==2 && receipt.RootElement.GetProperty("Snapshot").GetProperty("Totals").EnumerateArray().Sum(x=>x.GetProperty("Assigned").GetInt64())==1,"export owns an immutable original snapshot even if a caller mutates returned arrays");
        Check(File.ReadLines(output).First()=="UTC时间,投递位,识别次数,请求次数,格口取得次数,拒绝次数,未确认次数,人工请求次数","the existing eight CSV column headings remain exact");
        try { await api.ExportSessionStatisticsAsync(selection,output,default); throw new Exception("overwritten"); } catch(IOException){}
        Check(true,"export cannot overwrite a prior CSV/receipt");
        h.App.StationReportLifetime=TimeSpan.FromMilliseconds(50); var shortLived=await api.ReadSessionStatisticsAsync(Detail(),default); await Task.Delay(100);
        var expired=Path.Combine(root,"expired.csv");
        try { await api.ExportSessionStatisticsAsync(shortLived.SelectionId!,expired,default); throw new Exception("expired accepted"); } catch(InvalidOperationException){}
        Check(!File.Exists(expired),"expired snapshot refuses before creating an output file"); h.App.StationReportLifetime=TimeSpan.FromMinutes(10);
        var hold=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);h.Peer.Hold=hold;
        var uncertain=await h.App.SubmitParcelBarcodeAsync(await h.Command("right","SYNTH-TIMEOUT"),default);
        Check(uncertain.OperationId is not null, "timeout fixture admits its explicit new original: " + Wire.Encode(uncertain));
        await CameraRoundHarness.Until(async()=> (await h.App.OperationAsync(uncertain.OperationId!))?.Wcs?.State==StageState.Unknown);
        hold.TrySetResult();h.Peer.Hold=null;
        var timeout=await api.ReadSessionStatisticsAsync(Detail(),default);
        Check(timeout.Reasons.Any(x=>x.Code=="RcsTimeout"&&x.Count==1)&&timeout.Totals.Sum(x=>x.Unknown)==1,"actual expired request budget produces a typed timeout without parsing failure text");
        var unknownPage=await exceptions.ReadExceptionsAsync(new(StationExceptionKind.Unconfirmed),default);
        Check(unknownPage.UnconfirmedCount==1&&unknownPage.Items is [{ReasonCode:"RcsTimeout"}]&&unknownPage.Items[0].OperationId==uncertain.OperationId,"typed timeout reaches exceptions with the exact original ID and full unresolved count");
        var before=h.Peer.Requests.Count; await h.App.DisposeAsync(); await h.StartAsync(); api=h.App;
        var current=await api.ReadSessionStatisticsAsync(Detail(),default);var previous=await api.ReadSessionStatisticsAsync(Detail(StationReportRange.PreviousShift),default);
        Check(current.Shift!.Id!=id && current.Totals.Sum(x=>x.Requests)==0,"a real dispose/reopen starts a new shift with zero requests");
        Check(previous.Shift is { Status:"Closed",ClosedAt:not null,ElapsedReliable:true } && previous.Shift.Id==id && previous.Totals.Sum(x=>x.Requests)==3 && previous.Totals.Sum(x=>x.Unknown)==1,"previous shift retains its original totals and unresolved responsibility after normal shutdown");
        Check(h.Peer.Requests.Count==before && Wire.Encode((await h.App.OperationAsync(admitted.OperationId!))!.Wcs!.Request)==frozen,"opening reports and another shift never replays or replaces an original request");
        var today=await api.ReadSessionStatisticsAsync(Detail(StationReportRange.Today),default);
        Check(today.Totals.Sum(x=>x.Requests)==3,"calendar totals combine both runs without changing their shift ownership");
        await Migration11(root); Attribution(root,h.Config); await Interrupted(workspace,root);
    }
    static SqliteConnection Open(string file,bool readOnly=false)
    {var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Mode=readOnly?SqliteOpenMode.ReadOnly:SqliteOpenMode.ReadWriteCreate,Pooling=false}.ToString());c.Open();return c;}
    static async Task Migration11(string root)
    {
        var folder=Path.Combine(root,"predecessor11");Directory.CreateDirectory(folder);
        var config=new SportmasterFlow().InitialConfiguration() with { Mode=RunMode.Simulation };
        var configDir=Path.Combine(folder,"config");Directory.CreateDirectory(configDir);ExtensionStore.AtomicWrite(Path.Combine(configDir,"business.json"),config);
        var launch=new BusinessLaunch(configDir,Path.Combine(folder,"data"),null,null);var directory=Path.Combine(launch.DataRoot,config.Mode.ToString(),config.StationId,"sportmaster");Directory.CreateDirectory(directory);
        var database=Path.Combine(directory,"sportmaster.db");
        using(var db=Open(database))
        {
            var baseline=(string)typeof(ParcelRepository).GetField("Baseline11",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
            Database.CreateBaseline(db,"sportmaster",11,baseline);
            using var c=db.CreateCommand();c.CommandText="INSERT INTO station_metric_buckets VALUES($minute,'left',9,4,3,1,0,1)";c.Parameters.AddWithValue("$minute",DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:00Z"));c.ExecuteNonQuery();
        }
        ExtensionStore.AtomicWrite(Path.Combine(directory,"configuration.json"),CurrentConfigurationFixture.Create<ParcelConfiguration>(3,config,null,"Effective",null));
        var configHash=Wire.FileHash(Path.Combine(directory,"configuration.json"));var before=Wire.FileHash(database);
        try { ParcelRepository.ReadStorageVersion(database,"sportmaster");throw new Exception("implicit upgrade"); } catch(InvalidDataException){}
        Check(Wire.FileHash(database)==before,"runtime refuses DB11 before writes instead of silently migrating it");
        var service=new ParcelStartupMaintenance(launch,new SportmasterFlow(),()=>new Ipc.Assembly.ParcelCapabilityFactory(launch),()=>{},()=>"synthetic-maintainer") {MaintenanceWorkspace=()=>Directory.CreateDirectory(Path.Combine(folder,"scratch-"+Wire.Id())).FullName};
        var backup=Path.Combine(folder,"original11.db");var plan=await service.PlanChangeAsync("Upgrade",backup);
        Check(plan.Allowed&&plan.Summary.Contains("事务升级至12")&&!File.Exists(backup),"offline review plans DB11 full backup and exact format12 without writing the original");
        await service.ExecuteChangeAsync(plan.Identity,"Upgrade",backup,()=>Task.CompletedTask);
        Check(ParcelRepository.ReadStorageVersion(database,"sportmaster")==12&&ParcelRepository.ReadMaintenanceStorageVersion(backup,"sportmaster")==11,"verified backup remains format11 while the live database commits format12");
        using(var db=Open(database,true))
        {using var c=db.CreateCommand();c.CommandText="SELECT count(*) FROM station_run_shifts";Check(Convert.ToInt64(c.ExecuteScalar())==0,"upgrade never fabricates historical shifts");c.CommandText="SELECT sum(recognitions) FROM station_shift_totals WHERE shift=''";Check(Convert.ToInt64(c.ExecuteScalar())==9,"legacy retained aggregate survives under an explicit unassigned scope");}
        Check(Wire.FileHash(Path.Combine(directory,"configuration.json"))==configHash,"format12 upgrade preserves exact configuration bytes");
    }
    sealed class Clock(DateTimeOffset utc):TimeProvider
    {long ticks;public override long TimestampFrequency=>TimeSpan.TicksPerSecond;public override long GetTimestamp()=>ticks;public override DateTimeOffset GetUtcNow()=>utc;public void Advance(TimeSpan value){ticks+=value.Ticks;utc+=value;}public void MoveUtc(TimeSpan value)=>utc+=value;}
    static void Attribution(string root,ParcelConfiguration config)
    {
        var folder=Path.Combine(root,"attribution");Directory.CreateDirectory(folder);using var repo=new ParcelRepository(folder,"sportmaster");
        var clock=new Clock(DateTimeOffset.UtcNow);var a=Guid.NewGuid();repo.OpenStationShift(a,clock,clock.GetUtcNow(),clock.GetTimestamp());
        var payload=Wire.Encode(new ParcelPayload("SYNTH-ORIGINAL",Wire.Id(),null,null,null));var opid=Wire.Id();var admission=Wire.Id();
        var request=new FrozenRequest(Wire.Id(),"sportmaster",opid,admission,"Wcs",config.Wcs!,"SYNTHETIC",Wire.Hash(Wire.Encode(config)),payload,Wire.Hash(payload),clock.GetUtcNow());
        var op=new ParcelOperation(opid,admission,"SYNTH-ORIGINAL",Wire.Id(),clock.GetUtcNow(),request.ConfigurationHash,config,new(){[config.Wcs!.InstanceId]="SYNTHETIC"},config.Weight,null,ParcelState.Prepared,null,new(request,StageState.Prepared,0,[]),null,null,PositionId:"left");
        repo.Commit(op,"SyntheticPrepared","Projection fixture only; not evidence of delivery");repo.CloseStationShift(true);
        var b=Guid.NewGuid();repo.OpenStationShift(b,clock,clock.GetUtcNow(),clock.GetTimestamp());
        repo.Commit(op with {State=ParcelState.ResolvingWcs,Wcs=op.Wcs! with {State=StageState.Dispatching,Attempts=1}},"SyntheticAttemptRegistered","Local intent only, not delivery");
        var policy=StationShiftSchedule.Default;
        var previous=StationSessionReportReader.Read(Path.Combine(folder,"sportmaster.db"),b.ToString("N"),Detail(StationReportRange.PreviousShift),policy,clock.GetUtcNow(),0,default);
        var current=StationSessionReportReader.Read(Path.Combine(folder,"sportmaster.db"),b.ToString("N"),Detail(),policy,clock.GetUtcNow(),0,default);
        Check(previous.Totals.Sum(x=>x.Requests)==1&&current.Totals.Sum(x=>x.Requests)==0,"even same-minute reopen and later attempt registration retain the original T1 shift");
        Check(previous.Shift!.Sequence<current.Shift!.Sequence,"shift order uses durable sequence rather than a timestamp tie");
        clock.Advance(TimeSpan.FromMinutes(2));clock.MoveUtc(TimeSpan.FromMinutes(-5));repo.CloseStationShift(true);
        using(var db=Open(Path.Combine(folder,"sportmaster.db"),true))
        {using var c=db.CreateCommand();c.CommandText="SELECT elapsed_ms,clock_adjusted,closed_at<opened_at FROM station_run_shifts WHERE id=$id";c.Parameters.AddWithValue("$id",b.ToString("N"));using var r=c.ExecuteReader();r.Read();Check(r.GetDouble(0)==120000&&r.GetInt32(1)==1&&r.GetInt32(2)==1,"normal close records two monotonic minutes despite a backwards UTC clock");}
        repo.OpenStationShift(Guid.NewGuid(),clock,clock.GetUtcNow(),clock.GetTimestamp());
        for(var i=0;i<101;i++)
        {
            var oid=Wire.Id();var aid=Wire.Id();var body=Wire.Encode(new ParcelPayload("SYNTH-PAGE-"+i,aid,null,null,null));
            var req=request with {Id=Wire.Id(),OperationId=oid,AdmissionId=aid,Payload=body,PayloadHash=Wire.Hash(body),PreparedAt=clock.GetUtcNow()};
            repo.Commit(op with {Id=oid,AdmissionId=aid,ObservationId=Wire.Id(),ParcelCode="SYNTH-PAGE-"+i,State=ParcelState.WcsUnknown,Wcs=new(req,StageState.Unknown,1,[])},"SyntheticUncertain","Pagination fixture only; no adapter/POST");
        }
        var database=Path.Combine(folder,"sportmaster.db");
        var page1=StationExceptionReader.Read(database,new(StationExceptionKind.Unconfirmed),default);
        var page2=StationExceptionReader.Read(database,new(StationExceptionKind.Unconfirmed,page1.NextSequence),default);
        Check(page1.UnconfirmedCount==101&&page1.Items.Length==100&&page1.NextSequence is not null&&page2.Items.Length==1&&page2.NextSequence is null&&page1.Items.Select(x=>x.OperationId).Intersect(page2.Items.Select(x=>x.OperationId)).Count()==0,"101 unresolved originals paginate without omission, duplicate identity or an unbounded payload list");
        repo.ValidateStationProjection();
        using(var db=Open(database)){using var c=db.CreateCommand();c.CommandText="UPDATE station_shift_totals SET requests=requests+1 WHERE shift=$id";c.Parameters.AddWithValue("$id",repo.CurrentStationShiftId);c.ExecuteNonQuery();}
        try{repo.ValidateStationProjection();throw new Exception("corrupt totals accepted");}catch(InvalidDataException){}
        Check(true,"inconsistent durable aggregates refuse business startup instead of inventing a repair from retained detail");
    }
    public static async Task CrashChild(string launchFile,string ready)
    {
        var launch=Wire.Decode<BusinessLaunch>(File.ReadAllText(launchFile));var factory=new ManualSupplyHarness.ManualFactory(launch);
        await using var app=new ParcelRuntime(launch,new SportmasterFlow(),factory){RuntimeIdentityPreflight=()=>{}};await app.StartAsync(default);
        var report=await app.ReadSessionStatisticsAsync(new(StationReportRange.CurrentShift,false),default);ExtensionStore.AtomicWrite(ready,new{Id=report.Shift!.Id});
        await Task.Delay(Timeout.Infinite);
    }
    static async Task Interrupted(string workspace,string root)
    {
        await using var h=new ManualSupplyHarness(workspace,Path.Combine(root,"hard-kill"),true);var launchFile=Path.Combine(root,"child-launch.json");var ready=Path.Combine(root,"child-ready.json");ExtensionStore.AtomicWrite(launchFile,h.Launch);
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardError=true,RedirectStandardOutput=true};
        start.ArgumentList.Add(typeof(SessionShift25).Assembly.Location);start.ArgumentList.Add(workspace);start.ArgumentList.Add("--session-shift-crash-child");start.ArgumentList.Add(launchFile);start.ArgumentList.Add(ready);
        using var child=Process.Start(start)!;
        try
        {
            for(var i=0;i<400&&!File.Exists(ready)&&!child.HasExited;i++)await Task.Delay(25);
            if(!File.Exists(ready))throw new InvalidOperationException("synthetic crash child did not open: "+(child.HasExited?await child.StandardError.ReadToEndAsync():"timeout"));
            var id=JsonDocument.Parse(File.ReadAllText(ready)).RootElement.GetProperty("Id").GetString();child.Kill(true);await child.WaitForExitAsync();
            await h.StartAsync();var report=await ((IStationSessionStatistics)h.App).ReadSessionStatisticsAsync(Detail(StationReportRange.PreviousShift),default);
            Check(report.Shift?.Id==id&&report.Shift is{Status:"Interrupted",ClosedAt:null,ElapsedReliable:false}&&report.EffectiveElapsedMilliseconds is null,"actual process kill leaves an interrupted prior shift without a fabricated normal end or mean speed");
            Check(h.Peer.Requests.IsEmpty,"crash recovery and shift inspection send no production request");
        }
        finally{if(!child.HasExited){child.Kill(true);await child.WaitForExitAsync();}}
    }
}
