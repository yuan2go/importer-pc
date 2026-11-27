using Ipc.Presentation.Wpf;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed record BusinessDesktopProfile(string ApplicationId, string BusinessDisplayName)
{
    public string DefaultDesktopUi { get; init; } = "classic";
    public string InstallerVersionPrefix { get; init; } = "";
    public string FirstUseGuidance { get; init; } = "首次打开后，请完成设备与服务配置，再按检查结果启动。";
    public bool UsesStationDisplayPreferences { get; init; }
    public bool SupportsFirstConfigurationBarcodeTest { get; init; }

    public string DefaultThemeId { get; init; } = WorkstationThemeManager.IndustrialLightId;
    public IReadOnlyList<UiThemeDescriptor> SupportedThemes { get; init; } = WorkstationThemeManager.BuiltInThemes;

    public bool UsesWorkstationSettingsFlow { get; init; }
    public bool UsesFirstReadinessStep { get; init; }
    public bool SupportsBackupSetsDialog { get; init; }
    public bool UsesFullScreenOpeningPreference { get; init; }
    public bool UsesScanAndBarcodePolicy { get; init; }
    public bool UsesLegacyWeightAndQueueFields { get; init; } = true;
    public bool HidesUnboundSecondaryCapabilities { get; init; }
    public bool UsesCompactHistoryLayout { get; init; }
    public bool UsesSmCapacityProjection { get; init; }
    public bool UsesSmUnknownFaultGuidance { get; init; }
    public bool ShowsCompactDiskReserveHint { get; init; }
    public bool UsesStationCameraConfiguration { get; init; }
    public bool UsesRcsOnlyConfiguration { get; init; }
    public bool SupportsLiveConfiguration { get; init; }
    public bool UsesClassicViewportGuard { get; init; } = true;
}

public static class BusinessDesktopCatalog
{
    internal static BusinessDesktopProfile Station { get; } = new("sportmaster", "SM 供包业务")
    {
        DefaultDesktopUi = "supply-station",
        InstallerVersionPrefix = "SM ",
        FirstUseGuidance = "首次打开后先设置供包台。相机和 RCS 可稍后配置，配置完成前不能供包。",
        UsesStationDisplayPreferences = true,
        SupportsFirstConfigurationBarcodeTest = true,
        UsesWorkstationSettingsFlow = true,
        UsesFirstReadinessStep = true,
        SupportsBackupSetsDialog = true,
        UsesFullScreenOpeningPreference = true,
        UsesScanAndBarcodePolicy = true,
        UsesLegacyWeightAndQueueFields = false,
        HidesUnboundSecondaryCapabilities = true,
        UsesCompactHistoryLayout = true,
        UsesSmCapacityProjection = true,
        UsesSmUnknownFaultGuidance = true,
        ShowsCompactDiskReserveHint = true,
        UsesStationCameraConfiguration = true,
        UsesRcsOnlyConfiguration = true,
        SupportsLiveConfiguration = true,
        UsesClassicViewportGuard = false
    };

    static readonly BusinessDesktopProfile fallback = new("", "当前业务");
    static readonly IReadOnlyDictionary<string, BusinessDesktopProfile> profiles = new[]
    {
        Station,
        new("direct-plc", "设备直连业务"),
        new("wcs-to-plc", "调度转发业务")
    }.ToDictionary(profile => profile.ApplicationId, StringComparer.Ordinal);

    public static BusinessDesktopProfile For(string? applicationId) =>
        applicationId is not null && profiles.TryGetValue(applicationId, out var profile) ? profile : fallback;
}
