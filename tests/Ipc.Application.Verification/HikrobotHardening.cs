using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Assembly;
using Ipc.Platform;

internal static class HikrobotHardening
{
    static int checks;
    static void Check(bool ok, string message)
    { if (!ok) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS SDK HARDENING: " + message); }
    internal static async Task Run(string workspace, string root)
    {
        Console.WriteLine("EvidenceRoot=" + root);
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        var readers = seed.Topology!.Readers.Select((r, n) => r with { Driver = "extension:identity-" + n }).ToArray();
        var instances = readers.Select((r, n) => new ExtensionInstance("identity-" + n,
            n == 0 ? "hikrobot.idmvs-reader" : "hikrobot.mvs-acquisition", "synthetic-module", r.DeviceId, true,
            new() { ["device.ip"] = "192.0.2." + (n + 1), ["device.serial"] = "SYNTHETIC-" + n })).ToArray();
        var production = seed with { Mode = RunMode.Production, Reader = readers[0], Topology = seed.Topology with { Readers = readers },
            Extensions = new([], instances), DeferredConnections = null };
        ParcelConfiguration Change(int index, string key, string value) => production with { Extensions = production.Extensions! with {
            Instances = instances.Select((i, n) => n == index ? i with { Parameters = new(i.Parameters) { [key] = value } } : i).ToArray() } };
        void Reject(ParcelConfiguration config, string field)
        {
            try { ParcelCapabilityFactory.ValidateHikrobotIdentities(config); }
            catch (InvalidDataException ex) when (ex.Message.Contains(field, StringComparison.Ordinal))
            { Check(true, "identity candidate rejected at " + field); return; }
            throw new InvalidOperationException("Unsafe identity candidate accepted or wrong field reported");
        }
        ParcelCapabilityFactory.ValidateHikrobotIdentities(production);
        Check(true, "distinct physical serials and addresses pass the pure identity stage; qualification is still separate");
        Reject(Change(0, "device.serial", ""), "identity-0/device.serial");
        Reject(Change(1, "device.serial", " "), "identity-1/device.serial");
        Reject(Change(0, "device.serial", "BAD\0SERIAL"), "identity-0/device.serial");
        Reject(Change(1, "device.serial", "SYNTHETIC-0"), "device.serial");
        Reject(Change(1, "device.ip", "192.0.2.1"), "device.ip");
        var optional = Change(0, "device.serial", "");
        ParcelCapabilityFactory.ValidateHikrobotIdentities(optional with { Mode = RunMode.Simulation });
        Check(true, "Simulation may omit expected serial; driver lifetime pin is checked independently");
        ParcelCapabilityFactory.ValidateHikrobotIdentities(optional with { DeferredConnections = [readers[0].DeviceId] });
        Check(true, "explicitly deferred Production source can remain unconfigured without becoming business Ready");

        var directory = Path.Combine(root, "barcode-diagnostics"); Directory.CreateDirectory(directory);
        var now = DateTimeOffset.UtcNow; const string physicalSerial = "SYNTHETIC-PRIVATE-SERIAL";
        const string legacyCode = "SYNTHETIC-LEGACY-BARCODE", currentCode = "SYNTHETIC-CURRENT-BARCODE";
        // The actual journal format is one JSON object per physical line.
        var legacy = System.Text.Json.JsonSerializer.Serialize(new { At = now, ParcelCode = legacyCode, cameraSerial = physicalSerial, Level = "信息", Event = "legacy" });
        File.WriteAllText(Path.Combine(directory, "runtime.jsonl"), legacy + "\n");
        var current = new { At = now, ParcelCode = currentCode, Parameters = new Dictionary<string, string> { ["camera.serial"] = physicalSerial }, Event = "current", StatusCode = 200 };
        var originalHash = Wire.Hash(Wire.Encode(current));
        await using (var journal = new RuntimeJournal(directory, _ => { }))
        {
            journal.Write(current);
            journal.WriteDebug(new { At = now, Event = "debug", Candidates = new[] { currentCode },
                Body = Wire.Encode(new { loadCode = currentCode, deviceSerial = physicalSerial, skuId = "KEPT-SKU" }) });
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                using var status = System.Text.Json.JsonDocument.Parse(Wire.Encode(journal.Status));
                if (status.RootElement.GetProperty("Written").GetInt64() >= 2) break;
                await Task.Delay(10, budget.Token);
            }
            var tail = Wire.Encode(journal.ReadTail(null, null));
            Check(!tail.Contains(currentCode) && !tail.Contains(physicalSerial) && tail.Contains("KEPT-SKU"), "live diagnostic tail has masked barcode but useful response metadata");
        }
        var written = File.ReadAllText(Path.Combine(directory, "runtime.jsonl"));
        Check(!written.Contains(currentCode) && written.Contains(legacyCode) && Wire.Hash(Wire.Encode(current)) == originalHash,
            "new records are masked at write while original business object and historical file are not rewritten");
        var authority = new LocalManagementMode(Path.Combine(root, "log-authority"));
        authority.ConfigurePassword("SYNTHETIC-TEST-PASSWORD"); authority.Enter("SYNTHETIC-TEST-PASSWORD");
        using var reader = new SoftwareLogReader(directory, authority);
        var page = await reader.QueryAsync(new(now.AddMinutes(-1), now.AddMinutes(1)), null, 0, default);
        Check(page.Entries.Length == 3 && !Wire.Encode(page).Contains(legacyCode) && !Wire.Encode(page).Contains(currentCode) && !Wire.Encode(page).Contains(physicalSerial),
            "historical query masks legacy raw barcode without dropping records");
        var context = await reader.ContextAsync(page.Selection, page.Entries[0].Location, 0, 2, default);
        Check(context.Entries.Length == 3 && !Wire.Encode(context).Contains(legacyCode), "context retains original adjacency without exposing legacy barcode");
        var forbidden = Path.Combine(root, "protected-log-export.jsonl"); bool blocked = false;
        try { await reader.ExportAsync(page.Selection, forbidden, false, 0, default); }
        catch (IOException ex) when (ex.Message.Contains("业务数据目录之外")) { blocked = true; }
        Check(blocked && !File.Exists(forbidden), "export still refuses the protected business-data tree");
        var target = Path.GetFullPath(Path.Combine(root, "..", "protected-log-export-" + Wire.Id() + ".jsonl"));
        Check(await reader.ExportAsync(page.Selection, target, false, 0, default) == 3 && !File.ReadAllText(target).Contains(legacyCode) &&
            !File.ReadAllText(target).Contains(currentCode) && !File.ReadAllText(target).Contains(physicalSerial), "export revalidates originals and masks both historical and new barcode fields");
        Console.WriteLine("SDK_HARDENING_PASS checks=" + checks + "; pure identity and actual diagnostic storage/query/export; SDK/Production qualification NOT_RUN");
    }
}
