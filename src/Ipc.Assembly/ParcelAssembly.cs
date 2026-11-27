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

    public static ProductCapabilityPolicy CreateCapabilityPolicy() => CreateCapabilityPolicy(Definition().Id);

    public static ProductCapabilityPolicy CreateCapabilityPolicy(string applicationId)
    {
        if (!string.Equals(applicationId, Definition().Id, StringComparison.Ordinal))
            throw new ArgumentException("Capability policy belongs to another Application", nameof(applicationId));
#if SPORTMASTER
        return new(applicationId, builtInOnly: true, supportsExternalExtensions: false, supportsLiveReload: true,
            allowedSimulationReaderDrivers: ["manual", "tcp-line"], excludedReferenceCatalogProviders: ["tcp-line"])
        {
#if CAPABILITY_VERIFICATION
            AllowsMixedVerificationProducts = true
#endif
        };
#else
        return new(applicationId, builtInOnly: false, supportsExternalExtensions: true, supportsLiveReload: false);
#endif
    }

    public static BuiltInCapabilityRegistry CreateCapabilityRegistry()
    {
#if DIRECTPLC
        var providers = BuiltInReferenceProviders.Create(null).ToList();
#else
        var providers = BuiltInReferenceProviders.Create(b => new Ipc.Integrations.ReferenceWcs(b, BuiltInReferenceProviders.PeerDirectory(b))).ToList();
#endif
#if INCLUDE_HIKROBOT_MVS
        providers.Add(new BuiltInHikrobotReaderProvider());
#endif
#if INCLUDE_SPORTMASTER_RCS
        providers.Add(new BuiltInSportmasterRcsProvider());
#endif
#if INCLUDE_HIKROBOT_MVS
        return new(providers);
#else
        return new(providers, HikrobotReaderBindings.Validate);
#endif
    }

#if !CAPABILITY_VERIFICATION
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
        runtime.ManagementPasswordSetupApproval = password => ParcelStartupServices.ManagementMode(launch).ConfigureManagementPasswordAsync(password);
        if (ProductDeploymentCatalog.Require(runtime.Id).UsesStrictWindowsStorage)
        {
            var deployment = DeploymentMedia.ReadInstalled(AppContext.BaseDirectory, runtime.Id);
            runtime.AcquireRootOwnership = () => ApplicationRootOwnership.AcquireRuntime(runtime.Id, launch.DataRoot, () =>
            {
                if (deployment is not null) DeploymentMedia.RequireRuntimeLocation(deployment,
                    DeploymentMedia.ReadInstalled(AppContext.BaseDirectory, runtime.Id), launch.DataRoot, launch.ConfigurationDirectory);
            });
        }
        runtime.ManagementPasswordResetApproval = password => ParcelStartupServices.ManagementMode(launch).ResetManagementPasswordAsync(password);
        runtime.ManagementPasswordRecoveryInspection = () => ParcelStartupServices.ManagementMode(launch).InspectManagementPasswordRecoveryAsync();
        ApplicationReleaseValidation.ValidateIfPublished(AppContext.BaseDirectory, runtime.Id, runtime.Presentation, businessAssembly);
        return runtime;
    }
#endif
}
#if !CAPABILITY_VERIFICATION
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
#endif
