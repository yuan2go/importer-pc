using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace Ipc.Application.ParcelFlow.Desktop;

public sealed partial class ParameterSettingsControl
{
    void BuildCapabilities(ParameterEditorModel m)
    {
        foreach (var reader in m.Readers)
        {
            var page = Page(2, reader.Role switch { "left" => "左相机", "right" => "右相机", _ => reader.Role + " 读码实例" }, "编辑当前相机的连接和编码。实例数量及左右绑定由当前业务固定。");
            Capability(page, reader);
            foreach (var rcs in m.Components.Where(ParameterOwnership.IsRcs))
                foreach (var p in rcs.Parameters.Where(p => p.Field.Key == reader.Role + "-bcr-code")) Parameter(Form(page, "取格口映射"), page, rcs, p);
        }
        if (m.Readers.Length == 0)
        {
            var missing = Page(2, "缺少读码实例", "当前候选没有读码实例。请核对原拓扑和已安装驱动。");
            if (MaintenanceRequested is not null) { var open = new Button { Content = "查看实例与角色绑定…", HorizontalAlignment = HorizontalAlignment.Left }; open.Click += (_, _) => MaintenanceRequested?.Invoke(this, EventArgs.Empty); missing.Body.Children.Add(open); }
        }
        foreach (var component in m.Components)
        {
            var page = Page(3, ParameterOwnership.IsRcs(component) ? "RCS 连接" : component.Role switch
            { "maintenance-auth" => "维护认证", "operator-login" => "操作员登录", "default-scale" => "默认称重", _ => component.Role + " · " + component.InstanceId },
                ParameterOwnership.IsRcs(component) ? "左右相机共用一个 RCS 连接。保存后通过受控重启应用。" : "按已安装组件的实际描述编辑，保存后通过受控重启应用。");
            Capability(page, component);
            if (!ParameterOwnership.IsRcs(component)) continue;
            var references = new StackPanel();
            foreach (var p in component.Parameters.Where(p => ParameterOwnership.Category(component, p.Field.Key) != 3))
            {
                // References stay read-only; each field has one writable location.
                var row = new DockPanel { Margin = new(0, 4, 0, 8) };
                var jump = new Button { Content = "前往" + ParameterOwnership.CategoryName(ParameterOwnership.Category(component, p.Field.Key)), Style = (Style)Resources["ParameterQuietAction"] };
                jump.Click += (_, _) => NavigateTo(component.InstanceId + "/" + p.Field.Key); DockPanel.SetDock(jump, Dock.Right); row.Children.Add(jump);
                var value = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, Padding = new(0), MinHeight = 28 };
                value.SetBinding(TextBox.TextProperty, new Binding("Value") { Source = p, Mode = BindingMode.OneWay });
                var item = new StackPanel(); item.Children.Add(Text(ParameterLabel(p))); item.Children.Add(value); row.Children.Add(item); references.Children.Add(row);
            }
            page.Body.Children.Add(new Expander { Header = "其他页面的相关设置", Content = references, Margin = new(0, 20, 24, 0) });
            Technical(page, "连接与业务结果", component.Contract, "心跳 HTTP 交付不表示上游应用确认。当前协议无权威结果查询；未知原请求不自动重发。重连继续使用当前运行配置，不应用待重启修订。");
        }
    }
    sealed record ProviderChoice(string Label, Ipc.Platform.CapabilityDescriptor Provider);
    void Capability(ObjectPage page, CapabilityDraft draft)
    {
        if (draft.Role is "operator-login" or "default-scale")
        {
            var optional = new CheckBox { Content = "绑定此可选能力", MinHeight = 38 };
            optional.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(draft.Bound)) { Source = draft, Mode = BindingMode.TwoWay }); page.Body.Children.Add(optional);
        }
        var choices = draft.Providers.Select(p => new ProviderChoice(p.Implementation + " @ " + p.Version + " · " + p.Module, p)).ToArray();
        var providers = new ComboBox { ItemsSource = choices, SelectedItem = choices.SingleOrDefault(x => x.Provider == draft.Provider), DisplayMemberPath = "Label", MinHeight = 38 };
        var identity = new StackPanel(); var providerForm = new ParameterFormPanel(); identity.Children.Add(providerForm);
        Field(providerForm, "已安装类型与版本", providers, "按实际安装描述建立该类型的参数。更换前请核对精确版本。", draft.InstanceId + "/Provider", page);
        locations[draft.InstanceId] = locations[draft.InstanceId + "/Provider"];
        identity.Children.Add(new TextBox { IsReadOnly = true, Text = "实例：" + draft.InstanceId + "\n设备或目标：" + draft.Target + "\n" + draft.ProviderIdentity + "\n合同：" + draft.Contract, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent });
        providers.SelectionChanged += (_, _) => { if (providers.SelectedItem is ProviderChoice selected && selected.Provider != draft.Provider) draft.Provider = selected.Provider; };
        var details = new Expander { Header = "安装版本与实例身份", Content = identity, Margin = new(0, 20, 24, 0), IsExpanded = draft.Provider is null };
        if (draft.Provider is null) { var issue = Text("缺少精确依赖。恢复原安装包，或受控选择兼容版本后继续。"); issue.Foreground = Brush("#B02D35"); page.Body.Children.Add(issue); page.Body.Children.Add(details); return; }
        if (draft.Kind != "observation") Input(Form(page), ParameterOwnership.IsRcs(draft) ? "服务地址 *" : "组件连接目标", draft, "Target", draft.InstanceId + "/Target", page,
            ParameterOwnership.IsRcs(draft) ? "HTTP(S) 地址，路径为 /rcs/sorter/api。" : draft.TargetBoundary);
        var form = Form(page);
        var advanced = new List<CapabilityParameter>();
        foreach (var p in draft.Parameters.Where(p => ParameterOwnership.Category(draft, p.Field.Key) == page.Category))
        {
            if (draft.Kind == "observation" && !p.Field.Required && p.Field.Key is "queue.capacity" or "code.encoding") advanced.Add(p);
            else Parameter(form, page, draft, p);
        }
        if (advanced.Count > 0) { var advancedForm = AdvancedForm(page); foreach (var p in advanced) Parameter(advancedForm, page, draft, p); }
        if (draft.Parameters.Count == 0) page.Body.Children.Add(Text("此版本没有可配置参数。"));
        page.Body.Children.Add(details);
    }
    static string ParameterLabel(CapabilityParameter p) => p.Field.Key switch
    {
        "zone-id" => "区域 ID", "project-id" => "项目 ID", "quantity" => "每次请求数量", "specified-chute" => "指定格口", "chute-code" => "格口编码",
        "left-bcr-code" or "right-bcr-code" => "相机编码（取格口）", "bcr-code" => "相机编码（心跳）", "heartbeat-interval-seconds" => "心跳间隔",
        "request-id-base" => "请求编号起点（不含）", "request-id-count" => "请求编号容量", "source" => "协议来源", "locale" => "协议语言", _ => p.EditorLabel
    };
    static string ParameterHint(CapabilityParameter p) => p.Field.Key switch
    {
        "zone-id" or "project-id" => "填写上游分配的正整数标识。", "quantity" => "1～2147483647；与上游响应数量分别保留。",
        "bcr-code" => "心跳上报使用，左右相机不得重复。", "left-bcr-code" or "right-bcr-code" => "本位取格口请求使用；与心跳编码分别配置。",
        "device.ip" => "按 IP 连接当前相机，不修改设备 IP。", "device.serial" => "填写时同时核对 IP 和序列号。",
        "entity.mode" => "observe-only 仅观察；trigger 仍需真实部署资格。", "queue.capacity" => "1～1024；溢出后需检查并显式重连。",
        "heartbeat-interval-seconds" => "1～3600 秒；重启后应用。", "specified-chute" => "启用时必须填写下方格口编码。", "chute-code" => "仅指定格口开启时使用。",
        "request-id-base" => "大于或等于 0；保留原持久编号责任。", "request-id-count" => "大于或等于 1；与起点合计不能溢出。",
        _ => p.EditorHelp.Length > 100 ? "完整规则见下方技术信息。" : p.EditorHelp
    };
    sealed record BooleanChoice(string Value, string Label);
    void Parameter(Panel form, ObjectPage page, CapabilityDraft draft, CapabilityParameter p)
    {
        var path = draft.InstanceId + "/" + p.Field.Key;
        var required = p.Field.Required || ParameterOwnership.IsRcs(draft) && p.Field.Key == "chute-code" && ParameterOwnership.Applicable(draft, p.Field.Key);
        var label = ParameterLabel(p) + (string.IsNullOrEmpty(p.Field.Unit) ? "" : "（" + p.Field.Unit + "）") + (required ? " *" : "");
        var help = ParameterHint(p);
        if (p.Field.Type == "boolean" || p.Field.Key == "specified-chute")
        {
            var choice = new ComboBox { ItemsSource = new[] { new BooleanChoice("false", "关闭"), new BooleanChoice("true", "启用") }, DisplayMemberPath = "Label", SelectedValuePath = "Value" };
            choice.SetBinding(ComboBox.SelectedValueProperty, new Binding("Value") { Source = p, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            Field(form, label, choice, help, path, page);
        }
        else if (p.Field.Type == "choice") Choice(form, label, p, "Value", p.Field.Choices!, path, page, help);
        else
        {
            var box = Input(form, label, p, "Value", path, page, help, p.IsList); box.MaxLength = p.EditorMaximumLength; box.IsReadOnly = p.Field.ReadOnly; if (p.Field.Type == "integer") box.MaxWidth = 240;
        }
        locations[path] = locations[path] with { Help = p.EditorHelp + " " + help };
        locations[path].Control.IsEnabled = !p.Field.ReadOnly && ParameterOwnership.Applicable(draft, p.Field.Key);
        Technical(page, ParameterLabel(p), path, p.EditorHelp);
        if (ParameterOwnership.IsRcs(draft) && p.Field.Key == "chute-code")
        {
            var field = locations[path].Control;
            PropertyChangedEventHandler handler = (_, _) => field.IsEnabled = !p.Field.ReadOnly && ParameterOwnership.Applicable(draft, p.Field.Key);
            draft.PropertyChanged += handler; unsubscribe.Add(() => draft.PropertyChanged -= handler);
        }
    }
}
