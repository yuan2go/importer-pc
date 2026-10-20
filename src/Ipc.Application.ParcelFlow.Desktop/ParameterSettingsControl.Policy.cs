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
        var station = Page(0, "站点标识", "取格口与心跳是不同用途。以下字段仍属于同一完整候选，旧请求保持原值。");
        Readonly(station, "本地工作站标识 · StationId", m.StationId);
        var form = Form(station);
        Input(form, "心跳工位编码 · StationCode", m.Policy, "StationCode", "StationCode", station, "用于周期心跳；不自动替换取格口站码。");
        foreach (var rcs in m.Components.Where(ParameterOwnership.IsRcs))
        {
            foreach (var p in rcs.Parameters.Where(p => p.Field.Key is "zone-id" or "project-id")) Parameter(form, station, rcs, p);
            var left = rcs.Parameters.FirstOrDefault(p => p.Field.Key == "left-station-code"); var right = rcs.Parameters.FirstOrDefault(p => p.Field.Key == "right-station-code");
            if (left is not null && right is not null)
            {
                var pair = new CommonStationParameter(left, right);
                Input(form, "共同取格口站码 · stationCode", pair, "Value", rcs.InstanceId + "/left-station-code", station,
                    pair.OriginalValues + "。填写此项即明确将左右候选统一为该值；尚未保存。");
                locations[rcs.InstanceId + "/right-station-code"] = locations[rcs.InstanceId + "/left-station-code"];
            }
        }
        var data = Page(0, "数据与记录", "保留空间不足时阻断新准入；同库归档不会释放磁盘。"); form = Form(data);
        Readonly(data, "配置目录", m.Locations?.ConfigurationDirectory ?? "此宿主未提供目录投影；请从独立维护核对原部署。");
        Readonly(data, "数据目录", m.Locations?.DataDirectory ?? "未取得目录，不推测当前工作目录。");
        data.Body.Children.Add(Text("运行内只读。迁移须从独立维护处理原目录、完整备份及资源责任。"));
        Input(form, "常驻历史条数", m.Policy, "ResidentHistory", "ResidentHistory", data, "1～10000 条");
        Input(form, "关键写入保留空间", m.Policy, "MinimumFreeMiB", "MinimumFreeMiB", data, "1～1048576 MiB");
        if (m.Components.Any(c => c.Role == "operator-login" && c.Bound))
            Input(form, "操作员会话有效时长", m.Policy, "SessionLifetimeMs", "SessionLifetimeMs", data, "100～86400000 ms；只用于已绑定的操作员登录。");
        var backup = Page(0, "计划备份", "只在已暂停、维护授权有效且无外呼时执行；完整恢复集和原责任保持。");
        var enabled = new CheckBox { Content = "启用受控计划备份", MinHeight = 44 };
        enabled.SetBinding(CheckBox.IsCheckedProperty, new Binding("BackupEnabled") { Source = m.Policy, Mode = BindingMode.TwoWay }); backup.Body.Children.Add(enabled);
        form = Form(backup); form.SetBinding(IsEnabledProperty, new Binding("BackupEnabled") { Source = m.Policy });
        Input(form, "备份绝对目录", m.Policy, "BackupDirectory", "BackupDirectory", backup, "关闭时不保存备份配置；输入可在本次草稿中保留。");
        Input(form, "周期（分钟）", m.Policy, "BackupIntervalMinutes", "BackupIntervalMinutes", backup, "15～10080");
        Input(form, "容量上限（份）", m.Policy, "BackupMaximumSets", "BackupMaximumSets", backup, "1～1000；不自动删除原备份");
        var maintenance = Page(0, "维护与迁移", "模板只改变获准的通用策略和精确提供者草稿；站码、设备地址、目标及凭据不由模板填入。完整备份恢复与配置导入分别处理。");
        var open = new Button { Content = "打开维护与迁移", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += (_, _) => MaintenanceRequested?.Invoke(this, EventArgs.Empty); maintenance.Body.Children.Add(open);
        maintenance.Body.Children.Add(Text("维护入口包含显示偏好、加密草稿、模板差异、备份、已安装包与高级配置。首配中请使用本窗的维护操作。"));
    }
    void BuildBusiness(ParameterEditorModel m)
    {
        var page = Page(1, m.ApplicationId == "sportmaster" ? "条码筛选" : "业务策略", "当前业务 " + m.ApplicationId + "；此处不切换业务模块。"); var form = Form(page);
        if (m.ApplicationId == "sportmaster")
        {
            Input(form, "左右共用条码筛选正则", m.Policy, "BarcodePattern", "BarcodePattern", page, "整串匹配；单次25ms、总计100ms。旧null规则保持严格单码兼容；清空已有规则不会生成默认规则。", true);
            Preview(page, m);
            var rules = Page(1, "请求规则", "请求数量是正整数；响应数量保留上游字符串，两者不互相代替。新配置只影响新作业。"); form = Form(rules);
            foreach (var rcs in m.Components.Where(ParameterOwnership.IsRcs))
                foreach (var p in rcs.Parameters.Where(p => ParameterOwnership.Category(rcs, p.Field.Key) == 1)) Parameter(form, rules, rcs, p);
        }
        else
        {
            Choice(form, "重量来源", m.Policy, "WeightSource", Enum.GetValues<WeightSource>(), "WeightSource", page);
            Choice(form, "重量单位", m.Policy, "Unit", Enum.GetValues<WeightUnit>(), "Unit", page);
            foreach (var f in new[] { ("重量精度（0～6）", "Precision"), ("配置重量", "ConfiguredWeight"), ("测量新鲜度（ms）", "MaximumAgeMs"), ("本件等待期限（ms）", "WaitTimeoutMs") }) Input(form, f.Item1, m.Policy, f.Item2, f.Item2, page);
            Choice(form, "重量有效性时机", m.Policy, "Validity", Enum.GetValues<WeightValidity>(), "Validity", page);
            if (m.Components.Any(x => x.Kind == "camera"))
            {
                Choice(form, "图像上传时机", m.Policy, "UploadStrategy", Enum.GetValues<UploadStrategy>(), "UploadStrategy", page);
                foreach (var f in new[] { ("单件图像最大字节", "ImageBytes"), ("图像缓存总字节", "TotalBytes"), ("图像待处理件数", "MaximumPending") }) Input(form, f.Item1, m.Policy, f.Item2, f.Item2, page);
            }
        }
        var budget = Page(1, "处理预算", "影响业务接纳与外呼预算；不改变原请求或认定物理完成。"); form = Form(budget);
        Input(form, "输入队列容量", m.Policy, "QueueCapacity", "QueueCapacity", budget, "1～4096；满时围栏拒收");
        Input(form, "调用预算（ms）", m.Policy, "TimeoutMs", "TimeoutMs", budget, "100～30000");
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
        PropertyChangedEventHandler handler = (_, e) => { if (e.PropertyName == "BarcodePattern") Invalidate(); };
        m.Policy.PropertyChanged += handler; unsubscribe.Add(() => m.Policy.PropertyChanged -= handler);
        test.Click += async (_, _) =>
        {
            var pattern = m.Policy.BarcodePattern; var raw = sample.Text; test.IsEnabled = false; result.Text = "正在执行有界本地测试…"; tested = true;
            try
            {
                var reply = await Task.Run(() => m.Preview(new(pattern), raw.Replace("\r", "").Split('\n')));
                result.Text = m.Policy.BarcodePattern != pattern || sample.Text != raw ? "输入已改变，本次测试结果已过期；请重新测试。" :
                    reply.Detail + "\n选中码：" + (reply.SelectedCode ?? "无") + " · 有效候选：" + reply.Matches + " · " + reply.Outcome;
            }
            catch (Exception ex) { result.Text = "测试未完成：" + ex.Message + "；输入保留。"; }
            finally { test.IsEnabled = true; }
        };
        page.Body.Children.Add(test); page.Body.Children.Add(result);
    }
}
