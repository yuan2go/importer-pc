# DELIVERY-12 WPF映射与未验边界

设计实物仍是 [v1.1 README](ss-ui-vis-001-v1.1/README.md)、[GALLERY](ss-ui-vis-001-v1.1/GALLERY.md)、原HTML、分离CSS/JS、tokens、完整交接；原件不改。历史34张PNG来自HTML导出，本包实际WPF截图0。PAGE01～08原映射见 [10报告](../runtime/ui-baseline-alignment-10.md)，其历史记录不倒写。

|页面/边界|实际路径/契约|本包处理与待Windows验证|
|---|---|---|
|PAGE01启动/恢复、PAGE02身份|MainWindow.xaml / MainWindow.Extensions.cs，真实Management服务|业务NotReady/未登录仍可达核心管理；参数/生效仍由应用维护身份校验，安装由OS授权。无虚构进度或演示程序。登录/身份到期及UAC需实际验|
|PAGE03双通道事实/差异/固定动作|LaneControl、MainWindow.Presentation/Followup；原StationSnapshot/OperationGuards|继承双通道关键对象/包装需求供给数量/波次差异和固定主动作；维护抽屉扩至根区域，在抽屉头保留当前箱与阻断摘要、顶部健康不隐藏。实际可视区、长文本与动作滚动待验|
|PAGE04拆包、PAGE05关闭、新箱焦点|LaneControl SplitProgress；原两阶段关闭请求/Confirmation/FocusTicket|继承拆包责任与双通道关闭屏障；Unknown与关闭未决不可新箱，真正关闭后才有新箱输入。保留核心旅程原ID证据，扫码焦点/触控连续动作待验|
|PAGE06历史、PAGE07报警恢复|原HistoryPresentation/Closure/Recovery；应用受控查询/原请求Reconcile|读取不发业务请求；页面与异步身份投影失效规则沿用。抽屉/每窗独立草稿与选择不复制业务所有者。多窗连续操作待验|
|PAGE08维护 + 组件与部署新页|ExtensionsView.xaml/.cs、MainWindow.Extensions.cs；IExtensionManagement、原ConfigsAsync/SaveConfig/ActivateConfig|schema分组、嵌套键、说明/单位/默认/条件/集合/只读/凭据引用；实例/设备/角色、有效值及草稿对照、差异、CAS、原Revision与Actor冻结确认；等待回执而非本地假激活。完整WPF控件视觉/输入待验|
|首次Loaded前不足区、窗口间保护|Supply PresentationProtection + Desktop PresentationPolicy；每窗WindowArea来源标记|首次测量即可持久暂停，不依赖登录；不足区标记未移除时别窗不能恢复。扩大/关窗只移除本窗条件，需显式恢复人工暂停。共享策略及应用命令验证PASS，实际WPF时序NOT_RUN|
|分辨率/缩放/客户区|Windows06/W01矩阵、包内实际WPF验证脚本|不降低1280×680业务保护阈值，不缩小关键字号、不缩减原矩阵。保护拒绝只算阻断，不能记正常适配PASS。1366及不同缩放实际可用性仍OPEN|

源码实现及核心策略测试不能替代Windows截图/行为。实际UI安装、参数编辑、生效、失败升级、安全退回、停用和卸载以及持续业务旅程需同版11f0728候选执行，见 [12报告](../runtime/extension-windows-delivery-12.md)。
