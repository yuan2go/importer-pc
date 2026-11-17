using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Platform;

static partial class SportmasterRcs
{
    public static async Task InstallationBoundaryChecks(string workspace, string root)
    {
        Directory.CreateDirectory(root);
        var original = InstallationResourceBoundary.OtherInstallationForVerification;
        var peer = new DeploymentLocation("sportmaster", new string('a', 40), Path.Combine(root, "peer-program"),
            Directory.CreateDirectory(Path.Combine(root, "peer-config")).FullName, Path.Combine(root, "peer-data"), "S-1-5-32-545");
        var binding = Binding("http://127.0.0.1:48631/rcs/sorter/api");
        var configuration = HeartbeatConfiguration(workspace, binding);
        var reader = configuration.Reader with { Parameters = new() { ["device.ip"] = "192.0.2.10", ["device.serial"] = "FORMAL-SYNTHETIC" } };
        File.WriteAllText(Path.Combine(peer.ConfigurationDirectory, "business.json"), Wire.Encode(configuration with { Reader = reader }));
        try
        {
            InstallationResourceBoundary.OtherInstallationForVerification = () => peer;
            var created = 0;
            await using (var lookup = new CameraIdentityLookup(_ => { created++; throw new Exception("SDK must not be created for a reserved camera"); }))
            {
                try { lookup.Read("192.0.2.10", "mvid-native"); throw new Exception("Reserved camera lookup was allowed"); }
                catch (InvalidDataException) { Check(created == 0, "reserved camera identity lookup refuses before SDK creation or enumeration"); }
            }
            InstallationResourceBoundary.OtherInstallationForVerification = () => null;
            var connections = 0;
            await using (var service = new SportmasterRcsTaskService(binding, (_, _) =>
            { Interlocked.Increment(ref connections); return ValueTask.FromException<Stream>(new IOException("Unexpected synthetic connection")); }))
            {
                service.ConfigureRuntime(configuration);
                var request = Request(binding);
                request = request with { IntegrationRequest = service.PrepareRequest(request, 1) };
                var frozen = Wire.Encode(request);
                // The conflict appears after construction. The send boundary must
                // still observe it before invoking the transport, including data:[].
                InstallationResourceBoundary.OtherInstallationForVerification = () => peer;
                await service.StartAsync(default);
                using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while ((await service.HealthAsync(default)).Ready)
                    await Task.Delay(10, budget.Token);
                Check(connections == 0, "new peer reservation blocks initial heartbeat before transport access");
                Check(await service.SendHeartbeatAsync(default) == RcsHeartbeatResult.Failed && connections == 0,
                    "manual heartbeat also rechecks peer reservation without connecting");
                try { using var permit = await service.ReserveDispatchAsync(request, default); throw new Exception("Reserved destination request was allowed"); }
                catch (InvalidDataException)
                {
                    Check(connections == 0 && service.DispatchProgress.Occupied == 0 && Wire.Encode(request) == frozen,
                        "destination resource admission refuses before transport and preserves the frozen original request");
                }
            }
            var selected = InstallationIdentity.IsTest ? Path.Combine(root, "formal.db") : InstallationIdentity.TestDataArea + @"\backups\peer.db";
            await using var maintenance = new ParcelStartupMaintenance(new(Path.Combine(root, "config"), Path.Combine(root, "data"), null, null),
                new SportmasterFlow(), () => throw new Exception("Forbidden deletion must not open business ports"), () => { });
            try { await maintenance.DeleteBackupSetAsync(selected, new string('b', 64), null, default); throw new Exception("Cross-instance backup deletion was allowed"); }
            catch (InvalidDataException) { Check(!File.Exists(selected + ".deleting.json"), "cross-instance backup deletion creates no deletion intent or receipt"); }
        }
        finally { InstallationResourceBoundary.OtherInstallationForVerification = original; }
    }
}
