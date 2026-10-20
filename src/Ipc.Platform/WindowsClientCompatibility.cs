using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using Microsoft.Win32;

namespace Ipc.Platform;

public sealed record WindowsClientAssessment(bool CanStart, bool PrimaryTarget, string Target, string Reason);
public sealed record WindowsClientEnvironment(string OsVersion, string Edition, string DisplayVersion, string OsArchitecture,
    string ProcessArchitecture, string CpuModel, int LogicalProcessors, string Runtime,
    bool RuntimeSse2Enabled, bool RuntimeAvxEnabled, bool RuntimeAvx2Enabled, WindowsClientAssessment Assessment, string? InventoryDiagnostic = null);

public static class WindowsClientCompatibility
{
    // A functional admission floor, not a claim of OS-vendor lifecycle or device qualification.
    public static WindowsClientAssessment Assess(Version windows, Architecture os, Architecture process)
    {
        if (os != Architecture.X64 || process != Architecture.X64)
            return new(false, false, "需要原生 x64 Windows", "本包为 x64；32位Windows及ARM64不在本次交付范围。");
        if (windows.Major != 10 || windows.Build < 14393)
            return new(false, false, "系统版本不满足本包基线", "需要 Windows 10/11 x64；低于Windows10 build14393的系统不在运行时基线内。");
        var target = windows.Build >= 22000 ? "Windows 11 x64" : "Windows 10 x64";
        var primary = windows.Build >= 22000 || windows.Build == 19045;
        return new(true, primary, target, primary
            ? "属于本轮功能兼容目标；实机验证、微软支持周期及设备SDK资格另行核对。"
            : "满足基础版本/架构条件，但不是本轮优先的Win10 22H2/Win11矩阵；需单独实机验证。");
    }
    public static WindowsClientEnvironment Capture()
    {
        var os = RuntimeInformation.OSArchitecture; var process = RuntimeInformation.ProcessArchitecture;
        if (!OperatingSystem.IsWindows()) return new(Environment.OSVersion.VersionString, "", "", os.ToString(), process.ToString(), "", Environment.ProcessorCount,
            RuntimeInformation.FrameworkDescription, Sse2.IsSupported, Avx.IsSupported, Avx2.IsSupported, new(false, false, "需要Windows", "本包为原生Windows桌面程序。"));
        string edition = "Unknown", display = "Unknown", model = "Unknown"; string? diagnostic = null;
        try
        {
            using var version = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            edition = version?.GetValue("EditionID") as string ?? "Unknown";
            display = version?.GetValue("DisplayVersion") as string ?? version?.GetValue("ReleaseId") as string ?? "Unknown";
            using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            model = (cpu?.GetValue("ProcessorNameString") as string ?? "Unknown").Trim();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { diagnostic = "部分只读环境信息不可用：" + error.GetType().Name + "；未检测的型号不视为已验证。"; }
        return new(Environment.OSVersion.Version.ToString(), edition, display, os.ToString(), process.ToString(), model, Environment.ProcessorCount,
            RuntimeInformation.FrameworkDescription, Sse2.IsSupported, Avx.IsSupported, Avx2.IsSupported, Assess(Environment.OSVersion.Version, os, process), diagnostic);
    }
}
