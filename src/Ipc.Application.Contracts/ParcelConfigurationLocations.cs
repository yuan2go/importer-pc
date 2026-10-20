namespace Ipc.Application.Contracts;

// Read-only local deployment context; not a second configuration or migration API.
public sealed record ParcelConfigurationLocations(string ConfigurationDirectory, string DataDirectory);
public interface IParcelConfigurationLocationApplication
{
    ParcelConfigurationLocations ConfigurationLocations { get; }
}
