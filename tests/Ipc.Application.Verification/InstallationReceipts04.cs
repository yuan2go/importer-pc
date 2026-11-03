using System.Text.Json;
using Ipc.Platform;

// IE-P04 installation receipts in the diagnostics package: synthetic journals in a temporary
// deployments folder; only allowlisted fields may leave them.
internal static class InstallationReceipts04
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS RECEIPTS04 " + text); }
    internal static void Run(string root)
    {
        var folder = Path.Combine(root, "installation-receipts-04", "deployments");
        Check(DeploymentReceipts.Summarize(folder, "sportmaster").All(s => s.Count == 0 && s.Latest is null), "a station without journals reports no receipts");
        Directory.CreateDirectory(folder);
        string Op() => Guid.NewGuid().ToString("N");
        void Write(string kind, string operation, object value) => File.WriteAllText(Path.Combine(folder, $"sportmaster.{kind}-{operation}.json"), JsonSerializer.Serialize(value));

        var oldSetup = Op(); var newSetup = Op();
        Write("setup", oldSetup, new { Status = "Committed", ReleaseRoot = @"D:\Secret\releases\a", DataRoot = @"D:\Data" });
        File.SetLastWriteTimeUtc(Path.Combine(folder, $"sportmaster.setup-{oldSetup}.json"), DateTime.UtcNow.AddDays(-2));
        Write("setup", newSetup, new { Status = "Prepared", ReleaseRoot = @"D:\Secret\releases\b", RequestIdentity = new string('b', 64) });
        var quiet = Op();
        Write("quiet", quiet, new { Schema = 1, Status = "Completed", ExitCode = 0, LogSha256 = new string('c', 64),
            Intent = new { ApplicationId = "sportmaster", OperationId = quiet, Action = "Repair", LogFile = @"D:\Logs\secret.log" } });
        var rollback = Op();
        Write("rollback", rollback, new { SchemaVersion = 1, Status = "Failed", At = "2026-10-01T08:00:00+00:00", ErrorCode = "x", Plan = new { Identity = "p" } });
        Write("prune", Op(), new { Status = "Deleted; rm -rf", At = "2026-10-02T08:00:00+00:00" });
        Write("remove", Op(), "{ not json");
        File.WriteAllText(Path.Combine(folder, "sportmaster.remove-not-a-guid.json"), "{}");
        File.WriteAllText(Path.Combine(folder, $"sportmaster.setup-{Op().ToUpperInvariant()}.json"), "{\"Status\":\"Committed\"}");
        File.WriteAllText(Path.Combine(folder, $"direct-plc.setup-{Op()}.json"), "{\"Status\":\"Committed\"}");

        var summary = DeploymentReceipts.Summarize(folder, "sportmaster").ToDictionary(s => s.Kind);
        var setup = summary["setup"];
        Check(setup.Latest is { Status: "Prepared" } latest && latest.OperationId == newSetup, "the newest setup journal is the latest receipt");
        Check(summary["quiet"].Latest is { Status: "Completed", Action: "Repair", ExitCode: 0 }, "a quiet receipt keeps its action and exit code");
        Check(summary["rollback"].Latest is { Status: "Failed" } r && r.At == DateTimeOffset.Parse("2026-10-01T08:00:00+00:00"), "a recorded time wins over the file time");
        Check(summary["prune"].Latest is null && summary["prune"].Unreadable == 1, "a status that is not a plain word is not copied out");
        Check(summary["remove"].Latest is null && summary["remove"].Count == 1 && summary["remove"].Unreadable == 1, "a malformed journal counts as unreadable and a non-operation file is ignored");
        var json = JsonSerializer.Serialize(summary.Values);
        Check(!json.Contains("Secret") && !json.Contains("Logs") && !json.Contains(new string('b', 64)) && !json.Contains(new string('c', 64)) && !json.Contains("Identity"),
            "paths, hashes and plans never leave the journals");
        Check(setup.Count == 2 && setup.Unreadable == 0 && DeploymentReceipts.Summarize(folder, "direct-plc")[0].Count == 1,
            "another application's journals and non-canonical operation names are not counted");
        var refused = false; try { DeploymentReceipts.Summarize(folder, "../x"); } catch (ArgumentException) { refused = true; }
        Check(refused, "an application id that could escape the folder is refused");
    }
}
