using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Ipc.Platform;

public sealed record InstallationContinuation(WindowsDeploymentRequest? Request, InstallationInspection Inspection, string OperationFile);

public static partial class WindowsDeploymentSetup
{
    // Only a protected original request can be used after its initiating process
    // disappears. An extracted media path is evidence, not permission to replay.
    internal static WindowsDeploymentRequest ReadInstallationRequest(string application, string operation, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var request = Wire.Decode<WindowsDeploymentRequest>(root.GetProperty("Request").GetRawText());
        RequireCurrentInstallationRequest(request);
        if (request.ApplicationId != application || request.OperationId != operation ||
            root.GetProperty("RequestIdentity").GetString() != InstallationRequestIdentity(request) ||
            !Path.IsPathFullyQualified(request.DataRoot) || request.SourceCommit is not { Length: 40 } || !request.SourceCommit.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("原安装请求与受保护操作身份不一致。");
        return request;
    }

    public static WindowsDeploymentRequest ReadInstallationRequest(string application, string operation)
    {
        var file = OriginalInstallationFile(application, operation);
        MachineRuntimePolicy.RequireProtectedEvidence(file);
        if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("原安装请求超出读取预算。");
        return ReadInstallationRequest(application, operation, File.ReadAllText(file));
    }

    static string OriginalInstallationFile(string application, string operation)
    {
        if (!ExtensionSchema.IsId(application) || !IsInstallationOperationId(operation)) throw new InvalidDataException("原安装操作身份无效。");
        return Path.Combine(MachineRoot, "deployments", application + ".setup-" + operation + ".json");
    }

    public static InstallationContinuation? DiscoverInstallation(string application, string mediaSource)
    {
        if (!ExtensionSchema.IsId(application)) throw new InvalidDataException("无效的应用身份。");
        var directory = Path.Combine(MachineRoot, "deployments");
        if (!Directory.Exists(directory)) return null; // Read-only on a new machine.
        var files = Directory.EnumerateFiles(directory, application + ".setup-*.json").Take(1025).ToArray();
        if (files.Length > 1024) throw new InvalidDataException("安装历史超过核对预算，原件保留。");
        if (files.Length == 0) return null;
        using var lease = new FileStream(Path.Combine(directory, application + ".lease"), FileMode.Open, FileAccess.Read, FileShare.None);
        var current = Current(application);
        var pending = new List<InstallationContinuation>();
        InstallationContinuation? completed = null;
        long bytes = 0;
        foreach (var file in files)
        {
            MachineRuntimePolicy.RequireProtectedEvidence(file);
            var size = new FileInfo(file).Length; bytes = checked(bytes + size);
            if (size > 4 * 1024 * 1024 || bytes > 64 * 1024 * 1024) throw new InvalidDataException("安装历史超过读取预算，原件保留。");
            // System-repair archives are evidence of the original operation, not
            // another operation to resume.
            if (Path.GetFileName(file).Contains(".json.pre-system-repair-", StringComparison.Ordinal)) continue;
            var json = File.ReadAllText(file);
            using var document = JsonDocument.Parse(json);
            var state = document.RootElement.GetProperty("Status").GetString();
            if (!document.RootElement.TryGetProperty("Request", out _))
            {
                if (state is "Completed" or "Failed") continue; // No resumable request exists in a retired record format.
                pending.Add(new(null, new("Unknown", "原安装缺少可核验的完整请求；保留原件，请核对安装记录，不能重复提交。"), file));
                continue;
            }
            var operation = Path.GetFileName(file)[(application.Length + ".setup-".Length)..^5];
            var request = ReadInstallationRequest(application, operation, json);
            var changePlan = document.RootElement.TryGetProperty("ChangePlanIdentity", out var change) ? change.GetString() : null;
            if (state == "Failed" && current?.DeploymentPlanIdentity != Wire.Hash(Wire.Encode(request with { ChangePlanIdentity = changePlan }))) continue;
            if (state == "Completed" && current?.DeploymentPlanIdentity != Wire.Hash(Wire.Encode(request with { ChangePlanIdentity = changePlan }))) continue;
            var inspection = ProjectInstallation(request, json, current);
            var item = new InstallationContinuation(request, inspection, file);
            if (inspection.Status is "Completed")
            {
                if (request.SourceCommit == mediaSource)
                {
                    if (completed is not null) throw new InvalidDataException("当前登记对应多个原安装结果，须先核对。");
                    completed = item;
                }
            }
            else if (inspection.Status is not ("Failed" or "Superseded")) pending.Add(item);
        }
        if (pending.Count > 1) return new(null, new("Unknown", "存在多个未核对的原安装，不能选取其中一次继续；请核对原记录。"), directory);
        return pending.SingleOrDefault() ?? completed;
    }

    [SupportedOSPlatform("windows")]
    public static void ConcludeUncommittedInstallation(string application, string operation, MaintenanceExecution? execution = null)
    {
        execution?.Checkpoint();
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        using var lease = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var file = OriginalInstallationFile(application, operation);
        var request = ReadInstallationRequest(application, operation);
        var json = File.ReadAllText(file);
        var current = Current(application);
        var inspection = ProjectInstallation(request, json, current);
        if (inspection.Status == "Failed") return;
        if (inspection.Status != "NotCommitted") throw new InstallationOutcomeUnknownException("原安装不能确证未提交；保持原操作，请重新核对。");
        using var ownership = new StoppedApplicationOwnership(MachineRuntimePolicy.InstallerLeaseRoot(""), application, request.DataRoot, execution?.Token ?? default);
        using var document = JsonDocument.Parse(json);
        if (!CanCloseUncommittedInstallation(document.RootElement, current)) throw new InvalidDataException("原安装提交边界无法核验，未修改回执。");
        var closed = JsonNode.Parse(json)!;
        closed["Status"] = "Failed";
        closed["RecoveredFromStatus"] = document.RootElement.GetProperty("Status").GetString();
        closed["RecoveryReason"] = "Original registration verified unchanged under exclusive stopped ownership; review new inputs before another installation";
        execution?.BeginCommit(MaintenanceStage.RepairingSystem);
        try { WriteProtected(file, closed, new SecurityIdentifier(request.RuntimeAccountSid)); }
        catch (Exception error) { throw new InstallationOutcomeUnknownException("原安装收口回执未确认，请核对原次，不能直接开始新安装。", error); }
    }
}
