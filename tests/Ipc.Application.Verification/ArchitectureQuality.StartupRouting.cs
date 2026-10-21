using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Assembly;
using Ipc.Desktop;
using Ipc.Platform;

static partial class ArchitectureQuality
{
    static async Task StartupRoutingChecks()
    {
        var c = Config("sportmaster");
        const string name = "startup-existing-pending-fallback";
        var path = Path.Combine(root, name); Directory.CreateDirectory(path);
        ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), c);
        var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null);
        var offline = new ParcelStartupMaintenance(launch, new Ipc.Application.Sportmaster.SportmasterFlow(), () => new ParcelCapabilityFactory(launch));
        var first = await offline.ClassifyAsync();
        Check(DesktopLaunchOptions.StartupRoute(first) == DesktopStartupRoute.FirstConfiguration && !Directory.Exists(launch.DataRoot),
            "a readable seed without an existing authority opens first configuration without creating runtime data");
        var missingLaunch = launch with { ConfigurationDirectory = Path.Combine(root, "startup-no-seed"), DataRoot = Path.Combine(root, "startup-no-data") };
        var missing = await new ParcelStartupMaintenance(missingLaunch, new Ipc.Application.Sportmaster.SportmasterFlow(), () => new ParcelCapabilityFactory(missingLaunch)).ClassifyAsync();
        Check(DesktopLaunchOptions.StartupRoute(missing) == DesktopStartupRoute.FirstConfiguration && missing.Readable && !Directory.Exists(missingLaunch.DataRoot),
            "missing parameters still open the existing first-configuration route with system defaults and no runtime resources");
        ParcelOperation original;
        var rejectedTimeout = c.TimeoutMs + 51;
        await using (var app = await Open(c, name))
        {
            app.EmitReferenceAt("left", "startup-original", "000001", "startup-source");
            original = (await Wait(app, s => s.Operations.FirstOrDefault()?.State == ParcelState.SlotResolved)).Operations.Single();
            await Maintenance(app);
            var s = await app.SnapshotAsync();
            Check((await app.StageConfigurationAsync(s.Configuration.Revision, s.Configuration.Effective with { TimeoutMs = rejectedTimeout }, "Synthetic dependency removed after save")).Status == "PendingRestart",
                "the original runtime stages a complete candidate before its simulated exact dependency becomes unavailable");
        }
        var unavailable = new ParcelStartupMaintenance(launch, new Ipc.Application.Sportmaster.SportmasterFlow(), () => new UnavailablePendingFactory(new(launch), rejectedTimeout));
        var document = await unavailable.ReadAsync();
        var report = await unavailable.CheckAsync(document.Candidate!);
        Check(document.ExistingConfiguration && document.Candidate?.TimeoutMs == rejectedTimeout && report.Checks.Any(x => x.Status == "FAIL") && DesktopLaunchOptions.StartupRoute(document) == DesktopStartupRoute.Runtime,
            "a statically unusable Pending remains an existing-runtime launch, never a new-station wizard");
        UnavailablePendingFactory? owner = null;
        await using (var app = await Open(c, name, f => owner = new(f, rejectedTimeout)))
        {
            var s = await app.SnapshotAsync();
            Check(s.Configuration.Effective.TimeoutMs == c.TimeoutMs && s.Configuration.Pending?.TimeoutMs == rejectedTimeout && s.Configuration.Status == "ActivationFailed" && s.Configuration.Failure!.Contains("Synthetic exact dependency unavailable"),
                "controlled startup uses the confirmed Effective and retains failed Pending plus activation evidence");
            Check(owner!.CreatedTimeouts.SequenceEqual(new[] { c.TimeoutMs }) && Wire.Encode(s.Operations.Single().Wcs) == Wire.Encode(original.Wcs),
                "fallback constructs only Effective resources and preserves the original request, reply and attempt count without resend");
        }
        var authority = Path.Combine(launch.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId, "configuration.json");
        File.WriteAllText(authority, "{synthetic malformed authority"); var before = File.ReadAllBytes(authority);
        var corrupt = await offline.ReadAsync();
        Check(DesktopLaunchOptions.StartupRoute(corrupt) == DesktopStartupRoute.Repair && File.ReadAllBytes(authority).SequenceEqual(before),
            "an unreadable existing authority opens ordinary repair and preserves its bytes instead of reclassifying it as first configuration");
    }

    sealed class UnavailablePendingFactory(ParcelCapabilityFactory inner, int rejectedTimeout) : IParcelCapabilityFactory
    {
        public List<int> CreatedTimeouts { get; } = [];
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        void RequireAvailable(ParcelConfiguration c) { if (c.TimeoutMs == rejectedTimeout) throw new InvalidDataException("Synthetic exact dependency unavailable"); }
        public void Validate(ParcelConfiguration c) { RequireAvailable(c); inner.Validate(c); }
        public void ValidateStructure(ParcelConfiguration c) { RequireAvailable(c); inner.ValidateStructure(c); }
        public CapabilityCatalog Catalog() => inner.Catalog();
        public CapabilityRequirements Requirements(CapabilityBinding b) => inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c) { CreatedTimeouts.Add(c.TimeoutMs); return inner.Create(c); }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
