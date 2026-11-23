using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;

static class InstallerChangeGuards
{
    public static async Task Run(string workspace, string root)
    {
        var originalUserRoot = WindowsStoragePaths.UserRootForVerification;
        var userRoot = Directory.CreateDirectory(Path.Combine(root, "read-cache-user")).FullName;
        WindowsStoragePaths.UserRootForVerification = () => userRoot;
        try { await VerifyInstallAndRollbackAsync(workspace, root); }
        finally { WindowsStoragePaths.UserRootForVerification = originalUserRoot; }
    }

    static async Task VerifyInstallAndRollbackAsync(string workspace, string root)
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
        async Task Reject<T>(Func<Task> action, string message) where T : Exception
        {
            var rejected = false;
            try { await action(); } catch (T) { rejected = true; }
            Check(rejected, message);
        }

        foreach (var unknown in new[] { false, true })
        {
            var scenario = unknown ? "unknown" : "settled";
            var folder = Path.Combine(root, scenario);
            var leaseRoot = Path.Combine(folder, "lease");
            await using var harness = new ManualSupplyHarness(workspace, folder, deferred: true);
            var launch = harness.Launch;
            var definition = new SportmasterFlow();
            harness.RootOwnership = () => ApplicationRootOwnership.AcquireAt(leaseRoot, definition.Id, launch.DataRoot);
            await harness.StartAsync();
            if (unknown) harness.Peer.Mode = "malformed";
            var admission = await harness.App.SubmitParcelBarcodeAsync(await harness.Command("left", "INSTALLER-" + scenario), default);
            var operation = admission.OperationId ?? throw new InvalidOperationException("Installer fixture input was not admitted: " + admission.Detail);
            var expected = unknown ? StageState.Unknown : StageState.Accepted;
            await CameraRoundHarness.Until(async () => (await harness.App.OperationAsync(operation))?.Wcs?.State == expected);
            Check(harness.Peer.Requests.Count == 1, $"actual loopback RCS leaves one original request with result {expected}");
            if (!unknown) await SmRoundFixture.ConfirmRemovedAsync(harness.App, "left");

            await Reject<IOException>(() =>
            {
                using var stopped = new StoppedApplicationOwnership(leaseRoot, definition.Id, launch.DataRoot, default);
                return Task.CompletedTask;
            }, $"the running workstation blocks installation with result {expected}");
            await harness.App.DisposeAsync();

            await using var service = new ParcelStartupMaintenance(launch, definition, () => new ManualSupplyHarness.ManualFactory(launch), () => { })
            { MaintenanceWorkspace = () => Directory.CreateDirectory(Path.Combine(folder, "scratch-" + Wire.Id())).FullName };
            var read = await service.ReadAsync();
            if (read is not { ExistingConfiguration: true, RuntimeDirectory: { } directory, Candidate: { Wcs: { } wcs } candidate })
                throw new InvalidOperationException("Installer fixture could not read its original configuration: " + string.Join(Environment.NewLine, read.Diagnostics));
            var database = Path.Combine(directory, definition.Id + ".db");
            var before = Wire.FileHash(database);
            string[] originalFiles = [database, database + "-wal", database + "-shm", Path.Combine(directory, "configuration.json")];
            var originals = originalFiles.ToDictionary(path => path, FileHash);
            var destination = Path.Combine(folder, "before-rollback.db");
            bool OriginalsPreserved() => originals.All(entry => FileHash(entry.Key) == entry.Value);
            bool NoBackup() => !File.Exists(destination) && !Directory.Exists(destination + ".recovery");

            using (var stopped = new StoppedApplicationOwnership(leaseRoot, definition.Id, launch.DataRoot, default))
            {
                Check(stopped.Directories.SequenceEqual(new[] { directory }), $"installation takes station ownership after shutdown with original result {expected}");
                await Reject<IOException>(() =>
                {
                    using var restart = ApplicationRootOwnership.AcquireAt(leaseRoot, definition.Id, launch.DataRoot);
                    return Task.CompletedTask;
                }, "installation ownership keeps the workstation from starting again");
                var installed = new DeploymentLocation(definition.Id, new string('a', 40), Path.Combine(folder, "old-release"),
                    Path.Combine(folder, "missing-registered-seed"), launch.DataRoot, "synthetic-account");
                var prepared = InstallationConfiguration.Prepare(definition.InitialConfiguration(), installed, Path.Combine(folder, "install-seed"));
                var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(prepared, "business.json")));
                Check(Wire.Encode(seed) == Wire.Encode(candidate) && OriginalsPreserved() && NoBackup() && harness.Peer.Requests.Count == 1,
                    $"installation seed preparation preserves {expected} and original DB/WAL/SHM/configuration without backup or replay");
            }

            const string purpose = "Rollback";
            var plan = await service.PlanChangeAsync(purpose, destination);
            var committed = false;
            Check(plan.Allowed == !unknown && OriginalsPreserved(), $"rollback preflight preserves original files and refuses unresolved responsibility (Unknown={unknown})");
            if (unknown)
            {
                await Reject<InvalidOperationException>(() => service.ExecuteChangeAsync(plan.Identity, purpose, destination, Commit),
                    "a refused rollback plan cannot enter backup or the program-switch callback");
                Check(!committed && NoBackup() && OriginalsPreserved() && harness.Peer.Requests.Count == 1,
                    "blocked rollback preserves original bytes and never sends or commits again");
            }
            else
            {
                var revised = candidate with { Wcs = wcs with { Parameters = new(wcs.Parameters) { ["request-timeout-ms"] = "1501" } } };
                var staged = await service.StageAsync(read.ConfigurationHash, revised, "synthetic change after preflight");
                Check(staged.ConfigurationHash != plan.ConfigurationHash, "the fixture saved a new configuration revision after planning");
                await Reject<InvalidOperationException>(() => service.ExecuteChangeAsync(plan.Identity, purpose, destination, Commit),
                    "configuration changed after planning refuses the program switch");
                Check(!committed && NoBackup() && Wire.FileHash(database) == before, "stale rollback plan creates neither backup nor program-switch commit");
                plan = await service.PlanChangeAsync(purpose, destination);
                await service.ExecuteChangeAsync(plan.Identity, purpose, destination, () =>
                {
                    _ = ParcelRecovery.VerifyBackup(destination, () => { });
                    return Commit();
                });
                Check(committed && Wire.FileHash(database) == before && harness.Peer.Requests.Count == 1,
                    "fresh rollback plan publishes a verified full backup before the program-switch callback while retaining the original database and request");
            }

            Task Commit()
            {
                committed = true;
                return Task.CompletedTask;
            }
        }
        Console.WriteLine($"PASS {checks} focused installation and rollback guards; isolated ownership, SQLite and loopback RCS; no machine installation or real devices");
    }

    static string? FileHash(string path) => File.Exists(path) ? Wire.FileHash(path) : null;
}
