using System.Text.Json;

namespace Ipc.Platform;

internal sealed record DeploymentTransition(DeploymentLocation Previous, DeploymentLocation Next);

public static partial class WindowsDeploymentSetup
{
    // The caller holds the deployment lease. Edges come only from protected
    // intents; reaching the authoritative registration proves their commit.
    static DeploymentTransition[] ReadDeploymentHistory(string application)
    {
        var directory = Path.Combine(MachineRoot, "deployments");
        var edges = new List<DeploymentTransition>();
        var incompleteLocations = new List<(WindowsDeploymentRequest Request, DeploymentLocation Previous, string Json, string Identity)>();
        long bytes = 0;
        foreach (var kind in new[] { "setup", "remove", "migration", "rollback" })
        {
            var prefix = application + "." + kind + "-";
            var files = Directory.EnumerateFiles(directory, prefix + "*.json").Take(1025).ToArray();
            if (files.Length > 1024) throw new InvalidDataException("部署历史超过核对预算，保留原记录。");
            foreach (var file in files)
            {
                var operation = Path.GetFileName(file)[prefix.Length..^5];
                if (!IsInstallationOperationId(operation)) continue; // Repair archives are not operations.
                MachineRuntimePolicy.RequireProtectedEvidence(file);
                var length = new FileInfo(file).Length; bytes = checked(bytes + length);
                if (length > 4 * 1024 * 1024 || bytes > 64 * 1024 * 1024) throw new InvalidDataException("部署历史超过读取预算。");
                var json = File.ReadAllText(file);
                using var document = JsonDocument.Parse(json); var root = document.RootElement;
                if (kind == "setup")
                {
                    if (root.GetProperty("Status").GetString() is not ("ReadyToCommit" or "Completed" or "CommittedNeedsSystemRepair") ||
                        !root.TryGetProperty("Previous", out var previous) || previous.ValueKind == JsonValueKind.Null) continue;
                    if (!root.TryGetProperty("Location", out var target) || target.ValueKind != JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("Request", out _))
                        {
                            var originalRequest = ReadInstallationRequest(application, operation, json);
                            var previousLocation = Wire.Decode<DeploymentLocation>(previous.GetRawText());
                            if (originalRequest.ExpectedRegistrationHash != Wire.Hash(Wire.Encode(previousLocation))) throw new InvalidDataException("原安装的前序登记与冻结请求冲突。");
                            var changePlan = root.TryGetProperty("ChangePlanIdentity", out var changeValue) ? changeValue.GetString() : null;
                            incompleteLocations.Add((originalRequest, Wire.Decode<DeploymentLocation>(previous.GetRawText()), json, Wire.Hash(Wire.Encode(originalRequest with { ChangePlanIdentity = changePlan }))));
                        }
                        continue;
                    }
                    var next = Wire.Decode<DeploymentLocation>(target.GetRawText());
                    if (root.TryGetProperty("Request", out _))
                    {
                        var request = ReadInstallationRequest(application, operation, json);
                        var previousLocation = Wire.Decode<DeploymentLocation>(previous.GetRawText());
                        if (request.ExpectedRegistrationHash != Wire.Hash(Wire.Encode(previousLocation))) throw new InvalidDataException("原安装的前序登记与冻结请求冲突。");
                        var change = root.TryGetProperty("ChangePlanIdentity", out var plan) ? plan.GetString() : null;
                        if (next.DeploymentPlanIdentity != Wire.Hash(Wire.Encode(request with { ChangePlanIdentity = change })) || next.SourceCommit != request.SourceCommit)
                            throw new InvalidDataException("原安装目标与原请求不一致。");
                    }
                    Add(Wire.Decode<DeploymentLocation>(previous.GetRawText()), next);
                }
                else if (kind == "remove")
                {
                    var removal = Wire.Decode<RemovalState>(json); ValidateRemovalJournal(application, file, removal);
                    if (removal.Status != "NotCommitted") Add(removal.Location, removal.Location with { LifecycleState = "RemovedDataRetained", MaintenanceOperation = file });
                }
                else if (kind == "migration")
                {
                    var restore = Wire.Decode<DeploymentRestoreCommit>(json); ValidateRestoreCommit(restore, application, file);
                    Add(restore.Previous, restore.Next);
                }
                else
                {
                    var rollback = Wire.Decode<DeploymentRollbackRecord>(json);
                    if (rollback.SchemaVersion != 1 || rollback.Plan.OperationId != operation || rollback.Plan.Current.ApplicationId != application || rollback.Target.ApplicationId != application)
                        throw new InvalidDataException("原回滚部署关系无效。");
                    if (rollback.Status != "NotCommitted") Add(rollback.Plan.Current, rollback.Target);
                }
            }
        }
        foreach (var pending in incompleteLocations)
        {
            using var document = JsonDocument.Parse(pending.Json);
            var targets = edges.Select(e => e.Previous).Where(l => l.LifecycleState == "Installed" && InstallationLocationMatches(pending.Request, document.RootElement, l, pending.Identity))
                .DistinctBy(DeploymentNode).ToArray();
            if (targets.Length == 1) Add(pending.Previous, targets[0]);
        }
        return edges.ToArray();
        void Add(DeploymentLocation previous, DeploymentLocation next)
        {
            if (previous.ApplicationId != application || next.ApplicationId != application || previous.RuntimeAccountSid != next.RuntimeAccountSid)
                throw new InvalidDataException("部署关系的应用或账户身份冲突。");
            _ = DeploymentNode(previous); _ = DeploymentNode(next); edges.Add(new(previous, next));
        }
    }

    internal static string DeploymentNode(DeploymentLocation value)
    {
        if (value.SourceCommit is not { Length: 40 } || !value.SourceCommit.All(char.IsAsciiHexDigit) ||
            !Path.IsPathFullyQualified(value.ReleaseRoot) || !Path.IsPathFullyQualified(value.DataRoot))
            throw new InvalidDataException("部署关系缺少可核验的身份。");
        return Wire.Hash(Wire.Encode(new { value.ApplicationId, value.SourceCommit,
            Program = Path.GetFullPath(value.ReleaseRoot).ToUpperInvariant(), Data = Path.GetFullPath(value.DataRoot).ToUpperInvariant(),
            value.RuntimeAccountSid, value.DeploymentPlanIdentity, Installed = value.LifecycleState == "Installed" }));
    }

    internal static bool DeploymentPrecedes(DeploymentLocation original, DeploymentLocation? current, DeploymentTransition[] history)
        => DeploymentDistance(original, current, history) is > 0;

    internal static int? DeploymentDistance(DeploymentLocation original, DeploymentLocation? current, DeploymentTransition[] history)
    {
        if (current is null) return null;
        var target = DeploymentNode(current); var first = DeploymentNode(original);
        if (first == target) return 0;
        var seen = new HashSet<string>(StringComparer.Ordinal) { first }; var queue = new Queue<(string Node, int Distance)>(); queue.Enqueue((first, 0));
        while (queue.TryDequeue(out var item))
            foreach (var edge in history.Where(e => DeploymentNode(e.Previous) == item.Node))
            {
                var next = DeploymentNode(edge.Next);
                if (next == target) return item.Distance + 1;
                if (seen.Add(next)) queue.Enqueue((next, item.Distance + 1));
            }
        return null;
    }


}
