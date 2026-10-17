using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;
using Microsoft.Data.Sqlite;

static class ArchitectureMaintainabilityVerification
{
    static void Assert(bool condition, string text)
    { if (!condition) throw new InvalidOperationException("AC16 architecture: " + text); Console.WriteLine("PASS AC16 architecture: " + text); }

    public static async Task Run(string root, string url, string workspace)
    {
        var references = typeof(LoopbackIntegration).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert(references.Contains("Ipc.Supply.Contracts") && !references.Contains("Ipc.Supply"), "built-in adapters reference public contracts without Supply implementation");
        var original = Rig.Config(url);
        var unrelated = original with { Devices = [], Policy = "UNRELATED-BUSINESS-POLICY", FixtureDemand = -1 };
        Assert(SupplyConfiguration.Validate(unrelated).Count != 0, "business validation continues to refuse invalid Supply configuration");
        await using (var port = new LoopbackIntegration(unrelated))
            Assert(await port.HealthAsync(CancellationToken.None), "transport validates its own boundary independently of business lane and quantity policy");
        foreach (var invalid in new[] {
            original with { Mode = RunMode.Production }, original with { TimeoutMs = 0 },
            original with { Integration = original.Integration with { Contract = "UNAPPROVED" } },
            original with { Integration = original.Integration with { Endpoint = "http://localhost:8091/" } },
            original with { Integration = original.Integration with { Endpoint = url + "?unapproved=1" } },
            original with { Integration = original.Integration with { Endpoint = url + "#unapproved" } } })
        {
            bool refused = false;
            try { await using var invalidPort = new LoopbackIntegration(invalid); }
            catch (InvalidDataException) { refused = true; }
            Assert(refused, "transport refuses invalid mode/timeout/contract/endpoint before allocating client");
        }
        await using (var rig = await Rig.New(root, url))
        {
            var state = await rig.App.SnapshotAsync();
            var command = new Command(Wire.Id(), state.State.Revision, "Pause");
            var json = Wire.Encode(command);
            Assert(Wire.Decode<Command>(json) == command && json.Contains("\"Kind\": \"Pause\"", StringComparison.Ordinal), "original serialized command and history strings are preserved");
            var paused = await rig.App.ExecuteAsync(command);
            Assert(paused.Status == "Persisted" && await rig.App.ExecuteAsync(command) == paused &&
                rig.Store.Audits(rig.ConfigValue.StationId).Count(x => x.CommandId == command.Id && x.Kind == "Pause") == 1,
                "typed dispatch preserves original audit kind and one durable idempotent command receipt");
            await rig.Command(SupplyCommands.Resume);
            await rig.Command(SupplyCommands.Login, value: "operator");
            await rig.Command(SupplyCommands.SetFixtureFault, value: "None", expect: "RejectedByGuard");
            await rig.Command(SupplyCommands.SaveConfig, value: Wire.Encode(rig.ConfigValue with { Version = "UNAUTHORIZED" }), expect: "RejectedByGuard");
            Assert((await rig.App.ConfigsAsync()).All(x => x.Content.Version != "UNAUTHORIZED"), "shared maintenance judgment rejects operator commands without saving a deployment draft");
            await rig.Command(SupplyCommands.Login, value: "maintenance");
            await rig.Command("pause", expect: "RejectedByGuard");
            Assert(!(await rig.App.SnapshotAsync()).State.Blocks.Any(x => x.Code == SupplyReasonCodes.OperatorPaused), "unknown case does not alias a privileged command or mutate state");
            await rig.OpenBox();
            var request = (await rig.App.SnapshotAsync()).Requests.Single();
            var tampered = request with { Binding = request.Binding with { Endpoint = url + "?override=1" } };
            var bytes = Wire.Encode(tampered);
            bool refused = false;
            try { await rig.Port.QueryAsync(tampered, CancellationToken.None); }
            catch (InvalidDataException) { refused = true; }
            Assert(refused && Wire.Encode(tampered) == bytes && (await rig.App.DetailsAsync(request.Id)).Single().Request == request,
                "frozen transport binding is validated without reinterpreting or mutating original request");
        }
        var expected = new[] { "Login", "Logout", "Pause", "Resume", "ClearOverload", "OpenContext", "Submit", "ScanRetained", "ClearSplit", "CancelPrepared", "CancelDraft", "CheckClose", "CancelClose", "ConfirmClose", "SetFixtureFault", "SetSyntheticBlock", "ClearSyntheticBlock", "ReviewObservations", "RecordReconciliation", "SaveConfig", "ActivateConfig", "ClearQuarantine", "AckAlarm" };
        Assert(expected.All(x => SupplyCommands.TryParse(x, out var action) && action.ToString() == x) && expected.Length == Enum.GetValues<SupplyAction>().Length - 1 &&
            new string?[] { "0", "1", "Pause ", "pause", "Unknown", null, "" }.All(x => !SupplyCommands.TryParse(x, out _)), "exact legacy action vocabulary rejects numeric enum and case/whitespace aliases");
        var noBox = new StationSession { Status = SessionStatus.Rejected };
        Assert(SupplyCommands.SessionReady(SupplyAction.OpenContext, null) && SupplyCommands.SessionReady(SupplyAction.OpenContext, noBox) &&
            !SupplyCommands.SessionReady(SupplyAction.CheckClose, noBox) && !SupplyCommands.SessionReady(SupplyAction.CancelClose, noBox),
            "shared session eligibility preserves no-box, terminal-box and close-check barriers");
        noBox.Status = SessionStatus.AwaitingShortageDecision;
        Assert(SupplyCommands.SessionReady(SupplyAction.CancelClose, noBox) && !SupplyCommands.SessionReady(SupplyAction.ConfirmClose, noBox), "close confirmation requires original authoritative token while cancellation remains available");
        noBox.CloseToken = "SYNTHETIC-TOKEN";
        Assert(SupplyCommands.SessionReady(SupplyAction.ConfirmClose, noBox), "original shortage authority enables confirmation eligibility without granting other guards");
        Assert(SupplyCommands.IsPriority("Pause") && SupplyCommands.IsPriority("Logout") && SupplyCommands.IsPriority("ClearOverload") && !SupplyCommands.IsPriority("Submit"), "priority uses typed command vocabulary");
        using (var db = new SqliteConnection("Data Source=:memory:"))
        {
            db.Open();
            using var create = db.CreateCommand(); create.CommandText = "CREATE TABLE receipts(status TEXT NOT NULL); INSERT INTO receipts VALUES ('AwaitingResponse'),('RawUnavailable'),('Committed'),('NoResponse'),('FutureStatus');"; create.ExecuteNonQuery();
            using var query = db.CreateCommand(); query.CommandText = "SELECT status FROM receipts WHERE " + ReceiptResponsibilityRules.UnresolvedSql("status");
            using var reader = query.ExecuteReader(); List<string> pending = [];
            while (reader.Read()) pending.Add(reader.GetString(0));
            var all = new[] { "AwaitingResponse", "RawUnavailable", "Committed", "NoResponse", "FutureStatus" };
            Assert(all.All(x => pending.Contains(x) == ReceiptResponsibilityRules.IsUnresolved(new("occurrence", "request", null, "query", DateTimeOffset.UtcNow, x))) &&
                pending.SequenceEqual(new[] { "AwaitingResponse", "RawUnavailable", "FutureStatus" }), "live predicate and SQL readers retain each nonterminal/unknown receipt independently");
        }
        var packaged = Wire.Decode<ProductDefinition>(File.ReadAllText(Path.Combine(workspace, "config/product.reference.json")));
        Assert(Wire.Encode(packaged) == Wire.Encode(ReferenceProduct.Definition) && packaged.Views.SequenceEqual(SupplyViews.Installed), "business-owned view IDs and concrete module dependencies match shipped assembly registration");
        File.WriteAllText(Path.Combine(root, "architecture-maintainability-16.json"), Wire.Encode(new { AdapterReferences = references, CommandNames = expected, Views = SupplyViews.Installed, StorageFormat = 5, PublicContractMajor = 1, Windows = "NOT_RUN" }));
    }
}
