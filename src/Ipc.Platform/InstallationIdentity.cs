namespace Ipc.Platform;

// A build-time identity keeps installation authority independent of user arguments.
public static class InstallationIdentity
{
#if IPC_TEST_INSTALLATION
    public static bool IsTest => true;
#else
    public static bool IsTest => false;
#endif
    public static string ProductName => IsTest ? "ImporterPc.Test" : "ImporterPc";
    public static string DisplayName => IsTest ? "菜鸟供包台（测试）" : "菜鸟供包台";
    public static string DesktopExecutable => IsTest ? "Ipc.Desktop.Test.exe" : "Ipc.Desktop.exe";
    public static string ProtectedAnchor => IsTest ? @"D:\ImporterPcTest" : @"D:\ImporterPc";
    public static string StorageRegistryKey => @"SOFTWARE\" + ProductName + @"\Storage";
    public static string UninstallKeyPrefix => @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + ProductName + ".";
    public static string DefaultProgramRoot => IsTest ? @"D:\CainiaoSupplyStationTest" : @"D:\CainiaoSupplyStation";
    public static string DefaultDataRoot => IsTest ? @"D:\ProgramData\ImporterPcTest\data" : @"D:\ProgramData\ImporterPc\data";
    public static string DefaultLaunchDataRoot => IsTest ? DefaultDataRoot : Path.Combine(WindowsStoragePaths.UserRoot, "data");
    public static string DeploymentLogRoot => IsTest ? @"D:\ProgramData\ImporterPcTest\DeploymentLogs" : @"D:\ProgramData\ImporterPc\DeploymentLogs";

    public static void RequireDataRoot(string path)
    {
        if (IsTest && OperatingSystem.IsWindows())
            RequireWithin(path, DefaultDataRoot, "测试版数据目录须位于独立的测试数据目录中。");
    }

    public static void RequireProgramRoot(string path)
    {
        if (IsTest && OperatingSystem.IsWindows())
            RequireWithin(path, DefaultProgramRoot, "测试版程序目录须位于独立的测试安装目录中。");
    }

    static void RequireWithin(string path, string boundary, string message)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (!full.Equals(boundary, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(boundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(message);
    }
}
