using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;

static class StateJournalChecks
{
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS STATE-JOURNAL " + message); }
    static JsonElement Detail(JsonElement entry)
    {
        var detail = entry.GetProperty("Detail");
        return detail.ValueKind == JsonValueKind.String ? Parse(detail.GetString()!) : detail;
    }
    static JsonElement Parse(string text) { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
    static string? Event(JsonElement entry) => entry.TryGetProperty("Event", out var e) ? e.GetString() : null;
    static async Task<JsonElement[]> Read(CameraRoundHarness h)
    {
        var query = new SoftwareLogQuery(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1)) { KeyEvents = true };
        var page = await h.App.QueryLogsAsync(query, null, 0, default);
        try
        {
            List<JsonElement> entries = []; var offset = 0;
            while (true)
            {
                entries.AddRange(page.Entries.Select(e => Parse(e.Text))); offset += page.Entries.Length;
                if (!page.HasMore) return entries.ToArray();
                if (page.Entries.Length == 0) throw new InvalidDataException("Log paging made no progress");
                page = await h.App.QueryLogsAsync(query, page.Selection, offset, default);
            }
        }
        finally { h.App.ReleaseLogSelection(page.Selection); }
    }
    static async Task<JsonElement[]> WaitLog(CameraRoundHarness h, Func<JsonElement[], bool> found)
    {
        JsonElement[] logs = [];
        await CameraRoundHarness.Until(async () => { logs = await Read(h); return found(logs); });
        return logs;
    }
    static JsonElement State(JsonElement entry) => Detail(entry).GetProperty("State");
    static JsonElement Position(JsonElement state, string position) => state.GetProperty("Positions").EnumerateArray().Single(p => p.GetProperty("PositionId").GetString() == position);

    static async Task<BusinessReceipt> RetryConflictAsync(CameraRoundHarness h, Func<ParcelSnapshot, Task<BusinessReceipt>> submit)
    {
        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            var receipt = await submit(await h.App.SnapshotAsync());
            if (receipt.Status != "Conflict" || attempt == maxAttempts) return receipt;
        }
    }

    public static async Task Run(string workspace, string root)
    {
        VerifyDurablePause(Path.Combine(root, "pause-evidence"));
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "runtime"), nativeResults: true, heartbeatSeconds: 1);
        int DestinationRequests() => h.Peer.Requests.Count(body => Parse(body).GetProperty("data")[0].GetProperty("command").GetString() == "sorter.task.dest_request");
        h.Peer.HeartbeatUnavailable = true;
        await h.StartAsync(waitForRcs: false);
        h.App.StateCheckpointInterval = TimeSpan.FromMilliseconds(250);
        var logs = await WaitLog(h, all => all.Any(e => Event(e) is "供包台状态变化" or "供包台状态快照" && Detail(e).GetProperty("Trigger").GetString() == "StartupCompleted"));
        var startup = logs.First(e => Event(e) is "供包台状态变化" or "供包台状态快照" && Detail(e).GetProperty("Trigger").GetString() == "StartupCompleted");
        Check(!State(startup).GetProperty("SoftwarePaused").GetBoolean() && !Position(State(startup), "left").GetProperty("CameraAdmission").GetBoolean() &&
            Position(State(startup), "left").GetProperty("CameraBlockers").EnumerateArray().Any(r => r.GetString() == "RcsRemoteConfirmationUnavailable"),
            "startup checkpoint records blocked camera admission and its missing remote confirmation without claiming software pause");
        h.Peer.HeartbeatUnavailable = false; await h.WaitForCameraAdmissionAsync();
        logs = await WaitLog(h, all => all.Any(e => Event(e) is "供包台状态变化" or "供包台状态快照" &&
            e.GetProperty("Sequence").GetInt64() > startup.GetProperty("Sequence").GetInt64() && Position(State(e), "left").GetProperty("CameraAdmission").GetBoolean()));
        Check(true, "a real heartbeat response records camera admission after the blocked startup baseline");
        logs = await WaitLog(h, all => all.Any(e => Event(e) == "心跳实际响应" && Detail(e).GetProperty("ProtocolConfirmed").GetBoolean()));
        var heartbeat = Detail(logs.Last(e => Event(e) == "心跳实际响应" && Detail(e).GetProperty("ProtocolConfirmed").GetBoolean()));
        var heartbeatRequest = Parse(heartbeat.GetProperty("Request").GetProperty("Text").GetString()!);
        var heartbeatResponse = Parse(heartbeat.GetProperty("Response").GetProperty("Text").GetString()!);
        Check(heartbeatRequest.GetProperty("requestId").GetInt64() == heartbeat.GetProperty("RequestId").GetInt64() &&
            heartbeatResponse.GetProperty("requestId").GetInt64() == heartbeat.GetProperty("RequestId").GetInt64() &&
            heartbeat.GetProperty("HttpStatus").GetInt32() == 200 && heartbeat.GetProperty("ResponseComplete").GetBoolean() &&
            heartbeat.GetProperty("Vehicles").GetArrayLength() == 2 && heartbeat.GetProperty("Positions").GetArrayLength() == 2 &&
            heartbeat.GetProperty("ElapsedMs").GetDouble() >= 0,
            "heartbeat log links actual request/response, HTTP status, completeness, elapsed time, vehicles and current lane membership");
        Check(logs.Any(e => Event(e) == "心跳实际响应" && Detail(e).GetProperty("HttpStatus").ValueKind == JsonValueKind.Number &&
            Detail(e).GetProperty("HttpStatus").GetInt32() == 503 && !Detail(e).GetProperty("ProtocolConfirmed").GetBoolean() &&
            Detail(e).GetProperty("Failure").GetString() == "HTTP 503"),
            "failed HTTP heartbeat retains its actual status and failure instead of a generic offline message");
        h.Peer.HeartbeatTransform = _ => "{\"invalid\":true}";
        Check(await h.App.CheckRcsHeartbeatAsync(default) == RcsHeartbeatResult.Failed, "malformed actual heartbeat is rejected");
        logs = await WaitLog(h, all => all.Any(e => Event(e) == "心跳实际响应" &&
            Detail(e).GetProperty("Response").GetProperty("Text").GetString() == "{\"invalid\":true}"));
        var invalidHeartbeat = Detail(logs.Last(e => Event(e) == "心跳实际响应" &&
            Detail(e).GetProperty("Response").GetProperty("Text").GetString() == "{\"invalid\":true}"));
        Check(invalidHeartbeat.GetProperty("HttpStatus").GetInt32() == 200 && !invalidHeartbeat.GetProperty("ProtocolConfirmed").GetBoolean() &&
            invalidHeartbeat.GetProperty("ResponseComplete").GetBoolean() && invalidHeartbeat.GetProperty("Failure").GetString() == "Heartbeat protocol acknowledgement invalid",
            "HTTP success with invalid identity/protocol retains the exact body and protocol failure independently of connectivity");
        h.Peer.HeartbeatTransform = null;
        await h.WaitForCameraAdmissionAsync();
        var pauseId = Wire.Id();
        Check((await RetryConflictAsync(h, s => h.App.CommandAsync(new(pauseId, s.Revision, "Pause", Value: "SYNTH-WINDOW-PROTECTION")))).Status == "Persisted",
            "pause command is durably committed");
        logs = await WaitLog(h, all => all.Any(e => Event(e) == "软件暂停状态已记录"));
        var pause = Detail(logs.Last(e => Event(e) == "软件暂停状态已记录")).GetProperty("Evidence");
        Check(pause.GetProperty("CommandId").GetString() == pauseId && pause.GetProperty("After").GetBoolean() && !pause.GetProperty("Before").GetBoolean() &&
            pause.GetProperty("Reason").GetString() == "SYNTH-WINDOW-PROTECTION",
            "pause record identifies its command and both actual states");
        var raw = CameraRoundHarness.Barcode("SYNTH-STATE-BLOCKED");
        RecognitionSymbol Symbol(string code, int x = 20) => new(code, "Code128", [new(x, 20), new(x + 100, 20), new(x + 100, 60), new(x, 60)], 0);
        uint nextFrame = 1;
        async Task Feed(string code)
        {
            const int maxAttempts = 10;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                // Software pause deliberately blocks business admission while still allowing recognition.
                await CameraRoundHarness.Until(async () =>
                {
                    var current = await h.App.SnapshotAsync();
                    var now = DateTimeOffset.UtcNow;
                    var lane = current.Positions!.Single(p => p.Id == "left");
                    return lane.Arrival?.CameraBoundaryReady == true &&
                        current.Devices!.Any(d => d.DeviceId == h.Left && d.IsValid(now)) &&
                        current.Health.Any(health => health is { Ready: true, RemoteConfirmation: RemoteConnectionConfirmation.ResponseReceived } &&
                            health.ConfirmedAt <= now && health.ValidUntil > now && health.RemoteConfirmedAt <= now && health.RemoteValidUntil > now);
                });
                var frame = nextFrame++;
                var before = h.Handled(h.Left);
                h.Api(h.Left).EnqueueNative(raw, frame, [Symbol(code)]);
                await h.WaitFrameAsync(h.Left, frame, before);
                var completed = h.Factory.Sources[h.Left].Timings.Snapshot().RecentFrames.Single(f => f.Number == frame);
                if (completed.Delivered) return;
                // The native acquisition may already have entered a preview call when readiness returned.
                if (completed.DecodeAttempted || completed.RejectedFrame)
                    throw new InvalidOperationException($"Frame {frame} for {code} was decoded or rejected without delivery");
            }
            throw new TimeoutException($"No decoded frame delivered for {code}");
        }
        for (var i = 0; i < 12; i++) await Feed("SYNTH-STATE-BLOCKED");
        await Feed("SYNTH-STATE-CHANGED");
        // The changed input's timing event is emitted after the prior observation window is flushed.
        logs = await WaitLog(h, all => all.Any(e => Event(e) == "扫码阶段耗时与队列汇总" &&
            Detail(e).GetProperty("First").GetProperty("Candidates").EnumerateArray().Any(c => c.GetString() == "SYNTH-STATE-CHANGED")));
        var inputs = logs.Where(e => Event(e) == "输入处理完成" && Detail(e).GetProperty("Barcode").GetString() == "SYNTH-STATE-BLOCKED").Select(Detail).ToArray();
        var observations = logs.Where(e => Event(e) == "扫码阶段耗时与队列汇总" &&
            Detail(e).GetProperty("First").GetProperty("Disposition").GetString() == "PausedSamePlacementObservation").Select(Detail).ToArray();
        Check(inputs.Length < 12 && inputs.Sum(d => d.GetProperty("ObservationCount").GetInt32()) + observations.Sum(d => d.GetProperty("Count").GetInt32()) == 12,
            "blocked inputs and presence-only summaries retain the complete delivered observation count without duplicate business writes");
        Check(inputs.All(d => d.GetProperty("BlockReason").GetString() == "SoftwarePaused" &&
            d.GetProperty("RequestDisposition").GetString() == "NoNewRequest") &&
            observations.Any(d => d.GetProperty("First").GetProperty("SourceEventId").GetString() != d.GetProperty("Last").GetProperty("SourceEventId").GetString()),
            "blocked summaries retain exact pause reason and first/last acquisition identities");
        logs = await WaitLog(h, all => all.Any(e => Event(e) == "供包台状态变化" && Position(State(e), "left").GetProperty("Input").ValueKind == JsonValueKind.Object));
        var held = logs.Last(e => Event(e) is "供包台状态变化" or "供包台状态快照");
        Check(State(held).GetProperty("SoftwarePaused").GetBoolean() && !Position(State(held), "left").GetProperty("CameraAdmission").GetBoolean() &&
            Position(State(held), "left").GetProperty("BlockedOriginalImageAvailable").GetBoolean(), "state distinguishes a decoded but unadmitted parcel with an available current original");
        var window = Wire.Id(); await h.App.SetManagementWorkspaceAsync(window, true, default); await h.App.SetManagementWorkspaceAsync(window, false, default);
        logs = await WaitLog(h, all => all.Any(e => Event(e) is "供包台状态变化" or "供包台状态快照" && Detail(e).GetProperty("Trigger").GetString() == "ManagementClosed"));
        var closed = logs.Last(e => Event(e) is "供包台状态变化" or "供包台状态快照" && Detail(e).GetProperty("Trigger").GetString() == "ManagementClosed");
        Check(State(closed).GetProperty("SoftwarePaused").GetBoolean() && State(closed).GetProperty("ManagementWindowId").ValueKind == JsonValueKind.Null,
            "management close proves the workspace fence ended while the independent software pause remained");
        var disableId = Wire.Id();
        Check((await RetryConflictAsync(h, s =>
        {
            var control = s.Positions!.Single(p => p.Id == "right").SupplyControl!;
            return h.App.SetPositionEnabledAsync(new(disableId, s.RuntimeId, "right", control.Revision, false), default);
        })).Status == "Accepted", "right position disable command is accepted");
        logs = await WaitLog(h, all => all.Any(e => Event(e) == "投递位启停已记录"));
        Check(logs.Any(e => Event(e) == "投递位启停已记录" && Detail(e).GetProperty("CommandId").GetString() == disableId), "position switch logs retain their own command and original/current modes");
        var snapshot = await h.App.SnapshotAsync(); var conflictId = Wire.Id();
        var conflict = await h.App.CommandAsync(new(conflictId, snapshot.Revision - 1, "Resume"));
        Check(conflict.Status == "Conflict" && (await h.App.SnapshotAsync()).Paused, "rejected stale resume never changes software pause");
        logs = await WaitLog(h, all => all.Any(e => Event(e) == "供包命令结果" && Detail(e).GetProperty("CommandId").GetString() == conflictId));
        Check(logs.Any(e => Event(e) == "供包命令结果" && Detail(e).GetProperty("Status").GetString() == "Conflict"), "command-result logs distinguish CAS conflict from a successful resume");
        var resumeId = Wire.Id();
        Check((await RetryConflictAsync(h, s => h.App.CommandAsync(new(resumeId, s.Revision, "Resume")))).Status == "Persisted",
            "resume command is durably committed");
        await h.WaitForCameraAdmissionAsync(h.Left);
        await Feed("SYNTH-STATE-CHANGED");
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.State == ParcelState.SlotResolved));
        var operation = (await h.App.SnapshotAsync()).Operations.Single();
        logs = await WaitLog(h, all => all.Any(e => Event(e) == "取格口结果"));
        Check(logs.Where(e => Event(e) == "开始请求格口").Any(e => e.GetProperty("OperationId").GetString() == operation.Id &&
            Detail(e).GetProperty("ProtocolRequestId").GetInt64() == operation.Wcs!.Request.IntegrationRequest!.RequestId &&
            Detail(e).GetProperty("Target").GetString() == operation.Wcs.Request.Binding.Target), "send record links the exact job, protocol number and frozen target");
        Check(logs.Any(e => e.TryGetProperty("Source", out var source) && source.GetString() == "作业" &&
            e.GetProperty("OperationId").GetString() == operation.Id && Detail(e).GetProperty("State").GetString() == "SlotResolved"),
            "ordinary key events retain correlated job-stage transitions");
        var stableSequence = logs.Where(e => e.TryGetProperty("Sequence", out _)).Max(e => e.GetProperty("Sequence").GetInt64());
        logs = await WaitLog(h, all => all.Any(e => Event(e) == "供包台状态快照" && Detail(e).GetProperty("Trigger").GetString() == "RuntimeMonitor" &&
            e.GetProperty("Sequence").GetInt64() > stableSequence && Position(State(e), "left").GetProperty("CurrentOperation") is { ValueKind: JsonValueKind.Object } currentOperation && currentOperation.GetProperty("OperationId").GetString() == operation.Id));
        Check(true, "stable state publishes a newer periodic reconstruction baseline with the current operation without debug logging");
        var runtime = logs.Where(e => e.TryGetProperty("RuntimeId", out _)).ToArray();
        Check(runtime.All(e => e.GetProperty("RuntimeId").GetGuid() == snapshot.RuntimeId) &&
            runtime.Select(e => e.GetProperty("Sequence").GetInt64()).SequenceEqual(runtime.Select(e => e.GetProperty("Sequence").GetInt64()).Order()),
            "runtime identity and monotonic record sequence order ordinary state, frame and request events");
        Check(DestinationRequests() == 1, "logging, queries, checkpoints and resume never replay the blocked input");
        var beforeOutage = runtime.Max(e => e.GetProperty("Sequence").GetInt64());
        h.Peer.HeartbeatUnavailable = true;
        logs = await WaitLog(h, all => all.Any(e => Event(e) is "供包台状态变化" or "供包台状态快照" &&
            e.GetProperty("Sequence").GetInt64() > beforeOutage &&
            Position(State(e), "left").GetProperty("Camera").GetProperty("DecodePolicy").GetString() == "RcsUnavailablePreviewOnly"));
        var offline = logs.Last(e => Event(e) is "供包台状态变化" or "供包台状态快照" && e.GetProperty("Sequence").GetInt64() > beforeOutage &&
            Position(State(e), "left").GetProperty("Camera").GetProperty("DecodePolicy").GetString() == "RcsUnavailablePreviewOnly");
        Check(!State(offline).GetProperty("SoftwarePaused").GetBoolean() && !Position(State(offline), "left").GetProperty("CameraAdmission").GetBoolean(),
            "RCS offline checkpoint distinguishes preview-only decode gating from software pause");
        h.Peer.HeartbeatUnavailable = false;
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Arrival?.CameraBoundaryReady == true);
        logs = await WaitLog(h, all => all.Any(e => Event(e) is "供包台状态变化" or "供包台状态快照" &&
            e.GetProperty("Sequence").GetInt64() > offline.GetProperty("Sequence").GetInt64() &&
            Position(State(e), "left").GetProperty("Camera").GetProperty("DecodePolicy").GetString() == "Automatic"));
        Check(DestinationRequests() == 1, "RCS recovery records automatic decode restoration without a new parcel request");
        h.Peer.ReplySlot = "";
        await h.TrayAsync(h.Left, TrayPresence.Absent); await SmRoundFixture.NextVehicleAsync(h.App, "left");
        await h.WaitForCameraAdmissionAsync(h.Left);
        await Feed("SYNTH-STATE-UNKNOWN");
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.Wcs?.State == StageState.Unknown));
        var unknown = (await h.App.SnapshotAsync()).Operations.Single(o => o.Wcs?.State == StageState.Unknown);
        logs = await WaitLog(h, all => all.Any(e => Event(e) is "供包台状态变化" or "供包台状态快照" &&
            Position(State(e), "left").GetProperty("OriginalResponsibilities").EnumerateArray().Any(o => o.GetProperty("Result").GetString() == "Unknown")));
        var original = logs.Where(e => Event(e) is "供包台状态变化" or "供包台状态快照")
            .Select(e => Position(State(e), "left")).SelectMany(p => p.GetProperty("OriginalResponsibilities").EnumerateArray())
            .Last(o => o.GetProperty("Result").GetString() == "Unknown");
        Check(original.GetProperty("RequestId").GetString() == unknown.Wcs!.Request.Id && original.GetProperty("Attempts").GetInt32() == 1 &&
            original.GetProperty("PayloadHash").GetString() == unknown.Wcs.Request.PayloadHash && DestinationRequests() == 2,
            "an unconfirmed result snapshot retains its exact original request, frozen payload identity and one attempt");
        h.Api(h.Left).DestroyError = unchecked((int)0x80000214);
        try { await h.App.StopAsync(default); throw new InvalidOperationException("Synthetic destroy failure was accepted"); }
        catch (AggregateException) { }
        h.Api(h.Left).DestroyError = 0; await h.App.StopAsync(default); await h.App.DisposeAsync();
        var folder = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, "sportmaster", "diagnostics");
        var final = Directory.GetFiles(folder, "runtime.jsonl*").Where(p => !p.EndsWith(".lease", StringComparison.Ordinal)).SelectMany(File.ReadLines)
            .Select(Parse).ToArray();
        Check(final.Any(e => Event(e) == "供包台停止失败，所有权仍须核对" && !Detail(e).GetProperty("ResourcesReleased").GetBoolean()), "failed SDK destruction logs retained ownership rather than a false released state");
        Check(final.Any(e => Event(e) is "供包台状态变化" or "供包台状态快照" && Detail(e).GetProperty("Trigger").GetString() == "StopCompleted" &&
            State(e).GetProperty("DevicePortsReleased").GetBoolean() && !State(e).GetProperty("ApplicationOwnershipReleased").GetBoolean()), "stop checkpoint distinguishes released device ports from application ownership not yet disposed");
    }

    static void VerifyDurablePause(string root)
    {
        var command = Wire.Id(); var called = 0;
        using (var repository = new ParcelRepository(root, "sportmaster", ["left", "right"]))
        {
            repository.SoftwarePauseCommitted = _ => called++;
            repository.SoftwarePauseActor = () => "SYNTH-OPERATOR";
            repository.Boundary = point => { if (point == "BeforeBusinessCommit:SyntheticFailedPause") throw new IOException("Synthetic transaction rollback"); };
            try { repository.Commit(null, "SyntheticFailedPause", "must roll back", paused: true); throw new InvalidOperationException("Pause did not fail"); }
            catch (IOException) { }
            Check(!repository.Paused && called == 0, "failed pause transaction emits no committed state or success record");
            repository.Boundary = null;
            repository.Commit(null, "AdmissionPaused", "Synthetic operator pause", paused: true, command: new(command, repository.Revision, "Pause"));
            Check(called == 1, "pause notification occurs only after the actual transaction commits");
        }
        using var reopened = new ParcelRepository(root, "sportmaster", ["left", "right"]);
        reopened.LoadSoftwarePauseEvidence();
        Check(reopened.Paused && reopened.LastSoftwarePause is { After: true, Event: "AdmissionPaused", Actor: "SYNTH-OPERATOR" } record && record.CommandId == command,
            "reopening retains the original pause command and reason without a new schema or changing business state");
    }
}
