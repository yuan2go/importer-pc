using Ipc.Application.Contracts;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    static void RequireSameIdentity(ParcelConfiguration expected, ParcelConfiguration actual)
    {
        if (actual.ApplicationId != expected.ApplicationId || actual.StationId != expected.StationId || actual.Mode != expected.Mode)
            throw new InvalidDataException("DeploymentIdentityMismatch: original application/station/mode must match requested deployment before activation or device construction");
    }
    void ValidateDeploymentIdentity(ParcelConfiguration seed)
    {
        RequireSameIdentity(seed, configuration!.Current.Effective);
        if (configuration.Current.Pending is { } pending) RequireSameIdentity(seed, pending);
    }
}
