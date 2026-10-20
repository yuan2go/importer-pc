using Ipc.Application.Contracts;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IParcelConfigurationLocationApplication
{
    public ParcelConfigurationLocations ConfigurationLocations => new(Path.GetFullPath(launch.ConfigurationDirectory),
        Path.GetFullPath(directory ?? launch.DataRoot));
}
