using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static class InstallerChangeGuards
{
    public static async Task Run(string workspace, string root)
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
        async Task Reject(Func<Task> action, string message)
        {
            var rejected = false;
            try { await action(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, message);
        }
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        foreach (var unknown in new[] { false, true })
        {
            var config = Path.Combine(root, unknown ? "unknown-config" : "settled-config"); Directory.CreateDirectory(config);
            var c = seed with { Wcs = seed.Wcs! with { Parameters = new(seed.Wcs.Parameters) { ["reply-mode"] = unknown ? "lose-after-commit" : "normal" } } };
            File.WriteAllText(Path.Combine(config, "business.json"), Wire.Encode(c));
            var launch = new BusinessLaunch(config, Path.Combine(root, unknown ? "unknown-data" : "settled-data"), null, null);
            var factory = new CommercialProductization.SyntheticFactory(launch);
            await using (var runtime = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } })
            {
                await runtime.StartAsync(default);
                if (unknown)
                {
                    runtime.EmitReferenceAt("left", "installer-unknown-entity", "SYNTHETIC-INSTALLER-GUARD", "installer-observation");
                    ParcelSnapshot snapshot = await runtime.SnapshotAsync();
                    for (var i = 0; i < 100 && snapshot.Operations.FirstOrDefault()?.Wcs?.State != StageState.Unknown; i++)
                    { await Task.Delay(20); snapshot = await runtime.SnapshotAsync(); }
                    Check(snapshot.Operations.Single().Wcs?.State == StageState.Unknown && factory.Sends == 1, "actual lost-after-commit reference leaves one original Unknown request");
                }
            }
            var service = new ParcelStartupMaintenance(launch, new SportmasterFlow(), () => new CommercialProductization.SyntheticFactory(launch), () => { })
            { MaintenanceWorkspace = () => Directory.CreateDirectory(Path.Combine(root, "scratch-" + Wire.Id())).FullName };
            var read = await service.ReadAsync();
            var database = Path.Combine(read.RuntimeDirectory!, "sportmaster.db"); var before = Wire.FileHash(database);
            var destination = Path.Combine(root, unknown ? "unknown-backup.db" : "settled-backup.db");
            var plan = await service.PlanChangeAsync("Upgrade", destination); var committed = false;
            Check(plan.Allowed == !unknown && Wire.FileHash(database) == before, "original upgrade preflight preserves database and refuses Unknown=" + unknown);
            if (unknown)
            {
                await Reject(() => service.ExecuteChangeAsync(plan.Identity, "Upgrade", destination, () => { committed = true; return Task.CompletedTask; }), "a refused plan cannot enter backup or deployment callback");
                Check(!committed && !File.Exists(destination) && Wire.FileHash(database) == before && factory.Sends == 1, "blocked upgrade preserves original bytes and never sends or commits again");
            }
            else
            {
                await service.StageAsync(read.ConfigurationHash, read.Candidate! with { TimeoutMs = read.Candidate!.TimeoutMs + 1 }, "synthetic change after preflight");
                await Reject(() => service.ExecuteChangeAsync(plan.Identity, "Upgrade", destination, () => { committed = true; return Task.CompletedTask; }), "configuration changed after planning refuses deployment");
                Check(!committed && !File.Exists(destination), "stale plan creates neither backup nor deployment commit");
                plan = await service.PlanChangeAsync("Upgrade", destination);
                await service.ExecuteChangeAsync(plan.Identity, "Upgrade", destination, () => { committed = true; return Task.CompletedTask; });
                Check(committed && File.Exists(destination + ".recovery/COMPLETED.json") && Wire.FileHash(database) == before, "fresh plan completes full backup before deployment callback while retaining original database");
            }
        }
        Console.WriteLine($"PASS {checks} focused installer change guards; synthetic identity/authority only, no Windows restore or real devices");
    }
}
