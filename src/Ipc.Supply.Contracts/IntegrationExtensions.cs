using Ipc.Platform;
namespace Ipc.Supply;

// Business-owned wire boundary. This assembly contains no store, runtime, UI or DI container.
public sealed record IntegrationImplementation(string Driver, PackageReference? Package, string ModuleId,
    string InstanceId, string DeploymentVersion, string ConfigurationSha256, string ContractSha256,
    IntegrationBinding Target, int TimeoutMs, string Policy, int FixtureDemand, Dictionary<string, string> Parameters);
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
