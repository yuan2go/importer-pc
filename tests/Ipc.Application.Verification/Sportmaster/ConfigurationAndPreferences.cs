using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

using static SmContractFixture;

internal static class ConfigurationAndPreferences
{
    public static async Task Templates(string workspace, string root)
    {
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        var flow = new Ipc.Application.Sportmaster.SportmasterFlow();
        Check(flow.Validate(config with { Topology = config.Topology! with { Readers = null! }, DeferredConnections = [config.Reader.DeviceId] }).Length > 0,
            "missing topology collection returns configuration errors before connection validation");
        Check(flow.Validate(config with { BackupSchedule = new(null!, 15, 5) }).Length > 0, "missing backup directory is a configuration error, not a null dereference");

        await using var h = new ManualSupplyHarness(workspace, root, deferred: false, mode: RunMode.Simulation);
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        var vm = new ParcelViewModel(h.App, "参数", "合成 CR 回归");
        await vm.RefreshAsync(); await vm.LoadCapabilitiesAsync();
        var before = vm.BuildStructuredConfiguration();
        var originals = ParcelBindings.Readers(before).ToDictionary(r => r.DeviceId, r => Wire.Encode(r.Parameters));
        var template = vm.ExportSiteTemplate();
        template.Defaults.Values["policy.ReadTimeoutMs"] = "1700";
        vm.ApplySiteTemplate(vm.PreviewSiteTemplate(template));
        var after = vm.BuildStructuredConfiguration();
        Check(after.EffectiveReadTimeoutMs == 1700 && ParcelBindings.Readers(after).All(r => originals[r.DeviceId] == Wire.Encode(r.Parameters)),
            "template policy applies while each camera retains its own IP, serial and recognition parameters despite different editor row IDs");
        Check((await h.App.SnapshotAsync()).Configuration.Revision == vm.Snapshot!.Configuration.Revision && h.Factory.CreateCalls == 1 && h.Peer.Requests.IsEmpty,
            "template application changes only the complete draft, with no device reconnect, save or business request");
    }

    public static async Task Preferences(string workspace, string root)
    {
        var originalRoot = PresentationPreferences.UserRootForVerification;
        var userRoot = Path.Combine(root, "preferences");
        PresentationPreferences.UserRootForVerification = () => userRoot;
        try
        {
            var directory = Directory.CreateDirectory(Path.Combine(userRoot, "presentation")).FullName;
            foreach (var (surface, property, text) in new[]
            {
                ("window", "WindowOpeningMode", "{\"Schema\":2,\"Mode\":\"Window\"}"),
                ("pace", "ShowPace", "{\"Schema\":2,\"Compact\":true}"),
                ("audio", "CueVolume", "{\"Schema\":1,\"Volume\":101}")
            })
            {
                var file = Path.Combine(directory, "sportmaster." + surface + ".json"); File.WriteAllText(file, text);
                var draft = new SmDisplayPreferenceDraft();
                Check(!draft.CanEdit(property) && draft.ReadFailure(property).Length > 0 && File.ReadAllText(file) == text,
                    "invalid " + surface + " preference remains readable as a protected draft failure without replacing its original bytes");
                File.Delete(file);
            }
            var preferences = new SmDisplayPreferenceDraft { CueVolume = 40, ShowPace = false };
            var audio = Path.Combine(directory, "sportmaster.audio.json"); File.WriteAllText(audio, "{\"Schema\":1,\"Volume\":101}");
            var saved = preferences.Save();
            Check(saved.Single(r => r.Path == "CueVolume").State == "Unknown" && saved.Single(r => r.Path == "ShowPace").State == "Saved" &&
                preferences.HasChanges && File.ReadAllText(audio).Contains("101", StringComparison.Ordinal),
                "invalid stored audio reports a per-item failure while another preference saves independently");
            File.WriteAllText(audio, "{\"Schema\":1,\"Volume\":0}");
            Check(preferences.Save() is [{ Path: "CueVolume", State: "Saved" }] && !preferences.HasChanges && PresentationPreferences.ReadCueVolume("sportmaster") == 40,
                "preference retry saves only the failed item and retains the earlier successful item");
            await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "station"), deferred: true, mode: RunMode.Simulation);
            await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
            var before = await h.App.SnapshotAsync();
            var ledger = Path.Combine(DirectoryFor(h.Launch, h.Config), "configuration.json"); var original = File.ReadAllBytes(ledger);
            try
            {
                File.WriteAllText(ledger, new string(' ', 4 * 1024 * 1024 + 1));
                var inspection = await h.App.InspectConfigurationSubmissionAsync(before.RuntimeId, before.Configuration.Revision, before.Configuration.Effective);
                Check(inspection.Status == "Unknown" && new FileInfo(ledger).Length == 4 * 1024 * 1024 + 1,
                    "oversized authoritative envelope returns unknown submission status and preserves the original evidence");
            }
            finally { File.WriteAllBytes(ledger, original); }
            var vm = new ParcelViewModel(h.App, "参数", "合成 CR 回归");
            await vm.RefreshAsync(); await vm.LoadCapabilitiesAsync(); vm.LoadWindowPreference("sportmaster");
            vm.StructuredPolicy!.ReadTimeoutMs = "1700"; vm.SmPreferences.CueVolume = 101;
            Check(!await vm.PrepareConfigurationReviewAsync() && !vm.CanSubmitReviewedConfiguration &&
                vm.SmPreferences.CueVolume == 101 && (await h.App.SnapshotAsync()).Configuration.Revision == before.Configuration.Revision,
                "combined business/preference review rejects an invalid volume before any business commit and preserves both drafts");
        }
        finally { PresentationPreferences.UserRootForVerification = originalRoot; }
    }

    internal static async Task EditorIdentity(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, root, deferred: false, mode: RunMode.Simulation);
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        var catalog = await h.App.CapabilitiesAsync(); var binding = h.Config.Wcs!;
        var draft = new CapabilityDraft("integration", catalog.Providers, binding with { Contract = "different-contract" }, true);
        Check(draft.Provider is null, "exact provider lookup includes its contract identity");
        var original = new CapabilityDraft("integration", catalog.Providers, binding with { Parameters = new(binding.Parameters) { ["unknown-original"] = "must not resurrect" } }, true);
        var provider = original.Provider!;
        original.Provider = null; original.Provider = provider;
        foreach (var parameter in original.Parameters)
            if (binding.Parameters.TryGetValue(parameter.Field.Key, out var value)) parameter.Value = value;
        var built = Wire.Encode(original.Build());
        Check(Wire.Encode(CapabilityDraft.RestoreRaw(original.CaptureRaw(), catalog.Providers).Build()) == built && !built.Contains("must not resurrect", StringComparison.Ordinal),
            "provider reset survives encrypted draft capture and restore without resurrecting old parameters");
        var startup = new StartupConfigurationDraft(h.Config, catalog);
        var editor = startup.CreateParameterEditor((policy, codes) => Task.FromResult(BarcodeFiltering.Select(policy, codes)));
        var camera = startup.Readers[0]; var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.CameraSerialReader = (_, _) => pending.Task;
        var originalParameters = camera.Parameters.ToDictionary(p => p.Field.Key, p => p.Value);
        var reading = editor.ReadCameraIdentityAsync(camera); var cameraProvider = camera.Provider;
        camera.Provider = null; camera.Provider = cameraProvider;
        foreach (var parameter in camera.Parameters) parameter.Value = originalParameters[parameter.Field.Key];
        pending.SetResult("SYNTH-DETACHED-SERIAL");
        try { await reading; throw new Exception("Detached serial accepted"); }
        catch (InvalidOperationException error) when (error.Message.Contains("读取期间", StringComparison.Ordinal)) { }
        Check(camera.Parameters.Single(p => p.Field.Key == "device.serial").Value != "SYNTH-DETACHED-SERIAL", "late camera identity cannot write a detached parameter collection");
    }
}
