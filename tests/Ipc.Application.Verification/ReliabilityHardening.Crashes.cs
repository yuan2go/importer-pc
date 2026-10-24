using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;

static partial class ReliabilityHardening
{
    public static async Task CrashChild(string workspace, string path, string cut, string endpoint)
    {
        var c = Config(workspace); var launch = Launch(path, c);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var factory = new WrappedFactory(new(launch), p => new WrappedWcs(p)
        {
            Resolve = async (request, token) =>
            {
                if (request.AdmissionId == "left-original" && cut == "BeforeExternal") Cut(cut);
                using var response = await client.PostAsync(endpoint, new StringContent(Wire.Encode(request), Encoding.UTF8, "application/json"), token);
                response.EnsureSuccessStatusCode();
                var reply = Wire.Decode<StageReply>(await response.Content.ReadAsStringAsync(token));
                if (request.AdmissionId == "left-original" && cut == "AfterExternal") Cut(cut);
                return reply;
            }
        });
        // Intentionally not await-using: the controller must kill the process while
        // an actual business frame is blocked. A normal exit fails the test.
        var app = new ParcelRuntime(launch, new SportmasterFlow(), factory);
        await app.StartAsync(default);
        Check(app.EmitReferenceAt("right", "right-control", "002", "right-event"), "right control durable handoff");
        await Until(() => app.WorkerProgress().Single(p => p.Position == "right").Processed == 1);
        app.Boundary = point => { if (point == cut) Cut(cut); };
        var accepted = app.EmitReferenceAt("left", "left-original", "001", "left-event");
        Console.WriteLine("HANDOFF left=" + accepted); Console.Out.Flush();
        await Task.Delay(TimeSpan.FromSeconds(30));
        throw new Exception("External controller failed to kill the child at " + cut);
    }
    static void Cut(string cut)
    {
        Console.WriteLine("CUT " + cut); Console.Out.Flush();
        using var blocked = new ManualResetEventSlim();
        if (!blocked.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("External controller did not terminate process");
    }
    public static async Task Crashes(string workspace, string root)
    {
        foreach (var cut in new[] { "AfterIngressDurable", "AfterBusinessCommit:IntentPrepared", "AfterBusinessCommit:Dispatching", "BeforeExternal", "AfterExternal", "AfterRawFileDurable:wcs-slot", "AfterRawDurable", "BeforeBusinessCommit:ReceiptApplied", "AfterBusinessCommit:ReceiptApplied" })
            await Crash(workspace, Path.Combine(root, cut.Replace(':', '-')), cut);
    }
    static async Task Crash(string workspace, string path, string cut)
    {
        Directory.CreateDirectory(path);
        // Source evidence belongs to the controller, not the process under test.
        File.WriteAllText(Path.Combine(path, "source.json"), Wire.Encode(new[] {
            new { Position = "right", Admission = "right-control", Code = "002", Event = "right-event" },
            new { Position = "left", Admission = "left-original", Code = "001", Event = "left-event" } }));
        using var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
        var endpoint = "http://127.0.0.1:" + port + "/";
        using var listener = new HttpListener(); listener.Prefixes.Add(endpoint); listener.Start();
        using var serverStop = new CancellationTokenSource();
        var received = new List<FrozenRequest>();
        var server = Task.Run(async () =>
        {
            while (!serverStop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync().WaitAsync(serverStop.Token); }
                catch (OperationCanceledException) when (serverStop.IsCancellationRequested) { break; }
                using var input = new StreamReader(context.Request.InputStream);
                var request = Wire.Decode<FrozenRequest>(await input.ReadToEndAsync());
                lock (received) received.Add(request);
                using (var record = new FileStream(Path.Combine(path, "server-received.jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read))
                { record.Write(Encoding.UTF8.GetBytes(Wire.Encode(request) + "\n")); record.Flush(true); }
                var reply = new StageReply(Wire.Id(), request.Id, request.PayloadHash, true, true, true, null, "SYN-01", "Controlled loopback server; not RCS qualification", DateTimeOffset.UtcNow);
                var bytes = Encoding.UTF8.GetBytes(Wire.Encode(reply)); context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close();
            }
        });
        var start = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var arg in new[] { workspace, "--h02-crash-child", path, cut, endpoint }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Child not started");
        var hit = Signal(); var output = new StringBuilder();
        var stdout = Task.Run(async () => { while (await process.StandardOutput.ReadLineAsync() is { } line) { output.AppendLine(line); if (line == "CUT " + cut) hit.TrySetResult(); } });
        var stderr = process.StandardError.ReadToEndAsync(); var watch = Stopwatch.StartNew();
        try
        {
            await hit.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Check(!process.HasExited, cut + " controller observed live process blocked inside actual boundary");
            process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await stdout; File.WriteAllText(Path.Combine(path, "child.log"), output + await stderr);
            Check(process.ExitCode != 0, cut + " terminated externally without orderly Dispose");
            var c = Config(workspace); var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null);
            var database = Path.Combine(Data(launch, c), "sportmaster.db");
            Check(Scalar(database, "SELECT count(*) FROM parcel_ingress") == 2, cut + " independent left/right inputs both retain durable receipt identities");
            using (var originals = new ParcelRepository(Data(launch, c), "sportmaster", 9))
                File.WriteAllText(Path.Combine(path, "before-recovery.json"), Wire.Encode(originals.Load()));
            int calls = 0;
            var factory = new WrappedFactory(new(launch), p => new WrappedWcs(p) { Resolve = (r, t) => { Interlocked.Increment(ref calls); throw new Exception("Recovery attempted external resend"); } });
            await using var recovered = new ParcelRuntime(launch, new SportmasterFlow(), factory);
            await recovered.StartAsync(default); await ManagementModeFixture.Authorize(recovered);
            var snapshot = await recovered.SnapshotAsync();
            Check(snapshot.Paused && calls == 0, cut + " recovery pauses for original input inspection without external resend");
            Check(snapshot.Operations.Single(o => o.PositionId == "right").Wcs is { State: StageState.Accepted, Attempts: 1 }, cut + " right accepted control survives unchanged");
            var left = snapshot.Operations.SingleOrDefault(o => o.PositionId == "left");
            var hasRaw = cut.StartsWith("AfterRaw", StringComparison.Ordinal) || cut.Contains("ReceiptApplied", StringComparison.Ordinal);
            Check(cut == "AfterIngressDurable" ? left is null : hasRaw ? left?.Wcs is { State: StageState.Accepted, Attempts: 1 } :
                cut == "AfterBusinessCommit:IntentPrepared" ? left?.Wcs is { State: StageState.Prepared, Attempts: 0 } : left?.Wcs is { State: StageState.Unknown, Attempts: 1 },
                cut + " no invented acceptance; only original durable raw converges locally");
            if (left?.Wcs is { } stage)
            {
                FrozenRequest[] leftReceived; lock (received) leftReceived = received.Where(r => r.AdmissionId == "left-original").ToArray();
                Check(leftReceived.Length == (cut == "AfterExternal" || hasRaw ? 1 : 0), cut + " service-side receive count matches the selected crash window");
                if (leftReceived.Length == 1) Check(Wire.Encode(stage.Request) == Wire.Encode(leftReceived[0]), cut + " exact original request ID/body/hash/target/contract preserved");
            }
            Check(Scalar(database, "SELECT count(*) FROM parcel_ingress WHERE state='Interrupted' AND position='left'") == 1,
                cut + " interrupted left handoff remains inspectable even after business T3");
            await recovered.DisposeAsync();
            File.WriteAllText(Path.Combine(path, "result.json"), Wire.Encode(new { Cut = cut, SourceCommit = typeof(ParcelRuntime).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                ProcessId = process.Id, process.ExitCode, Milliseconds = watch.Elapsed.TotalMilliseconds, ExternalCallsOnRecovery = calls, Snapshot = snapshot }));
            Console.WriteLine($"MEASURE H02 crash={cut}; elapsed_ms={watch.Elapsed.TotalMilliseconds:F2}; input_receipts=2; recovery_external_calls={calls}");
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            await stdout; File.WriteAllText(Path.Combine(path, "child.log"), output + await stderr);
            serverStop.Cancel(); await server; listener.Stop();
        }
    }
}
