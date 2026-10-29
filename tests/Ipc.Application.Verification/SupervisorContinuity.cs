using System.Diagnostics;
using System.Text.Json.Nodes;
using Ipc.Platform;

static class SupervisorContinuity
{
    static void Check(bool yes, string message) { if (!yes) throw new Exception("FAIL WT024 " + message); Console.WriteLine("PASS WT024 " + message); }
    static DeploymentLocation Location(string root) => new("sportmaster", new string('a',40), Path.Combine(root,"release"), Path.Combine(root,"config"), Path.Combine(root,"data"), "synthetic-account")
        { Recovery = new(Enabled:true, RestartDelaySeconds:1, MaximumRestarts:2, HealthyResetSeconds:60) };
    static string StatePath(DeploymentLocation l) => Path.Combine(DesktopRecoverySupervisor.StateDirectory(l), "state.json");
    static Process Start(string root, string mode)
    {
        var s = new ProcessStartInfo("dotnet") { UseShellExecute=false, CreateNoWindow=true };
        foreach (var a in new[] { typeof(SupervisorContinuity).Assembly.Location, root, "--supervisor-continuity-child", mode }) s.ArgumentList.Add(a);
        var p = Process.Start(s)!; _ = p.Handle;
        var watch = Stopwatch.StartNew();
        while (!p.HasExited && p.MainModule?.FileName is null && watch.Elapsed < TimeSpan.FromSeconds(2)) Thread.Sleep(5);
        return p;
    }
    public static async Task Child(string root, string mode)
    {
        if (mode == "normal") { await Task.Delay(400); return; }
        if (mode == "permanent") { await Task.Delay(400); Environment.Exit(78); return; }
        if (mode == "fault")
        {
            File.WriteAllText(Path.Combine(root,"child-ready"), Environment.ProcessId.ToString());
            while (!File.Exists(Path.Combine(root,"exit-signal"))) await Task.Delay(20);
            Environment.Exit(78); return;
        }
        var l = Location(root);
        Environment.ExitCode = await DesktopRecoverySupervisor.RunAsync(l, () => l, launch: _ =>
        {
            var prior = File.Exists(Path.Combine(root,"launches")); File.AppendAllText(Path.Combine(root,"launches"), "launch\n");
            // The verification parent owns the fault process so waitpid/ExitCode remain observable on Unix after this supervisor dies.
            if (!prior && File.Exists(Path.Combine(root,"observer-owned-child"))) return Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(root,"observer-owned-child"))));
            return Start(root, prior ? "normal" : "fault");
        });
    }
    static bool Refused(Action action)
    { try { action(); return false; } catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or System.Text.Json.JsonException) { return true; } }
    public static async Task Run(string root)
    {
        Directory.CreateDirectory(root);
        foreach (var mode in new[] { "missing-directory", "missing-file", "invalid-json", "oversize", "invalid-running", "invalid-new", "locked" })
        {
            var l=Location(Path.Combine(root,mode)); var file=StatePath(l);
            if (mode != "missing-directory") Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (mode is not ("missing-directory" or "missing-file")) File.WriteAllText(file, mode switch
            {
                "invalid-json" => "{broken original ledger", "oversize" => new string('x',16385),
                "invalid-running" => "{\"Launches\":2,\"Status\":\"Running\"}",
                "invalid-new" => "{\"Launches\":2,\"Status\":\"New\"}",
                _ => "{\"Launches\":0,\"Status\":\"New\"}"
            });
            var before = File.Exists(file) ? File.ReadAllBytes(file) : null;
            using (var locked = mode == "locked" ? new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None) : null)
            {
                var result=await DesktopRecoverySupervisor.RunAsync(l,()=>l,launch:_=>throw new Exception("Unexpected launch"));
                Check(result==77 && Refused(()=>DesktopRecoverySupervisor.ResetAfterAuthorizedRepair(l)), mode+" blocks launch and repair without inventing a budget");
            }
            Check(before is null ? !File.Exists(file) : File.ReadAllBytes(file).SequenceEqual(before), mode+" original ledger bytes/existence preserved");
            if(mode=="missing-directory") Check(!Directory.Exists(DesktopRecoverySupervisor.StateDirectory(l)),"missing parent is not recreated by lease or diagnostics");
            if(mode=="missing-file") Check(Refused(()=>DesktopRecoverySupervisor.InitializeNewDeployment(l)),"existing empty directory is not proof of a new deployment");
        }
        foreach(var mode in new[]{"normal","permanent"})
        {
            var l=Location(Path.Combine(root,"diagnostic-"+mode)); DesktopRecoverySupervisor.InitializeNewDeployment(l);
            Directory.CreateDirectory(Path.Combine(DesktopRecoverySupervisor.StateDirectory(l),"alert.json"));
            Directory.Move(Path.Combine(DesktopRecoverySupervisor.StateDirectory(l),"diagnostics"),Path.Combine(DesktopRecoverySupervisor.StateDirectory(l),"saved-diagnostics"));
            File.WriteAllText(Path.Combine(DesktopRecoverySupervisor.StateDirectory(l),"diagnostics"),"retained original obstruction");
            var count=0;var exit=await DesktopRecoverySupervisor.RunAsync(l,()=>l,launch:_=>{count++;return Start(root,mode);});
            var s=JsonNode.Parse(File.ReadAllText(StatePath(l)))!;
            Check(count==1 && exit==(mode=="normal"?0:77) && (string?)s["Status"]==(mode=="normal"?"Exited":"Blocked"), mode+" observed exit remains authoritative when diagnostics fail");
        }
        {
            var l=Location(Path.Combine(root,"running-save-failure"));DesktopRecoverySupervisor.InitializeNewDeployment(l);Process? observer=null;
            var injected=false;
            DesktopRecoverySupervisor.BeforePersistForVerification=status=>{if(status=="Running"&&!injected){injected=true;throw new IOException("Synthetic state publication failure");}};
            try
            {
                var exit=await DesktopRecoverySupervisor.RunAsync(l,()=>l,launch:_=>{var p=Start(root,"fault");observer=Process.GetProcessById(p.Id);var retainedHandle=observer.Handle;return p;});
                var state=JsonNode.Parse(File.ReadAllText(StatePath(l)))!;
                var projected=false;
                Check(exit==77 && observer is {HasExited:false} && (int?)state["ChildPid"]==observer.Id && (bool?)state["LaunchUncertain"]==true &&
                    Refused(()=>DesktopRecoverySupervisor.RepairAndReset(l,()=>projected=true)) && !projected,"failed Running publication retains live child identity and refuses repair before projections");
            }
            finally { DesktopRecoverySupervisor.BeforePersistForVerification=null; if(observer is not null){if(!observer.HasExited)observer.Kill();await observer.WaitForExitAsync();observer.Dispose();} }
        }
        foreach (var status in new[] { "New", "Exited", "Blocked" })
        {
            var source=Location(Path.Combine(root,"migration-"+status));DesktopRecoverySupervisor.InitializeNewDeployment(source);
            if(status!="New")ExtensionStore.AtomicWrite(StatePath(source),new {Launches=2,Status=status,SourceCommit=new string('a',40),ExecutableSha256=new string('b',64)});
            var original=File.ReadAllBytes(StatePath(source));var targetRoot=Path.Combine(root,"migration-target-"+status);Directory.CreateDirectory(targetRoot);
            using(var held=DesktopRecoverySupervisor.HoldMigration(source,targetRoot))
            {
                held.Require(source,targetRoot);
                Check(File.ReadAllBytes(StatePath(source with {DataRoot=targetRoot})).SequenceEqual(original) && File.ReadAllBytes(StatePath(source)).SequenceEqual(original),status+" migration preserves exact original ledger bytes");
                Check(Refused(()=>{using var other=new ResourceLease(DesktopRecoverySupervisor.StateDirectory(source),["supervisor"]);}),"migration retains exclusive original supervision until registration can finish");
            }
            Check(DesktopRecoverySupervisor.IsBlocked(source with {DataRoot=targetRoot})==(status=="Blocked"),"migration does not reset a blocked budget");
        }
        {
            var source=Location(Path.Combine(root,"migration-uncertain"));DesktopRecoverySupervisor.InitializeNewDeployment(source);
            ExtensionStore.AtomicWrite(StatePath(source),new {Launches=1,Status="Launching",SourceCommit=new string('a',40)});
            var target=Path.Combine(root,"migration-uncertain-target");Directory.CreateDirectory(target);var invoked=false;
            Check(Refused(()=>{using var held=DesktopRecoverySupervisor.HoldMigration(source,target);invoked=true;})&&!invoked&&!Directory.Exists(DesktopRecoverySupervisor.StateDirectory(source with {DataRoot=target})),"uncertain supervision refuses migration before business handoff or target publication");
        }
        {
            var source=Location(Path.Combine(root,"migration-disabled")) with {Recovery=new()};var target=Path.Combine(root,"migration-disabled-target");Directory.CreateDirectory(target);
            using(var held=DesktopRecoverySupervisor.HoldMigration(source,target)) {held.Require(source,target);Check(held.LedgerSha256 is null,"disabled absent supervision migrates without fabricating a ledger");}
            DesktopRecoverySupervisor.InitializeNewDeployment(source with {DataRoot=target});
            Check(Refused(()=>{using var held=DesktopRecoverySupervisor.HoldMigration(source,target);}),"disabled migration refuses unowned target supervision evidence");
        }
        await LostOwner(Path.Combine(root,"lost-owner"));
    }
    static async Task LostOwner(string root)
    {
        Directory.CreateDirectory(root); var l=Location(root);DesktopRecoverySupervisor.InitializeNewDeployment(l);
        Process? watcher=null,child=null;
        try
        {
            child=Start(root,"fault");File.WriteAllText(Path.Combine(root,"observer-owned-child"),child.Id.ToString());
            watcher=Start(root,"watcher");
            var deadline=Stopwatch.StartNew();
            while(!File.Exists(Path.Combine(root,"child-ready")) || !File.ReadAllText(StatePath(l)).Contains("Running"))
            { if(watcher.HasExited||deadline.Elapsed>TimeSpan.FromSeconds(10))throw new Exception("Watcher did not acknowledge original child");await Task.Delay(20); }
            Check(child.Id==int.Parse(File.ReadAllText(Path.Combine(root,"child-ready"))),"independent observer retains the actual original process handle");
            watcher.Kill();await watcher.WaitForExitAsync();File.WriteAllText(Path.Combine(root,"exit-signal"),"synthetic permanent storage fault");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));Check(child.ExitCode==78,"original child actually exits 78 after supervisor loss");child.Dispose();child=null;
            var before=JsonNode.Parse(File.ReadAllText(StatePath(l)))!;
            using var replacement=Start(root,"watcher");await replacement.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var after=JsonNode.Parse(File.ReadAllText(StatePath(l)))!;
            Check(replacement.ExitCode==77 && File.ReadAllLines(Path.Combine(root,"launches")).Length==1,"replacement supervisor does not create a second client after losing the original exit observation");
            Check(new[]{"Launches","ChildPid","ChildStartedTicks","SourceCommit","ExecutableSha256"}.All(k=>JsonNode.DeepEquals(before[k],after[k])) && (bool?)after["LaunchUncertain"]==true &&
                Refused(()=>DesktopRecoverySupervisor.ResetAfterAuthorizedRepair(l)),"lost exit retains original identity/count and cannot be reset as a known outcome");
        }
        finally { if(watcher is not null){if(!watcher.HasExited){watcher.Kill();await watcher.WaitForExitAsync();}watcher.Dispose();}if(child is not null){if(!child.HasExited){child.Kill();await child.WaitForExitAsync();}child.Dispose();} }
    }
}
