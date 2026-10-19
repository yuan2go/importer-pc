using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Audit03
{
    public static async Task Snapshots(string source, string evidence)
    {
        workspace = source; root = evidence;
        await using var app = await Open(Config("sportmaster"), "snapshots");
        app.EmitReferenceAt("left", "original-left", "LEFT", "left-event");
        await Wait(app, s => s.Operations.FirstOrDefault()?.State == ParcelState.SlotResolved);
        foreach (var alternate in new[] { false, true })
        {
            var probe = new SportmasterUi.SnapshotProbe(app);
            ParcelViewModel vm = alternate ? new SupplyStationViewModel(probe) : new ParcelViewModel(probe, "classic", "sportmaster");
            await vm.RefreshAsync(); var a = vm.Snapshot!; var writes = probe.Writes;
            probe.Read = () => Task.FromException<ParcelSnapshot>(new IOException("Synthetic polling failure"));
            await vm.RunAsync(vm.RefreshAsync);
            Check(!vm.Fresh && vm.CurrentSlot.Contains("失效") && !vm.CanResume && !vm.CanConfigure && vm.Status.Contains("失效"), "both layouts explicitly invalidate old slot, status and maintenance actions");
            // The polling lane fails; Pause must still reach the application's CAS guard.
            await vm.PauseAsync();
            Check(probe.Commands.Last().Kind == "Pause" && (await app.SnapshotAsync()).Paused && !vm.Fresh, "poll failure cannot hide independent software pause; receipt does not claim fresh state");
            probe.Read = null; await vm.RefreshAsync();
            Check((await app.SnapshotAsync()).Paused && probe.Writes == writes + 1, "recovering a fresh snapshot never resumes business or resends");
            var b = vm.Snapshot! with { RuntimeId = Guid.NewGuid() }; probe.Read = () => Task.FromResult(b); await vm.RefreshAsync();
            probe.Read = () => Task.FromResult(a); await vm.RefreshAsync();
            Check(vm.Snapshot!.RuntimeId == b.RuntimeId && !vm.Fresh, "retired runtime response cannot revive a previous instance even at a larger revision");
            probe.Read = () => Task.FromResult(b); await vm.RefreshAsync();
            probe.Read = () => Task.FromResult(b with { Revision = b.Revision - 1 }); await vm.RefreshAsync();
            Check(!vm.Fresh && vm.Snapshot!.Revision == b.Revision, "revision regression invalidates dependent facts without applying old state");
            probe.Read = () => Task.FromResult(b); await vm.RefreshAsync();
            var pending = new TaskCompletionSource<ParcelSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            probe.Read = () => pending.Task; var older = vm.RefreshAsync();
            probe.Read = () => Task.FromResult(b); await vm.RefreshAsync(); pending.SetResult(a); await older;
            Check(vm.Fresh && vm.Snapshot!.RuntimeId == b.RuntimeId, "late older read cannot replace the latest valid runtime snapshot");
        }
        if (failures != 0) throw new Exception("A03 snapshot failures: " + failures);
        Console.WriteLine("PASS A03 SNAPSHOT COMPLETE");
    }
}
