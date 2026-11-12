using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;

static class SmParameters
{
    static void Check(bool ok, string reason) { if (!ok) throw new Exception("FAIL SM-PARAM: " + reason); Console.WriteLine("PASS SM-PARAM: " + reason); }
    public static async Task Run(string workspace, string root)
    {
        Directory.CreateDirectory(root);
        var previous = PresentationPreferences.UserRootForVerification;
        PresentationPreferences.UserRootForVerification = () => Path.Combine(root, "preferences");
        try
        {
            var initial = new SportmasterFlow().InitialConfiguration();
            var launch = new BusinessLaunch(workspace, Path.Combine(root, "catalog"), null, null);
            var catalog = new ParcelCapabilityFactory(launch).Catalog();
            var startup = new StartupConfigurationDraft(initial, catalog, true);
            var editor = startup.CreateParameterEditor((p, codes) => Task.FromResult(BarcodeFiltering.Select(p, codes)));
            Check(startup.Readers.All(r => r.Parameters.Single(p => p.Field.Key == "reader.engine").Value == "mvid-native") &&
                RcsRequestPolicy.TimeoutMs(initial.Wcs!) == 1000 && initial.EffectiveReadTimeoutMs == 1000 && initial.Retention!.MinimumFreeBytes == 536870912,
                "new-station authoritative defaults: native reader, independent 1000ms read/request deadlines and 512MiB reserve");
            foreach (var timeout in new[] { "100", "1000", "30000" })
                Check(RcsRequestPolicy.TimeoutMs(initial.Wcs! with { Parameters = new(initial.Wcs!.Parameters) { ["request-timeout-ms"] = timeout } }) == int.Parse(timeout), "explicit legal request deadline " + timeout);
            foreach (var timeout in new[] { "0", "99", "30001", "abc", "" })
            {
                try { _ = RcsRequestPolicy.TimeoutMs(initial.Wcs! with { Parameters = new(initial.Wcs!.Parameters) { ["request-timeout-ms"] = timeout } }); throw new Exception("Accepted invalid deadline"); }
                catch (InvalidDataException) { Check(true, "invalid request deadline rejected: " + timeout); }
            }
            var rcs = startup.Capabilities.Single(ParameterOwnership.IsRcs);
            rcs.ConfigureLater = false; rcs.Target = "http://127.0.0.1:1/rcs/sorter/api";
            foreach (var p in rcs.Parameters.Where(p => p.Field.Key is "zone-id" or "project-id")) p.Value = "1";
            var deadline = rcs.Parameters.Single(p => p.Field.Key == "request-timeout-ms");
            deadline.Value = "";
            Check(!editor.ValidateFields() && editor.Issues.Any(i => i.Path == rcs.InstanceId + "/request-timeout-ms"), "an explicitly cleared numeric request deadline is immediately locatable rather than disguised as an omitted default");
            rcs.ConfigureLater = true; rcs.Target = ""; deadline.Value = "1000";
            foreach (var p in rcs.Parameters.Where(p => p.Field.Key is "zone-id" or "project-id")) p.Value = "";
            startup.Policy.SessionLifetimeMs = "not an SM setting"; startup.Policy.MaximumOperations = "not an SM limit";
            startup.Policy.QueueCapacity = "internal"; startup.Policy.TimeoutMs = "internal"; startup.Policy.Precision = "disabled";
            Check(editor.ValidateFields() && Wire.Encode(startup.Build()) == Wire.Encode(initial), "hidden SM login, disabled weight, unused operation count and internal budgets neither create edits nor unfixable validation failures");
            startup.Policy.ReadTimeoutMs = "99";
            Check(!editor.ValidateFields() && editor.Issues.Single().Path == "ReadTimeoutMs", "invalid automatic read deadline is precisely locatable");
            startup.Policy.ReadTimeoutMs = "1200";
            Check(editor.ValidateFields() && startup.Build().EffectiveReadTimeoutMs == 1200, "corrected read deadline reaches the complete candidate");
            startup.Policy.ReadTimeoutMs = "1000";
            startup.Policy.AutomaticHistoryCleanup = true; startup.Policy.HistoryDays = "";
            Check(!editor.ValidateFields() && editor.Issues.Any(i => i.Path == "HistoryDays"), "automatic cleanup cannot silently run without a cleanup horizon");
            startup.Policy.HistoryDays = "7"; startup.Policy.CleanupHour = "24";
            Check(!editor.ValidateFields() && editor.Issues.Any(i => i.Path == "CleanupHour"), "cleanup schedule range has a field error");
            startup.Policy.CleanupHour = "3";
            Check(editor.ValidateFields() && startup.Build().OperationsPolicy is { AutomaticHistoryCleanup: true, CleanupHour: 3, MaximumBackupSets: 7 }, "reachable maintenance draft builds the existing operations policy");
            var reader = startup.Readers[0]; reader.ConfigureLater = false;
            reader.Parameters.Single(p => p.Field.Key == "device.ip").Value = "192.0.2.10";
            reader.Parameters.Single(p => p.Field.Key == "device.serial").Value = "SYNTHETIC-PARAMETER-CAMERA";
            reader.Parameters.Single(p => p.Field.Key == "code.formats").Value = "not a software format list";
            reader.Parameters.Single(p => p.Field.Key == "code.try-invert").Value = "not a boolean";
            Check(editor.ValidateFields() && reader.BuildReader("sportmaster").Parameters["reader.engine"] == "mvid-native", "native schema and real candidate building ignore inactive software settings without overwriting them");
            reader.Parameters.Single(p => p.Field.Key == "reader.engine").Value = "zxing-cpp";
            Check(!editor.ValidateFields() && editor.Issues.Any(i => i.Path == reader.InstanceId + "/code.formats") && editor.Issues.Any(i => i.Path == reader.InstanceId + "/code.try-invert"), "explicit software selection exposes its own invalid parameters");
            reader.Parameters.Single(p => p.Field.Key == "reader.engine").Value = "mvid-native";
            var prefs = editor.DisplayPreferences!;
            prefs.ShowPace = false; prefs.CueVolume = 35; prefs.OpeningMode = PresentationPreferences.WindowOpeningMode.FullScreen;
            var capture = startup.Capture(); var restored = new StartupConfigurationDraft(initial, catalog, true).WithRestored(capture);
            Check(restored.DisplayPreferences!.Signature == prefs.Signature && restored.DisplayPreferences.HasChanges && PresentationPreferences.ReadCueVolume("sportmaster") == 0, "current encrypted draft captures all local preferences without applying them");
            var template = StartupSiteTemplates.Export("sportmaster", capture);
            Check(!template.Defaults.Values.Keys.Any(k => k.StartsWith("preference.")), "portable site templates never carry another Windows user's preference drafts");
            var dir = Path.Combine(root, "preferences", "presentation"); Directory.CreateDirectory(dir);
            var paceFile = Path.Combine(dir, "sportmaster.pace.json"); File.WriteAllText(paceFile, "{broken");
            var first = prefs.Save();
            Check(first.Single(x => x.Path == "ShowPace").State == "Unknown" && prefs.HasChanges && PresentationPreferences.ReadCueVolume("sportmaster") == 35 && PresentationPreferences.ReadWindowMode("sportmaster") == PresentationPreferences.WindowOpeningMode.FullScreen,
                "one damaged preference file preserves its bytes and failed draft while the other two save independently");
            Check(File.ReadAllText(paceFile) == "{broken" && prefs.Changes().Select(x => x.Path).SequenceEqual(["ShowPace"]), "partial result does not retain already successful preference changes for retry");
            File.WriteAllText(paceFile, Wire.Encode(new { Schema = 1, Compact = true }));
            var second = prefs.Save();
            Check(second.Length == 1 && second[0].Path == "ShowPace" && !prefs.HasChanges && new SmDisplayPreferenceDraft().Signature == prefs.Signature, "retry reconciles only the failed preference; reopening reads all successful values");
            prefs.CueVolume = 36;
            Action<string,int> brokenObserver = (_, _) => throw new IOException("synthetic observer failure after atomic commit");
            PresentationPreferences.CueVolumeChanged += brokenObserver;
            try { Check(prefs.Save().Single().State == "Saved" && !prefs.HasChanges && PresentationPreferences.ReadCueVolume("sportmaster") == 36, "post-commit notification failure is reconciled from the exact persisted target, not repeated"); }
            finally { PresentationPreferences.CueVolumeChanged -= brokenObserver; }
            await Runtime(workspace, root);
        }
        finally { PresentationPreferences.UserRootForVerification = previous; }
    }
    static async Task Runtime(string workspace, string root)
    {
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        seed = seed with { MaximumOperations = 1, Retention = (seed.Retention ?? new()) with { ResidentHistory = 3 } };
        var cfg = Path.Combine(root,"runtime"); Directory.CreateDirectory(cfg); ExtensionStore.AtomicWrite(Path.Combine(cfg,"business.json"),seed);
        var launch = new BusinessLaunch(cfg,Path.Combine(cfg,"data"),null,null);
        await using var app = new ParcelRuntime(launch,new SportmasterFlow(),new P0SimulationFactory(launch)) { RuntimeIdentityPreflight=()=>{} };
        await app.StartAsync(default); await ManagementModeFixture.Authorize(app);
        var vm = new ParcelViewModel(app,"参数","合成验证"); await vm.RefreshAsync(); await vm.LoadCapabilitiesAsync(); vm.LoadWindowPreference("sportmaster");
        Check(vm.Snapshot!.AutomaticCleanup is { Enabled:false,NextCheck:null,State:"未启用自动清理" } && vm.AutomaticCleanupStatusText.Contains("未启用"), "disabled cleanup is projected as off, without a fictitious next execution");
        var revision=vm.Snapshot.Configuration.Revision;
        vm.SmPreferences.CueVolume=40;
        Check(vm.HasUnsavedDisplayPreferences && vm.CanReviewConfiguration && await vm.PrepareConfigurationReviewAsync() && vm.ReviewedConfigurationChanges.Single().Path=="CueVolume", "daily audio participates in dirty state and review without business validation or an SDK lookup");
        await vm.SaveWindowPreferenceAsync(); await vm.RefreshAsync();
        Check(!vm.HasUnsavedDisplayPreferences && vm.Snapshot!.Configuration.Revision==revision && PresentationPreferences.ReadCueVolume("sportmaster")==40, "local save immediately applies to this Windows user and creates no business CAS revision");
        vm.StructuredPolicy!.ReadTimeoutMs="1100";
        Check(vm.GetConfigurationChangeItems().Any(i=>i.Path=="ReadTimeoutMs" && i.Before=="1000 毫秒" && i.After=="1100 毫秒"), "read deadline diff uses the same effective default, name and milliseconds as runtime");
        vm.StructuredPolicy.MinimumFreeMiB="512.5";
        var units=vm.GetConfigurationChangeItems();
        Check(units.Any(i=>i.Path=="MinimumFreeMiB" && i.Label=="磁盘预留" && i.Before=="64 MiB" && i.After=="512.5 MiB"),
            "review uses exact MiB rather than raw bytes, consistently with the editable disk reserve");
        for (var n = 0; n < 2; n++)
        {
            var code = "ARCHIVE-" + n;
            app.EmitReference("archive-" + n, code, Wire.Id());
            await CameraRoundHarness.Until(async () => (await app.SnapshotAsync()).Operations.Any(o => o.ParcelCode == code && o.State == ParcelState.SlotResolved));
            await SmRoundFixture.NextVehicleAsync(app, "left");
        }
        Check((await app.SnapshotAsync()).Operations.Length == 2, "SM resident archive threshold remains 3 despite the unused MaximumOperations value of 1; legal later parcels continue");
        await app.StopAsync(default);
    }
}
