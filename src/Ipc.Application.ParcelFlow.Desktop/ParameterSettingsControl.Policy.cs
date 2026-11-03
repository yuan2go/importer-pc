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
        var station = Page(0, "供包台标识", "填写 RCS 分配给本供包台的编号，取格口和心跳都使用这些编号。");
        var hasRcs = m.Components.Any(ParameterOwnership.IsRcs);
        var form = Form(station);
        foreach (var rcs in m.Components.Where(ParameterOwnership.IsRcs))
        {
            foreach (var p in rcs.Parameters.Where(p => p.Field.Key is "zone-id" or "project-id")) Parameter(form, station, rcs, p);
            var left = rcs.Parameters.FirstOrDefault(p => p.Field.Key == "left-station-code"); var right = rcs.Parameters.FirstOrDefault(p => p.Field.Key == "right-station-code");
            if (left is not null && right is not null)
            {
                var pair = new CommonStationParameter(left, right, m.Policy); unsubscribe.Add(pair.Dispose);
                if (ParameterOwnership.StationConflict(rcs) || left.Value != m.Policy.StationCode)
                {
                    var conflict = Text("请确认统一供包台编号。" + pair.OriginalValues + "。在下方填写后，会同时用于新取格口请求和心跳；旧请求保留原编号。");
                    conflict.Foreground = Brush("#915300"); station.Body.Children.Add(conflict);
                }
                Input(Form(station), "供包台编号 *", pair, "Value", rcs.InstanceId + "/left-station-code", station, "RCS 分配给本供包台的编号，左右投递位共用。");
                locations[rcs.InstanceId + "/right-station-code"] = locations[rcs.InstanceId + "/left-station-code"];
                locations["StationCode"] = locations[rcs.InstanceId + "/left-station-code"];
                Technical(station, "供包台编号", rcs.InstanceId + "/left-station-code / right-station-code", "RCS 用它识别本供包台。保存并重启软件后，用于新的取格口请求和心跳。" + pair.OriginalValues);
            }
        }
        if (!hasRcs) Input(Form(station), "供包台编号 *", m.Policy, "StationCode", "StationCode", station, "用于向外部系统识别当前供包台。");
        Technical(station, "本地工作站标识", "StationId", m.StationId);
        var data = Page(0, "数据与记录", ""); form = Form(data);
        Input(form, "常驻记录", m.Policy, "ResidentHistory", "ResidentHistory", data, "1～10000");
        Input(form, "磁盘预留", m.Policy, "MinimumFreeMiB", "MinimumFreeMiB", data, "剩余空间低于此值时暂停接收新包裹。");
        Input(form, "日志保留天数", m.Policy, "DiagnosticLogDays", "DiagnosticLogDays", data, "默认 7 天。只清理诊断日志，不删除作业记录；留空则按容量自动轮换。");
        Input(form, "已结数据保留天数", m.Policy, "HistoryDays", "HistoryDays", data, "默认 7 天，可填 7–30 天。到期后清理已完成的作业明细，未处理完的作业始终保留；留空则不清理。");
        data.Body.Children.Add(Text("保留天数保存并重启后生效。只有明确启用自动清理并满足空闲、备份和责任保护条件时，才会后台清理。"));
        if (RuntimeContext is { } retentionContext)
        {
            var maintain = new Button { Content = "停止软件并清理到期记录…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
            maintain.Click += async (_, _) => { if (retentionContext.RequestRetentionAsync is { } request) await request(); else retentionContext.Message = "请先暂停并退出软件，再从开始菜单的“配置工具”清理到期记录。"; };
            data.Body.Children.Add(maintain);
        }
        Technical(data, "到期记录清理", "HistoryDays", "保存并重启后生效。手动清理需先暂停并退出软件；自动清理由已生效策略在空闲窗口执行。两者都先完整备份，未处理完的作业不会被删除。");
        data.Body.Children.Add(Text("存储位置", true));
        Readonly(data, "配置目录", m.Locations?.ConfigurationDirectory ?? "未取得配置目录。");
        Readonly(data, "数据目录", m.Locations?.DataDirectory ?? "未取得数据目录。");
        Technical(data, "日志保留", "DiagnosticLogDays", "留空则按容量自动轮换，可填 1–3650 天。只影响诊断日志。" );
        Technical(data, "记录与容量", "ResidentHistory / MinimumFreeMiB", "常驻记录 1–10000 条，磁盘预留空间 1–1048576 MiB。软件运行时目录不能修改，迁移数据请使用维护工具。");
        var backup = Page(0, "自动备份", "只在暂停供包、处于管理模式且没有进行中的请求时执行。");
        if (RuntimeContext is { } backupContext)
        {
            var actual = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            actual.SetBinding(TextBox.TextProperty, new Binding(nameof(ParcelViewModel.BackupStatusText)) { Source = backupContext, Mode = BindingMode.OneWay });
            backup.Body.Children.Add(actual);
        }
        var enabled = new CheckBox { Content = "启用自动备份", MinHeight = 38 };
        enabled.SetBinding(CheckBox.IsCheckedProperty, new Binding("BackupEnabled") { Source = m.Policy, Mode = BindingMode.TwoWay });
        Field(Form(backup), "备份计划", enabled, null, "BackupEnabled", backup);
        form = Form(backup); form.SetBinding(IsEnabledProperty, new Binding("BackupEnabled") { Source = m.Policy });
        Input(form, "备份目录", m.Policy, "BackupDirectory", "BackupDirectory", backup, "填写完整的文件夹路径。");
        form = Form(backup); form.SetBinding(IsEnabledProperty, new Binding("BackupEnabled") { Source = m.Policy });
        Input(form, "备份间隔", m.Policy, "BackupIntervalMinutes", "BackupIntervalMinutes", backup, "15～10080");
        Input(form, "保留份数", m.Policy, "BackupMaximumSets", "BackupMaximumSets", backup, "可填 1–1000 份，旧备份不会自动删除。");
        if (BackupRequested is not null)
        {
            backup.Body.Children.Add(Text("手动备份", true));
            backup.Body.Children.Add(Text("立即创建一份完整备份，保存到你选择的位置。"));
            var open = new Button { Content = "立即备份…", HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += (_, _) => BackupRequested?.Invoke(this, EventArgs.Empty); backup.Body.Children.Add(open);
        }
        Technical(backup, "自动备份", "BackupEnabled / BackupDirectory / BackupIntervalMinutes / BackupMaximumSets", "完整备份由数据库文件和同名恢复文件夹组成，恢复时两者都需要。关闭自动备份不会删除已有备份。");
        var display = Page(0, "显示偏好", m.ApplicationId == "sportmaster" ? "设置本机窗口的打开方式和布局。" : "仅影响本机显示。");
        if (m.ApplicationId == "sportmaster")
        {
            var pace = new CheckBox { Content = "作业页显示本班取得格口次数与近十分钟速度", MinHeight = 44 };
            bool readable = true;
            try { pace.IsChecked = Ipc.Platform.PresentationPreferences.Read("sportmaster", "pace") ?? true; }
            catch (Exception error) { readable = false; display.Body.Children.Add(Text(ChineseInterfaceText.ParameterMessage(error.Message, "效率偏好未能读取，原文件保留；请检查本机设置文件与访问权限。"))); }
            pace.IsEnabled = readable; display.Body.Children.Add(pace);
            var savePace = new Button { Content = "应用效率显示", MinHeight = 44, IsEnabled = readable, HorizontalAlignment = HorizontalAlignment.Left };
            savePace.Click += async (_, _) => { try { if (RuntimeContext is { } context) await context.RequireManagementModeAsync(); Ipc.Platform.PresentationPreferences.Save("sportmaster", "pace", pace.IsChecked == true); } catch (Exception error) { if (RuntimeContext is { } context) context.Message = ChineseInterfaceText.ParameterMessage(error.Message, "效率偏好未保存；请检查本机设置文件与访问权限后重试。"); } };
            display.Body.Children.Add(savePace);
        }
        if (m.ApplicationId != "sportmaster" && RuntimeContext is { } runtime)
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
        if (m.ApplicationId != "sportmaster") display.Body.Children.Add(Text("简体中文 · 浅色"));
        display.Body.Children.Add(Text("窗口布局", true));
        var opening = new ComboBox { ItemsSource = new[] { new WindowModeChoice(Ipc.Platform.PresentationPreferences.WindowOpeningMode.Window, "窗口"), new WindowModeChoice(Ipc.Platform.PresentationPreferences.WindowOpeningMode.FullScreen, "最大化（全屏）") }, DisplayMemberPath = "Label", SelectedValuePath = "Mode", MinHeight = 40 };
        var modeStatus = Text("");
        bool modeReadable = true;
        try { opening.SelectedValue = Ipc.Platform.PresentationPreferences.ReadWindowMode(m.ApplicationId); }
        catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { modeReadable = false; modeStatus.Text = ChineseInterfaceText.ParameterMessage(error.Message, "窗口设置未能读取，原文件保留；请检查本机设置文件与访问权限。"); }
        Field(Form(display), "默认打开方式", opening, "最大化（全屏）时隐藏标题栏并铺满屏幕；按 F11 切换，按 Esc 返回窗口。", "WindowOpeningMode", display);
        var saveMode = new Button { Content = "保存打开方式", HorizontalAlignment = HorizontalAlignment.Left, MinHeight = 44, IsEnabled = modeReadable };
        saveMode.Click += (_, _) =>
        {
            if (RuntimeContext is { IsManagementMode: false }) { modeStatus.Text = "请先进入管理模式。"; return; }
            try
            {
                if (opening.SelectedValue is not Ipc.Platform.PresentationPreferences.WindowOpeningMode mode) throw new InvalidOperationException("请选择默认打开方式。");
                Ipc.Platform.PresentationPreferences.SaveWindowMode(m.ApplicationId, mode); modeStatus.Text = "已保存，下次打开作业窗口生效。";
            }
            catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
            { modeStatus.Text = ChineseInterfaceText.ParameterMessage(error.Message, "打开方式未保存；请检查本机设置文件与访问权限后重试。"); }
        };
        display.Body.Children.Add(saveMode); display.Body.Children.Add(modeStatus);
        if (m.ApplicationId == "sportmaster") AudioPreferences(display);
        display.Body.Children.Add(Text("小窗口会自动使用紧凑布局。"));
        if (DisplayPreferencesRequested is not null)
        {
            var change = new Button { Content = "切换标准 / 紧凑布局", HorizontalAlignment = HorizontalAlignment.Left };
            change.Click += (_, _) => DisplayPreferencesRequested?.Invoke(this, EventArgs.Empty); display.Body.Children.Add(change);
        }
        else display.Body.Children.Add(Text("完成首次配置后，可在作业窗口调整本机布局。"));
        Technical(display, "本机显示", "PresentationPreferences", "窗口打开方式和布局按当前 Windows 用户保存，不影响业务设置和设备。");
    }
    sealed record WindowModeChoice(Ipc.Platform.PresentationPreferences.WindowOpeningMode Mode, string Label);
    void SessionLifetime(Panel form, ObjectPage page, ParameterEditorModel m)
    {
        const string path = "SessionLifetimeMs";
        var input = Input(form, "会话时长", m.Policy, path, path, page,
            "仅用于操作员登录。");
        input.ToolTip = "100～86400000 毫秒";
        AutomationProperties.SetHelpText(input, "操作员登录后多久需要重新登录，100–86400000 毫秒，与管理模式无关。");
        Technical(page, "操作员会话", path, "100–86400000 毫秒，只用于操作员登录。");
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
        var login = Page(1, "作业登录", "操作员登录用于记录作业人员，与管理密码无关。");
        if (m.OperatorLoginRole is { } role)
        {
            var caption = new FrameworkElementFactory(typeof(TextBlock));
            caption.SetBinding(TextBlock.TextProperty, new Binding { Converter = new OperatorLoginChoiceText(m) });
            caption.SetBinding(ToolTipProperty, new Binding("Label"));
            var selection = new ComboBox { ItemsSource = role.Choices, ItemTemplate = new DataTemplate { VisualTree = caption }, MinHeight = 38 };
            selection.SetBinding(ComboBox.SelectedItemProperty, new Binding("Selection") { Source = role, Mode = BindingMode.TwoWay });
            Field(Form(login), "操作员登录服务", selection, "不选择登录服务时，无需登录即可作业。", "OperatorLogin", login);
        }
        else if (m.Components.SingleOrDefault(c => c.Role == "operator-login") is { } optionalLogin)
        {
            var enable = new CheckBox { Content = "要求操作员登录", MinHeight = 38 };
            enable.SetBinding(CheckBox.IsCheckedProperty, new Binding("Bound") { Source = optionalLogin, Mode = BindingMode.TwoWay });
            Field(Form(login), "作业登录要求", enable, "启用前需要先安装并配置操作员登录服务。", "OperatorLogin", login);
        }
        SessionLifetime(Form(login), login, m);
        var page = Page(1, m.ApplicationId == "sportmaster" ? "条码筛选" : "业务策略", m.ApplicationId == "sportmaster" ? "设置哪些条码有效，以及一次读到多个条码时如何处理。左右相机共用。" : "编辑当前业务策略。"); var form = Form(page);
        if (m.ApplicationId == "sportmaster")
        {
            Input(form, "匹配规则（可选）", m.Policy, "BarcodePattern", "BarcodePattern", page, "条码需完整匹配此规则（正则表达式）；留空则不筛选。", true);
            Input(form, "排除规则（可选）", m.Policy, "BarcodeExclusionPattern", "BarcodeExclusionPattern", page, "完整匹配此规则的条码会被排除；留空则不排除。", true);
            var reject = new CheckBox { Content = "拒绝多个有效条码", MinHeight = 38 };
            reject.SetBinding(CheckBox.IsCheckedProperty, new Binding("RejectMultipleCodes") { Source = m.Policy, Mode = BindingMode.TwoWay });
            Field(form, "多码处理", reject, "默认开启：读到多个有效条码时不发送，需要重新扫描。关闭后按读取顺序拼接成一个条码发给 RCS。", "RejectMultipleCodes", page);
            Input(form, "多条码分隔符", m.Policy, "MultiCodeSeparator", "MultiCodeSeparator", page, "仅在允许多码时使用，1–16 个字符。拼接后最长 512 字符，超出则不发送。");
            Technical(page, "筛选执行边界", "BarcodeFilter.Pattern", "规则按整串匹配，单次判断限时 25 毫秒；不会自动挑选第一个条码。");
            Preview(page, m);
            var rules = Page(1, "格口请求", "软件把识别到的条码发给 RCS 获取格口。这里只设置是否指定固定格口，最终格口以 RCS 返回为准。"); form = Form(rules);
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
        var budget = Page(1, "接收与等待限制", "一般保持默认值。出现积压或超时，应先检查相机和 RCS 连接。"); form = Form(budget);
        Input(form, "扫码消息缓冲上限", m.Policy, "QueueCapacity", "QueueCapacity", budget, "等待处理的扫码最多可排队多少条，1–4096。排满后新扫码会被拒收。");
        Input(form, "外部服务等待时间", m.Policy, "TimeoutMs", "TimeoutMs", budget, "调用外部系统时最多等待多久，100–30000 毫秒。超时后结果未确认，系统不会自动重发。");
        Input(form, "待处理作业上限", m.Policy, "MaximumOperations", "MaximumOperations", budget, "未处理完的作业最多保留多少个，1–100000。达到上限后暂停接收新包裹。");
    }
    void Preview(ObjectPage page, ParameterEditorModel m)
    {
        var body = new StackPanel();
        page.Body.Children.Add(new Expander { Header = "测试条码", Content = body, Margin = new(0, 12, 0, 0) });
        body.Children.Add(Text("每行一个条码，模拟一次读到这些条码。测试不会发给 RCS。"));
        var sample = new TextBox { AcceptsReturn = true, MinHeight = 96, MaxLength = 65536, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(sample, "条码筛选样例"); body.Children.Add(sample);
        var diagnostic = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent };
        var result = Text("尚未测试"); var test = new Button { Content = "测试规则", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 4) };
        bool tested = false;
        void Invalidate() { if (tested) result.Text = "输入已改变，原测试结果已过期；请重新测试。"; }
        sample.TextChanged += (_, _) => Invalidate();
        PropertyChangedEventHandler handler = (_, e) => { if (e.PropertyName is "BarcodePattern" or "BarcodeExclusionPattern" or "RejectMultipleCodes" or "MultiCodeSeparator") Invalidate(); };
        m.Policy.PropertyChanged += handler; unsubscribe.Add(() => m.Policy.PropertyChanged -= handler);
        test.Click += async (_, _) =>
        {
            var policy = m.Policy.BuildBarcodePolicy(); var raw = sample.Text; test.IsEnabled = false; result.Text = "正在测试…"; tested = true;
            try
            {
                var reply = await Task.Run(() => m.Preview(policy, raw.Replace("\r", "").Split('\n')));
                result.Text = m.Policy.BuildBarcodePolicy() != policy || sample.Text != raw ? "输入已改变，本次测试结果已过期；请重新测试。" :
                    ChineseInterfaceText.Status(reply.Outcome) + "\n选中码：" + (reply.SelectedCode ?? "无") + " · 有效条码：" + reply.Matches;
                diagnostic.Text = ChineseInterfaceText.ParameterMessage(reply.Detail) + string.Concat((reply.Candidates ?? []).Select(c => "\n第" + (c.Index + 1) + "项：" + ChineseInterfaceText.ParameterMessage(c.Reason)));
            }
            catch (Exception ex) { result.Text = ChineseInterfaceText.ParameterMessage(ex.Message, "测试未完成，输入保留；请检查条码筛选规则后重试。"); }
            finally { test.IsEnabled = true; }
        };
        body.Children.Add(test); body.Children.Add(result); body.Children.Add(new Expander { Header = "测试详情", Content = diagnostic });
    }
}
