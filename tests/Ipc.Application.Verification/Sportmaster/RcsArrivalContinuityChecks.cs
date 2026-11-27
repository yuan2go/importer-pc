using System.Collections.Concurrent;
using Ipc.Application.Contracts;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Platform;
using static SmContractFixture;

internal static class RcsArrivalContinuityChecks
{
    internal static async Task Run(string workspace, string root)
    {
        await ArrivalConfirmationLoss(workspace, root);
        await using var peer = new CameraRoundHarness(workspace, Path.Combine(root, "heartbeat-owner"));
        await HeartbeatRecovery(peer.Config);
    }
    static async Task ArrivalConfirmationLoss(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, root);
        await h.StartAsync(); await h.App.ExitManagementModeAsync();
        var before = h.Api(h.Left).Clears;
        h.Api(h.Left).ClearGate.Reset();
        try
        {
            h.Peer.Vehicles[h.Config.Wcs!.Parameters["left-bcr-code"]] = "SYNTHETIC-NEW-ARRIVAL";
            await SmRoundFixture.CompletedHeartbeatAsync(h.App);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Api(h.Left).Clears > before));
            var generation = (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Arrival!.Generation;
            h.Peer.HeartbeatUnavailable = true;
            await SmRoundFixture.CompletedHeartbeatAsync(h.App);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Arrival?.Confirmed == false);
            h.Api(h.Left).ClearGate.Set();
            await h.AuthorizeAsync(); // Read the protected completion journal for this generation.
            await CameraRoundHarness.Until(async () => (await h.LogDetailsAsync("相机到位边界完成", "小车到位")).Any(detail =>
                detail.GetProperty("Position").GetString() == "left" && detail.GetProperty("Generation").GetString() == generation));
            h.Peer.HeartbeatUnavailable = false;
            await SmRoundFixture.CompletedHeartbeatAsync(h.App);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Arrival?.CameraBoundaryReady == true);
            Check(h.Peer.Requests.IsEmpty, "lost heartbeat confirmation during cache cutover recovers the same arrival after a real response without a destination request");
        }
        finally { h.Api(h.Left).ClearGate.Set(); h.Peer.HeartbeatUnavailable = false; }
    }
    sealed class FailingHeartbeatOwner(ParcelConfiguration configuration) : ISmHeartbeatOwner
    {
        int calls, completions;
        public readonly TaskCompletionSource Retry = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<SmHeartbeatIntent?> BeginHeartbeatAsync(long id, CancellationToken token)
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1) throw new IOException("synthetic heartbeat intent failure");
            if (call >= 3) await Retry.Task.WaitAsync(token);
            return new(id, Wire.Hash(Wire.Encode(configuration)), configuration.Topology!.Positions.Select(p => new SmHeartbeatMember(p.Id,
                configuration.StationCode!, configuration.Wcs!.Parameters![p.Id + "-bcr-code"], 0)).ToArray());
        }
        public Task CompleteHeartbeatAsync(SmHeartbeatIntent intent, SmHeartbeatConfirmation confirmation)
        {
            if (Interlocked.Increment(ref completions) == 1) throw new IOException("synthetic heartbeat completion failure");
            Completed.TrySetResult(); return Task.CompletedTask;
        }
    }
    static async Task HeartbeatRecovery(ParcelConfiguration configuration)
    {
        var binding = configuration.Wcs! with { Parameters = new(configuration.Wcs!.Parameters!) { ["heartbeat-interval-ms"] = "100" } };
        configuration = configuration with { Wcs = binding };
        await using var rcs = new SportmasterRcsTaskService(binding);
        var owner = new FailingHeartbeatOwner(configuration);
        rcs.ConfigureRuntime(configuration); rcs.SetHeartbeatOwner(owner); await rcs.StartAsync(default);
        try
        {
            await CameraRoundHarness.Until(async () => (await rcs.HealthAsync(default)).Detail.Contains("completion failure", StringComparison.Ordinal));
            Check((await rcs.HealthAsync(default)).RemoteConfirmation == RemoteConnectionConfirmation.Unconfirmed,
                "heartbeat owner failures revoke remote confirmation and do not terminate the automatic loop");
            owner.Retry.TrySetResult();
            await owner.Completed.Task.WaitAsync(TimeSpan.FromSeconds(4));
            await CameraRoundHarness.Until(async () => (await rcs.HealthAsync(default)).RemoteConfirmation == RemoteConnectionConfirmation.ResponseReceived);
            Check(true, "a later real heartbeat recovers after both intent and completion callbacks failed");
        }
        finally { owner.Retry.TrySetResult(); }
    }
}
