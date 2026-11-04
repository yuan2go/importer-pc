using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Assembly;

public static partial class ParcelStartupServices
{
    public static Task<ProgramUninstallResult> UninstallAsync(string expectedHash, bool deleteData,
        IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => UninstallAsync(expectedHash, deleteData, progress, token, null);

    public static async Task<ProgramUninstallResult> UninstallAsync(string expectedHash, bool deleteData,
        IProgress<MaintenanceProgress>? progress, CancellationToken token, DeploymentMedia? media)
    {
        var current = WindowsDeploymentSetup.Current(ApplicationId) ?? throw new InvalidDataException("未找到安装信息。");
        var deployment = media is null ? null : new WindowsDeploymentRequest(media.Root, ApplicationId, media.SourceCommit,
            current.RuntimeAccountSid, current.DataRoot, current.ConfigurationDirectory, RunMode.Simulation, null, null, []);
        return Wire.Decode<ProgramUninstallResult>(await ElevateAsync(new(media is null ? "SimpleUninstall" : "MediaUninstall", ExpectedHash: expectedHash, Deployment: deployment) { DeleteData = deleteData, RemovalOperationId = Wire.Id() },
            maintenanceProgress: progress, cancellationToken: token) ?? throw new InstallationOutcomeUnknownException("卸载结果尚未确认，请查看结果。"));
    }

    public static ProgramUninstallResult InspectUninstall()
    {
        var current = WindowsDeploymentSetup.Current(ApplicationId) ?? throw new InvalidDataException("未找到安装信息。");
        return current.LifecycleState is "Purging" or "PartiallyPurged" or "Purged"
            ? new(null, WindowsDeploymentSetup.InspectDataPurge(ApplicationId), true)
            : new(WindowsDeploymentSetup.InspectRemoval(ApplicationId), null, false);
    }

    static ProgramUninstallResult SimpleUninstallCore(StartupMaintenanceRequest request, MaintenanceExecution? execution)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        if (ApplicationId != "sportmaster") throw new NotSupportedException("此卸载方式仅用于当前SM。");
        var current = WindowsDeploymentSetup.Current(ApplicationId) ?? throw new InvalidDataException("未找到安装信息。");
        if (Wire.FileHash(WindowsDeploymentSetup.Registration(ApplicationId)) != request.ExpectedHash)
            throw new InvalidOperationException("安装信息已变化，请重新打开卸载程序。");
        if (current.LifecycleState == "Purged") return InspectUninstall();
        if (current.LifecycleState is "Purging" or "PartiallyPurged")
        {
            if (!request.DeleteData) throw new InvalidOperationException("上次数据删除尚未完成。请选择“同时删除本站数据”后重试卸载。");
            var original = WindowsDeploymentSetup.InspectDataPurge(ApplicationId);
            return new(null, WindowsDeploymentSetup.ResumeDataPurge(ApplicationId, original.OperationId, execution), true);
        }
        // Only process ownership gates uninstall. Neither branch opens or
        // repairs a business database, parses configuration, or creates backups.
        var removal = WindowsDeploymentSetup.UninstallProgram(ApplicationId, request.ExpectedHash!, RemoveShortcuts, execution, request.RemovalOperationId);
        if (!request.DeleteData || removal.Status != "RemovedDataRetained") return new(removal, null, request.DeleteData);
        try
        {
            var plan = WindowsDeploymentSetup.PlanDataPurge(ApplicationId, Wire.Id());
            if (!plan.Allowed) throw new InvalidOperationException(plan.Reason);
            var deletion = WindowsDeploymentSetup.PurgeData(plan, execution);
            return new(removal, deletion with { Remaining = deletion.Remaining.Take(256).ToArray() }, true);
        }
        catch (Exception error) when (error is not InstallationOutcomeUnknownException)
        { throw new InstallationOutcomeUnknownException("程序已卸载，数据清理未完成。请查看结果后重试卸载。", error); }
    }
}
