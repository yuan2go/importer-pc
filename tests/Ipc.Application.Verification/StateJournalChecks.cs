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
        var page = await h.App.QueryLogsAsync(new(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1)) { KeyEvents = true }, null, 0, default);
        try { return page.Entries.Select(e => Parse(e.Text)).ToArray(); }
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
        var snapshot = await h.App.SnapshotAsync(); var pauseId = Wire.Id();
        Check((await h.App.CommandAsync(new(pauseId, snapshot.Revision, "Pause", Value: "SYNTH-WINDOW-PROTECTION"))).Status == "Persisted", "pause command is durably committed");
        logs = await WaitLog(h, all => all.Any(e => Event(e) == "软件暂停状态已记录"));
        var pause = Detail(logs.Last(e => Event(e) == "软件暂停状态已记录")).GetProperty("Evidence");
        Check(pause.GetProperty("CommandId").GetString() == pauseId && pause.GetProperty("After").GetBoolean() && !pause.GetProperty("Before").GetBoolean() &&
            pause.GetProperty("Reason").GetString() == "SYNTH-WINDOW-PROTECTION",
            "pause record identifies its command and both actual states");
        var raw = CameraRoundHarness.Barcode("SYNTH-STATE-BLOCKED");
        RecognitionSymbol Symbol(string code, int x = 20) => new(code, "Code128", [new(x, 20), new(x + 100, 20), new(x + 100, 60), new(x, 60)], 0);
        async Task Feed(string code, uint frame)
        {
            h.Api(h.Left).EnqueueNative(raw, frame, [Symbol(code)]);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Inputs!.Any(i => i.PositionId == "left" && i.Observation.SourceEventId == "mvid.frame." + frame));
        }
        for (uint i = 1; i <= 12; i++) await Feed("SYNTH-STATE-BLOCKED", i);
        await Feed("SYNTH-STATE-CHANGED", 13);
        logs = await WaitLog(h, all => all.Any(e => Event(e) == "输入处理完成" && Detail(e).GetProperty("Barcode").GetString() == "SYNTH-STATE-CHANGED"));
        var inputs = logs.Where(e => Event(e) == "输入处理完成" && Detail(e).GetProperty("Barcode").GetString() == "SYNTH-STATE-BLOCKED").Select(Detail).ToArray();
        Check(inputs.Length < 12 && inputs.Sum(d => d.GetProperty("ObservationCount").GetInt32()) == 12,
            "repeated blocked scans aggregate without losing the actual delivered observation count");
        Check(inputs.All(d => d.GetProperty("Reason").GetString()!.Contains("Global software pause")) &&
            inputs.Any(d => d.GetProperty("FirstSourceEventId").GetString() != d.GetProperty("SourceEventId").GetString()),
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
        snapshot = await h.App.SnapshotAsync();
        var control = snapshot.Positions!.Single(p => p.Id == "right").SupplyControl!;
        var disableId = Wire.Id(); await h.App.SetPositionEnabledAsync(new(disableId, snapshot.RuntimeId, "right", control.Revision, false), default);
        logs = await WaitLog(h, all => all.Any(e => Event(e) == "投递位启停已记录"));
        Check(logs.Any(e => Event(e) == "投递位启停已记录" && Detail(e).GetProperty("CommandId").GetString() == disableId), "position switch logs retain their own command and original/current modes");
        snapshot = await h.App.SnapshotAsync(); var conflictId = Wire.Id();
        var conflict = await h.App.CommandAsync(new(conflictId, snapshot.Revision - 1, "Resume"));
        Check(conflict.Status == "Conflict" && (await h.App.SnapshotAsync()).Paused, "rejected stale resume never changes software pause");
        logs = await WaitLog(h, all => all.Any(e => Event(e) == "供包命令结果" && Detail(e).GetProperty("CommandId").GetString() == conflictId));
        Check(logs.Any(e => Event(e) == "供包命令结果" && Detail(e).GetProperty("Status").GetString() == "Conflict"), "command-result logs distinguish CAS conflict from a successful resume");
        snapshot = await h.App.SnapshotAsync();
        await h.App.CommandAsync(new(Wire.Id(), snapshot.Revision, "Resume"));
        await Feed("SYNTH-STATE-CHANGED", 14);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.State == ParcelState.SlotResolved));
        var operation = (await h.App.SnapshotAsync()).Operations.Single();
        logs = await WaitLog(h, all => all.Any(e => Event(e) == "取格口结果"));
        Check(logs.Where(e => Event(e) == "开始请求格口").Any(e => e.GetProperty("OperationId").GetString() == operation.Id &&
            Detail(e).GetProperty("ProtocolRequestId").GetInt64() == operation.Wcs!.Request.IntegrationRequest!.RequestId &&
            Detail(e).GetProperty("Target").GetString() == operation.Wcs.Request.Binding.Target), "send record links the exact job, protocol number and frozen target");
        Check(logs.Any(e => e.TryGetProperty("Source", out var source) && source.GetString() == "作业" &&
            e.GetProperty("OperationId").GetString() == operation.Id && Detail(e).GetProperty("State").GetString() == "SlotResolved"),
            "ordinary key events retain correlated job-stage transitions");
        logs = await WaitLog(h, all => all.Any(e => Event(e) == "供包台状态快照" && Detail(e).GetProperty("Trigger").GetString() == "RuntimeMonitor"));
        Check(logs.Any(e => Event(e) == "供包台状态快照"), "stable state publishes a periodic reconstruction baseline without debug logging");
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
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").BarcodeInput?.Camera == true);
        logs = await WaitLog(h, all => all.Any(e => Event(e) is "供包台状态变化" or "供包台状态快照" &&
            e.GetProperty("Sequence").GetInt64() > offline.GetProperty("Sequence").GetInt64() &&
            Position(State(e), "left").GetProperty("Camera").GetProperty("DecodePolicy").GetString() == "Automatic"));
        Check(DestinationRequests() == 1, "RCS recovery records automatic decode restoration without a new parcel request");
        h.Peer.ReplySlot = "";
        await Feed("SYNTH-STATE-UNKNOWN", 15);
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
