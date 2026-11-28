using System.IO;
using Ipc.Platform;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;

namespace Ipc.Desktop;

internal enum DesktopStartupRoute { Runtime, FirstConfiguration, Repair }

// Desktop-only selection: never persisted into business configuration or request identity.
public sealed record DesktopLaunchOptions(BusinessLaunch Business, string Ui)
{
    public string? UiOverride { get; init; }

    public string[] PresentationArguments()
    {
        List<string> arguments = [];
        if (UiOverride is not null) arguments.AddRange(["--ui", UiOverride]);
        if (Business.View is not null) arguments.AddRange(["--view", Business.View]);
        if (Business.Layout is not null) arguments.AddRange(["--layout", Business.Layout]);
        return [.. arguments];
    }

    public string[] OrdinaryArguments() =>
        ["--config", Path.GetFullPath(Business.ConfigurationDirectory), "--data-root", Path.GetFullPath(Business.DataRoot), .. PresentationArguments()];

    // Pending validity belongs to the runtime's controlled activation/fallback.
    // A readable existing authority must never be mistaken for a new station.
    internal static DesktopStartupRoute StartupRoute(StartupMaintenanceDocument document) =>
        document.ExistingConfiguration ? DesktopStartupRoute.Runtime :
        document.Candidate is null ? DesktopStartupRoute.Repair : DesktopStartupRoute.FirstConfiguration;
    internal static DesktopStartupRoute StartupRoute(StartupClassification document) =>
        document.ExistingConfiguration ? DesktopStartupRoute.Runtime : document.Readable ? DesktopStartupRoute.FirstConfiguration : DesktopStartupRoute.Repair;

    public static DesktopLaunchOptions Parse(string[] args, string applicationId, bool recoveryChild = false)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            var key = args[i];
            if (key is not ("--config" or "--data-root" or "--view" or "--layout" or "--ui"))
                throw new ArgumentException("启动参数无效：" + key);
            if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("启动参数缺值：" + key);
            if (!options.TryAdd(key, args[i + 1])) throw new ArgumentException("启动参数重复：" + key);
        }
        var uiOverride = options.GetValueOrDefault("--ui");
        var ui = BusinessDesktopCatalog.ResolveWorkstationPage(applicationId, uiOverride, null, DefaultUi(applicationId));
        // Selecting a maintenance root is not permission to start. Keep the
        // authoritative root available when an original lifecycle is unfinished;
        // the normal startup and runtime still require InstalledReady.
        var installed = DeploymentMedia.ReadInstalled(AppContext.BaseDirectory, applicationId, requireReady: false);
        return new(new(
            options.GetValueOrDefault("--config", installed?.ConfigurationDirectory ?? Path.Combine(AppContext.BaseDirectory, "config")),
            options.GetValueOrDefault("--data-root", installed?.DataRoot ?? InstallationIdentity.DefaultLaunchDataRoot),
            options.GetValueOrDefault("--view"), options.GetValueOrDefault("--layout"))
        { RequireRecoveryReview = recoveryChild }, ui) { UiOverride = uiOverride };
    }

    public static string DefaultUi(string applicationId)
    {
#if SPORTMASTER
        if (BusinessDesktopCatalog.For(applicationId).SupportedWorkstationPages.Count > 1)
            return Ipc.Application.Sportmaster.Desktop.Presentation.BuildDefaultWorkstationPage;
#endif
        return BusinessDesktopCatalog.For(applicationId).DefaultWorkstationPage;
    }
}
