using Ipc.Platform;

namespace Ipc.Assembly;

public static partial class ParcelStartupServices
{
    public static async Task<InstallationInspection> ContinueInstallationAsync(DeploymentMedia media, WindowsDeploymentRequest original,
        bool repair, IProgress<MaintenanceProgress>? progress, CancellationToken token)
    {
        // The current complete medium provides the executing helper. The original
        // request is independently reloaded from the protected machine journal.
        var route = original with { MediaRoot = media.Root, SourceCommit = media.SourceCommit };
        return Wire.Decode<InstallationInspection>(await ElevateAsync(new(repair ? "MediaRepairInstallation" : "MediaConcludeInstallation", Deployment: route)
        { InstallationOperationId = original.OperationId }, maintenanceProgress: progress, cancellationToken: token)
            ?? throw new InstallationOutcomeUnknownException("原安装续办回执缺失，请继续核对原结果。"));
    }

    static string ContinueInstallationCore(StartupMaintenanceRequest request, bool repair, MaintenanceExecution? execution)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        RequireMatchingMedia(request.Deployment ?? throw new InvalidDataException("缺少当前完整介质。"), execution?.Token ?? default);
        _ = DeploymentMedia.Inspect(request.Deployment.MediaRoot, ApplicationId, execution?.Token ?? default);
        var original = WindowsDeploymentSetup.ReadInstallationRequest(ApplicationId, request.InstallationOperationId ?? throw new InvalidDataException("缺少原安装身份。"));
        if (repair)
            WindowsDeploymentSetup.RepairCommittedInstallation(original, Wire.FileHash(WindowsDeploymentSetup.Registration(ApplicationId)), (location, previous) =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                CreateShortcuts(location, previous);
            }, execution);
        else WindowsDeploymentSetup.ConcludeUncommittedInstallation(ApplicationId, original.OperationId!, execution);
        return Wire.Encode(WindowsDeploymentSetup.InspectInstallation(original));
    }
}
