using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;

internal static class SnapshotOrderingChecks
{
    static string workspace = "", root = "";
    static int failures;
    static void Check(bool condition, string message)
    { Console.WriteLine((condition ? "PASS SNAPSHOT " : "FAIL SNAPSHOT ") + message); if (!condition) failures++; }
    static async Task<ParcelRuntime> Open()
    {
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests", "Ipc.Application.Verification", "sportmaster.synthetic.json")));
        var directory = Path.Combine(root, "snapshots", "config"); Directory.CreateDirectory(directory);
        ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), c);
        var launch = new BusinessLaunch(directory, Path.Combine(root, "snapshots", "data"), null, null);
        var factory = new ParcelCapabilityFactory(launch); var app = new ParcelRuntime(launch, new SportmasterFlow(), factory);
        try { await app.StartAsync(default); return app; } catch { await app.DisposeAsync(); throw; }
    }
    static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> predicate)
    {
        for (int i = 0; i < 200; i++) { var s = await app.SnapshotAsync().WaitAsync(TimeSpan.FromSeconds(2)); if (predicate(s)) return s; await Task.Delay(15); }
        throw new Exception("A03 timeout: " + Wire.Encode(await app.SnapshotAsync()));
    }

    public static async Task Run(string source, string evidence)
    {
        workspace = source; root = evidence;
        await using var app = await Open();
        app.EmitReferenceAt("left", "original-left", "LEFT", "left-event");
        await Wait(app, s => s.Operations.FirstOrDefault()?.State == ParcelState.SlotResolved);
        foreach (var alternate in new[] { false, true })
        {
            var probe = new SnapshotReadFixture.SnapshotProbe(app);
            ParcelViewModel vm = alternate ? new SupplyStationViewModel(probe) : new ParcelViewModel(probe, "classic", "sportmaster");
            await vm.RefreshAsync(); var a = vm.Snapshot!; var writes = probe.Writes;
            probe.Read = () => Task.FromException<ParcelSnapshot>(new IOException("Synthetic polling failure"));
            await vm.RunAsync(vm.RefreshAsync);
            Check(!vm.Fresh && vm.CurrentSlot.Contains("失效") && !vm.CanResume && !vm.CanConfigure && vm.Status.Contains("仅供参考"), "both layouts explicitly invalidate old slot, status and maintenance actions");
            // The polling lane fails; Pause must still reach the application's CAS guard.
            await vm.PauseAsync();
            Check(probe.Commands.Last().Kind == "Pause" && (await app.SnapshotAsync()).Paused && !vm.Fresh, "poll failure cannot hide independent software pause; receipt does not claim fresh state");
            probe.Read = null; await vm.RefreshAsync();
            Check((await app.SnapshotAsync()).Paused && probe.Writes == writes + 1, "recovering a fresh snapshot never resumes business or resends");
            var b = vm.Snapshot! with { RuntimeId = Guid.NewGuid() }; probe.Read = () => Task.FromResult(b); await vm.RefreshAsync();
            probe.Read = () => Task.FromResult(a); await vm.RefreshAsync();
            Check(ReferenceEquals(vm.Snapshot, b), "retired runtime response cannot revive a previous instance even at a larger revision");
            probe.Read = () => Task.FromResult(b); await vm.RefreshAsync();
            probe.Read = () => Task.FromResult(b with { Revision = b.Revision - 1 }); await vm.RefreshAsync();
            Check(ReferenceEquals(vm.Snapshot, b), "regressing revision cannot replace the last accepted facts");
            probe.Read = () => Task.FromResult(b); await vm.RefreshAsync();
            var expired = b with { RuntimeId = Guid.NewGuid(), PublishedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - 4 * System.Diagnostics.Stopwatch.Frequency };
            probe.Read = () => Task.FromResult(expired); await vm.RefreshAsync();
            Check(!vm.Fresh && ReferenceEquals(vm.Snapshot, b) && !vm.CanResume && !vm.CanConfigure,
                "an expired publication closes fresh-state actions without adopting its facts");
            b = b with { PublishedTicks = System.Diagnostics.Stopwatch.GetTimestamp() };
            probe.Read = () => Task.FromResult(b); await vm.RefreshAsync();
            var pending = new TaskCompletionSource<ParcelSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            probe.Read = () => pending.Task; var older = vm.RefreshAsync(); await probe.ReadEntered.WaitAsync(TimeSpan.FromSeconds(3));
            probe.Read = () => Task.FromResult(b); await vm.RefreshAsync(); pending.SetResult(a); await older;
            Check(vm.Fresh && vm.Snapshot!.RuntimeId == b.RuntimeId, "late older read cannot replace the latest valid runtime snapshot");
        }
        if (failures != 0) throw new Exception("Snapshot failures: " + failures);
        Console.WriteLine("PASS SNAPSHOT COMPLETE");
    }
}
