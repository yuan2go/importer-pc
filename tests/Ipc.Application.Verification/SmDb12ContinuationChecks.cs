using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;

namespace Ipc.Verification;

internal static class SmDb12ContinuationChecks
{
    static void Check(bool ok, string reason)
    { if (!ok) throw new InvalidDataException(reason); Console.WriteLine("PASS DB12 " + reason); }
    public static async Task CrashChild(string config, string data, string identity)
    {
        await using var service = Service(new(config, data, null, null));
        service.ContinuationBoundary = point => { if (point == "BeforeDb12Commit") Environment.Exit(73); };
        await service.ContinueSmDb12Async(identity);
    }
    static ParcelStartupMaintenance Service(BusinessLaunch launch) => new(launch, new SportmasterFlow(), () => new ManualSupplyHarness.ManualFactory(launch), () => { }, () => "synthetic-maintainer")
    { MaintenanceWorkspace = () => { var path = Path.Combine(Path.GetTempPath(), "db12-check-" + Wire.Id()); Directory.CreateDirectory(path); return path; } };
    public static async Task Run(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "db12"), deferred: true, mode: RunMode.Simulation);
        await h.StartAsync();
        var firstCommand = await h.Command("left", "KEEP-DB12-001");
        Check(!Wire.Encode(firstCommand).Contains("ExpectedRoundId", StringComparison.Ordinal), "an absent round precondition preserves the original command identity bytes");
        var first = await h.App.SubmitParcelBarcodeAsync(firstCommand, default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(first.OperationId!))?.Wcs?.State == StageState.Accepted);
        h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "KEEP-DB12-002"), default);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 2));
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(second.OperationId!))?.Wcs?.State == StageState.Unknown);
        h.Peer.Hold.TrySetResult(); h.Peer.Hold = null;
        await h.App.DisposeAsync();
        var directory = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, "sportmaster");
        var database = Path.Combine(directory, "sportmaster.db");
        var oldDatabase = Path.Combine(root, "original-db12.db");
        // Frozen DB12 SQL is pinned to the original writer's hash. Copy the shared
        // tables from real production transactions; no request/receipt logic is recreated.
        using (var db = Database.Open(oldDatabase))
        {
            Database.CreateBaseline(db, "sportmaster", 12, ParcelRepository.SmDb12Baseline);
            using var q = db.CreateCommand(); q.CommandText = "ATTACH DATABASE $source AS previous"; q.Parameters.AddWithValue("$source", database); q.ExecuteNonQuery();
            q.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT IN ('platform_migrations','sqlite_sequence')";
            List<string> tables = []; using (var r = q.ExecuteReader()) while (r.Read()) tables.Add(r.GetString(0));
            foreach (var table in tables)
            { q.CommandText = $"INSERT OR REPLACE INTO main.\"{table}\" SELECT * FROM previous.\"{table}\""; q.ExecuteNonQuery(); }
            q.CommandText = "UPDATE parcel_operations SET body=json_remove(body,'$.RoundId','$.IngressId'); DELETE FROM sqlite_sequence; INSERT INTO sqlite_sequence SELECT * FROM previous.sqlite_sequence; DETACH DATABASE previous";
            q.ExecuteNonQuery();
            // Capture a stopped-writer DB/WAL pair while committed facts are still
            // in WAL. The source writer is a separate synthetic fixture only.
            File.Copy(oldDatabase, database, true);
            if (File.Exists(oldDatabase + "-wal")) File.Copy(oldDatabase + "-wal", database + "-wal", true);
        }
        var ledger = Path.Combine(directory, "configuration.json");
        var configBytes = File.ReadAllBytes(ledger); var seedBytes = File.ReadAllBytes(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"));
        var current = ConfigurationRevisions<ParcelConfiguration>.ReadCurrentEnvelope(File.ReadAllText(ledger));
        Check(!File.ReadAllText(ledger).Contains("ReadTimeoutMs", StringComparison.Ordinal) && current.Effective.EffectiveReadTimeoutMs == 1000,
            "an omitted default preserves the original confirmed hash without rewriting the ledger");
        var policyDraft = ConfigurationPolicyDraft.From(current.Effective);
        Check(policyDraft.ApplyParameters(current.Effective).ReadTimeoutMs is null, "opening and applying unchanged settings does not materialize the omitted default");
        policyDraft.ReadTimeoutMs = "1500";
        Check(policyDraft.ApplyParameters(current.Effective).ReadTimeoutMs == 1500, "a deliberately changed read deadline is persisted explicitly");
        var evidence = Rows(database);
        var requests = h.Peer.Requests.Count;
        var oldRequestCeiling = h.Peer.Requests.Select(body => { using var json = JsonDocument.Parse(body); return json.RootElement.GetProperty("requestId").GetInt64(); }).Max();
        await using var service = Service(h.Launch);
        var read = await service.ReadAsync();
        Check(read.Candidate is not null && read.NeedsSmDb12Continuation, "offline read retains original parameters and explicitly identifies pinned DB12");
        await using (var unauthorized = new ParcelStartupMaintenance(h.Launch, new SportmasterFlow(), () => new ManualSupplyHarness.ManualFactory(h.Launch), () => throw new UnauthorizedAccessException()))
        {
            try { await unauthorized.PlanSmDb12ContinuationAsync(); throw new InvalidOperationException("missing authority accepted"); }
            catch (UnauthorizedAccessException) { Check(true, "actual maintenance execution requires authority"); }
            try { await unauthorized.ContinueSmDb12Async("untrusted"); throw new InvalidOperationException("missing continuation authority accepted"); }
            catch (UnauthorizedAccessException) { Check(true, "the mutating continuation entry also requires actual authority before reading or writing"); }
        }
        using (var owner = new ResourceLease(directory, ["application-owner"]))
        {
            try { await service.PlanSmDb12ContinuationAsync(); throw new InvalidOperationException("live owner accepted"); }
            catch (IOException) { Check(true, "a live application owner prevents continuation"); }
        }
        var originalHash = Wire.FileHash(database);
        var plan = await service.PlanSmDb12ContinuationAsync();
        Check(plan.Allowed && Wire.FileHash(database) == originalHash && File.ReadAllBytes(ledger).SequenceEqual(configBytes) && h.Peer.Requests.Count == requests,
            "planning uses a private converted copy and leaves the original files and network untouched");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { typeof(SmDb12ContinuationChecks).Assembly.Location, workspace, "--sm-db12-crash-child", h.Launch.ConfigurationDirectory, h.Launch.DataRoot, plan.Identity }) start.ArgumentList.Add(arg);
        using (var child = Process.Start(start)!)
        {
            var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            Check(child.ExitCode == 73, "a real child process terminates before the schema transaction commits: " + await stdout + await stderr);
        }
        Check((await service.ReadAsync()).NeedsSmDb12Continuation && Rows(database).SequenceEqual(evidence),
            "process termination rolls back all schema changes and preserves original rows including WAL-backed facts");
        plan = await service.PlanSmDb12ContinuationAsync();
        service.ContinuationBoundary = point => { if (point == "AfterDb12Commit") throw new IOException("synthetic lost commit reply"); };
        try { await service.ContinueSmDb12Async(plan.Identity); throw new InvalidOperationException("lost reply expected"); }
        catch (InstallationOutcomeUnknownException error) when (error.InnerException?.Message == "synthetic lost commit reply")
        { Check(true, "post-commit failures retain unknown-outcome semantics instead of claiming unchanged data"); }
        service.ContinuationBoundary = null;
        var completed = await service.ContinueSmDb12Async(plan.Identity);
        Check(completed.Candidate is not null && !completed.NeedsSmDb12Continuation && ParcelRepository.ReadStorageVersion(database, "sportmaster") == 13,
            "a lost post-commit reply resolves from the original transaction receipt without another conversion");
        Check(File.ReadAllBytes(ledger).SequenceEqual(configBytes) && File.ReadAllBytes(Path.Combine(h.Launch.ConfigurationDirectory, "business.json")).SequenceEqual(seedBytes),
            "continuation never changes the seed, Effective/Pending configuration or their hashes");
        Check(Rows(database).SequenceEqual(evidence), "all original request bodies, receipts, commands, permanent owners and ranges survive unchanged");
        var originals = Directory.GetFiles(Path.Combine(directory, "startup-maintenance"), "sportmaster.db", SearchOption.AllDirectories);
        Check(originals.Length >= 2 && originals.Any(p => Wire.FileHash(p) == originalHash), "pre-commit DB and configuration originals remain in custody after interruption and success");
        await h.StartAsync(rcsImplementationFingerprint: "new-host-after-db12");
        var s = await h.App.SnapshotAsync();
        Check(h.Peer.Requests.Count == requests && (await h.App.OperationAsync(first.OperationId!)) is not null &&
            (await h.App.OperationAsync(second.OperationId!)) is { Wcs.State: StageState.Unknown },
            "upgrade startup retains confirmed and unknown originals without replay, query or obsolete-request deletion");
        Check((await h.App.SubmitParcelBarcodeAsync(firstCommand, default)).OperationId == first.OperationId && h.Peer.Requests.Count == requests,
            "the original CommandId replays its receipt without sending again");
        Check(s.Capacity is { HistoricalUnknown: 1, CurrentRounds: 0, ExecutingRequests: 0 }, "old unknown custody is counted separately from current rounds and real execution");
        await ManagementModeFixture.Authorize(h.App);
        var fullConfiguration = ConfigurationRevisions<ParcelConfiguration>.ReadCurrentEnvelope(File.ReadAllText(ledger)).Effective;
        var saved = await h.App.StageConfigurationAsync(s.Configuration.Revision, fullConfiguration with { ReadTimeoutMs = 1500 }, "Synthetic setting after DB12 custody");
        var staged = await h.App.SnapshotAsync();
        Check(saved.Status == "PendingRestart" && staged.ConfigurationActivationBlock is null,
            "closed DB12 request custody does not block a legitimate pending configuration; actual pending receipts still do: " + saved.Status + " / " + saved.Reason + " / " + staged.ConfigurationActivationBlock);
        var next = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "KEEP-DB12-001"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(next.OperationId!))?.Wcs?.State == StageState.Accepted);
        var later = (await h.App.OperationAsync(next.OperationId!))!;
        using var newWire = JsonDocument.Parse(h.Peer.Requests.Last());
        Check(h.Peer.Requests.Count == requests + 1 && later.Id != first.OperationId &&
            newWire.RootElement.GetProperty("requestId").GetInt64() > oldRequestCeiling,
            "a deliberate new same-code round makes one request above the preserved permanent number ceiling");
        await h.App.DisposeAsync();
        var draining = Path.Combine(root, "pending-stop"); Directory.CreateDirectory(draining);
        var drainingDb = Path.Combine(draining, "sportmaster.db"); File.Copy(originals[0], drainingDb);
        if (File.Exists(originals[0] + "-wal")) File.Copy(originals[0] + "-wal", drainingDb + "-wal");
        using (var db = Database.Open(drainingDb, false))
        {
            using var q = db.CreateCommand();
            q.CommandText = "INSERT INTO parcel_audit(at,event,owner,detail) VALUES($at,'PositionSupplyChanged','position-control:left',$body)";
            q.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            q.Parameters.AddWithValue("$body", Wire.Encode(new StationPositionState("left", StationPositionMode.Disabling, 1, DateTimeOffset.UtcNow, "synthetic", first.OperationId, null)));
            q.ExecuteNonQuery();
        }
        var drainingHash = Wire.FileHash(drainingDb);
        try { ParcelRepository.ContinueSmDb12(drainingDb, "pending-stop"); throw new InvalidOperationException("unfinished placement inferred ended"); }
        catch (InvalidDataException error)
        { Check(error.Message.Contains("停用中", StringComparison.Ordinal) && Wire.FileHash(drainingDb) == drainingHash, "pending-stop placement responsibility refuses continuation instead of inventing a cleared tray"); }
        // Forged identity and changed configuration must still refuse; a current
        // schema marker is never an alternative to checking the actual structure.
        var bad = Path.Combine(root, "forged"); Directory.CreateDirectory(bad);
        var badDb = Path.Combine(bad, "sportmaster.db"); File.Copy(originals[0], badDb);
        if (File.Exists(originals[0] + "-wal")) File.Copy(originals[0] + "-wal", badDb + "-wal");
        using (var db = Database.Open(badDb, false)) Database.Execute(db, null, "CREATE TABLE unapproved(id TEXT)");
        var badHash = Wire.FileHash(badDb);
        try { ParcelRepository.ContinueSmDb12(badDb, "forged"); throw new InvalidOperationException("changed schema accepted"); }
        catch (InvalidDataException) { Check(Wire.FileHash(badDb) == badHash, "changed DB12 structures refuse before any writable open"); }
        var changed = current with { Effective = current.Effective with { TimeoutMs = current.Effective.TimeoutMs + 1 } };
        try { ConfigurationRevisions<ParcelConfiguration>.ReadCurrentEnvelope(Wire.Encode(changed)); throw new InvalidOperationException("changed configuration accepted"); }
        catch (InvalidDataException) { Check(true, "omission stability does not weaken original configuration hash verification"); }
    }
    static string[] Rows(string database)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
        List<string> result = [];
        foreach (var table in new[] { "parcel_operations", "parcel_archives", "parcel_receipts", "parcel_archived_receipts", "parcel_reply_identities", "parcel_commands", "parcel_integration_id_owners", "parcel_integration_id_ranges", "parcel_integration_id_restores" })
        {
            using var q = db.CreateCommand(); q.CommandText = $"SELECT * FROM \"{table}\" ORDER BY 1";
            using var r = q.ExecuteReader(); while (r.Read())
                result.Add(table + ":" + Wire.Encode(Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? null : r.GetValue(i)).ToArray()));
        }
        return result.ToArray();
    }
}
