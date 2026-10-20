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
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        Check(!Wire.Encode(c).Contains("BarcodeFilter") && Wire.Encode(Wire.Decode<ParcelConfiguration>(Wire.Encode(c))) == Wire.Encode(c), "missing legacy policy is omitted and round-trips without manufacturing a production rule");
        Check(BarcodeFiltering.Select(null, ["X", "Y"]).Outcome == "MultipleCodes", "legacy policy retains strict single-code compatibility");
        Check(BarcodeFiltering.Select(new("OK[0-9]+"), ["bad", "noise", "OK003"]).SelectedCode == "OK003", "third candidate is evaluated and original order is retained");
        Check(BarcodeFiltering.Select(new("OK[0-9]+"), ["preOK003", "OK003tail"]).Outcome == "NoMatchingCode", "matching is whole-string rather than substring");
        Check(BarcodeFiltering.Select(new("OK[0-9]+"), ["OK003", "OK004"]).Outcome == "AmbiguousCodes", "multiple valid candidates are ambiguous");
        Check(BarcodeFiltering.Select(new("a|ab"), ["ab"]).SelectedCode == "ab", "whole-string alternation is evaluated correctly");
        Check(BarcodeFiltering.Select(new("["), ["X"]).Outcome == "InvalidPattern", "invalid syntax produces a bounded rejection");
        Check(BarcodeFiltering.Select(new("(a+)+$"), [new string('a', 500) + "!"]).Outcome == "FilterTimeout", "catastrophic backtracking times out and does not select a code");
        Check(BarcodeFiltering.Select(new("OK.*"), ["OK003"], "truncated").Outcome == "IncompleteCandidates" && BarcodeFiltering.Select(new("OK.*"), Enumerable.Repeat("noise", 300).Append("OK003").ToArray()).Outcome == "IncompleteCandidates", "incomplete and oversized candidate sets can never imply uniqueness");
        Check(new SportmasterFlow().Validate(c with { BarcodeFilter = new("[") }).Any(x => x.Contains("BarcodeFilter.Pattern")), "business configuration validation rejects malformed rules before save");
        var path = Path.Combine(root, "parameters"); Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), c with { BarcodeFilter = new("OK[0-9]+") });
        var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null);
        var factory = new UxCommercialInputs.InputFactory(new ParcelCapabilityFactory(launch));
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } };
        await app.StartAsync(default);
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
        var before = await app.SnapshotAsync(); await app.CommandAsync(new(Wire.Id(), before.Revision, "Pause")); await app.AuthorizeMaintenanceAsync("maintenance", "SIMULATION_ONLY");
        await Wait(app, s => s.Availability!.Configure);
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
        var proxy = DispatchProxy.Create<IParcelApplication, SaveProbe>(); var probe = (SaveProbe)(object)proxy; probe.Inner = app; probe.FailInspection = true;
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
        var policy = new BarcodeFilterPolicy("OK[0-9]+"); var preview = await app.TestBarcodeFilterAsync(policy, ["bad", "OK003"]);
        Check(preview.SelectedCode == "OK003" && (await app.SnapshotAsync()).Configuration.Revision == invalidRevision, "sample test shares the bounded matcher and has no configuration side effect");
        factory.Emit(reader, "Read", "third-entity", ["OK003"], "duplicate-original");
        var duplicate = await Wait(app, s => s.Inputs!.Any(x => x.Observation.SourceEventId == "duplicate-original"));
        Check(duplicate.Inputs!.Single(x => x.PositionId == "left").Outcome == ParcelInputOutcome.Duplicate && Wire.Encode(duplicate.Operations.Single(o => o.Id == original.Id).Wcs) == frozen, "existing entity uses frozen policy while paused and cannot replay its original request");
    }
    public class SaveProbe : DispatchProxy
    {
        public IParcelApplication Inner = null!; public bool FailInspection; public int StageCalls;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IParcelApplication.InspectConfigurationSubmissionAsync) && FailInspection) throw new IOException("Synthetic authority read failure");
            if (method.Name == nameof(IParcelApplication.StageConfigurationAsync)) StageCalls++;
            try { return method.Invoke(Inner, args); } catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
        }
    }
}
