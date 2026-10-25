using Ipc.Platform;

namespace Ipc.Assembly;

public sealed record WindowsDeploymentPlan(int SchemaVersion, string PlanSha256, WindowsDeploymentRequest Deployment);
public static class DeploymentBatch
{
    sealed class OriginalOperationExistsException(string operationFile) : IOException("原安装操作已存在；只能核对原记录，不能重复提交")
    { public string OperationFile { get; } = operationFile; }

    internal static void RequireUnsubmittedOperation(string operationFile)
    {
        DeploymentPaths.RequireOrdinaryAncestors(operationFile);
        try { _ = File.GetAttributes(operationFile); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        // Do not inspect a status and infer permission to retry. Any original
        // journal, including Unknown/failed/interrupted, fences this operation.
        // Access/IO errors propagate; File.Exists would hide an unreadable record.
        throw new OriginalOperationExistsException(operationFile);
    }

    public static WindowsDeploymentPlan Freeze(WindowsDeploymentRequest request) => new(1, Wire.Hash(Wire.Encode(request)), request);
    public static bool Handle(string[] args)
    {
        if (args.FirstOrDefault() != "--deployment-plan") return false;
        string? receipt = null; bool ownsReceipt = false; int code = 2;
        void Result(object value)
        { if (ownsReceipt) ExtensionStore.AtomicWrite(receipt!, value); }
        try
        {
            if (args.Length is not (4 or 5) || args[2] != "--receipt" || args.Length == 5 && args[4] != "--preflight") throw new ArgumentException("--deployment-plan <plan.json> --receipt <new-receipt.json> [--preflight]");
            receipt = Path.GetFullPath(args[3]); DeploymentPaths.RequireOrdinaryAncestors(receipt);
            // Reserve with CreateNew before any mutation. Error reporting must never
            // overwrite somebody else's receipt or use an unvalidated output path.
            using (var reserved = new FileStream(receipt, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { reserved.Write(System.Text.Encoding.UTF8.GetBytes("{\"Status\":\"Reviewing\"}")); reserved.Flush(true); }
            ownsReceipt = true;
            if (new FileInfo(args[1]).Length > 8 * 1024 * 1024) throw new InvalidDataException("部署计划超出预算");
            var plan = Wire.Decode<WindowsDeploymentPlan>(File.ReadAllText(args[1])); var request = plan.Deployment ?? throw new InvalidDataException("缺少逐站部署参数");
            if (plan.SchemaVersion != 1 || plan.PlanSha256 != Wire.Hash(Wire.Encode(request)) || request.ApplicationId != ParcelStartupServices.ApplicationId || request.Packages is null || request.Packages.Count > 128 || request.ConfigurationHashes is null || request.OperationId is not null && !WindowsDeploymentSetup.IsInstallationOperationId(request.OperationId))
                throw new InvalidDataException("部署计划格式/身份不匹配");
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows安装计划不能在portable运行");
            var media = DeploymentMedia.Inspect(request.MediaRoot, request.ApplicationId);
            if (media.SourceCommit != request.SourceCommit) throw new InvalidDataException("计划的源介质已变化");
            var committed = WindowsDeploymentSetup.Current(request.ApplicationId);
            if (committed is { LifecycleState: "Installed" } && committed.DeploymentPlanIdentity == plan.PlanSha256)
            {
                // Re-entry acknowledges only the original target. A migration or
                // changed registration must not become a success for an old plan.
                ParcelStartupServices.RequireMatchingInstalledReceipt(request, committed);
                if (DeploymentMedia.Inspect(committed.ReleaseRoot, request.ApplicationId).SourceCommit != request.SourceCommit)
                    throw new InvalidDataException("原部署介质与本计划来源不匹配；保留原登记并核对");
                Result(new { Status = "AlreadyInstalled", plan.PlanSha256, Location = committed, DeviceOrBusinessStarted = false });
                Environment.ExitCode = 0; return true;
            }
            _ = ParcelStartupServices.RevalidateDeployment(media, request);
            if (request.OperationId is not null) RequireUnsubmittedOperation(WindowsDeploymentSetup.InstallationOperationFile(request));
            var components = DeploymentComponentReviewing.Inspect(request);
            if (args.Length == 5)
            { Result(new { Status = "PreflightPassed", PlanSha256 = plan.PlanSha256, request.OperationId, request.ApplicationId, request.SourceCommit, Components = components, DeviceOrBusinessStarted = false }); code = 0; }
            else
            {
                if (request.OperationId is null)
                {
                    // A legacy plan can be checked or recognised as already
                    // committed above, but cannot acquire a fresh identity here.
                    Result(new { Status = "LegacyPlanNeedsReview", plan.PlanSha256, ExitCode = 2, OriginalDataRetained = true });
                    Environment.ExitCode = 2; return true;
                }
                MachineRuntimePolicy.RequireExtensionInstallerAuthority(); // No hidden elevation or bypass in unattended execution.
                ParcelStartupServices.ExecuteBatchInstall(request).GetAwaiter().GetResult();
                var installed = WindowsDeploymentSetup.Current(request.ApplicationId) ?? throw new IOException("缺少安装提交回执");
                if (installed.DeploymentPlanIdentity != plan.PlanSha256 || installed.LifecycleState != "Installed") throw new IOException("安装提交身份与计划不匹配");
                Result(new { Status = "Installed", PlanSha256 = plan.PlanSha256, Location = installed, ProductionQualified = false, DeviceOrBusinessStarted = false }); code = 0;
            }
        }
        catch (OriginalOperationExistsException error)
        { code = 10; Result(new { Status = "OriginalOperationNeedsInspection", ExitCode = code, error.OperationFile, OriginalDataRetained = true, DeviceOrBusinessStarted = false }); }
        catch (UnauthorizedAccessException)
        { code = 5; Result(new { Status = "AuthorizationRequired", ExitCode = code, OriginalDataRetained = true }); }
        catch (Exception error)
        { code = error is ArgumentException or InvalidDataException or System.Text.Json.JsonException or FormatException ? 2 : 10; Result(new { Status = "FailedOrNeedsRecovery", ExitCode = code, ErrorCode = error.GetType().Name, OriginalDataRetained = true }); }
        Environment.ExitCode = code; return true;
    }
}
