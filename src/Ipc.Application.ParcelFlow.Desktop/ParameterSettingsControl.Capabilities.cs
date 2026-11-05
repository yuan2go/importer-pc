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
            if (m.ApplicationId == "sportmaster") page.Body.Children.Add(Text("填写要使用的相机；另一台不使用时留空。"));
            Capability(page, reader);
        }
        if (m.Readers.Length == 0)
        {
            var missing = Page(2, "缺少读码实例", "没有找到这台相机的配置，请用原安装包修复软件。");
            if (MaintenanceRequested is not null) { var open = new Button { Content = "查看实例与角色绑定…", HorizontalAlignment = HorizontalAlignment.Left }; open.Click += (_, _) => MaintenanceRequested?.Invoke(this, EventArgs.Empty); missing.Body.Children.Add(open); }
        }
        foreach (var component in m.Components.Where(c => (!FirstConfiguration || c.Bound) && (m.ApplicationId != "sportmaster" || c.Kind is not ("weight" or "plc" or "camera" or "upload"))))
        {
            var page = Page(ParameterOwnership.ObjectCategory(component), ParameterOwnership.IsRcs(component) ? "RCS 服务" : FirstConfiguration ? m.ComponentName(component) : m.ComponentLabel(component),
                ParameterOwnership.IsRcs(component) && m.ApplicationId == "sportmaster" ? "左右投递位共用此连接。符合条件时保存后重连 RCS 生效，否则需受控重启；以保存回执为准。" : "保存并重启软件后生效。");
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
            page.Body.Children.Add(Text("心跳正常只说明 RCS 可以连接；格口未确认的包裹不会自动重发。"));
            page.Body.Children.Add(new Expander { Header = "其他页面的相关设置", Content = references, Margin = new(0, 20, 24, 0) });
        }
    }
    sealed record ProviderChoice(string Label, Ipc.Platform.CapabilityDescriptor Provider);
    void Capability(ObjectPage page, CapabilityDraft draft)
    {
        if (Model?.ApplicationId != "sportmaster" || draft.Kind != "observation") RuntimeSummary(page, draft);
        if (Model?.ApplicationId == "sportmaster" && draft.Kind == "observation")
        {
            var sdkSelected = draft.Provider?.Implementation == "hikrobot-mvs-reader";
            var identity = new TextBox { Text = sdkSelected ? "海康相机（随软件安装）" : "原相机设置不适用于当前 MVS SDK", IsReadOnly = true, MinHeight = 40 };
            Field(Form(page), "相机驱动", identity, null, draft.InstanceId + "/Provider", page);
            locations[draft.InstanceId] = locations[draft.InstanceId + "/Provider"];
            if (!sdkSelected)
            {
                page.Body.Children.Add(Text("这台相机的设置来自旧版本。请点击下方按钮重新配置，再填写 IP 和序列号；相机编码和另一台相机不受影响。"));
                var sdk = draft.Providers.SingleOrDefault(p => p.Implementation == "hikrobot-mvs-reader");
                var useSdk = new Button { Content = "按 MVS SDK 重新配置本相机", MinHeight = 44, IsEnabled = sdk is not null, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 12, 0, 0) };
                AutomationProperties.SetAutomationId(useSdk, draft.InstanceId + "/ApplyProviderChange");
                useSdk.Click += (_, _) => { draft.ProposeProvider(sdk!); draft.ApplyProviderChange(); };
                page.Body.Children.Add(useSdk);
                if (sdk is null) page.Body.Children.Add(Text("当前安装缺少海康 SDK 相机入口，请使用完整的当前安装包修复。"));
                CameraCode(page, draft);
                return; // Never mix old TCP fields with a proposed SDK connection.
            }
            page.Body.Children.Add(Text("通过 IP 地址找到相机，通过序列号确认是这一台。"));
            page.Body.Children.Add(Text("填写 IP 和序列号，保存并重启软件后连接。不使用这台相机时，两项都留空。"));
        }
        if (draft.Role == "default-scale")
        {
            var optional = new CheckBox { Content = "绑定此可选能力", MinHeight = 38 };
            optional.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(draft.Bound)) { Source = draft, Mode = BindingMode.TwoWay }); page.Body.Children.Add(optional);
        }
        var fixedRcs = Model?.ApplicationId == "sportmaster" && draft.Kind == "integration";
        if (fixedRcs && !draft.Providers.Contains(draft.Provider!))
        {
            page.Body.Children.Add(Text("此连接不适用于当前 SM。请重新配置 RCS 服务，并填写实际服务地址。"));
            var rcs = draft.Providers.SingleOrDefault();
            var configure = new Button { Content = "配置 RCS 服务", MinHeight = 44, IsEnabled = rcs is not null, HorizontalAlignment = HorizontalAlignment.Left };
            configure.Click += (_, _) => { draft.ProposeProvider(rcs!); draft.ApplyProviderChange(); if (!Uri.TryCreate(draft.Target, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https")) draft.Target = ""; };
            page.Body.Children.Add(configure);
            return;
        }
        var choices = draft.Providers.Where(p => Model!.ApplicationId != "sportmaster" || draft.Kind != "observation" || p.Implementation == "hikrobot-mvs-reader").Where(p => !FirstConfiguration || p == draft.Provider || p.Implementation != "manual" && !p.Implementation.StartsWith("reference-", StringComparison.Ordinal)).Select(p => new ProviderChoice(p.Implementation == "tcp-line" ? "TCP/IP（换行收码）" : p.Implementation is "manual" or "reference-integration" ? "本地模拟" : p.Contract == "SPORTMASTER-RCS-SORTER-v1" ? "RCS 服务" : p.Implementation is "hikrobot-mvs" or "extension:hikrobot.mvs-acquisition/mvs-acquisition" ? "海康标准相机 SDK（MVS，仅采图）" : p.Implementation == "hikrobot-mvs-reader" ? "海康 MVS＋软件识别" : p.Implementation is "hikrobot-idmvs" or "extension:hikrobot.idmvs-reader/idmvs-reader" ? "海康智能读码器（MvCodeReader SDK）" : ChineseInterfaceText.CapabilityKind(p.Kind), p)).ToArray();
        if (!fixedRcs && (draft.Kind is "observation" or "integration" && choices.Length > 1 || draft.Provider is null || !choices.Any(c => c.Provider == draft.Provider)))
        {
            var providers = new ComboBox { ItemsSource = choices, SelectedItem = choices.SingleOrDefault(x => x.Provider == draft.Provider), DisplayMemberPath = "Label", MinHeight = 38 };
            var providerForm = Form(page);
            Field(providerForm, draft.Kind == "observation" ? "相机连接方式" : draft.Kind == "integration" ? "服务连接方式" : "选择已安装组件", providers, null, draft.InstanceId + "/Provider", page);
            var change = new StackPanel { Margin = new(0, 4, 0, 16) };
            var consequence = draft.Kind == "observation"
                ? "还未切换。确认后会清空这台相机的连接参数并改用新方式的默认值，需要重新填写；相机编码和另一台相机不受影响。"
                : "还未切换。确认后会清空这项连接的参数（服务地址保留），需要重新填写。";
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
            var issue = Text("缺少对应的组件，请用原安装包修复软件。"); issue.SetResourceReference(TextBlock.ForegroundProperty, "Danger"); page.Body.Children.Add(issue); return;
        }
        if (FirstConfiguration && (draft.Kind == "observation" && draft.Provider.Implementation == "manual" || draft.Kind == "integration" && draft.Provider.Implementation == "reference-integration"))
        {
            var missing = Text(draft.Kind == "observation"
                ? "请选择连接方式并填写参数。"
                : "当前是本地模拟。正式使用请在上方选择“RCS 服务”并填写地址。");
            missing.SetResourceReference(TextBlock.ForegroundProperty, "Warning"); page.Body.Children.Add(missing); return;
        }
        var isRcs = ParameterOwnership.IsRcs(draft);
        if (draft.Provider.Implementation is "hikrobot-mvs" or "extension:hikrobot.mvs-acquisition/mvs-acquisition")
            page.Body.Children.Add(Text("这种方式只采图、不识别条码，不能用于正式供包。"));
        if (draft.Provider.Implementation is "hikrobot-mvs-reader" or "hikrobot-idmvs" or "extension:hikrobot.idmvs-reader/idmvs-reader")
            page.Body.Children.Add(Text("相机连续读码，供包台筛选条码，同一次放置只请求一次格口。启用前请关闭其他占用相机的软件，例如 MVS 客户端。"));
        if (draft.Kind != "observation") Input(Form(page, isRcs ? "服务连接" : "连接"), isRcs ? "服务地址 *" : ParameterOwnership.IsDriver(draft) ? "设备连接目标" : "组件连接目标", draft, "Target", draft.InstanceId + "/Target", page,
            ParameterOwnership.IsRcs(draft) ? "HTTP(S) 地址，路径为 /rcs/sorter/api。" : draft.TargetBoundary);
        var form = Form(page, draft.Kind == "observation" ? "连接" : isRcs ? "协议与心跳" : null);
        var advanced = new List<CapabilityParameter>();
        var engine = draft.Parameters.FirstOrDefault(p => p.Field.Key == "reader.engine");
        if (engine is not null)
        {
            PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName == nameof(CapabilityParameter.Value)) Render(); };
            engine.PropertyChanged += changed; unsubscribe.Add(() => engine.PropertyChanged -= changed);
        }
        foreach (var p in draft.Parameters.Where(p => ParameterOwnership.Category(draft, p.Field.Key) == page.Category))
        {
            if (Model!.ApplicationId == "sportmaster" && draft.Provider.Implementation == "hikrobot-mvs-reader" && p.Field.Key is "entity.mode" or "acquisition.trigger") continue;
            if (engine?.Value == "mvid-native" && p.Field.Key == "code.try-invert") continue;
            if (engine?.Value == "mvid-native" && p.Field.Key == "code.formats") continue;
            if (p.Field.Key is "decision" or "reply-mode" or "complete-on-query") advanced.Add(p);
            else if (draft.Kind == "observation" && !p.Field.Required && (p.Field.Key is "queue.capacity" or "queue.maximum-code-bytes" or "code.encoding" or "image.nodes" or "image.maximum-payload-bytes" or "motion.still-ms")) advanced.Add(p);
            else Parameter(form, page, draft, p);
        }
        if (draft.Kind == "observation")
        {
            CameraCode(page, draft);
        }
        if (advanced.Count > 0) { var advancedForm = AdvancedForm(page, isRcs ? "请求编号区间" : draft.Provider.Implementation is "hikrobot-mvs" or "extension:hikrobot.mvs-acquisition/mvs-acquisition" ? "采集缓冲" : draft.Kind == "observation" ? "识别与缓冲" : "模拟调试选项"); foreach (var p in advanced) Parameter(advancedForm, page, draft, p); }

        if (draft.Kind == "observation" && draft.Provider.Implementation == "manual")
        {
            var missing = Text("请选择连接方式并填写参数。");
            missing.SetResourceReference(TextBlock.ForegroundProperty, "Warning"); page.Body.Children.Insert(0, missing);
        }
        // Type/version/instance/contract are support metadata, not connection inputs.
    }
    void CameraCode(ObjectPage page, CapabilityDraft draft)
    {
        var mappings = Model!.Components.Where(ParameterOwnership.IsRcs).SelectMany(c => c.Parameters.Where(p => p.Field.Key == draft.Role + "-bcr-code").Select(p => (Component: c, Parameter: p))).ToArray();
        if (mappings.Length == 0) return;
        var form = Form(page, "相机编码");
        foreach (var mapping in mappings) Parameter(form, page, mapping.Component, mapping.Parameter);
    }
    static string ParameterLabel(CapabilityParameter p) => p.Field.Key switch
    {
        "zone-id" => "仓ID",
        "project-id" => "生产线ID",
        "device.ip" => "IP 地址",
        "device.port" or "port" => "端口",
        "left-bcr-code" => "左相机编码",
        "right-bcr-code" => "右相机编码",
        "heartbeat-interval-ms" => "心跳间隔",
        "request-timeout-ms" => "取格口请求超时",
        "no-dest-to-exp" => "无可用格口时",
        "source" => "协议来源",
        "locale" => "协议语言",
        "reply-mode" => "模拟返回方式",
        "decision" => "模拟业务结果",
        "complete-on-query" => "查询时报告完成",
        "endpoint" => "服务地址",
        _ => ChineseInterfaceText.ParameterName(p.Field.Key, p.EditorLabel)
    };
    static string ParameterHint(CapabilityParameter p) => p.Field.Key switch
    {
        "subjects" => "模拟认证允许的账号；不会创建 Windows 账号。现场使用实际安装的认证服务。",
        "decision" or "reply-mode" or "complete-on-query" => "仅用于模拟服务调试，不代表真实业务结果。",
        "zone-id" => "RCS 分配的仓库编号。",
        "project-id" => "RCS 分配的产线编号。",
        "left-bcr-code" => "左投递位使用的相机编码，取格口与心跳共用；不能与右相机编码相同。",
        "right-bcr-code" => "右投递位使用的相机编码，取格口与心跳共用；不能与左相机编码相同。",
        "device.ip" => "相机的 IP 地址（软件不会修改相机设置）。",
        "device.serial" => "相机机身上的序列号，正式使用必填。",
        "code.encoding" => "与相机输出条码报文的字符编码一致；编码不匹配会导致条码无法正确解码。",
        "entity.mode" => "仅观察不创建作业。触发关联要求设备已开启触发，并已确认一触发一个包裹、同触发多帧及重连后的原包裹处置；本选项不会修改相机触发设置。",
        "queue.capacity" => "排队等待处理的条码最多多少条。排满后停止接收，需要检查后手动重连。",
        "queue.maximum-code-bytes" => "条码排队占用的内存上限，默认 16 MiB，可填 1–64 MiB。超出后本位停止供包，需要检查后恢复。",
        "device.port" or "port" => "填写相机提供条码报文的TCP端口；与相机网络设置一致，范围1～65535。",
        "heartbeat-interval-ms" => "多久向 RCS 报告一次在线状态，默认1000毫秒，可填100–3600000毫秒。小车状态只提示托盘放包，不限制条码受理或格口申请。",
        "code.formats" => "只勾选面单实际使用的条码种类：勾得越少识别越快，也越不容易读到包装上的其他条码（如商品码）而整帧拒收。没勾的种类不会被识别。",
        "code.try-invert" => "识别白底黑条以外的浅色条码配深色背景。面单都是白底黑条时可关闭以提速；关闭前请用真实面单确认。",
        "motion.still-ms" => "包裹放稳多久后才接收识别结果，默认 200 毫秒，可填 0–2000 毫秒。放置过程中的结果不进入供包业务；画面一直不稳时仍会间隔接收。填 0 表示每帧接收。",
        "request-timeout-ms" => "一次取格口最多等待多久，默认 5000 毫秒，可填 100–30000 毫秒。超时后结果未确认，请到记录核对；不会自动重发。",
        "source" => "RCS 接口约定的来源标记，按接口文档填写。",
        "locale" => "RCS 接口约定的语言标记，按接口文档填写（不影响本软件界面语言）。",
        _ => p.EditorHelp
    };
    sealed record BooleanChoice(string Value, string Label);
    void Parameter(Panel form, ObjectPage page, CapabilityDraft draft, CapabilityParameter p)
    {
        if (ParameterOwnership.IsRcs(draft) && p.Field.Key == "quantity") return;
        var path = draft.InstanceId + "/" + p.Field.Key;
        var cameraCodeRequired = ParameterOwnership.IsRcs(draft) && p.Field.Key is "left-bcr-code" or "right-bcr-code";
        var required = p.Field.Required || cameraCodeRequired;
        var label = (draft.Provider?.Implementation is "hikrobot-idmvs" or "hikrobot-mvs-reader" && p.Field.Key == "entity.mode" ? "读码结果处理" : draft.Provider?.Implementation == "tcp-line" && p.Field.Key == "entity.mode" ? "包裹关联方式" : ParameterLabel(p)) + (string.IsNullOrEmpty(p.Field.Unit) || p.Field.Key is "heartbeat-interval-ms" or "request-timeout-ms" ? "" : "（" + p.Field.Unit + "）") + (required ? " *" : "");
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
            Field(form, label, box, "敏感内容已隐藏。" + (p.Field.ReadOnly ? "此字段只读。" : ""), path, page);
        }
        else if (p.Field.Type == "boolean")
        {
            var values = ParameterOwnership.IsRcs(draft) && p.Field.Key == "no-dest-to-exp"
                ? new[] { new BooleanChoice("true", "送往异常口（默认）"), new BooleanChoice("false", "留在原位，人工处理") }
                : new[] { new BooleanChoice("false", "关闭"), new BooleanChoice("true", "启用") };
            var choice = new ComboBox { ItemsSource = values, DisplayMemberPath = "Label", SelectedValuePath = "Value", MinHeight = 44 };
            choice.SetBinding(ComboBox.SelectedValueProperty, new Binding("Value") { Source = p, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            Field(form, label, choice, help, path, page);
        }
        else if (p.Field.Type == "string-list" && p.Field.Choices is { Length: > 0 }) ChoiceList(form, label, p, path, page, help);
        else if (p.Field.Type == "choice") Choice(form, label, p, "Value", p.Field.Choices!, path, page, help);
        else
        {
            var box = Input(form, label, p, "Value", path, page, help, p.IsList); box.MaxLength = p.EditorMaximumLength; box.IsReadOnly = p.Field.ReadOnly; if (p.Field.Type == "integer") box.MaxWidth = 240;
        }
        var requirements = ChineseInterfaceText.ParameterMessage(p.EditorHelp, help ?? "请按字段说明填写。");
        locations[path] = locations[path] with { Help = requirements + " " + help };
        var accessibleHelp = help == requirements ? help : help + "\n填写要求：" + requirements;
        AutomationProperties.SetHelpText(locations[path].Control, accessibleHelp);
        locations[path].Control.ToolTip = accessibleHelp;
        locations[path].Control.IsEnabled = !p.Field.ReadOnly;
    }
}
