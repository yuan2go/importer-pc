using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;
using Microsoft.Data.Sqlite;

static partial class ReceiptPreflightVerification
{
    public static async Task Run(string root, string url)
    {
        await using var endpoint = new OperationClosureVerification.QueryEndpoint(url, gateQueries: false);
        foreach (var kind in new[] { "empty-fence", "fence", "missing-contract", "bad-json", "wrong-file", "wrong-request", "wrong-attempt", "bad-hash", "missing-reservation", "committed-mismatch", "committed-hash" })
        {
            var directory = Path.Combine(root, "receipt-preflight-" + kind);
            var config = Rig.Config(endpoint.Url, timeout: 10000);
            await Crash(directory, config, kind.StartsWith("committed", StringComparison.Ordinal) ? "T3Committed" : "ReceiptFlushed",
                kind == "committed-hash" ? "corrupt-committed-hash" : "supply");
            var database = Path.Combine(directory, "station.db");
            var file = Directory.GetFiles(Path.Combine(directory, "receipt-ingress")).Single();
            var raw = JsonNode.Parse(File.ReadAllText(file))!;
            var expected = kind switch
            {
                "empty-fence" or "fence" => "Unsupported receipt fence",
                "missing-contract" => "requires its original request contract",
                "wrong-file" => "custody requires inspection",
                "wrong-request" => "no original request",
                "wrong-attempt" => "send attempt identity differs",
                "bad-hash" => "identity/hash/classification",
                "missing-reservation" => "reservation missing",
                _ => "committed raw identity differs"
            };
            switch (kind)
            {
                case "empty-fence": case "fence":
                    ExtensionStore.AtomicWrite(Path.Combine(directory, "receipt-owner-fence.json"), kind == "empty-fence" ? Array.Empty<string>() : new[] { "synthetic-owner" }); break;
                case "bad-json": File.WriteAllText(file, "{"); break;
                case "wrong-file": File.Move(file, Path.Combine(Path.GetDirectoryName(file)!, "wrong-name.json")); break;
                case "wrong-request": raw["RequestId"] = "unknown-request"; break;
                case "wrong-attempt": raw["Attempt"]!["Ordinal"] = raw["Attempt"]!["Ordinal"]!.GetValue<int>() + 1; break;
                case "bad-hash": raw["Response"]!["PayloadHash"] = new string('0', 64); break;
                case "missing-reservation":
                    raw["Id"] = "zz-unreserved"; File.WriteAllText(Path.Combine(Path.GetDirectoryName(file)!, "zz-unreserved.json"), raw.ToJsonString()); break;
                case "committed-mismatch": raw["Response"]!["Reason"] = "different raw receipt"; break;
            }
            if (kind is "wrong-request" or "wrong-attempt" or "bad-hash" or "committed-mismatch") File.WriteAllText(file, raw.ToJsonString());
            var before = Snapshot(directory); var executions = endpoint.ExecuteCalls; var queries = endpoint.QueryCalls;
            var refused = false;
            try { using var invalid = new SupplyStore(database, kind == "missing-contract" ? null : new SimulationRequestContract(config)); }
            catch (JsonException) when (kind == "bad-json") { refused = true; }
            catch (InvalidDataException error) when (kind != "bad-json" && error.Message.Contains(expected, StringComparison.Ordinal)) { refused = true; }
            Check(refused, kind + " rejected by the intended original-receipt guard");
            CheckUnchanged(directory, before, endpoint, executions, queries, kind);
        }
        foreach (var point in new[] { "ReceiptFlushed", "T3Committed" })
        {
            var directory = Path.Combine(root, "receipt-preflight-valid-" + point); var config = Rig.Config(endpoint.Url, timeout: 10000);
            await Crash(directory, config, point);
            var executions = endpoint.ExecuteCalls; var queries = endpoint.QueryCalls;
            await using var restored = await Rig.Open(directory, config);
            var request = restored.Store.Requests().Single();
            Check(request.Status == RequestStatus.Accepted && restored.Store.Responses(request.Id).Length == 1 &&
                !Directory.EnumerateFiles(Path.Combine(directory, "receipt-ingress")).Any() &&
                endpoint.ExecuteCalls == executions && endpoint.QueryCalls == queries,
                point + " valid original restores exactly once without Execute or Query");
        }
        var orphan = Path.Combine(root, "receipt-preflight-orphan"); Directory.CreateDirectory(Path.Combine(orphan, "receipt-ingress"));
        File.WriteAllText(Path.Combine(orphan, "receipt-ingress", "original.json"), "{}");
        var orphanHash = Wire.FileHash(Path.Combine(orphan, "receipt-ingress", "original.json"));
        var orphanRefused = false;
        try { using var invalid = new SupplyStore(Path.Combine(orphan, "station.db"), new SimulationRequestContract(Rig.Config(endpoint.Url))); }
        catch (InvalidDataException error) when (error.Message.Contains("no original database", StringComparison.Ordinal)) { orphanRefused = true; }
        Check(orphanRefused && !File.Exists(Path.Combine(orphan, "station.db")) && Wire.FileHash(Path.Combine(orphan, "receipt-ingress", "original.json")) == orphanHash,
            "orphan raw cannot create an empty replacement database");
        var fenced = Path.Combine(root, "receipt-preflight-backup-fence");
        await Crash(fenced, Rig.Config(endpoint.Url), "ConfigSeeded");
        File.WriteAllText(Path.Combine(fenced, "receipt-owner-fence.json"), "[]");
        var fencedBefore = Snapshot(fenced); var executeBefore = endpoint.ExecuteCalls; var queryBefore = endpoint.QueryCalls;
        var destination = Path.Combine(fenced, "must-not-publish.db"); var backupRefused = false;
        try { SupplyStore.BackupReadOnly(Path.Combine(fenced, "station.db"), destination); }
        catch (InvalidOperationException error) when (error.Message.Contains("Recover receipt custody", StringComparison.Ordinal)) { backupRefused = true; }
        Check(backupRefused && !File.Exists(destination) && !Directory.EnumerateFiles(fenced, "*.unverified").Any(),
            "empty fence alone blocks a database-only backup before creating output");
        CheckUnchanged(fenced, fencedBefore, endpoint, executeBefore, queryBefore, "empty-fence backup");
    }
    public static async Task Child(string[] args)
    {
        var directory = args[1]; var point = args[2]; var mode = args[3];
        var config = Wire.Decode<StationConfig>(File.ReadAllText(Path.Combine(directory, "config.json")));
        await using var router = mode.StartsWith("product", StringComparison.Ordinal) ? new IntegrationRuntime(config, null) : null;
        router?.Initialize();
        var faults = new FaultInjection();
        await using var fixture = await Rig.Open(directory, config, faults, requestContract: router);
        if (router is not null)
        {
            fixture.Store.Commit(fixture.Store.Load(), "SeedEffective", config.StationId, "fixture",
                config: new(Wire.Id(), config.Version, config, "fixture", "Effective", "synthetic startup original", DateTimeOffset.UtcNow));
            if (mode.Contains("pending", StringComparison.Ordinal))
            {
                var pending = config with { Version = "preflight-next", StationId = mode.Contains("invalid", StringComparison.Ordinal) ? "wrong-station" : config.StationId };
                fixture.Store.Commit(fixture.Store.Load(), "SeedPending", config.StationId, "fixture",
                    config: new(Wire.Id(), config.Version, pending, "fixture", "PendingActivation", "synthetic startup pending", DateTimeOffset.UtcNow));
            }
        }
        if (point == "ConfigSeeded") Environment.FailFast("SIMULATION crash window ConfigSeeded");
        if (mode == "corrupt-committed-hash") faults.Hook = boundary =>
        {
            if (boundary != "T3Committed") return;
            var file = Directory.GetFiles(Path.Combine(directory, "receipt-ingress")).Single();
            var id = JsonNode.Parse(File.ReadAllText(file))!["Id"]!.GetValue<string>();
            // Deliberate on-disk corruption, not an application command: Commit correctly
            // forbids changing a final receipt. The original child writer remains open.
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = Path.Combine(directory, "station.db"), Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE receipt_occurrences SET json=json_set(json,'$.ContentSha256',$hash) WHERE id=$id";
            command.Parameters.AddWithValue("$hash", new string('0', 64)); command.Parameters.AddWithValue("$id", id);
            Check(command.ExecuteNonQuery() == 1, "synthetic committed hash corruption applied to the original row");
        };
        faults.CrashAt = point;
        await fixture.Command("OpenContext", value: "000001");
        var claimed = (await fixture.App.ClaimAsync())!.Value;
        Check(await fixture.App.BeforeSendAsync(claimed.Request, claimed.Attempt), "child original send admitted");
        await fixture.App.CompleteAsync(claimed.Request, claimed.Attempt, await fixture.Port.ExecuteAsync(claimed.Request, CancellationToken.None));
        throw new InvalidOperationException("Requested real crash boundary was not reached");
    }
    internal static async Task Crash(string directory, StationConfig config, string point, string mode = "supply")
    {
        Directory.CreateDirectory(directory);
        ExtensionStore.AtomicWrite(Path.Combine(directory, "config.json"), config);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { typeof(ReceiptPreflightVerification).Assembly.Location, "--receipt-preflight-child", directory, point, mode }) start.ArgumentList.Add(arg);
        start.Environment["DOTNET_DbgEnableMiniDump"] = "0";
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Receipt crash child did not start");
        var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25)); }
        finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); } }
        var error = await stderr;
        File.WriteAllText(Path.Combine(directory, "child-stdout.log"), await stdout);
        File.WriteAllText(Path.Combine(directory, "child-stderr.log"), error);
        var wal = Path.Combine(directory, "station.db-wal");
        var raw = Path.Combine(directory, "receipt-ingress");
        var count = Directory.Exists(raw) ? Directory.GetFiles(raw).Length : 0;
        Check(child.ExitCode != 0 && error.Contains(point, StringComparison.Ordinal) && File.Exists(wal) && new FileInfo(wal).Length > 32 &&
            count == (point is "ConfigSeeded" or "AfterT2" ? 0 : 1),
            "current Supply 6 child crashed at " + point + " with an unclosed real WAL; raw receipts=" + count);
    }
    internal sealed record OriginalSnapshot(string Files, string Rows);
    internal static OriginalSnapshot Snapshot(string directory)
    {
        var files = Wire.Encode(Originals(directory));
        var rows = LogicalRows(Path.Combine(directory, "station.db"));
        if (files != Wire.Encode(Originals(directory))) throw new InvalidOperationException("Read-only evidence reader changed original DB/WAL/raw/fence files");
        var result = new OriginalSnapshot(files, rows);
        File.WriteAllText(Path.Combine(directory, "before-originals.json"), Wire.Encode(result));
        RecordShm(directory, "before");
        return result;
    }
    internal static void CheckUnchanged(string directory, OriginalSnapshot before, OperationClosureVerification.QueryEndpoint endpoint, int executions, int queries, string label)
    {
        var rows = LogicalRows(Path.Combine(directory, "station.db"));
        var after = new OriginalSnapshot(Wire.Encode(Originals(directory)), rows);
        File.WriteAllText(Path.Combine(directory, "after-originals.json"), Wire.Encode(after));
        RecordShm(directory, "after");
        Check(before == after && endpoint.ExecuteCalls == executions && endpoint.QueryCalls == queries,
            label + " retains DB/WAL/raw/fence existence, length, hashes and original/config/audit rows; zero Execute/Query");
    }
    static void RecordShm(string directory, string phase)
    {
        var file = Path.Combine(directory, "station.db-shm");
        File.WriteAllText(Path.Combine(directory, phase + "-shm.json"), Wire.Encode(new { Exists = File.Exists(file), Length = File.Exists(file) ? new FileInfo(file).Length : 0, Sha256 = File.Exists(file) ? Wire.FileHash(file) : null }));
    }
    internal static object[] Originals(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .Where(file => Path.GetFileName(file) is "station.db" or "station.db-wal" or "receipt-owner-fence.json" or "config.json" || Path.GetFileName(Path.GetDirectoryName(file)) == "receipt-ingress")
        .Order(StringComparer.Ordinal).Select(file => (object)new { File = Path.GetRelativePath(directory, file), Length = new FileInfo(file).Length, Sha256 = Wire.FileHash(file) }).ToArray();
    internal static string LogicalRows(string database)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        var tables = new Dictionary<string, object[]>();
        foreach (var table in new[] { "state", "requests", "attempts", "receipt_occurrences", "configs", "audit", "outbox", "responses", "response_conflicts", "acceptance" })
        {
            using var command = connection.CreateCommand(); command.CommandText = "SELECT * FROM " + table + " ORDER BY rowid";
            using var reader = command.ExecuteReader(); var rows = new List<object>();
            while (reader.Read()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i)).ToArray());
            tables[table] = rows.ToArray();
        }
        return Wire.Encode(tables);
    }
    static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS RECEIPT-PREFLIGHT " + message); }
}
