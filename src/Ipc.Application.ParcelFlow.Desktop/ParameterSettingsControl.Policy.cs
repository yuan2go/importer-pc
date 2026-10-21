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
                var pair = new CommonStationParameter(left, right);
                if (ParameterOwnership.StationConflict(rcs))
                {
                    var conflict = Text("需要核对旧设置：左右取格口站码不同。" + pair.OriginalValues + "。请填写本站共同使用的编码。");
                    conflict.Foreground = Brush("#915300"); station.Body.Children.Add(conflict);
                }
                Input(Form(station), "供包台编码 *", pair, "Value", rcs.InstanceId + "/left-station-code", station, "左右相机的取格口请求共同使用此编码。");
                locations[rcs.InstanceId + "/right-station-code"] = locations[rcs.InstanceId + "/left-station-code"];
                Technical(station, "供包台编码", rcs.InstanceId + "/left-station-code / right-station-code", pair.OriginalValues + "。明确填写共同值才统一两个候选；原请求保持原值。");
            }
        }
        form = Form(station, hasRcs ? "心跳身份" : null);
        Input(form, "供包台编码 *", m.Policy, "StationCode", "StationCode", station, "用于周期心跳；与上方取格口编码分别配置。");
        Technical(station, "本地工作站标识", "StationId", m.StationId);
        Technical(station, "心跳供包台编码", "StationCode", "不自动替换取格口供包台编码；长度1～256，不含控制字符。");
        var data = Page(0, "数据与记录", "设置当前窗口常驻的记录数量，以及关键写入需要保留的磁盘空间。"); form = Form(data);
        Input(form, "常驻历史条数", m.Policy, "ResidentHistory", "ResidentHistory", data, "1～10000 条");
        Input(form, "磁盘保留空间（MiB，二进制兆字节）", m.Policy, "MinimumFreeMiB", "MinimumFreeMiB", data, "1～1048576 MiB；空间不足时阻断新准入。");
        Input(form, "诊断日志保留天数", m.Policy, "DiagnosticLogDays", "DiagnosticLogDays", data, "选填1～3650天；留空沿用容量轮转。仅清理诊断日志，最多约20 MB；常驻历史、业务审计和图片证据独立保留。");
        data.Body.Children.Add(Text("存储位置", true));
        Readonly(data, "配置目录", m.Locations?.ConfigurationDirectory ?? "此宿主未提供目录；可从独立维护核对原部署。");
        Readonly(data, "数据目录", m.Locations?.DataDirectory ?? "未取得数据目录。");
        Technical(data, "记录与容量", "ResidentHistory / MinimumFreeMiB", "运行内目录只读。同库归档不会释放磁盘；迁移在独立维护中处理原目录、完整备份和原责任。");
        var backup = Page(0, "自动备份", "完整备份包含数据库和同名恢复目录。计划只在暂停、维护授权有效且无在途外呼时执行。");
        var enabled = new CheckBox { Content = "启用自动备份", MinHeight = 38 };
        enabled.SetBinding(CheckBox.IsCheckedProperty, new Binding("BackupEnabled") { Source = m.Policy, Mode = BindingMode.TwoWay });
        Field(Form(backup), "备份计划", enabled, null, "BackupEnabled", backup);
        form = Form(backup); form.SetBinding(IsEnabledProperty, new Binding("BackupEnabled") { Source = m.Policy });
        Input(form, "备份目录", m.Policy, "BackupDirectory", "BackupDirectory", backup, "填写绝对路径。");
        form = Form(backup); form.SetBinding(IsEnabledProperty, new Binding("BackupEnabled") { Source = m.Policy });
        Input(form, "备份间隔（分钟）", m.Policy, "BackupIntervalMinutes", "BackupIntervalMinutes", backup, "15～10080 分钟");
        Input(form, "备份份数上限", m.Policy, "BackupMaximumSets", "BackupMaximumSets", backup, "1～1000 份；到达上限后不自动删除原备份。");
        if (BackupRequested is not null)
        {
            backup.Body.Children.Add(Text("手动备份", true));
            backup.Body.Children.Add(Text("创建一份新的完整恢复集，保存到明确选择的路径。"));
            var open = new Button { Content = "立即完整备份…", HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += (_, _) => BackupRequested?.Invoke(this, EventArgs.Empty); backup.Body.Children.Add(open);
        }
        Technical(backup, "自动备份", "BackupEnabled / BackupDirectory / BackupIntervalMinutes / BackupMaximumSets", "关闭计划时不提交备份配置，本次输入可留在草稿中；既有完整恢复集保留。参数模板不包含数据库和恢复目录。");
        var display = Page(0, "显示偏好", "本机显示偏好按当前系统用户保存，立即作用于当前窗口。");
        if (RuntimeContext is { } runtime)
        {
            form = Form(display);
            var name = Input(form, "站点显示名称", runtime, "DisplayNameDraft", "DisplayNameDraft", display, "选填，最多64字；工作台同时保留本地工作站标识。");
            var target = Input(form, "目标效率（件/小时）", runtime, "TargetEfficiencyDraft", "TargetEfficiencyDraft", display, "选填1～100000；仅显示目标，不改变输送速度、节拍或实际产量。");
            var apply = new Button { Content = "应用本机显示", HorizontalAlignment = HorizontalAlignment.Left, MinHeight = 44 };
            apply.Click += (_, _) => { if (!runtime.ApplyStationDisplay()) { if (runtime.DisplayPreferenceStatus.Contains("目标效率", StringComparison.Ordinal)) target.Focus(); else name.Focus(); } };
            display.Body.Children.Add(apply);
            var feedback = Text(""); feedback.SetBinding(TextBlock.TextProperty, new Binding("DisplayPreferenceStatus") { Source = runtime });
            AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite); display.Body.Children.Add(feedback);
        }
        display.Body.Children.Add(Text("当前界面：简体中文 · 浅色。仅提供已实现的显示选项。"));
        display.Body.Children.Add(Text("窗口布局", true));
        display.Body.Children.Add(Text("标准布局保留更宽的留白；紧凑布局适用于较小屏幕。窗口不足时会自动使用紧凑布局。"));
        if (DisplayPreferencesRequested is not null)
        {
            var change = new Button { Content = "切换标准 / 紧凑布局", HorizontalAlignment = HorizontalAlignment.Left };
            change.Click += (_, _) => DisplayPreferencesRequested?.Invoke(this, EventArgs.Empty); display.Body.Children.Add(change);
        }
        else display.Body.Children.Add(Text("完成首次配置后，可在作业窗口调整本机布局。"));
        display.Body.Children.Add(Text("显示偏好不进入业务配置修订；不会启动设备或恢复新入场。"));
    }
    void SessionLifetime(Panel form, ObjectPage page, ParameterEditorModel m)
    {
        const string path = "SessionLifetimeMs";
        var input = Input(form, "操作员会话有效时长", m.Policy, path, path, page,
            "100～86400000 毫秒；用于已绑定的操作员登录。维护认证不使用此时长；解绑保留原策略，已有错误仍须纠正。");
        var field = (FrameworkElement)input.Parent;
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
    void BuildBusiness(ParameterEditorModel m)
    {
        var login = Page(1, "作业登录", "按当前业务选择是否要求操作员登录。维护授权独立，关闭作业登录不会解除维护保护。");
        if (m.OperatorLoginRole is { } role)
        {
            var selection = new ComboBox { ItemsSource = role.Choices, DisplayMemberPath = "Label", MinHeight = 38 };
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
            Input(form, "条码筛选规则（选填）", m.Policy, "BarcodePattern", "BarcodePattern", page, "选填；留空不筛选。填写后使用正则表达式匹配完整条码；多个候选仍须选出唯一条码。", true);
            Input(form, "条码排除规则（选填）", m.Policy, "BarcodeExclusionPattern", "BarcodeExclusionPattern", page, "选填；整串命中此正则的候选将被拒绝。留空不排除。", true);
            Technical(page, "筛选执行边界", "BarcodeFilter.Pattern", "整串匹配；单次25ms、总计100ms。留空不筛选；仍须唯一有效码；不默认选首码，不去重制造唯一候选。");
            Preview(page, m);
            var rules = Page(1, "取格口请求", "设置每次请求数量，以及是否向上游请求指定格口。新设置仅影响新作业。"); form = Form(rules);
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
        var budget = Page(1, "处理参数", "设置业务队列、外呼时限和未清偿作业容量。"); form = Form(budget);
        Input(form, "输入队列容量", m.Policy, "QueueCapacity", "QueueCapacity", budget, "1～4096；队列满时拒收新输入。");
        Input(form, "调用时限（毫秒）", m.Policy, "TimeoutMs", "TimeoutMs", budget, "100～30000");
        Input(form, "未清偿作业容量", m.Policy, "MaximumOperations", "MaximumOperations", budget, "1～100000");
    }
    void Preview(ObjectPage page, ParameterEditorModel m)
    {
        page.Body.Children.Add(Text("本地候选样例", true)); page.Body.Children.Add(Text("每行一个，视为同一次观察；测试不会触发业务请求或保存。"));
        var sample = new TextBox { AcceptsReturn = true, MinHeight = 96, MaxHeight = 180, MaxLength = 65536, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetName(sample, "条码筛选样例"); page.Body.Children.Add(sample);
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
                    reply.Detail + "\n选中码：" + (reply.SelectedCode ?? "无") + " · 有效候选：" + reply.Matches +
                    string.Concat((reply.Candidates ?? []).Select(c => "\n第" + (c.Index + 1) + "项：" + c.Reason));
            }
            catch (Exception ex) { result.Text = "测试未完成：" + ex.Message + "；输入保留。"; }
            finally { test.IsEnabled = true; }
        };
        page.Body.Children.Add(test); page.Body.Children.Add(result);
    }
}
