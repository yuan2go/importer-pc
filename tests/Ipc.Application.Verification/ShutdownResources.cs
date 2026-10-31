using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static partial class SportmasterRcs
{
    public static async Task ShutdownResources(string workspace, string root)
    {
        await using var peer = new Peer();
        var seed = HeartbeatConfiguration(workspace, Binding(peer.Endpoint) with { Implementation = "sportmaster-rcs" });
        var readers = seed.Topology!.Readers.Select((r, i) => r with { Driver = "hikrobot-mvs-reader", Parameters = new()
        { ["device.ip"] = "192.0.2." + (11 + i), ["device.serial"] = "SYNTH-SHUTDOWN-" + i, ["entity.mode"] = "read-result", ["acquisition.trigger"] = "off" } }).ToArray();
        var config = seed with { Reader = readers[0], Topology = seed.Topology with { Readers = readers }, Login = null,
            DeferredConnections = [readers[1].DeviceId], BarcodeFilter = new("") };
        foreach (var (directDispose, failBeforeStop) in new[] { (false, false), (true, false), (false, true) })
        {
            var caseRoot = Path.Combine(root, failBeforeStop ? "failure-before-stop" : directDispose ? "dispose" : "stop-then-dispose");
            var configRoot = Path.Combine(caseRoot, "config"); Directory.CreateDirectory(configRoot);
            ExtensionStore.AtomicWrite(Path.Combine(configRoot, "business.json"), config);
            var launch = new BusinessLaunch(configRoot, Path.Combine(caseRoot, "data"), null, null);
            var directory = Path.Combine(launch.DataRoot, config.Mode.ToString(), config.StationId, config.ApplicationId);
            var api = new SoftwareFrameApi(readers[0]); var decoder = new OriginalDecoder();
            var factory = new SoftwareFlowFactory(launch, config, api, false, decoder);
            var app = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } };
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var failures = 0;
            app.Boundary = point =>
            {
                if (point == "BeforeOriginalFrameWrite") { entered.Set(); release.Wait(); }
                if (point == "BeforeBusinessCommit:OriginalFrameSaved" && Interlocked.Increment(ref failures) == 1)
                    throw new IOException("Synthetic one-shot original status commit failure");
            };
            try
            {
                await app.StartAsync(default); await ManagementModeFixture.Authorize(app);
                await Wait(app, s => s.Positions!.Single(p => p.Id == "left").Admit);
                decoder.Results.Enqueue(["SYNTH-SHUTDOWN"]); api.Enqueue(new(new byte[] { 30, 70, 140, 210 }, 2, 2), 1);
                Check(entered.Wait(5000), "shutdown writer owns the original copied pixels");
                var original = (await Wait(app, s => s.Operations.SingleOrDefault()?.State == ParcelState.SlotResolved)).Operations.Single();
                var requestCount = peer.Requests.Count;
                if (failBeforeStop)
                {
                    release.Set();
                    await Until(() => Volatile.Read(ref failures) == 1);
                    // Observe two completed maintenance cycles after the fault through
                    // the public progress surface, so normal task pruning runs first.
                    for (var cycle = 0; cycle < 2; cycle++)
                    {
                        var after = DateTimeOffset.UtcNow;
                        await Until(() => System.Text.Json.JsonSerializer.SerializeToElement(app.RuntimeProgress())
                            .GetProperty("Background").EnumerateArray().Any(p => p.GetProperty("Name").GetString() == "maintenance" &&
                                p.GetProperty("LastProgress").ValueKind == System.Text.Json.JsonValueKind.String &&
                                p.GetProperty("LastProgress").GetDateTimeOffset() > after));
                    }
                }
                var closing = directDispose ? app.DisposeAsync().AsTask() : app.StopAsync(default);
                await Until(() => factory.Source!.Phase == "stopped");
                if (!failBeforeStop)
                    Check(!closing.IsCompleted && app.HasUnreleasedResources && factory.Source!.RetainedOriginalFrames == 1,
                        "shutdown drains actual image work before releasing the application owner");
                // In the two in-flight cases maintenance stops before the fault;
                // the third case has already allowed ordinary pruning to run.
                release.Set();
                try { await closing.WaitAsync(TimeSpan.FromSeconds(5)); throw new Exception("Background failure was lost"); }
                catch (InvalidOperationException error)
                { Check(error.ToString().Contains("Synthetic one-shot original status commit failure"), "completed background failure remains visible to the lifecycle caller"); }
                app.Boundary = null;
                if (!directDispose)
                {
                    Check(app.HasUnreleasedResources && factory.Source!.RetainedOriginalFrames == 0, "stopped faulted work retains the application until real disposal");
                    try { await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); throw new Exception("Original background failure was lost during disposal"); }
                    catch (InvalidOperationException error)
                    { Check(error.ToString().Contains("Synthetic one-shot original status commit failure"), "disposal preserves the prior stop failure after completing cleanup"); }
                }
                Check(!app.HasUnreleasedResources && api.Calls.TakeLast(4).SequenceEqual(["stop", "close", "destroy", "finalize"]) && failures == 1,
                    "a failed completed task cannot strand native resources or database leases");
                var teardownCalls = api.Calls.ToArray();
                await app.DisposeAsync();
                Check(!app.HasUnreleasedResources && api.Calls.SequenceEqual(teardownCalls),
                    "repeated disposal succeeds without replaying the completed task or double releasing resources");
                using var repository = new ParcelRepository(directory, "sportmaster");
                var saved = repository.ReadOperation(original.Id)!;
                Check(Wire.Encode(saved.Wcs) == Wire.Encode(original.Wcs) && saved.OriginalFrame?.State == "Pending" &&
                    File.Exists(Path.Combine(directory, original.OriginalFrame!.RelativeFile)) && peer.Requests.Count == requestCount,
                    "released database reopens with original request and uncertain image status intact, without resend or fabricated archive success");
            }
            finally { release.Set(); if (app.HasUnreleasedResources) await app.DisposeAsync(); }
        }
        Console.WriteLine("SHUTDOWN_RESOURCES_PASS; synthetic SDK/decoder; actual SQLite/PNG/HTTP; hardware NOT_RUN");
    }
}
