using System.Text.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using Ipc.Assembly;
using Microsoft.Data.Sqlite;

static class OperatorAdministration
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL OPS: " + message); Console.WriteLine("PASS OPS: " + message); }
    static async Task Reject<T>(Func<Task> action, string message) where T : Exception
    { try { await action(); throw new Exception("Expected rejection: " + message); } catch (T) { Check(true, message); } }
    public static async Task Run(string workspace, string root)
    {
        var relocationSource = Path.Combine(root, "relocation-source"); var relocationTarget = Path.Combine(root, "relocation-target");
        using (var unused = new ParcelRepository(relocationSource, "sportmaster")) { }
        Directory.CreateDirectory(relocationTarget);
        var sourceDatabase = Path.Combine(relocationSource, "sportmaster.db"); var targetDatabase = Path.Combine(relocationTarget, "sportmaster.db");
        void Execute(string file, string sql) { using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString()); db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        Execute(sourceDatabase, "INSERT INTO parcel_audit(at,event,owner,detail) VALUES('synthetic','Original','owner','preserve')");
        File.Copy(sourceDatabase, targetDatabase);
        Execute(targetDatabase, "INSERT INTO parcel_audit(at,event,owner,detail) VALUES('synthetic','Maintenance','owner','additional')");
        ParcelRepository.RequireUnusedRelocationStatePreserved(sourceDatabase, targetDatabase);
        Check(true, "unused simulation relocation preserves every original row while allowing additional maintenance audit");
        Execute(targetDatabase, "UPDATE parcel_audit SET detail='changed' WHERE event='Original'");
        await Reject<InvalidDataException>(() => Task.Run(() => ParcelRepository.RequireUnusedRelocationStatePreserved(sourceDatabase, targetDatabase)), "relocation refuses changed original audit instead of silently accepting a fresh ledger");
        Execute(targetDatabase, "UPDATE parcel_audit SET detail='preserve' WHERE event='Original'; INSERT INTO parcel_operations VALUES('synthetic','admission','observation','{}')");
        await Reject<InvalidOperationException>(() => Task.Run(() => ParcelRepository.RequireUnusedRelocationStatePreserved(sourceDatabase, targetDatabase)), "even one original operation prevents the unused-simulation relocation exception");
        var data = Path.Combine(root, "admin-log-data"); var admin = LocalManagementMode.ForRoot(data);
        var password = "Synthetic-" + Guid.NewGuid().ToString("N");
        var simpleRoot = Path.Combine(root, "five-character-mode"); var simple = new LocalManagementMode(simpleRoot);
        foreach (var invalid in new[] { "1234", new string('1', 257), "1234\n" })
            await Reject<InvalidDataException>(() => Task.Run(() => simple.ConfigurePassword(invalid)), "new setup rejects out-of-range or control-character passwords without creating a credential");
        Check(!simple.Status().Configured, "rejected new passwords leave the original unconfigured state");
        simple.ConfigurePassword("11111"); Check(simple.Enter("11111"), "five repeated numeric characters can configure and enter the real management authority");
        var simpleGrant = simple.Require();
        await Reject<InvalidDataException>(() => Task.Run(() => simple.ChangePassword("1234")), "changing to fewer than five characters is rejected by the same service policy");
        Check(simple.Status().Authorized && !simpleGrant.IsCancellationRequested, "rejected password replacement preserves the current grant and credential");
        simple.ChangePassword("22222");
        Check(simpleGrant.IsCancellationRequested && !simple.Status().Authorized && !simple.Enter("11111") && simple.Enter("22222"), "five-digit replacement revokes old authorization and authenticates only the new password");
        Check(!File.ReadAllText(Path.Combine(simpleRoot, "security/management-mode.json")).Contains("\"11111\"") && !File.ReadAllText(Path.Combine(simpleRoot, "security/management-mode.json")).Contains("\"22222\""), "numeric passwords are not persisted in plaintext");
        Check(!admin.Status().Configured && !admin.Status().Authorized, "no default management password opens the mode");
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => admin.Require()), "direct service calls reject before management-password setup");
        admin.ConfigurePassword(password);
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => admin.ConfigurePassword(password)), "initial management-password setup cannot overwrite an existing password");
        Check(!File.ReadAllText(Path.Combine(data, "security/management-mode.json")).Contains(password), "credential file contains salted hashes rather than passwords");
        var missingRoot = Path.Combine(root, "missing-account-store"); var missing = new LocalManagementMode(missingRoot); missing.ConfigurePassword(password);
        var missingFile = Path.Combine(missingRoot, "security/management-mode.json"); File.Move(missingFile, missingFile + ".retained");
        await Reject<InvalidDataException>(() => Task.Run(() => new LocalManagementMode(missingRoot).Status()), "configured-mode marker prevents a missing credential file from reopening setup");
        await Reject<InvalidDataException>(() => Task.Run(() => new LocalManagementMode(missingRoot).ConfigurePassword(password)), "missing configured mode credentials cannot be silently replaced");
        for (var i = 0; i < 5; i++) Check(!admin.Enter("wrong"), "invalid credential grants no session " + i);
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => new LocalManagementMode(data).Enter(password)), "persisted login lockout survives a new authenticator");
        // A separate store verifies the remaining session cases without bypassing lockout.
        data = Path.Combine(root, "runtime-admin-data"); admin = LocalManagementMode.ForRoot(data); admin.ConfigurePassword(password); Check(admin.Enter(password), "real password hash authentication succeeds");
        var delegated = admin.Delegate(new string('A', 64)); var helper = new LocalManagementMode(data);
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => helper.ValidateDelegation(delegated with { RequestHash = new string('B', 64) }, new string('B', 64), true)), "a copied helper ticket cannot change its server-bound request hash");
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => helper.ValidateDelegation(delegated with { ExpiresAt = delegated.ExpiresAt.AddMinutes(5) }, new string('A', 64), true)), "a helper ticket cannot extend its server-bound expiry");
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => helper.ValidateDelegation(delegated with { Id = Wire.Id() }, new string('A', 64), true)), "a helper ticket cannot mint another one-use authorization");
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => helper.ValidateDelegation(delegated with { SessionId = Wire.Id() }, new string('A', 64), true)), "helper proof cannot borrow or forge a different management-mode session");
        helper.ValidateDelegation(delegated, new string('A', 64), true);
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => helper.ValidateDelegation(delegated, new string('A', 64), true)), "elevated helper delegation is single-use rather than Windows-only authorization");
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => helper.ValidateDelegation(delegated, new string('B', 64), false)), "helper authorization is bound to the exact original maintenance request");
        var grant = admin.Require(); admin.ChangePassword(password + "-2"); Check(grant.IsCancellationRequested && !admin.Status().Authorized, "changing the one mode password immediately revokes pending tasks and the original mode");
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => helper.ValidateDelegation(delegated, new string('A', 64), false)), "password replacement invalidates already-issued helper delegation");
        Check(!admin.Enter(password) && admin.Enter(password + "-2"), "old password stops working and the replacement re-enters management mode without a username");
        admin.ChangePassword(password); Check(admin.Enter(password), "one management password is changed only from an active mode");
        Check(typeof(IManagementMode).GetMethod("EnterManagementModeAsync")!.GetParameters().Length == 1 && !typeof(IManagementMode).GetMethods().Any(m => m.Name.Contains("Accounts")), "normal management mode API has one password and no account enumeration or maintenance");
        var legacyRoot = Path.Combine(root, "legacy-mode-conversion"); var legacyDirectory = Path.Combine(legacyRoot, "security"); Directory.CreateDirectory(legacyDirectory);
        var salt = RandomNumberGenerator.GetBytes(32); var oldPassword = "Old-" + Guid.NewGuid().ToString("N");
        var oldFile = Path.Combine(legacyDirectory, "administrators.json");
        File.WriteAllText(oldFile, JsonSerializer.Serialize(new { Accounts = new[] { new { Name = "old-maintainer", Salt = Convert.ToBase64String(salt), Hash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(oldPassword, salt, 600000, HashAlgorithmName.SHA256, 32)), Enabled = true } }, Failures = 0, LockedUntil = DateTimeOffset.MinValue }));
        var legacyHash = Wire.FileHash(oldFile); var legacyMode = new LocalManagementMode(legacyRoot);
        Check(legacyMode.Status().NeedsLegacyConversion && !legacyMode.Status().Authorized, "old configured account ledger opens no mode and explicitly requires conversion");
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => legacyMode.ConfigurePassword(password)), "first mode setup cannot overwrite old software credentials");
        Check(!legacyMode.ConvertLegacyCredentials("old-maintainer", "wrong", password) && Wire.FileHash(oldFile) == legacyHash, "failed legacy conversion preserves original credential bytes");
        Check(legacyMode.ConvertLegacyCredentials("old-maintainer", oldPassword, password) && !legacyMode.Status().Authorized && legacyMode.Enter(password), "verified old credentials convert once into a new independent station mode password");
        Check(Wire.FileHash(oldFile) == legacyHash && !File.ReadAllText(Path.Combine(legacyDirectory, "management-mode.json")).Contains("Accounts"), "conversion retains old bytes and stores a single credential rather than a hidden account");
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => legacyMode.ConvertLegacyCredentials("old-maintainer", oldPassword, password + "-3")), "legacy conversion cannot replace an already-configured mode password");
        var converted = Path.Combine(legacyDirectory, "management-mode.json"); File.Move(converted, converted + ".retained");
        await Reject<InvalidDataException>(() => Task.Run(() => legacyMode.Status()), "missing new mode credentials fail closed instead of falling back to retained old accounts");
        var expiry = new LocalManagementMode(Path.Combine(root, "expiry")); expiry.ConfigurePassword(password); expiry.Enter(password);
        var expiryGrant = expiry.Require(); expiry.Clock = () => DateTimeOffset.UtcNow.AddMinutes(16);
        Check(!expiry.Status().Authorized && expiryGrant.IsCancellationRequested, "session expiry revokes authorization and pending task tokens");
        Check(File.ReadAllText(Path.Combine(root, "expiry/security/diagnostics/runtime.jsonl")).Contains("ManagementModeExpired"), "session expiry is recorded in the actual authentication journal");
        var configRoot = Path.Combine(root, "admin-config"); Directory.CreateDirectory(configRoot);
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json"))) with { Login = null, StationId = "ops-" + Wire.Id() };
        ExtensionStore.AtomicWrite(Path.Combine(configRoot, "business.json"), seed);
        var launch = new BusinessLaunch(configRoot, data, null, null);
        var factory = new CommercialProductization.SyntheticFactory(launch);
        await using (var realCatalog = new ParcelCapabilityFactory(launch))
        {
            Check(!realCatalog.Requirements(seed.Wcs! with { Implementation = "sportmaster-rcs", Version = "1.3.1", Contract = "SPORTMASTER-RCS-SORTER-v1", Package = null }).Query, "actual package-less RCS requirements reject unsupported authoritative query");
            await Reject<InvalidOperationException>(() => Task.Run(() => realCatalog.Validate(seed with { Mode = RunMode.Production })), "unprotected local mode state cannot satisfy production authentication capability");
        }
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } };
        await app.StartAsync(default);
        await app.EnterManagementModeAsync(password);
        var initial = await app.SnapshotAsync();
        Check(initial.CurrentOperator is null, "entering management mode never creates or changes a parcel operator");
        Check(!(await app.SnapshotAsync()).Paused, "management mode entry and parameter catalog reads do not pause feeding");
        await app.CapabilitiesAsync();
        Check((await app.StageConfigurationAsync(initial.Configuration.Revision, seed with { TimeoutMs = 2000 }, "test")).Status == "Rejected", "management-authorized configuration save still requires pause and drain");
        await app.ExitManagementModeAsync();
        await Reject<UnauthorizedAccessException>(() => app.CapabilitiesAsync(), "optional operator login disabled still protects parameter reads");
        Check((await app.SnapshotAsync()).Configuration.Effective.Wcs!.Parameters.Count == 0, "operator snapshot cannot expose the editable provider configuration");
        await Reject<UnauthorizedAccessException>(() => app.ValidateConfigurationAsync(seed), "direct candidate validation rejects inactive management mode");
        Check((await app.StageConfigurationAsync(initial.Configuration.Revision, seed, "test")).Status == "Rejected", "direct save cannot reuse a revoked mode grant");
        await app.EnterManagementModeAsync(password);
        var logDirectory = Path.Combine(data, "log-fixture", "diagnostics"); Directory.CreateDirectory(logDirectory);
        var priorFile = Path.Combine(logDirectory, "runtime.jsonl.1"); var activeFile = Path.Combine(logDirectory, "runtime.jsonl");
        var now = DateTimeOffset.UtcNow;
        string Line(int number, bool hit) => JsonSerializer.Serialize(new { At = now.AddMilliseconds(number), Level = "信息", Source = "合成通信", Message = (hit ? "needle " : "other ") + number, password = "NEVER-EXPORT-THIS", Exception = "SyntheticException\n   at Original.Stack()" });
        File.WriteAllText(priorFile, Line(0, true) + "\n" + Line(1, false) + "\n" + Line(2, true) + "\n");
        File.WriteAllText(activeFile, string.Join("\n", Enumerable.Range(3, 305).Select(i => Line(i, true))) + "\n");
        var q = new SoftwareLogQuery(now.AddMinutes(-1), now.AddMinutes(1), "needle");
        using var reader = new SoftwareLogReader(logDirectory, admin);
        var page = await reader.QueryAsync(q, null, 0, default);
        Check(page.Entries.Length == 200 && page.HasMore, "grep query is bounded to 200 records per page");
        Check(page.Entries.All(e => !e.Text.Contains("NEVER-EXPORT-THIS")), "query results redact sensitive values before exposure");
        File.Move(priorFile, priorFile + ".moved"); File.Delete(priorFile + ".moved");
        var context = await reader.ContextAsync(page.Selection, page.Entries[1].Location, 1, 1, default);
        Check(context.Entries.Length == 3 && context.Entries[0].Text.Contains("other 1") && context.Entries[2].Text.Contains("needle 3"), "context uses original adjacent records across rotations, not previous/next matches");
        Check(context.Entries.All(e => e.Text.Contains("Original.Stack")), "full original exception stacks survive query and context");
        var export = Path.Combine(root, "all-matches.jsonl");
        Check(await reader.ExportAsync(page.Selection, export, false, 0, default) == 307, "export traverses every match beyond the current page");
        Check(!File.ReadAllText(export).Contains("NEVER-EXPORT-THIS") && File.ReadLines(export).First().Contains("ToExclusive"), "exports include original source/query range and exclude credentials");
        var timed = Path.Combine(root, "time-range.jsonl");
        Check(await reader.ExportAsync(page.Selection, timed, true, 0, default) == 308, "time-range export includes nonmatching records");
        var expanded = Path.Combine(root, "with-context.jsonl");
        Check(await reader.ExportAsync(page.Selection, expanded, false, 1, default) == 308, "overlapping original contexts export once without duplicates");
        await Reject<IOException>(() => reader.ExportAsync(page.Selection, activeFile, false, 0, default), "export cannot write into managed business/log sources");
        await Reject<IOException>(() => reader.ExportAsync(page.Selection, export, false, 0, default), "existing export files remain protected");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var cancelledFile = Path.Combine(root, "cancelled.jsonl");
        await Reject<OperationCanceledException>(() => reader.ExportAsync(page.Selection, cancelledFile, false, 0, cancel.Token), "cancelled exports do not publish partial success");
        Check(!File.Exists(cancelledFile) && !Directory.EnumerateFiles(root, "*.partial").Any(), "failed and cancelled exports clean partial files");
        await Reject<ArgumentException>(() => reader.QueryAsync(q with { Regex = true, Keyword = "[" }, null, 0, default), "invalid regex is reported without a shell invocation");
        File.WriteAllText(activeFile, JsonSerializer.Serialize(new { At = now, Message = new string('a', 30000) + "!" }) + "\n");
        await Reject<RegexMatchTimeoutException>(() => reader.QueryAsync(q with { Regex = true, Keyword = "(a+)+$" }, null, 0, default), "catastrophic regex terminates under a per-record timeout");
        await app.ExitManagementModeAsync();
        await Reject<UnauthorizedAccessException>(() => reader.ContextAsync(page.Selection, page.Entries[0].Location, 1, 1, default), "revoked sessions cannot read previously opened context");
        await Reject<UnauthorizedAccessException>(() => reader.ExportAsync(page.Selection, Path.Combine(root, "revoked.jsonl"), false, 0, default), "revoked sessions cannot export an old selection");
        await app.EnterManagementModeAsync(password);
        File.WriteAllText(activeFile, string.Join("\n", Enumerable.Range(0, 15000).Select(i => Line(i, true))) + "\n");
        var queryTask = app.QueryLogsAsync(q, null, 0, default);
        app.EmitReferenceAt("left", "ops-left", "LEFT-CODE", "ops-left-observation"); app.EmitReferenceAt("right", "ops-right", "RIGHT-CODE", "ops-right-observation");
        ParcelSnapshot state = await app.SnapshotAsync();
        for (int i = 0; i < 150 && state.Operations.Count(o => o.State == ParcelState.SlotResolved) < 2; i++) { await Task.Delay(20); state = await app.SnapshotAsync(); }
        await queryTask;
        Check(state.Operations.Count(o => o.State == ParcelState.SlotResolved) == 2 && state.Operations.Single(o => o.PositionId == "left").ParcelCode == "LEFT-CODE" && state.Operations.Single(o => o.PositionId == "right").ParcelCode == "RIGHT-CODE", "both feeding positions advance correctly while querying logs without the business gate: " + string.Join("; ", state.BlockingReasons));
        var all = await reader.QueryAsync(q, null, 0, default);
        var streaming = reader.ExportAsync(all.Selection, Path.Combine(root, "feeding-export.jsonl"), false, 0, default);
        app.EmitReferenceAt("left", "ops-left-next", "LEFT-NEXT", "ops-left-next-event");
        for (int i = 0; i < 150; i++) { state = await app.SnapshotAsync(); if (state.Operations.Any(o => o.ParcelCode == "LEFT-NEXT" && o.State == ParcelState.SlotResolved)) break; await Task.Delay(20); }
        Check(await streaming == 15000 && state.Operations.Any(o => o.ParcelCode == "LEFT-NEXT" && o.State == ParcelState.SlotResolved), "streaming complete export and another admitted parcel both finish without sharing the business lock");
        var beforeAudit = await app.SnapshotAsync(); await app.CommandAsync(new(Wire.Id(), beforeAudit.Revision, "Pause"));
        var pausedAudit = await app.SnapshotAsync();
        var unchanged = await app.StageConfigurationAsync(pausedAudit.Configuration.Revision, pausedAudit.Configuration.Effective, "Synthetic mode evidence audit");
        Check(unchanged.Status == "Unchanged", "management mode preserves the existing unified save and complete-candidate guard for an unchanged revision");
        using(var auditDb = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(data, "Simulation", seed.StationId, "sportmaster/sportmaster.db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            auditDb.Open(); using var command = auditDb.CreateCommand(); command.CommandText="SELECT body FROM parcel_configuration_audit ORDER BY rowid DESC LIMIT 1";
            var evidence = Wire.Decode<ConfigurationChangeAudit>((string)command.ExecuteScalar()!);
            Check(evidence.Actor is null && evidence.Authorization?.SessionId == admin.Evidence?.SessionId, "parameter audit records a separate management-mode grant without inventing an operator account");
        }
        await app.StopAsync(default);
        var unknownConfig = Path.Combine(root, "unknown-config"); Directory.CreateDirectory(unknownConfig);
        var uncertain = seed with { StationId = "unknown-" + Wire.Id(), Login = seed.MaintenanceLogin! with { InstanceId = "operator-login", Target = "reference://operator", Parameters = new() { ["subjects"] = "operator-a" } }, Wcs = seed.Wcs! with { Parameters = new(seed.Wcs!.Parameters) { ["reply-mode"] = "lose-after-commit" } } };
        ExtensionStore.AtomicWrite(Path.Combine(unknownConfig, "business.json"), uncertain);
        var unknownLaunch = new BusinessLaunch(unknownConfig, Path.Combine(root, "unknown-data"), null, null);
        var unsupported = new NoQueryFactory(unknownLaunch);
        await using var unknown = new ParcelRuntime(unknownLaunch, new SportmasterFlow(), unsupported) { RuntimeIdentityPreflight = () => { } };
        await unknown.StartAsync(default); Check((await unknown.LoginAsync("operator-a", "SIMULATION_ONLY")).Status == "Authenticated", "operator authentication remains its own real provider session"); await ManagementModeFixture.Authorize(unknown);
        unknown.EmitReferenceAt("left", "unknown-entity", "SAME-CODE", "first-unknown-event");
        ParcelSnapshot pending = await unknown.SnapshotAsync();
        for (int i = 0; i < 150 && pending.Operations.FirstOrDefault()?.State != ParcelState.WcsUnknown; i++) { await Task.Delay(20); pending = await unknown.SnapshotAsync(); }
        var originalOperator = pending.Operations.Single().Operator; var originalCurrentOperator = pending.CurrentOperator;
        await unknown.ExitManagementModeAsync(); await ManagementModeFixture.Authorize(unknown);
        var modeSwitched = await unknown.SnapshotAsync();
        Check(originalOperator is not null && originalCurrentOperator is not null && Wire.Encode(modeSwitched.Operations.Single().Operator) == Wire.Encode(originalOperator) && Wire.Encode(modeSwitched.CurrentOperator) == Wire.Encode(originalCurrentOperator), "exiting and re-entering management mode cannot rewrite the operator or an in-flight frozen parcel identity");
        var original = pending.Operations.Single().Wcs!.Request; var operationId = pending.Operations.Single().Id;
        Check(pending.Operations.Single().State == ParcelState.WcsUnknown && unsupported.Sends == 1, "lost reply retains one original unknown request");
        await unknown.ReconcileAsync(operationId);
        unknown.EmitReferenceAt("left", "unknown-entity", "SAME-CODE", "duplicate-unknown-event"); await Task.Delay(150);
        var afterUnknown = (await unknown.SnapshotAsync()).Operations.Single();
        Check(unsupported.Sends == 1 && afterUnknown.State == ParcelState.WcsUnknown && Wire.Encode(afterUnknown.Wcs!.Request) == Wire.Encode(original), "unsupported authoritative query and repeated input never resend or replace the frozen original request");
        await unknown.StopAsync(default);
    }
    sealed class NoQueryFactory(BusinessLaunch launch) : IParcelCapabilityFactory
    {
        readonly CommercialProductization.SyntheticFactory inner = new(launch);
        public int Sends => inner.Sends;
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public void ValidateStructure(ParcelConfiguration c) => inner.ValidateStructure(c);
        public CapabilityCatalog Catalog() => inner.Catalog();
        public CapabilityRequirements Requirements(CapabilityBinding binding) => ((IParcelCapabilityFactory)inner).Requirements(binding) with { Query = false };
        public ParcelPorts Create(ParcelConfiguration c) => inner.Create(c);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

}
