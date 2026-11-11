using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

internal static class StabilityAnalysis
{
    static void Check(bool ok, string detail) { if (!ok) throw new InvalidOperationException(detail); Console.WriteLine("PASS ANALYSIS " + detail); }
    public static async Task Run(string workspace, string root)
    {
        var configPath = Path.Combine(root, "config"); Directory.CreateDirectory(configPath);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json"))) with { BackupSchedule = null, Login = null };
        ExtensionStore.AtomicWrite(Path.Combine(configPath, "business.json"), config);
        var launch = new BusinessLaunch(configPath, Path.Combine(root, "data"), null, null);
        var directory = Path.Combine(launch.DataRoot, config.Mode.ToString(), config.StationId, config.ApplicationId);
        var collection = Path.Combine(root, "collection"); Directory.CreateDirectory(collection);
        var backup = Path.Combine(root, "complete.db");
        await using (var app = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } })
        {
            await app.StartAsync(default); await ManagementModeFixture.Authorize(app);
            await CameraRoundHarness.Until(async () => (await app.SnapshotAsync()).Availability!.Admit);
            var first = app.RuntimeProgress(); var started = DateTimeOffset.UtcNow; var clock = Stopwatch.StartNew();
            app.EmitReferenceAt("left", "synth-one", "SYNTH-INPUT-1", "event-one");
            await CameraRoundHarness.Until(async () => (await app.SnapshotAsync()).Operations.SingleOrDefault()?.State == ParcelState.SlotResolved && app.WorkerProgress().Sum(w => w.Processed) >= 1);
            // Same frame/source repetition is an observation, not another RCS intent.
            app.EmitReferenceAt("left", "synth-two", "SYNTH-INPUT-1", "event-two");
            await CameraRoundHarness.Until(() => Task.FromResult(app.WorkerProgress().Sum(w => w.Processed + w.ObservedOnly) >= 2));
            var ended = DateTimeOffset.UtcNow; var last = app.RuntimeProgress(); var snapshot = await app.SnapshotAsync();
            ExtensionStore.AtomicWrite(Path.Combine(collection, "identity.json"), new { RuntimeId = snapshot.RuntimeId, SourceCommit = new string('a', 40) });
            ExtensionStore.AtomicWrite(Path.Combine(collection, "collection.json"), new
            {
                RuntimeId = snapshot.RuntimeId,
                SourceCommit = new string('a', 40),
                NormalStart = started,
                NormalEnd = ended,
                ActualNormalSeconds = clock.Elapsed.TotalSeconds,
                Status = "SMOKE_ONLY",
                FirstNormal = first,
                LastNormal = last,
                Failures = new { },
                Ui = Array.Empty<object>()
            });
            File.WriteAllText(Path.Combine(collection, "samples.jsonl"), JsonSerializer.Serialize(new { Progress = last }, new JsonSerializerOptions(Wire.Json) { WriteIndented = false }) + "\n");
            snapshot = await app.SnapshotAsync(); await app.CommandAsync(new(Wire.Id(), snapshot.Revision, "Pause"));
            BusinessReceipt? receipt = null;
            for (var i = 0; i < 40; i++)
            {
                receipt = await app.BackupAsync(backup); if (receipt.Status == "BackupCreated") break;
                await Task.Delay(20);
            }
            Check(receipt?.Status == "BackupCreated", "real Application/Repository generated the DB13 complete analysis input");
        }
        var original = Inventory(backup);
        var output = Path.Combine(root, "analysis.json");
        var (exit, text) = await Analyze(workspace, collection, backup, output);
        Check(exit == 0 && File.Exists(output), "actual Python analysis entry accepts current production verifier contract" + (exit == 0 ? "" : ": " + text));
        using (var report = JsonDocument.Parse(File.ReadAllText(output)))
        {
            var counters = report.RootElement.GetProperty("Counters");
            Check(counters.GetProperty("RcsFacts").GetProperty("FrozenNewIntents").GetInt32() == 1 &&
                counters.GetProperty("PhysicalPlacements").ValueKind == JsonValueKind.Null && report.RootElement.GetProperty("CollectionStatus").GetString() == "SMOKE_ONLY" &&
                report.RootElement.GetProperty("RoundCounts").EnumerateArray().Single().EnumerateArray().Select(x => x.ToString()).SequenceEqual(["left", "1", "1"]),
                "duplicate observations remain one request intent; missing placement evidence is null and smoke identity is retained");
            Check(report.RootElement.GetProperty("SourceDeliveryReconciliation").GetProperty("Status").GetString() == "NOT_RECONCILED" &&
                report.RootElement.GetProperty("LatencyMilliseconds").GetProperty("left").GetProperty("input_queue_decision_and_handoff_ms").GetProperty("Samples").GetInt32() >= 1,
                "analysis explicitly reports source gaps and computes paired input timing, never adds phase percentiles");
        }
        Check(original.OrderBy(x => x.Key).SequenceEqual(Inventory(backup).OrderBy(x => x.Key)), "successful analysis does not modify the source recovery set");
        var paired = Path.Combine(root, "paired.db"); CopySet(backup, paired);
        using (var db = new SqliteConnection("Data Source=" + paired + ";Pooling=False"))
        {
            db.Open(); using var timing = db.CreateCommand();
            // A deliberately anti-correlated synthetic timing distribution. The
            // paired P95 is 110ms; adding the phase P95s would incorrectly yield 200ms.
            timing.CommandText = "UPDATE parcel_ingress SET queue_ms=CASE WHEN id=(SELECT min(id) FROM parcel_ingress) THEN 10 ELSE 100 END,processing_ms=CASE WHEN id=(SELECT min(id) FROM parcel_ingress) THEN 100 ELSE 10 END";
            timing.ExecuteNonQuery();
        }
        var pairedManifest = paired + ".recovery/COMPLETED.json";
        var pairedNode = JsonNode.Parse(File.ReadAllText(pairedManifest))!;
        pairedNode["Database"] = Path.GetFileName(paired); pairedNode["DatabaseHash"] = Wire.FileHash(paired);
        File.WriteAllText(pairedManifest, pairedNode.ToJsonString());
        var pairedOutput = Path.Combine(root, "paired-analysis.json");
        var pairedResult = await Analyze(workspace, collection, paired, pairedOutput);
        Check(pairedResult.Exit == 0, "paired synthetic timing backup passes real analysis");
        using (var pairedReport = JsonDocument.Parse(File.ReadAllText(pairedOutput)))
            Check(pairedReport.RootElement.GetProperty("LatencyMilliseconds").GetProperty("left").GetProperty("input_queue_decision_and_handoff_ms").GetProperty("P95").GetDouble() == 110,
                "anti-correlated phases yield measured paired P95=110ms, not summed phase P95=200ms");
        foreach (var defect in new[] { "missing", "tampered", "version", "forged-schema", "application", "configuration", "path", "active", "wrong-runtime", "input-hash" })
        {
            var variant = Path.Combine(root, defect + ".db"); CopySet(backup, variant);
            var manifestPath = variant + ".recovery/COMPLETED.json";
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!; manifest["Database"] = Path.GetFileName(variant);
            var configFile = variant + ".recovery/configuration.json";
            if (defect == "missing") File.Delete(configFile);
            if (defect == "tampered") File.AppendAllText(configFile, "tampered");
            if (defect == "configuration")
            {
                var node = JsonNode.Parse(File.ReadAllText(configFile))!; node["Effective"]!["ApplicationId"] = "direct-plc";
                File.WriteAllText(configFile, node.ToJsonString()); manifest["Files"]!["configuration.json"] = Wire.FileHash(configFile);
            }
            if (defect == "path") manifest["Files"]!["receipts/../escape.json"] = new string('A', 64);
            if (defect == "active") File.WriteAllText(variant + "-wal", "retained sidecar");
            if (defect is "version" or "forged-schema" or "application" or "wrong-runtime" or "input-hash")
            {
                using var db = new SqliteConnection("Data Source=" + variant + ";Pooling=False"); db.Open(); using var command = db.CreateCommand();
                command.CommandText = defect switch
                {
                    "version" => "UPDATE platform_migrations SET version=11",
                    "forged-schema" => "DROP TABLE parcel_ingress_audits",
                    "application" => "UPDATE platform_migrations SET application='direct-plc'",
                    "wrong-runtime" => "UPDATE parcel_ingress SET session='foreign-runtime'",
                    _ => "UPDATE parcel_ingress SET body_hash='CHANGED'"
                }; command.ExecuteNonQuery();
            }
            manifest["DatabaseHash"] = Wire.FileHash(variant); File.WriteAllText(manifestPath, manifest.ToJsonString());
            var before = Inventory(variant); var refused = Path.Combine(root, defect + "-analysis.json");
            var attempt = await Analyze(workspace, collection, variant, refused);
            Check(attempt.Exit != 0 && !File.Exists(refused) && !File.Exists(Path.Combine(root, defect + "-analysis-input-dispositions.jsonl")) &&
                before.OrderBy(x => x.Key).SequenceEqual(Inventory(variant).OrderBy(x => x.Key)), defect + " refuses without source changes or publishing a successful/partial analysis");
        }
    }
    static Dictionary<string, string> Inventory(string database) => new[] { database }.Concat(Directory.GetFiles(database + ".recovery", "*", SearchOption.AllDirectories))
        .Concat(new[] { database + "-wal", database + "-shm", database + "-journal" }.Where(File.Exists)).ToDictionary(p => p, p => Wire.FileHash(p));
    static void CopySet(string source, string target)
    {
        File.Copy(source, target); Directory.CreateDirectory(target + ".recovery");
        foreach (var file in Directory.GetFiles(source + ".recovery", "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(target + ".recovery", Path.GetRelativePath(source + ".recovery", file)); Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Copy(file, to);
        }
    }
    static async Task<(int Exit, string Text)> Analyze(string workspace, string collection, string backup, string output)
    {
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "python" : "python3") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { Path.Combine(workspace, "scripts/stability-qualification.py"), "analyze", "--collection", collection, "--database", backup, "--output", output, "--verifier", typeof(StabilityAnalysis).Assembly.Location }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40)); return (process.ExitCode, await stdout + await stderr);
    }
}
