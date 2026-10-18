using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
namespace Ipc.Application.Sportmaster;

public sealed class SportmasterFlow : FlowDefinition
{
    public override string Id => "sportmaster";
    public override bool RequiresWcs => true;
    public override bool RequiresPlc => false;
    public override string[] ValidateBusiness(ParcelConfiguration c) { return c.Weight.Source != WeightSource.Disabled ? ["Sportmaster does not participate in weight or PLC"] : []; }
}
public static class SportmasterApplication
{
    public static ParcelRuntime Create(BusinessLaunch launch, IParcelCapabilityFactory factory) => new(launch, new SportmasterFlow(), factory);
}
