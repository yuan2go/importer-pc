using System.Security.Cryptography;
using Ipc.Application.Contracts;
using Ipc.Platform;

internal static class CameraRoundChecks
{
    static void Check(bool ok, string detail) { if (!ok) throw new InvalidOperationException(detail); Console.WriteLine("PASS ROUND " + detail); }
    static async Task Reject(Func<Task> action, string detail)
    { try { await action(); } catch (Exception e) when (e is InvalidOperationException or UnauthorizedAccessException or OperationCanceledException) { Check(true, detail); return; } throw new InvalidOperationException("Guard escaped: " + detail); }
    public static async Task Run(string workspace, string root)
    {
        Console.WriteLine("Real MVS driver/ZXing/SQLite/built-in RCS, synthetic native APIs and loopback HTTP; real hardware/customer RCS NOT_RUN");
        await using (var h = new CameraRoundHarness(workspace, Path.Combine(root, "complete")))
        {
            await h.StartAsync(); await h.App.ExitManagementModeAsync();
            await Reject(async () => { await h.App.ReadCameraReadRoundAsync(default); }, "round evidence requires management authority");
            await h.AuthorizeAsync(); var scope = await h.BeginAsync();
            Check(scope.BusinessFenced && scope.SingleReadRounds && scope.State == "Active", "isolated one-shot scope is authoritative before scanning");
            var pixels = CameraRoundHarness.Barcode("ROUND-0001"); h.Api(h.Left).Enqueue(pixels, 1);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Api(h.Left).Releases >= 1));
            Check(h.Factory.Decoders[h.Left].Count == 0 && h.Peer.Requests.IsEmpty, "scope starts decode-closed while the single native owner still returns SDK loans");
            var first = await h.ArmAsync(h.Left); Check(first.State == "Reading" && first.Frame is null && first.Barcode is null && first.RequestBody is null, "arm opens a clean round without a request or old photo");
            h.Api(h.Left).Enqueue(new(new byte[100 * 80], 100, 80), 2);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Decoders[h.Left].Count >= 1));
            Check((await h.App.ReadCameraReadRoundAsync(default)).State == "Reading", "NoRead continues the same round without a fabricated result");
            h.Api(h.Left).Enqueue(CameraRoundHarness.Multiple("MULTI-A", "MULTI-B"), 3);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Decoders[h.Left].Count >= 2));
            Check(h.Factory.Decoders[h.Left].Last.Length == 2 && (await h.App.ReadCameraReadRoundAsync(default)).State == "Reading", "real ZXing multi-candidate frame is not silently reduced to the first code");
            h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("WRONG-CAMERA"), 1);
            h.Api(h.Left).Enqueue(pixels, 4); h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("NEXT-PARCEL"), 5);
            var locked = await h.LockedAsync();
            Check(locked.Barcode == "ROUND-0001" && locked.Frame is { FrameNumber: 4 } && locked.CanSend, "unique winner locks its exact frame and prepares an eligible manual request: " + locked.Detail);
            var original = await h.App.ReadCameraOriginalAsync(locked.Frame!.Id, default) ?? throw new InvalidDataException("Locked image missing");
            Check(original.Gray8.SequenceEqual(pixels.Data) && original.Metadata.OriginalSha256 == Convert.ToHexString(SHA256.HashData(pixels.Data)), "persisted original equals pre-contrast bytes for the selected frame");
            Check(original.Metadata.Symbols.Length == 1 && original.Metadata.Symbols[0].Corners.Length == 4 &&
                Wire.Encode(original.Metadata.Symbols) == Wire.Encode(h.Factory.Decoders[h.Left].Last), "image geometry equals actual ZXing coordinates from that one frame");
            await CameraRoundHarness.Until(() => Task.FromResult(h.Api(h.Left).Releases >= 5 && h.Api(h.Right).Releases >= 1));
            var frozen = await h.App.ReadCameraReadRoundAsync(default);
            Check(frozen.Frame?.Id == locked.Frame.Id && frozen.Barcode == locked.Barcode && h.Factory.Decoders[h.Left].Count == 3 && h.Factory.Decoders[h.Right].Count == 0,
                "later parcels and the unselected camera cannot decode/overwrite the frozen winner");
            var op = (await h.App.SnapshotAsync()).Operations.Single();
            var blocked = (await h.App.SnapshotAsync()).Inputs!.Single(i => i.Observation.SourceEventId == locked.Frame.SourceEventId);
            Check(blocked.Outcome == ParcelInputOutcome.Blocked && op.Acquisition is { } capture && capture.DeviceId == blocked.Observation.DeviceId &&
                capture.Epoch == blocked.Observation.Epoch && capture.SourceEventId == blocked.Observation.SourceEventId && capture.AdmissionId == blocked.Observation.AdmissionId &&
                capture.ObservedAt == blocked.Observation.ObservedAt && op.AdmissionId != capture.AdmissionId,
                "independent debug intent carries the genuine source acquisition while the original observation remains blocked");
            Check(op.DebugRequest?.FrameId == locked.Frame.Id && op.Wcs is { State: StageState.Prepared, Attempts: 0 } && h.Peer.Requests.IsEmpty &&
                op.Wcs.Request.IntegrationRequest?.Body == locked.RequestBody && locked.RequestBody!.Contains("ROUND-0001"), "request JSON auto-fill is the actual frozen T1 with zero HTTP attempts");
            var send = await h.SendCommandAsync(locked);
            await Reject(async () => { await h.App.SendCameraRoundAsync(send with { RequestHash = new string('0', 64) }, default); }, "tampered request hash cannot send");
            var returned = await h.App.SendCameraRoundAsync(send, default);
            Check(h.Peer.Requests.Count == 1 && h.Peer.Requests.Single() == locked.RequestBody && returned.Result!.StartsWith("状态：Accepted") &&
                (await h.App.SnapshotAsync()).Operations.Single().Wcs?.State == StageState.Accepted,
                "explicit send posts exactly the frozen JSON once and records a genuinely Accepted stage");
            await h.App.SendCameraRoundAsync(send, default);
            await Reject(async () => { await h.App.SendCameraRoundAsync(send with { CommandId = Wire.Id() }, default); }, "fresh command cannot resend an attempted request");
            Check(h.Peer.Requests.Count == 1 && (await h.App.SnapshotAsync()).Operations.Single().Wcs!.Attempts == 1, "same-command lookup and new-command rejection preserve one attempt");
            var next = await h.ArmAsync(h.Left); Check(next.Frame is null && next.RequestBody is null && next.Result is null && next.Barcode is null, "again-scan clears all round evidence and results before the next frame");
            var changed = CameraRoundHarness.Barcode("ROUND-0001"); changed.Data[0] = 240; h.Api(h.Left).Enqueue(changed, 6);
            var second = await h.LockedAsync();
            Check(second.Frame!.Id != locked.Frame.Id && second.Frame.FrameNumber == 6 && second.Frame.OriginalSha256 != locked.Frame.OriginalSha256 && h.Peer.Requests.Count == 1,
                "same barcode on another round retains another frame and never auto-sends");
            var s = await h.App.SnapshotAsync(); var oldExpected = new ArmCameraRead(Wire.Id(), s.RuntimeId, s.Configuration.Revision, scope.SessionId!, h.Right, first.Id);
            await Reject(async () => { await h.App.ArmCameraReadAsync(oldExpected, default); }, "stale multi-window round CAS cannot replace the current round");
            await h.ArmAsync(h.Right);
            Check((await h.App.SnapshotAsync()).Operations.Single(o => o.Id == second.OperationId).Wcs!.State == StageState.Cancelled, "next round cancels only the old never-attempted request");
            h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("ROUND-RIGHT"), 2); var right = await h.LockedAsync();
            h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously); var held = await h.SendCommandAsync(right); var sending = h.App.SendCameraRoundAsync(held, default);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 2));
            await Reject(async () => { await h.ArmAsync(h.Left); }, "sending round cannot be replaced while HTTP is in flight");
            await Reject(h.EndAsync, "scene clearance cannot erase an in-flight request");
            await h.App.ExitManagementModeAsync(); await Reject(async () => { await sending; }, "revoked management interrupts the wait without claiming no external effect");
            var unknown = (await h.App.SnapshotAsync()).Operations.Single(o => o.Id == right.OperationId);
            Check(unknown.Wcs is { State: StageState.Unknown, Attempts: 1 }, "authority cancellation retains Unknown and one attempted request without exposing privileged JSON");
            h.Peer.Hold.TrySetResult(); h.Peer.Hold = null; await h.AuthorizeAsync(); await h.App.SendCameraRoundAsync(held, default);
            unknown = (await h.App.SnapshotAsync()).Operations.Single(o => o.Id == right.OperationId);
            Check(unknown.Wcs!.Request.IntegrationRequest!.Body == right.RequestBody, "reauthorized custody exposes the unchanged original JSON");
            Check(h.Peer.Requests.Count == 2 && !(await h.App.ReadCameraReadRoundAsync(default)).CanSend, "reauthorization/query never replays Unknown");
            var unknownRequest = Wire.Encode(unknown.Wcs!.Request); await h.App.DisposeAsync(); await h.StartAsync();
            var recovered = (await h.App.SnapshotAsync()).Operations.Single(o => o.Id == unknown.Id);
            Check(recovered.Wcs is { State: StageState.Unknown, Attempts: 1 } && Wire.Encode(recovered.Wcs.Request) == unknownRequest &&
                (await h.App.CameraInspectionStatusAsync(default)).BusinessFenced, "restart preserves original Unknown and the persistent isolation fence");
            h.Api(h.Left).Enqueue(pixels, 100); await CameraRoundHarness.Until(() => Task.FromResult(h.Api(h.Left).Releases == 1));
            Check(h.Factory.Decoders[h.Left].Count == 0 && h.Peer.Requests.Count == 2, "recovered isolation starts decode-closed and does not replay an old round");
            await h.EndAsync();
            Check((await h.App.SnapshotAsync()).Operations.Single(o => o.Id == unknown.Id).Wcs!.State == StageState.Unknown && h.Peer.Requests.Count == 2, "explicit clearance keeps unresolved original responsibility");
        }
        await using (var h = new CameraRoundHarness(workspace, Path.Combine(root, "clear-cancel")))
        {
            await h.StartAsync(); await h.BeginAsync(); h.Api(h.Left).ClearGate.Reset();
            var baseline = h.Api(h.Left).Clears; using var cancel = new CancellationTokenSource(); var snap = await h.App.SnapshotAsync(); var status = await h.App.CameraInspectionStatusAsync(default);
            var arming = h.App.ArmCameraReadAsync(new(Wire.Id(), snap.RuntimeId, snap.Configuration.Revision, status.SessionId!, h.Left, null), cancel.Token);
            try
            {
                await CameraRoundHarness.Until(() => Task.FromResult(h.Api(h.Left).Clears > baseline)); cancel.Cancel();
                await Reject(async () => { await arming; }, "cancelling SDK cutover does not certify native completion");
                Check(h.Api(h.Left).Starts == 1 && h.Api(h.Left).Opens == 1 && (await h.App.CameraInspectionStatusAsync(default)).BusinessFenced && h.Peer.Requests.IsEmpty,
                    "cancelled native cutover retains one owner and isolation without an HTTP request");
            }
            finally { h.Api(h.Left).ClearGate.Set(); }
        }
        await using (var h = new CameraRoundHarness(workspace, Path.Combine(root, "clear-failure")))
        {
            await h.StartAsync(); await h.BeginAsync(); h.Api(h.Left).FailNextClear = true;
            try { await h.ArmAsync(h.Left); throw new InvalidOperationException("Injected SDK failure escaped"); }
            catch (IOException error) when (error.Message == "Synthetic SDK cache clear failed") { Check(true, "SDK cache-clear failure is not reported as a completed scan trigger"); }
            Check((await h.App.ReadCameraReadRoundAsync(default)).State == "Interrupted" && (await h.App.CameraInspectionStatusAsync(default)).BusinessFenced && h.Peer.Requests.IsEmpty,
                "failed SDK cutover preserves isolation and has no request/photo substitution");
        }
        await using (var h = new CameraRoundHarness(workspace, Path.Combine(root, "rejected-send")))
        {
            await h.StartAsync(); await h.BeginAsync(); await h.ArmAsync(h.Left); h.Peer.Mode = "reject";
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("ROUND-REJECT"), 1); var round = await h.LockedAsync();
            var command = await h.SendCommandAsync(round); var returned = await h.App.SendCameraRoundAsync(command, default);
            Check(returned.Result!.StartsWith("状态：Rejected") && h.Peer.Requests.Count == 1 && (await h.App.SnapshotAsync()).Operations.Single().Wcs is { State: StageState.Rejected, Attempts: 1 },
                "RCS business rejection retains its actual response and single attempt");
            await h.App.SendCameraRoundAsync(command, default); await h.EndAsync();
            Check(h.Peer.Requests.Count == 1, "rejection lookup and explicit clearance do not resubmit");
        }
        await using (var h = new CameraRoundHarness(workspace, Path.Combine(root, "missing-acquisition")))
        {
            await h.StartAsync(); await h.BeginAsync(); await h.ArmAsync(h.Left);
            // Deliberate adapter-boundary corruption. Keep the actual driver,
            // decoder and pixels; remove only the capture identity delivered to Application.
            var source = h.Factory.Sources[h.Left];
            var registration = (InspectionRegistration)source.GetType().GetField("inspection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(source)!;
            await source.SetInspectionAsync(registration with { Receive = frame => registration.Receive(frame with { Acquisition = null }) }, default);
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("MISSING-PROOF"), 1); var locked = await h.LockedAsync();
            Check(locked.Frame is not null && locked.RequestBody is null && !locked.CanSend && locked.Detail.Contains("采集身份缺失") &&
                (await h.App.SnapshotAsync()).Operations.Length == 0 && h.Peer.Requests.IsEmpty,
                "missing acquisition proof keeps diagnostic pixels but cannot invent a sendable request or business intent");
        }
    }
}
