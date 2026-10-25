using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
namespace Ipc.Application.ParcelFlow.Desktop;

public sealed partial class ParameterSettingsControl
{
    void BuildCapabilities(ParameterEditorModel m)
    {
        foreach (var reader in m.Readers)
        {
            var page = Page(2, reader.Role switch { "left" => "左相机", "right" => "右相机", _ => reader.Role + " 读码实例" }, "");
            Capability(page, reader);
        }
        if (m.Readers.Length == 0)
        {
            var missing = Page(2, "缺少读码实例", "当前候选没有读码实例。请核对原拓扑和已安装驱动。");
            if (MaintenanceRequested is not null) { var open = new Button { Content = "查看实例与角色绑定…", HorizontalAlignment = HorizontalAlignment.Left }; open.Click += (_, _) => MaintenanceRequested?.Invoke(this, EventArgs.Empty); missing.Body.Children.Add(open); }
        }
        foreach (var component in m.Components.Where(c => (c.Role != "maintenance-auth" && (!FirstConfiguration || c.Bound)) && (m.ApplicationId != "sportmaster" || c.Kind is not ("weight" or "plc" or "camera" or "upload"))))
        {
            var page = Page(ParameterOwnership.ObjectCategory(component), ParameterOwnership.IsRcs(component) ? "RCS 服务" : FirstConfiguration ? m.ComponentName(component) : m.ComponentLabel(component),
                ParameterOwnership.IsRcs(component) ? "左右相机共用此连接，保存后待受控应用。" :
                ParameterOwnership.IsDriver(component) ? "按已安装硬件驱动的实际描述编辑。实例与用途由当前业务决定，保存后受控重启应用。" : "按已安装组件的实际描述编辑，保存后通过受控重启应用。");
            if (!ParameterOwnership.IsRcs(component))
                foreach (var role in m.ComponentRoles)
                {
                    PropertyChangedEventHandler renamed = (_, e) =>
                    {
                        if (e.PropertyName != nameof(CapabilityRoleDraft.Selection)) return;
                        var previous = page.Title;
                        page.Title = FirstConfiguration ? m.ComponentName(component) : m.ComponentLabel(component);
                        if (lastPages.GetValueOrDefault(page.Category) == previous) lastPages[page.Category] = page.Title;
                        RefreshSearch();
                    };
                    role.PropertyChanged += renamed; unsubscribe.Add(() => role.PropertyChanged -= renamed);
                }
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
            Technical(page, "连接与业务结果", component.Contract, "心跳网络交付不表示上游应用确认。当前协议无权威结果查询；未知原请求不自动重发。重连继续使用当前运行配置，不应用待重启修订。");
        }
    }
    sealed record ProviderChoice(string Label, Ipc.Platform.CapabilityDescriptor Provider);
    void Capability(ObjectPage page, CapabilityDraft draft)
    {
        if (Model?.ApplicationId == "sportmaster" && (draft.Kind == "observation" || ParameterOwnership.IsRcs(draft)))
        {
            var deferred = new CheckBox { Content = "稍后配置此连接", MinHeight = 40 };
            deferred.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(CapabilityDraft.ConfigureLater)) { Source = draft, Mode = BindingMode.TwoWay });
            Field(Form(page), "配置状态", deferred, "勾选后可先进入软件；此连接不启动，相关作业保持阻断。填写完成后取消勾选，保存并重启以启用连接。", draft.InstanceId + "/ConfigureLater", page);
        }
        RuntimeSummary(page, draft);
        if (draft.Role == "default-scale")
        {
            var optional = new CheckBox { Content = "绑定此可选能力", MinHeight = 38 };
            optional.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(draft.Bound)) { Source = draft, Mode = BindingMode.TwoWay }); page.Body.Children.Add(optional);
        }
        var choices = draft.Providers.Where(p => !FirstConfiguration || p == draft.Provider || p.Implementation != "manual" && !p.Implementation.StartsWith("reference-", StringComparison.Ordinal)).Select(p => new ProviderChoice(p.Implementation == "tcp-line" ? "TCP/IP（换行收码）" : p.Implementation is "manual" or "reference-integration" ? "本地模拟（旧配置）" : p.Contract == "SPORTMASTER-RCS-SORTER-v1" ? "RCS 服务" : p.Implementation.Contains("hikrobot", StringComparison.OrdinalIgnoreCase) ? "海康相机（SDK）" : ChineseInterfaceText.CapabilityKind(p.Kind), p)).ToArray();
        if (draft.Kind is "observation" or "integration" && choices.Length > 1 || draft.Provider is null)
        {
            var providers = new ComboBox { ItemsSource = choices, SelectedItem = choices.SingleOrDefault(x => x.Provider == draft.Provider), DisplayMemberPath = "Label", MinHeight = 38 };
            providers.SelectionChanged += (_, _) => { if (providers.SelectedItem is ProviderChoice selected && selected.Provider != draft.Provider) draft.Provider = selected.Provider; };
            Field(Form(page), draft.Kind == "observation" ? "相机连接方式" : draft.Kind == "integration" ? "服务连接方式" : "选择已安装组件", providers, null, draft.InstanceId + "/Provider", page);
            locations[draft.InstanceId] = locations[draft.InstanceId + "/Provider"];
        }
        if (draft.Provider is null)
        {
            var issue = Text("缺少对应驱动或组件，请安装匹配的程序包。"); issue.Foreground = Brush("#B02D35"); page.Body.Children.Add(issue); return;
        }
        if (FirstConfiguration && (draft.Kind == "observation" && draft.Provider.Implementation == "manual" || draft.Kind == "integration" && draft.Provider.Implementation == "reference-integration"))
        {
            var missing = Text(draft.Kind == "observation"
                ? "请选择 TCP/IP 连接方式，填写实际相机 IP 和端口。原模拟用途和历史数据仍保留。"
                : "当前为旧模拟连接。可在上方选择 RCS 服务，再填写现场服务地址；原用途和历史数据仍保留。");
            missing.Foreground = Brush("#915300"); page.Body.Children.Add(missing); return;
        }
        var isRcs = ParameterOwnership.IsRcs(draft);
        if (draft.Kind != "observation") Input(Form(page, isRcs ? "服务连接" : "连接"), isRcs ? "服务地址 *" : ParameterOwnership.IsDriver(draft) ? "设备连接目标" : "组件连接目标", draft, "Target", draft.InstanceId + "/Target", page,
            ParameterOwnership.IsRcs(draft) ? "HTTP(S) 地址，路径为 /rcs/sorter/api。" : draft.TargetBoundary);
        if (isRcs)
        {
            var numbers = Form(page, "请求编号区间");
            foreach (var p in draft.Parameters.Where(p => p.Field.Key is "request-id-base" or "request-id-count")) Parameter(numbers, page, draft, p);
        }
        var form = Form(page, draft.Kind == "observation" ? "连接" : isRcs ? "协议与心跳" : null);
        var advanced = new List<CapabilityParameter>();
        foreach (var p in draft.Parameters.Where(p => ParameterOwnership.Category(draft, p.Field.Key) == page.Category))
        {
            if (Model!.ApplicationId == "sportmaster" && draft.Provider.Implementation == "tcp-line" && p.Field.Key == "entity.mode") continue;
            if (p.Field.Key is "decision" or "reply-mode" or "complete-on-query") advanced.Add(p);
            else if (isRcs && p.Field.Key is "request-id-base" or "request-id-count") continue;
            else if (draft.Kind == "observation" && !p.Field.Required && (p.Field.Key is "queue.capacity" or "code.encoding" || p.Field.Key == "entity.mode" && draft.Provider.Implementation != "tcp-line")) advanced.Add(p);
            else if (draft.Kind != "observation" || p.Field.Key != "bcr-code") Parameter(form, page, draft, p);
        }
        if (draft.Kind == "observation")
        {
            if (Model!.ApplicationId == "sportmaster" && draft.Provider.Implementation == "tcp-line")
                page.Body.Children.Add(Text("正常作业时，相机扫描一个包裹得到一个条码，软件据此向RCS获取分拣信息。无需选择包裹关联方式。保存时将按此流程核对旧的仅观察设置。"));
            var codes = draft.Parameters.Where(p => p.Field.Key == "bcr-code").ToArray();
            var mappings = Model!.Components.Where(ParameterOwnership.IsRcs).SelectMany(c => c.Parameters.Where(p => p.Field.Key == draft.Role + "-bcr-code").Select(p => (Component: c, Parameter: p))).ToArray();
            if (codes.Length + mappings.Length > 0)
            {
                var codeForm = Form(page, "相机编码");
                foreach (var mapping in mappings) Parameter(codeForm, page, mapping.Component, mapping.Parameter);
                foreach (var code in codes) Parameter(codeForm, page, draft, code);
            }
        }
        if (advanced.Count > 0) { var advancedForm = AdvancedForm(page, isRcs ? "请求编号区间" : draft.Kind == "observation" ? "识别与缓冲" : "模拟调试选项"); foreach (var p in advanced) Parameter(advancedForm, page, draft, p); }

        if (draft.Kind == "observation" && draft.Provider.Implementation == "manual")
        {
            var missing = Text("请选择 TCP/IP 连接方式，填写实际相机 IP 和端口。原模拟用途和历史数据仍保留。");
            missing.Foreground = Brush("#915300"); page.Body.Children.Insert(0, missing);
        }
        // Type/version/instance/contract are support metadata, not connection inputs.
    }
    static string ParameterLabel(CapabilityParameter p) => p.Field.Key switch
    {
        "zone-id" => "仓ID", "project-id" => "生产线ID", "device.ip" => "IP 地址", "device.port" or "port" => "端口", "specified-chute" => "指定格口", "chute-code" => "格口编号",
        "left-bcr-code" or "right-bcr-code" => "取格口相机编码", "bcr-code" => "心跳相机编码", "heartbeat-interval-seconds" => "心跳间隔",
        "request-id-base" => "编号起点（不含）", "request-id-count" => "编号数量", "source" => "协议来源", "locale" => "协议语言",
        "reply-mode" => "模拟回执方式", "decision" => "模拟业务结果", "complete-on-query" => "查询时报告完成", "endpoint" => "服务地址", _ => ChineseInterfaceText.ParameterName(p.Field.Key, p.EditorLabel)
    };
    static string ParameterHint(CapabilityParameter p) => p.Field.Key switch
    {
        "subjects" => "模拟认证允许的账号；不会创建 Windows 账号。现场使用实际安装的认证服务。",
        "decision" or "reply-mode" or "complete-on-query" => "仅用于模拟服务调试，不代表真实业务结果。",
        "zone-id" => "填写RCS分配给当前仓库的ID，用于确定格口请求所属仓库。",
        "project-id" => "填写RCS分配给当前生产线的ID，用于确定格口请求所属生产线。",
        "bcr-code" => "心跳上报使用，左右相机不得重复。", "left-bcr-code" or "right-bcr-code" => "本位取格口请求使用；与心跳编码分别配置。",
        "device.ip" => "按网络地址连接当前相机，不修改设备地址。", "device.serial" => "填写时同时核对网络地址和序列号。",
        "code.encoding" => "与相机输出条码报文的字符编码一致；编码不匹配会导致条码无法正确解码。",
        "entity.mode" => "仅观察只显示扫码信息，不创建作业；确认相机报文与包裹一一对应后，才可使用关联模式。", "queue.capacity" => "相机读码消息的缓冲上限；溢出后停止接收，须检查并显式重连。",
        "device.port" or "port" => "填写相机提供条码报文的TCP端口；与相机网络设置一致，范围1～65535。",
        "heartbeat-interval-seconds" => "向RCS报告供包台及相机在线状态的间隔；范围1～3600秒，保存并重启后生效。",
        "specified-chute" => "不指定时由RCS分配格口；只有现场业务要求固定格口时才选择指定，并填写格口编号。",
        "chute-code" => "请求RCS使用的目标格口编号；仅选择指定格口时发送，最终结果仍以RCS回包为准。",
        "source" => "RCS约定的请求来源标记；用于识别调用方，不是服务地址或供包台编号。",
        "locale" => "RCS协议约定的语言标记；填写接口合同要求的值，不改变本软件界面语言。",
        "request-id-base" => "RCS分配给本供包台的请求编号起点（不含）；新请求从下一编号开始，不能与其他供包台区间重叠。",
        "request-id-count" => "本供包台可使用的请求编号数量；用尽后阻止新请求，须分配新区间，不能重置旧编号重复使用。",
        _ => p.EditorHelp.Length > 100 ? "完整规则见下方参数说明。" : p.EditorHelp
    };
    sealed record BooleanChoice(string Value, string Label);
    void Parameter(Panel form, ObjectPage page, CapabilityDraft draft, CapabilityParameter p)
    {
        if (ParameterOwnership.IsRcs(draft) && p.Field.Key == "quantity") return;
        var path = draft.InstanceId + "/" + p.Field.Key;
        var required = p.Field.Required || p.Field.Key == "bcr-code" && Model!.Components.Any(ParameterOwnership.IsRcs) || ParameterOwnership.IsRcs(draft) && p.Field.Key == "chute-code" && ParameterOwnership.Applicable(draft, p.Field.Key);
        var label = (draft.Provider?.Implementation == "tcp-line" && p.Field.Key == "entity.mode" ? "包裹关联方式" : ParameterLabel(p)) + (string.IsNullOrEmpty(p.Field.Unit) || p.Field.Key == "heartbeat-interval-seconds" ? "" : "（" + p.Field.Unit + "）") + (required ? " *" : "");
        var help = draft.Provider?.Implementation == "tcp-line" && p.Field.Key == "entity.mode"
            ? "仅观察不创建作业；每行一个包裹须先确认完整报文与包裹一一对应。" : ParameterHint(p);
        if (p.IsSensitive)
        {
            // PasswordBox keeps the existing reference masked. Never replace it
            // with a display sentinel, clear it on navigation, or expose it to UIA.
            var box = new PasswordBox { Password = p.Value, MaxLength = p.EditorMaximumLength, MinHeight = 38, MaxWidth = 400, FontSize = UiTypography.Body, VerticalContentAlignment = VerticalAlignment.Center };
            box.PasswordChanged += (_, _) => { if (p.Value != box.Password) p.Value = box.Password; };
            PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName == nameof(p.Value) && box.Password != p.Value) box.Password = p.Value; };
            p.PropertyChanged += changed; unsubscribe.Add(() => p.PropertyChanged -= changed);
            Field(form, label, box, "敏感引用已隐藏；修改后统一审阅保存。" + (p.Field.ReadOnly ? "此字段只读。" : ""), path, page);
        }
        else if (p.Field.Type == "boolean" || p.Field.Key == "specified-chute")
        {
            var specified = p.Field.Key == "specified-chute";
            var choice = new ComboBox { ItemsSource = specified ? new[] { new BooleanChoice("", "请选择"), new BooleanChoice("false", "不指定"), new BooleanChoice("true", "指定") } : new[] { new BooleanChoice("false", "关闭"), new BooleanChoice("true", "启用") }, DisplayMemberPath = "Label", SelectedValuePath = "Value" };
            choice.SetBinding(ComboBox.SelectedValueProperty, new Binding("Value") { Source = p, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            Field(form, label, choice, help, path, page);
        }
        else if (p.Field.Type == "choice") Choice(form, label, p, "Value", p.Field.Choices!, path, page, help);
        else
        {
            var box = Input(form, label, p, "Value", path, page, help, p.IsList); box.MaxLength = p.EditorMaximumLength; box.IsReadOnly = p.Field.ReadOnly; if (p.Field.Type == "integer") box.MaxWidth = 240;
        }
        locations[path] = locations[path] with { Help = p.EditorHelp + " " + help };
        var accessibleHelp = help == p.EditorHelp ? help : help + "\n填写限制：" + p.EditorHelp;
        AutomationProperties.SetHelpText(locations[path].Control, accessibleHelp);
        locations[path].Control.ToolTip = accessibleHelp;
        locations[path].Control.IsEnabled = !p.Field.ReadOnly && ParameterOwnership.Applicable(draft, p.Field.Key);
        Technical(page, ParameterLabel(p), path, help + "\n填写限制：" + p.EditorHelp);
        if (ParameterOwnership.IsRcs(draft) && p.Field.Key == "chute-code")
        {
            var field = locations[path].Control;
            var row = locations[path].Row!;
            bool Applicable() => draft.Parameters.FirstOrDefault(x => x.Field.Key == "specified-chute")?.Value == "true";
            locations[path] = locations[path] with { Applicable = Applicable };
            void Update() { var selected = Applicable(); row.Visibility = selected ? Visibility.Visible : Visibility.Collapsed; field.IsEnabled = !p.Field.ReadOnly && selected; RefreshSearch(); }
            PropertyChangedEventHandler handler = (_, _) => Update();
            draft.PropertyChanged += handler; unsubscribe.Add(() => draft.PropertyChanged -= handler);
            Update();
        }
    }
}
