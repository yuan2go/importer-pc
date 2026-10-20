using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Ipc.Application.Contracts;
namespace Ipc.Application.ParcelFlow.Desktop;

// Shared by offline first configuration and the running editor. Native controls keep
// focus, selection and scroll while snapshots refresh; only a deliberate reload replaces Model.
public sealed class ParameterSettingsControl : UserControl
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model), typeof(ParameterEditorModel), typeof(ParameterSettingsControl), new PropertyMetadata(null, ModelChanged));
    public ParameterEditorModel? Model { get => (ParameterEditorModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    readonly TabControl categories = new() { Name = "ParameterCategories" };
    readonly StackPanel errors = new();
    readonly Dictionary<string, (int Category, FrameworkElement Control)> locations = [];
    readonly List<(CapabilityDraft Draft, System.ComponentModel.PropertyChangedEventHandler Handler)> subscriptions = [];
    public ParameterSettingsControl()
    {
        var root = new DockPanel(); var errorScroll = new ScrollViewer { Content = errors, MaxHeight = 132, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; DockPanel.SetDock(errorScroll, Dock.Top); root.Children.Add(errorScroll); root.Children.Add(categories); Content = root;
    }
    static void ModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ParameterSettingsControl)d;
        if (e.OldValue is ParameterEditorModel old) old.ValidationChanged -= view.ShowIssues;
        if (e.NewValue is ParameterEditorModel next) next.ValidationChanged += view.ShowIssues;
        view.Render();
    }
    static TextBlock Text(string value, bool heading = false) => new() { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = heading ? 18 : 14,
        FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal, Margin = new(0, heading ? 18 : 4, 0, 6) };
    StackPanel Category(string title)
    {
        var panel = new StackPanel { Margin = new(18, 4, 18, 18) };
        categories.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }); return panel;
    }
    void Input(Panel panel, string label, object source, string property, string path, int category, string? help = null, bool multiline = false)
    {
        panel.Children.Add(Text(label));
        var box = new TextBox { MinHeight = multiline ? 72 : 36, MaxLength = 65536, AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        box.SetBinding(TextBox.TextProperty, new Binding(property) { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        AutomationProperties.SetName(box, path); box.ToolTip = help ?? label; panel.Children.Add(box); locations[path] = (category, box);
        if (help is not null) panel.Children.Add(Text(help));
    }
    void Render()
    {
        foreach (var subscription in subscriptions) subscription.Draft.PropertyChanged -= subscription.Handler; subscriptions.Clear();
        var selected = categories.SelectedIndex; categories.Items.Clear(); locations.Clear(); errors.Children.Clear();
        if (Model is not { } m) { errors.Children.Add(Text("正在读取完整配置与已安装描述；没有建立第二份配置。")); return; }
        var system = Category("系统"); var business = Category("业务模块"); var drivers = Category("驱动"); var components = Category("组件");
        system.Children.Add(Text("软件级参数", true)); system.Children.Add(Text("当前业务：" + m.ApplicationId + "。系统必需项均有软件默认值，缺配不阻止打开本编辑器。所有运行参数随完整修订保存，待受控重启。显示偏好仍按当前用户管理。"));
        foreach (var f in new[] { ("工位外部编码（StationCode）", "StationCode", "软件默认取当前工作站标识；可独立修改，用于组件心跳，独立于取格口左右编码。旧账缺此字段时默认值只补入候选，明确保存后再应用"),
            ("输入队列容量", "QueueCapacity", "1～4096；满时围栏拒收，需现场检查"), ("调用预算（ms）", "TimeoutMs", "100～30000；全局外呼预算"),
            ("未清偿作业容量", "MaximumOperations", "1～100000；不是已完成包裹数"), ("常驻历史条数", "ResidentHistory", "1～10000；同库归档不释放磁盘"),
            ("关键写入保留空间（MiB）", "MinimumFreeMiB", "1～1048576；不足时阻断新准入") }) Input(system, f.Item1, m.Policy, f.Item2, f.Item2, 0, f.Item3);
        var backup = new CheckBox { Content = "启用受控计划备份", MinHeight = 36 }; backup.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(m.Policy.BackupEnabled)) { Source = m.Policy, Mode = BindingMode.TwoWay }); system.Children.Add(backup);
        Input(system, "计划备份绝对目录", m.Policy, "BackupDirectory", "BackupDirectory", 0, "只在已暂停、维护授权有效且无外呼时执行；不自动删除原备份");
        Input(system, "备份周期（分钟，15～10080）", m.Policy, "BackupIntervalMinutes", "BackupIntervalMinutes", 0);
        Input(system, "备份容量上限（份，1～1000）", m.Policy, "BackupMaximumSets", "BackupMaximumSets", 0);
        business.Children.Add(Text(m.ApplicationId + " 业务参数", true));
        if (m.ApplicationId == "sportmaster")
        {
            Input(business, "左右共用条码筛选正则", m.Policy, "BarcodePattern", "BarcodePattern", 1, "整串匹配；单次25ms、总计100ms预算。旧配置留空保持严格单码兼容，不自动生成生产规则。新规则仅影响新准入。", true);
            var sample = new TextBox { AcceptsReturn = true, MinHeight = 96, MaxLength = 65536, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            AutomationProperties.SetName(sample, "条码筛选样例"); business.Children.Add(Text("本地候选样例（每行一个，同一次观察；不外呼、不保存）")); business.Children.Add(sample);
            var result = Text("尚未测试"); var test = new Button { Content = "测试候选筛选", MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 4) };
            test.Click += async (_, _) =>
            {
                var pattern = m.Policy.BarcodePattern; var raw = sample.Text; test.IsEnabled = false; result.Text = "正在执行有界本地测试…";
                try
                {
                    var reply = await Task.Run(() => m.Preview(new(pattern), raw.Replace("\r", "").Split('\n')));
                    result.Text = m.Policy.BarcodePattern != pattern || sample.Text != raw ? "输入已改变，本次测试结果已过期；请重新测试。" : reply.Outcome + " · " + reply.Detail + "\n选中码：" + (reply.SelectedCode ?? "无") + " · 有效候选：" + reply.Matches;
                }
                catch (Exception ex) { result.Text = "测试未完成：" + ex.Message + "；输入保留。"; }
                finally { test.IsEnabled = true; }
            };
            business.Children.Add(test); business.Children.Add(result);
        }
        else
        {
            EnumChoice(business, "重量来源", m.Policy, nameof(m.Policy.WeightSource), Enum.GetValues<WeightSource>());
            EnumChoice(business, "重量单位", m.Policy, nameof(m.Policy.Unit), Enum.GetValues<WeightUnit>());
            Input(business, "重量精度（0～6）", m.Policy, "Precision", "Precision", 1);
            Input(business, "配置重量（所选kg/g）", m.Policy, "ConfiguredWeight", "ConfiguredWeight", 1, "仅配置重量模式生效；不等于实测重量");
            Input(business, "测量新鲜度（ms）", m.Policy, "MaximumAgeMs", "MaximumAgeMs", 1);
            Input(business, "本件等待期限（ms）", m.Policy, "WaitTimeoutMs", "WaitTimeoutMs", 1);
            EnumChoice(business, "重量有效性时机", m.Policy, nameof(m.Policy.Validity), Enum.GetValues<WeightValidity>());
            if (m.Components.Any(x => x.Kind == "camera"))
            {
                EnumChoice(business, "图像上传时机", m.Policy, nameof(m.Policy.UploadStrategy), Enum.GetValues<UploadStrategy>());
                Input(business, "单件图像最大字节", m.Policy, "ImageBytes", "ImageBytes", 1);
                Input(business, "图像缓存总字节", m.Policy, "TotalBytes", "TotalBytes", 1);
                Input(business, "图像待处理件数", m.Policy, "MaximumPending", "MaximumPending", 1);
            }
        }
        drivers.Children.Add(Text("Application 声明的读码实例", true)); drivers.Children.Add(Text("Sportmaster 固定左右两个实例；可共用同一驱动包，连接和参数独立。身份与数量只读，参数只来自精确安装描述。"));
        foreach (var reader in m.Readers) Capability(drivers, reader, 2);
        components.Children.Add(Text("业务组件", true)); components.Children.Add(Text("Sportmaster 左右共用一个逻辑RCS连接。quantity、指定格口、左右取格口编码及心跳间隔均归组件。"));
        foreach (var component in m.Components.Where(x => x.Bound || x.Enabled)) Capability(components, component, 3);
        categories.SelectedIndex = Math.Clamp(selected, 0, 3); ShowIssues(this, EventArgs.Empty);
    }
    static void EnumChoice(Panel panel, string label, object source, string property, Array values)
    {
        panel.Children.Add(Text(label)); var choice = new ComboBox { ItemsSource = values, MinHeight = 36 };
        choice.SetBinding(ComboBox.SelectedItemProperty, new Binding(property) { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        AutomationProperties.SetName(choice, property); panel.Children.Add(choice);
    }
    void Capability(Panel panel, CapabilityDraft draft, int category)
    {
        var section = new StackPanel(); panel.Children.Add(section);
        void RenderSection() { section.Children.Clear(); CapabilityFields(section, draft, category); }
        System.ComponentModel.PropertyChangedEventHandler handler = (_, e) => { if (e.PropertyName == nameof(CapabilityDraft.Provider)) RenderSection(); };
        draft.PropertyChanged += handler; subscriptions.Add((draft, handler)); RenderSection();
    }
    sealed record ProviderChoice(string Label, Ipc.Platform.CapabilityDescriptor Provider);
    void CapabilityFields(Panel panel, CapabilityDraft draft, int category)
    {
        panel.Children.Add(Text((draft.Kind == "observation" ? draft.Role + " · 读码实例 " : "组件实例 ") + draft.InstanceId, true));
        panel.Children.Add(Text("稳定身份/目标：" + draft.Target + "\n" + draft.ProviderIdentity));
        if (draft.Role is "operator-login" or "default-scale")
        {
            var optional = new CheckBox { Content = "绑定此可选能力（仍需业务策略适用）", MinHeight = 36 };
            optional.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(draft.Bound)) { Source = draft, Mode = BindingMode.TwoWay }); panel.Children.Add(optional);
        }
        var choices = draft.Providers.Select(p => new ProviderChoice(p.Implementation + " @ " + p.Version + " · " + p.Module, p)).ToArray();
        var providers = new ComboBox { ItemsSource = choices, SelectedItem = choices.SingleOrDefault(x => x.Provider == draft.Provider), DisplayMemberPath = "Label", MinHeight = 36 };
        AutomationProperties.SetName(providers, draft.InstanceId + "/Provider"); providers.ToolTip = "只选择已安装的精确版本；身份/数量/用途保持Application声明";
        providers.SelectionChanged += (_, _) => { if (providers.SelectedItem is ProviderChoice selected && selected.Provider != draft.Provider) draft.Provider = selected.Provider; };
        panel.Children.Add(providers);
        if (draft.Provider is null) { panel.Children.Add(Text("缺少精确依赖：恢复原包或在高级维护中受控选择兼容版本。当前输入和绑定保留。")); return; }
        if (draft.Kind != "observation") Input(panel, "组件连接目标", draft, "Target", draft.InstanceId + "/Target", category, draft.TargetBoundary);
        if (draft.Parameters.Count == 0) panel.Children.Add(Text("此版本没有可配置参数。"));
        foreach (var group in draft.Parameters.GroupBy(p => p.Field.Group))
        {
            panel.Children.Add(Text(group.Key, true));
            foreach (var parameter in group)
            {
                var path = draft.InstanceId + "/" + parameter.Field.Key;
                if (parameter.Field.Type is "choice" or "boolean")
                {
                    panel.Children.Add(Text(parameter.EditorLabel + (parameter.Field.Required ? " *" : "")));
                    var choice = new ComboBox { ItemsSource = parameter.Field.Type == "boolean" ? new[] { "true", "false" } : parameter.Field.Choices, MinHeight = 36, IsEnabled = !parameter.Field.ReadOnly };
                    choice.SetBinding(ComboBox.SelectedItemProperty, new Binding("Value") { Source = parameter, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                    AutomationProperties.SetName(choice, path); panel.Children.Add(choice); locations[path] = (category, choice); panel.Children.Add(Text(parameter.EditorHelp));
                }
                else
                {
                    Input(panel, parameter.EditorLabel + (parameter.Field.Required ? " *" : "") + " " + parameter.Field.Unit, parameter, "Value", path, category, parameter.EditorHelp + " · 保存后待重启", parameter.IsList);
                    if (locations[path].Control is TextBox box) { box.MaxLength = parameter.EditorMaximumLength; box.IsReadOnly = parameter.Field.ReadOnly; }
                }
            }
        }
    }
    void ShowIssues(object? sender, EventArgs e)
    {
        errors.Children.Clear(); foreach (var item in locations.Values) item.Control.ClearValue(Control.BorderBrushProperty);
        foreach (var issue in Model?.Issues ?? [])
        {
            var button = new Button { Content = new TextBlock { Text = issue.Category + " / " + issue.Path + "：" + issue.Message, TextWrapping = TextWrapping.Wrap },
                HorizontalContentAlignment = HorizontalAlignment.Left, Foreground = Brushes.Firebrick, Margin = new(4), MinHeight = 36 };
            if (locations.TryGetValue(issue.Path, out var location))
            {
                location.Control.SetValue(Control.BorderBrushProperty, Brushes.Firebrick);
                button.Click += (_, _) => { categories.SelectedIndex = location.Category; location.Control.BringIntoView(); location.Control.Focus(); };
            }
            errors.Children.Add(button);
        }
    }
}
