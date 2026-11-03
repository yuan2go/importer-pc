using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;

static partial class Program
{
    static async Task VerifyStartupDefaultsUi(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS STARTUP-DEFAULTS " + text); }
        Check(Ipc.Platform.PresentationPreferences.ReadCueVolume("sportmaster") == 0, "new user preferences default to muted operator cues");
        var initial = new SportmasterFlow().InitialConfiguration();
        var seed = Ipc.Platform.Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json")));
        string[] removed = ["request-id-base", "request-id-count", "specified-chute", "chute-code"];
        Check(new[] { initial, seed }.All(c => !c.Wcs!.Parameters.Keys.Intersect(removed).Any()),
            "generated and packaged new stations contain neither request ranges nor fixed-chute configuration");
        await using var factory = new ParcelCapabilityFactory(new(Path.Combine(root, "config/sportmaster"), Path.Combine(evidence, "data"), null, null));
        var catalog = factory.Catalog();
        var incomplete = initial.Wcs! with { Parameters = new() { ["left-station-code"] = "NEW-STATION", ["right-station-code"] = "NEW-STATION" } };
        var completing = new CapabilityDraft("integration", catalog.Providers, incomplete, true) { ConfigureLater = false, Target = "http://127.0.0.1:18849/rcs/sorter/api" };
        foreach (var parameter in completing.Parameters.Where(p => p.Field.Key is "zone-id" or "project-id")) parameter.Value = "1";
        var built = completing.Build();
        Check(!built.Parameters.Keys.Intersect(removed).Any() && !built.Parameters.ContainsKey("request-timeout-ms"),
            "completing a deferred binding needs no retired options and preserves omitted optional defaults");
        var retired = incomplete with { Parameters = new(incomplete.Parameters) { ["request-id-base"] = "40", ["request-id-count"] = "20", ["specified-chute"] = "true", ["chute-code"] = "OLD" } };
        var clean = new CapabilityDraft("integration", catalog.Providers, retired, true);
        Check(!clean.Parameters.Any(p => removed.Contains(p.Field.Key)) && !clean.Build().Parameters.Keys.Intersect(removed).Any(),
            "saving an existing draft removes retired fields without exposing replacement editors");
        foreach (var firstRun in new[] { true, false })
        {
            var draft = new StartupConfigurationDraft(initial, catalog);
            var rcsDraft = draft.Capabilities.Single(c => c.Kind == "integration");
            var originalRcs = Ipc.Platform.Wire.Encode(rcsDraft.Build());
            rcsDraft.ProposeProvider(rcsDraft.Providers.Single(p => p.Implementation == "reference-integration"));
            var model = draft.CreateParameterEditor((policy, codes) => Task.FromResult(Ipc.Application.ParcelFlow.BarcodeFiltering.Select(policy, codes)));
            Check(!rcsDraft.ProviderChangePending && Ipc.Platform.Wire.Encode(rcsDraft.Build()) == originalRcs,
                "removed simulation proposal is cancelled without resetting the current RCS binding");
            var editor = new ParameterSettingsControl { FirstConfiguration = firstRun, Model = model };
            var client = new Grid(); client.Children.Add(editor);
            var window = new Window { Content = client, Title = firstRun ? "首次配置 · 默认值验证" : "日常参数 · 相机编码验证", Width = 1280, Height = 800 };
            NameScope.SetNameScope(window, new NameScope()); window.RegisterName("ClientArea", client);
            window.Show();
            async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
            try
            {
                await SetClient(window, 1280, 800);
                Check(!editor.ShowPage(1, "格口请求"), "fixed-chute page is absent in " + (firstRun ? "first configuration" : "daily settings"));
                Check(editor.ShowPage(firstRun ? 2 : 0, firstRun ? "左右相机" : "站点编号与 RCS"), "camera identity page is available");
                await Flush();
                foreach (var side in new[] { "left", "right" })
                {
                    var input = Descendants(editor).OfType<TextBox>().Single(t => AutomationProperties.GetAutomationId(t) == "sportmaster.integration/" + side + "-bcr-code");
                    var label = AutomationProperties.GetLabeledBy(input) as TextBlock;
                    Check(label?.Text.StartsWith(side == "left" ? "左相机编码" : "右相机编码", StringComparison.Ordinal) == true,
                        "visible and accessible label identifies " + side + " camera in " + (firstRun ? "first configuration" : "daily settings"));
                }
                CaptureInternal(evidence, firstRun ? "defaults-first-cameras" : "defaults-daily-cameras", client, lines);
                if (firstRun) Check(editor.ShowPage(3, "RCS 服务"), "first-run RCS range page is available");
                await Flush();
                Check(!Descendants(editor).Any(t => removed.Any(key => AutomationProperties.GetAutomationId(t).EndsWith("/" + key, StringComparison.Ordinal))),
                    "neither ranges nor fixed-chute controls exist in first configuration or daily settings");
                Check(!model.Components.Single(ParameterOwnership.IsRcs).Parameters.Any(p => removed.Contains(p.Field.Key)),
                    "retired fields are absent from the editor model, not merely hidden");
                Check(!Descendants(editor).OfType<ComboBox>().Any(c => AutomationProperties.GetAutomationId(c).EndsWith("/Provider", StringComparison.Ordinal)),
                    "SM RCS page has no connection provider selector");
                Check(model.Components.Single(ParameterOwnership.IsRcs).Providers.All(p => p.Implementation == "sportmaster-rcs"),
                    "SM editor offers only built-in RCS and no local simulation provider");
                CaptureInternal(evidence, firstRun ? "defaults-first-rcs" : "defaults-daily-rcs", client, lines);
                if (!firstRun)
                {
                    Check(editor.NavigateTo("CueVolume"), "daily audio preference is reachable");
                    await Flush();
                    var volume = Descendants(editor).OfType<Slider>().Single(s => AutomationProperties.GetAutomationId(s) == "CueVolume");
                    Check(volume.Value == 0, "actual unsaved cue volume slider starts at zero");
                    CaptureInternal(evidence, "defaults-daily-audio", client, lines);
                    Ipc.Platform.PresentationPreferences.SaveCueVolume("sportmaster", 30);
                    Check(Ipc.Platform.PresentationPreferences.ReadCueVolume("sportmaster") == 30, "an explicitly saved volume survives the new default");
                }
            }
            finally { window.Close(); }
        }
    }
}
