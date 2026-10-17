using Ipc.Platform;
namespace Ipc.Supply;

// Business-owned wire boundary. This assembly contains no store, runtime, UI or DI container.
public sealed record IntegrationImplementation(string Driver, PackageReference? Package, string ModuleId,
    string InstanceId, string DeploymentVersion, string ConfigurationSha256, string ContractSha256,
    IntegrationBinding Target, int TimeoutMs, string Policy, int FixtureDemand, Dictionary<string, string> Parameters)
{
    public bool Equals(IntegrationImplementation? other) => other is not null && Driver == other.Driver && Package == other.Package && ModuleId == other.ModuleId &&
        InstanceId == other.InstanceId && DeploymentVersion == other.DeploymentVersion && ConfigurationSha256 == other.ConfigurationSha256 &&
        ContractSha256 == other.ContractSha256 && Target == other.Target && TimeoutMs == other.TimeoutMs && Policy == other.Policy && FixtureDemand == other.FixtureDemand &&
        Parameters.OrderBy(x => x.Key, StringComparer.Ordinal).SequenceEqual(other.Parameters.OrderBy(x => x.Key, StringComparer.Ordinal));
    public override int GetHashCode() => HashCode.Combine(ConfigurationSha256, Driver, Package, ModuleId, InstanceId, Target, ContractSha256);
}
public sealed record IntegrationExtensionContext(string InstanceId, RunMode Mode, IntegrationBinding Target,
    int TimeoutMs, string Policy, int FixtureDemand, IReadOnlyDictionary<string, string> Parameters);
public sealed record IntegrationDiagnostic(string InstanceId, string State, int InFlight, string? LastFailure);
public interface IIntegrationExtension
{
    IControlledIntegration Create(IntegrationExtensionContext context);
}
public interface IControlledIntegration : IRequestContract, IIntegrationPort
{
    string InstanceId { get; }
    string Contract { get; }
    IntegrationDiagnostic Diagnostic { get; }
    Task StopAsync(CancellationToken token);
}
