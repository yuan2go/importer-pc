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
            if (m.ApplicationId == "sportmaster") page.Body.Children.Add(Text("左右相机至少配置并启用一台即可。未配置的位置不接包，不影响另一台已就绪相机。"));
            Capability(page, reader);
        }
        if (m.Readers.Length == 0)
        {
            var missing = Page(2, "缺少读码实例", "当前候选没有读码实例。请核对原拓扑和已安装驱动。");
            if (MaintenanceRequested is not null) { var open = new Button { Content = "查看实例与角色绑定…", HorizontalAlignment = HorizontalAlignment.Left }; open.Click += (_, _) => MaintenanceRequested?.Invoke(this, EventArgs.Empty); missing.Body.Children.Add(open); }
        }
        foreach (var component in m.Components.Where(c => (!FirstConfiguration || c.Bound) && (m.ApplicationId != "sportmaster" || c.Kind is not ("weight" or "plc" or "camera" or "upload"))))
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
                        RefreshAvailablePages();
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
                var camera = p.Field.Key switch { "left-bcr-code" => "左相机", "right-bcr-code" => "右相机", _ => null };
                var jump = new Button { Content = "前往" + (camera ?? ParameterOwnership.CategoryName(ParameterOwnership.Category(component, p.Field.Key))), Style = (Style)Resources["ParameterQuietAction"] };
                jump.Click += (_, _) => NavigateTo(component.InstanceId + "/" + p.Field.Key); DockPanel.SetDock(jump, Dock.Right); row.Children.Add(jump);
                var value = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, Padding = new(0), MinHeight = 28 };
                value.SetBinding(TextBox.TextProperty, new Binding("Value") { Source = p, Mode = BindingMode.OneWay });
                var item = new StackPanel(); item.Children.Add(Text((camera is null ? "" : camera + " · ") + ParameterLabel(p))); item.Children.Add(value); row.Children.Add(item); references.Children.Add(row);
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
        if (Model?.ApplicationId == "sportmaster" && draft.Kind == "observation")
        {
            var identity = new TextBox { Text = draft.Provider?.Implementation == "hikrobot-mvs-reader" ? "海康 MVS＋软件识别（随软件安装）" : draft.Provider?.Implementation == "hikrobot-idmvs" ? "海康读码 SDK（MvCodeReader，随软件安装）" : "当前候选使用原有驱动；可切换为 MVS＋软件识别并核对参数", IsReadOnly = true, MinHeight = 40 };
            Field(Form(page), "相机连接", identity, null, draft.InstanceId + "/Provider", page);
            locations[draft.InstanceId] = locations[draft.InstanceId + "/Provider"];
        }
        if (draft.Role == "default-scale")
        {
            var optional = new CheckBox { Content = "绑定此可选能力", MinHeight = 38 };
            optional.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(draft.Bound)) { Source = draft, Mode = BindingMode.TwoWay }); page.Body.Children.Add(optional);
        }
        var choices = draft.Providers.Where(p => Model!.ApplicationId != "sportmaster" || draft.Kind != "observation" || p.Implementation == "hikrobot-mvs-reader").Where(p => !FirstConfiguration || p == draft.Provider || p.Implementation != "manual" && !p.Implementation.StartsWith("reference-", StringComparison.Ordinal)).Select(p => new ProviderChoice(p.Implementation == "tcp-line" ? "TCP/IP（换行收码）" : p.Implementation is "manual" or "reference-integration" ? "本地模拟" : p.Contract == "SPORTMASTER-RCS-SORTER-v1" ? "RCS 服务" : p.Implementation is "hikrobot-mvs" or "extension:hikrobot.mvs-acquisition/mvs-acquisition" ? "海康标准相机 SDK（MVS，仅采图）" : p.Implementation == "hikrobot-mvs-reader" ? "海康 MVS＋软件识别" : p.Implementation is "hikrobot-idmvs" or "extension:hikrobot.idmvs-reader/idmvs-reader" ? "海康智能读码器（MvCodeReader SDK）" : ChineseInterfaceText.CapabilityKind(p.Kind), p)).ToArray();
        if (draft.Kind is "observation" or "integration" && choices.Length > 1 || draft.Provider is null || !choices.Any(c => c.Provider == draft.Provider))
        {
            var providers = new ComboBox { ItemsSource = choices, SelectedItem = choices.SingleOrDefault(x => x.Provider == draft.Provider), DisplayMemberPath = "Label", MinHeight = 38 };
            var providerForm = Form(page);
            Field(providerForm, draft.Kind == "observation" ? "相机连接方式" : draft.Kind == "integration" ? "服务连接方式" : "选择已安装组件", providers, null, draft.InstanceId + "/Provider", page);
            var change = new StackPanel { Margin = new(0, 4, 0, 16) };
            var consequence = draft.Kind == "observation"
                ? "尚未切换，下方仍是当前方式的参数。确认后将清空本相机的连接参数，并采用新方式的默认值；请重新填写所选方式适用的参数；SDK正式启用须填写预期物理序列号。相机编码和另一相机的参数保留。"
                : "尚未切换，下方仍是当前方式的参数。确认后将清空本组件的参数，并采用新方式的默认值；服务地址保留，请核对地址并重新填写协议参数。";
            var proposal = Text(""); change.Children.Add(proposal);
            var actions = new WrapPanel { Margin = new(0, 8, 0, 0) };
            var apply = new Button { Content = "切换并重置参数", MinHeight = 40 };
            var cancel = new Button { Content = "取消切换", MinHeight = 40, Margin = new(8, 0, 0, 0) };
            AutomationProperties.SetAutomationId(apply, draft.InstanceId + "/ApplyProviderChange");
            AutomationProperties.SetAutomationId(cancel, draft.InstanceId + "/CancelProviderChange");
            apply.Click += (_, _) => draft.ApplyProviderChange();
            cancel.Click += (_, _) => { draft.CancelProviderChange(); providers.SelectedItem = choices.SingleOrDefault(x => x.Provider == draft.Provider); };
            actions.Children.Add(apply); actions.Children.Add(cancel); change.Children.Add(actions); providerForm.Children.Add(change);
            void RefreshProposal()
            {
                change.Visibility = draft.ProviderChangePending ? Visibility.Visible : Visibility.Collapsed;
                proposal.Text = "准备切换为：" + choices.SingleOrDefault(x => x.Provider == draft.ProposedProvider)?.Label + "。" + consequence;
            }
            var restoringSelection = false;
            providers.SelectionChanged += (_, _) =>
            {
                if (restoringSelection || providers.SelectedItem is not ProviderChoice selected) return;
                draft.ProposeProvider(selected.Provider);
                // Until confirmed, the selector and parameter form both identify
                // the current provider. The separate proposal names the new one.
                restoringSelection = true;
                try { providers.SelectedItem = choices.SingleOrDefault(x => x.Provider == draft.Provider); }
                finally { restoringSelection = false; }
            };
            PropertyChangedEventHandler proposed = (_, e) => { if (e.PropertyName == nameof(CapabilityDraft.ProviderChangePending)) RefreshProposal(); };
            draft.PropertyChanged += proposed; unsubscribe.Add(() => draft.PropertyChanged -= proposed); RefreshProposal();
            locations[draft.InstanceId] = locations[draft.InstanceId + "/Provider"];
        }
        if (draft.Provider is null)
        {
            var issue = Text("缺少对应驱动或组件，请安装匹配的程序包。"); issue.Foreground = Brush("#B02D35"); page.Body.Children.Add(issue); return;
        }
        if (FirstConfiguration && (draft.Kind == "observation" && draft.Provider.Implementation == "manual" || draft.Kind == "integration" && draft.Provider.Implementation == "reference-integration"))
        {
            var missing = Text(draft.Kind == "observation"
                ? "请选择已安装且适用于当前设备的连接方式，并填写实际参数。"
                : "当前为本地模拟连接。可在上方选择 RCS 服务，再填写实际服务地址与参数。");
            missing.Foreground = Brush("#915300"); page.Body.Children.Add(missing); return;
        }
        var isRcs = ParameterOwnership.IsRcs(draft);
        if (draft.Provider.Implementation is "hikrobot-mvs" or "extension:hikrobot.mvs-acquisition/mvs-acquisition")
            page.Body.Children.Add(Text("海康 SDK 随供包台软件安装，通过 IP 和预期序列号连接相机；实际连接与帧请在诊断中核对。尚缺适用的条码识别组件，不会生成条码或供包作业，正式供包不可用。"));
        if (draft.Provider.Implementation is "hikrobot-mvs-reader" or "hikrobot-idmvs" or "extension:hikrobot.idmvs-reader/idmvs-reader")
            page.Body.Children.Add(Text("电脑通过海康 SDK 连续读码。每次有效识别结果均提交 RCS，包含重复条码，由 RCS 负责去重；无需 PLC 或外部触发。启用前请关闭占用相机的旧客户端或设备工具。实际读码与 RCS 结果可在作业和诊断中核对。"));
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
            else if (draft.Kind == "observation" && !p.Field.Required && (p.Field.Key is "queue.capacity" or "queue.maximum-code-bytes" or "code.encoding" or "image.nodes" or "image.maximum-payload-bytes")) advanced.Add(p);
            else Parameter(form, page, draft, p);
        }
        if (draft.Kind == "observation")
        {
            if (Model!.ApplicationId == "sportmaster" && draft.Provider.Implementation == "tcp-line")
                page.Body.Children.Add(Text("正常作业时，相机扫描一个包裹输出一条完整报文，软件据此向RCS获取分拣信息。请先确认报文与实体包裹一一对应。"));
            var mappings = Model!.Components.Where(ParameterOwnership.IsRcs).SelectMany(c => c.Parameters.Where(p => p.Field.Key == draft.Role + "-bcr-code").Select(p => (Component: c, Parameter: p))).ToArray();
            if (mappings.Length > 0)
            {
                var codeForm = Form(page, "相机编码");
                foreach (var mapping in mappings) Parameter(codeForm, page, mapping.Component, mapping.Parameter);
            }
        }
        if (advanced.Count > 0) { var advancedForm = AdvancedForm(page, isRcs ? "请求编号区间" : draft.Provider.Implementation is "hikrobot-mvs" or "extension:hikrobot.mvs-acquisition/mvs-acquisition" ? "采集缓冲" : draft.Kind == "observation" ? "识别与缓冲" : "模拟调试选项"); foreach (var p in advanced) Parameter(advancedForm, page, draft, p); }

        if (draft.Kind == "observation" && draft.Provider.Implementation == "manual")
        {
            var missing = Text("请选择已安装且适用于当前设备的连接方式，并填写实际参数。");
            missing.Foreground = Brush("#915300"); page.Body.Children.Insert(0, missing);
        }
        // Type/version/instance/contract are support metadata, not connection inputs.
    }
    static string ParameterLabel(CapabilityParameter p) => p.Field.Key switch
    {
        "zone-id" => "仓ID", "project-id" => "生产线ID", "device.ip" => "IP 地址", "device.port" or "port" => "端口", "specified-chute" => "指定格口", "chute-code" => "格口编号",
        "left-bcr-code" or "right-bcr-code" => "相机编码", "heartbeat-interval-seconds" => "心跳间隔",
        "request-id-base" => "编号起点（不含）", "request-id-count" => "编号数量", "source" => "协议来源", "locale" => "协议语言",
        "reply-mode" => "模拟回执方式", "decision" => "模拟业务结果", "complete-on-query" => "查询时报告完成", "endpoint" => "服务地址", _ => ChineseInterfaceText.ParameterName(p.Field.Key, p.EditorLabel)
    };
    static string ParameterHint(CapabilityParameter p) => p.Field.Key switch
    {
        "subjects" => "模拟认证允许的账号；不会创建 Windows 账号。现场使用实际安装的认证服务。",
        "decision" or "reply-mode" or "complete-on-query" => "仅用于模拟服务调试，不代表真实业务结果。",
        "zone-id" => "填写RCS分配给当前仓库的ID，用于确定格口请求所属仓库。",
        "project-id" => "填写RCS分配给当前生产线的ID，用于确定格口请求所属生产线。",
        "left-bcr-code" or "right-bcr-code" => "本相机取格口请求与心跳共用这一编码，左右相机不得重复。更换连接方式不会清空此编码。",
        "device.ip" => "按网络地址连接当前相机，不修改设备地址。", "device.serial" => "正式启用必填，按预期物理序列号核对本位相机；模拟或独立诊断可留空。",
        "code.encoding" => "与相机输出条码报文的字符编码一致；编码不匹配会导致条码无法正确解码。",
        "entity.mode" => "仅观察不创建作业。触发关联要求设备已开启触发，并已确认一触发一个包裹、同触发多帧及重连后的原包裹处置；本选项不会修改相机触发设置。", "queue.capacity" => "条码观察的条数上限；同时受字节预算限制。溢出后停止接收，须检查并显式重连。",
        "queue.maximum-code-bytes" => "排队及正在处理的条码缓冲预算；默认16777216字节（16MiB），范围1～64MiB，不是原图缓存。超限后故障阻断，须检查并受控恢复。",
        "device.port" or "port" => "填写相机提供条码报文的TCP端口；与相机网络设置一致，范围1～65535。",
        "heartbeat-interval-seconds" => "向RCS报告供包台及相机在线状态的间隔；范围1～3600秒，保存并重启后生效。",
        "request-timeout-ms" => "单次取格口请求的总时限，包含连接和响应读取；默认5000毫秒，范围100～30000毫秒，保存并重启后生效。超时后提示重新扫描。",
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
        var cameraCodeRequired = ParameterOwnership.IsRcs(draft) && p.Field.Key is "left-bcr-code" or "right-bcr-code";
        var required = p.Field.Required || cameraCodeRequired || ParameterOwnership.IsRcs(draft) && p.Field.Key == "chute-code" && ParameterOwnership.Applicable(draft, p.Field.Key);
        var label = (draft.Provider?.Implementation is "hikrobot-idmvs" or "hikrobot-mvs-reader" && p.Field.Key == "entity.mode" ? "读码结果处理" : draft.Provider?.Implementation == "tcp-line" && p.Field.Key == "entity.mode" ? "包裹关联方式" : ParameterLabel(p)) + (string.IsNullOrEmpty(p.Field.Unit) || p.Field.Key == "heartbeat-interval-seconds" ? "" : "（" + p.Field.Unit + "）") + (required ? " *" : "");
        var help = draft.Provider?.Implementation is "hikrobot-mvs" or "hikrobot-idmvs" or "hikrobot-mvs-reader" && p.Field.Key == "entity.mode" ? p.Field.Description : draft.Provider?.Implementation == "tcp-line" && p.Field.Key == "entity.mode"
            ? "仅观察不创建作业；每行一个包裹须先确认完整报文与包裹一一对应。" : draft.Provider?.Implementation == "extension:hikrobot.mvs-acquisition/mvs-acquisition" ? p.EditorHelp : ParameterHint(p);
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
