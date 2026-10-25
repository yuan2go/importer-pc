using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;

static class ParameterGuidanceStartup
{
    static void Check(bool value, string reason)
    { if (!value) throw new Exception("FAIL PGS01: " + reason); Console.WriteLine("PASS PGS01: " + reason); }
    public static async Task Run(string workspace, string root)
    {
        var flow = new SportmasterFlow(); var initial = flow.InitialConfiguration();
        Check(initial.Retention is { DiagnosticLogDays: 7, HistoryDays: 7 } && initial.DeferredConnections?.Length == 3,
            "new stations default to seven days and explicitly deferred left/right/RCS connections");
        var offlineLaunch = new BusinessLaunch(Path.Combine(root, "offline-config"), Path.Combine(root, "offline-data"), null, null);
        var offline = new ParcelStartupMaintenance(offlineLaunch, flow, () => new ParcelCapabilityFactory(offlineLaunch), () => { });
        var document = await offline.ReadAsync();
        var report = await offline.CheckAsync(document.Candidate!);
        Check(report.Checks.All(c => c.Status != "FAIL"), "the actual offline first-run checker accepts deferred Production connections without starting them");
        var saved = await offline.RepairInitialAsync(document.ConfigurationHash, document.Candidate!, "synthetic first configuration");
        Check(saved.Candidate!.DeferredConnections?.Length == 3 && File.Exists(Path.Combine(offlineLaunch.ConfigurationDirectory, "business.json")) &&
            (!Directory.Exists(offlineLaunch.DataRoot) || !Directory.EnumerateFiles(offlineLaunch.DataRoot, "*.db", SearchOption.AllDirectories).Any()),
            "first-run persistence saves the explicit Production candidate without creating device sessions or a substitute business database");
        var launch = new BusinessLaunch(Path.Combine(root, "config"), Path.Combine(root, "data"), null, null);
        await using var factory = new ParcelCapabilityFactory(launch);
        factory.ValidateStructure(initial);
        var draft = new StartupConfigurationDraft(initial, factory.Catalog());
        var editor = draft.CreateParameterEditor((p, codes) => Task.FromResult(BarcodeFiltering.Select(p, codes)));
        Check(editor.ValidateFields() && Wire.Encode(draft.Build()) == Wire.Encode(initial),
            "incomplete connection fields are preserved and an untouched deferred candidate round-trips without defaults added");
        var rcs = draft.Capabilities.Single(ParameterOwnership.IsRcs);
        Check(!rcs.Parameters.Any(p => p.Field.Key == "quantity") && !draft.Build().Wcs!.Parameters.ContainsKey("quantity") &&
            ParcelBindings.Readers(draft.Build()).All(r => r.Parameters!["entity.mode"] == "line"),
            "one scan per parcel is the Sportmaster candidate policy; quantity is absent from its editors and new configuration");
        var oldReader = draft.Readers[0].Parameters.Single(p => p.Field.Key == "entity.mode"); oldReader.Value = "observe-only";
        Check(draft.Build().Reader.Parameters!["entity.mode"] == "line" && initial.Reader.Parameters!["entity.mode"] == "line",
            "saving a Sportmaster candidate replaces the obsolete observe-only choice without editing the original configuration");
        oldReader.Value = "line";
        var left = rcs.Parameters.Single(p => p.Field.Key == "left-station-code"); var right = rcs.Parameters.Single(p => p.Field.Key == "right-station-code");
        var frozen = Wire.Encode(initial);
        using (var shared = new CommonStationParameter(left, right, draft.Policy)) shared.Value = "SYNTHETIC-STATION";
        Check(left.Value == right.Value && left.Value == draft.Policy.StationCode && Wire.Encode(initial) == frozen,
            "one deliberate station edit updates both request identities and heartbeat while leaving the original configuration unchanged");
        left.Value = "old-left";
        using (var shared = new CommonStationParameter(left, right, draft.Policy))
            Check(shared.Value == "" && !editor.ValidateFields() && editor.Issues.Any(i => i.Path == "StationCode"), "conflicting historical station identifiers require an explicit common choice");
        left.Value = right.Value;
        draft.Readers[0].ConfigureLater = false;
        Check(!editor.ValidateFields() && editor.Issues.Any(i => i.Path.EndsWith("/device.ip")), "activating an incomplete camera restores required field validation");
        draft.Readers[0].ConfigureLater = true;
        var state = draft.Capture(); draft.Readers[0].ConfigureLater = false; draft.Restore(state);
        Check(draft.Readers[0].ConfigureLater && draft.WithRestored(state).Build().DeferredConnections?.Length == 3, "raw startup drafts preserve deferred connections and incomplete input");
        var legacy = initial with { Retention = null, DeferredConnections = null };
        var legacyDraft = new StartupConfigurationDraft(legacy, factory.Catalog());
        Check(legacyDraft.Policy.DiagnosticLogDays == "" && legacyDraft.Policy.HistoryDays == "" && legacyDraft.Readers.All(r => !r.ConfigureLater),
            "old omitted retention and connection fields keep their prior semantics");
        var unfinished = new StartupConfigurationDraft(legacy, factory.Catalog(), initializeDefaults: true);
        Check(unfinished.Policy.DiagnosticLogDays == "7" && unfinished.Policy.HistoryDays == "7" && unfinished.Readers.All(r => r.ConfigureLater) && legacy.Retention is null,
            "an older unfinished installer seed receives first-run defaults in the editable candidate without modifying the seed or existing deployment policy");
        var invalid = initial with { DeferredConnections = ["unknown"] };
        Check(flow.Validate(invalid).Length > 0, "unknown deferred identities are rejected by Application rules");
        try { factory.ValidateStructure(initial with { DeferredConnections = null }); throw new Exception("unexpected valid active configuration"); }
        catch (InvalidDataException) { Check(true, "removing deferred state restores real adapter configuration validation"); }
        var c = draft.Build() with { Mode = RunMode.Simulation, StationId = "deferred-" + Wire.Id() };
        var oldReaders = c.Topology!.Readers.Select((r, i) => i != 0 ? r : r with { Parameters = new(r.Parameters!) { ["entity.mode"] = "observe-only" } }).ToArray();
        c = c with { Reader = oldReaders[0], Topology = c.Topology with { Readers = oldReaders }, Wcs = c.Wcs! with { Parameters = new(c.Wcs!.Parameters) { ["quantity"] = "7" } } };
        Directory.CreateDirectory(launch.ConfigurationDirectory); ExtensionStore.AtomicWrite(Path.Combine(launch.ConfigurationDirectory, "business.json"), c);
        await using (var app = new ParcelRuntime(launch, flow, new ParcelCapabilityFactory(launch)) { RuntimeIdentityPreflight = () => { } })
        {
            await app.StartAsync(default);
            var snapshot = await app.SnapshotAsync();
            Check(snapshot.Configuration.Effective.DeferredConnections?.Length == 3 && !snapshot.Availability!.Resume && !snapshot.Availability.Admit && snapshot.Operations.Length == 0,
                "actual runtime opens with incomplete connections but refuses resume and admission");
            Check(snapshot.BlockingReasons.Any(r => r.Contains("连接待配置")) && snapshot.Health.All(h => !h.Ready) && snapshot.Devices!.All(d => d.Health != DeviceHealth.Ready),
                "pending dependencies report unavailable health with an actionable configuration reason");
            Check(!app.EmitReferenceAt("left", "synthetic", "CODE", "event"), "deferred sources expose no simulated input fallback");
            var vm = new ParcelViewModel(app, "synthetic", "synthetic", "sportmaster");
            await vm.RefreshAsync();
            await app.CommandAsync(new(Wire.Id(), snapshot.Revision, "Pause"));
            await ManagementModeFixture.Authorize(app); await vm.LoadCapabilitiesAsync();
            var scanCandidate = vm.BuildStructuredConfiguration();
            var policySnapshot = await app.SnapshotAsync();
            Check(vm.HasUnsavedConfiguration && scanCandidate.Reader.Parameters?.GetValueOrDefault("entity.mode") == "line" &&
                scanCandidate.Wcs?.Parameters.ContainsKey("quantity") == false && policySnapshot.Configuration.Effective.Reader.Parameters?.GetValueOrDefault("entity.mode") == "observe-only" && policySnapshot.Configuration.Effective.Wcs?.Parameters.GetValueOrDefault("quantity") == "7",
                "legacy scan policy is an explicit saveable reviewed candidate, while Effective retains its original observe-only mode and quantity");
            foreach (var p in vm.ReaderDrafts[0].Capability.Parameters) p.Value = p.Field.Key switch { "device.ip" => "192.0.2.10", "device.port" => "9000", _ => p.Value };
            vm.ReaderDrafts[0].Capability.ConfigureLater = false;
            Check(vm.HasUnsavedConfiguration && vm.GetConfigurationChangeItems().Any(i => i.Label == "稍后配置的连接"), "daily deferred-state edits participate in dirty tracking and save review");
            vm.ReaderDrafts[0].Capability.ConfigureLater = true;
            Check(vm.BuildStructuredConfiguration().DeferredConnections?.Length == 3, "the daily editor preserves the same complete deferred candidate");
            vm.StructuredPolicy!.QueueCapacity = "65"; vm.ConfigurationReason = "synthetic deferred revision";
            await vm.SaveStructuredConfigurationAsync();
            var pending = await app.SnapshotAsync();
            Check(pending.Configuration.Pending?.DeferredConnections?.Length == 3 && pending.Configuration.Effective.QueueCapacity == c.QueueCapacity &&
                pending.Configuration.Pending.QueueCapacity == 65 && pending.Operations.Length == 0,
                "daily deferred configuration saves through the existing Pending revision without applying it or creating requests");
            await app.StopAsync(default);
        }
        Console.WriteLine("PASS PGS01 COMPLETE");
    }
}
