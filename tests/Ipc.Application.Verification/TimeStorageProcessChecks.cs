using System.Diagnostics;
using Ipc.Platform;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Microsoft.Data.Sqlite;

static class TimeStorageProcessChecks
{
    public static async Task Supervisor(string root)
    {
        var location = new DeploymentLocation("sportmaster", new string('a', 40), Path.Combine(root, "release"), Path.Combine(root, "config"), Path.Combine(root, "data"), "synthetic-account")
        { Recovery = new(RestartDelaySeconds: 1, MaximumRestarts: 2, HealthyResetSeconds: 60) };
        var launches = 0;
        Process Launch()
        {
            launches++; var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { typeof(TimeStorageProcessChecks).Assembly.Location, root, "--time-storage-exit-child", "78" }) start.ArgumentList.Add(arg);
            return Process.Start(start)!;
        }
        var result = await DesktopRecoverySupervisor.RunAsync(location, () => location, launch: _ => Launch());
        if (result != 77 || launches != 1) throw new Exception("FAIL ST08 permanent storage exit must block after one launch; actual=" + launches);
        Console.WriteLine("PASS ST08 permanent storage failure blocks supervision after one launch");
    }

    sealed class Factory(BusinessLaunch launch) : IParcelCapabilityFactory
    {
        readonly CommercialProductization.SyntheticFactory inner = new(launch);
        public int Calls;
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public void ValidateStructure(ParcelConfiguration c) => inner.ValidateStructure(c);
        public ParcelPorts Create(ParcelConfiguration c) { var ports = inner.Create(c); return ports with { Wcs = new Unknown(ports.Wcs!, this) }; }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
        sealed class Unknown(IWcsPort inner, Factory owner) : IWcsPort
        {
            public string InstanceId => inner.InstanceId;
            public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
            public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
            public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
            public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
            public ValueTask DisposeAsync() => inner.DisposeAsync();
            public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => throw new Exception("Unexpected query");
            public Task<StageReply> ResolveSlotAsync(FrozenRequest r, CancellationToken t)
            { Interlocked.Increment(ref owner.Calls); throw new TimeoutException("synthetic original response lost"); }
        }
    }
    public static async Task Child(string workspace, string root, string mode)
    {
        Directory.CreateDirectory(root);
        if (mode != "recover")
        {
            var source = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json"))) with { Login = null, BackupSchedule = null };
            ExtensionStore.AtomicWrite(Path.Combine(root, "business.json"), source);
        }
        var launch = new BusinessLaunch(root, Path.Combine(root, "data"), null, null);
        var factory = new Factory(launch);
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } };
        using var diagnostics = new CrashDiagnostics(Path.Combine(root, "fatal-diagnostics"), "synthetic-ST08", typeof(TimeStorageProcessChecks).Assembly.GetCustomAttributes(false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion);
        using var stop = new CancellationTokenSource();
        var monitor = RuntimeFatalMonitor.RunAsync(app, diagnostics, stop.Token);
        await app.StartAsync(default);
        if (mode == "recover")
        {
            var snapshot = await app.SnapshotAsync(); var op = snapshot.Operations.Single();
            if (factory.Calls != 0 || op.State != ParcelState.WcsUnknown || Wire.Encode(op.Wcs!.Request) != File.ReadAllText(Path.Combine(root, "frozen-request.json")))
                throw new Exception("Recovery changed Unknown/original request or replayed a provider call");
            var c = snapshot.Configuration.Effective;
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(launch.DataRoot, "Simulation", c.StationId, c.ApplicationId, "sportmaster.db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
            if (File.Exists(Path.Combine(root, "expect-interrupted")))
            {
                using var query = db.CreateCommand(); query.CommandText = "SELECT count(*) FROM parcel_ingress WHERE state='Interrupted'";
                if (Convert.ToInt64(query.ExecuteScalar()) != 1) throw new Exception("Committed Pending input was not retained as Interrupted");
            }
            stop.Cancel(); await monitor;
            Console.WriteLine("PASS ST08 restarted Unknown/frozen request retained; provider calls=0; committed input not replayed"); return;
        }
        var wait = Stopwatch.StartNew();
        while (!(await app.SnapshotAsync()).Positions!.Any(p => p.Id == "left" && p.Admit))
        { if (wait.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException("Synthetic reader readiness"); await Task.Delay(10); }
        app.EmitReferenceAt("left", "synthetic-storage-unknown", "000123", Wire.Id());
        ParcelOperation? original;
        while ((original = (await app.SnapshotAsync()).Operations.SingleOrDefault())?.State != ParcelState.WcsUnknown)
        { if (wait.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException("Synthetic Unknown"); await Task.Delay(10); }
        File.WriteAllText(Path.Combine(root, "frozen-request.json"), Wire.Encode(original.Wcs!.Request));
        if (factory.Calls != 1) throw new Exception("Expected one original provider call");
        if (mode == "ambiguous") File.WriteAllText(Path.Combine(root, "expect-interrupted"), "original input must remain");
        app.Boundary = point =>
        {
            if (mode == "permanent" && point == "BeforeIngressDurable") throw new SqliteException("synthetic disk full", 13);
            if (mode == "budget" && point == "BeforeIngressDurable") throw new IOException("synthetic persistent pre-write I/O");
            if (mode == "ambiguous" && point == "AfterIngressDurable") throw new IOException("synthetic committed acknowledgement lost");
        };
        app.EmitReferenceAt("left", "synthetic-storage-fault-input", "000124", Wire.Id());
        await Task.WhenAny(monitor, Task.Delay(TimeSpan.FromSeconds(12)));
        throw new Exception("Storage fatal monitor did not terminate the child within its budget");
    }
    public static async Task Processes(string workspace, string root)
    {
        foreach (var mode in new[] { "budget", "ambiguous", "permanent" })
        {
            var childRoot = Path.Combine(root, mode); var launches = 0;
            var location = new DeploymentLocation("sportmaster", new string('a', 40), Path.Combine(childRoot, "release"), childRoot, Path.Combine(childRoot, "data"), "synthetic-account")
            { Recovery = new(RestartDelaySeconds: 1, MaximumRestarts: 2, HealthyResetSeconds: 60) };
            Process Launch()
            {
                var scenario = launches++ == 0 ? mode : "recover";
                var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in new[] { typeof(TimeStorageProcessChecks).Assembly.Location, workspace, "--time-storage-runtime-child", childRoot, scenario }) start.ArgumentList.Add(arg);
                return Process.Start(start)!;
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var elapsed = Stopwatch.StartNew();
            var result = await DesktopRecoverySupervisor.RunAsync(location, () => location, launch: _ => Launch(), cancellationToken: deadline.Token);
            if (mode == "permanent" ? result != 77 || launches != 1 : result != 0 || launches != 2)
                throw new Exception("FAIL ST08 process closure " + mode + "; launches=" + launches + "; supervisor=" + result);
            var text = string.Join("\n", Directory.GetFiles(Path.Combine(childRoot, "fatal-diagnostics"), "*", SearchOption.AllDirectories).Select(File.ReadAllText));
            if (!text.Contains("Storage.IngressCustody.") || !text.Contains("SourceCommit") || !text.Contains("Epochs") || !text.Contains("RecentState"))
                throw new Exception("Missing flushed version/commit/instance/epoch/state fatal storage diagnostic");
            if (mode == "permanent" && !File.Exists(Path.Combine(DesktopRecoverySupervisor.StateDirectory(location), "alert.json"))) throw new Exception("Missing permanent failure alert");
            Console.WriteLine("PASS ST08 process closure " + mode + "; launches=" + launches + "; supervisor=" + result + "; elapsedMs=" + elapsed.ElapsedMilliseconds);
        }
    }
}
