using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

using static SmContractFixture;

internal static class BackupOwnershipChecks
{
    public static Task BackupOwnership(string workspace, string root)
    {
        Directory.CreateDirectory(root);
        // DB14 validates recorded business identities. Exercise the generic ownership
        // fence with two identities that the current format actually supports.
        var owner = new Ipc.Application.Sportmaster.SportmasterFlow().Id;
        var foreignOwner = new Ipc.Application.DirectPlc.DirectPlcFlow().Id;
        var backup = Path.Combine(root, "foreign.db");
        using (var repository = new ParcelRepository(Path.Combine(root, "source"), owner)) repository.Backup(backup);
        // A foreign current-format database is a boundary fixture, not development of another business module.
        using (var database = new SqliteConnection("Data Source=" + backup + ";Pooling=False"))
        {
            database.Open(); using var command = database.CreateCommand();
            command.CommandText = "UPDATE platform_migrations SET application=$foreign";
            command.Parameters.AddWithValue("$foreign", foreignOwner);
            command.ExecuteNonQuery();
        }
        var recovery = Directory.CreateDirectory(backup + ".recovery").FullName;
        var completion = Path.Combine(recovery, "COMPLETED.json");
        var manifest = new ParcelRecoveryManifest(owner, Path.GetFileName(backup), Wire.FileHash(backup), [], default, "Synthetic incomplete foreign backup");
        ExtensionStore.AtomicWrite(completion, manifest);
        var databaseHash = Wire.FileHash(backup); var completionHash = Wire.FileHash(completion);
        try { _ = ParcelRecovery.PlanSetDeletion(backup, owner, () => { }, default); throw new Exception("Self-declared manifest granted foreign database deletion"); }
        catch (InvalidDataException) { }
        Check(Wire.FileHash(backup) == databaseHash && Wire.FileHash(completion) == completionHash && !File.Exists(backup + ".deleting.json"),
            "an incomplete manifest cannot turn another application's database into the local deletion inventory");
        ExtensionStore.AtomicWrite(completion, manifest with { Application = foreignOwner });
        var foreign = ParcelRecovery.PlanSetDeletion(backup, foreignOwner, () => { }, default);
        foreach (var status in new[] { "NotStarted", "Partial" })
        {
            ExtensionStore.AtomicWrite(backup + ".deleting.json", foreign with { Status = status });
            var originals = new[] { backup, completion, backup + ".deleting.json" }.ToDictionary(p => p, Wire.FileHash);
            using var execution = new MaintenanceExecution();
            try { _ = ParcelRecovery.DeleteSet(backup, foreign.Identity, owner, () => { }, _ => { }, execution); throw new Exception("Foreign deletion receipt bypassed current business ownership"); }
            catch (InvalidDataException) { }
            Check(originals.All(p => File.Exists(p.Key) && Wire.FileHash(p.Key) == p.Value),
                status + " deletion resume rejects foreign ownership before mutating any frozen byte");
        }
        using (var execution = new MaintenanceExecution())
            Check(ParcelRecovery.DeleteSet(backup, foreign.Identity, foreignOwner, () => { }, _ => { }, execution).Status == "Deleted",
                "the original owner can still finish the exact attributable incomplete deletion");
        return Task.CompletedTask;
    }

    public static async Task BackupFences(string workspace, string root)
    {
        foreach (var phase in new[] { "HeartbeatMaintenanceLease", "HeartbeatMaintenanceReleased", "both-storage" })
        {
            await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, phase), deferred: true, mode: RunMode.Simulation);
            await h.StartAsync(); await Pause(h.App);
            var repository = Field<ParcelRepository>(h.App, "repository");
            repository.Boundary = name =>
            {
                if (phase == "both-storage" && name is "BeforeBusinessCommit:HeartbeatMaintenanceLease" or "BeforeBusinessCommit:HeartbeatMaintenanceReleased")
                    throw new IOException("Synthetic source " + name);
                if (name == "BeforeBusinessCommit:" + phase) throw new InvalidOperationException("Synthetic " + phase);
            };
            Exception error;
            try { error = await LifecycleOptimization.AfterMonitorDrain(h.App, () => Failure(() => h.App.BackupAsync(Path.Combine(root, phase, "first.db")))); }
            finally { repository.Boundary = null; }
            Check(!Field<bool>(h.App, "backupInProgress"), phase + " releases only its owned temporary ingress fence on every failure path");
            if (phase == "both-storage")
            {
                Check(error is AggregateException aggregate && aggregate.Flatten().InnerExceptions.Count == 2 && h.App.FatalFailure.IsCompleted,
                    "preparation and cleanup errors both survive while the real source-storage guard remains active");
                Check((await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "CR-SM-STORAGE-BLOCKED"), default)).Status != "Admitted" && h.Peer.Requests.IsEmpty,
                    "temporary fence release cannot bypass damaged-source admission protection");
            }
            else
            {
                Check(error is InvalidOperationException, "heartbeat maintenance failure is not reported as backup success");
                await CameraRoundHarness.Until(async () => await h.App.CheckRcsHeartbeatAsync(default) == RcsHeartbeatResult.Received);
                var result = await LifecycleOptimization.AfterMonitorDrain(h.App, () => h.App.BackupAsync(Path.Combine(root, phase, "next.db")));
                Check(result.Status == "BackupCreated", "a later explicit backup can complete after the original failure without restarting");
            }
        }
    }
}
