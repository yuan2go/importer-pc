using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Assembly;

public static class ParcelAssembly
{
    public static Ipc.Application.ParcelFlow.FlowDefinition Definition() =>
#if SPORTMASTER
        new Ipc.Application.Sportmaster.SportmasterFlow();
#elif DIRECTPLC
        new Ipc.Application.DirectPlc.DirectPlcFlow();
#else
        new Ipc.Application.WcsToPlc.WcsToPlcFlow();
#endif
    public static IBusinessRuntime Create(BusinessLaunch launch)
    {
        ParcelRuntimeSetup.Prepare();
        MachineRuntimePolicy.RequireRuntimeIdentity();
        ParcelCapabilityFactory.RegisterContracts();
        var businessAssembly = Definition().GetType().Assembly;
        var compatibility = BusinessCompatibility.Read(businessAssembly);
        var factory = new ParcelCapabilityFactory(launch, compatibility, businessAssembly);
#if SPORTMASTER
        var runtime = Ipc.Application.Sportmaster.SportmasterApplication.Create(launch, factory);
#elif DIRECTPLC
        var runtime = Ipc.Application.DirectPlc.DirectPlcApplication.Create(launch, factory);
#elif WCSTOPLC
        var runtime = Ipc.Application.WcsToPlc.WcsToPlcApplication.Create(launch, factory);
#else
#error A registered Application must be selected at build time.
#endif
        ApplicationReleaseValidation.ValidateIfPublished(AppContext.BaseDirectory, runtime.Id, runtime.Presentation, businessAssembly);
        return runtime;
    }
}
static class InstalledBusiness
{
    public static BusinessAssembly Create() => new(
#if SPORTMASTER
        "sportmaster",
#elif DIRECTPLC
        "direct-plc",
#else
        "wcs-to-plc",
#endif
        ParcelAssembly.Create);
}
