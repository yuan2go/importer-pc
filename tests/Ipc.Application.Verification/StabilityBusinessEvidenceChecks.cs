using System.Diagnostics;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

internal static partial class StabilityAnalysis
{
    static async Task VerifyManualBusinessEvidence(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, root, deferred: false, mode: RunMode.Simulation);
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config with
        { Wcs = h.Config.Wcs! with { Parameters = new(h.Config.Wcs.Parameters) { ["request-timeout-ms"] = "10000" } } });
        await h.StartAsync();
        foreach (var camera in h.Factory.Cameras.Values) camera.ConfirmConnected();
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.All(p => p.BarcodeInput?.Camera == true));
        var left = h.Factory.Cameras[h.Config.Topology!.Readers[0].DeviceId];
        async Task Observe(string[] codes, TrayPresence? presence = null)
        {
            var before = h.App.WorkerProgress().Single(w => w.Position == "left").Processed;
            if (!left.Read(codes, presence is { } fact ? new(fact, "synthetic-analysis-presence") : null)) throw new InvalidOperationException("Controlled source did not hand off");
            await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(w => w.Position == "left").Processed > before));
        }
        async Task Complete(string id) => await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(id))?.Wcs?.State == StageState.Accepted);
        async Task<ParcelBarcodeReceipt> Submit(string side, string code, ParcelBarcodeInputKind kind = ParcelBarcodeInputKind.Keyboard)
            => await h.App.SubmitParcelBarcodeAsync(await h.Command(side, code, kind), default);

        h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Peer.HoldWhen = body => body.Contains("SYNTH-PRIOR", StringComparison.Ordinal);
        var prior = await Submit("left", "SYNTH-PRIOR");
        await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 1));
        await SmRoundFixture.ConfirmRemovedAsync(h.App, "left");
        var first = h.App.RuntimeProgress(); var started = DateTimeOffset.UtcNow; var clock = Stopwatch.StartNew();
        var manual = await Submit("right", "SYNTH-PURE-MANUAL"); await Complete(manual.OperationId!);
        var manualEnd = DateTimeOffset.UtcNow; var manualLast = h.App.RuntimeProgress();
        await SmRoundFixture.ConfirmRemovedAsync(h.App, "right");
        h.Peer.Hold.TrySetResult(); h.Peer.Hold = null; h.Peer.HoldWhen = null; await Complete(prior.OperationId!);

        await Observe([], TrayPresence.Present);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Rounds.Any(r => r.PositionId == "left" && r.AutoExpired));
        var pda = await Submit("left", "SYNTH-TIMEOUT-PDA", ParcelBarcodeInputKind.PdaKeyboard); await Complete(pda.OperationId!);
        await Observe([], TrayPresence.Absent);
        await Observe(["SYNTH-AUTOMATIC"]);
        string? automatic = null;
        await CameraRoundHarness.Until(async () => (automatic = (await h.App.SnapshotAsync()).Rounds.SingleOrDefault(r => r.PositionId == "left")?.OperationId) is not null);
        await Complete(automatic!); await Observe([], TrayPresence.Absent);

        h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Peer.HoldWhen = body => body.Contains("SYNTH-LATE", StringComparison.Ordinal);
        var late = await Submit("right", "SYNTH-LATE");
        await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 5));
        await SmRoundFixture.ConfirmRemovedAsync(h.App, "right");
        var ended = DateTimeOffset.UtcNow; var last = h.App.RuntimeProgress(); var snapshot = await h.App.SnapshotAsync();
        h.Peer.Hold.TrySetResult(); h.Peer.Hold = null; h.Peer.HoldWhen = null; await Complete(late.OperationId!);
        await ManagementModeFixture.Authorize(h.App);
        await h.App.CommandAsync(new(Wire.Id(), (await h.App.SnapshotAsync()).Revision, "Pause"));
        var backup = Path.Combine(root, "business.db");
        BusinessReceipt? receipt = null;
        await CameraRoundHarness.Until(async () => (receipt = await h.App.BackupAsync(backup)).Status == "BackupCreated");
        Check(receipt?.Status == "BackupCreated", "manual/mixed evidence uses a production-verified complete backup with original requests and receipts");

        string Collection(string name, DateTimeOffset end, object final)
        {
            var path = Path.Combine(root, name); Directory.CreateDirectory(path);
            ExtensionStore.AtomicWrite(Path.Combine(path, "identity.json"), new { RuntimeId = snapshot.RuntimeId, SourceCommit = new string('a', 40) });
            ExtensionStore.AtomicWrite(Path.Combine(path, "collection.json"), new
            {
                RuntimeId = snapshot.RuntimeId,
                SourceCommit = new string('a', 40),
                NormalStart = started,
                NormalEnd = end,
                ActualNormalSeconds = clock.Elapsed.TotalSeconds,
                Status = "SMOKE_ONLY",
                FirstNormal = first,
                LastNormal = final,
                Failures = new { },
                Ui = Array.Empty<object>()
            });
            File.WriteAllText(Path.Combine(path, "samples.jsonl"), JsonSerializer.Serialize(new { Progress = final }, new JsonSerializerOptions(Wire.Json) { WriteIndented = false }) + "\n");
            return path;
        }
        var manualOutput = Path.Combine(root, "manual-analysis.json");
        var manualResult = await Analyze(workspace, Collection("manual", manualEnd, manualLast), backup, manualOutput);
        Check(manualResult.Exit == 0, "a pure manual window is analyzed without requiring camera ingress" + (manualResult.Exit == 0 ? "" : ": " + manualResult.Text));
        using (var report = JsonDocument.Parse(File.ReadAllText(manualOutput)))
        {
            var counts = report.RootElement.GetProperty("BusinessEventsInWindow");
            Check(report.RootElement.GetProperty("Counts").GetArrayLength() == 0 && counts.GetProperty("ManualCommandsSubmitted").GetInt32() == 1 &&
                counts.GetProperty("RequestsPrepared").GetInt32() == 1 && counts.GetProperty("CallsCompleted").GetInt32() == 1,
                "manual commands, rounds, prepared requests and calls use their own timestamps; no-camera is not no-business");
        }
        var mixedOutput = Path.Combine(root, "mixed-analysis.json");
        var mixed = await Analyze(workspace, Collection("mixed", ended, last), backup, mixedOutput);
        Check(mixed.Exit == 0, "mixed camera/PDA and cross-window calls pass the real offline analysis entry" + (mixed.Exit == 0 ? "" : ": " + mixed.Text));
        using (var report = JsonDocument.Parse(File.ReadAllText(mixedOutput)))
        {
            var facts = report.RootElement.GetProperty("Counters").GetProperty("RcsFacts"); var counts = report.RootElement.GetProperty("BusinessEventsInWindow");
            Check(counts.GetProperty("ManualCommandsSubmitted").GetInt32() == 3 && counts.GetProperty("RequestsPrepared").GetInt32() == 4 &&
                report.RootElement.GetProperty("SendAttemptsInWindow").GetInt32() == 4 && counts.GetProperty("CallsCompleted").GetInt32() == 4 &&
                facts.GetProperty("SnapshotOutcome:Accepted").GetInt32() == 5 && facts.GetProperty("ReceiptOutsideRuntimeWindow").GetInt32() == 1,
                "prior-window request completion counts in-window, late completion stays outside, and final accepted snapshots do not rewrite either event window");
            Check(report.RootElement.GetProperty("Evidence").GetProperty("MissingAssociations").GetArrayLength() == 0 &&
                report.RootElement.GetProperty("Counters").GetProperty("LinkedRequestIds").EnumerateObject().Count() == 5,
                "camera, timeout-PDA and manual requests join by runtime/round/command/operation/request identities without barcode guesses");
        }
        var directory = Path.Combine(root, "foreign"); Directory.CreateDirectory(directory);
        var valid = JsonSerializer.SerializeToNode(JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "mixed", "collection.json"))).RootElement)!;
        var foreign = Guid.NewGuid(); valid["RuntimeId"] = foreign.ToString(); valid["FirstNormal"]!["RuntimeId"] = foreign.ToString(); valid["LastNormal"]!["RuntimeId"] = foreign.ToString();
        File.WriteAllText(Path.Combine(directory, "collection.json"), valid.ToJsonString());
        ExtensionStore.AtomicWrite(Path.Combine(directory, "identity.json"), new { RuntimeId = foreign, SourceCommit = new string('a', 40) });
        var denied = Path.Combine(root, "foreign.json"); var attempt = await Analyze(workspace, directory, backup, denied);
        Check(attempt.Exit != 0 && !File.Exists(denied) && !File.Exists(Path.Combine(root, "foreign-business-events.jsonl")),
            "an unrelated runtime still refuses and publishes no partial business evidence; manual support does not waive runtime ownership");

        // Exercise real detail retention, not a hand-written imitation of the
        // analyzer's missing-operation classification. Original backup stays intact.
        await h.App.StopAsync(default);
        var data = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, h.Config.ApplicationId);
        var retainedBackup = Path.Combine(root, "retained-business.db");
        ParcelRetentionResult retention;
        using (var repository = new ParcelRepository(data, h.Config.ApplicationId))
        {
            repository.Archive(repository.Load().Select(o => o.Id).ToArray(), "Synthetic analysis retention control");
            retention = repository.ExpireHistoryCore(new SportmasterFlow(), 7, DateTimeOffset.UtcNow.AddDays(8), Wire.FileHash(backup), () => { });
            var envelope = ConfigurationRevisions<ParcelConfiguration>.ReadCurrentEnvelope(File.ReadAllText(Path.Combine(data, "configuration.json")));
            ParcelRecovery.CreateBackup(data, retainedBackup, envelope.Effective, envelope.Pending, repository.StorageBytes, repository.Backup, (_, _) => { });
        }
        var retainedOutput = Path.Combine(root, "retained-analysis.json");
        var retained = await Analyze(workspace, Path.Combine(root, "mixed"), retainedBackup, retainedOutput);
        if (retained.Exit != 0) throw new InvalidOperationException("Actual retention backup analysis refused: " + retained.Text);
        using (var report = JsonDocument.Parse(File.ReadAllText(retainedOutput)))
            Check(retention.Operations == 5 && report.RootElement.GetProperty("SendAttemptsInWindow").ValueKind == JsonValueKind.Null &&
                report.RootElement.GetProperty("Evidence").GetProperty("MissingAssociations").EnumerateArray().Count(g => g.GetProperty("Kind").GetString() == "OperationMissing") == 5 &&
                h.Peer.Requests.Count == 5,
                "real history retention preserves rounds and permanent request custody; missing operation details make attempt totals unknown, never zero or a resend");
    }
}
