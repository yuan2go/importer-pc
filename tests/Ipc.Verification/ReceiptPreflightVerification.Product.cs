using System.Text.Json.Nodes;
using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

static partial class ReceiptPreflightVerification
{
    public static async Task Product(string root, string url)
    {
        await using var endpoint = new OperationClosureVerification.QueryEndpoint(url, gateQueries: false);
        foreach (var kind in new[] { "effective-fence", "pending-valid-fence", "pending-invalid-fence", "raw-bad-hash", "pending-invalid-raw-bad-hash" })
        {
            var config = Rig.Config(endpoint.Url, timeout: 10000);
            var boot = Boot(Path.Combine(root, "product-preflight-" + kind), config);
            var directory = Path.GetDirectoryName(boot.Station)!;
            await Crash(directory, config, kind.EndsWith("raw-bad-hash", StringComparison.Ordinal) ? "ReceiptFlushed" : "ConfigSeeded", "product-" + kind);
            if (kind.EndsWith("raw-bad-hash", StringComparison.Ordinal))
            {
                var file = Directory.GetFiles(Path.Combine(directory, "receipt-ingress")).Single();
                var raw = JsonNode.Parse(File.ReadAllText(file))!; raw["Response"]!["PayloadHash"] = new string('0', 64);
                File.WriteAllText(file, raw.ToJsonString());
            }
            else ExtensionStore.AtomicWrite(Path.Combine(directory, "receipt-owner-fence.json"), Array.Empty<string>());
            var before = Snapshot(directory); var executions = endpoint.ExecuteCalls; var queries = endpoint.QueryCalls; var factories = 0;
            await using (var runtime = new ProductRuntime(boot, NullLogger<ProductRuntime>.Instance, (_, _) =>
                { factories++; throw new InvalidOperationException("Rejected startup must not create a source"); }))
            {
                await runtime.StartAsync(CancellationToken.None);
                var expected = kind.EndsWith("raw-bad-hash", StringComparison.Ordinal) ? "Response identity/hash/classification" : "Unsupported receipt fence";
                Check(runtime.Application is null && runtime.SourceStartAttempts == 0 && factories == 0 &&
                    runtime.Diagnostics.Any(x => x.Contains(expected, StringComparison.Ordinal)),
                    kind + " reaches the receipt guard through actual Product startup: " + string.Join("; ", runtime.Diagnostics));
            }
            CheckUnchanged(directory, before, endpoint, executions, queries, "Product " + kind);
            using var dataProbe = new ResourceLease(directory, ["database"]);
            using var sourceProbe = new ResourceLease(boot.LeaseRoot!, ProductRuntime.ConfigurationResources(config));
            Check(true, kind + " failed startup disposal releases data and resource leases");
        }

        foreach (var kind in new[] { "reserved", "committed", "unknown", "pending-valid", "pending-invalid", "new" })
        {
            var config = Rig.Config(endpoint.Url, timeout: 10000);
            var boot = Boot(Path.Combine(root, "product-preflight-valid-" + kind), config);
            var directory = Path.GetDirectoryName(boot.Station)!;
            var point = kind switch { "reserved" => "ReceiptFlushed", "committed" => "T3Committed", "unknown" => "AfterT2", _ => "ConfigSeeded" };
            if (kind != "new") await Crash(directory, config, point, "product-" + kind);
            var database = Path.Combine(directory, "station.db");
            var original = kind is "reserved" or "committed" or "unknown" ? ReadRequest(database) : null;
            if (original is not null) Check(original.Implementation is not null, kind + " uses a genuine frozen Product implementation identity");
            var executions = endpoint.ExecuteCalls; var queries = endpoint.QueryCalls;
            await using (var runtime = new ProductRuntime(boot, NullLogger<ProductRuntime>.Instance))
            {
                await runtime.StartAsync(CancellationToken.None);
                Check(runtime.Application is not null && runtime.Diagnostics.Count == 0 && runtime.SourceStartAttempts == 2 &&
                    runtime.Configuration?.Version == (kind == "pending-valid" ? "preflight-next" : config.Version),
                    kind + " actual Product startup preserves configuration selection: " + string.Join("; ", runtime.Diagnostics));
                if (original is not null)
                {
                    var current = ReadRequest(database);
                    Check(current.Status == (kind == "unknown" ? RequestStatus.Unknown : RequestStatus.Accepted) &&
                        Wire.Encode(original with { Status = current.Status }) == Wire.Encode(current) &&
                        await ((StationApplication)runtime.Application!).ClaimAsync() is null,
                        kind + " retains original identity/payload/target and cannot be blindly claimed again");
                    Check(ReadCount(database, "responses") == (kind == "unknown" ? 0 : 1) &&
                        (!Directory.Exists(Path.Combine(directory, "receipt-ingress")) || !Directory.EnumerateFiles(Path.Combine(directory, "receipt-ingress")).Any()),
                        kind + " preserves the exact response cardinality and handles its original raw custody");
                }
            }
            var final = SupplyStore.ReadStartupState(database);
            Check(final.Pending is null && final.Effective?.Content.Version == (kind == "pending-valid" ? "preflight-next" : config.Version) &&
                endpoint.ExecuteCalls == executions && endpoint.QueryCalls == queries,
                kind + " effective/fallback result persists with zero Execute/Query during startup and stop");
        }

        var leaseConfig = Rig.Config(endpoint.Url, timeout: 10000);
        var leaseBoot = Boot(Path.Combine(root, "product-preflight-lease-conflict"), leaseConfig);
        var leaseDirectory = Path.GetDirectoryName(leaseBoot.Station)!;
        await Crash(leaseDirectory, leaseConfig, "ConfigSeeded", "product-pending-valid");
        var leaseBefore = Snapshot(leaseDirectory); var callsBefore = endpoint.ExecuteCalls; var queriesBefore = endpoint.QueryCalls;
        using (var blocker = new ResourceLease(leaseBoot.LeaseRoot!, ProductRuntime.ConfigurationResources(leaseConfig)))
        {
            await using var runtime = new ProductRuntime(leaseBoot, NullLogger<ProductRuntime>.Instance);
            await runtime.StartAsync(CancellationToken.None);
            Check(runtime.Application is null && runtime.SourceStartAttempts == 0 && runtime.Diagnostics.Any(x => x.Contains("IOException", StringComparison.Ordinal)),
                "final resource contention remains NotReady before writer/activation");
        }
        CheckUnchanged(leaseDirectory, leaseBefore, endpoint, callsBefore, queriesBefore, "preflight resource contention");
        await using (var retry = new ProductRuntime(leaseBoot, NullLogger<ProductRuntime>.Instance))
        {
            await retry.StartAsync(CancellationToken.None);
            Check(retry.Application is not null && retry.Diagnostics.Count == 0 && retry.Configuration?.Version == "preflight-next",
                "unchanged Pending can activate after the original resource contention ends");
        }
    }
    static BootOptions Boot(string area, StationConfig config)
    {
        Directory.CreateDirectory(area);
        var definition = ReferenceProduct.Definition; var install = Path.GetDirectoryName(typeof(ProductRuntime).Assembly.Location)!;
        var manifest = new ProductManifest(1, definition.ProductId, config.Mode, definition.Runtime, "any", definition.BusinessModule, definition.Views,
            definition.Modules.Select(x => new ModuleSpec(x.Id, x.Kind, x.Version, x.ContractMajor, x.Dependencies, x.File, Wire.FileHash(Path.Combine(install, x.File)))).ToArray());
        var data = Path.Combine(area, "data"); var directory = Path.Combine(data, config.Mode.ToString(), config.StationId);
        Directory.CreateDirectory(directory);
        ExtensionStore.AtomicWrite(Path.Combine(area, "product.json"), manifest);
        ExtensionStore.AtomicWrite(Path.Combine(area, "presentation.json"), new PresentationSettings());
        ExtensionStore.AtomicWrite(Path.Combine(directory, "config.json"), config);
        return new(Path.Combine(area, "product.json"), Path.Combine(directory, "config.json"), Path.Combine(area, "presentation.json"), data, install, Path.Combine(area, "leases"));
    }
    static BusinessRequest ReadRequest(string database)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT json FROM requests";
        return Wire.Decode<BusinessRequest>((string)command.ExecuteScalar()!);
    }
    static int ReadCount(string database, string table)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT count(*) FROM " + table;
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
