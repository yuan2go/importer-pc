using Ipc.Platform;
using Ipc.Supply;

namespace Ipc.Product;

public sealed partial class ProductRuntime
{
    ExtensionStore? extensionStore;
    ExtensionLoading? extensionLoading;
    PackageUseLease? extensionLease;
    readonly List<string> extensionDiagnostics = [];
    public IntegrationDiagnostic[] IntegrationDiagnostics => integrations?.Diagnostics ?? [];
    public string? ExtensionRoot => extensionStore?.Root;
    public string BaseDataRoot => Path.GetFullPath(options.DataRoot);
    public string? ExtensionTrustFile { get; private set; }
    public Task SetWindowAreaAsync(string windowId, bool insufficient) => application?.SetWindowAreaAsync(windowId, insufficient) ?? Task.CompletedTask;
    void InitializeExtensionManagement(RunMode mode)
    {
        try
        {
            var paths = MachineRuntimePolicy.ExtensionPaths(mode, options.ExtensionRoot, options.ExtensionTrustFile);
            ExtensionTrustFile = paths.TrustFile;
            if (!File.Exists(paths.TrustFile)) { extensionDiagnostics.Add("Independent offline extension trust is not provisioned; core recovery remains available"); return; }
            if (new FileInfo(paths.TrustFile).Length > ExtensionPackages.MaximumManifestBytes) throw new InvalidDataException("Trust policy quota exceeded");
            var trust = Wire.Decode<ExtensionTrust>(File.ReadAllText(paths.TrustFile));
            if (mode == RunMode.Production && trust.Publishers.Any(x => x.TestOnly)) throw new InvalidDataException("Production trust contains a test publisher");
            extensionStore = new(paths.Root, trust, mode, MachineRuntimePolicy.ResolveLeaseRoot(options.LeaseRoot));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { extensionDiagnostics.Add(ex.GetType().Name + ": " + ex.Message); }
    }
    static string[] ExtensionDriverIds(StationConfig config) => config.Extensions?.Instances.Where(x => x.Enabled).Select(x => "extension:" + x.Id).ToArray() ?? [];
    IDisposable? AcquireDeploymentValidationLease(StationConfig config)
    {
        if (config.Extensions?.Packages is not { Length: > 0 } packages) return null;
        if (packages.Any(x => x is null)) return null; // Declarative validation records malformed draft; do not dereference it here.
        try
        {
            var manager = extensionStore ?? throw new InvalidDataException("Extension store unavailable");
            manager.RegisterConsumer(store!.Path);
            return manager.AcquireUse(packages);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        { throw new GuardException("Deployment package is unavailable or under maintenance; re-read before saving/activating: " + ex.Message); }
    }
    public Task<ExtensionManagementSnapshot> ExtensionSnapshotAsync() => Task.Run(() =>
    {
        try
        {
            return new ExtensionManagementSnapshot(extensionStore is null ? "Unprovisioned" : "Available", extensionDiagnostics.ToArray(),
                extensionStore?.Inventory() ?? [], extensionStore?.Operations() ?? [], Configuration?.Version,
                Configuration is null ? null : Wire.Hash(Wire.Encode(Configuration)));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { return new ExtensionManagementSnapshot("Failed", [.. extensionDiagnostics, ex.GetType().Name + ": " + ex.Message], [], [], Configuration?.Version, null); }
    });
    public Task<VerifiedExtension> PlanExtensionAsync(string file) => Task.Run(() =>
        (extensionStore ?? throw new InvalidOperationException("Extension trust/store unavailable")).Plan(file));
    public Task<ExtensionManifest> ExtensionManifestAsync(PackageReference package) => Task.Run(() =>
        (extensionStore ?? throw new InvalidOperationException("Extension trust/store unavailable")).VerifyInstalled(package).Manifest);
    public async Task<ExtensionInstallation> InstallExtensionAsync(string file, string expectedHash)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        return await Task.Run(() => (extensionStore ?? throw new InvalidOperationException("Extension store unavailable")).Install(file, expectedHash)).ConfigureAwait(false);
    }
    public async Task<ExtensionInstallation> RecoverExtensionInstallAsync(string originalOperationId)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var store = extensionStore ?? throw new InvalidOperationException("Extension store unavailable");
        var operation = store.Operations().Single(x => x.Id == originalOperationId);
        if (operation.Kind == "Uninstall") _ = await (application ?? throw new GuardException("Uninstall recovery needs station inspection")).RequiredExtensionPackagesAsync().ConfigureAwait(false);
        return await Task.Run(() => store.Recover(originalOperationId, refreshReferences: operation.Kind == "Uninstall"
            ? () => store.InspectConsumers(SupplyStore.InspectRequiredExtensionPackages).Concat(application!.RequiredExtensionPackagesAsync().GetAwaiter().GetResult()).Distinct().ToArray() : null)).ConfigureAwait(false);
    }
    public async Task UninstallExtensionAsync(PackageReference package)
    {
        if (application is null) throw new GuardException("Offline uninstall requires exclusive station inspection; use the maintenance helper");
        _ = await application.RequiredExtensionPackagesAsync().ConfigureAwait(false);
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        await Task.Run(() => (extensionStore ?? throw new InvalidOperationException("Extension store unavailable")).Uninstall(package,
            () => extensionStore!.InspectConsumers(SupplyStore.InspectRequiredExtensionPackages).Concat(application.RequiredExtensionPackagesAsync().GetAwaiter().GetResult()).Distinct().ToArray())).ConfigureAwait(false);
    }
}
