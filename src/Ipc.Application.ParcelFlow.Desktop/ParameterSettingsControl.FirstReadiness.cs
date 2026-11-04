using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Ipc.Application.ParcelFlow.Desktop;

// SM first configuration only: the planned sources on step one and, on the completion step,
// what the draft has per source plus an optional network-layer check. Session state only:
// the choice is not saved and never edits a field.
public sealed partial class ParameterSettingsControl
{
    FirstConfigurationScenario? scenario;
    readonly StackPanel readinessLines = new();
    readonly TextBlock readinessSummary = Themed(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Body, FontWeight = UiTypography.Emphasis, Margin = new(0, 0, 0, 8) }, TextBlock.ForegroundProperty, "Ink");
    readonly StackPanel networkResults = new() { Margin = new(0, 8, 0, 0) };
    string? networkFingerprint;
    bool networkChecking, readinessHooked;

    bool UsesFirstReadiness => FirstConfiguration && Model?.ApplicationId == "sportmaster";

    CameraSlot[] CameraSlots() => (Model?.Readers ?? []).Where(r => r.IsSimpleCamera).Select(r => new CameraSlot(r.Role,
        r.Parameters.FirstOrDefault(p => p.Field.Key == "device.ip")?.Value ?? "", r.Parameters.FirstOrDefault(p => p.Field.Key == "device.serial")?.Value ?? "")).ToArray();
    string RcsTarget() => Model?.Components.FirstOrDefault(ParameterOwnership.IsRcs)?.Target ?? "";

    FrameworkElement ScenarioCard()
    {
        scenario ??= FirstConfigurationReadiness.Infer(CameraSlots());
        var panel = new StackPanel();
        panel.Children.Add(Text("计划使用的来源", true));
        panel.Children.Add(Text("只用来提示哪些设备需要填写；不会自动创建设备，也不会清空已填的内容。以后可在设置里随时补配相机。"));
        foreach (var option in FirstConfigurationReadiness.Scenarios)
        {
            var choice = new RadioButton
            {
                GroupName = "FirstConfigurationScenario", IsChecked = option == scenario, Margin = new(0, 8, 0, 0), MinHeight = 44, ContentTemplate = null,
                Content = new StackPanel { Children = { Text(FirstConfigurationReadiness.Name(option), true), Text(FirstConfigurationReadiness.Description(option)) } }
            };
            AutomationProperties.SetName(choice, FirstConfigurationReadiness.Name(option));
            choice.Checked += (_, _) => { scenario = option; RefreshReadiness(); };
            panel.Children.Add(choice);
        }
        var card = Card(panel); card.Margin = new(0, 16, 0, 8); return card;
    }

    FrameworkElement ReadinessCard()
    {
        foreach (var content in new FrameworkElement[] { readinessSummary, readinessLines, networkResults })
            if (content.Parent is Panel previous) previous.Children.Remove(content);
        var panel = new StackPanel();
        panel.Children.Add(Text("各来源情况", true));
        AutomationProperties.SetLiveSetting(readinessSummary, AutomationLiveSetting.Polite);
        panel.Children.Add(readinessSummary); panel.Children.Add(readinessLines);
        var check = new Button { Content = "检查网络连接", MinHeight = 38, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 12, 0, 0), Style = (Style)Resources["ParameterQuietAction"] };
        AutomationProperties.SetHelpText(check, "只检查网络能否到达相机和 RCS 地址，不连接相机、不发送取格口请求。");
        check.Click += async (_, _) => await CheckNetwork(check);
        panel.Children.Add(check);
        panel.Children.Add(Text("网络检查只说明地址能否到达；相机身份、采图和 RCS 心跳在保存并启动后确认。"));
        panel.Children.Add(networkResults);
        var card = Card(panel); card.Margin = new(0, 0, 0, 16); return card;
    }

    void RefreshReadiness()
    {
        if (!UsesFirstReadiness) return;
        var cameras = CameraSlots(); var target = RcsTarget();
        var lines = FirstConfigurationReadiness.Lines(scenario ??= FirstConfigurationReadiness.Infer(cameras), cameras, target);
        readinessSummary.Text = FirstConfigurationReadiness.Summary(lines);
        readinessLines.Children.Clear();
        foreach (var line in lines)
        {
            var row = new DockPanel { Margin = new(0, 4, 0, 4) };
            var source = Text(line.Source); source.Width = 96; source.FontWeight = UiTypography.Emphasis; DockPanel.SetDock(source, Dock.Left); row.Children.Add(source);
            var state = Text(line.State); state.SetResourceReference(TextBlock.ForegroundProperty, line.NeedsAttention ? "Warning" : "Muted"); row.Children.Add(state);
            readinessLines.Children.Add(row);
        }
        if (networkFingerprint is not null && networkFingerprint != FirstConfigurationReadiness.Fingerprint(cameras, target))
        {
            networkFingerprint = null; networkResults.Children.Clear();
            networkResults.Children.Add(Text("地址已修改，上次网络检查结果已作废，请重新检查。"));
        }
    }

    async Task CheckNetwork(Button button)
    {
        if (networkChecking) return;
        var cameras = CameraSlots(); var target = RcsTarget();
        networkChecking = true; button.IsEnabled = false; networkResults.Children.Clear();
        networkResults.Children.Add(Text("正在检查网络（每项最多 10 秒）…"));
        try
        {
            var results = await FirstConfigurationReadiness.CheckNetworkAsync(cameras, target, CancellationToken.None);
            networkResults.Children.Clear();
            if (results.Length == 0) networkResults.Children.Add(Text("还没有填写相机或 RCS 地址，无需检查。"));
            foreach (var result in results)
            {
                var line = Text(result.Source + "（" + result.Target + "）：" + result.Text);
                line.SetResourceReference(TextBlock.ForegroundProperty, result.Reached ? "Success" : "Warning");
                networkResults.Children.Add(line);
            }
            networkResults.Children.Add(Text("检查时间 " + DateTime.Now.ToString("HH:mm:ss")));
            networkFingerprint = FirstConfigurationReadiness.Fingerprint(cameras, target);
        }
        finally { networkChecking = false; button.IsEnabled = true; }
    }
}
