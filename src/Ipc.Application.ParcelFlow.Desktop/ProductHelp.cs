namespace Ipc.Application.ParcelFlow.Desktop;

public static class ProductHelp
{
    public static System.Windows.FrameworkElement CreateView()
    {
        var panel = new System.Windows.Controls.Grid { Margin = new(16) };
        panel.RowDefinitions.Add(new() { Height = System.Windows.GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = System.Windows.GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = new(1, System.Windows.GridUnitType.Star) });
        var search = new System.Windows.Controls.TextBox { MinHeight = 44, FontSize = 16, ToolTip = "搜索任务或故障，例如首配、Unknown、备份、卸载" };
        System.Windows.Automation.AutomationProperties.SetName(search, "搜索离线帮助");
        var searchArea = new System.Windows.Controls.StackPanel();
        searchArea.Children.Add(new System.Windows.Controls.TextBlock { Text = "搜索任务或故障 · 首配 / Unknown / 备份 / 卸载", FontSize = 16, Margin = new(4, 0, 4, 8) });
        searchArea.Children.Add(search); panel.Children.Add(searchArea);
        var topics = QuickStart.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
        var list = new System.Windows.Controls.ListBox { MinHeight = 88, MaxHeight = 170, FontSize = 16, Margin = new(0, 8, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetName(list, "帮助任务");
        System.Windows.Controls.Grid.SetRow(list, 1); panel.Children.Add(list);
        var text = new System.Windows.Controls.TextBox { IsReadOnly = true, TextWrapping = System.Windows.TextWrapping.Wrap, FontSize = 16, VerticalContentAlignment = System.Windows.VerticalAlignment.Top, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
        System.Windows.Automation.AutomationProperties.SetName(text, "帮助正文");
        System.Windows.Controls.Grid.SetRow(text, 2); panel.Children.Add(text);
        void Filter()
        {
            var found = topics.Where(t => t.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            list.Items.Clear();
            foreach (var topic in found) list.Items.Add(new System.Windows.Controls.ListBoxItem { Content = topic.Split('\n')[0], Tag = topic, MinHeight = 44 });
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            else text.Text = "没有匹配的帮助。请尝试‘首配’、‘备份’或‘Unknown’；业务状态未改变。";
        }
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is System.Windows.Controls.ListBoxItem item) text.Text = (string)item.Tag; };
        search.TextChanged += (_, _) => Filter(); Filter(); return panel;
    }
    public const string QuickStart = """
菜鸟供包台 · 离线快速开始

安装与首配
从可信渠道取得完整同版介质。运行Setup，选择程序/数据位置和已有Windows运行账户；检查实际安装摘要后授权。日常运行使用指定账户的非提权进程。安装完成不等于已具备生产资格。
新站先核对既有部署的站标识和Simulation/Production模式。Sportmaster首次启动独立经过“业务模块 → 系统 → 驱动 → 组件 → 检查与完成”，日常页面不重复首配。稳定身份由部署确定，界面不按示例生成设备。尚未连接设备或缺部署资格时可保存未完成草稿；它不会成为有效配置。分层检查后再提交待生效修订。

参数配置与未完成设置
系统管理本地身份、取格口区域/项目/共同站码、独立心跳站码、数据与备份；业务模块管理条码筛选、请求规则和处理预算；驱动按左右实例维护真实参数及各自业务映射；组件管理唯一RCS连接、协议、编号范围和心跳间隔。取格口编码与心跳编码用途不同，不自动互换。旧左右站码冲突须明确填写共同值。
用参数搜索按名称、英文键或对象定位；Enter跳到第一项，Esc清除，Ctrl+F回到搜索框。切分类和切页保留输入；安装版本与稳定设备身份可展开查看。
先查看跨对象修改并检查完整候选，再暂停，打开“保存准备…”认证维护、填写原因、保存修改。保存后仅待重启，设备重连不应用Pending。保存结果不明，在保存准备中核对上次保存结果，不能反复提交。关窗或重读可继续编辑、加密保留或明确放弃。加密草稿不等于待生效配置；失败仍保留本窗输入。
同版模板导入先预览差异，只改变允许的通用策略和精确提供者选择；站点、设备地址、目标、秘密、编号和备份路径留在本机。若提供者不能承载本机字段，导入拒绝，需显式迁移。显示布局在“更多”中设置，不改业务配置。

配置启动失败
从开始菜单“启动修复”或Maintenance进入独立配置。读取原目录；不要另建空根、改站号或删除配置/租约。若原配置未确认且无法启动，检查候选后选择“受控修复未确认配置”。此操作保留原账和失败尝试；原责任未清、资格不足或原资源未释放时仍会拒绝。
保存Pending只表示待生效。重试启动会重新检查并启动资源，全部成功后才确认。旧账成功启动但仍待维护确认时，在维护页读取原证据、认证并显式确认，随后按守卫恢复新入场。

日常作业与异常
Sportmaster默认进入双位作业页，兼容界面使用显式 --ui classic。“更多”可切布局、打开辅助窗口；日常导航为作业、作业记录、参数配置、诊断与恢复，只切页，不重建运行实例。其他业务保持自身启动方式。
工作台、记录、设置和辅助窗共享一个运行实例。左右来源按已确认设备绑定路由，设备读码不依赖键盘焦点；失败观察不会借用上一件的格口。软件暂停只暂停新业务入场，不是安全急停。
设备失联/健康过期：检查供电、连接和绑定，使用受控重连，取得新鲜健康后检查开工条件。对端拒绝：保留原结果，按原因处理当前实体。结果Unknown：保留原请求、目标、合同和报文，只按真实合同允许的查询/核对处理；不要重发、换地址重投或重复放货。告警已读不表示故障已恢复。
存储或容量不足：停止受影响准入，保留原库/原件；检查目标卷、完整备份和迁移计划。归档减少常驻工作集，不等于释放磁盘。

记录与导出
按完整条码、投递位、状态、原请求和明确时区范围查询。时间含开始、不含结束，例如2026-10-19T08:00:00+08:00。查询固定序号上界；导出使用同一筛选，不包含查询之后的新入场。每个同码实体仍独立保留。
CSV文本列带显式文本标记，条码原UTF-8另存base64列；请按文本导入电子表格。导出有行数/字节/时间预算，可取消；失败/取消不发布部分CSV，保留结果回执。取得格口或请求接受均不等于物理完成。

备份、恢复与迁移
使用“完整备份”，不要复制在线db冒充完整恢复集。计划备份仅在软件暂停、维护授权有效且无在途外呼时执行；条件不足显示待执行，不自动暂停业务，到容量上限不删除原备份。
独立维护可列出并校验完整备份，先预检，再恢复到新的隔离根。恢复保留原请求/回执/编号责任，不能使原根与新根同时控制设备。原机迁移和换机恢复分别核对账户、秘密作用域、驱动、设备身份、精确包、资格及外部分配编号范围；没有这些证据时保持隔离。

升级、修复与卸载
先停止原实例，在新介质中检查原责任和完整备份计划，再并排部署。程序切回不回滚已升级数据库，不能用旧EXE读新库。系统“已安装应用”可进入本产品维护，重建安装信息或保留数据卸载。占用文件若被登记为重启删除，按回执重启；中断后保留原数据和操作记录，从可信完整介质继续维护，勿手工清理共享驱动、机级租约或扩展消费者。

诊断与支持
选择时间和原作业，先预览再保存支持摘要。默认只有源码/介质/契约/config/DB/包身份、状态计数、健康时间和有界脱敏事件代码，不含密码、完整参数、条码明文、原报文、数据库或原件，也不会自动上传。启动失败可从独立维护导出轻量摘要。联系支持时提供摘要、失败步骤、软件SourceCommit和介质SHA256；生产附件另按获授权范围处理。

验收边界
Simulation只验证参考机制。真实SDK/合同/设备/实体许可、目标Windows令牌/ACL、DPI/触控/读屏及现场放行须各自有证据。界面可打开、软件构建通过、安装成功或局部测试通过都不能代替现场投产资格。DirectPlc完整作业UI仍延期；Sportmaster不控制PLC、不展示或留存图片。
""";
}
