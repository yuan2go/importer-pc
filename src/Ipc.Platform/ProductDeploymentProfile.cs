namespace Ipc.Platform;

// Build-time declarations only. Deployment records continue to store the product ID.
public sealed class ProductDeploymentProfile
{
    internal ProductDeploymentProfile(string applicationId) => ApplicationId = applicationId;

    public string ApplicationId { get; }
    public string DisplayName => UsesUnqualifiedDisplayName ? InstallationIdentity.DisplayName : "菜鸟供包台 · " + ApplicationId;
    internal string ShortcutDescription => UsesUnqualifiedDisplayName ? InstallationIdentity.DisplayName : InstallationIdentity.DisplayName + " · " + ApplicationId;
    internal bool UsesUnqualifiedDisplayName { get; init; }
    internal string? ShortcutBusinessName { get; init; }
    public bool SupportsWindowsShortcuts => ShortcutBusinessName is not null;
    public bool UseDualProgramShortcuts { get; init; }
    public bool SupportsAutoStart { get; init; }
    public bool UsesStrictWindowsStorage { get; init; }
    public bool UsesProgramUseLock { get; init; }
    public bool CloseUncommittedOnInstall { get; init; }
    public bool UsesManagementModeBroker { get; init; }
    public bool UsesRecoveryEntry { get; init; }
    public bool SupportsOpaqueRemoval { get; init; }
    public bool RequiresRestoreHandoff { get; init; }
    public bool RequiresBundledCameraDependencies { get; init; }
    public bool RequiresBundledMvidVcRuntime { get; init; }
    public bool SupportsStandaloneOperationsTool { get; init; }
    public bool UsesQuietOperationJournal { get; init; }
    public bool PreservesInstallationConfiguration { get; init; }
    public string DefaultDesktopUi { get; init; } = "classic";
    public bool UsesInlineInstallationReview { get; init; }
    public bool UsesInstallationContinuation { get; init; }
    public bool ShowsAdvancedDeploymentMaintenance { get; init; }
    public bool UsesStationDisplayPreferences { get; init; }
    public bool SupportsFirstConfigurationBarcodeTest { get; init; }
    public string InstallerVersionPrefix { get; init; } = "";
    public string FirstUseGuidance { get; init; } = "首次打开后，请完成设备与服务配置，再按检查结果启动。";
    public string? MissingReleaseRepairVersion { get; init; }
    internal string? SameVersionRollbackVersion { get; init; }

    public bool AllowsSameVersionRollback(string currentVersion, string targetVersion) =>
        SameVersionRollbackVersion is { } allowed && currentVersion == allowed && targetVersion == allowed;
}

public static class ProductDeploymentCatalog
{
    // The paired formal/test installations and legacy user-root provisioning
    // have always used this one registration in each protected anchor.
    internal static ProductDeploymentProfile IsolatedWorkstation { get; } = new("sportmaster")
    {
        ShortcutBusinessName = "SM供包业务",
        UsesUnqualifiedDisplayName = true,
        UseDualProgramShortcuts = true,
        SupportsAutoStart = true,
        UsesStrictWindowsStorage = true,
        UsesProgramUseLock = true,
        CloseUncommittedOnInstall = true,
        UsesManagementModeBroker = true,
        UsesRecoveryEntry = true,
        SupportsOpaqueRemoval = true,
        RequiresRestoreHandoff = true,
        RequiresBundledCameraDependencies = true,
        RequiresBundledMvidVcRuntime = true,
        SupportsStandaloneOperationsTool = true,
        UsesQuietOperationJournal = true,
        PreservesInstallationConfiguration = true,
        DefaultDesktopUi = "supply-station",
        UsesInlineInstallationReview = true,
        UsesInstallationContinuation = true,
        ShowsAdvancedDeploymentMaintenance = true,
        UsesStationDisplayPreferences = true,
        SupportsFirstConfigurationBarcodeTest = true,
        InstallerVersionPrefix = "SM ",
        FirstUseGuidance = "首次打开后先设置供包台。相机和 RCS 可稍后配置，配置完成前不能供包。",
        MissingReleaseRepairVersion = "1.0.0",
        SameVersionRollbackVersion = "1.0.0"
    };

    static readonly ProductDeploymentProfile[] profiles =
    [
        IsolatedWorkstation,
        new("direct-plc") { ShortcutBusinessName = "设备直连业务", UsesManagementModeBroker = true, UsesRecoveryEntry = true },
        new("wcs-to-plc") { ShortcutBusinessName = "调度转发业务", UsesManagementModeBroker = true, UsesRecoveryEntry = true },
        new("supply"),
        new("inspection")
    ];
    static readonly Dictionary<string, ProductDeploymentProfile> byId = profiles.ToDictionary(p => p.ApplicationId, StringComparer.Ordinal);

    public static ProductDeploymentProfile? Find(string? applicationId) =>
        applicationId is null ? null : byId.GetValueOrDefault(applicationId);

    public static ProductDeploymentProfile Require(string applicationId) => Find(applicationId) ??
        throw new NotSupportedException("未声明此产品的部署能力：" + applicationId);

    public static IReadOnlyList<string> RegisteredIds { get; } = SelectIds(_ => true);
    public static IReadOnlyList<string> KnownManagedIds { get; } = SelectIds(p => p.UsesManagementModeBroker);
    internal static IReadOnlyList<string> RecoveryEntryProducts { get; } = SelectIds(p => p.UsesRecoveryEntry);
    internal static IReadOnlyList<string> RestoreHandoffProducts { get; } = SelectIds(p => p.RequiresRestoreHandoff);
    public static IReadOnlyList<string> CameraDependencyProducts { get; } = SelectIds(p => p.RequiresBundledCameraDependencies);
    public static IReadOnlyList<string> OperationsEnabledProducts { get; } = SelectIds(p => p.SupportsStandaloneOperationsTool);

    static IReadOnlyList<string> SelectIds(Func<ProductDeploymentProfile, bool> match) =>
        Array.AsReadOnly(profiles.Where(match).Select(p => p.ApplicationId).ToArray());
}
