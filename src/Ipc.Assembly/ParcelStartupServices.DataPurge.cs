using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Assembly;

// Permanent deletion of a removed station's data: business check (every request settled),
// optional backup, then the platform's inventory-fenced delete. Never part of uninstall.
public static partial class ParcelStartupServices
{
    public static async Task<StartupDataPurgeReview> ReviewDataPurgeAsync(string expectedHash, string? backup,
        IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => Wire.Decode<StartupDataPurgeReview>(await ElevateAsync(new("ReviewDataPurge", ExpectedHash: expectedHash, Destination: backup)
        { RollbackOperationId = Wire.Id() }, maintenanceProgress: progress, cancellationToken: token)
            ?? throw new IOException("缺少永久删除检查回执。"));

    public static async Task<DeploymentPurgeReceipt> PurgeDataAsync(StartupDataPurgeReview review, string? backup,
        IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => Wire.Decode<DeploymentPurgeReceipt>(await ElevateAsync(new("PurgeData", Destination: backup, ChangePlanIdentity: review.Business?.Identity)
        { DataPurgePlan = review.Deployment }, maintenanceProgress: progress, cancellationToken: token)
            ?? throw new InstallationOutcomeUnknownException("缺少原永久删除结果，请核对原次。"));

    public static async Task<DeploymentPurgeReceipt> ResumeDataPurgeAsync(string operation, IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => Wire.Decode<DeploymentPurgeReceipt>(await ElevateAsync(new("ResumeDataPurge") { RollbackOperationId = operation },
            maintenanceProgress: progress, cancellationToken: token) ?? throw new InstallationOutcomeUnknownException("缺少原永久删除继续结果。"));

    static DeploymentPurgeBusinessCheck? BusinessCheck(StartupChangePreview? preview) =>
        preview is null ? null : new(preview.Identity, preview.Allowed, preview.OperatorSummary);

    static async Task<string> ReviewDataPurgeCore(StartupMaintenanceRequest request, MaintenanceExecution? execution)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var current = WindowsDeploymentSetup.Current(ApplicationId) ?? throw new InvalidDataException("缺少唯一部署登记。");
        if (Wire.FileHash(WindowsDeploymentSetup.Registration(ApplicationId)) != request.ExpectedHash) throw new InvalidOperationException("安装登记已变化，请重新读取。");
        StartupChangePreview? business = null;
        if (current.LifecycleState == "RemovedDataRetained")
            business = await Core(new(current.ConfigurationDirectory, current.DataRoot, null, null), installer: true)
                .PlanChangeAsync("Purge", request.Destination, execution ?? throw new InvalidOperationException("缺少维护上下文。"));
        var plan = WindowsDeploymentSetup.PlanDataPurge(ApplicationId, request.RollbackOperationId!, BusinessCheck(business));
        // Review carries counts only; the commit re-derives and compares the full inventory.
        var summary = new StartupDataPurgeReview(plan with { Delete = plan.Delete.Select(d => d with { Files = [], ReviewedFileCount = d.Files.Count }).ToArray() }, business);
        return Wire.Encode(summary);
    }

    static async Task<string> ExecuteDataPurgeCore(StartupMaintenanceRequest request, MaintenanceExecution? execution)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var plan = request.DataPurgePlan ?? throw new InvalidDataException("缺少固定永久删除计划。");
        if (plan.ApplicationId != ApplicationId) throw new InvalidDataException("永久删除不属于此业务。");
        var current = WindowsDeploymentSetup.Current(ApplicationId) ?? throw new InvalidDataException("缺少唯一部署登记。");
        if (Wire.FileHash(WindowsDeploymentSetup.Registration(ApplicationId)) != plan.ExpectedRegistrationHash) throw new InvalidOperationException("登记已变化，请重新检查。");
        // Re-check every request and take the chosen backup; the owner lease is released before
        // the delete so its own lock file can be removed. The inventory fence covers the gap.
        var business = await Core(new(current.ConfigurationDirectory, current.DataRoot, null, null), installer: true)
            .ExecuteChangeAsync(request.ChangePlanIdentity!, "Purge", request.Destination ?? "", () => Task.CompletedTask, execution);
        var receipt = WindowsDeploymentSetup.PurgeData(plan, BusinessCheck(business)!, execution);
        return Wire.Encode(receipt with { Remaining = receipt.Remaining.Take(256).ToArray() });
    }

    static string ResumeDataPurgeCore(StartupMaintenanceRequest request, MaintenanceExecution? execution)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var receipt = WindowsDeploymentSetup.ResumeDataPurge(ApplicationId, request.RollbackOperationId!, execution);
        return Wire.Encode(receipt with { Remaining = receipt.Remaining.Take(256).ToArray() });
    }
}
