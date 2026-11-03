using System.Text.Json;

namespace Ipc.Platform;

// Read-only summary of the deployment journals for the diagnostics package. Only the
// allowlisted fields below leave the protected journal: kind, operation id, status, time,
// action and exit code. Paths, accounts, hashes, plans and file lists never do.
public sealed record DeploymentReceipt(string Kind, string OperationId, string Status, DateTimeOffset At, string? Action = null, int? ExitCode = null);
public sealed record DeploymentReceiptSummary(string Kind, int Count, DeploymentReceipt? Latest, int Unreadable);

public static class DeploymentReceipts
{
    public static readonly string[] Kinds = ["setup", "quiet", "remove", "rollback", "prune", "purge"];
    const int MaximumBytes = 1024 * 1024;
    const int MaximumFiles = 1025;

    public static DeploymentReceiptSummary[] Summarize(string application) =>
        Summarize(Path.Combine(WindowsStoragePaths.MachineRoot, "deployments"), application);

    // directory: <machine root>\deployments. A missing directory reads as no receipts.
    public static DeploymentReceiptSummary[] Summarize(string directory, string application)
    {
        if (!ExtensionSchema.IsId(application)) throw new ArgumentException("应用标识无效。", nameof(application));
        return Kinds.Select(kind =>
        {
            if (!Directory.Exists(directory)) return new DeploymentReceiptSummary(kind, 0, null, 0);
            var prefix = application + "." + kind + "-";
            var files = Directory.EnumerateFiles(directory, prefix + "*.json").Take(MaximumFiles)
                .Where(file => OperationOf(file, prefix) is not null).ToArray();
            var receipts = files.Select(file => Read(file, kind, OperationOf(file, prefix)!)).ToArray();
            var latest = receipts.OfType<DeploymentReceipt>().OrderByDescending(r => r.At).ThenBy(r => r.OperationId, StringComparer.Ordinal).FirstOrDefault();
            return new DeploymentReceiptSummary(kind, files.Length, latest, receipts.Count(r => r is null));
        }).ToArray();
    }

    static string? OperationOf(string file, string prefix)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        if (!name.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var operation = name[prefix.Length..];
        return Guid.TryParseExact(operation, "N", out var parsed) && parsed != Guid.Empty && parsed.ToString("N") == operation ? operation : null;
    }

    static DeploymentReceipt? Read(string file, string kind, string operation)
    {
        try
        {
            var info = new FileInfo(file);
            if (info.Length > MaximumBytes) return null;
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Status", out var status) || status.ValueKind != JsonValueKind.String) return null;
            var text = status.GetString()!;
            if (text.Length is 0 or > 40 || !text.All(char.IsAsciiLetterOrDigit)) return null;
            var at = root.TryGetProperty("At", out var time) && time.ValueKind == JsonValueKind.String && time.TryGetDateTimeOffset(out var stamp)
                ? stamp : new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            int? exit = root.TryGetProperty("ExitCode", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var value) ? value : null;
            string? action = kind == "quiet" && root.TryGetProperty("Intent", out var intent) && intent.ValueKind == JsonValueKind.Object &&
                intent.TryGetProperty("Action", out var verb) && verb.GetString() is "Install" or "Repair" or "Uninstall" ? verb.GetString() : null;
            return new DeploymentReceipt(kind, operation, text, at, action, exit);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
}
