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
        Check(new[] { initial, seed }.All(c => c.Wcs!.Parameters.GetValueOrDefault("request-id-base") == "0" && c.Wcs.Parameters.GetValueOrDefault("request-id-count") == "10000" && c.Wcs.Parameters.GetValueOrDefault("specified-chute") == "false"),
            "both generated and packaged new stations default to inclusive requests 1..10000 and no fixed chute");
        await using var factory = new ParcelCapabilityFactory(new(Path.Combine(root, "config/sportmaster"), Path.Combine(evidence, "data"), null, null));
        var catalog = factory.Catalog();
        var incomplete = initial.Wcs! with { Parameters = new() { ["left-station-code"] = "NEW-STATION", ["right-station-code"] = "NEW-STATION" } };
        var completing = new CapabilityDraft("integration", catalog.Providers, incomplete, true) { ConfigureLater = false, Target = "http://127.0.0.1:18849/rcs/sorter/api" };
        foreach (var parameter in completing.Parameters.Where(p => p.Field.Key is "zone-id" or "project-id")) parameter.Value = "1";
        var built = completing.Build();
        Check(built.Parameters.GetValueOrDefault("request-id-base") == "0" && built.Parameters.GetValueOrDefault("request-id-count") == "10000" && built.Parameters.GetValueOrDefault("specified-chute") == "false" && !built.Parameters.ContainsKey("request-timeout-ms"),
            "completing a deferred binding materializes required defaults while preserving omitted optional defaults");
        foreach (var firstRun in new[] { true, false })
        {
            var draft = new StartupConfigurationDraft(initial, catalog);
            var model = draft.CreateParameterEditor((policy, codes) => Task.FromResult(Ipc.Application.ParcelFlow.BarcodeFiltering.Select(policy, codes)));
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
                TextBox Range(string key) => Descendants(editor).OfType<TextBox>().Single(t => AutomationProperties.GetAutomationId(t) == "sportmaster.integration/" + key);
                Check(Range("request-id-base").Text == "1" && Range("request-id-count").Text == "10000", "actual inclusive range editors start at 1 and end at 10000");
                Check(!Descendants(editor).Any(t => AutomationProperties.GetAutomationId(t).EndsWith("/specified-chute", StringComparison.Ordinal) || AutomationProperties.GetAutomationId(t).EndsWith("/chute-code", StringComparison.Ordinal)),
                    "fixed-chute switch and code have no editable controls");
                var rcs = model.Components.Single(ParameterOwnership.IsRcs);
                Check(rcs.Parameters.Single(p => p.Field.Key == "specified-chute").Value == "false", "hidden new-station fixed-chute value is explicitly false");
                CaptureInternal(evidence, firstRun ? "defaults-first-rcs" : "defaults-daily-rcs", client, lines);
                rcs.Parameters.Single(p => p.Field.Key == "request-id-base").Value = "40";
                rcs.Parameters.Single(p => p.Field.Key == "request-id-count").Value = "20";
                await Flush();
                Check(Range("request-id-base").Text == "41" && Range("request-id-count").Text == "60", "explicitly allocated ranges remain editable and are never replaced by startup defaults");
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
