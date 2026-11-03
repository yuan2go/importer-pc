using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.ComponentModel;
using System.Globalization;
namespace Ipc.Application.ParcelFlow.Desktop;

public sealed partial class ParameterSettingsControl
{
    public Func<string, FrameworkElement>? CameraPreviewFactory { get; set; }
    void ArrangeSportmasterPages()
    {
        var station = pages.First(p => p.Title == "供包台标识");
        if (!FirstConfiguration)
        {
            station.Title = "站点编号与 RCS";
            // One page in reading order: who this station is, how RCS tells the two cameras apart,
            // where RCS is and which request numbers it allotted; protocol details stay folded.
            if (station.Body.Children.OfType<ParameterFormPanel>().FirstOrDefault() is { } identity)
            { var heading = Text("本站身份", true); heading.Margin = new(0, 16, 0, 8); station.Body.Children.Insert(station.Body.Children.IndexOf(identity), heading); }
            var cameras = Section(station, "相机编码"); var service = Section(station, "RCS 服务"); var numbers = Section(station, "请求编号区间");
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
                    var target = key.EndsWith("-bcr-code", StringComparison.Ordinal) ? cameras.Form : key is "Provider" or "Target" ? service.Form : key is "request-id-base" or "request-id-count" ? numbers.Form : protocol;
                    if (location.Row.Parent is Panel parent) parent.Children.Remove(location.Row);
                    target.Children.Add(location.Row);
                    locations[pair.Key] = location with { Page = station };
                }
                foreach (var running in rcs.Body.Children.OfType<Border>().ToArray())
                { rcs.Body.Children.Remove(running); service.Form.Children.Insert(0, running); }
                foreach (var evidence in rcs.Technical.Children.OfType<UIElement>().ToArray())
                { rcs.Technical.Children.Remove(evidence); station.Technical.Children.Add(evidence); }
                rcs.Applicable = () => false;
            }
            foreach (var section in new[] { cameras, service, numbers }.Where(x => x.Form.Children.Count == 0))
            { station.Body.Children.Remove(section.Heading); station.Body.Children.Remove(section.Form); }
            if (protocol.Children.Count == 0 && protocol.Parent is Expander folded) station.Body.Children.Remove(folded);
        }
        var cameraPages = pages.Where(p => p.Category == 2 && p.Title is "左相机" or "右相机").ToArray();
        if (cameraPages.Length > 0)
        {
            var combined = Page(2, "左右相机", "IP 用于找到相机，物理序列号用于确认设备。不使用的位置两项都留空。保存后只重新连接改动的那台相机。");
            var columns = new Grid(); columns.ColumnDefinitions.Add(new()); columns.ColumnDefinitions.Add(new()); columns.RowDefinitions.Add(new() { Height = GridLength.Auto }); columns.RowDefinitions.Add(new() { Height = GridLength.Auto });
            combined.Body.Children.Add(columns);
            foreach (var (page, index) in cameraPages.Select((p, i) => (p, i)))
            {
                foreach (var note in page.Body.Children.OfType<TextBlock>().Where(t => t.Text.StartsWith("填写要使用的相机", StringComparison.Ordinal) || t.Text.StartsWith("通过 IP 地址找到", StringComparison.Ordinal) || t.Text.StartsWith("填写 IP 和序列号", StringComparison.Ordinal) || t.Text.StartsWith("相机连续读码", StringComparison.Ordinal)).ToArray()) page.Body.Children.Remove(note);
                var cameraDraft = Model!.Readers.First(r => (r.Role == "left" ? "左相机" : "右相机") == page.Title);
                if (cameraDraft.Provider?.Implementation == "hikrobot-mvs-reader" && locations.TryGetValue(cameraDraft.InstanceId + "/Provider", out var provider) && provider.Row is { } providerRow && providerRow.Parent is Panel providerParent)
                {
                    providerParent.Children.Remove(providerRow);
                    if (provider.Control.Parent is Panel identityParent) identityParent.Children.Remove(provider.Control);
                    if (provider.Control is TextBox identity) { identity.MinHeight = 0; identity.BorderThickness = new(0); identity.Padding = new(0); identity.FontSize = UiTypography.Label; identity.Background = System.Windows.Media.Brushes.Transparent; }
                    page.Body.Children.Insert(1, provider.Control); page.Technical.Children.Add(providerRow);
                }
                page.Scroll.Content = null; page.Body.Margin = new(0, 0, 16, 0); page.Body.MaxWidth = double.PositiveInfinity; page.Body.HorizontalAlignment = HorizontalAlignment.Stretch;
                // Detached source scrolls must no longer dictate the embedded form's width.
                page.Body.ClearValue(WidthProperty);
                if (!FirstConfiguration && CameraPreviewFactory is { } preview)
                {
                    var reader = Model!.Readers.First(r => (r.Role == "left" ? "左相机" : "右相机") == page.Title);
                    page.Body.Children.Insert(Math.Min(2, page.Body.Children.Count), preview(reader.InstanceId));
                }
                if (!FirstConfiguration)
                {
                    var reference = Model!.Components.Where(ParameterOwnership.IsRcs).SelectMany(c => c.Parameters.Where(p => p.Field.Key == cameraDraft.Role + "-bcr-code").Select(p => (Component: c, Parameter: p))).FirstOrDefault();
                    if (reference.Parameter is not null)
                    {
                        var code = new TextBlock { FontSize = UiTypography.Label, Margin = new(0, 4, 0, 4), TextWrapping = TextWrapping.Wrap };
                        code.SetBinding(TextBlock.TextProperty, new Binding("Value") { Source = reference.Parameter, Mode = BindingMode.OneWay, StringFormat = "相机编码：{0}（在“站点编号与 RCS”中修改）" });
                        page.Body.Children.Insert(Math.Min(2, page.Body.Children.Count), code);
                    }
                }
                columns.Children.Add(page.Body); Grid.SetColumn(page.Body, index); page.Applicable = () => false;
                foreach (var pair in locations.Where(x => ReferenceEquals(x.Value.Page, page)).ToArray()) locations[pair.Key] = pair.Value with { Page = combined };
            }
            columns.SizeChanged += (_, _) =>
            {
                var narrow = columns.ActualWidth < 780; columns.ColumnDefinitions[1].Width = narrow ? new(0) : new(1, GridUnitType.Star);
                for (var i = 0; i < cameraPages.Length; i++) { Grid.SetColumn(cameraPages[i].Body, narrow ? 0 : i); Grid.SetRow(cameraPages[i].Body, narrow ? i : 0); cameraPages[i].Body.Width = double.NaN; }
            };
        }
        foreach (var page in pages)
            page.TaskGroup = FirstConfiguration ? page.Category switch { 0 => "本站信息", 1 => "业务规则", 2 => "左右相机", 3 => "RCS 连接", _ => "检查与完成" } :
                ReferenceEquals(page, station) ? "本站接入" : page.Category == 2 ? "相机" : page.Title == "条码筛选" ? "分拣规则" : page.Title is "数据与记录" or "自动备份" or "显示偏好" or "作业登录" ? "系统维护" : "高级";
    }
    void RequestRange(Panel form, ObjectPage page, CapabilityDraft draft)
    {
        var baseValue = draft.Parameters.Single(p => p.Field.Key == "request-id-base");
        var countValue = draft.Parameters.Single(p => p.Field.Key == "request-id-count");
        var projection = new InclusiveRange(baseValue, countValue); unsubscribe.Add(projection.Dispose);
        // One setting, two boxes: the first and last number RCS allotted, both inclusive.
        TextBox Box(string property, string path)
        {
            var box = new TextBox { MinHeight = 40, MaxLength = 20, Width = 148, VerticalContentAlignment = VerticalAlignment.Center };
            box.SetBinding(TextBox.TextProperty, new Binding(property) { Source = projection, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            AutomationProperties.SetName(box, path); AutomationProperties.SetAutomationId(box, path); return box;
        }
        var basePath = draft.InstanceId + "/request-id-base"; var countPath = draft.InstanceId + "/request-id-count";
        var first = Box(nameof(projection.First), basePath); var last = Box(nameof(projection.Last), countPath);
        var to = Text("至"); to.Margin = new(8, 0, 8, 0); to.VerticalAlignment = VerticalAlignment.Center;
        var range = new StackPanel { Orientation = Orientation.Horizontal, Tag = "inline" }; range.Children.Add(first); range.Children.Add(to); range.Children.Add(last);
        Field(form, "请求编号范围（由 RCS 分配） *", range, "填 RCS 分给本供包台的编号段，不能和其他供包台重叠。", basePath, page);
        // The pair is one row; each box keeps its own path for navigation, errors and automation.
        range.ClearValue(AutomationProperties.NameProperty); range.ClearValue(AutomationProperties.AutomationIdProperty);
        var row = locations[basePath];
        AutomationProperties.SetLabeledBy(first, AutomationProperties.GetLabeledBy(range)); AutomationProperties.SetLabeledBy(last, AutomationProperties.GetLabeledBy(range));
        AutomationProperties.SetHelpText(first, "起始编号（含）"); AutomationProperties.SetHelpText(last, "结束编号（含）");
        locations[basePath] = row with { Control = first, Label = "请求编号起点 *" };
        locations[countPath] = row with { Control = last, Label = "请求编号终点 *" };
        TrackEdits(first); TrackEdits(last);
    }
    sealed class InclusiveRange : INotifyPropertyChanged, IDisposable
    {
        readonly CapabilityParameter start, count; bool changing; string first = "", last = "";
        public event PropertyChangedEventHandler? PropertyChanged;
        public InclusiveRange(CapabilityParameter start, CapabilityParameter count) { this.start = start; this.count = count; Reload(); start.PropertyChanged += Changed; count.PropertyChanged += Changed; }
        public string First { get => first; set { first = value; Apply(); } }
        public string Last { get => last; set { last = value; Apply(); } }
        void Changed(object? sender, PropertyChangedEventArgs e) { if (!changing && e.PropertyName == "Value") Reload(); }
        void Reload()
        {
            if (long.TryParse(start.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var b) && long.TryParse(count.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && b < long.MaxValue && n > 0 && b <= long.MaxValue - n)
            { first = (b + 1).ToString(CultureInfo.InvariantCulture); last = (b + n).ToString(CultureInfo.InvariantCulture); }
            else { first = start.Value.Length == 0 ? "" : "请重新填写"; last = count.Value.Length == 0 ? "" : "请重新填写"; }
            PropertyChanged?.Invoke(this, new(nameof(First))); PropertyChanged?.Invoke(this, new(nameof(Last)));
        }
        void Apply()
        {
            changing = true;
            try
            {
                var validStart = long.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var a) && a > 0;
                var validEnd = long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var z) && z >= a;
                // Invalid visible text poisons the underlying candidate, so an old valid range cannot be saved.
                start.Value = validStart ? (a - 1).ToString(CultureInfo.InvariantCulture) : "invalid";
                count.Value = validStart && validEnd ? checked(z - a + 1).ToString(CultureInfo.InvariantCulture) : "invalid";
            }
            finally { changing = false; }
        }
        public void Dispose() { start.PropertyChanged -= Changed; count.PropertyChanged -= Changed; }
    }
}
