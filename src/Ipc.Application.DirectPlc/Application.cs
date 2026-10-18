using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
namespace Ipc.Application.DirectPlc;

public sealed class DirectPlcFlow : FlowDefinition
{
    public override string Id => "direct-plc";
    public override bool RequiresWcs => false;
    public override bool RequiresPlc => true;
    public override string[] ValidateBusiness(ParcelConfiguration c) { return []; }
}
public static class DirectPlcApplication
{
    public static ParcelRuntime Create(BusinessLaunch launch, IParcelCapabilityFactory factory) => new(launch, new DirectPlcFlow(), factory);
}
