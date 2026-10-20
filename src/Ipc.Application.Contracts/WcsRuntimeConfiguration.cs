namespace Ipc.Application.Contracts;

// Optional companion port. Assembly supplies the effective revision before Start;
// integrations never read the configuration store or own a second configuration.
public interface IWcsRuntimeConfiguration
{
    void ConfigureRuntime(ParcelConfiguration configuration);
}
