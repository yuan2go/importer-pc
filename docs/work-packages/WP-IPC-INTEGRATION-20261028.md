# INTEGRATION-20261028 · 并行任务收口

起点 main `629364832e6193628e3d9b74d7d80f8e4b2e86e3`；远端备份标签 `pre-integration-20261028`。独立 D 盘 worktree，分支 `integration/20261028`。原 58 个 worktree 的状态、11 个脏工作区的暂存/未暂存补丁、未跟踪文件、冲突工作副本和 index 已备份至仓库外 `D:/integration-backups/20261028`；原目录不改、不清理。受限附件仅本地保管。

本包记录本次实际执行；旧测试、冻结介质、原回执不转记新 main。当前阶段为任务建模和先行文档清理，运行验证尚未执行。执行及最终状态见[本轮报告](../runtime/integration-20261028.md)。

## 范围与顺序

覆盖 2026-10-22 起 main 第一父历史、全部开放 PR、相关本地分支及未提交产物，并定点追溯快照 PR50/71。继承 ST01–ST14、LIA01–07、F01–03 的风险台账。F1–F5 原提示词编号与全文未能从仓库证实完整映射，不能猜造；已有任务按实际规格及 ST 编号核对。

| 顺序 | 任务 | 基线/原因 |
|---|---|---|
| 0 | 本轮文档清理 | 先撤销 INDEX 过期的 PR108 待合并状态和旧本轮入口；历史证据不改 |
| 1 | PR133，370ccb00 | 基线 d506f77c，早于 PR138；SDK ABI诊断及打包/编码边界先行 |
| 2 | PR139，e011da26 | 基线62936483；普通令牌、安装登记及原操作恢复先行，明确继承PR137的共用启动器 |
| 3 | PR137，76dd6078及未提交补修 | 基线d506f77c；在PR139后合并，保留安装扩展和PR138卸载按钮，修复实际失败的UI/目录回归 |
| 单列 | PR134，36a98016 | 同基线；触发实体/内置无解码默认与PR137连续逐次上报冲突，不能整块选边；保留开放，P2池预算兼容问题仍需处理 |
| 单列 | PR135，07b352f6 | 同基线；TCP生产者默认与后续SDK-only用户流程相冲突；不改已定义线上合同猜测兼容，保留开放 |
| 单列 | PR136，4608f71c | 依赖PR135；服务租约归属/心跳合同未裁决且VM SDK未取得，仍为未完草稿，保留开放 |
| 最后 | 干净最终main | 完整构建、关键套件、全台账检查；不生成发布介质、不宣称验收 |

顺序依语义依赖、共用启动符号、相机方案取代关系和风险确定。2026-10-28已读原任务中用户对连续采集、每次识别上报、删除触发/读码处理/配置状态选项的明确纠偏；旧同码不等于同实体、原请求/Unknown责任仍保留。不能以此批准TCP/触发协议变更。当前产品版本固定1.0.0，外部SDK/协议与数据格式保持原编号；已合并DB10/当前格式属于保护线，不回退DB9，不改冻结字节。

## 意图台账

下表是验证基准，不能将实现存在等同本轮PASS。

| 任务 | 预期行为与不变式 | 文件/符号 | 判定方法 |
|---|---|---|---|
| ST01 / PR109/110 | 独立延后、禁用读码器不强求心跳编号；无活动读码器不发空心跳；待配置拒绝业务 | ParcelCapabilityFactory、RcsConfigurationValidation、ParcelConnections | deferred-connections、p0-deferred、正式provider兼容验证 |
| ST02 | 已核验DB归档的冗余原件暂时占用仅退避；缺失/损坏/矛盾仍停供 | ParcelArchiveCleanup、ParcelHistory、ParcelIntegrationIds | p0-cleanup，锁定/解锁和三类损坏 |
| ST03 | 在线/计划备份完整取消，I/O不无界占gate；失败不写COMPLETED，Stop保有真实所有权 | ParcelCommands、ParcelRepository、ParcelScheduledBackup | latency-storage-checks、生命周期备份状态 |
| ST04 | 大详情仅可见且修订变化时后台生成；完整原文受管理权限与选择代次保护 | ParcelDetailsProjection、ParcelTargets | latency-details-checks、原生选择/撤权/取消 |
| ST05 | 历史count在gate外按修订缓存，多窗共享同次快照；不丢LIA字段 | ParcelSnapshotCache、SharedSnapshotRead、ParcelHistory、ParcelCommands | latency-projection-checks、快照字段断言 |
| ST06 | TCP keepalive/单调接收活性可观测，原Epoch/资源隔离；不重放 | TcpLineObservationSource、ParcelConnections | p0-tcp、p0-tcp-components；自动重连合同另列 |
| ST07 / G01 | Unknown保留原请求、目标、报文及责任；交接无外呼；无假Query/清责 | ParcelStages、ParcelGuards、RequestHandover | p0-rcs、p0-unknown-restart、LIA交接；真实处置需合同 |
| ST08 | 只重试确认未COMMIT且已回滚的本地输入事务；结果不明不重做；永久故障首次阻止拉起 | AtomicStorageWrite、RuntimeFatalMonitor、DesktopRecoverySupervisor | time-storage-atomic/supervisor/process-checks |
| ST09 | 原生SDK未结束时不释放handle/委托/租约，不虚称取消；是否隔离由证据决定 | HikrobotReader及Sdk生命周期 | 阻塞SDK/资源既有夹具；真实SDK挂起/隔离为独立决定 |
| ST10 | 两级预警先于硬阻断，近期增长估算可解释，不删未决 | ParcelStorageCapacity | latency-capacity-checks、配置默认/公式静态核对 |
| ST11 | PASSIVE WAL结果、高水位可观测；同实例仅一导出 | ParcelHistoryQuery、ParcelStorageCapacity | latency-storage-checks、读事务与导出所有权 |
| ST12 | 容量/保留间隔单调；UTC仅审计/日历；合并不改回墙钟 | ParcelStorageCapacity、ParcelHistory、RuntimeJournal | time-storage-fault-checks、回拨/前跳 |
| ST13 | 新回包本地回拨证明绑定原请求/hash；实时与恢复同校验；历史原字节不变 | ParcelClockEvidence、ParcelStages、ParcelReplyIdentities | time-storage-fault-checks，无证明/矛盾继续拒绝 |
| ST14 | 致命退出、有限拉起、健康预算、循环保护；恢复先暂停，不自动供包/重放 | DesktopRecoverySupervisor、CrashDiagnostics、BusinessLaunch | p0-supervisor、time-storage-process-checks、0/2次预算 |
| 快捷方式 / PR113 | Unicode中文创建/读取/清理；错目标/参数/目录/空目标保原件；COM释放一次 | DeploymentShortcuts、ShellLinkFile | 三ACP同DLL、installer-change-guards、生命周期44断言 |
| LIA01 | 部署/卸载原操作核对，计划不换身份，未知不重投，保留系统/文件真实结果 | ParcelStartupServices、DeploymentBatch、DeploymentRemoval | lifecycle-ux、批量计划/Recovery漂移 |
| LIA02 | 完整候选Pending→受控停止→生效；权限到期/撤销立即有效；多窗退出守卫 | StartupWorkflows、ManagementMode、ConfigurationSubmission | lifecycle-ux、原生权限/草稿/取消 |
| LIA03 | 固定查询上界、前后页与对象ID，改条件不能沿旧查询导出 | HistoryEditing、ParcelTargets | 原生history-context、固定范围/选择 |
| LIA04 | 当前说明对应代码；历史证据保持身份；随包映射无死链 | INDEX、release_documents | 本地链接、canonical manifest、文档映射测试 |
| LIA05 | 既有详情中交接原请求；按权限最少必要信息，无外呼/重发 | ParcelTargets、RequestHandover | 同码不同实体、Unknown/迟到、原文权限 |
| LIA06 | 管理模式/独立操作员、真实日志过滤/上下文/导出；帮助支持保持删除 | SoftwareLogPage、宿主XAML、ManagementModePresentation | 原生lifecycle-ux/reliability-logs、静态无旧入口 |
| LIA07 | 清理预检、完整备份核验；最近成功不被失败抹掉；到限不删旧集 | ParcelScheduledBackup、ParcelStartupRetention、RecoveryFiles | 备份取消/损坏/额外文件、持续授权 |
| PR90/91/94 | 四类参数共模、逐页校验、适用驱动字段与首配回退不丢；相机页面服从10/28纠偏 | ParameterSettingsControl、FirstConfiguration | 参数/首配关键套件 |
| PR92 | 过期输入不借旧格口，持久输入和命令守卫完整 | ParcelRuntime/Guards、作业投影 | architecture-quality-remediation |
| PR93 | 首帧和启动竞争、单侧故障/停止取消保持所有权 | ObservationSession、双相机装配 | dual-camera-communication |
| PR95/96/97/98 | 有效格口判断链、跨页回执、Noto字体与查询状态保留 | Sportmaster.Desktop、Presentation.Wpf | task-hierarchy-chain、原生日志与字体资源hash |
| PR99/100 | 输入/永久身份（当前DB10基线）、诊断分级、双机隔离与到期维护 | ParcelIngress、Retention、RuntimeJournal | win11-stability、当前DB10门禁/责任回归；旧SQL原字节只按历史提交核对 |
| PR101/102/103/105/106 | 输入rowid线性分页、未决保护、完整预算；原大规模失败/成功各守身份 | ParcelHistoryRetention、RetentionIngress、维护脚本 | retention-ingress、查询计划/对账、冻结证据hash |
| PR104 | 取消/健康/Dispose竞争、日志读及单调退避、进程强杀责任 | ObservationSession、SoftwareLogReader、RecoveryFiles | reliability-02、h02-crashes |
| PR107 | 仓/产线、统一台号、7天默认、窗口偏好、quantity=1一次原码请求 | 配置Schema、RCS、DesktopLaunch | parameter-guidance-startup、sportmaster-rcs |
| 本地 F01～03 | 当前种子/旧格式拒绝守卫、可达预检详情、历史原件完整性不弱化 | ParcelStartupServices、StartupSetupWindow、ParcelIntegrationIds | StabilityAuditFixes、StabilitySeedUpgrade及原件反例 |


| PR116 | 超时后新扫码可独立申请；旧Unknown和固定请求不重发；错误信息可追踪 | SportmasterRcs、ParcelStages、ParcelTargets | rcs-timeout-errors、sportmaster-rcs |
| PR117/138/139 | 安装/修复/更新/卸载保持原操作、CAS、备份、未决责任；普通启动不放松身份守卫 | DeploymentInstaller、OrdinaryRuntimeProcess、InstallerWindow、DeploymentRemoval | installer-change-guards、installed-lifecycle-windows、ordinary-runtime-startup、原生installer-lifecycle |
| PR118/129/132 | 三组作业状态、管理模式、热调试/日志暂停、查询与单位置准入保持 | Sportmaster.Desktop、SoftwareLogPage、ParcelGuards | station-single-camera、management-mode、task-hierarchy-chain、原生查询/日志 |
| PR119–125 | 当前精确合同/格式、完整候选和保全拒绝；Supply与Application默认构建入口有效 | CurrentContract、Database、Configuration、Solution | current-contract、fresh-baseline、默认入口及完整构建；不修改DB结构 |
| PR126/128/130/131/133 | 内置MVS、精确ABI、每相机统一编码、物理身份、原生所有权与日志隐私 | MvsAcquisition、MvsNative、BundledMvs、HikrobotReader | Probe、hikrobot-hardening、hikrobot-sdk-editing、test_mvs_runtime |
| PR127 | 自有版本固定1.0.0，SourceCommit区分构建 | 项目与compositions/manifest | test_application_compatibility、版本静态核对 |
| PR137 | SDK连续采集/读回、每次识别独立请求、空白无作业、无客户端同码去重；空相机不连接、部分输入拒绝 | MvsSoftwareReader、ParcelCapabilityFactory、CapabilityEditing、共享参数页 | mvs-software-flow、camera-config-startup、原生startup/parameter-v3，旧TCP转换保留共用编码 |
| PR134/135/136 | 原独立任务的解码保有/触发、连续TCP、服务就绪与资源互斥 | Decoding、BarcodeProducer、ReaderService | 仅核对分支和原证据；未合并不记main成立，冲突及缺SDK阻塞 |

## 检测与验证规则

净补丁反向检查、patch-id/git cherry仅为文本线索；后续修改须结合意图判断。追踪陈旧基线删除行的父树blame，检查双亲差异和大段替换。对发现的缺陷记录真实失败，再修根因和同类遗漏，提交独立修复；不删断言、跳过必要检查或以旧PASS代替新执行。每次合并核对已合任务意图，最终只在干净main执行完整构建与关键套件。

不强推、不重写历史、不删除分支/worktree/stash。业务边界、冻结报文、DB结构和冻结历史冲突跳过该项；保护/检查无法满足时保持开放。需要破坏性操作或无法安全剔除身份/凭据时立即停止。代码回滚通过基于最新main的新revert PR，不把main重置到备份标签。
