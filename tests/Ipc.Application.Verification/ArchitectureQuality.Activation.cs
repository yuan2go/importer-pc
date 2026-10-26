using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
using System.Diagnostics;

static partial class ArchitectureQuality
{
    static async Task ActivationCrashChecks()
    {
        foreach (var point in new[] { "BeforeCreate", "AfterCreate", "AfterPortStart" })
        {
            var name = "activation-crash-" + point; var c = Config("sportmaster"); long priorRevision;
            await using (var app = await Open(c, name))
            {
                await Maintenance(app); var s = await Wait(app, x => x.Availability?.Configure == true); priorRevision = s.Configuration.Revision;
                var saved = await StageAfterCallsDrain(app, priorRevision, s.Configuration.Effective with { TimeoutMs = c.TimeoutMs + 17 }, "Synthetic interrupted candidate " + point);
                Check(saved.Status == "PendingRestart", "activation crash probe saves complete audited candidate " + point + ": " + saved.Status + "/" + saved.Reason);
            }
            var path = Path.Combine(root, name);
            var psi = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add(typeof(ArchitectureQuality).Assembly.Location); psi.ArgumentList.Add(workspace);
            psi.ArgumentList.Add("--configuration-crash-child"); psi.ArgumentList.Add(path); psi.ArgumentList.Add(point);
            using var child = Process.Start(psi)!;
            var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); _ = await stdout; _ = await stderr;
            var file = Path.Combine(DirectoryFor(name, c), "configuration.json"); var interrupted = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(file));
            Check(child.ExitCode == 73 && interrupted.Effective.TimeoutMs == c.TimeoutMs && interrupted.Pending?.TimeoutMs == c.TimeoutMs + 17 && interrupted.Status == "ActivationInProgress" && interrupted.Activation is { FinishedAt: null }, "process exit before resource confirmation retains prior Effective and original Pending " + point);
            await using var reopened = await Open(c, name); var recovered = (await reopened.SnapshotAsync()).Configuration;
            Check(recovered.Effective.TimeoutMs == c.TimeoutMs && recovered.Pending?.TimeoutMs == c.TimeoutMs + 17 && recovered.Status == "ActivationFailed" && recovered.Failure!.Contains("interrupted", StringComparison.OrdinalIgnoreCase) && recovered.Activation?.Id == interrupted.Activation?.Id && recovered.Activation?.Outcome == "Interrupted", "restart retains interrupted attempt without automatically retrying candidate " + point);
            await Maintenance(reopened);
            var restaged = await StageAfterCallsDrain(reopened, recovered.Revision, recovered.Pending!, "Explicitly revalidated interrupted candidate " + point);
            Check(restaged.Status == "PendingRestart", "maintenance can explicitly restage interrupted candidate with current CAS " + point + ": " + restaged.Status + "/" + restaged.Reason);
            await reopened.DisposeAsync();
            await using var confirmed = await Open(c, name); var final = (await confirmed.SnapshotAsync()).Configuration;
            Check(final.Status == "Effective" && final.Pending is null && final.Effective.TimeoutMs == c.TimeoutMs + 17 && final.Activation is { Outcome: "Activated", FinishedAt: not null } && final.Activation.Id != recovered.Activation?.Id, "explicit restaging creates a new confirmed attempt only after real resource startup " + point);
        }
    }
    static async Task<BusinessReceipt> StageAfterCallsDrain(ParcelRuntime app, long expectedRevision, ParcelConfiguration candidate, string reason)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var before = (await Wait(app, x => x.Availability?.Configure == true)).Configuration;
            var receipt = await app.StageConfigurationAsync(expectedRevision, candidate, reason);
            if (receipt.Status != "Rejected" || receipt.Reason != "Pause and authenticate maintenance before configuration") return receipt;
            // A periodic health read may start after the snapshot. Only retry this
            // pre-write fixture guard, never a submitted candidate or business send.
            var after = (await app.SnapshotAsync()).Configuration;
            if (Wire.Encode(before) != Wire.Encode(after)) throw new InvalidDataException("Pre-write guard changed configuration");
            await Task.Delay(20);
        }
        throw new TimeoutException("Configuration fixture never reached a drained interval");
    }
    public static async Task ActivationCrashChild(string path, string point)
    {
        var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null);
        var app = new ParcelRuntime(launch, new SportmasterFlow(), new CrashFactory(new ParcelCapabilityFactory(launch), point));
        await app.StartAsync(default); throw new Exception("Crash point did not run");
    }
    sealed class CrashFactory(ParcelCapabilityFactory inner, string point) : IParcelCapabilityFactory
    {
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public CapabilityRequirements Requirements(CapabilityBinding b) => inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c)
        {
            if (point == "BeforeCreate") Environment.Exit(73);
            var ports = inner.Create(c); if (point == "AfterCreate") Environment.Exit(73);
            return ports with { Wcs = new CrashWcs(ports.Wcs!) };
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class CrashWcs(IWcsPort inner) : IWcsPort
    {
        public string InstanceId => inner.InstanceId;
        public async Task StartAsync(CancellationToken t) { await inner.StartAsync(t); Environment.Exit(73); }
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public Task<StageReply> ResolveSlotAsync(FrozenRequest r, CancellationToken t) => inner.ResolveSlotAsync(r,t);
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => inner.QueryAsync(r,t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
