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
            var page = Page(2, reader.Role switch { "left" => "左相机", "right" => "右相机", _ => reader.Role + " 读码实例" }, "数量与绑定由当前业务决定。取格口编码与驱动心跳编码用途独立。");
            Capability(page, reader);
            foreach (var rcs in m.Components.Where(ParameterOwnership.IsRcs))
                foreach (var p in rcs.Parameters.Where(p => p.Field.Key == reader.Role + "-bcr-code")) Parameter(Form(page, "取格口业务映射"), page, rcs, p);
        }
        if (m.Readers.Length == 0) Page(2, "缺少读码实例", "当前完整候选没有读码实例。请在维护与迁移中修复原拓扑；不按示例新增设备。");
        foreach (var component in m.Components)
        {
            var page = Page(3, ParameterOwnership.IsRcs(component) ? "RCS 连接" : component.Role switch
            { "maintenance-auth" => "维护认证", "operator-login" => "操作员登录", "default-scale" => "默认称重", _ => component.Role + " · " + component.InstanceId },
                "保存后受控重启生效；设备重连继续使用当前运行配置。");
            Capability(page, component);
            if (!ParameterOwnership.IsRcs(component)) continue;
            page.Body.Children.Add(Text("其他分类的参数引用", true));
            foreach (var p in component.Parameters.Where(p => ParameterOwnership.Category(component, p.Field.Key) != 3))
            {
                var row = new DockPanel { Margin = new(0, 4, 0, 4) };
                var jump = new Button { Content = "前往" + ParameterOwnership.CategoryName(ParameterOwnership.Category(component, p.Field.Key)), MinHeight = 44 };
                jump.Click += (_, _) => NavigateTo(component.InstanceId + "/" + p.Field.Key); DockPanel.SetDock(jump, Dock.Right); row.Children.Add(jump);
                var value = new TextBox { IsReadOnly = true, MinHeight = 44, TextWrapping = TextWrapping.Wrap };
                value.SetBinding(TextBox.TextProperty, new Binding("Value") { Source = p, Mode = BindingMode.OneWay });
                var item = new StackPanel(); item.Children.Add(Text(p.EditorLabel)); item.Children.Add(value); row.Children.Add(item); page.Body.Children.Add(row);
            }
            page.Body.Children.Add(Text("心跳 HTTP 交付不表示上游应用确认。当前协议无权威结果查询；未知原请求不自动重发。"));
        }
    }
    sealed record ProviderChoice(string Label, Ipc.Platform.CapabilityDescriptor Provider);
    void Capability(ObjectPage page, CapabilityDraft draft)
    {
        var identity = new StackPanel { Margin = new(0, 8, 0, 8) };
        identity.Children.Add(new TextBox { IsReadOnly = true, Text = draft.InstanceId + " · " + draft.Target + "\n" + draft.ProviderIdentity, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0) });
        if (draft.Role is "operator-login" or "default-scale")
        {
            var optional = new CheckBox { Content = "绑定此可选能力（仍需业务策略适用）", MinHeight = 44 };
            optional.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(draft.Bound)) { Source = draft, Mode = BindingMode.TwoWay }); page.Body.Children.Add(optional);
        }
        var choices = draft.Providers.Select(p => new ProviderChoice(p.Implementation + " @ " + p.Version + " · " + p.Module, p)).ToArray();
        var providers = new ComboBox { ItemsSource = choices, SelectedItem = choices.SingleOrDefault(x => x.Provider == draft.Provider), DisplayMemberPath = "Label", MinHeight = 44 };
        var providerForm = new ParameterFormPanel(); identity.Children.Add(providerForm);
        Field(providerForm, "已安装类型 / 精确版本", providers, "更换类型会按其真实描述重新建立参数候选；保存前须完整校验。", draft.InstanceId + "/Provider", page);
        locations[draft.InstanceId] = locations[draft.InstanceId + "/Provider"];
        providers.SelectionChanged += (_, _) => { if (providers.SelectedItem is ProviderChoice selected && selected.Provider != draft.Provider) draft.Provider = selected.Provider; };
        var details = new Expander { Header = "安装版本与设备身份", Content = identity, Margin = new(0, 12, 0, 4), IsExpanded = draft.Provider is null };
        if (draft.Provider is null) { page.Body.Children.Add(Text("缺少精确依赖。恢复原包或在维护中受控选择兼容版本；当前绑定保留。")); page.Body.Children.Add(details); return; }
        var form = Form(page);
        if (draft.Kind != "observation") Input(form, "组件连接目标", draft, "Target", draft.InstanceId + "/Target", page, draft.TargetBoundary);
        foreach (var p in draft.Parameters.Where(p => ParameterOwnership.Category(draft, p.Field.Key) == page.Category)) Parameter(form, page, draft, p);
        if (draft.Parameters.Count == 0) page.Body.Children.Add(Text("此版本没有可配置参数。"));
        page.Body.Children.Add(details);
    }
    void Parameter(Panel form, ObjectPage page, CapabilityDraft draft, CapabilityParameter p)
    {
        var path = draft.InstanceId + "/" + p.Field.Key;
        if (p.Field.Type is "choice" or "boolean")
            Choice(form, p.EditorLabel + (p.Field.Required ? " *" : ""), p, "Value", p.Field.Type == "boolean" ? new[] { "true", "false" } : p.Field.Choices!, path, page, p.EditorHelp);
        else
        {
            var box = Input(form, p.EditorLabel + (p.Field.Required ? " *" : "") + " " + p.Field.Unit, p, "Value", path, page, p.EditorHelp, p.IsList);
            box.MaxLength = p.EditorMaximumLength; box.IsReadOnly = p.Field.ReadOnly;
        }
        locations[path].Control.IsEnabled = !p.Field.ReadOnly && ParameterOwnership.Applicable(draft, p.Field.Key);
        if (ParameterOwnership.IsRcs(draft) && p.Field.Key == "chute-code")
        {
            var field = locations[path].Control; form.Children.Add(Text("仅指定格口开启时生效；关闭时从保存候选移除此参数。"));
            PropertyChangedEventHandler handler = (_, _) => field.IsEnabled = ParameterOwnership.Applicable(draft, p.Field.Key);
            draft.PropertyChanged += handler; unsubscribe.Add(() => draft.PropertyChanged -= handler);
        }
    }
}
