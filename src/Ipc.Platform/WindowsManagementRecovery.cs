using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Ipc.Platform;

public static class WindowsManagementRecovery
{
    public static void RequireConfirmation(string dataRoot)
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
            throw new UnauthorizedAccessException("本站密码恢复需要交互式Windows管理员确认。");
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("需要Windows管理员权限。");
        DeploymentPaths.RequireOrdinaryAncestors(dataRoot);
        var installation = Wire.Hash(Path.GetFullPath(dataRoot).ToUpperInvariant())[..12];
        if (MessageBox(0, "是否重置本站管理密码？\n\n本站标识：" + installation +
            "\n将撤销所有原管理授权；设备配置、操作员和业务记录保持不变。", "菜鸟供包台 · Windows管理员确认", 0x4 | 0x30 | 0x100 | 0x10000) != 6)
            throw new UnauthorizedAccessException("Windows管理员已取消本站密码重置，原密码保留。");
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    static extern int MessageBox(nint owner, string text, string caption, uint flags);
}
