using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Assembly;

public static partial class ParcelStartupServices
{
    public static async Task<StartupRollbackReview> ReviewRollbackAsync(string expectedHash, string backup,
        IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => Wire.Decode<StartupRollbackReview>(await ElevateAsync(new("ReviewRollback", ExpectedHash: expectedHash, Destination: backup)
        { RollbackOperationId = Wire.Id() }, maintenanceProgress: progress, cancellationToken: token) ?? throw new IOException("缺少回退预检回执。"));

    public static async Task<DeploymentRollbackReceipt> RollbackAsync(StartupRollbackReview review, string backup,
        IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => Wire.Decode<DeploymentRollbackReceipt>(await ElevateAsync(new("Rollback", Destination: backup, ChangePlanIdentity: review.Business?.Identity)
        { RollbackPlan = review.Deployment }, maintenanceProgress: progress, cancellationToken: token) ?? throw new InstallationOutcomeUnknownException("原回退回执缺失，请核对该次结果。"));

    public static async Task<DeploymentRollbackReceipt> InspectRollbackAsync(string operation, bool repair,
        IProgress<MaintenanceProgress>? progress = null, CancellationToken token = default)
        => Wire.Decode<DeploymentRollbackReceipt>(await ElevateAsync(new(repair ? "RepairRollback" : "InspectRollback")
        { RollbackOperationId = operation }, maintenanceProgress: progress, cancellationToken: token) ?? throw new IOException("缺少原回退核对结果。"));

    static async Task<string> ReviewRollbackCore(StartupMaintenanceRequest request, MaintenanceExecution? execution)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var plan = WindowsDeploymentSetup.PlanRollback(ApplicationId, request.RollbackOperationId!);
        if (plan.ExpectedRegistrationHash != request.ExpectedHash) throw new InvalidOperationException("安装登记已变化，请重新读取。");
        StartupChangePreview? business = null;
        if (plan.Allowed)
            business = await Core(new(plan.Current.ConfigurationDirectory, plan.Current.DataRoot, null, null), installer: true)
                .PlanChangeAsync("Rollback", request.Destination, execution ?? throw new InvalidOperationException("缺少维护执行上下文。"));
        return Wire.Encode(new StartupRollbackReview(plan, business));
    }

    static async Task<string> ExecuteRollbackCore(StartupMaintenanceRequest request, MaintenanceExecution? execution)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var plan = request.RollbackPlan ?? throw new InvalidDataException("缺少已审阅回退计划。");
        if (plan.Current.ApplicationId != ApplicationId) throw new InvalidDataException("回退不属于此业务。");
        // Never derive an elevated business-data path from the client's review.
        // The platform rechecks this same registration again at the switch.
        var current = WindowsDeploymentSetup.Current(ApplicationId) ?? throw new InvalidDataException("缺少唯一部署登记。");
        if (Wire.FileHash(WindowsDeploymentSetup.Registration(ApplicationId)) != plan.ExpectedRegistrationHash)
            throw new InvalidOperationException("部署登记已变化，请重新检查回退条件。");
        DeploymentRollbackReceipt? receipt = null;
        var switchStarted = false;
        try { await Core(new(current.ConfigurationDirectory, current.DataRoot, null, null), installer: true)
            .ExecuteChangeAsync(request.ChangePlanIdentity!, "Rollback", request.Destination!, () =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                switchStarted = true;
                receipt = WindowsDeploymentSetup.Rollback(plan, location =>
                {
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); CreateShortcuts(location);
                }, execution); return Task.CompletedTask;
            }, execution); }
        catch (Exception error) when (!switchStarted && error is not OutOfMemoryException)
        {
            // Positive in-process evidence: the program switch callback was
            // never entered. Persist that fact before returning another review.
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            return Wire.Encode(WindowsDeploymentSetup.RecordUnstartedRollback(plan, DiagnosticText.Protect(error.Message)));
        }
        return Wire.Encode(receipt ?? throw new InstallationOutcomeUnknownException("回退提交结果缺失。"));
    }

    static async Task<string> InspectRollbackCore(StartupMaintenanceRequest request, bool repair)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        DeploymentRollbackReceipt? receipt = null;
        void Inspect()
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            receipt = WindowsDeploymentSetup.InspectRollback(ApplicationId, request.RollbackOperationId!, repair, location =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); CreateShortcuts(location);
            });
        }
        if (!repair) Inspect();
        else
        {
            var current = WindowsDeploymentSetup.Current(ApplicationId) ?? throw new InvalidDataException("缺少原部署。");
            await Core(new(current.ConfigurationDirectory, current.DataRoot, null, null), installer: true)
                .WithOfflineApplicationOwnerAsync(() => { Inspect(); return Task.CompletedTask; });
        }
        return Wire.Encode(receipt!);
    }
}
