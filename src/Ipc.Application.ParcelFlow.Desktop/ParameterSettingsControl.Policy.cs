using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Ipc.Application.Contracts;
namespace Ipc.Application.ParcelFlow.Desktop;

public sealed partial class ParameterSettingsControl
{
    void BuildSystem(ParameterEditorModel m)
    {
        var station = Page(0, "供包台标识", "取格口与心跳编码分别维护。");
        var hasRcs = m.Components.Any(ParameterOwnership.IsRcs);
        var form = Form(station, hasRcs ? "取格口身份" : null);
        foreach (var rcs in m.Components.Where(ParameterOwnership.IsRcs))
        {
            foreach (var p in rcs.Parameters.Where(p => p.Field.Key is "zone-id" or "project-id")) Parameter(form, station, rcs, p);
            var left = rcs.Parameters.FirstOrDefault(p => p.Field.Key == "left-station-code"); var right = rcs.Parameters.FirstOrDefault(p => p.Field.Key == "right-station-code");
            if (left is not null && right is not null)
            {
                var pair = new CommonStationParameter(left, right); unsubscribe.Add(pair.Dispose);
                if (ParameterOwnership.StationConflict(rcs))
                {
                    var conflict = Text("需要核对旧设置：左右取格口站码不同。" + pair.OriginalValues + "。请填写本站共同使用的编码。");
                    conflict.Foreground = Brush("#915300"); station.Body.Children.Add(conflict);
                }
                Input(Form(station), "供包台编码 *", pair, "Value", rcs.InstanceId + "/left-station-code", station, "左右相机的格口请求共同使用此编码。");
                locations[rcs.InstanceId + "/right-station-code"] = locations[rcs.InstanceId + "/left-station-code"];
                Technical(station, "供包台编码", rcs.InstanceId + "/left-station-code / right-station-code", pair.OriginalValues + "。明确填写共同值才统一两个候选；原请求保持原值。");
            }
        }
        form = Form(station, hasRcs ? "心跳身份" : null);
        Input(form, "供包台编码 *", m.Policy, "StationCode", "StationCode", station, "用于周期心跳；与上方取格口编码分别配置。");
        Technical(station, "本地工作站标识", "StationId", m.StationId);
        Technical(station, "心跳供包台编码", "StationCode", "不自动替换取格口供包台编码；长度1～256，不含控制字符。");
        var data = Page(0, "数据与记录", ""); form = Form(data);
        Input(form, "常驻记录", m.Policy, "ResidentHistory", "ResidentHistory", data, "1～10000");
        Input(form, "磁盘预留", m.Policy, "MinimumFreeMiB", "MinimumFreeMiB", data, "空间不足时停止新入场。");
        Input(form, "日志保留", m.Policy, "DiagnosticLogDays", "DiagnosticLogDays", data, "仅诊断日志；留空按容量轮转。");
        Input(form, "已结历史保留", m.Policy, "HistoryDays", "HistoryDays", data, "7～30天；到期在维护窗口删除已结明细。留空保持原历史。");
        Technical(data, "历史到期维护", "HistoryDays", "保存并重启后生效。先暂停和退出软件，再用本发布的expire-history命令备份、核验并删除到期已结明细；未决责任、防重及编号索引永久保留。正常运行期间不执行批量删除。");
        data.Body.Children.Add(Text("存储位置", true));
        Readonly(data, "配置目录", m.Locations?.ConfigurationDirectory ?? "未取得配置目录。");
        Readonly(data, "数据目录", m.Locations?.DataDirectory ?? "未取得数据目录。");
        Technical(data, "日志保留", "DiagnosticLogDays", "留空按容量轮转。仅控制诊断日志；业务审计与图片证据按各自规则保留。非空为1～3650天。" );
        Technical(data, "记录与容量", "ResidentHistory / MinimumFreeMiB", "当前窗口常驻记录1～10000条，磁盘预留空间1～1048576 MiB（二进制兆字节）。运行内目录只读。同库归档不会释放磁盘；迁移在独立维护中处理原目录、完整备份和原责任。");
        var backup = Page(0, "自动备份", "仅在暂停、管理模式有效且无在途外呼时执行。");
        var enabled = new CheckBox { Content = "启用自动备份", MinHeight = 38 };
        enabled.SetBinding(CheckBox.IsCheckedProperty, new Binding("BackupEnabled") { Source = m.Policy, Mode = BindingMode.TwoWay });
        Field(Form(backup), "备份计划", enabled, null, "BackupEnabled", backup);
        form = Form(backup); form.SetBinding(IsEnabledProperty, new Binding("BackupEnabled") { Source = m.Policy });
        Input(form, "备份目录", m.Policy, "BackupDirectory", "BackupDirectory", backup, "填写绝对路径。");
        form = Form(backup); form.SetBinding(IsEnabledProperty, new Binding("BackupEnabled") { Source = m.Policy });
        Input(form, "备份间隔", m.Policy, "BackupIntervalMinutes", "BackupIntervalMinutes", backup, "15～10080");
        Input(form, "保留份数", m.Policy, "BackupMaximumSets", "BackupMaximumSets", backup, "1～1000；不自动删除旧备份。");
        if (BackupRequested is not null)
        {
            backup.Body.Children.Add(Text("手动备份", true));
            backup.Body.Children.Add(Text("创建一份新的完整恢复集，保存到明确选择的路径。"));
            var open = new Button { Content = "立即完整备份…", HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += (_, _) => BackupRequested?.Invoke(this, EventArgs.Empty); backup.Body.Children.Add(open);
        }
        Technical(backup, "自动备份", "BackupEnabled / BackupDirectory / BackupIntervalMinutes / BackupMaximumSets", "完整备份包含数据库和同名恢复目录，两者须一起保留。关闭计划时不提交备份配置，本次输入可留在草稿中；既有完整恢复集保留。参数模板不包含数据库和恢复目录。");
        var display = Page(0, "显示偏好", "仅影响本机显示。");
        if (RuntimeContext is { } runtime)
        {
            form = Form(display);
            var name = Input(form, "站点显示名称", runtime, "DisplayNameDraft", "DisplayNameDraft", display, "选填，最多64字。");
            var target = Input(form, "目标效率", runtime, "TargetEfficiencyDraft", "TargetEfficiencyDraft", display, "选填，1～100000。");
            var apply = new Button { Content = "应用本机显示", HorizontalAlignment = HorizontalAlignment.Left, MinHeight = 44 };
            apply.Click += (_, _) => { if (!runtime.ApplyStationDisplay()) { if (runtime.DisplayPreferenceStatus.Contains("目标效率", StringComparison.Ordinal)) target.Focus(); else name.Focus(); } };
            display.Body.Children.Add(apply);
            var feedback = Text(""); feedback.SetBinding(TextBlock.TextProperty, new Binding("DisplayPreferenceStatus") { Source = runtime });
            AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite); display.Body.Children.Add(feedback);
        }
        display.Body.Children.Add(Text("简体中文 · 浅色"));
        display.Body.Children.Add(Text("窗口布局", true));
        display.Body.Children.Add(Text("小窗口会自动使用紧凑布局。"));
        if (DisplayPreferencesRequested is not null)
        {
            var change = new Button { Content = "切换标准 / 紧凑布局", HorizontalAlignment = HorizontalAlignment.Left };
            change.Click += (_, _) => DisplayPreferencesRequested?.Invoke(this, EventArgs.Empty); display.Body.Children.Add(change);
        }
        else display.Body.Children.Add(Text("完成首次配置后，可在作业窗口调整本机布局。"));
        Technical(display, "本机显示", "PresentationPreferences", "按当前 Windows 用户保存，不进入业务配置修订；工作台保留本地标识。目标效率不改变输送速度、节拍或实际产量；不会启动设备或恢复新入场。");
    }
    void SessionLifetime(Panel form, ObjectPage page, ParameterEditorModel m)
    {
        const string path = "SessionLifetimeMs";
        var input = Input(form, "会话时长", m.Policy, path, path, page,
            "仅用于操作员登录。");
        input.ToolTip = "100～86400000 毫秒";
        AutomationProperties.SetHelpText(input, "操作员登录会话时长，100～86400000 毫秒；与管理模式和 Windows 授权分开。");
        Technical(page, "操作员会话", path, "100～86400000 毫秒；仅用于作业登录，不是管理模式有效期。");
        var field = locations[path].Row!;
        bool Applicable() => m.HasOperatorLogin || m.Issues.Any(i => i.Path == path);
        locations[path] = locations[path] with { Applicable = Applicable };
        void Refresh() { field.Visibility = Applicable() ? Visibility.Visible : Visibility.Collapsed; RefreshSearch(); }
        PropertyChangedEventHandler changed = (_, _) => Refresh();
        if (m.OperatorLoginRole is { } role)
        {
            role.PropertyChanged += changed; unsubscribe.Add(() => role.PropertyChanged -= changed);
        }
        else foreach (var draft in m.Components.Where(c => c.Role == "operator-login"))
        {
            draft.PropertyChanged += changed; unsubscribe.Add(() => draft.PropertyChanged -= changed);
        }
        EventHandler validated = (_, _) => Refresh();
        m.ValidationChanged += validated; unsubscribe.Add(() => m.ValidationChanged -= validated);
        Refresh();
    }
    // Translate the visible choice only; role binding and the original instance identity remain intact.
    sealed class OperatorLoginChoiceText(ParameterEditorModel model) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is CapabilityChoice { Capability: { } capability } ? model.ComponentName(capability) : "不要求操作员登录";
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
    void BuildBusiness(ParameterEditorModel m)
    {
        var login = Page(1, "作业登录", "操作员登录与管理模式分别授权。");
        if (m.OperatorLoginRole is { } role)
        {
            var caption = new FrameworkElementFactory(typeof(TextBlock));
            caption.SetBinding(TextBlock.TextProperty, new Binding { Converter = new OperatorLoginChoiceText(m) });
            caption.SetBinding(ToolTipProperty, new Binding("Label"));
            var selection = new ComboBox { ItemsSource = role.Choices, ItemTemplate = new DataTemplate { VisualTree = caption }, MinHeight = 38 };
            selection.SetBinding(ComboBox.SelectedItemProperty, new Binding("Selection") { Source = role, Mode = BindingMode.TwoWay });
            Field(Form(login), "操作员登录服务", selection, "不绑定时无需作业登录；连接参数在组件中维护。", "OperatorLogin", login);
        }
        else if (m.Components.SingleOrDefault(c => c.Role == "operator-login") is { } optionalLogin)
        {
            var enable = new CheckBox { Content = "要求操作员登录", MinHeight = 38 };
            enable.SetBinding(CheckBox.IsCheckedProperty, new Binding("Bound") { Source = optionalLogin, Mode = BindingMode.TwoWay });
            Field(Form(login), "作业登录要求", enable, "启用后需在组件中选择已安装的登录服务并设置连接。", "OperatorLogin", login);
        }
        SessionLifetime(Form(login), login, m);
        var page = Page(1, m.ApplicationId == "sportmaster" ? "条码筛选" : "业务策略", m.ApplicationId == "sportmaster" ? "从同一次观察的候选中选出唯一有效条码。左右相机共用此规则。" : "编辑当前业务策略。"); var form = Form(page);
        if (m.ApplicationId == "sportmaster")
        {
            Input(form, "匹配规则（可选）", m.Policy, "BarcodePattern", "BarcodePattern", page, "完整匹配；留空不筛选。同一次观察仍须得到唯一有效候选。", true);
            Input(form, "排除规则（可选）", m.Policy, "BarcodeExclusionPattern", "BarcodeExclusionPattern", page, "完整匹配后排除；留空不排除。", true);
            Technical(page, "筛选执行边界", "BarcodeFilter.Pattern", "整串匹配；单次25ms、总计100ms。留空不筛选；仍须唯一有效码；不默认选首码，不去重制造唯一候选。");
            Preview(page, m);
            var rules = Page(1, "格口请求", "设置每次请求数量，以及是否向上游请求指定格口。新设置仅影响新作业。"); form = Form(rules);
            foreach (var rcs in m.Components.Where(ParameterOwnership.IsRcs))
                foreach (var p in rcs.Parameters.Where(p => ParameterOwnership.Category(rcs, p.Field.Key) == 1)) Parameter(form, rules, rcs, p);
        }
        else
        {
            Choice(form, "重量来源", m.Policy, "WeightSource", Enum.GetValues<WeightSource>(), "WeightSource", page);
            Choice(form, "重量单位", m.Policy, "Unit", Enum.GetValues<WeightUnit>(), "Unit", page);
            foreach (var f in new[] { ("重量精度（0～6）", "Precision"), ("配置重量", "ConfiguredWeight"), ("测量新鲜度（毫秒）", "MaximumAgeMs"), ("本件等待期限（毫秒）", "WaitTimeoutMs") }) Input(form, f.Item1, m.Policy, f.Item2, f.Item2, page);
            Choice(form, "重量有效性时机", m.Policy, "Validity", Enum.GetValues<WeightValidity>(), "Validity", page);
            var range = new CheckBox { Content = "限制包裹重量范围", MinHeight = 38 };
            range.SetBinding(CheckBox.IsCheckedProperty, new Binding("WeightRangeEnabled") { Source = m.Policy, Mode = BindingMode.TwoWay });
            Field(form, "重量范围", range, "范围单位与重量策略一致。越界、不稳定或超时均不生成默认有效重量。", "WeightRangeEnabled", page);
            var bounds = Form(page); bounds.SetBinding(IsEnabledProperty, new Binding("WeightRangeEnabled") { Source = m.Policy });
            Input(bounds, "包裹重量下限", m.Policy, "MinimumWeight", "MinimumWeight", page, "包含边界；至少为0，精度与重量策略一致。");
            Input(bounds, "包裹重量上限", m.Policy, "MaximumWeight", "MaximumWeight", page, "包含边界；不小于下限且大于0，最大100000。");
            if (m.Components.Any(x => x.Kind == "camera"))
            {
                Choice(form, "图像上传时机", m.Policy, "UploadStrategy", Enum.GetValues<UploadStrategy>(), "UploadStrategy", page);
                foreach (var f in new[] { ("单件图像最大字节", "ImageBytes"), ("图像缓存总字节", "TotalBytes"), ("图像待处理件数", "MaximumPending") }) Input(form, f.Item1, m.Policy, f.Item2, f.Item2, page);
            }
        }
        var budget = Page(1, "处理参数", "设置业务队列、外呼时限和未清偿作业上限。"); form = Form(budget);
        Input(form, "输入队列容量", m.Policy, "QueueCapacity", "QueueCapacity", budget, "1～4096；队列满时拒收新输入。");
        Input(form, "调用超时", m.Policy, "TimeoutMs", "TimeoutMs", budget, "100～30000");
        Input(form, "未清偿作业上限", m.Policy, "MaximumOperations", "MaximumOperations", budget, "1～100000");
    }
    void Preview(ObjectPage page, ParameterEditorModel m)
    {
        var body = new StackPanel();
        page.Body.Children.Add(new Expander { Header = "测试条码", Content = body, Margin = new(0, 12, 0, 0) });
        body.Children.Add(Text("每行一条，视为同一次观察；测试不保存或发出业务请求。"));
        var sample = new TextBox { AcceptsReturn = true, MinHeight = 96, MaxLength = 65536, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(sample, "条码筛选样例"); body.Children.Add(sample);
        var diagnostic = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent };
        var result = Text("尚未测试"); var test = new Button { Content = "测试候选筛选", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 4) };
        bool tested = false;
        void Invalidate() { if (tested) result.Text = "输入已改变，原测试结果已过期；请重新测试。"; }
        sample.TextChanged += (_, _) => Invalidate();
        PropertyChangedEventHandler handler = (_, e) => { if (e.PropertyName is "BarcodePattern" or "BarcodeExclusionPattern") Invalidate(); };
        m.Policy.PropertyChanged += handler; unsubscribe.Add(() => m.Policy.PropertyChanged -= handler);
        test.Click += async (_, _) =>
        {
            var pattern = m.Policy.BarcodePattern; var exclusion = m.Policy.BarcodeExclusionPattern; var raw = sample.Text; test.IsEnabled = false; result.Text = "正在执行有界本地测试…"; tested = true;
            try
            {
                var reply = await Task.Run(() => m.Preview(new(pattern, exclusion.Length == 0 ? null : exclusion), raw.Replace("\r", "").Split('\n')));
                result.Text = m.Policy.BarcodePattern != pattern || m.Policy.BarcodeExclusionPattern != exclusion || sample.Text != raw ? "输入已改变，本次测试结果已过期；请重新测试。" :
                    ChineseInterfaceText.Status(reply.Outcome) + "\n选中码：" + (reply.SelectedCode ?? "无") + " · 有效候选：" + reply.Matches;
                diagnostic.Text = Ipc.Platform.DiagnosticText.Protect(reply.Detail + string.Concat((reply.Candidates ?? []).Select(c => "\n第" + (c.Index + 1) + "项：" + c.Reason)));
            }
            catch (Exception ex) { result.Text = "测试未完成：" + ex.Message + "；输入保留。"; }
            finally { test.IsEnabled = true; }
        };
        body.Children.Add(test); body.Children.Add(result); body.Children.Add(new Expander { Header = "测试诊断详情", Content = diagnostic });
    }
}
