using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;

static class CurrentContractVerification
{
    public static async Task Run(string root, string url)
    {
        await using (var fixture = await Rig.New(root, url))
        {
            fixture.AutoConfirmSyntheticHealth = false;
            var config = fixture.ConfigValue; var now = DateTimeOffset.UtcNow;
            var fact = new IntegrationHealthFact("builtin.reference", "requests", config.Version, new string('B', 64), config.Integration,
                Wire.Id(), 1, ServiceStatus.Ready, now, now.AddSeconds(5), null);
            await fixture.App.SetIntegrationHealthAsync([fact]);
            var ready = await fixture.App.SnapshotAsync();
            Check(ready.State.IntegrationHealth is null && ready.State.IntegrationServiceHealth is { } service && service.IsValid(now) &&
                service.ConfirmedAt == fact.ConfirmedAt && service.ValidUntil == fact.ValidUntil && ready.State.ServiceHealth["requests"] == fact &&
                !ready.Admit.Reasons.Any(x => x.StartsWith("Wcs", StringComparison.Ordinal)),
                "current builtin requests health reaches the same service authority and guard as an extension, retaining exact source validity");
            var opened = await fixture.Command("OpenContext", value: "000001");
            Check(opened.RequestId is not null && fixture.Store.Requests().Length == 1, "current builtin health permits one explicit original context request");
            now = DateTimeOffset.UtcNow;
            await fixture.App.SetIntegrationHealthAsync([fact with { Sequence = 2, ConfirmedAt = now, ValidUntil = now.AddSeconds(5), Status = ServiceStatus.Unavailable }]);
            var unavailable = await fixture.App.SnapshotAsync();
            Check(unavailable.State.IntegrationServiceHealth?.Status == ServiceStatus.Unavailable && unavailable.Admit.Reasons.Any(x => x.StartsWith("Wcs", StringComparison.Ordinal)),
                "builtin unavailability still blocks the current business guard");
        }
        foreach (var kind in new[] { "fence", "missing-reservation" })
        {
            var directory = Path.Combine(root, "current-refusal-" + kind);
            var config = Rig.Config(url);
            BusinessRequest original; SendAttempt attempt; IntegrationResponse response;
            string? validRaw = null; string? validRawHash = null;
            await using (var fixture = await Rig.Open(directory, config))
            {
                await fixture.Command("OpenContext", value: "000001");
                var claimed = (await fixture.App.ClaimAsync())!.Value;
                original = claimed.Request; attempt = claimed.Attempt;
                response = await fixture.Port.ExecuteAsync(original, CancellationToken.None);
                if (kind == "missing-reservation")
                {
                    var reserved = await fixture.App.BeginReceiptAsync(original, attempt, "send");
                    validRaw = Path.Combine(directory, "receipt-ingress", reserved + ".json");
                    ExtensionStore.AtomicWrite(validRaw, new { Id = reserved, RequestId = original.Id, Attempt = attempt, Response = response, ReceivedAt = DateTimeOffset.UtcNow });
                    validRawHash = Wire.FileHash(validRaw);
                }
            }
            var file = kind == "fence" ? Path.Combine(directory, "receipt-owner-fence.json") : Path.Combine(directory, "receipt-ingress", Wire.Id() + ".json");
            var id = Path.GetFileNameWithoutExtension(file);
            if (kind == "fence") ExtensionStore.AtomicWrite(file, new[] { original.Id });
            else ExtensionStore.AtomicWrite(file, new { Id = id, RequestId = original.Id, Attempt = attempt, Response = response, ReceivedAt = DateTimeOffset.UtcNow });
            var database = Path.Combine(directory, "station.db");
            var databaseHash = Wire.FileHash(database); var fileHash = Wire.FileHash(file);
            using (var store = new SupplyStore(database))
            {
                var originalBytes = Wire.Encode(store.Request(original.Id)); var stateBytes = Wire.Encode(store.Load());
                var refused = false;
                try { await using var invalid = new StationApplication(store, config, new SimulationRequestContract(config)); }
                catch (InvalidDataException error) when (error.Message.Contains(kind == "fence" ? "Unsupported receipt fence" : "reservation missing", StringComparison.Ordinal)) { refused = true; }
                Check(refused && originalBytes == Wire.Encode(store.Request(original.Id)) && stateBytes == Wire.Encode(store.Load()), kind + " refuses before startup/recovery writes; original request and state unchanged");
            }
            Check(Wire.FileHash(database) == databaseHash && Wire.FileHash(file) == fileHash, kind + " retains exact database and raw-file bytes");
            if (validRaw is not null) Check(Wire.FileHash(validRaw) == validRawHash, "later unsupported raw does not partially replay or delete an earlier valid reserved receipt");

            await using var router = new IntegrationRuntime(config with { Integration = config.Integration with { Endpoint = "http://127.0.0.1:1/" } }, null);
            router.Initialize(); var missing = original with { Implementation = null }; var frozen = Wire.Encode(missing); var entries = router.Diagnostics.Length;
            foreach (var queryOnly in new[] { false, true })
            {
                var refused = false;
                try { if (queryOnly) await router.QueryAsync(missing, default); else await router.ExecuteAsync(missing, default); }
                catch (InvalidDataException error) when (error.Message.Contains("frozen implementation identity is missing", StringComparison.Ordinal)) { refused = true; }
                Check(refused && Wire.Encode(missing) == frozen && router.Diagnostics.Length == entries, "missing frozen implementation refuses Execute/Query before borrowing the changed target or constructing another route");
            }
        }
    }
    static void Check(bool valid, string message)
    { if (!valid) throw new InvalidOperationException(message); Console.WriteLine("PASS CURRENT " + message); }
}
