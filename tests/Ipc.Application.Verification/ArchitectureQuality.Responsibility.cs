using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Assembly;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static partial class ArchitectureQuality
{
    static string DirectoryFor(string run, ParcelConfiguration c) => Path.Combine(root, run, "data", c.Mode.ToString(), c.StationId, c.ApplicationId);
    static async Task ResponsibilityChecks()
    {
        var sport = Config("sportmaster") with { MaximumOperations = 2 };
        string firstId, firstBody;
        await using (var app = await Open(sport, "archive-cycle"))
        {
            for (var i = 0; i < 12; i++)
            {
                var position = i % 2 == 0 ? "left" : "right";
                app.EmitReferenceAt(position, "archive-" + i, "00001", "archive-source-" + i);
                await Wait(app, s => s.Operations.Any(o => o.AdmissionId == "archive-" + i && o.State == ParcelState.SlotResolved));
            }
            var page = await app.HistoryAsync(long.MaxValue, 3); var seen = new List<ParcelHistoryItem>(page.Items);
            while (page.NextBeforeSequence is { } cursor) { page = await app.HistoryAsync(cursor, 3); seen.AddRange(page.Items); }
            Check(seen.Count == 12 && seen.Select(x => x.Operation.Id).Distinct().Count() == 12 && seen.Count(x => x.Archived) >= 10, "bounded keyset pages include every original archived/resident operation exactly once");
            var first = seen.Single(x => x.Operation.AdmissionId == "archive-0"); firstId = first.Operation.Id; firstBody = Wire.Encode(first.Operation);
            var state = await app.SnapshotAsync(); Check(state.Capacity!.Resident <= 2 && state.Capacity.Available && !state.Paused, "repeated real main chains keep resident work bounded while archived history grows");
            await Maintenance(app); await app.BackupAsync(Path.Combine(root, "archive-cycle.backup.db"));
        }
        await using (var app = await Open(sport, "archive-cycle"))
        {
            await Command(app, "Resume"); app.EmitReferenceAt("left", "archive-0", "00001", "archive-source-0"); await Task.Delay(100);
            Check((await app.HistoryAsync(long.MaxValue, 50)).Items.Length == 12 && Wire.Encode(await app.OperationAsync(firstId)) == firstBody, "restart deduplicates permanent archived admission and retains original JSON/request bytes");
        }
        ParcelRecovery.Restore(Path.Combine(root, "archive-cycle.backup.db"), DirectoryFor("archive-restored", sport));
        await using (var app = await Open(sport, "archive-restored")) Check((await app.HistoryAsync(long.MaxValue, 50)).Items.Length == 12, "actual backup/isolated restore retains full paged history and identity index");

        var conflictConfig = Config("sportmaster"); string conflictId;
        await using (var app = await Open(conflictConfig, "conflict"))
        {
            app.EmitReferenceAt("left", "same-identity", "001", "original-left"); await Wait(app, s => s.Operations.Any(o => o.State == ParcelState.SlotResolved));
            app.EmitReferenceAt("right", "same-identity", "002", "conflicting-right");
            var s = await Wait(app, s => s.IdentityConflicts?.Length == 1); conflictId = s.IdentityConflicts!.Single().Id;
            Check(s.Positions!.All(p => !p.Admit) && s.Operations.Single().ParcelCode == "001", "cross-position identity conflict durably blocks both affected positions and preserves original entity");
        }
        await using (var app = await Open(conflictConfig, "conflict"))
        {
            Check((await app.SnapshotAsync()).IdentityConflicts!.Single().Id == conflictId, "identity conflict remains a stable responsibility after restart");
            await Maintenance(app); await Command(app, "AcknowledgeIdentityConflict", conflictId, "Synthetic inspection: conflicting source isolated; original entity retained");
            await Command(app, "Resume"); Check((await app.SnapshotAsync()).Positions!.All(p => p.Admit), "authorized identity disposition recovers affected positions without resending original WCS");
        }
        await using (var app = await Open(Config("sportmaster") with { Retention = new(MinimumFreeBytes: 1099511627776) }, "storage-reserve"))
        {
            var s = await app.SnapshotAsync(); Check(!s.Availability!.Admit && !s.Availability.Resume && !s.Capacity!.Available && s.BlockingReasons.Any(r => r.Contains("Storage reserve")), "actual disk reserve policy shares admission/Resume/projection without filling or deleting the disk");
        }
        await OverloadChecks();
        await ConfigurationChecks();
    }
    static async Task OverloadChecks()
    {
        var c = Config("sportmaster"); var path = DirectoryFor("overload-durable", c); var marker = Path.Combine(path, "overload.json");
        await using (var app = await Open(c, "overload-durable")) await Maintenance(app);
        ExtensionStore.AtomicWrite(marker, new { At = DateTimeOffset.UtcNow, Positions = new[] { "left" }, Device = c.Reader.DeviceId, Reason = "Synthetic persisted callback overload" });
        string ackId;
        await using (var app = await Open(c, "overload-durable"))
        {
            await app.AuthorizeMaintenanceAsync("maintenance", "SIMULATION_ONLY");
            var before = await app.SnapshotAsync(); var originalMarker = File.ReadAllText(marker);
            using (var db = new SqliteConnection("Data Source=" + Path.Combine(path, "sportmaster.db")))
            {
                db.Open(); using var tx = db.BeginTransaction();
                try { await Task.Run(() => app.CommandAsync(new(Wire.Id(), before.Revision, "AcknowledgeOverload"))); Check(false, "SQLite writer contention must reject acknowledgement"); }
                catch (SqliteException e) { Check(e.SqliteErrorCode == 5 && File.ReadAllText(marker) == originalMarker, "AQ02 real SQLITE_BUSY preserves original marker byte-for-byte before acknowledgement commit"); }
                tx.Rollback();
            }
            var s = await app.SnapshotAsync(); Check(s.BlockingReasons.Any(x => x.Contains("Overload")), "failed acknowledgement cannot clear in-memory overload responsibility");
            app.Boundary = p => { if (p == "AfterOverloadAcknowledged") throw new IOException("Synthetic interruption after durable ack, before marker cleanup"); };
            ackId = Wire.Id(); s = await app.SnapshotAsync();
            try { await app.CommandAsync(new(ackId, s.Revision, "AcknowledgeOverload")); Check(false, "post-commit boundary must interrupt cleanup"); }
            catch (IOException) { Check(File.Exists(marker), "post-commit interruption retains marker for deterministic restart cleanup"); }
        }
        await using (var app = await Open(c, "overload-durable"))
        {
            var s = await app.SnapshotAsync();
            using var db = new SqliteConnection("Data Source=" + Path.Combine(path, "sportmaster.db")); db.Open(); using var q = db.CreateCommand(); q.CommandText = "SELECT count(*) FROM parcel_commands WHERE id=$id"; q.Parameters.AddWithValue("$id", ackId);
            Check(!File.Exists(marker) && !s.BlockingReasons.Any(x => x.Contains("Overload")) && (long)q.ExecuteScalar()! == 1, "restart cleans only durably acknowledged marker and retains original command receipt");
        }
    }
    static async Task ConfigurationChecks()
    {
        var c = Config("sportmaster");
        await using (var app = await Open(c, "config-audit"))
        {
            await Maintenance(app); var s = await app.SnapshotAsync();
            var candidate = s.Configuration.Effective with { TimeoutMs = 1800 };
            Check((await app.ValidateConfigurationAsync(candidate)).Status == "Validated" && (await app.SnapshotAsync()).Configuration.Pending is null, "full topology candidate can be validated without saving or activating");
            app.Boundary = p => { if (p == "AfterConfigurationStaged") throw new IOException("Synthetic envelope/audit interruption"); };
            try { await app.StageConfigurationAsync(s.Configuration.Revision, candidate, "Synthetic maintenance timeout revision"); Check(false, "audit interruption must be visible"); }
            catch (IOException) { Check((await app.SnapshotAsync()).Configuration.Pending?.TimeoutMs == 1800, "interrupted save retains authoritative Pending and durable audit intention"); }
        }
        await using (var app = await Open(c, "config-audit"))
        {
            var s = await app.SnapshotAsync(); Check(s.Configuration.Effective.TimeoutMs == 1800, "controlled restart activates complete original candidate");
            using var db = new SqliteConnection("Data Source=" + Path.Combine(DirectoryFor("config-audit", c), "sportmaster.db")); db.Open(); using var q = db.CreateCommand(); q.CommandText = "SELECT body FROM parcel_configuration_audit";
            var audit = Wire.Decode<ConfigurationChangeAudit>((string)q.ExecuteScalar()!);
            Check(audit.Actor?.Subject == "maintenance" && audit.Status == "Activated" && audit.BeforeRevision == 0 && audit.AfterRevision == 2 && audit.BeforeHash != audit.CandidateHash && audit.Reason.Contains("timeout"), "restart closes configuration audit with original maintenance actor/reason, before/after revisions and exact hashes");
            await app.AuthorizeMaintenanceAsync("maintenance", "SIMULATION_ONLY");
            Check((await app.StageConfigurationAsync(0, s.Configuration.Effective, "Stale synthetic editor")).Status == "Rejected", "configuration CAS still rejects a stale full editor after activation");
        }
    }
}
