using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static class LatencyProjectionChecks
{
    public static async Task Run(string workspace, string root)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
        var config = Path.Combine(root, "config"); Directory.CreateDirectory(config);
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        File.WriteAllText(Path.Combine(config, "business.json"), Wire.Encode(c));
        var launch = new BusinessLaunch(config, Path.Combine(root, "data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default);
        const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        var gate = (SemaphoreSlim)typeof(ParcelRuntime).GetField("gate", hidden)!.GetValue(runtime)!;
        var repo = (ParcelRepository)typeof(ParcelRuntime).GetField("repository", hidden)!.GetValue(runtime)!;
        var id = Wire.Id();
        var op = new ParcelOperation(id, "synthetic-admission", "SYNTHETIC", "synthetic-observation", DateTimeOffset.UtcNow, Wire.Hash(Wire.Encode(c)), c, [], c.Weight, null,
            ParcelState.Abandoned, null, null, null, "Synthetic unsent terminal", PositionId: "left");
        await gate.WaitAsync();
        try { repo.Commit(op, "SyntheticTest", "No external facts asserted"); repo.Archive([id], "Synthetic test"); }
        finally { gate.Release(); }
        var counting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCount = new ManualResetEventSlim();
        runtime.Boundary = p => { if (p == "ArchiveCountReading") { counting.TrySetResult(); releaseCount.Wait(); } };
        var snapshot = Task.Run(runtime.SnapshotAsync);
        await counting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var command = runtime.CommandAsync(new(Wire.Id(), repo.Revision, "Pause"));
        await command.WaitAsync(TimeSpan.FromSeconds(5)); // Independent count cannot own the business coordinator.
        releaseCount.Set(); var first = await snapshot;
        Check(first.Capacity!.Archived == 1 && first.Paused, "history revision refresh is exact and scalar I/O is outside the gate");
        var encodes = 0;
        runtime.Boundary = p => { if (p == "SnapshotEncoding") Interlocked.Increment(ref encodes); };
        for (var i = 0; i < 4; i++) await runtime.SnapshotAsync();
        Check(encodes == 0, "unchanged business revision does not reserialize operations despite live worker/header changes");
        first.Operations[0].FrozenConfiguration.Wcs!.Parameters["slot"] = "MUTATED-DETACHED";
        var second = await runtime.SnapshotAsync();
        Check(!second.Operations[0].FrozenConfiguration.Wcs!.Parameters.ContainsValue("MUTATED-DETACHED"), "API callers retain independent detached object graphs");
        await ManagementModeFixture.Authorize(runtime);
        var authorized = await runtime.SnapshotAsync();
        Check(authorized.Operations[0].FrozenConfiguration.Wcs!.Target == c.Wcs!.Target && encodes == 1, "authority change invalidates redacted operation cache exactly once");

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        runtime.Boundary = p => { if (p == "SnapshotGateAcquired") { entered.TrySetResult(); release.Wait(); } };
        var authority = new ManagementModeStatus(true, true, DateTimeOffset.UtcNow.AddMinutes(1));
        var shared1 = SharedSnapshotRead.Read(runtime, authority);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var shared2 = SharedSnapshotRead.Read(runtime, authority);
        Check(ReferenceEquals(shared1, shared2), "concurrent windows share one in-flight detached projection");
        var differentAuthority = SharedSnapshotRead.Read(runtime, authority with { Active = false });
        Check(!ReferenceEquals(shared1, differentAuthority), "a different authority generation cannot join the earlier privileged read");
        release.Set(); await Task.WhenAll(shared1, shared2, differentAuthority);
        runtime.Boundary = null;
    }
}
