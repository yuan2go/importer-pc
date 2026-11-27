using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Ipc.Application.Contracts;
namespace Ipc.Application.ParcelFlow.Desktop;

public sealed partial class ParameterSettingsControl
{
    public Func<string, FrameworkElement>? CameraPreviewFactory { get; set; }

    static Border ActivationBadge(string label, string background) => Themed(new Border
    {
        Child = Themed(new TextBlock { Text = label, FontSize = UiTypography.Diagnostic }, TextBlock.ForegroundProperty, "Muted"),
        Padding = new(6, 2, 6, 2), CornerRadius = new(8), Margin = new(6, 0, 0, 4), VerticalAlignment = VerticalAlignment.Center
    }, Border.BackgroundProperty, background);

    DockPanel ReconnectHeading(TextBlock heading, ParameterFormPanel[] forms)
    {
        var header = new DockPanel { Margin = new(0, 0, 0, 8) };
        var badge = ActivationBadge(FirstConfiguration ? "启动时连接" : "修改后自动重连生效", "AccentBackground");
        badge.ToolTip = FirstConfiguration ? "进入工作站后校验已填写的连接。"
            : "修改已配置连接时可自动重连。新增或移除连接需重启，具体以保存结果为准。";
        badge.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(badge, Dock.Right);
        header.Children.Add(badge);
        heading.Margin = new(0);
        heading.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(heading);
        // Ownership filtering and reparenting finish before the heading summarizes these fields.
        groupActivationLabels.Add(() =>
        {
            foreach (var form in forms)
                foreach (UIElement field in form.Children)
                    if (reconnectBadges.TryGetValue(field, out var individual))
                    {
                        individual.Visibility = Visibility.Collapsed;
                        badge.Visibility = Visibility.Visible;
                    }
        });
        return header;
    }

    void ArrangeSportmasterPages()
    {
        var station = pages.First(p => p.Title == "供包台标识");
        {
            station.Title = "本站与 RCS";
            if (station.Body.Children.OfType<ParameterFormPanel>().FirstOrDefault() is { } identity)
                identity.SectionHeader = ReconnectHeading(Text("本站身份", true), [identity]);
            var service = Section(station, "RCS 服务");
            var protocol = AdvancedForm(station, "协议与心跳");
            foreach (var component in Model!.Components.Where(ParameterOwnership.IsRcs))
            {
                var rcs = pages.FirstOrDefault(p => p.Title == "RCS 服务");
                if (rcs is null) continue;
                foreach (var pair in locations.Where(x => x.Key.StartsWith(component.InstanceId + "/", StringComparison.Ordinal)).ToArray())
                {
                    var location = pair.Value;
                    if (ReferenceEquals(location.Page, station) || location.Row is null) continue;
                    var key = pair.Key[(component.InstanceId.Length + 1)..];
                    if (key.EndsWith("-bcr-code", StringComparison.Ordinal)) continue;
                    var target = key is "Provider" or "Target" or RcsRequestPolicy.DestinationMode or RcsRequestPolicy.DestinationChuteCode or "no-dest-to-exp" ? service.Form : protocol;
                    if (location.Row.Parent is Panel parent) parent.Children.Remove(location.Row);
                    target.Children.Add(location.Row);
                    locations[pair.Key] = location with { Page = station };
                }
                foreach (var running in rcs.Body.Children.OfType<Border>().ToArray())
                { rcs.Body.Children.Remove(running); station.Body.Children.Insert(station.Body.Children.IndexOf(service.Heading), running); }
                rcs.Applicable = () => false;
            }
            station.Body.Children.Remove(service.Heading);
            service.Form.SectionHeader = ReconnectHeading(service.Heading, [service.Form]);
            if (protocol.Parent is Expander folded)
            {
                if (protocol.Children.Count == 0) station.Body.Children.Remove(folded);
                else folded.Header = ReconnectHeading(Text("协议与心跳", true), [protocol]);
            }
        }
        var cameraPages = pages.Where(p => p.Category == 2 && p.Title is "左相机" or "右相机").ToArray();
        if (cameraPages.Length > 0)
        {
            var combined = Page(2, "左右相机", "填写相机 IP，保存时自动读取序列号。不用的相机请清空 IP。");
            var columns = new Grid(); columns.ColumnDefinitions.Add(new()); columns.ColumnDefinitions.Add(new()); columns.RowDefinitions.Add(new() { Height = GridLength.Auto }); columns.RowDefinitions.Add(new() { Height = GridLength.Auto });
            if (Audience != SmParameterAudience.Technical && Model is { CameraDiscoverer: not null } cameraModel)
                AddCameraDiscovery(combined, cameraModel);
            combined.Body.Children.Add(columns);
            foreach (var (page, index) in cameraPages.Select((p, i) => (p, i)))
            {
                foreach (var note in page.Body.Children.OfType<TextBlock>().Where(t => t.Text.StartsWith("填写要使用的相机", StringComparison.Ordinal) || t.Text.StartsWith("填写相机 IP", StringComparison.Ordinal) || t.Text.StartsWith("相机连续读码", StringComparison.Ordinal)).ToArray()) page.Body.Children.Remove(note);
                var cameraDraft = Model!.Readers.First(r => (r.Role == "left" ? "左相机" : "右相机") == page.Title);
                if (SmParameterOwnership.Show(Audience, cameraDraft.InstanceId + "/Provider") && cameraDraft.Provider?.Implementation == "hikrobot-mvs-reader" && locations.TryGetValue(cameraDraft.InstanceId + "/Provider", out var provider) && provider.Row is { } providerRow && providerRow.Parent is Panel providerParent)
                {
                    providerParent.Children.Remove(providerRow);
                    if (provider.Control.Parent is Panel identityParent) identityParent.Children.Remove(provider.Control);
                    if (provider.Control is TextBox identity) { identity.MinHeight = 0; identity.BorderThickness = new(0); identity.Padding = new(0); identity.FontSize = UiTypography.Label; identity.Background = System.Windows.Media.Brushes.Transparent; }
                    page.Body.Children.Insert(1, provider.Control);
                }
                page.Scroll.Content = null; page.Body.Margin = new(0, 0, 16, 0); page.Body.MaxWidth = double.PositiveInfinity; page.Body.HorizontalAlignment = HorizontalAlignment.Stretch;
                // Detached source scrolls must no longer dictate the embedded form's width.
                page.Body.ClearValue(WidthProperty);
                if (!FirstConfiguration && CameraPreviewFactory is { } preview)
                {
                    page.Body.Children.Add(new Expander { Header = "实时预览", Content = preview(cameraDraft.InstanceId), Margin = new(0, 8, 0, 0) });
                }
                if (page.Body.Children[0] is TextBlock cameraHeading)
                {
                    var forms = page.Body.Children.OfType<ParameterFormPanel>().Concat(
                        page.Body.Children.OfType<Expander>().Select(expander => expander.Content).OfType<ParameterFormPanel>()).ToArray();
                    page.Body.Children.Remove(cameraHeading);
                    page.Body.Children.Insert(0, ReconnectHeading(cameraHeading, forms));
                }
                var card = Card(page.Body); card.Margin = index == 0 ? new(0, 0, 8, 8) : new(8, 0, 0, 8);
                page.Body.Margin = new(0);
                columns.Children.Add(card); Grid.SetColumn(card, index); page.Applicable = () => false;
                foreach (var pair in locations.Where(x => ReferenceEquals(x.Value.Page, page)).ToArray()) locations[pair.Key] = pair.Value with { Page = combined };
            }
            columns.SizeChanged += (_, _) =>
            {
                var narrow = columns.ActualWidth < 580; columns.ColumnDefinitions[1].Width = narrow ? new(0) : new(1, GridUnitType.Star);
                for (var i = 0; i < cameraPages.Length; i++) { Grid.SetColumn(columns.Children[i], narrow ? 0 : i); Grid.SetRow(columns.Children[i], narrow ? i : 0); }
            };
        }
        if (FirstConfiguration)
        {
            EnsureReviewPage();
            var optional = new StackPanel();
            foreach (var source in pages.Where(p => p.Title is "数据与记录" or "自动备份" or "显示偏好").ToArray())
            {
                source.Scroll.Content = null; source.Body.Margin = new(0); source.Body.ClearValue(WidthProperty);
                optional.Children.Add(source.Body); source.Applicable = () => false;
                foreach (var pair in locations.Where(x => ReferenceEquals(x.Value.Page, source)).ToArray())
                    locations[pair.Key] = pair.Value with { Page = reviewPage! };
            }
            reviewPage!.Body.Children.Insert(2, new Expander { Header = "维护与显示（使用默认值，按需展开）", Content = optional, Margin = new(0, 16, 0, 8) });
        }
        if (StationSettingsOnly)
        {
            var display = pages.Single(p => p.Title == "显示偏好");
            display.Title = "显示与提示音";
            station.NavigationTitle = "1. 本站与 RCS";
            var cameras = pages.FirstOrDefault(p => p.Title == "左右相机");
            if (cameras is not null) cameras.NavigationTitle = "2. 左右相机";
            display.NavigationTitle = "3. 显示与提示音";
            foreach (var page in pages.Where(p => p.NavigationTitle is null)) page.Applicable = () => false;
        }
        foreach (var page in pages)
            page.TaskGroup = FirstConfiguration ? page.Category switch { 0 => "本站与 RCS", 1 => "识读规则", 2 => "左右相机", 3 => "本站与 RCS", _ => "检查与完成" } :
                ReferenceEquals(page, station) ? "本站接入" : page.Category == 2 ? "相机" : page.Title == "条码筛选" ? "分拣规则" : page.Title is "数据与记录" or "自动备份" or "显示偏好" or "显示与提示音" ? "系统维护" : "高级";
    }
}
