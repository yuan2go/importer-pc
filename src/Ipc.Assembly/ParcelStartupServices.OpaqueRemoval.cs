using Ipc.Platform;

namespace Ipc.Assembly;

public static partial class ParcelStartupServices
{
    public static async Task<DeploymentOpaqueRemovalPlan> ReviewOpaqueRemovalAsync(string expectedHash, bool skipBackup,
        IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => Wire.Decode<DeploymentOpaqueRemovalPlan>(await ElevateAsync(new("ReviewOpaqueRemoval", ExpectedHash: expectedHash) { SkipBackup = skipBackup },
            maintenanceProgress: progress, cancellationToken: token) ?? throw new IOException("缺少仅移除程序的检查回执。"));

    public static async Task<WindowsRemovalReceipt> RemoveWithoutReadableConfigurationAsync(DeploymentOpaqueRemovalPlan plan,
        IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => Wire.Decode<WindowsRemovalReceipt>(await ElevateAsync(new("OpaqueUninstall") { OpaqueRemovalPlan = plan, SkipBackup = true },
            maintenanceProgress: progress, cancellationToken: token) ?? throw new InstallationOutcomeUnknownException("仅移除程序的结果未确认，请核对原卸载。"));

    static string ReviewOpaqueRemovalCore(StartupMaintenanceRequest request, MaintenanceExecution? execution)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var plan = WindowsDeploymentSetup.ReviewOpaqueRemoval(ApplicationId, request.ExpectedHash!, request.SkipBackup, execution);
        var summary = plan with { RuntimeDirectories = [], RuntimeDirectoryCount = plan.RuntimeDirectories.Length };
        var text = Wire.Encode(summary);
        if (BoundMaintenanceTerminal("Completed", new string('0', 64), null, text).Status != "Completed")
            throw new IOException("原部署路径超过检查结果预算，原程序和数据保留，请由实施人员核对。");
        return text;
    }
}
