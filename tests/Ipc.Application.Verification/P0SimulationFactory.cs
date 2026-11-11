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
    public CapabilityCatalog Catalog() => validation.Catalog();
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
            : r.Driver == "tcp-line" ? new TcpLineObservationSource(r) : new ManualObservationSource(r.DeviceId));
        IWcsPort? wcs = c.Wcs is not { } b ? null : ParcelConnectionSetup.IsDeferred(c, b.InstanceId) ? new DeferredWcsPort(b.InstanceId)
            : b.Implementation == "sportmaster-rcs" ? new SportmasterRcsTaskService(b) : new ReferenceWcs(b, Path.Combine(launch.DataRoot, "synthetic-peer"));
        if (wcs is IWcsRuntimeConfiguration configured) configured.ConfigureRuntime(c);
        // Shared parameter regressions retain the real reference weight/PLC
        // consumers without relying on a developer machine's installed lease root.
        if (c.Scale is { Implementation: not "reference-weight" } || c.Plc is { Implementation: not "reference-plc" })
            throw new InvalidOperationException("Only controlled reference device ports belong in this fixture");
        var weight = c.Scale is { } scale ? new ReferenceWeightSource(scale) : null;
        var plc = c.Plc is { } binding ? new ReferencePlc(binding, Path.Combine(launch.DataRoot, "synthetic-peer")) : null;
        var fingerprints = new[] { c.Wcs, c.Scale, c.Plc }.OfType<CapabilityBinding>().ToDictionary(b => b.InstanceId, _ => "isolated-simulation-fixture");
        return new(readers[c.Reader.DeviceId], weight, wcs, plc, fingerprints, Readers: readers);
    }
    public ValueTask DisposeAsync() => validation.DisposeAsync();
}
