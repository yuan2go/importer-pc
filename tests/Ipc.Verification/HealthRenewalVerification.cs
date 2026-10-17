using System.Diagnostics;
using System.Text.Json;
using Ipc.Platform;
using Ipc.Supply;
using Microsoft.Data.Sqlite;

static class HealthRenewalVerification
{
    public static async Task Run(string root, string url)
    {
        Console.WriteLine("RUN AC16 health renewal authority, durable transitions and checkpoint/restart");
        var directory = Path.Combine(root, "health-authority"); var clock = new TestClock(); var cfg = Rig.Config(url);
        long sequence = 0; var epoch = Wire.Id();
        IntegrationHealthFact Fact(ServiceStatus status = ServiceStatus.Ready, string? failure = null) =>
            new("builtin", "requests", cfg.Version, new string('B', 64), cfg.Integration, epoch, ++sequence, status, clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(5), failure);
        void Assert(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        await using (var h = await Rig.Open(directory, cfg, clock: clock))
        {
            await h.OpenBox(); var op = await h.Enter("left"); h.AutoConfirmSyntheticHealth = false;
            await h.App.SetIntegrationHealthAsync([Fact()]);
            var start = await h.App.SnapshotAsync(); var audit = h.Store.Audits(cfg.StationId).Length;
            var device = start.State.Devices["left"]; clock.Advance(TimeSpan.FromSeconds(1));
            var freshDevice = new DeviceHealthConfirmation(device.DeviceId, device.Epoch, DeviceHealth.Ready, clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(5), device.Source);
            await h.App.SetDeviceHealthAsync(freshDevice); var fact = Fact(); await h.App.SetIntegrationHealthAsync([fact]);
            var renewed = await h.App.SnapshotAsync();
            Assert(renewed.State.Revision == start.State.Revision && renewed.State.Devices["left"].ConfirmedAt == freshDevice.ConfirmedAt && renewed.State.ServiceHealth["requests"] == fact,
                "Every renewal reaches current authority without invalidating unrelated business CAS");
            Assert(h.Store.Load().Devices["left"].ConfirmedAt == device.ConfirmedAt && h.Store.Audits(cfg.StationId).Length == audit, "Renewals alone do not persist a second permit ledger");
            await h.Command("Pause"); Assert(h.Store.Load().Devices["left"].ConfirmedAt == freshDevice.ConfirmedAt, "Next business transaction checkpoints latest confirmation");
            await h.App.SetIntegrationHealthAsync([fact with { Status = ServiceStatus.Unavailable, ValidUntil = fact.ValidUntil.AddSeconds(60) }]);
            await h.App.SetDeviceHealthAsync(freshDevice with { Epoch = "old-epoch", Health = DeviceHealth.Faulted });
            Assert((await h.App.SnapshotAsync()).State.ServiceHealth["requests"] == fact && (await h.App.SnapshotAsync()).State.Devices["left"].Epoch == device.Epoch,
                "Non-newer sequence and unregistered epoch cannot replace accepted health");
            clock.Advance(TimeSpan.FromSeconds(6)); var expired = await h.App.SnapshotAsync(); await h.App.SnapshotAsync();
            Assert(expired.State.Devices["left"].ConfirmedAt == freshDevice.ConfirmedAt && expired.Admit.Reasons.Any(x => x.Contains("Expired")), "Read cannot renew expired source or service authority");
            await h.App.SetDeviceHealthAsync(new(device.DeviceId, device.Epoch, DeviceHealth.Ready, clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(5), device.Source));
            await h.App.SetIntegrationHealthAsync([Fact()]);
            Assert(h.Store.Load().ServiceHealth["requests"].ConfirmedAt == clock.GetUtcNow(), "Expired-to-confirmed transition is durable");
            clock.Advance(TimeSpan.FromSeconds(1)); await h.App.SetIntegrationHealthAsync([Fact(ServiceStatus.Unavailable, "offline")]);
            Assert(h.Store.Load().ServiceHealth["requests"].Status == ServiceStatus.Unavailable && (await h.App.SnapshotAsync()).Admit.Reasons.Any(x => x.StartsWith("Wcs")), "Disconnect transition persists and blocks actual guard");
            clock.Advance(TimeSpan.FromSeconds(1)); await h.App.SetIntegrationHealthAsync([Fact()]);
            // Actual observations are never coalesced with health renewals.
            await h.Event("left", "Read", op.AdmissionId, ["000123"], 1);
            using var db = new SqliteConnection("Data Source=" + h.Store.Path); db.Open(); using var command = db.CreateCommand();
            command.CommandText = "SELECT count(*) FROM observations"; Assert(Convert.ToInt32(command.ExecuteScalar()) == 2, "Every admitted observation remains individually persisted");
        }
        using (var store = new SupplyStore(Path.Combine(directory, "station.db")))
        await using (var app = new StationApplication(store, cfg, new Ipc.Adapters.SimulationRequestContract(cfg), clock: clock))
        {
            var restarted = await app.SnapshotAsync();
            Assert(restarted.State.Devices.Values.All(x => x.Epoch == "not-connected" && x.ConfirmedAt is null) && restarted.State.ServiceHealth.Count == 0 && !restarted.Admit.Allowed,
                "Restart never trusts checkpointed health; both sources and integration must reconfirm");
        }
        Console.WriteLine("PASS AC16 health renewal authority, durable transitions and checkpoint/restart");
    }
    // Identical deterministic two-source workload is run before and after the change.
    // data_version observes commits from another connection; audit and trigger counts
    // independently expose full aggregate rewrites, including unchanged operation rows.
    public static async Task Profile(string root, string url)
    {
        var clock = new TestClock(); var cfg = Rig.Config(url);
        await using var h = await Rig.Open(Path.Combine(root, "health-profile"), cfg, clock: clock);
        await h.OpenBox(); await h.Enter("left"); await h.Enter("right"); h.AutoConfirmSyntheticHealth = false;
        using var observer = new SqliteConnection("Data Source=" + h.Store.Path); observer.Open();
        long Scalar(string sql) { using var c = observer.CreateCommand(); c.CommandText = sql; return Convert.ToInt64(c.ExecuteScalar()); }
        using (var command = observer.CreateCommand())
        {
            command.CommandText = "CREATE TABLE profile_writes(kind TEXT); CREATE TRIGGER profile_state AFTER UPDATE ON state BEGIN INSERT INTO profile_writes VALUES('state'); END; CREATE TRIGGER profile_operation AFTER UPDATE ON operations BEGIN INSERT INTO profile_writes VALUES('operation'); END;";
            command.ExecuteNonQuery();
        }
        var auditBefore = Scalar("SELECT count(*) FROM audit"); var version = Scalar("PRAGMA data_version"); var transactions = 0;
        var latencies = new List<double>(); long sequence = 0;
        var serviceEpoch = Wire.Id();
        // Establish the integration's stable identity before measuring repeat renewals.
        IntegrationHealthFact Fact() => new("builtin", "requests", cfg.Version, new string('A', 64), cfg.Integration, serviceEpoch, ++sequence, ServiceStatus.Ready, clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(5), null);
        await h.App.SetIntegrationHealthAsync([Fact()]);
        auditBefore = Scalar("SELECT count(*) FROM audit"); version = Scalar("PRAGMA data_version");
        using (var clear = observer.CreateCommand()) { clear.CommandText = "DELETE FROM profile_writes"; clear.ExecuteNonQuery(); }
        async Task Sample(Task work)
        {
            await work; var current = Scalar("PRAGMA data_version"); if (current != version) transactions++; version = current;
        }
        for (int i = 0; i < 120; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            foreach (var device in (await h.App.SnapshotAsync()).State.Devices.Values)
                await Sample(h.App.SetDeviceHealthAsync(new(device.DeviceId, device.Epoch, DeviceHealth.Ready, clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(5), device.Source)));
            await Sample(h.App.SetIntegrationHealthAsync([Fact()]));
            var timer = Stopwatch.StartNew(); await h.App.SnapshotAsync(); timer.Stop(); latencies.Add(timer.Elapsed.TotalMilliseconds);
        }
        latencies.Sort();
        var result = new { Scenario = "120 virtual seconds, two Ready sources + one Ready requests service; two occupied lanes; no business observations sampled", HealthEvents = 360,
            ExternalCommitIntervals = transactions, AuditGrowth = Scalar("SELECT count(*) FROM audit") - auditBefore,
            StateRewrites = Scalar("SELECT count(*) FROM profile_writes WHERE kind='state'"), OperationRewrites = Scalar("SELECT count(*) FROM profile_writes WHERE kind='operation'"),
            SnapshotMedianMs = latencies[latencies.Count / 2], SnapshotP95Ms = latencies[(int)(latencies.Count * .95)], SnapshotMaxMs = latencies[^1] };
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "health-profile.json"), json + "\n"); Console.WriteLine(json);
    }
}
