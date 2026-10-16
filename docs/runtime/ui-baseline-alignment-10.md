> 后续复核整改：[REVIEW-FOLLOWUP-11](../work-packages/WP-IPC-REVIEW-FOLLOWUP-11.md)。PR9复核发现波次差异常显、箱码焦点交接两项P2；Product组合层仍活来源的保留分支需补动态证据。下文a080825及原状态保留形成时含义，不能当作本次已验结论。

# UI基线落实10：实际WPF组件与逐页映射

日期：2026-10-16（Asia/Shanghai）。执行包：WP-IPC-AUDIT-RECONCILIATION-01 / WP-IPC-UI-BASELINE-ALIGNMENT-10，起始main `856e4de73f09a1c4f12af798c4d21e8631a6a86f`。这是新F-02参考产品的桌面增量，沿用单个ProductRuntime / Supply应用所有者、SQLite3和SIMULATION-v1；不是旧现场工程迁移，也不修改D批准、原RV或历史验收。

本次用户已授权源码实施及必要合成验证、构建与发布；旧“不测试”与NOT_RUN原记录保存在纠偏前快照。本记录的源码映射不能代替Windows行为结果。最终源码、同版包和运行验证身份由[参考验证09](reference-validation-09.md)统一记录。

## 输入实物与来源

已读取SS-UI-VIS-001 v1.1目录的README、GALLERY、完整UIUX交接、原单文件HTML、分离HTML/CSS/JS和design/tokens.json/tokens.css。原HTML嵌入CSS和JS与分离源码去除边缘换行后逐字一致；原文件保持原字节。被动查看`01-normal-1920.png`、`26-split-1366.png`、`12-record-detail-1920.png`、`13-devices-1920.png`，核对专业作业层级、紧凑拆包、低频抽屉和详情。

这些PNG仍是原样HTML的同源Chromium导出，**不是此次WPF或Windows截图**。没有重导出图册、引入字体文件或复制SS_DEMO的随机业务、模拟登录成功、计时接受/复位、一键成功逻辑。

## 已落实的组件与页面

|页面与原编号|实际源码 / 控件|应用事实与命令|本次实现与边界|
|---|---|---|---|
|PAGE-01 启动/恢复；REQ-001、038～040、044～049|`MainWindow.xaml` EntryOverlay、StartupChecks；`MainWindow.Presentation.cs` UpdateEntryProjection|Runtime.Configuration/Diagnostics/RuntimeId、当前Session、原Requests、Quarantined、设备和WCS健康|分项展示真实配置、运行所有者、箱/未决及健康；没有假进度。未知/恢复隔离先进入恢复说明和原请求抽屉；不自动接受、不换ID重发。Host装配前致命启动错误仍用原NotReady错误窗。|
|PAGE-02 登录；REQ-003、004、052|IdentityEntry、OpenIdentityClick；SendAsync Login/Logout|Supply拥有Actor/Role/ExpiresAt与命令守卫|独立且醒目标识“SIMULATION 模拟身份入口”，选择固定操作员/维护员，不收集真实密码，不伪称WCS认证。Production不显示角色模拟入口；无真实能力继续NotReady。退出/到期使业务与历史访问失效，不清除原作业责任。|
|PAGE-03 主工作台；REQ-005～016、022、025～027、031～036|SharedContext、StatusBanner、LaneGrid、LaneControl、LaneViewModel|StationSnapshot、State.Session、OperationGuards、AcceptedQuantity、当前Operation/Task/Input|共享箱/波次、独立CAM0/1状态、条码、SKU、任务、格口、Item波次与包装/需求/供给三级数量同时组织。正常尺寸不再单选通道替代另一侧事实；接受、拒绝、Unknown、发送阶段与实体占位分别显示。最近作业仅为当前快照摘要，点击详情走受控应用查询。|
|PAGE-04 拆包；REQ-017～022、036|LaneControl内SplitProgress、ProgressBar、保留件输入与固定主动作|当前Supply/Package、SplitScans、Revision，ScanRetained/ClearSplit/Submit|显示有效保留件/目标、应移除量与当前修订。扫描独立身份，由Supply判断；仅Simulation的KEEP策略，真实D-04未批准。计数未齐不可确认；清空对象/计数/后果需确认；拆包提交需要核对实际保留/移除并勾选。另一通道不消失。|
|PAGE-05 结束供包；REQ-023、024、040|CloseButton、CloseDecisionButton、Confirmation.cs；ConfirmCloseClick|CheckClose建立双通道关闭屏障；原CloseCheckRequest/CloseRemaining及全站ExpectedRevision|先调用实际两阶段应用契约检查；明确0缺失时以冻结检查ID/revision普通确认关闭，不弹少货风险框。大于0显示总量与“分SKU明细未提供”，检查事实后勾选强制结束；取消提交CancelClose恢复原箱。null绝不当0。Closing/CloseUnknown保留原箱并禁新箱；远端明确关闭才显示新箱输入。|
|PAGE-06 历史/详情；REQ-028、035、037～040、046|HistoryPage、HistoryPresentation.cs、MainWindow.Closure.cs、MainWindow.HistoryExport.cs|QueryHistoryAsync、SessionDetailsAsync、DetailsAsync；无发送副作用|右侧抽屉及可返回详情，箱会话→全部作业→关闭链可导航；精确箱/Item/SKU/任务/状态/通道及带时区日期过滤，分页使用应用返回的原上界。作业、原请求、Attempt、Response/Audit分层，完整ID/原payload可选择复制。查询、空、失败及失效旧快照明确区分；导出仅当前已查询筛选，保留父记录上界，身份变化/数量变化/重复分页不发布不完整文件，子事实为读取期间事实而非不可变DB快照。|
|PAGE-07 报警/诊断；REQ-006、026、039、042、046|RecoveryPage及AlarmHistoryFeedback、RecoveryGrid、ObservationReviews|当前Snapshot.Alarms/Recovery；AlarmHistoryAsync、DetailsAsync；AckAlarm、Reconcile、精确来源核对|活动与已恢复历史分别读取。已读不解除阻断，来源/作用域/发生/恢复时间保留。原请求Unknown只查询/核对，不标记成功或重发；来源核对冻结Code/Scope/Source/Epoch/EvidenceVersion，不解除其它来源或旧预留。自由文本不伪造接受证据。|
|PAGE-08 设备/维护；REQ-044、048、049、051、052|DiagnosticsPage / MaintenancePage同一抽屉的“设备与连接 / 维护配置”入口|实际Runtime身份、当前Devices与来源代次、ConfigsAsync，SaveConfig/ActivateConfig、Backup/Export|只读设备与WCS诊断，型号/SDK/实体反馈不足明确未提供；不建设相机预览/图片留存。继承现有实际配置表单、差异、验证、待重启激活与失败保旧；旧请求合同/目标不重写。维护权限由应用层检查。高级JSON折叠，工程注入单独SIMULATION抽屉。|

`MainWindow.xaml`负责工作台骨架和低频视图容器；`LaneControl`负责一个通道的结果/独立字段/数量/拆包/固定动作；`MainWindow.Presentation.cs`只拥有窗口导航、显示区域、焦点与阅读访问状态；`Themes/Workstation.xaml`集中v1.1颜色、字体、按钮与只读字段；现有Closure负责历史/精确来源投影。没有新业务状态机、第二套演示应用或新设备连接。

## 交互与安全继承

- 普通手动供包不再每次模态确认：本通道按钮直接携带点击时owner和全站ExpectedRevision。对话框前冻结的owner/revision在风险确认后不重读替换；期间新观察、回执或另一窗口改版由Supply CAS拒绝，要求重新核对。
- 当前Read重新取得或非法/多码后，任务和数量继续以本次应用投影为准；主屏显示当前候选观察，不拿上次证实结果授权本次输入。上次证实结果只在详情明确标识为历史。
- 低频视图用同一右侧抽屉；主导航仅作业记录、报警、设备与设置及身份/暂停，不把六个工程Tab暴露为业务主导航。设备和维护是抽屉内部切换。全局箱、健康和故障提示在抽屉之外持续可见；背景业务控件禁用，后台不停止。
- 抽屉独立Tab循环域，打开焦点移到返回按钮，Esc只返回视图并恢复触发焦点；不能撤销远端关闭。风险对话框取消在左、具体动作在右，危险操作无全局Enter默认；少货/拆包检查有明确勾选。
- 每窗保留箱码/保留件/工程输入、筛选、选区、滚动与详情代次；窗口刷新不重建控件或抢扫码焦点。扫码Enter/Tab只在所属输入框处理，不成为全局读码/发车快捷键。相机事件仍按来源路由。
- 主窗继承单提示消费者，副窗不创建第二个声音消费任务。接受、拒绝、拆包提示仍来自应用序号队列，未通过截图或试听替代Windows声学听审。
- 未登录/登出/过期/更换身份使历史、详情、报警历史和配置修订失效；旧列表/详情保留但禁用并明确标“旧阅读快照”。请求前检查当前身份，完成后核对原身份和读取代次，迟到旧身份结果不覆盖新阅读。真正权限仍在Supply所有新旧接口内。

## 尺寸、视觉和固定动作

v1.1的背景`#F1F4F7`、文字`#192736/#536577`、主色`#205FCE`、接受`#147450`、未知`#895509`、拒绝`#B52F38`及状态背景、边框、圆角和字体落入共享资源。Windows中文系统字体与Consolas回退，不分发字体。按钮默认、悬停、按下、键盘焦点、禁用、执行中、接受和拒绝均有对应可视表现；成功/失败由真实通道状态承载，不由按钮点击生成。

工作台按实际WPF ClientArea判断1280×680 DIP最小区；标准/自动紧凑仍保留并排双通道。紧凑将数量提前到任务元数据之前，缩小结果/条码/数量至23/23/26，普通目标≥44、主动作48，标准主动作52；不做整页Viewbox缩字。每通道动作始终固定在本卡底部；主动作旁的详情/处置入口在标准和紧凑均保留。必核条码、SKU/任务/格口、包装/需求/供给及单位、拆包有效数/目标/移除量组成独立核对区域；来源、Item波次、语义证据和上次证实结果进入辅助展开。字段区允许局部滚动，ReviewFits逐项将必核控件转换到实际ScrollContentPresenter视口，并检查完整矩形位于视口内，同时检查固定动作位于通道可见区域；辅助内容溢出本身不禁操作。必核字段被滚动裁切或长内容确实不完整时继续拒绝提交/拆包输入，不能因有按钮就绕过核对。

客户区不足时显示保护视图并阻断本窗新动作，提交应用层Pause以阻断新入场；状态竞争保留失败反馈并由后续快照再核对。恢复窗口空间不会清除保护暂停，必须人工Resume；该动作只解除人工暂停，Unknown/实体/来源/存储等仍由应用守卫保留。软件暂停不是安全急停。

1280×680是候选参考最小有效客户区，**尚未取得Windows实际布局或触控可用性证明**。极长任务/原因、拆包和多窗口可能需要更大有效区；安全守卫不会通过压小关键文字换取启用。没有批准保留宽屏单选、六Tab主导航或普通供包重复模态的差异。

## 验证身份与仍待执行

本子任务静态核对全部XAML XML语法，检查控件/处理器与数据来源，并复用原HTML内嵌/分离源码一致性核对。首次统一构建发现Dispatcher调度CS4014（WarningsAsErrors），已修成显式`_ = Dispatcher.InvokeAsync(...)`；实际重建结果以09的统一日志为准。未声称本子任务运行过WPF、Windows截图或行为。

Windows06 W-01～06仍须对最终同版包执行：1920/1366各100/125/150%、实际客户区/不足与恢复、所有主要状态/长字段、跨屏DPI、双窗草稿/焦点/滚动、扫码终止符、风险勾选、三种提示声、故障、解压安装/维护/恢复。当前执行宿主为macOS，没有实际Windows客户端、输入设备或音箱现场；这些项NOT_RUN。源码与交叉构建通过也不能把它们改成PASS。

真实SDK/WCS、实体映射、数量/拆包规则、生产身份及现场资料按05 B-01～06逐项BLOCKED。实际缺失不妨碍此次通用UI/参考闭环实施，但不生成假字段、客户批准或真实动作成功。最终运行/包SHA/SourceCommit及可复现命令见09，D/RV继续按原证据登记。

详情中的草稿处置在开始读取前冻结全站revision，连同所读作业revision/完整事实核对；读完或点击时版本/事实变化则只读，不借新snapshot的revision给旧详情授权。处置资格依有效身份、实际当前草稿状态和应用命令守卫判断，与是否打开历史抽屉无关；确认后仍携带原冻结revision交给Supply CAS。后台或另一窗口更新后必须重新读取。

## 两轴复核后修正（同一任务）

复核发现两项真实工程问题：原ReviewFits把含低频字段的整个ScrollViewer“无可滚动内容”作为前提，紧凑局部滚动可能持续禁用；原ApplyDensity折叠全部次动作且历史抽屉使CanCancel为false，导致紧凑的当前草稿缺详情/处置路径。已分别改为上面的必核控件实际可见矩形守卫、辅助展开和始终可达的固定详情/处置动作，取消资格不再依赖工作台视图available。继承并收紧fc5e24e的旧详情权限冻结，未将应用CAS改成新revision重试。配置Superseded状态显示“已被新修订替代”。

本段是源码整改与静态XML检查记录，实际Windows几何、焦点、连续扫码、声音和双窗仍NOT_RUN；后续统一构建/复核结果绑定09最终源提交，不把静态尺寸推算写成运行PASS。
