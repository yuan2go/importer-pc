using Ipc.Application.Contracts;
using Ipc.Adapters;
using Ipc.Application.ParcelFlow;
using Ipc.Assembly;
using Ipc.Drivers;
using Ipc.Integrations;
using Ipc.Integrations.SportmasterRcs;
using Ipc.Platform;

// Test-only composition: never provision or bypass the production machine policy.
// Actual application/SQLite/TCP/RCS code runs only against fresh Simulation roots.
sealed class P0SimulationFactory(BusinessLaunch launch) : IParcelCapabilityFactory
{
    readonly ParcelCapabilityFactory validation = new(launch);
    public ParcelConfiguration Normalize(ParcelConfiguration c) => validation.Normalize(c);
    public void ValidateStructure(ParcelConfiguration c) => validation.ValidateStructure(c);
    public void Validate(ParcelConfiguration c)
    {
        if (c.Mode != RunMode.Simulation) throw new InvalidOperationException("Simulation fixture only");
        validation.ValidateStructure(c);
    }
    public CapabilityRequirements Requirements(CapabilityBinding b) => validation.Requirements(b);
    public ParcelPorts Create(ParcelConfiguration c)
    {
        Validate(c);
        var readers = ParcelBindings.Readers(c).ToDictionary(r => r.DeviceId, r => ParcelConnectionSetup.IsDeferred(c, r.DeviceId)
            ? (IControlledObservationSource)new DeferredObservationSource(r.DeviceId)
            : r.Driver == "tcp-line" ? new TcpLineObservationSource(r, key => new ResourceLease(Path.Combine(launch.DataRoot, "fixture-camera-leases"), [key])) : new ManualObservationSource(r.DeviceId));
        IWcsPort? wcs = c.Wcs is not { } b ? null : ParcelConnectionSetup.IsDeferred(c, b.InstanceId) ? new DeferredWcsPort(b.InstanceId)
            : b.Implementation == "sportmaster-rcs" ? new SportmasterRcsTaskService(b) : new ReferenceWcs(b, Path.Combine(launch.DataRoot, "synthetic-peer"));
        if (wcs is IWcsRuntimeConfiguration configured) configured.ConfigureRuntime(c);
        return new(readers[c.Reader.DeviceId], null, wcs, null,
            wcs is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [wcs.InstanceId] = "isolated-simulation-fixture" }, Readers: readers);
    }
    public ValueTask DisposeAsync() => validation.DisposeAsync();
}
