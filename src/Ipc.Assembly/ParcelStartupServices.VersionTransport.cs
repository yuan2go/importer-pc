using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Assembly;

public static partial class ParcelStartupServices
{
    internal static string MaintenanceFailureStatus(Exception error) => error is InstallationOutcomeUnknownException ? "Unknown" : "Failed";
    internal static bool CancellableMaintenance(string action) => action is "ResumeHistoricalAssets" or "Backup" or "Restore" or "PlanChange" or "PrepareChange" or "Install" or "InstallWithPreflight" or "RepairSystem" or
        "ReviewRollback" or "Rollback" or "ReviewVersionPruning" or "PruneVersions" or "ResumeVersionPruning" or "SimpleUninstall" or "MediaUninstall" or "MediaConcludeInstallation" or "MediaRepairInstallation";
    internal static (string Status, string? Error, string? Result) BoundMaintenanceTerminal(string status, string hash, string? error, string? result)
    {
        error = error is null ? null : error[..Math.Min(error.Length, 4000)];
        if (JsonSerializer.Serialize(new { Status = status, RequestSha256 = hash, Error = error, Result = result }).Length <= 65536)
            return (status, error, result);
        return ("Unknown", "维护结果超出传输预算；原完整回执保留，请核对原操作，不能重复提交。", null);
    }

    internal static StartupVersionPruneReview VersionPruningReviewSummary(StartupVersionPruneReview review)
    {
        // Review only needs roots/counts and the server-computed full identity.
        // Commit re-derives the complete inventory from protected originals.
        var summary = review with
        {
            Deployment = review.Deployment with
            {
                Delete = review.Deployment.Delete.Select(d => d with { Files = [], ReviewedFileCount = d.Files.Count }).ToArray()
            }
        };
        if (BoundMaintenanceTerminal("Completed", new string('0', 64), null, Wire.Encode(summary)).Status != "Completed")
            throw new IOException("版本目录过多或路径过长，超出审阅预算；所有版本保留，请由实施人员核对。");
        return summary;
    }

    internal static DeploymentPruneReceipt VersionPruningReceiptSummary(DeploymentPruneReceipt receipt)
    {
        var selected = new List<string>();
        foreach (var item in receipt.Remaining.Take(256))
        {
            selected.Add(item);
            var candidate = receipt with { Remaining = selected.ToArray(), TotalRemaining = receipt.Remaining.Length, RemainingTruncated = true };
            if (BoundMaintenanceTerminal("Completed", new string('0', 64), null, Wire.Encode(candidate)).Status != "Completed")
            { selected.RemoveAt(selected.Count - 1); break; }
        }
        return receipt with { Remaining = selected.ToArray(), TotalRemaining = receipt.Remaining.Length, RemainingTruncated = selected.Count != receipt.Remaining.Length };
    }
}
