using System.Text.Json;
using System.Text.RegularExpressions;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static class OperatorAdministration
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL OPS: " + message); Console.WriteLine("PASS OPS: " + message); }
    static async Task Reject<T>(Func<Task> action, string message) where T : Exception
    { try { await action(); throw new Exception("Expected rejection: " + message); } catch (T) { Check(true, message); } }
    public static async Task Run(string workspace, string root)
    {
        var data = Path.Combine(root, "admin-log-data"); var admin = LocalAdministrator.ForRoot(data);
        var password = "Synthetic-" + Guid.NewGuid().ToString("N");
        Check(!admin.Status().Provisioned && !admin.Status().Authorized, "no default account or password grants software authority");
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => admin.Require()), "direct service calls reject before provisioning");
        admin.Provision("administrator", password);
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => admin.Provision("other", password)), "first-account bootstrap cannot overwrite existing accounts");
        Check(!File.ReadAllText(Path.Combine(data, "security/administrators.json")).Contains(password), "credential file contains salted hashes rather than passwords");
        for (var i = 0; i < 5; i++) Check(!admin.Login("administrator", "wrong"), "invalid credential grants no session " + i);
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => new LocalAdministrator(data).Login("administrator", password)), "persisted login lockout survives a new authenticator");
        // A separate store verifies the remaining session cases without bypassing lockout.
        data = Path.Combine(root, "runtime-admin-data"); admin = LocalAdministrator.ForRoot(data); admin.Provision("administrator", password); Check(admin.Login("administrator", password), "real password hash authentication succeeds");
        var delegated = admin.Delegate(new string('A', 64)); var helper = new LocalAdministrator(data);
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => helper.ValidateDelegation(delegated with { RequestHash = new string('B', 64) }, new string('B', 64), true)), "a copied helper ticket cannot change its server-bound request hash");
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => helper.ValidateDelegation(delegated with { ExpiresAt = delegated.ExpiresAt.AddMinutes(5) }, new string('A', 64), true)), "a helper ticket cannot extend its server-bound expiry");
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => helper.ValidateDelegation(delegated with { Id = Wire.Id() }, new string('A', 64), true)), "a helper ticket cannot mint another one-use authorization");
        helper.ValidateDelegation(delegated, new string('A', 64), true);
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => helper.ValidateDelegation(delegated, new string('A', 64), true)), "elevated helper delegation is single-use rather than Windows-only authorization");
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => helper.ValidateDelegation(delegated, new string('B', 64), false)), "helper authorization is bound to the exact original maintenance request");
        var grant = admin.Require(); admin.Maintain("second-admin", password + "-2", true); Check(grant.IsCancellationRequested && !admin.Status().Authorized, "account changes immediately revoke existing tasks and session");
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => helper.ValidateDelegation(delegated, new string('A', 64), false)), "account revocation also invalidates an already-issued helper delegation");
        admin.Login("administrator", password);
        admin.Maintain("second-admin", null, false); admin.Login("administrator", password);
        await Reject<InvalidDataException>(() => Task.Run(() => admin.Maintain("administrator", null, false)), "last enabled administrator remains protected");
        var expiry = new LocalAdministrator(Path.Combine(root, "expiry")); expiry.Provision("expiry-admin", password); expiry.Login("expiry-admin", password);
        var expiryGrant = expiry.Require(); expiry.Clock = () => DateTimeOffset.UtcNow.AddMinutes(16);
        Check(!expiry.Status().Authorized && expiryGrant.IsCancellationRequested, "session expiry revokes authorization and pending task tokens");
        Check(File.ReadAllText(Path.Combine(root, "expiry/security/diagnostics/runtime.jsonl")).Contains("AdministratorSessionExpired"), "session expiry is recorded in the actual authentication journal");
        var configRoot = Path.Combine(root, "admin-config"); Directory.CreateDirectory(configRoot);
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json"))) with { Login = null, StationId = "ops-" + Wire.Id() };
        ExtensionStore.AtomicWrite(Path.Combine(configRoot, "business.json"), seed);
        var launch = new BusinessLaunch(configRoot, data, null, null);
        var factory = new CommercialProductization.SyntheticFactory(launch);
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } };
        await app.StartAsync(default);
        await app.LoginAdministratorAsync("administrator", password);
        var initial = await app.SnapshotAsync();
        Check(initial.CurrentOperator is null, "administrator login never creates or changes a parcel operator");
        Check(!(await app.SnapshotAsync()).Paused, "administrator login and parameter catalog reads do not pause feeding");
        await app.CapabilitiesAsync();
        Check((await app.StageConfigurationAsync(initial.Configuration.Revision, seed with { TimeoutMs = 2000 }, "test")).Status == "Rejected", "authenticated configuration save still requires pause and drain");
        await app.LogoutAdministratorAsync();
        await Reject<UnauthorizedAccessException>(() => app.CapabilitiesAsync(), "optional operator login disabled still protects parameter reads");
        Check((await app.SnapshotAsync()).Configuration.Effective.Wcs!.Parameters.Count == 0, "operator snapshot cannot expose the editable provider configuration");
        await Reject<UnauthorizedAccessException>(() => app.ValidateConfigurationAsync(seed), "direct candidate validation rejects logged-out administrator");
        Check((await app.StageConfigurationAsync(initial.Configuration.Revision, seed, "test")).Status == "Rejected", "direct save cannot reuse a revoked administrator grant");
        await app.LoginAdministratorAsync("administrator", password);
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
        await app.LogoutAdministratorAsync();
        await Reject<UnauthorizedAccessException>(() => reader.ContextAsync(page.Selection, page.Entries[0].Location, 1, 1, default), "revoked sessions cannot read previously opened context");
        await Reject<UnauthorizedAccessException>(() => reader.ExportAsync(page.Selection, Path.Combine(root, "revoked.jsonl"), false, 0, default), "revoked sessions cannot export an old selection");
        await app.LoginAdministratorAsync("administrator", password);
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
        await app.StopAsync(default);
        var unknownConfig = Path.Combine(root, "unknown-config"); Directory.CreateDirectory(unknownConfig);
        var uncertain = seed with { StationId = "unknown-" + Wire.Id(), Wcs = seed.Wcs! with { Parameters = new(seed.Wcs!.Parameters) { ["reply-mode"] = "lose-after-commit" } } };
        ExtensionStore.AtomicWrite(Path.Combine(unknownConfig, "business.json"), uncertain);
        var unknownLaunch = new BusinessLaunch(unknownConfig, Path.Combine(root, "unknown-data"), null, null);
        var unsupported = new NoQueryFactory(unknownLaunch);
        await using var unknown = new ParcelRuntime(unknownLaunch, new SportmasterFlow(), unsupported) { RuntimeIdentityPreflight = () => { } };
        await unknown.StartAsync(default); await AdministratorFixture.Authorize(unknown);
        unknown.EmitReferenceAt("left", "unknown-entity", "SAME-CODE", "first-unknown-event");
        ParcelSnapshot pending = await unknown.SnapshotAsync();
        for (int i = 0; i < 150 && pending.Operations.FirstOrDefault()?.State != ParcelState.WcsUnknown; i++) { await Task.Delay(20); pending = await unknown.SnapshotAsync(); }
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
