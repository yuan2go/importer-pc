using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static class UiSpec24
{
    static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); Console.WriteLine("PASS UI24 " + message); }
    public static async Task Run(string workspace, string root)
    {
        var original = new SportmasterFlow().InitialConfiguration(); var rules = original with { BarcodeFilter = new("^SYNTH.*$") };
        Check(ConfigurationActivation.RulesOnly("sportmaster", original, null, rules), "rules-only full candidate predicts live activation");
        Check(!ConfigurationActivation.RulesOnly("sportmaster", original, original, rules), "existing Pending forbids live rule prediction");
        Check(!ConfigurationActivation.RulesOnly("sportmaster", original, null, rules with { TimeoutMs = original.TimeoutMs + 1 }), "mixed rules and connection budget predict whole-candidate restart");
        Check(!ConfigurationActivation.RulesOnly("directplc", original, null, rules), "other applications cannot acquire SM hot activation");
        var frameWindow = new DecodeWindow(); frameWindow.Frame(); frameWindow.Frame(); frameWindow.Decoded(true); frameWindow.Decoded(false);
        var sample = frameWindow.Read();
        Check(sample is { Frames: 2, DecodedFrames: 2, FramesWithCodes: 1 }, "frame-based numerator counts a frame once independently of its barcode candidate count");
        Check(new DecodeWindow().Read() is { Frames: 0, DecodedFrames: 0, FramesWithCodes: 0 }, "new connection window starts empty and cannot retain old connection rates");

        var management = new LocalManagementMode(Path.Combine(root, "renewal")); var clock = DateTimeOffset.UtcNow;
        typeof(LocalManagementMode).GetProperty("Clock", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(management, (Func<DateTimeOffset>)(() => clock));
        try { management.Renew(); throw new Exception("unauthorized renewal accepted"); } catch (UnauthorizedAccessException) { }
        Check(true, "renewal rejects before password authentication");
        management.ConfigurePassword("SYNTH-Ui24-Password"); Check(management.Enter("SYNTH-Ui24-Password"), "synthetic management authority authenticates");
        var before = management.Status(); var originalEntered = management.Evidence!.EnteredAt; var authority = management.Require(); var delegation = management.Delegate(Wire.Hash("synthetic-operation"));
        clock = clock.AddMinutes(14); var renewed = management.Renew();
        Check(renewed.SessionId == before.SessionId && renewed.ExpiresAt == clock.AddMinutes(15) && !authority.IsCancellationRequested, "renewal preserves session ownership and reschedules its original authority token");
        try { management.ValidateDelegation(delegation, Wire.Hash("synthetic-operation"), false); throw new Exception("old ticket extended"); } catch (UnauthorizedAccessException) { }
        Check(true, "renewal never extends an already-issued delegation deadline");
        Check(management.Evidence?.EnteredAt == originalEntered, "renewal preserves the original authentication timestamp in audit evidence");
        clock = clock.AddMinutes(16);
        try { management.Renew(); throw new Exception("expired renewal accepted"); } catch (UnauthorizedAccessException) { }
        Check(true, "expired authority cannot be revived by renewal");
        management.Exit();

        var op = Wire.Id();
        var entry = SoftwareLogFacts.Enrich(new(new("f", 1, "hash"), "RCS结果", DateTimeOffset.UtcNow, "信息", Wire.Encode(new { Event = "取格口结果", Detail = Wire.Encode(new { OperationId = op, ParcelCode = "SYNTH" }) })));
        Check(entry.OperationId == op && entry.EventCode == "取格口结果", "operation jump identity comes from structured original fields");
        var query = new SoftwareLogQuery(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1)) { Levels = ["警告", "信息"], Sources = ["RCS结果"], OperationId = op, KeyEvents = true };
        Check(SoftwareLogFacts.Matches(entry, query) && !SoftwareLogFacts.Matches(entry, query with { Levels = ["错误"] }) && !SoftwareLogFacts.Matches(entry, query with { OperationId = Wire.Id() }), "multi-level/source and original operation filters combine without broadening identity");
        Check(SoftwareLogFacts.Enrich(entry with { Text = "not structured " + op, OperationId = null }).OperationId is null, "free-text GUIDs never become operation jump authority");

        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "runtime"), deferred: true); await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        var initial = await h.App.SnapshotAsync();
        var live = await h.App.StageConfigurationAsync(initial.Configuration.Revision, initial.Configuration.Effective with { BarcodeFilter = new("^SYNTH.*$") }, "验证规则直接生效");
        Check(live.Status == "Effective" && (await h.App.SnapshotAsync()).Configuration.Pending is null, "executing Application confirms the same rules-only activation mode");
        var active = await h.App.SnapshotAsync();
        var mixed = await h.App.StageConfigurationAsync(active.Configuration.Revision, active.Configuration.Effective with { TimeoutMs = active.Configuration.Effective.TimeoutMs + 1, BarcodeFilter = new("^OTHER.*$") }, "验证混合修改待重启");
        var saved = await h.App.SnapshotAsync();
        Check(mixed.Status == "PendingRestart" && saved.Configuration.Pending is not null && saved.Configuration.Effective.TimeoutMs == active.Configuration.Effective.TimeoutMs && saved.Configuration.Effective.BarcodeFilter == active.Configuration.Effective.BarcodeFilter, "mixed save leaves both original live rules and timeout intact until restart");
        var settings = (IRuntimeDiagnosticsControl)h.App;
        typeof(ParcelRuntime).GetField("DebugLoggingLifetime", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(h.App, TimeSpan.FromMilliseconds(150));
        var enabled = await settings.SetDebugLoggingAsync(true);
        Check(enabled.DebugEnabled && enabled.ExpiresAt is not null, "debug enable returns authoritative automatic expiry");
        await CameraRoundHarness.Until(async () => !(await settings.DiagnosticSettingsAsync()).DebugEnabled);
        Check((await settings.DiagnosticSettingsAsync()).Revision > enabled.Revision, "runtime closes detailed logging without any UI page or timer");
        await h.App.ExitManagementModeAsync();
        try { await settings.SetDebugLoggingAsync(true); throw new Exception("unauthorized logging accepted"); } catch (UnauthorizedAccessException) { }
        Check(true, "detailed logging still requires execution-time management authorization");
        var stats = await ((IStationOperations)h.App).ReadStatisticsAsync(Minute(DateTimeOffset.UtcNow).AddMinutes(-1), Minute(DateTimeOffset.UtcNow).AddMinutes(1), default);
        Check(stats.Buckets.Sum(x => x.Requests) == 0, "diagnostics, renewal and configuration never create a business request");
        var production = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-HISTORY-BATCH"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(production.OperationId!))?.Wcs?.State == StageState.Accepted);
        var selection = await ((IParcelHistorySessionApplication)h.App).SelectHistoryAsync(new());
        var batch = await ((IParcelHistoryBatchApplication)h.App).ReadHistoryBatchAsync(selection, long.MaxValue, 50, default);
        Check(batch.Page.Items.Length == 1 && batch.Metrics.Operations == 1 && batch.Metrics.WcsAccepted == 1 && batch.Page.Items[0].Operation.Wcs?.State == StageState.Accepted && h.Peer.Requests.Count == 1, "same short SQLite read transaction returns the page and its aggregate without another RCS request");
        await using var camera = new CameraRoundHarness(workspace, Path.Combine(root, "continuous-trial")); await camera.StartAsync();
        var cameraSnapshot = await camera.App.SnapshotAsync();
        var scope = await camera.App.BeginCameraInspectionAsync(new(Wire.Id(), cameraSnapshot.RuntimeId, cameraSnapshot.Configuration.Revision, CameraInspectionMode.ObserveProduction, "合成连续二十帧试扫验证", new(1, 64, 1000), true), default);
        Check((await camera.App.SnapshotAsync()).Paused == cameraSnapshot.Paused && !scope.BusinessFenced, "continuous trial leaves both production positions and pause state unchanged");
        var pixels = CameraRoundHarness.Barcode("SYNTH-CONTINUOUS");
        for (var i = 0; i < 20; i++)
        {
            camera.Api(camera.Left).Enqueue(i % 2 == 0 ? pixels : new(new byte[100 * 80], 100, 80), (uint)(i + 1));
            var count = i + 1;
            await CameraRoundHarness.Until(() => Task.FromResult(camera.Factory.Decoders[camera.Left].Count >= count));
        }
        await camera.App.StopCameraInspectionCaptureAsync(scope.SessionId!, "合成连续试扫结束采集", default);
        var framePage = await camera.App.ReadCameraFramesAsync(scope.SessionId!, camera.Left, 0, null, 200, default);
        Check(framePage.Rows.Length == 20 && framePage.Rows.Count(f => f.Symbols.Length > 0) == 10, "real ZXing and inspection store retain twenty independent frames including ten empty frames");
        using (var diagnostics = await camera.App.ReadCameraPreviewLeaseAsync(camera.Left, default))
            Check(diagnostics.Diagnostics?.Window is { DecodedFrames: 20, FramesWithCodes: 10 }, "driver rolling diagnostics use decoded frame counts rather than candidate or physical parcel counts");
        await CameraRoundHarness.Until(async () => (await camera.App.SnapshotAsync()).Operations.Count(o => o.Wcs?.State == StageState.Accepted) == 1);
        Check(camera.Peer.Requests.Count == 1 && (await camera.App.SnapshotAsync()).Operations.Length == 1, "passive sampling adds no debug POST while normal same-placement production creates its one request");
        await camera.App.EndCameraInspectionAsync(new(Wire.Id(), scope.SessionId!, false, false, "合成结束采集，不改变正常供包"), default);
        Check((await camera.App.SnapshotAsync()).Paused == cameraSnapshot.Paused && !(await camera.App.CameraInspectionStatusAsync(default)).BusinessFenced, "ending the trial requires no clearance and leaves normal production state unchanged");
    }
    static DateTimeOffset Minute(DateTimeOffset value) => new(value.UtcDateTime.AddTicks(-(value.UtcTicks % TimeSpan.TicksPerMinute)), TimeSpan.Zero);
}
