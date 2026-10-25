using System.Text.Json;

namespace Ipc.Platform;

public sealed record InstallationInspection(string Status, string Detail, DeploymentLocation? Location = null, string? ChangePlanIdentity = null);

public static partial class WindowsDeploymentSetup
{
    public static string InstallationOperationFile(WindowsDeploymentRequest request)
    {
        if (!ExtensionSchema.IsId(request.ApplicationId) || request.OperationId is null || !ExtensionSchema.IsId(request.OperationId))
            throw new InvalidDataException("本次安装缺少固定操作身份；保留原记录，不能猜测或重复安装。");
        return Path.Combine(MachineRoot, "deployments", request.ApplicationId + ".setup-" + request.OperationId + ".json");
    }
    static string InstallationRequestIdentity(WindowsDeploymentRequest request) => Wire.Hash(Wire.Encode(request with { ChangePlanIdentity = null }));
    // Uses only the original protected journal and registration, never file existence as success.
    public static InstallationInspection InspectInstallation(WindowsDeploymentRequest request)
    {
        var file = InstallationOperationFile(request);
        try
        {
            using var guard = new FileStream(Path.Combine(MachineRoot, "deployments", request.ApplicationId + ".lease"), FileMode.Open, FileAccess.Read, FileShare.None);
            MachineRuntimePolicy.RequireProtectedEvidence(file);
            if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("安装记录超出读取预算");
            return ProjectInstallation(request, File.ReadAllText(file), Current(request.ApplicationId));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        { return new("Unknown", "尚未取得可核验的原操作终态。安装可能仍在执行；请稍后再次核对，禁止重复提交。" + error.GetType().Name); }
    }
    internal static InstallationInspection ProjectInstallation(WindowsDeploymentRequest request, string journal, DeploymentLocation? current)
    {
        using var document = JsonDocument.Parse(journal); var root = document.RootElement;
        if (!root.TryGetProperty("RequestIdentity", out var identity) || identity.GetString() != InstallationRequestIdentity(request))
            return new("Unknown", "操作记录与本次审阅不匹配；保留原身份，不重新安装。");
        var state = root.GetProperty("Status").GetString();
        var plan = root.TryGetProperty("ChangePlanIdentity", out var value) ? value.GetString() : null;
        if (state is "Prepared" or "ReadyToCommit")
        {
            // The caller holds the deployment lease exclusively: this writer can no
            // longer commit behind the inspection. Registration remains the sole commit.
            if (current?.DeploymentPlanIdentity == Wire.Hash(Wire.Encode(request with { ChangePlanIdentity = plan })))
                return new("CommittedNeedsSystemRepair", "原登记已提交，但原回执未完成。请修复原系统入口，不重新安装。", current, plan);
            if (current is null && request.ExpectedRegistrationHash is null || current is not null && Wire.Hash(Wire.Encode(current)) == request.ExpectedRegistrationHash)
                return new("NotCommitted", "已取得独占部署核对权，原登记未改变；原操作在提交前中断。原暂存文件保留，可返回选项重新审阅。");
            return new("Unknown", "原登记已由其他操作改变；请核对原记录，禁止重复提交。");
        }
        if (state == "Failed") return new("Failed", "原操作记录为提交前失败；数据及原登记保留。检查原记录后重新审阅。");
        if (state is not ("Completed" or "CommittedNeedsSystemRepair")) return new("Unknown", "无法识别原操作终态；保持原身份。");
        if (current is null || current.LifecycleState != "Installed" || current.DeploymentPlanIdentity != Wire.Hash(Wire.Encode(request with { ChangePlanIdentity = plan })))
            return new("Unknown", "原操作记录与唯一部署登记不一致；请核对，不重复提交。");
        return new(state, state == "Completed" ? "原安装回执及唯一登记已核对一致。" : "原安装登记已提交，系统入口尚需按原登记修复；不要重新安装。", current, plan);
    }

    public static WindowsRemovalReceipt InspectRemoval(string application)
    {
        using var guard = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.Read, FileShare.None);
        var location = Current(application) ?? throw new InvalidDataException("原部署登记不可读");
        if (location.LifecycleState == "Installed") return new("Installed", "", [location.DataRoot, location.ConfigurationDirectory], false);
        var file = location.MaintenanceOperation ?? throw new InvalidDataException("退役登记缺少原卸载操作");
        MachineRuntimePolicy.RequireProtectedEvidence(file);
        if (!TrustedProgramRoots.Contains(Path.Combine(MachineRoot, "deployments"), file) || new FileInfo(file).Length > 4 * 1024 * 1024)
            throw new InvalidDataException("原卸载记录不可信");
        var plan = Wire.Decode<RemovalState>(File.ReadAllText(file)); ValidateRemovalState(plan, location);
        return new(plan.Status == "Prepared" ? "RetiredCleanupPending" : plan.Status, file,
            [location.DataRoot, location.ConfigurationDirectory], plan.RestartRequired, plan.Remaining ?? [], location.ReleaseRoot);
    }
    static void ValidateRemovalState(RemovalState plan, DeploymentLocation location)
    {
        if (plan.Location.ApplicationId != location.ApplicationId || plan.Location.ReleaseRoot != location.ReleaseRoot ||
            plan.Location.SourceCommit != location.SourceCommit || plan.Location.DataRoot != location.DataRoot || plan.Files.Length > 16384 ||
            plan.Files.Any(n => !ExtensionPackages.SafePath(n)) || plan.Files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Files.Length)
            throw new InvalidDataException("Removal journal differs from authoritative registration");
    }
}
