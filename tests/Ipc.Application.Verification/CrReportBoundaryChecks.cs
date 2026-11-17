using System.Reflection;
using System.Security.AccessControl;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

internal static class CrReportBoundaryChecks
{
    static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS CR-BOUNDARY " + message); }
    static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { Check(true, message); return; }
        throw new InvalidOperationException(message);
    }
    public static async Task Run(string workspace, string root)
    {
        TextAndSchemas(workspace);
        await ArgumentSelection(workspace);
        ConfigurationOwnership(root);
        ExportIdentity(Path.Combine(root, "exports"));
        ExportExpiry(Path.Combine(root, "export-expiry"));
        UserIsolation(Path.Combine(root, "user-isolation"));
        DeploymentSummaries(Path.Combine(root, "receipts"));
        using (var execution = new MaintenanceExecution())
        {
            using var callback = execution.Token.Register(execution.Dispose);
            await Task.Run(() => execution.TryCancel()).WaitAsync(TimeSpan.FromSeconds(3));
            Check(!execution.CanCancel, "maintenance cancellation callback can dispose its execution without waiting on itself");
        }
        var first = new LocalManagementMode(Path.Combine(root, "authorization")); var second = new LocalManagementMode(Path.Combine(root, "authorization"));
        first.ConfigurePassword("SYNTHETIC-PASSWORD"); Check(first.Enter("SYNTHETIC-PASSWORD") && second.Enter("SYNTHETIC-PASSWORD"), "independent management clients establish distinct grants");
        var secondId = second.Status().SessionId; first.Exit();
        Check(second.Status().Authorized && second.Evidence?.SessionId == secondId, "exit from the retired client cannot revoke the replacement grant or mix its evidence"); second.Exit();
        await Custody(workspace, Path.Combine(root, "custody"));
        await ArchiveCommit(workspace, Path.Combine(root, "archive"));
        await EditorIdentity(workspace, Path.Combine(root, "editor"));
        await RetentionFixtureOwnership(workspace, Path.Combine(root, "retention-fixture"));
    }
    static async Task RetentionFixtureOwnership(string workspace, string root)
    {
        var seed = Path.Combine(root, "seed"); var current = Path.Combine(root, "simulated-8");
        await RetentionQualification.Run(workspace, ["seed", seed]);
        await RetentionQualification.Run(workspace, ["create", current, seed, "8"]);
        var manifest = Path.Combine(current, "FIXTURE.json"); var bytes = File.ReadAllBytes(manifest);
        var original = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        var database = original["Database"]!.GetValue<string>(); var hash = Wire.FileHash(database);
        var outside = Path.Combine(root, "foreign-original.db"); File.Copy(database, outside);
        foreach (var mutation in new[] { "Database", "DatabaseSha256", "ProductSha256", "FixtureSha256" })
        {
            var changed = original.DeepClone(); changed[mutation] = mutation == "Database" ? outside : new string('0', 64);
            File.WriteAllText(manifest, changed.ToJsonString());
            try
            {
                try { await RetentionQualification.Run(workspace, ["exercise-simulated-authority", current]); }
                catch (InvalidDataException)
                {
                    Check(Wire.FileHash(database) == hash && Wire.FileHash(outside) == hash && !File.Exists(Path.Combine(current, "complete-before-expiry.db")),
                        "retention refuses substituted " + mutation + " before backup or destructive mutation of either original");
                    continue;
                }
                throw new InvalidOperationException("Retention fixture substitution escaped: " + mutation);
            }
            finally { File.WriteAllBytes(manifest, bytes); }
        }
        await RetentionQualification.Run(workspace, ["exercise-simulated-authority", current]);
        Check(File.Exists(Path.Combine(current, "VERIFIED.json")) && Wire.FileHash(outside) == hash,
            "genuine same-build fixture completes exact guard-custody verification while the unrelated original remains unchanged");
    }
    static void ExportExpiry(string root)
    {
        Directory.CreateDirectory(root);
        var mode = new LocalManagementMode(Path.Combine(root, "authority"));
        mode.ConfigurePassword("SYNTHETIC-EXPORT-AUTHORITY");
        Check(mode.Enter("SYNTHETIC-EXPORT-AUTHORITY"), "export expiry fixture owns an actual management grant");
        var destination = Path.Combine(root, "expired.txt"); var temporary = destination + ".partial";
        File.WriteAllText(temporary, "synthetic authorized bytes");
        var now = DateTimeOffset.UtcNow; var checks = 0;
        mode.Clock = () => Interlocked.Increment(ref checks) == 1 ? now : now.AddHours(1);
        using var publication = new ExportPublication(destination);
        var denied = false;
        try { mode.PublishExport(publication, temporary, new FileInfo(temporary).Length, Wire.FileHash(temporary)); }
        catch (UnauthorizedAccessException) { denied = true; }
        finally { mode.Clock = () => DateTimeOffset.UtcNow; mode.Exit(); }
        Check(denied && checks >= 2 && !File.Exists(destination) && File.Exists(temporary), "authority expiry during byte verification is checked again at rename and preserves the unpublished original");
    }
    static async Task ArgumentSelection(string workspace)
    {
        foreach (var extra in new[] { "--misspelled-suite", "--cr-report-cameras" })
        {
            var refused = false;
            try { VerificationArguments.Require([workspace, "--log-readable", extra]); }
            catch (ArgumentException) { refused = true; }
            Check(refused, "an unsupported option or second suite is refused before dispatch: " + extra);
        }
        VerificationArguments.Require([workspace, "--decode-tilt-benchmark", "--expect", "--literal-barcode", "--rounds", "1"]);
        Check(true, "a documented option consumes its literal value without mistaking it for another suite");
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(CrReportBoundaryChecks).Assembly.Location);
        foreach (var value in new[] { workspace, "--log-readable", "--misspelled-suite" }) start.ArgumentList.Add(value);
        using var child = System.Diagnostics.Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
        try
        {
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(child.ExitCode == 2 && !(await output).Contains("PASS ", StringComparison.Ordinal) && (await error).Contains("Unsupported option", StringComparison.Ordinal),
                "the actual verifier refuses an invalid complete argument set before running a recognized suite");
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            await Task.WhenAll(output, error);
        }
    }
    static void UserIsolation(string root)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var caller = System.Security.Principal.WindowsIdentity.GetCurrent();
        var sid = caller.User!;
        var directory = new DirectoryInfo(root);
        var acl = new System.Security.AccessControl.DirectorySecurity();
        acl.SetOwner(sid); acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(sid, System.Security.AccessControl.FileSystemRights.FullControl,
            System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
            System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
        directory.Create(acl);
        WindowsStoragePaths.RequireUserDirectory(root, sid.Value);
        Check(true, "ordinary per-user directory permits only its actual Windows owner and maintenance authorities");
        acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(new System.Security.Principal.SecurityIdentifier("S-1-1-0"),
            System.Security.AccessControl.FileSystemRights.WriteData, System.Security.AccessControl.AccessControlType.Allow));
        directory.SetAccessControl(acl);
        var refused = false;
        try { WindowsStoragePaths.RequireUserDirectory(root, sid.Value); }
        catch (UnauthorizedAccessException) { refused = true; }
        Check(refused, "a correctly named user directory with another user's write access is refused without changing its contents or ACL");
    }
    static void ExportIdentity(string root)
    {
        Directory.CreateDirectory(root); var destination = Path.Combine(root, "result.txt"); var temporary = destination + ".partial";
        File.WriteAllText(temporary, "synthetic original"); var hash = Wire.FileHash(temporary); var length = new FileInfo(temporary).Length;
        using (var publication = new ExportPublication(destination))
        {
            if (OperatingSystem.IsWindows())
            {
                var refused = false;
                try { Directory.Move(root, root + "-replaced"); }
                catch (IOException) { refused = true; }
                catch (UnauthorizedAccessException) { refused = true; }
                Check(refused && Directory.Exists(root), "export pins every destination ancestor against replacement until publication");
            }
            File.WriteAllText(temporary, "synthetic substitute");
            Reject(() => publication.Publish(temporary, length, hash), "publication refuses a substituted temporary file without publishing it");
            Check(!File.Exists(destination), "rejected export cannot create a misleading final artifact");
            File.WriteAllText(temporary, "synthetic original"); publication.Publish(temporary, length, hash);
            Check(Wire.FileHash(destination) == hash && !File.Exists(temporary), "handle publication preserves the checked bytes and consumes only its original temporary file");
        }
        File.WriteAllText(temporary, "another original");
        using var collision = new ExportPublication(destination);
        var blocked = false;
        try { collision.Publish(temporary, new FileInfo(temporary).Length, Wire.FileHash(temporary)); }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception) { blocked = true; }
        Check(blocked && Wire.FileHash(destination) == hash, "handle rename never replaces a competing completed export");
    }
    static void DeploymentSummaries(string root)
    {
        Directory.CreateDirectory(root);
        var old = DateTimeOffset.UtcNow.AddDays(-1);
        for (var i = 0; i < 1030; i++) File.WriteAllText(Path.Combine(root, "sportmaster.quiet-" + Wire.Id() + ".json"), Wire.Encode(new { Status = "Completed", At = old, Intent = new { Action = 1 } }));
        var newest = Wire.Id(); File.WriteAllText(Path.Combine(root, "sportmaster.quiet-" + newest + ".json"), Wire.Encode(new { Status = "Completed", At = DateTimeOffset.UtcNow, Intent = new { Action = "Install" } }));
        var summary = DeploymentReceipts.Summarize(root, "sportmaster").Single(x => x.Kind == "quiet");
        Check(summary.Count == 1031 && summary.Latest?.OperationId == newest && summary.Unreadable == 0,
            "deployment summaries include every eligible receipt and tolerate malformed optional actions");
    }
    sealed record MutableConfiguration(Dictionary<string, string> Values);
    static void ConfigurationOwnership(string root)
    {
        var seed = new MutableConfiguration(new() { ["original"] = "seed" });
        var revisions = new ConfigurationRevisions<MutableConfiguration>(Path.Combine(root, "configuration.json"), seed, _ => []);
        seed.Values["original"] = "changed";
        Check(revisions.Current.Effective.Values["original"] == "seed", "configuration seed is a defensive snapshot");
        var candidate = new MutableConfiguration(new() { ["candidate"] = "saved" });
        var staged = revisions.Stage(revisions.Current.Revision, candidate);
        candidate.Values["candidate"] = "caller"; staged.Pending!.Values["candidate"] = "returned";
        var exposed = revisions.Current; exposed.Pending!.Values["candidate"] = "snapshot";
        Check(revisions.Current.Pending!.Values["candidate"] == "saved", "caller, returned receipt and public snapshots cannot mutate the configuration ledger");
        _ = revisions.BeginActivation(_ => true, value => value.Values["candidate"] = "preflight");
        var attempt = revisions.Current.Activation!.Id;
        try { revisions.Stage(revisions.Current.Revision, candidate); throw new Exception("staging during activation was accepted"); }
        catch (InvalidOperationException) { }
        try { revisions.FailActivation(Wire.Id(), seed, candidate, new IOException("wrong attempt")); throw new Exception("wrong activation failure was accepted"); }
        catch (InvalidOperationException) { }
        Check(revisions.Current.Activation!.Id == attempt && revisions.Current.Pending!.Values["candidate"] == "saved", "activation callback, staging and foreign failure cannot replace the active candidate");
        revisions.ConfirmActivation(attempt);
        Check(revisions.Current.Effective.Values["candidate"] == "saved", "activation confirms the exact frozen candidate");
        revisions.Stage(revisions.Current.Revision, seed); var revision = revisions.Current.Revision;
        if (OperatingSystem.IsWindows())
        {
            using var blocked = new FileStream(Path.Combine(root, "configuration.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
            var failed = false;
            try { revisions.BeginActivation(_ => true, _ => { }); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failed = true; }
            Check(failed && revisions.Current.Revision == revision && revisions.Current.Status == "PendingRestart",
                "failed activation publication does not rewrite an uncommitted transition as activation failure");
        }
    }
    static void TextAndSchemas(string workspace)
    {
        foreach (var key in new[] { "private_key", "private-key", "privatekey", "token" })
            Check(!DiagnosticText.Protect(key + "=SYNTHETIC_SECRET").Contains("SYNTHETIC_SECRET", StringComparison.Ordinal), "plain diagnostic masks " + key);
        Check(!DiagnosticText.Protect("{\"token\":\"FIRST_SECRET\",\"token\":\"SECOND_SECRET\"}").Contains("SECRET", StringComparison.Ordinal), "duplicate JSON keys neither crash redaction nor expose their values");
        var uri = "https://synthetic-user:" + new string('s', 17000) + "@example.invalid/failure";
        Check(!WindowsInstallationError.Capture(new IOException(uri)).Message.Contains("synthetic-user", StringComparison.Ordinal), "installation diagnostic redacts complete credential syntax before applying its length limit");
        Check(ChineseInterfaceText.Notice("remote-id=UntrustedObservation-42") == "remote-id=UntrustedObservation-42", "translation preserves opaque remote identifiers");
        Check(!ChineseInterfaceText.Notice("Transport/protocol facts do not establish business acceptance").Contains("RCS", StringComparison.Ordinal), "generic uncertain transport evidence does not claim a specific subsystem");
        Check(!QueryTime.DisplayNotice("现存日志记录时间：。早于首条/晚于末条的请求范围无法证明完整；无记录不等于无异常。").Contains("已清理", StringComparison.Ordinal), "missing logs remain unconfirmed instead of being declared deleted");
        foreach (var json in new[] { "null", "[]", "123", "{\"Source\":123,\"Event\":\"test\"}", "{\"Source\":\"test\",\"Detail\":\"[]\"}" })
            Check(SoftwareLogText.Summary(json).Length > 0, "malformed log shape remains displayable: " + json);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        var flow = new SportmasterFlow();
        Check(flow.Validate(config with { Topology = config.Topology! with { Readers = null! }, DeferredConnections = [config.Reader.DeviceId] }).Length > 0,
            "missing topology collection returns configuration errors before connection validation");
        Check(flow.Validate(config with { BackupSchedule = new(null!, 15, 5) }).Length > 0, "missing backup directory is a configuration error, not a null dereference");
        var list = new CapabilityParameter(new("test", "test", "test", "string-list", MaximumLength: 32, MaximumItems: 3), "");
        Check(!ExtensionSchema.IsId("abc\n") && !ExtensionSchema.IsVersion("1.2.3\n"), "protocol identity/version validators reject trailing control characters");
        Check(ExtensionSchema.Validate([new("fixed", "test", "fixed", "string", Default: "constant", ReadOnly: true)], new Dictionary<string, string> { ["fixed"] = "" }).Length > 0,
            "an empty value cannot bypass read-only schema enforcement");
        Check(ExtensionSchema.Validate([list.Field], new Dictionary<string, string> { ["test"] = "null" }).Length > 0, "null string-list produces a validation error without escaping the trust boundary");
        var escaped = "[\"" + string.Concat(Enumerable.Repeat("\\u4e00", 32)) + "\",\"" + string.Concat(Enumerable.Repeat("\\u4e00", 32)) + "\",\"" + string.Concat(Enumerable.Repeat("\\u4e00", 32)) + "\"]";
        Check(list.EditorMaximumLength >= escaped.Length && Wire.Decode<string[]>(escaped).All(s => s.Length == 32), "string-list editor admits valid JSON with worst-case escaping");
        Check(ExtensionSchema.Validate([list.Field], new Dictionary<string, string> { ["test"] = escaped }).Length == 0, "schema validation and editor agree on valid escaped-list limits");
    }
    static async Task Custody(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, root, deferred: true, mode: RunMode.Simulation);
        await h.StartAsync();
        var receipt = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "CR-CUSTODY"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(receipt.OperationId!))?.Wcs?.State == StageState.Accepted);
        var operation = (await h.App.OperationAsync(receipt.OperationId!))!;
        await h.App.DisposeAsync();
        var directory = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, h.Config.ApplicationId);
        using var repository = new ParcelRepository(directory, "sportmaster");
        var original = Wire.Encode(repository.ReadOperation(operation.Id));
        foreach (var changed in new[] { operation with { AdmissionId = Wire.Id() }, operation with { ObservationId = Wire.Id() }, operation with { PositionId = "right" }, operation with { Wcs = null } })
        {
            Reject(() => repository.Commit(changed, "SyntheticInvalidIdentity", "must reject"), "existing operation identity and external intent cannot be erased or replaced");
            Check(Wire.Encode(repository.ReadOperation(operation.Id)) == original, "rejected owner mutation preserves the exact original body");
        }
        var reply = operation.Wcs!.Replies[^1] with { Id = Wire.Id() };
        var occurrence = repository.BeginReceipt(operation, operation.Wcs, "Query");
        repository.StoreRaw(occurrence, reply);
        var file = Path.Combine(directory, "receipts", occurrence + ".json"); var hash = Wire.FileHash(file);
        var changedReply = reply with { Evidence = "different synthetic original" };
        Reject(() => repository.StoreRaw(occurrence, changedReply), "repeated raw write rejects before overwriting durable original bytes");
        Reject(() => repository.RecoverRaw(occurrence, changedReply), "raw recovery cannot replace a durable custody hash");
        repository.RecoverRaw(occurrence, reply);
        Check(Wire.FileHash(file) == hash && repository.PendingReceipts().Single(p => p.Id == occurrence).Reply == reply,
            "same-content original recovery remains valid and preserves independent raw evidence");
        var revision = repository.Revision;
        repository.Boundary = point => { if (point == "AfterBusinessCommit:SyntheticPostCommit") throw new InvalidOperationException("post-commit reporting failure"); };
        try { repository.Commit(null, "SyntheticPostCommit", "committed", paused: true); throw new Exception("Expected reporting failure"); }
        catch (InvalidOperationException error) when (error.Message == "post-commit reporting failure") { }
        finally { repository.Boundary = null; }
        Check(repository.Revision == revision + 1 && repository.Paused, "repository adopts durable revision and pause state before post-commit callbacks");
    }
    static async Task ArchiveCommit(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, root, deferred: true, mode: RunMode.Simulation);
        await h.StartAsync();
        var receipt = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "CR-ARCHIVE"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(receipt.OperationId!))?.Wcs?.State == StageState.Accepted);
        await SmRoundFixture.ConfirmRemovedAsync(h.App, "left");
        await ManagementModeFixture.Authorize(h.App);
        ParcelSnapshot snapshot;
        var paused = false;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            snapshot = await h.App.SnapshotAsync(); var pause = await h.App.CommandAsync(new(Wire.Id(), snapshot.Revision, "Pause"));
            if (pause.Status == "Conflict") continue;
            Check(pause.Status == "Persisted", "archive fixture actually enters its required paused state"); paused = true; break;
        }
        Check(paused, "archive fixture pause converges before injecting its post-commit failure");
        var repository = (ParcelRepository)typeof(ParcelRuntime).GetField("repository", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(h.App)!;
        repository.Boundary = point => { if (point == "AfterArchiveCommit") throw new InvalidOperationException("archive reporting failure"); };
        snapshot = await h.App.SnapshotAsync();
        try
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                snapshot = await h.App.SnapshotAsync();
                var result = await h.App.CommandAsync(new(Wire.Id(), snapshot.Revision, "ArchiveHistory", Value: "Synthetic archive failure"));
                if (result.Status != "Conflict" && result is not { Status: "Rejected", Reason: "Unsupported command or an application guard is closed" })
                    throw new Exception("Expected archive reporting failure: " + result.Status + "/" + result.Reason);
                await Task.Delay(20);
            }
            throw new TimeoutException("Archive fixture never reached its original maintenance boundary");
        }
        catch (InvalidOperationException error) when (error.Message == "archive reporting failure") { }
        finally { repository.Boundary = null; }
        Check(!(await h.App.SnapshotAsync()).Operations.Any(o => o.Id == receipt.OperationId) && await h.App.OperationAsync(receipt.OperationId!) is not null && h.Peer.Requests.Count == 1,
            "post-commit archive failure still reconciles the resident set and retains the original history without resending");
    }
    static async Task EditorIdentity(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, root, deferred: false, mode: RunMode.Simulation);
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        var catalog = await h.App.CapabilitiesAsync(); var binding = h.Config.Wcs!;
        var draft = new CapabilityDraft("integration", catalog.Providers, binding with { Contract = "different-contract" }, true);
        Check(draft.Provider is null, "exact provider lookup includes its contract identity");
        var original = new CapabilityDraft("integration", catalog.Providers, binding with { Parameters = new(binding.Parameters) { ["unknown-original"] = "must not resurrect" } }, true);
        var provider = original.Provider!;
        original.Provider = null; original.Provider = provider;
        foreach (var parameter in original.Parameters)
            if (binding.Parameters.TryGetValue(parameter.Field.Key, out var value)) parameter.Value = value;
        var built = Wire.Encode(original.Build());
        Check(Wire.Encode(CapabilityDraft.RestoreRaw(original.CaptureRaw(), catalog.Providers).Build()) == built && !built.Contains("must not resurrect", StringComparison.Ordinal),
            "provider reset survives encrypted draft capture and restore without resurrecting old parameters");
        var startup = new StartupConfigurationDraft(h.Config, catalog);
        var editor = startup.CreateParameterEditor((policy, codes) => Task.FromResult(BarcodeFiltering.Select(policy, codes)));
        var camera = startup.Readers[0]; var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.CameraSerialReader = (_, _) => pending.Task;
        var originalParameters = camera.Parameters.ToDictionary(p => p.Field.Key, p => p.Value);
        var reading = editor.ReadCameraIdentityAsync(camera); var cameraProvider = camera.Provider;
        camera.Provider = null; camera.Provider = cameraProvider;
        foreach (var parameter in camera.Parameters) parameter.Value = originalParameters[parameter.Field.Key];
        pending.SetResult("SYNTH-DETACHED-SERIAL");
        try { await reading; throw new Exception("Detached serial accepted"); }
        catch (InvalidOperationException error) when (error.Message.Contains("读取期间", StringComparison.Ordinal)) { }
        Check(camera.Parameters.Single(p => p.Field.Key == "device.serial").Value != "SYNTH-DETACHED-SERIAL", "late camera identity cannot write a detached parameter collection");
    }
}
