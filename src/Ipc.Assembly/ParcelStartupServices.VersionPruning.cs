using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Assembly;

public static partial class ParcelStartupServices
{
    public static async Task<StartupVersionPruneReview> ReviewVersionPruningAsync(string expectedHash, string backup, bool ordinaryStartupConfirmed,
        IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => Wire.Decode<StartupVersionPruneReview>(await ElevateAsync(new("ReviewVersionPruning", ExpectedHash: expectedHash, Destination: backup)
        { RollbackOperationId = Wire.Id(), OrdinaryStartupConfirmed = ordinaryStartupConfirmed }, maintenanceProgress: progress, cancellationToken: token)
            ?? throw new IOException("缺少版本清理检查回执。"));

    public static async Task<DeploymentPruneReceipt> PruneVersionsAsync(StartupVersionPruneReview review, string backup,
        IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => Wire.Decode<DeploymentPruneReceipt>(await ElevateAsync(new("PruneVersions", Destination: backup, ChangePlanIdentity: review.Business?.Identity)
        { VersionPrunePlan = review.Deployment }, maintenanceProgress: progress, cancellationToken: token)
            ?? throw new InstallationOutcomeUnknownException("缺少原版本清理结果，请核对原次。"));

    public static async Task<DeploymentPruneReceipt> InspectVersionPruningAsync(string operation, bool resume,
        IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => Wire.Decode<DeploymentPruneReceipt>(await ElevateAsync(new(resume ? "ResumeVersionPruning" : "InspectVersionPruning")
        { RollbackOperationId = operation }, maintenanceProgress: progress, cancellationToken: token)
            ?? throw new IOException("缺少原版本清理核对结果。"));

    public static async Task<DeploymentPruneReceipt> KeepVersionPruningRemainderAsync(string operation,
        IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => Wire.Decode<DeploymentPruneReceipt>(await ElevateAsync(new("KeepVersionPruningRemainder") { RollbackOperationId = operation },
            maintenanceProgress: progress, cancellationToken: token) ?? throw new InstallationOutcomeUnknownException("缺少原版本保留余项回执。"));

    static async Task<string> ReviewVersionPruningCore(StartupMaintenanceRequest request, MaintenanceExecution? execution)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var plan = WindowsDeploymentSetup.PlanVersionPruning(ApplicationId, request.RollbackOperationId!, request.OrdinaryStartupConfirmed);
        var current = WindowsDeploymentSetup.Current(ApplicationId) ?? throw new InvalidDataException("缺少唯一部署登记。");
        if (plan.ExpectedRegistrationHash != request.ExpectedHash) throw new InvalidOperationException("安装登记已变化，请重新读取。");
        StartupChangePreview? business = null;
        if (plan.Allowed) business = await Core(new(current.ConfigurationDirectory, current.DataRoot, null, null), installer: true)
            .PlanChangeAsync("PruneVersions", request.Destination, execution ?? throw new InvalidOperationException("缺少维护上下文。"));
        return Wire.Encode(VersionPruningReviewSummary(new(plan, business)));
    }

    static async Task<string> ExecuteVersionPruningCore(StartupMaintenanceRequest request, MaintenanceExecution? execution)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var plan = request.VersionPrunePlan ?? throw new InvalidDataException("缺少固定版本清理计划。");
        if (plan.ApplicationId != ApplicationId) throw new InvalidDataException("清理不属于此业务。");
        var current = WindowsDeploymentSetup.Current(ApplicationId) ?? throw new InvalidDataException("缺少唯一部署登记。");
        if (Wire.FileHash(WindowsDeploymentSetup.Registration(ApplicationId)) != plan.ExpectedRegistrationHash)
            throw new InvalidOperationException("登记已变化，请重新检查。");
        DeploymentPruneReceipt? receipt = null;
        await Core(new(current.ConfigurationDirectory, current.DataRoot, null, null), installer: true)
            .ExecuteChangeAsync(request.ChangePlanIdentity!, "PruneVersions", request.Destination!, () =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                receipt = WindowsDeploymentSetup.PruneVersions(plan, execution); return Task.CompletedTask;
            }, execution);
        return Wire.Encode(VersionPruningReceiptSummary(receipt ?? throw new InstallationOutcomeUnknownException("原清理回执缺失。")));
    }

    static async Task<string> InspectVersionPruningCore(StartupMaintenanceRequest request, bool resume, MaintenanceExecution? execution)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        DeploymentPruneReceipt? receipt = null;
        void Inspect()
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            receipt = WindowsDeploymentSetup.InspectVersionPruning(ApplicationId, request.RollbackOperationId!, resume, execution);
        }
        if (!resume) Inspect();
        else
        {
            var current = WindowsDeploymentSetup.Current(ApplicationId) ?? throw new InvalidDataException("缺少原部署。");
            await Core(new(current.ConfigurationDirectory, current.DataRoot, null, null), installer: true)
                .WithOfflineApplicationOwnerAsync(() => { Inspect(); return Task.CompletedTask; });
        }
        return Wire.Encode(VersionPruningReceiptSummary(receipt!));
    }
}
