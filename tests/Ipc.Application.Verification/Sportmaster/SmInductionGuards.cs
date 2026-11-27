using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

using static SmContractFixture;

internal static class SmInductionGuards
{
    public static async Task Removal(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, root, deferred: true, mode: RunMode.Simulation);
        await h.StartAsync();
        var first = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "CR-SM-REMOVAL"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(first.OperationId!))?.Wcs?.State == StageState.Accepted);
        await ManagementModeFixture.Authorize(h.App);
        var window = Wire.Id();
        await h.App.SetManagementWorkspaceAsync(window, true, default);
        try
        {
            var before = await h.App.SnapshotAsync();
            Check(Left(before).PlacementRemovalOperation is null && Left(before).CurrentRoundId is not null, "management workspace forbids physical confirmation for the existing round");
            foreach (var token in new string?[] { null, "", " " })
            {
                var receipt = await h.App.CommandAsync(new(Wire.Id(), before.Revision, "ConfirmPlacementRemoval", "left", token));
                Check(receipt.Status != "Persisted" && Left(await h.App.SnapshotAsync()).CurrentRoundId == Left(before).CurrentRoundId,
                    "missing/empty removal token cannot satisfy a closed confirmation guard");
            }
        }
        finally { await h.App.SetManagementWorkspaceAsync(window, false, default); }
        var current = await h.App.SnapshotAsync();
        var valid = new BusinessCommand(Wire.Id(), current.Revision, "ConfirmPlacementRemoval", "left", Left(current).PlacementRemovalOperation);
        Check(valid.Value is not null && (await h.App.CommandAsync(valid)).Status == "Persisted" && Left(await h.App.SnapshotAsync()).CurrentRoundId is null,
            "a current nonempty round/version token still confirms this parcel exactly once");
        await h.NextVehicleAsync();
        var next = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "CR-SM-NEXT"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(next.OperationId!))?.Wcs?.State == StageState.Accepted);
        var nextRound = Left(await h.App.SnapshotAsync()).CurrentRoundId;
        Check((await h.App.CommandAsync(valid with { Id = Wire.Id() })).Status != "Persisted" && Left(await h.App.SnapshotAsync()).CurrentRoundId == nextRound && h.Peer.Requests.Count == 2,
            "the old removal token cannot end or resubmit the next parcel");
    }

    public static async Task Dispatch(string workspace, string root)
    {
        foreach (var storageFailure in new[] { false, true })
        {
            await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "publication-" + storageFailure), deferred: true, mode: RunMode.Simulation);
            await h.StartAsync();
            var command = await h.Command("left", "CR-SM-PUBLICATION");
            h.App.Boundary = phase =>
            {
                if (phase != "BeforeSmInputPublication") return;
                if (storageFailure) throw new IOException("Synthetic business-storage publication failure");
                throw new InvalidOperationException("Synthetic auxiliary publication failure");
            };
            Exception error;
            try { error = await Failure(() => h.App.SubmitParcelBarcodeAsync(command, default)); }
            finally { h.App.Boundary = null; }
            Check(error is IOException == storageFailure, "the original publication failure remains visible to its caller");
            var operation = (await h.App.SnapshotAsync()).Operations.Single();
            if (storageFailure)
            {
                await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity!.ManagedExecutions == 0);
                Check(h.App.FatalFailure.IsCompleted && h.Peer.Requests.IsEmpty && (await h.App.OperationAsync(operation.Id))!.Wcs?.Attempts is null or 0,
                    "registered execution still respects the real storage-failure guard and sends nothing");
            }
            else
            {
                await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(operation.Id))?.Wcs?.State == StageState.Accepted);
                var replay = await h.App.SubmitParcelBarcodeAsync(command, default);
                Check(replay.OperationId == operation.Id && h.Peer.Requests.Count == 1 && (await h.App.OperationAsync(operation.Id))!.Wcs!.Attempts == 1,
                    "durably claimed work completes after auxiliary failure; original command reconciliation never resends");
            }
        }
    }
}
