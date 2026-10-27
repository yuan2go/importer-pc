using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;

static class ParameterRefactor
{
    static void Check(bool ok, string reason) { if (!ok) throw new Exception("FAIL PARAM01: " + reason); Console.WriteLine("PASS PARAM01: " + reason); }
    static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> condition)
    {
        for (var i = 0; i < 250; i++) { var s = await app.SnapshotAsync(); if (condition(s)) return s; await Task.Delay(20); }
        throw new Exception("PARAM01 timed out: " + Wire.Encode(await app.SnapshotAsync()));
    }
    public static async Task Run(string workspace, string root)
    {
        SportmasterRcs.ParameterStaticChecks(workspace);
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        var oldReaders = ParcelBindings.Readers(c); var fixtureReaders = oldReaders.Select((r, i) => r with { DeviceId = "parameter." + Wire.Id() + "." + i }).ToArray();
        c = c with { Reader = fixtureReaders[0], Topology = c.Topology! with { Readers = fixtureReaders, Positions = c.Topology.Positions.Select(p => p with { ReaderDeviceId = fixtureReaders[Array.FindIndex(oldReaders, r => r.DeviceId == p.ReaderDeviceId)].DeviceId }).ToArray() } };
        Check(!Wire.Encode(c).Contains("BarcodeFilter") && Wire.Encode(Wire.Decode<ParcelConfiguration>(Wire.Encode(c))) == Wire.Encode(c), "missing legacy policy is omitted and round-trips without manufacturing a production rule");
        Check(BarcodeFiltering.Select(null, ["X", "Y"]).Outcome == "MultipleCodes", "legacy policy retains strict single-code compatibility");
        Check(BarcodeFiltering.Select(new("OK[0-9]+"), ["bad", "noise", "OK003"]).SelectedCode == "OK003", "third candidate is evaluated and original order is retained");
        Check(BarcodeFiltering.Select(new("OK[0-9]+"), ["preOK003", "OK003tail"]).Outcome == "NoMatchingCode", "matching is whole-string rather than substring");
        Check(BarcodeFiltering.Select(new("OK[0-9]+"), ["OK003", "OK004"]).Outcome == "AmbiguousCodes", "multiple valid candidates are ambiguous");
        Check(BarcodeFiltering.Select(new("a|ab"), ["ab"]).SelectedCode == "ab", "whole-string alternation is evaluated correctly");
        Check(BarcodeFiltering.Validate(new("")) is null && BarcodeFiltering.Select(new(""), ["OK001"]).SelectedCode == "OK001", "empty rule is optional and keeps the single valid code without generating a regex");
        Check(BarcodeFiltering.Select(new(""), ["OK001", "OK002"]).Outcome == "AmbiguousCodes" && BarcodeFiltering.Select(new(""), ["OK001", "OK001"]).Outcome == "AmbiguousCodes", "unfiltered and repeated candidates still require uniqueness");
        Check(BarcodeFiltering.Select(new("", "BAD.*"), ["BAD001", "OK001"]).SelectedCode == "OK001", "optional allow rule supports an exclusion-only policy");
        Check(BarcodeFiltering.Select(new("["), ["X"]).Outcome == "InvalidPattern", "invalid syntax produces a bounded rejection");
        Check(BarcodeFiltering.Select(new("(a+)+$"), [new string('a', 500) + "!"]).Outcome == "FilterTimeout", "catastrophic backtracking times out and does not select a code");
        Check(BarcodeFiltering.Select(new("OK.*"), ["OK003"], "truncated").Outcome == "IncompleteCandidates" && BarcodeFiltering.Select(new("OK.*"), Enumerable.Repeat("noise", 300).Append("OK003").ToArray()).Outcome == "IncompleteCandidates", "incomplete and oversized candidate sets can never imply uniqueness");
        Check(new SportmasterFlow().Validate(c with { BarcodeFilter = new("[") }).Any(x => x.Contains("BarcodeFilter.Pattern")), "business configuration validation rejects malformed rules before save");
        var missingPath = Path.Combine(root, "missing-config");
        var missingLaunch = new BusinessLaunch(missingPath, Path.Combine(root, "missing-data"), null, null);
        var maintenance = new ParcelStartupMaintenance(missingLaunch, new SportmasterFlow(), () => new ParcelCapabilityFactory(missingLaunch), () => { });
        var missing = await maintenance.ReadAsync();
        Check(missing.Candidate is { Mode: RunMode.Production, QueueCapacity: 64, TimeoutMs: 1500, MaximumOperations: 10000, StationCode: "station-01" } && missing.ConfigurationStatus == "MissingConfiguration" && !File.Exists(Path.Combine(missingPath, "business.json")), "missing configuration opens an editable fixed-dual-position draft with system defaults, without implicit persistence or simulation runtime");
        var bootstrapReaders = missing.Candidate!.Topology!.Readers.Select((r, i) => r with { Parameters = new() { ["device.ip"] = "192.0.2." + (11 + i), ["device.port"] = "9000" } }).ToArray();
        var bootstrap = missing.Candidate with { Reader = bootstrapReaders[0], Topology = missing.Candidate.Topology with { Readers = bootstrapReaders }, BarcodeFilter = new("OK[0-9]+"),
            Wcs = missing.Candidate.Wcs! with { Target = "http://127.0.0.1:1/rcs/sorter/api", Parameters = new() { ["zone-id"] = "1", ["project-id"] = "1", ["specified-chute"] = "false", ["request-id-base"] = "1000000", ["request-id-count"] = "100", ["left-station-code"] = "SYNTHETIC", ["right-station-code"] = "SYNTHETIC", ["left-bcr-code"] = "L", ["right-bcr-code"] = "R" } } };
        var created = await maintenance.RepairInitialAsync(missing.ConfigurationHash, bootstrap, "Synthetic initial defaults and explicit synthetic connection settings");
        Check(created.Candidate is not null && File.Exists(Path.Combine(missingPath, "business.json")) && (!Directory.Exists(missingLaunch.DataRoot) || !Directory.EnumerateFiles(missingLaunch.DataRoot, "*.db", SearchOption.AllDirectories).Any()) && (!Directory.Exists(missingLaunch.DataRoot) || Directory.EnumerateFiles(missingLaunch.DataRoot, "*lease*", SearchOption.AllDirectories).All(p => Path.GetFullPath(p) == Path.GetFullPath(Path.Combine(missingLaunch.DataRoot, "security/diagnostics/rotation.lease")))), "explicit first save persists parameters without starting SDK, creating a business database/device lease, or inventing qualification; administrator custody and diagnostic rotation are separate");
        var invalidPath = Path.Combine(root, "invalid-config"); Directory.CreateDirectory(invalidPath); ExtensionStore.AtomicWrite(Path.Combine(invalidPath, "business.json"), c with { BarcodeFilter = new("[") });
        var invalidLaunch = new BusinessLaunch(invalidPath, Path.Combine(root, "invalid-data"), null, null);
        var invalidMaintenance = new ParcelStartupMaintenance(invalidLaunch, new SportmasterFlow(), () => new ParcelCapabilityFactory(invalidLaunch), () => { });
        Check((await invalidMaintenance.ReadAsync()).Candidate?.BarcodeFilter?.Pattern == "[", "invalid business parameters remain editable while business startup is blocked");
        var defaults = new StartupConfigurationDraft(c with { StationCode = null }, (await maintenance.ReadAsync()).Catalog);
        Check(defaults.Policy.StationCode == c.StationId && defaults.Build().StationCode == c.StationId && defaults.Policy.BackupIntervalMinutes == "60" && defaults.Policy.BackupMaximumSets == "10" && !defaults.Policy.BackupEnabled, "mandatory system values have explicit defaults; optional scheduled backup remains off");
        var path = Path.Combine(root, "parameters"); Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), c with { BarcodeFilter = new("OK[0-9]+") });
        var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null);
        var factory = new UxCommercialInputs.InputFactory(new ParcelCapabilityFactory(launch));
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } };
        await app.StartAsync(default); await ManagementModeFixture.Authorize(app);
        var reader = c.Topology!.Readers[0].DeviceId;
        factory.Emit(reader, "Read", "third-entity", ["noise", "other", "OK003"], "third-source");
        var first = await Wait(app, s => s.Operations.Any(o => o.AdmissionId == "third-entity" && o.State == ParcelState.SlotResolved));
        var original = first.Operations.Single(); var frozen = Wire.Encode(original.Wcs);
        Check(original.ParcelCode == "OK003" && original.BarcodeSelection?.Outcome == "Selected" && first.Inputs!.Single().Observation.Codes.SequenceEqual(["noise", "other", "OK003"]), "actual admission freezes the selected code and preserves all raw input candidates");
        foreach (var (codes, outcome) in new[] { (new[] { "noise" }, "NoMatchingCode"), (new[] { "OK004", "OK005" }, "AmbiguousCodes") })
        {
            var source = Wire.Id(); factory.Emit(reader, "Read", "rejected-" + source, codes, source);
            var rejected = await Wait(app, s => s.Inputs!.Any(x => x.Observation.SourceEventId == source));
            Check(rejected.Inputs!.Single().BarcodeSelection?.Outcome == outcome && rejected.Operations.Length == 1 && rejected.Positions!.Single(p => p.Id == "left").CurrentOperation is null, outcome + " creates no request and cannot reuse a previous slot");
        }
        factory.Emit(c.Topology.Readers[1].DeviceId, "Read", "right-entity", ["OK003"], "right-source");
        await Wait(app, s => s.Operations.Length == 2 && s.Operations.All(o => o.State == ParcelState.SlotResolved));
        Check((await app.SnapshotAsync()).Operations.Select(o => o.AdmissionId).Distinct().Count() == 2, "same code at independent positions remains two distinct entities");
        var before = await app.SnapshotAsync(); await app.CommandAsync(new(Wire.Id(), before.Revision, "Pause")); await ManagementModeFixture.Authorize(app);
        // Isolate configuration semantics from periodic health probes taking the
        // shared external-call guard between availability and Stage. The real
        // guard remains enforced; all in-flight probes must drain first.
        app.BeforeMonitorProbe = (_, token) => Task.Delay(Timeout.Infinite, token);
        await Wait(app, s => s.Availability!.Configure);
        var unchangedBefore = (await app.SnapshotAsync()).Configuration;
        var noChange = await app.StageConfigurationAsync(unchangedBefore.Revision, unchangedBefore.Effective, "Synthetic no-change guard");
        var unchangedAfter = (await app.SnapshotAsync()).Configuration;
        Check(noChange.Status == "Unchanged" && unchangedAfter.Pending is null && unchangedAfter.Revision == unchangedBefore.Revision,
            "unchanged Effective never creates a pending restart revision");
        Check((await app.InspectConfigurationSubmissionAsync((await app.SnapshotAsync()).RuntimeId, unchangedBefore.Revision, unchangedBefore.Effective)).Status == "Unchanged",
            "authority inspection identifies the same unchanged candidate without claiming pending or resubmitting");
        var vm = new ParcelViewModel(app, "参数", "合成验证"); await vm.RefreshAsync(); await vm.LoadCapabilitiesAsync();
        var startup = new StartupConfigurationDraft((await app.SnapshotAsync()).Configuration.Effective, await app.CapabilitiesAsync());
        Check(Wire.Encode(vm.BuildStructuredConfiguration()) == Wire.Encode(startup.Build()), "first configuration and daily editor preserve an identical complete candidate");
        vm.StructuredPolicy!.StationCode = "SYSTEM-EDIT"; vm.StructuredPolicy.BarcodePattern = "NEW[0-9]+"; vm.ConfigurationReason = "Synthetic parameter refactor verification";
        var revision = (await app.SnapshotAsync()).Configuration.Revision;
        app.Boundary = stage => { if (stage == "AfterConfigurationStaged") throw new InvalidOperationException("Synthetic post-stage audit refusal"); };
        await vm.SaveStructuredConfigurationAsync();
        var saved = (await app.SnapshotAsync()).Configuration;
        Check(vm.Message.StartsWith("PendingRestart") && saved.Revision == revision + 1 && saved.Pending!.StationCode == "SYSTEM-EDIT" && saved.Effective.BarcodeFilter!.Pattern == "OK[0-9]+", "post-write Rejected is reconciled from authority; saving does not apply the new rule");
        Check(!vm.HasUnsavedConfiguration && Wire.Encode((await app.SnapshotAsync()).Operations.Single(o => o.Id == original.Id).Wcs) == frozen, "confirmed save accepts only its draft baseline and preserves the original request bytes");
        app.Boundary = null;
        var pendingRepeat = await app.StageConfigurationAsync(saved.Revision, saved.Pending!, "Synthetic existing Pending");
        Check(pendingRepeat.Status == "PendingRestart" && (await app.SnapshotAsync()).Configuration.Revision == saved.Revision,
            "identical Pending remains idempotent after the no-change guard: " + pendingRepeat.Status + " / " + pendingRepeat.Reason);
        var staleNoChange = await app.StageConfigurationAsync(saved.Revision - 1, saved.Effective, "Synthetic stale no-change");
        Check(staleNoChange.Status == "Rejected" && (await app.SnapshotAsync()).Configuration.Pending!.StationCode == "SYSTEM-EDIT",
            "stale CAS cannot disguise itself as unchanged or discard Pending");
        var proxy = DispatchProxy.Create<IManagementParcelFixture, SaveProbe>(); var probe = (SaveProbe)(object)proxy; probe.Inner = app; probe.FailInspection = true;
        var pendingVm = new ParcelViewModel(proxy, "待核对", "合成验证"); await pendingVm.RefreshAsync(); await pendingVm.LoadCapabilitiesAsync(); pendingVm.ConfigurationReason = "Synthetic uncertain result"; pendingVm.StructuredPolicy!.TimeoutMs = "1700";
        await pendingVm.SaveStructuredConfigurationAsync();
        Check(pendingVm.Message.Contains("禁止重复提交") && pendingVm.HasUnsavedConfiguration, "unavailable authority inspection retains raw input and an explicit uncertain-save state");
        var blocked = false; try { await pendingVm.SaveStructuredConfigurationAsync(); } catch (InvalidOperationException) { blocked = true; }
        Check(blocked && probe.StageCalls == 1, "uncertain result prevents a second stage submission");
        probe.FailInspection = false; await pendingVm.InspectLastSaveAsync();
        Check(!pendingVm.HasUnsavedConfiguration && pendingVm.Message.StartsWith("PendingRestart") && probe.StageCalls == 1, "later authority inspection confirms the original submission without resubmitting");
        var stale = new ParcelViewModel(app, "旧CAS", "合成验证"); await stale.RefreshAsync(); await stale.LoadCapabilitiesAsync();
        pendingVm.StructuredPolicy.TimeoutMs = "1800"; await pendingVm.SaveStructuredConfigurationAsync();
        stale.StructuredPolicy!.TimeoutMs = "1900"; stale.ConfigurationReason = "Synthetic stale CAS"; await stale.SaveStructuredConfigurationAsync();
        Check(stale.Message.StartsWith("Conflict") && stale.HasUnsavedConfiguration && stale.StructuredPolicy.TimeoutMs == "1900", "CAS conflict preserves input and cannot replace the saved candidate");
        var invalidRevision = (await app.SnapshotAsync()).Configuration.Revision; stale.StructuredPolicy.QueueCapacity = "bad"; await stale.SaveStructuredConfigurationAsync();
        Check(stale.ParameterEditor!.Issues.Any(i => i.Path == "QueueCapacity") && (await app.SnapshotAsync()).Configuration.Revision == invalidRevision, "field errors are locatable and static validation cannot save or contact a device");
        stale.StructuredPolicy.QueueCapacity = "64"; stale.StructuredPolicy.BackupEnabled = true;
        stale.StructuredPolicy.BackupIntervalMinutes = "bad"; stale.StructuredPolicy.BackupMaximumSets = "0";
        stale.ParameterEditor.ValidateFields();
        Check(stale.ParameterEditor.Issues.Any(i => i.Path == "BackupIntervalMinutes") && stale.ParameterEditor.Issues.Any(i => i.Path == "BackupMaximumSets") && !stale.ParameterEditor.Issues.Any(i => i.Path == "BackupDirectory") && stale.StructuredPolicy.BackupIntervalMinutes == "bad" && (await app.SnapshotAsync()).Configuration.Revision == invalidRevision,
            "backup numeric errors locate their actual fields, retain invalid text and never blame a valid directory or save");
        stale.StructuredPolicy.BackupEnabled = false;
        var heldProxy = DispatchProxy.Create<IManagementParcelFixture, SaveProbe>(); var heldProbe = (SaveProbe)(object)heldProxy; heldProbe.Inner = app; heldProbe.FailInspection = true;
        var heldVm = new ParcelViewModel(heldProxy, "校验期间编辑", "合成验证"); await heldVm.RefreshAsync(); await heldVm.LoadCapabilitiesAsync(); heldVm.ConfigurationReason = "Synthetic validation race";
        heldVm.StructuredPolicy!.TimeoutMs = "2000"; heldProbe.HoldValidation = true; var heldSave = heldVm.SaveStructuredConfigurationAsync(); await heldProbe.ValidationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        heldVm.StructuredPolicy.TimeoutMs = "2100"; heldProbe.ValidationRelease.TrySetResult(); await heldSave;
        heldProbe.FailInspection = false; await heldVm.InspectLastSaveAsync();
        Check(heldVm.HasUnsavedConfiguration && heldVm.StructuredPolicy.TimeoutMs == "2100" && (await app.SnapshotAsync()).Configuration.Pending!.TimeoutMs == 2000, "late authority inspection accepts only the pre-validation submission while newer input remains unsaved");
        heldVm.StructuredPolicy.TimeoutMs = "2000"; Check(!heldVm.HasUnsavedConfiguration && heldProbe.StageCalls == 1, "returning to the inspected original candidate restores its exact clean baseline without resubmission");
        var previewRevision = (await app.SnapshotAsync()).Configuration.Revision;
        var policy = new BarcodeFilterPolicy("OK[0-9]+"); var preview = await app.TestBarcodeFilterAsync(policy, ["bad", "OK003"]);
        Check(preview.SelectedCode == "OK003" && (await app.SnapshotAsync()).Configuration.Revision == previewRevision, "sample test shares the bounded matcher and has no configuration side effect");
        factory.Emit(reader, "Read", "third-entity", ["OK003"], "duplicate-original");
        var duplicate = await Wait(app, s => s.Inputs!.Any(x => x.Observation.SourceEventId == "duplicate-original"));
        Check(duplicate.Inputs!.Single(x => x.PositionId == "left").Outcome == ParcelInputOutcome.Duplicate && Wire.Encode(duplicate.Operations.Single(o => o.Id == original.Id).Wcs) == frozen, "existing entity uses frozen policy while paused and cannot replay its original request");
    }
    public class SaveProbe : DispatchProxy
    {
        public IParcelApplication Inner = null!; public bool FailInspection, HoldValidation; public int StageCalls;
        public TaskCompletionSource ValidationEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), ValidationRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<BusinessReceipt> Validate(ParcelConfiguration candidate)
        { if (HoldValidation) { HoldValidation = false; ValidationEntered.TrySetResult(); await ValidationRelease.Task; } return await Inner.ValidateConfigurationAsync(candidate); }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IParcelApplication.InspectConfigurationSubmissionAsync) && FailInspection) throw new IOException("Synthetic authority read failure");
            if (method.Name == nameof(IParcelApplication.ValidateConfigurationAsync)) return Validate((ParcelConfiguration)args![0]!);
            if (method.Name == nameof(IParcelApplication.StageConfigurationAsync)) StageCalls++;
            try { return method.Invoke(Inner, args); } catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
        }
    }
}

static partial class SportmasterRcs
{
    public static void ParameterStaticChecks(string workspace)
    {
        var binding = Binding("http://127.0.0.1:1/rcs/sorter/api");
        var c = HeartbeatConfiguration(workspace, binding);
        var launch = new BusinessLaunch(workspace, Path.Combine(Path.GetTempPath(), "ipc-static-" + Wire.Id()), null, null);
        void Reject(ParcelConfiguration candidate, string expected)
        {
            var factory = new ParcelCapabilityFactory(launch);
            try { factory.ValidateStructure(candidate); throw new Exception("Expected static preflight rejection: " + expected); }
            catch (InvalidDataException ex) when (ex.Message.Contains(expected, StringComparison.Ordinal)) { Console.WriteLine("PASS PARAM01: save-time static preflight rejects " + expected + " without constructing a component"); }
        }
        Reject(c with { StationCode = null }, "StationCode");
        var duplicate = binding with { Parameters = new(binding.Parameters) { ["right-bcr-code"] = binding.Parameters["left-bcr-code"] } };
        Reject(c with { Wcs = duplicate }, "distinct");
        var missing = binding with { Parameters = new(binding.Parameters.Where(p => p.Key != "left-bcr-code")) };
        Reject(c with { Wcs = missing }, "left-bcr-code");
        Reject(c with { Wcs = binding with { Target = "http://127.0.0.1:1/wrong" } }, "/rcs/sorter/api");
    }
}
