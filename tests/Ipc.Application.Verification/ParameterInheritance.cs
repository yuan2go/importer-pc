using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.DirectPlc;
using Ipc.Assembly;
using Ipc.Platform;

static class ParameterInheritance
{
    static void Check(bool ok, string reason) { if (!ok) throw new Exception("FAIL PARAM-INHERIT: " + reason); Console.WriteLine("PASS PARAM-INHERIT: " + reason); }
    static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> predicate)
    {
        for (var i = 0; i < 300; i++) { var s = await app.SnapshotAsync(); if (predicate(s)) return s; await Task.Delay(20); }
        throw new Exception("Parameter inheritance timeout: " + Wire.Encode(await app.SnapshotAsync()));
    }
    public static async Task Run(string workspace, string root)
    {
        var legacy = new BarcodeFilterPolicy("OK[0-9]+");
        Check(Wire.Encode(legacy) == Wire.Encode(new { Pattern = "OK[0-9]+" }), "old single-pattern bytes and fingerprint remain unchanged");
        var rule = legacy with { ExclusionPattern = "OK00[12]" };
        var selected = BarcodeFiltering.Select(rule, ["noise", "OK001", "OK003"]);
        Check(selected.SelectedCode == "OK003" && selected.Candidates!.Select(x => x.Outcome).SequenceEqual(["NotAllowed", "Excluded", "Eligible"]) && selected.RuleHash != BarcodeFiltering.Select(legacy, ["OK003"]).RuleHash, "allow/exclude returns precise candidate reasons and freezes both rules in fingerprint");
        Check(BarcodeFiltering.Select(rule, ["OK001", "OK002"]).Outcome == "ExcludedCodes" && BarcodeFiltering.Select(rule, ["OK003", "OK003"]).Outcome == "AmbiguousCodes", "excluded-only observations reject; repeated valid entries never manufacture uniqueness");
        Check(BarcodeFiltering.Select(new("OK.*", "001"), ["OK001"]).SelectedCode == "OK001", "exclusion matches the entire code rather than a substring");
        Check(BarcodeFiltering.Validate(new("OK.*", "["))!.StartsWith("BarcodeFilter.ExclusionPattern") && BarcodeFiltering.Select(new(".*", "(a+)+$"), [new string('a', 240) + "!"]).Outcome == "FilterTimeout", "invalid exclusion is field-addressable and pathological exclusion exhausts the same bounded budget");
        Check(BarcodeFiltering.Select(rule, ["OK003"], "truncated").Outcome == "IncompleteCandidates", "exclusion never bypasses candidate completeness");
        foreach (var kind in new[] { "observation", "weight", "plc", "camera", "login", "upload", "integration" })
            Check(ParameterOwnership.ObjectCategory(new(kind, [], null, false)) == (kind is "observation" or "weight" or "plc" or "camera" ? 2 : 3), kind + " has consistent physical/service ownership");
        var journalPath = Path.Combine(root, "retention-diagnostics"); Directory.CreateDirectory(journalPath);
        foreach (var name in new[] { "runtime.jsonl", "runtime.jsonl.2", "parcel.db", "evidence.jpg" }) { File.WriteAllText(Path.Combine(journalPath, name), name); File.SetLastWriteTimeUtc(Path.Combine(journalPath, name), DateTime.UtcNow.AddDays(-10)); }
        File.WriteAllText(Path.Combine(journalPath, "runtime.jsonl.1"), "recent");
        List<string> failures = [];
        await using (var journal = new RuntimeJournal(journalPath, failures.Add)) { journal.ConfigureRetention(2); journal.Write(new { Synthetic = true }); }
        Check(failures.Count == 0 && !File.Exists(Path.Combine(journalPath, "runtime.jsonl.1")) && File.ReadAllText(Path.Combine(journalPath, "runtime.jsonl.2")) == "recent" && File.Exists(Path.Combine(journalPath, "parcel.db")) && File.Exists(Path.Combine(journalPath, "evidence.jpg")), "log retention deletes only expired diagnostic rotations, preserving recent diagnostics and authoritative/evidence files");
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json"))) with { StationId = "inherit-" + Wire.Id(), BarcodeFilter = rule, Retention = new(200, 67108864, 2) };
        var oldReaders = ParcelBindings.Readers(seed); var isolated = oldReaders.Select((r, i) => r with { DeviceId = "inherit." + Wire.Id() + "." + i }).ToArray();
        seed = seed with { Reader = isolated[0], Topology = seed.Topology! with { Readers = isolated, Positions = seed.Topology.Positions.Select(p => p with { ReaderDeviceId = isolated[Array.FindIndex(oldReaders, r => r.DeviceId == p.ReaderDeviceId)].DeviceId }).ToArray() } };
        var path = Path.Combine(root, "inherit-sportmaster"); Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), seed);
        var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null);
        string frozen;
        await using (var app = new ParcelRuntime(launch, new SportmasterFlow(), new ParcelCapabilityFactory(launch)) { RuntimeIdentityPreflight = () => { } })
        {
            await app.StartAsync(default); await AdministratorFixture.Authorize(app); var vm = new ParcelViewModel(app, "参数继承", "合成验证"); await vm.RefreshAsync(); await vm.LoadCapabilitiesAsync();
            var startup = new StartupConfigurationDraft(vm.Snapshot!.Configuration.Effective, await app.CapabilitiesAsync());
            Check(Wire.Encode(startup.Build()) == Wire.Encode(vm.BuildStructuredConfiguration()) && vm.StructuredPolicy!.BarcodeExclusionPattern == rule.ExclusionPattern && vm.StructuredPolicy.DiagnosticLogDays == "2", "first and daily editors round-trip the same complete candidate including exclusion and retention");
            var revision = vm.Snapshot!.Configuration.Revision;
            vm.DisplayNameDraft = "合成站点"; vm.TargetEfficiencyDraft = "1800"; Check(vm.ApplyStationDisplay(), "display preferences persist through the existing local preference mechanism");
            vm.DisplayNameDraft = "未应用草稿"; await vm.RefreshAsync();
            Check(vm.DisplayNameDraft == "未应用草稿" && vm.StationDisplaySummary.Contains("合成站点") && vm.StationDisplaySummary.Contains("1800 件/小时") && vm.Snapshot!.Configuration.Revision == revision && !vm.HasUnsavedConfiguration, "display has real consumers, preserves drafts during snapshots and never creates a runtime revision");
            var secondVm = new ParcelViewModel(app, "另一个窗口", "合成验证"); await secondVm.RefreshAsync();
            Check(secondVm.DisplayNameDraft == "合成站点" && secondVm.TargetEfficiencyDraft == "1800", "a new window reads the saved per-station display preference");
            vm.TargetEfficiencyDraft = "0"; Check(!vm.ApplyStationDisplay() && vm.TargetEfficiencyDraft == "0" && vm.StationDisplaySummary.Contains("1800"), "invalid display input remains editable without changing the active preference");
            app.EmitReference("excluded", "OK001", Wire.Id()); await Wait(app, s => s.Inputs!.Any(x => x.BarcodeSelection?.Outcome == "ExcludedCodes"));
            Check((await app.SnapshotAsync()).Operations.Length == 0, "actual exclusion creates no business operation or external request");
            app.EmitReference("accepted", "OK003", Wire.Id()); var accepted = await Wait(app, s => s.Operations.Any(o => o.State == ParcelState.SlotResolved)); frozen = Wire.Encode(accepted.Operations.Single().Wcs);
            await app.CommandAsync(new(Wire.Id(), accepted.Revision, "Pause")); await AdministratorFixture.Authorize(app);
            app.BeforeMonitorProbe = (_, token) => Task.Delay(Timeout.Infinite, token); await Wait(app, s => s.Availability!.Configure);
            await vm.RefreshAsync(); vm.StructuredPolicy!.BarcodeExclusionPattern = "OK00[1-3]"; vm.StructuredPolicy.DiagnosticLogDays = "3"; vm.ConfigurationReason = "Synthetic inheritance revision";
            await vm.SaveIncompleteEditorAsync(); vm.DisplayNameDraft = "changed"; vm.TargetEfficiencyDraft = "1";
            await vm.ContinueIncompleteEditorAsync();
            Check(vm.DisplayNameDraft == "未应用草稿" && vm.TargetEfficiencyDraft == "0" && vm.HasUnsavedDisplayPreferences && vm.StationDisplaySummary.Contains("1800"), "encrypted editor draft preserves invalid display input without applying it or creating another configuration account");
            var changes = vm.GetConfigurationChangeItems();
            Check(changes.Any(x => x.Path == "BarcodeExclusionPattern" && x.Category == "业务模块") && changes.Any(x => x.Path == "DiagnosticLogDays" && x.Category == "系统"), "review uses the same business/system field paths as navigation and errors");
            await vm.SaveStructuredConfigurationAsync(); var saved = await app.SnapshotAsync();
            Check(saved.Configuration.Pending?.BarcodeFilter?.ExclusionPattern == "OK00[1-3]" && saved.Configuration.Effective.BarcodeFilter == rule && Wire.Encode(saved.Operations.Single().Wcs) == frozen, "new rules save as Pending while the active rule and original request stay frozen");
        }
        await using (var restarted = new ParcelRuntime(launch, new SportmasterFlow(), new ParcelCapabilityFactory(launch)) { RuntimeIdentityPreflight = () => { } })
        {
            await restarted.StartAsync(default); var active = await restarted.SnapshotAsync();
            Check(active.Configuration.Effective.BarcodeFilter?.ExclusionPattern == "OK00[1-3]" && active.Configuration.Effective.Retention?.DiagnosticLogDays == 3 && Wire.Encode(active.Operations.Single().Wcs) == frozen, "restart activates the exact saved policy without rewriting original requests");
        }
        await WeightRange(workspace, root);
    }
    static async Task WeightRange(string workspace, string root)
    {
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/direct-plc/business.json"))) with
        { Weight = new(WeightSource.Measured, WeightUnit.Kg, 3, WaitTimeoutMs: 1200), WeightRange = new(1, 2), Scale = new("weight", "reference-weight", "1.0.0", "scale.one", "reference://weight/inherit", "PARCEL-REFERENCE-v1", []) };
        var isolatedReader = c.Reader with { DeviceId = "inherit.weight." + Wire.Id() };
        c = c with { Reader = isolatedReader, Topology = c.Topology! with { Readers = [isolatedReader], Positions = c.Topology.Positions.Select(p => p with { ReaderDeviceId = isolatedReader.DeviceId }).ToArray() } };
        var path = Path.Combine(root, "inherit-weight"); Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), c);
        var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null);
        await using var app = new ParcelRuntime(launch, new DirectPlcFlow(), new ParcelCapabilityFactory(launch)) { RuntimeIdentityPreflight = () => { } }; await app.StartAsync(default); await AdministratorFixture.Authorize(app);
        var vm = new ParcelViewModel(app, "称重业务", "合成验证"); await vm.RefreshAsync(); await vm.LoadCapabilitiesAsync();
        vm.NewCapabilityKind = "weight"; await vm.AddCapabilityInstanceAsync(); var added = vm.CapabilityDrafts.Last();
        var position = vm.PositionDrafts.Single(); position.Scale = position.Scales.Single(x => x.Capability == added);
        Check(vm.BuildStructuredConfiguration().Topology!.Positions.Single().ScaleInstanceId == added.InstanceId, "an applicable Application can bind a second instance of the same driver without changing the reader");
        var referencedBlocked = false; try { await vm.RemoveCapabilityInstanceAsync(added); } catch (InvalidOperationException) { referencedBlocked = true; }
        Check(referencedBlocked, "removing a driver referenced by a position is still forbidden");
        position.Scale = position.Scales.Single(x => x.Capability is null); await vm.RemoveCapabilityInstanceAsync(added);
        Check(!vm.CapabilityDrafts.Contains(added) && vm.BuildStructuredConfiguration().Scale!.InstanceId == c.Scale!.InstanceId, "unbound candidate removal preserves the original default driver and installed packages");
        app.EmitReference("weight-range", "001", Wire.Id()); await Wait(app, s => s.Operations.Length == 1);
        app.EmitReferenceWeight(new(Wire.Id(), "weight-range", c.Scale!.Target, 0.5m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow));
        var rejected = await Wait(app, s => s.Operations.Single().Reason?.Contains("业务范围") == true);
        Check(rejected.Operations.Single().Weight is null && rejected.Operations.Single().Plc is null, "out-of-range stable measurement remains rejected with diagnostic reason and no PLC request");
        var timed = await Wait(app, s => s.Operations.Single().State == ParcelState.WeightTimedOut);
        app.EmitReferenceWeight(new(Wire.Id(), "weight-range", c.Scale.Target, 1.5m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow)); await Task.Delay(80);
        Check((await app.SnapshotAsync()).Operations.Single().Weight is null && timed.Operations.Single().Plc is null, "out-of-range samples never extend the deadline and timeout never substitutes a default weight");
        var op = timed.Operations.Single();
        WeightMeasurement Sample(decimal value, bool stable = true) => new(Wire.Id(), op.AdmissionId, c.Scale.Target, value, WeightUnit.Kg, 3, MeasurementQuality.Valid, stable, DateTimeOffset.UtcNow);
        Check(WeightRules.Invalid(Sample(1), op, c.Scale.Target, DateTimeOffset.UtcNow) is null && WeightRules.Invalid(Sample(2), op, c.Scale.Target, DateTimeOffset.UtcNow) is null && WeightRules.Invalid(Sample(1.5m, false), op, c.Scale.Target, DateTimeOffset.UtcNow) is not null, "inclusive range boundaries preserve stability and measurement identity checks");
        Check(new DirectPlcFlow().Validate(c with { Weight = new(WeightSource.Configured, WeightUnit.Kg, 3, 3), Scale = null }).Any(x => x.Contains("WeightRange")), "configured weight outside the business range is rejected before runtime");
        async Task<BusinessReceipt> Command(string kind, string? id = null) => await app.CommandAsync(new(Wire.Id(), (await app.SnapshotAsync()).Revision, kind, id, "SIMULATION: inspected and removed original entity"));
        await Command("Pause"); Check((await Command("AbandonUnsubmitted", op.Id)).Status == "Persisted" && (await Command("Resume")).Status == "Persisted", "timeout recovery requires explicit inspected disposition through Application guards");
        app.EmitReference("weight-next", "001", Wire.Id()); await Wait(app, s => s.Operations.Any(x => x.AdmissionId == "weight-next"));
        app.EmitReferenceWeight(new(Wire.Id(), "weight-next", c.Scale.Target, 1m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow));
        var next = await Wait(app, s => s.Operations.Any(x => x.AdmissionId == "weight-next" && x.State == ParcelState.PlcAccepted));
        Check(next.Operations.Single(x => x.AdmissionId == "weight-next").Weight is { Value: 1m, Source: WeightSource.Measured } && next.Operations.Single(x => x.AdmissionId == "weight-range").Weight is null, "next same-code entity accepts its own in-range measurement and never reuses the rejected parcel measurement");
    }
}
