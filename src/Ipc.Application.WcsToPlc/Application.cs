using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
namespace Ipc.Application.WcsToPlc;

public sealed class WcsToPlcFlow : FlowDefinition
{
    public override string Id => "wcs-to-plc";
    public override bool RequiresWcs => true;
    public override bool RequiresPlc => true;
    public override string[] ValidateBusiness(ParcelConfiguration c) { return []; }
}
public static class WcsToPlcApplication
{
    public static ParcelRuntime Create(BusinessLaunch launch, IParcelCapabilityFactory factory) => new(launch, new WcsToPlcFlow(), factory);
}
