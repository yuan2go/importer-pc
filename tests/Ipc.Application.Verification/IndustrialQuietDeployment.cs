using Ipc.Platform;

static class IndustrialQuietDeployment
{
    public static void Run(string root)
    {
        var checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL IE01-QUIET " + message); checks++; Console.WriteLine("PASS IE01-QUIET " + message); }
        void Reject<T>(Action action, string message) where T : Exception
        { try { action(); } catch (T) { Check(true, message); return; } throw new Exception("Expected " + typeof(T).Name + ": " + message); }
        var id = Wire.Id(); var data = Path.Combine(root, "data"); var log = Path.Combine(root, "command.json");
        var intent = new QuietDeploymentIntent("sportmaster", id, "Install", data, log, new string('a', 40), new string('b', 64));
        intent.Validate();
        Check(intent.Identity == Wire.Decode<QuietDeploymentIntent>(Wire.Encode(intent)).Identity, "canonical intent survives actual JSON round trip without recursive identity serialization");
        Check(intent.Identity != (intent with { MediaInventorySha256 = new string('c', 64) }).Identity, "same source with different package inventory is a payload conflict");
        var initial = new QuietDeploymentRecord(1, intent, "Reserved");
        WindowsDeploymentSetup.ValidateQuietRecord(initial, intent); Check(true, "reserved original validates before any deployment work");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ValidateQuietRecord(initial, intent with { LogFile = log + ".other" }), "same operation cannot move its output log");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ValidateQuietRecord(initial, intent with { RequestedDataRoot = data + "-other" }), "same operation cannot silently change requested data root");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ValidateQuietRecord(initial, intent with { Action = "Repair" }), "same operation cannot change action to repair");
        Reject<ArgumentException>(() => (intent with { MediaSourceCommit = null }).Validate(), "missing original media identity refused");
        Reject<ArgumentException>(() => (intent with { Action = "Uninstall" }).Validate(), "uninstall cannot carry a replacement media or data override");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ValidateQuietRecord(initial with { ExpectedRegistrationHash = "bad" }, intent), "invalid original registration digest refused");
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ValidateQuietRecord(initial with { Status = "InventedSuccess" }, intent), "unknown acknowledgement state cannot authorize replay");
        var request = new WindowsDeploymentRequest(Path.Combine(root, "media"), "sportmaster", intent.MediaSourceCommit!, "synthetic", data,
            Path.Combine(root, "seed"), RunMode.Production, null, null, [], OperationId: id);
        var recordFile = Path.Combine(root, "protected-record-stand-in.json");
        var saved = initial;
        void Save(QuietDeploymentRecord record) { File.WriteAllText(recordFile, Wire.Encode(record)); saved = Wire.Decode<QuietDeploymentRecord>(File.ReadAllText(recordFile)); }
        var originalStatus = "Unknown";
        using (var operation = new QuietDeploymentOperation(initial, false, new ResourceLease(root, ["quiet-operation-test"]), Save, record => WindowsDeploymentSetup.ProjectQuietOutcome(record, originalStatus)))
        {
            operation.FreezeInstallation(request);
            Check(saved.Status == "Executing" && Wire.Encode(saved.Installation) == Wire.Encode(request), "frozen exact installation request is durable before execution can begin");
            Reject<InstallationOutcomeUnknownException>(() => operation.FreezeInstallation(request), "same operation instance cannot execute a second time");
            Check(operation.Finish(0).ExitCode == 1627 && saved.Status == "Unknown", "caller success code cannot override Unknown original authority");
            originalStatus = "Completed";
            Check(operation.Finish(1627).ExitCode == 0 && saved.Status == "Completed", "lost acknowledgement derives completion from original authority rather than rerunning");
            Reject<IOException>(() => { using var competitor = new ResourceLease(root, ["quiet-operation-test"]); }, "real command lease remains held through final acknowledgement");
        }
        using (new ResourceLease(root, ["quiet-operation-test"])) Check(true, "full completion releases actual command lease");
        var frozen = saved;
        Reject<InvalidDataException>(() => WindowsDeploymentSetup.ValidateQuietRecord(frozen with { ExpectedRegistrationHash = new string('f', 64) }, intent), "index cannot substitute a different precondition for original frozen request");
        using (var replay = new QuietDeploymentOperation(frozen, true, new ResourceLease(root, ["quiet-replay"]), Save,
            record => WindowsDeploymentSetup.ProjectQuietOutcome(record, originalStatus)))
        {
            Reject<InstallationOutcomeUnknownException>(() => replay.FreezeInstallation(request), "completed replay has no execution path");
            Check(replay.Inspect().ExitCode == 0, "same exact completed original can return idempotent success");
            originalStatus = "Unknown";
            Check(replay.Finish(0).ExitCode == 1627, "later deployment mismatch invalidates a cached success acknowledgement");
        }
        using (var interrupted = new QuietDeploymentOperation(initial, true, new ResourceLease(root, ["reserved-replay"]), Save,
            record => WindowsDeploymentSetup.ProjectQuietOutcome(record, "Unknown")))
            Check(interrupted.Finish(1603).ExitCode == 1627 && saved.Status == "Unknown", "reopened reserved intent is not silently closed as a new local failure");
        using (var preflight = new QuietDeploymentOperation(initial, false, new ResourceLease(root, ["preflight-failure"]), Save,
            _ => throw new Exception("no original transaction should be inspected")))
            Check(preflight.Finish(87).ExitCode == 87 && saved.Status == "Failed", "known local validation failure before freeze preserves exact failure code");
        using (var failedSave = new QuietDeploymentOperation(initial, false, new ResourceLease(root, ["failed-save"]),
            _ => throw new IOException("synthetic journal flush failure"), record => WindowsDeploymentSetup.ProjectQuietOutcome(record, "Unknown")))
            Reject<IOException>(() => failedSave.FreezeInstallation(request), "failed durable freeze cannot enter program execution");
        foreach (var state in new[] { "Prepared", "NotCommitted", "CommittedNeedsSystemRepair", "Unknown", "RetiredCleanupPending", "InventedSuccess", "RemovedDataRetained", "RestartRequired" })
            Check(WindowsDeploymentSetup.ProjectQuietOutcome(frozen, state).ExitCode == 1627, "installation pending or unrecognized original remains Unknown: " + state);
        Check(WindowsDeploymentSetup.ProjectQuietOutcome(frozen, "Failed").ExitCode == 1603, "original recorded failure remains failure");
        Check(WindowsDeploymentSetup.ProjectQuietOutcome(frozen, "NotStarted").ExitCode == 1603, "exclusive no-original proof reports no program commit, never success");
        var uninstall = intent with { Action = "Uninstall", RequestedDataRoot = null, MediaSourceCommit = null, MediaInventorySha256 = null };
        var installed = new DeploymentLocation("sportmaster", new string('a', 40), Path.Combine(root, "release"), Path.Combine(root, "config"), data, "synthetic");
        using (var removal = new QuietDeploymentOperation(new(1, uninstall, "Reserved"), false, new ResourceLease(root, ["removal"]), Save,
            record => WindowsDeploymentSetup.ProjectQuietOutcome(record, "RemovedDataRetained")))
        {
            removal.FreezeRemoval(installed, new string('d', 64));
            Check(saved.RemovalFile == WindowsDeploymentSetup.RemovalOperationFile("sportmaster", id), "new quiet removal shares the original lifecycle operation identity");
            Check(removal.Finish(1603).ExitCode == 0, "confirmed original removal may resolve a lost acknowledgement");
        }
        var removalRecord = saved;
        using (var assets = new QuietDeploymentOperation(new(1, uninstall with { OperationId = Wire.Id() }, "Reserved"), false, new ResourceLease(root, ["assets"]), Save,
            record => WindowsDeploymentSetup.ProjectQuietOutcome(record, "RemovedDataRetained")))
        {
            assets.FreezeRetiredAssets(installed with { LifecycleState = "Purged" }, new string('d', 64));
            WindowsDeploymentSetup.ValidateQuietRecord(saved, saved.Intent);
            Check(saved.RetiredAssetsScope == WindowsDeploymentSetup.UninstallScope(installed with { LifecycleState = "Purged" }), "cleanup of a previously purged installation freezes its own original asset scope");
            Check(assets.Finish(1603).ExitCode == 0, "confirmed asset cleanup reconciles a lost acknowledgement without a second removal");
            Check(WindowsDeploymentSetup.ProjectQuietOutcome(saved, "RestartRequired").ExitCode == 3010 &&
                WindowsDeploymentSetup.ProjectQuietOutcome(saved, "RemovedWithRetainedFiles").ExitCode == 1603, "asset queue and actual leftovers remain distinct silent outcomes");
        }
        Check(WindowsDeploymentSetup.ProjectQuietOutcome(removalRecord, "NotCommitted").ExitCode == 1603, "explicitly closed original removal cannot be reported as removed");
        Check(WindowsDeploymentSetup.ProjectQuietOutcome(removalRecord, "RemovedWithRetainedFiles").ExitCode == 1603, "known residual program files are not a false zero exit");
        Check(WindowsDeploymentSetup.ProjectQuietOutcome(removalRecord, "RestartRequired").ExitCode == 3010, "historical restart obligation remains a distinct original result");
        WindowsDeploymentSetup.RequireQuietLogOutsideManagedRoots(log, [data, installed.ReleaseRoot]); Check(true, "external output log is allowed without touching managed data");
        Reject<ArgumentException>(() => WindowsDeploymentSetup.RequireQuietLogOutsideManagedRoots(Path.Combine(data, "log.json"), [data]), "output log cannot poison original data scope");
        File.WriteAllText(log, "synthetic original log must remain unchanged"); var logHash = Wire.FileHash(log);
        var logged = frozen with { LogSha256 = logHash };
        Check(WindowsDeploymentSetup.ProjectQuietOutcome(logged, "Completed").LogStatus == "Unchanged" && Wire.FileHash(log) == logHash, "replay observes original log without overwriting it");
        File.AppendAllText(log, "; synthetic external change");
        Check(WindowsDeploymentSetup.ProjectQuietOutcome(logged, "Completed").LogStatus == "Changed", "changed log cannot be represented as original evidence");
        File.Delete(log); Check(WindowsDeploymentSetup.ProjectQuietOutcome(logged, "Completed").LogStatus == "Missing", "missing log is explicit and is not recreated by replay");
        var human = Path.Combine(root, "operator-readable.log");
        QuietDeploymentOutputReceipt outputReceipt;
        using (var output = new QuietDeploymentOutput(human, "sportmaster", id, "Install"))
        {
            Check(File.Exists(human) && !File.Exists(output.ReceiptFile), "human log starts without publishing an empty success receipt");
            Reject<InvalidDataException>(() => output.Finish(0, "Unknown", null, "synthetic"), "output cannot label zero exit as Unknown");
            outputReceipt = output.Finish(1627, "Unknown", null, "合成原结果尚未核对。password=quiet-canary-917");
            Check(outputReceipt.OperationId == id && outputReceipt.ExitCode == 1627 && !outputReceipt.BusinessStarted && !outputReceipt.DevicesStarted,
                "JSON output binds original identity and honest no-runtime facts");
            Check(outputReceipt.LogSha256 == Wire.FileHash(human) && output.TemporaryReceiptFile is null, "closed human log and atomic JSON output hashes read back exactly");
            Reject<InvalidOperationException>(() => output.Finish(0, "Completed", null, "do not overwrite"), "completed output cannot be rewritten as success");
        }
        var humanText = File.ReadAllText(human); var machineText = File.ReadAllText(human + ".receipt.json");
        Check(humanText.StartsWith("部署开始：") && humanText.Contains("操作标识：" + id) && humanText.Contains("结果未确认"), "operator log is readable text with original operation and safe next action");
        Check(!humanText.Contains("quiet-canary-917") && !machineText.Contains("quiet-canary-917"), "both independently written outputs exclude the synthetic secret canary");
        Check(System.Text.Json.JsonSerializer.Deserialize<QuietDeploymentOutputReceipt>(machineText) == outputReceipt, "published machine JSON is a separate verified receipt");
        var humanHash = Wire.FileHash(human); var machineHash = Wire.FileHash(human + ".receipt.json");
        Reject<ArgumentException>(() => { using var duplicate = new QuietDeploymentOutput(human, "sportmaster", id, "Install"); }, "existing human log cannot be reused or overwritten");
        Check(Wire.FileHash(human) == humanHash && Wire.FileHash(human + ".receipt.json") == machineHash, "refusal retains both original output byte streams");
        var foreign = Path.Combine(root, "foreign-receipt.log"); File.WriteAllText(foreign + ".receipt.json", "synthetic foreign receipt");
        Reject<ArgumentException>(() => { using var blockedOutput = new QuietDeploymentOutput(foreign, "sportmaster", id, "Repair"); }, "existing sidecar is checked before human log creation");
        Check(!File.Exists(foreign), "foreign receipt refusal creates no new operator log");
        var interruptedLog = Path.Combine(root, "interrupted-output.log");
        using (var interruptedOutput = new QuietDeploymentOutput(interruptedLog, "sportmaster", id, "Repair")) { }
        Check(File.ReadAllText(interruptedLog).Contains("当前尚未完成") && !File.Exists(interruptedLog + ".receipt.json"), "interrupted logger never fabricates a terminal JSON receipt");
        var collisionLog = Path.Combine(root, "late-collision.log");
        using (var late = new QuietDeploymentOutput(collisionLog, "sportmaster", id, "Uninstall"))
        {
            File.WriteAllText(late.ReceiptFile, "synthetic concurrent foreign file");
            Reject<IOException>(() => late.Finish(1603, "Completed", "synthetic-original-removal", "原事务完成，残件保留。"), "late sidecar collision cannot overwrite another output");
            Check(File.ReadAllText(late.ReceiptFile) == "synthetic concurrent foreign file" && late.TemporaryReceiptFile is { } partial && File.Exists(partial), "failed publication keeps foreign bytes and identifies exact own temporary evidence");
            Reject<InvalidOperationException>(() => late.Finish(0, "Completed", null, "retry"), "failed publication cannot silently rerun or rewrite original result");
        }
        var oversizedLog = Path.Combine(root, "oversized-external.log");
        using (var oversized = new FileStream(oversizedLog, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            oversized.SetLength(4 * 1024 * 1024 + 1);
            Check(WindowsDeploymentSetup.ProjectQuietOutcome(frozen with { Intent = intent with { LogFile = oversizedLog }, LogSha256 = new string('f', 64) }, "Completed").LogStatus == "TooLarge",
                "oversized external log is classified before opening or hashing locked bytes");
        }
        Console.WriteLine("IE01-QUIET checks=" + checks + "; real file locks/JSON and injected authoritative outcomes; Windows ACL/actual quiet lifecycle NOT_RUN");
    }
}
