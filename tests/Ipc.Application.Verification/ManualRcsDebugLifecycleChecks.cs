using Ipc.Application.Contracts;
using Ipc.Platform;

internal static partial class ManualRcsDebugChecks
{
    public static async Task Lifecycle(string workspace, string root)
    {
        Console.WriteLine("EvidenceRoot=" + root);
        Console.WriteLine("Explicit Simulation fixture; real SQLite and built-in loopback RCS. Production/Windows/site qualification is separate.");
        var failures = new List<Exception>();
        foreach (var test in new Func<Task>[] { Capacity, PreparationFailure, PendingReceipt })
            try { await test(); }
            catch (Exception error) { Console.WriteLine("FAIL MANUAL-DEBUG-LIFECYCLE " + error); failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException(failures);

        async Task Capacity()
        {
            await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "capacity"), deferred: true, maximumOperations: 1, mode: RunMode.Simulation);
            await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
            var s = await h.App.SnapshotAsync();
            var command = new PrepareManualRcsRequest(Wire.Id(), s.RuntimeId, s.Configuration.Revision, "left", "CAPACITY-ONE", null);
            var first = await h.App.PrepareManualRcsRequestAsync(command, default);
            Check(first.CanSend && !(await h.App.SnapshotAsync()).Capacity!.Available,
                "last reserved slot can send while new ordinary admissions remain blocked");
            var other = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "OTHER"), default);
            Check(other.Status == "Rejected" && h.Peer.Requests.IsEmpty, "ordinary other-position admission cannot consume a full capacity slot");
            var replacement = await h.App.PrepareManualRcsRequestAsync(command with { CommandId = Wire.Id(), Barcode = "REPLACEMENT", ExpectedRoundId = first.Id }, default);
            Check(replacement.CanSend && (await Original(h, first)).State == ParcelState.Abandoned,
                "replacing this unsent intent releases its reservation before admitting the replacement");
            var closeOld = new CloseUnsentDebugRound(Wire.Id(), s.RuntimeId, first.Id!);
            await Reject(() => h.App.CloseUnsentDebugRoundAsync(closeOld, default), "old-window cleanup cannot close the new round");
            Check((await h.App.ReadCameraReadRoundAsync(default)).Id == replacement.Id && replacement.CanSend, "newer round survives stale cleanup");
            var send = new SendCameraRound(Wire.Id(), s.RuntimeId, replacement.Id!, replacement.RequestHash!);
            await h.App.SendCameraRoundAsync(send, default); await h.App.SendCameraRoundAsync(send, default);
            Check(h.Peer.Requests.Count == 1 && (await Original(h, replacement)).Wcs is { State: StageState.Accepted, Attempts: 1 },
                "capacity-one request sends once; repeated send only reads original facts");
            await Reject(() => h.App.CloseUnsentDebugRoundAsync(new(Wire.Id(), s.RuntimeId, replacement.Id!), default), "accepted original cannot be cancelled by page exit");
            h.Peer.Mode = "malformed";
            var unknown = await h.App.PrepareManualRcsRequestAsync(command with { CommandId = Wire.Id(), Barcode = "UNKNOWN", ExpectedRoundId = replacement.Id }, default);
            await h.App.SendCameraRoundAsync(new(Wire.Id(), s.RuntimeId, unknown.Id!, unknown.RequestHash!), default);
            var before = await Original(h, unknown);
            await Reject(() => h.App.CloseUnsentDebugRoundAsync(new(Wire.Id(), s.RuntimeId, unknown.Id!), default), "Unknown original cannot be cancelled by page exit");
            Check(before.Wcs is { State: StageState.Unknown, Attempts: 1 } && Wire.Encode(await Original(h, unknown)) == Wire.Encode(before),
                "Unknown keeps the original request bytes and attempt after rejected cleanup");
        }
        async Task PreparationFailure()
        {
            await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "prepare-failure"), deferred: true, maximumOperations: 1, requestIdCount: 1, mode: RunMode.Simulation);
            await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
            var s = await h.App.SnapshotAsync();
            var command = new PrepareManualRcsRequest(Wire.Id(), s.RuntimeId, s.Configuration.Revision, "left", "USE-ONLY-ID", null);
            var first = await h.App.PrepareManualRcsRequestAsync(command, default);
            await h.App.CloseUnsentDebugRoundAsync(new(Wire.Id(), s.RuntimeId, first.Id!), default);
            command = command with { CommandId = Wire.Id(), Barcode = "NO-ID-LEFT", ExpectedRoundId = first.Id };
            var failed = await h.App.PrepareManualRcsRequestAsync(command, default);
            var original = await Original(h, failed);
            Check(original is { State: ParcelState.Blocked, Wcs: null } && original.Reason!.Contains("exhausted") && h.Peer.Requests.IsEmpty,
                "real numeric ID exhaustion persists a blocked intent without WCS or HTTP attempt");
            Check((await h.App.PrepareManualRcsRequestAsync(command, default)).OperationId == failed.OperationId,
                "repeated failed preparation retains the same operation");
            var close = new CloseUnsentDebugRound(Wire.Id(), s.RuntimeId, failed.Id!);
            await h.App.CloseUnsentDebugRoundAsync(close, default); await h.App.CloseUnsentDebugRoundAsync(close, default);
            var cancelled = await Original(h, failed);
            Check(cancelled.State == ParcelState.Abandoned && cancelled.Wcs is null &&
                cancelled.OperatorDebugRequest == original.OperatorDebugRequest && h.Peer.Requests.IsEmpty &&
                (await h.App.SnapshotAsync()).Positions!.All(p => p.BarcodeInput?.Manual == true),
                "failed preparation can be audited and closed idempotently without losing evidence or blocking manual admission");
            var next = await h.App.PrepareManualRcsRequestAsync(command with { CommandId = Wire.Id(), ExpectedRoundId = failed.Id }, default);
            var replacement = await h.App.PrepareManualRcsRequestAsync(command with { CommandId = Wire.Id(), ExpectedRoundId = next.Id }, default);
            Check((await Original(h, next)).State == ParcelState.Abandoned && (await Original(h, replacement)).Wcs is null,
                "another explicit prepare safely replaces a never-sent intent whose preparation failed");
            await h.App.CloseUnsentDebugRoundAsync(close, default);
            Check((await h.App.ReadCameraReadRoundAsync(default)).Id == replacement.Id && (await Original(h, replacement)).State == ParcelState.Blocked,
                "replaying an already completed cleanup receipt cannot affect a newer round");
            await h.App.CloseUnsentDebugRoundAsync(new(Wire.Id(), s.RuntimeId, replacement.Id!), default);
        }
        async Task PendingReceipt()
        {
            await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "pending"), deferred: true, mode: RunMode.Simulation);
            await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
            var s = await h.App.SnapshotAsync();
            var round = await h.App.PrepareManualRcsRequestAsync(new(Wire.Id(), s.RuntimeId, s.Configuration.Revision, "left", "PENDING-RAW", null), default);
            h.App.Boundary = point => { if (point == "AfterRawDurable") throw new IOException("Synthetic T3 failure after durable raw reply"); };
            await Reject(() => h.App.SendCameraRoundAsync(new(Wire.Id(), s.RuntimeId, round.Id!, round.RequestHash!), default), "T3 failure remains observable");
            h.App.Boundary = null;
            var before = await Original(h, round);
            Check((await h.App.SnapshotAsync()).AuxiliaryReasons!.Any(r => r.Contains("durable pending T3")), "raw receipt remains durably pending before cleanup");
            await Reject(() => h.App.CloseUnsentDebugRoundAsync(new(Wire.Id(), s.RuntimeId, round.Id!), default), "pending original receipt cannot be cancelled by page exit");
            Check(before.Wcs is { Attempts: 1 } && Wire.Encode(await Original(h, round)) == Wire.Encode(before) && h.Peer.Requests.Count == 1,
                "pending original bytes, attempt and receipt responsibility survive cleanup rejection");
        }
    }
    static async Task<ParcelOperation> Original(ManualSupplyHarness h, CameraReadRound round) =>
        (await h.App.ReadCameraOperationDetailAsync(round.OperationId!, default))!.Original;
    static async Task Reject(Func<Task> action, string message)
    {
        try { await action(); }
        catch (Exception error) when (error is InvalidOperationException or IOException) { Check(true, message); return; }
        throw new Exception("Expected rejection: " + message);
    }
}
