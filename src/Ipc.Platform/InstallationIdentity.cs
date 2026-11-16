namespace Ipc.Platform;

// A build-time identity keeps installation authority independent of user arguments.
public static class InstallationIdentity
{
    public const string TestProtectedAnchor = @"D:\ImporterPcTest";
    public const string TestProgramRoot = @"D:\CainiaoSupplyStationTest";
    public const string TestDataArea = @"D:\ProgramData\ImporterPcTest";
#if IPC_TEST_INSTALLATION
    public static bool IsTest => true;
#else
    public static bool IsTest => false;
#endif
    public static string ProductName => IsTest ? "ImporterPc.Test" : "ImporterPc";
    public static string DisplayName => IsTest ? "菜鸟供包台（测试）" : "菜鸟供包台";
    public static string DesktopExecutable => IsTest ? "Ipc.Desktop.Test.exe" : "Ipc.Desktop.exe";
    public static string ProtectedAnchor => IsTest ? TestProtectedAnchor : @"D:\ImporterPc";
    public static string StorageRegistryKey => @"SOFTWARE\" + ProductName + @"\Storage";
    public static string UninstallKeyPrefix => @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + ProductName + ".";
    public static string DefaultProgramRoot => IsTest ? @"D:\CainiaoSupplyStationTest" : @"D:\CainiaoSupplyStation";
    public static string DefaultDataRoot => IsTest ? @"D:\ProgramData\ImporterPcTest\data" : @"D:\ProgramData\ImporterPc\data";
    public static string DefaultLaunchDataRoot => IsTest ? DefaultDataRoot : Path.Combine(WindowsStoragePaths.UserRoot, "data");
    public static string DeploymentLogRoot => IsTest ? @"D:\ProgramData\ImporterPcTest\DeploymentLogs" : @"D:\ProgramData\ImporterPc\DeploymentLogs";

    public static void RequireDataRoot(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (IsTest) RequireWithin(path, DefaultDataRoot, "测试版数据目录须位于独立的测试数据目录中。");
        RequireIndependentPath(path);
    }

    public static void RequireProgramRoot(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (IsTest) RequireWithin(path, DefaultProgramRoot, "测试版程序目录须位于独立的测试安装目录中。");
        RequireIndependentPath(path);
    }

    // Backups, exports and logs have their own test area. They cannot select a
    // production original or a shared backup, even when no WAL or owner remains.
    public static void RequireExternalFilePath(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (IsTest) RequireWithin(path, TestDataArea, "测试版备份、交付和导出须使用独立的测试数据区域。");
        RequireIndependentPath(path);
    }

    public static void RequireIndependentPath(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        DeploymentPaths.RequireOrdinaryAncestors(path);
        if (!IsTest) { RequireOutsideTestStorage(path); return; }
        RequireOutsideOtherInstallation(path, InstallationResourceBoundary.OtherInstallation());
    }

    public static void RequireOutsideTestStorage(string path)
    {
        var full = CanonicalPath(path);
        if (new[] { TestProtectedAnchor, TestProgramRoot, TestDataArea }.Any(root => Overlaps(CanonicalPath(root), full)))
            throw new InvalidDataException("正式版不能使用测试版的程序、数据或机器安全目录，也不能选择包含这些目录的范围。");
    }

    internal static bool Overlaps(string left, string right) => TrustedProgramRoots.Contains(left, right) || TrustedProgramRoots.Contains(right, left);
    internal static void RequireOutsideOtherInstallation(string path, DeploymentLocation? other)
    {
        path = CanonicalPath(path);
        for (var remaining = 16; other is not null; other = other.PreviousDeployment)
        {
            if (--remaining < 0) throw new InvalidDataException("另一实例的部署关系无法核验，原目录保留。");
            if (new[] { other.DataRoot, other.ConfigurationDirectory, other.ReleaseRoot, other.MaintenanceRoot }.OfType<string>().Any(root => Overlaps(CanonicalPath(root), path)))
                throw new InvalidDataException("所选范围属于另一实例，不能读取为本站数据或执行写入、恢复和清理。");
        }
    }

    static void RequireWithin(string path, string boundary, string message)
    {
        var full = CanonicalPath(path).TrimEnd(Path.DirectorySeparatorChar);
        boundary = CanonicalPath(boundary).TrimEnd(Path.DirectorySeparatorChar);
        if (!full.Equals(boundary, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(boundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(message);
    }

    // Resolve existing ancestors too: an 8.3 alias must not turn a reserved
    // directory into an apparently unrelated destination for a new child.
    static string CanonicalPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (!OperatingSystem.IsWindows()) return full;
        DeploymentPaths.RequireOrdinaryAncestors(full);
        var suffix = new Stack<string>(); var existing = full;
        while (!Directory.Exists(existing))
        {
            suffix.Push(Path.GetFileName(existing));
            existing = Path.GetDirectoryName(existing) ?? throw new InvalidDataException("所选范围没有可核验的本地目录。");
        }
        var canonical = WindowsStoragePaths.CanonicalDirectory(existing);
        while (suffix.TryPop(out var part)) canonical = Path.Combine(canonical, part);
        return canonical;
    }
}
