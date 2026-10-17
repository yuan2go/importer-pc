using Ipc.Platform;
namespace Ipc.Supply;

public sealed record ExtensionManagementSnapshot(string State, string[] Diagnostics,
    ExtensionInstallation[] Packages, ExtensionInstallOperation[] Operations, string? EffectiveDeployment, string? EffectiveFingerprint);
public interface IExtensionManagement
{
    Task<ExtensionManagementSnapshot> ExtensionSnapshotAsync();
    Task<VerifiedExtension> PlanExtensionAsync(string file);
    Task<ExtensionManifest> ExtensionManifestAsync(PackageReference package);
    Task<ExtensionInstallation> InstallExtensionAsync(string file, string expectedHash);
    Task<ExtensionInstallation> RecoverExtensionInstallAsync(string originalOperationId);
    Task UninstallExtensionAsync(PackageReference package);
}
// The business-owned presentation facade. The WPF pages do not receive a store,
// product service provider or concrete runtime implementation.
public interface IStationRuntime : IExtensionManagement
{
    Guid RuntimeId { get; }
    RunMode? Mode { get; }
    StationConfig? Configuration { get; }
    PresentationSettings Presentation { get; }
    string[] AllowedViews { get; }
    string[] AllowedLayouts { get; }
    string[] InstalledDrivers { get; }
    IReadOnlyList<string> RuntimeDiagnostics { get; }
    IntegrationDiagnostic[] IntegrationDiagnostics { get; }
    string BaseDataRoot { get; }
    IStationApplication? Application { get; }
    void ReportPresentationDiagnostic(string reason);
    IReadOnlyList<string> ValidateConfiguration(StationConfig draft);
    Task<CommandReceipt> CommandAsync(Command command);
    Task SetWindowAreaAsync(string windowId, bool insufficient);
    Task SavePresentationAsync(PresentationSettings settings);
    Task EmitAsync(string lane, string kind, string[]? codes = null, int? quantity = null, string? sourceEventId = null);
    Task ReplayAsync(string lane);
    Task RestartSourceAsync(string deviceId);
    Task ReconcileAsync(string requestId);
    Task ExportAsync(string destination, string? owner = null);
}
